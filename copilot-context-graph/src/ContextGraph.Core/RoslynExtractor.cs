using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ContextGraph.Core;

/// <summary>Extracts nodes/edges from one C# document using the semantic model.
/// Only symbols declared in source become nodes; BCL/NuGet references are dropped,
/// which kills builtin noise at the source instead of post-hoc blocklists.</summary>
public sealed class RoslynExtractor
{
    private readonly string _rootDir;

    public RoslynExtractor(string rootDir) => _rootDir = rootDir;

    public async Task<Extraction> ExtractAsync(Document document, CancellationToken ct = default)
    {
        var extraction = new Extraction();
        var tree = await document.GetSyntaxTreeAsync(ct);
        var model = await document.GetSemanticModelAsync(ct);
        if (tree is null || model is null || document.FilePath is null)
            return extraction;
        // Generated files (AssemblyInfo etc.) live under obj/ — not part of the picture.
        if (document.FilePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            return extraction;

        var relPath = Path.GetRelativePath(_rootDir, document.FilePath).Replace('\\', '/');
        var fileNode = new Node { Id = relPath, Label = Path.GetFileName(relPath), Type = "file", SourceFile = relPath };
        extraction.Nodes.Add(fileNode);

        var walker = new Walker(model, extraction, relPath, fileNode.Id);
        walker.Visit(await tree.GetRootAsync(ct));
        return extraction;
    }

    public static string SymbolId(ISymbol symbol)
    {
        // FullyQualifiedFormat omits containing types for members, so qualify methods
        // manually. Overloads collapse onto one node (no parameter list in the id).
        if (symbol is IMethodSymbol method)
        {
            var name = method.MethodKind == MethodKind.Constructor ? "ctor" : method.Name;
            return SymbolId(method.ContainingType) + "." + name;
        }
        var display = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return display.StartsWith("global::") ? display["global::".Length..] : display;
    }

    private sealed class Walker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _model;
        private readonly Extraction _extraction;
        private readonly string _relPath;
        private readonly string _fileId;
        private readonly HashSet<string> _declaredNamespaces = [];
        // Innermost enclosing declared node (file → type → method) — the edge source for calls/references.
        private readonly Stack<string> _scope = new();

        public Walker(SemanticModel model, Extraction extraction, string relPath, string fileId)
        {
            _model = model;
            _extraction = extraction;
            _relPath = relPath;
            _fileId = fileId;
            _scope.Push(fileId);
        }

        // --- types ---

        public override void VisitClassDeclaration(ClassDeclarationSyntax node) => VisitType(node, "class", () => base.VisitClassDeclaration(node));
        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => VisitType(node, "interface", () => base.VisitInterfaceDeclaration(node));
        public override void VisitStructDeclaration(StructDeclarationSyntax node) => VisitType(node, "struct", () => base.VisitStructDeclaration(node));
        public override void VisitRecordDeclaration(RecordDeclarationSyntax node) => VisitType(node, "record", () => base.VisitRecordDeclaration(node));
        public override void VisitEnumDeclaration(EnumDeclarationSyntax node) => VisitType(node, "enum", () => base.VisitEnumDeclaration(node));

        private void VisitType(BaseTypeDeclarationSyntax node, string kind, Action visitChildren)
        {
            if (_model.GetDeclaredSymbol(node) is not INamedTypeSymbol symbol)
                return;
            var id = SymbolId(symbol);

            var metadata = new Dictionary<string, string> { ["namespace"] = symbol.ContainingNamespace.ToDisplayString() };
            // SignalR hub (base-list name check — the Hub base class is framework code).
            if (node is TypeDeclarationSyntax { BaseList: { } bases } &&
                bases.Types.Any(b => b.Type.ToString() is var s && (s == "Hub" || s.StartsWith("Hub<"))))
                metadata["hub"] = "true";

            AddNode(id, symbol.Name, kind, Line(node), metadata);
            AddNamespace(symbol.ContainingNamespace, id);
            AddEdge(_fileId, id, Relation.Contains, Confidence.Extracted, Line(node));

            if (node.BaseList is not null)
            {
                foreach (var baseType in node.BaseList.Types)
                {
                    var baseSymbol = _model.GetTypeInfo(baseType.Type).Type;

                    // MassTransit consumer: class X : IConsumer<TMessage> → X consumes TMessage.
                    if (baseSymbol is INamedTypeSymbol { Name: "IConsumer", TypeArguments.Length: 1 } consumer &&
                        consumer.ContainingNamespace.ToDisplayString() == "MassTransit" &&
                        Resolve(consumer.TypeArguments[0]) is { } message)
                    {
                        AddNode(message.Id, message.Symbol.Name, TypeKindName(message.Symbol), null, null);
                        AddEdge(id, message.Id, Relation.Consumes, Confidence.Extracted, Line(baseType));
                    }

                    if (Resolve(baseSymbol) is not { } target)
                        continue;
                    var relation = target.Symbol.TypeKind == TypeKind.Interface ? Relation.Implements : Relation.Inherits;
                    AddNode(target.Id, target.Symbol.Name, TypeKindName(target.Symbol), null, null);
                    AddEdge(id, target.Id, relation, Confidence.Extracted, Line(baseType));
                }
            }

            var savedPrefix = _routePrefix;
            var savedController = _controllerName;
            _routePrefix = AttributeStringArg(node.AttributeLists, "Route");
            _controllerName = symbol.Name.EndsWith("Controller") ? symbol.Name[..^"Controller".Length] : symbol.Name;

            _scope.Push(id);
            visitChildren();
            _scope.Pop();

            _routePrefix = savedPrefix;
            _controllerName = savedController;
        }

        // --- members ---

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (_model.GetDeclaredSymbol(node) is not IMethodSymbol symbol)
                return;
            var id = SymbolId(symbol);
            AddNode(id, symbol.Name + "()", "method", Line(node), JsInvokableMetadata(node, symbol.Name));
            AddEdge(_scope.Peek(), id, Relation.Contains, Confidence.Extracted, Line(node));
            AddHttpRoute(id, node);
            AddTypeReference(id, symbol.ReturnType, "return_type", Line(node));
            foreach (var parameter in symbol.Parameters)
                AddTypeReference(id, parameter.Type, "parameter_type", Line(node));

            _scope.Push(id);
            base.VisitMethodDeclaration(node);
            _scope.Pop();
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            if (_model.GetDeclaredSymbol(node) is not IMethodSymbol symbol)
                return;
            var id = SymbolId(symbol);
            AddNode(id, symbol.ContainingType.Name + "()", "method", Line(node), null);
            AddEdge(_scope.Peek(), id, Relation.Contains, Confidence.Extracted, Line(node));
            foreach (var parameter in symbol.Parameters)
                AddTypeReference(id, parameter.Type, "parameter_type", Line(node));

            _scope.Push(id);
            base.VisitConstructorDeclaration(node);
            _scope.Pop();
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (_model.GetTypeInfo(node.Declaration.Type).Type is { } type)
                AddTypeReference(_scope.Peek(), type, "field", Line(node));
            base.VisitFieldDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (_model.GetTypeInfo(node.Type).Type is { } type)
                AddTypeReference(_scope.Peek(), type, "field", Line(node));
            base.VisitPropertyDeclaration(node);
        }

        public override void VisitAttribute(AttributeSyntax node)
        {
            if (Resolve(_model.GetTypeInfo(node).Type) is { } target)
            {
                AddNode(target.Id, target.Symbol.Name, TypeKindName(target.Symbol), null, null);
                AddEdge(_scope.Peek(), target.Id, Relation.References, Confidence.Extracted, Line(node), context: "attribute");
            }
            base.VisitAttribute(node);
        }

        // --- calls ---

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (!TryStringContractCall(node) && !TryFrameworkCall(node))
                AddCall(_model.GetSymbolInfo(node), node);
            base.VisitInvocationExpression(node);
        }

        private static readonly HashSet<string> HttpClientMethods =
        [
            "GetAsync", "PostAsync", "PutAsync", "DeleteAsync", "PatchAsync", "GetStringAsync",
            "GetFromJsonAsync", "PostAsJsonAsync", "PutAsJsonAsync", "DeleteFromJsonAsync",
        ];

        /// <summary>Calls whose first string-literal argument is a cross-boundary contract:
        /// JS interop, SignalR hub methods and events, HTTP client requests. Receiver type
        /// names disambiguate (HubConnection vs IJSRuntime vs IClientProxy).</summary>
        private bool TryStringContractCall(InvocationExpressionSyntax node)
        {
            if (node.Expression is not MemberAccessExpressionSyntax member ||
                node.ArgumentList.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax { Token.Value: string literal })
                return false;
            var methodName = member.Name.Identifier.Text;
            var receiver = _model.GetTypeInfo(member.Expression).Type?.Name ?? "";

            switch (methodName)
            {
                // Client → hub: connection.InvokeAsync("HubMethod") / SendAsync("HubMethod").
                case "InvokeAsync" or "SendAsync" when receiver == "HubConnection":
                    // Placeholder; resolved against methods on Hub-derived types, else dropped.
                    AddEdge(_scope.Peek(), "hubmethod:" + literal, Relation.Calls, Confidence.Extracted, Line(node), context: "call");
                    return true;

                // C# → JS: JS.InvokeVoidAsync("fn", …).
                case "InvokeAsync" or "InvokeVoidAsync":
                    // No SourceFile: stub — the defining .js file supplies the location on merge.
                    _extraction.Nodes.Add(new Node { Id = "js:" + literal, Label = literal, FileType = "concept", Type = "js_function" });
                    AddEdge(_scope.Peek(), "js:" + literal, Relation.Calls, Confidence.Inferred, Line(node), context: "call");
                    return true;

                // Server → clients: Clients.All.SendAsync("Event", …).
                case "SendAsync":
                {
                    var confidence = receiver.StartsWith("IClientProxy") || receiver.StartsWith("IHub") || receiver.StartsWith("ISingleClient")
                        ? Confidence.Extracted : Confidence.Inferred;
                    _extraction.Nodes.Add(new Node { Id = "event:" + literal, Label = literal, FileType = "concept", Type = "signalr_event" });
                    AddEdge(_scope.Peek(), "event:" + literal, Relation.Publishes, confidence, Line(node));
                    return true;
                }

                // Client handler: connection.On("Event", handler).
                case "On" when receiver is "HubConnection" or "" && node.ArgumentList.Arguments.Count >= 2:
                    _extraction.Nodes.Add(new Node { Id = "event:" + literal, Label = literal, FileType = "concept", Type = "signalr_event" });
                    AddEdge(_scope.Peek(), "event:" + literal, Relation.Consumes,
                        receiver == "HubConnection" ? Confidence.Extracted : Confidence.Inferred, Line(node));
                    return true;

                // HTTP client: http.GetAsync("api/orders/…").
                case var _ when HttpClientMethods.Contains(methodName):
                    var routeId = "route:" + NormalizeRoute(literal);
                    _extraction.Nodes.Add(new Node { Id = routeId, Label = NormalizeRoute(literal), FileType = "concept", Type = "route" });
                    AddEdge(_scope.Peek(), routeId, Relation.Calls, Confidence.Inferred, Line(node), context: "http");
                    return true;

                // Config reads: config.GetSection("Features") / GetValue<T>("A:B") / GetConnectionString("Default").
                case "GetSection" or "GetValue" or "GetConnectionString":
                {
                    var key = methodName == "GetConnectionString" ? "ConnectionStrings:" + literal : literal;
                    var confidence = receiver.Contains("Configuration") ? Confidence.Extracted : Confidence.Inferred;
                    // No node declared: appsettings extraction supplies it, else the edge drops.
                    AddEdge(_scope.Peek(), "config:" + key, Relation.References, confidence, Line(node), context: "config");
                    return true;
                }

                // Dapper: conn.QueryAsync("SELECT … FROM Orders") — mine the SQL literal.
                case var _ when DapperMethods.Contains(methodName):
                    ScanSqlLiteral(literal, node);
                    return true;
            }
            return false;
        }

        private static readonly HashSet<string> DapperMethods =
        [
            "Query", "QueryAsync", "QueryFirst", "QueryFirstAsync", "QueryFirstOrDefault", "QueryFirstOrDefaultAsync",
            "QuerySingle", "QuerySingleAsync", "QuerySingleOrDefault", "QuerySingleOrDefaultAsync", "QueryMultiple",
            "QueryMultipleAsync", "Execute", "ExecuteAsync", "ExecuteScalar", "ExecuteScalarAsync",
        ];

        private static readonly Regex SqlTableRx = new(@"(?:FROM|JOIN|INTO|UPDATE)\s+([\w\.\[\]]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SqlExecRx = new(@"EXEC(?:UTE)?\s+([\w\.\[\]]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Edges to table:/proc: nodes declared by .sql migrations; unmatched names
        /// drop in GraphBuilder (schema not in this repo → honestly not claimed).</summary>
        private void ScanSqlLiteral(string sql, SyntaxNode node)
        {
            foreach (var table in SqlTableRx.Matches(sql).Select(m => m.Groups[1].Value).Distinct())
                AddEdge(_scope.Peek(), SqlExtractor.SqlObjectId("table", table), Relation.Uses, Confidence.Inferred, Line(node), context: "sql");
            foreach (var proc in SqlExecRx.Matches(sql).Select(m => m.Groups[1].Value).Distinct())
                AddEdge(_scope.Peek(), SqlExtractor.SqlObjectId("proc", proc), Relation.Calls, Confidence.Inferred, Line(node), context: "sql");
        }

        /// <summary>Config indexer reads: config["Features:EnableX"].</summary>
        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            if (node.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax { Token.Value: string key } &&
                (_model.GetTypeInfo(node.Expression).Type?.Name ?? "").Contains("Configuration"))
                AddEdge(_scope.Peek(), "config:" + key, Relation.References, Confidence.Extracted, Line(node), context: "config");
            base.VisitElementAccessExpression(node);
        }

        private static string NormalizeRoute(string path)
        {
            var trimmed = path.Split('?')[0].Trim('/');
            return "/" + trimmed.ToLowerInvariant();
        }

        private static readonly HashSet<string> DiMethods =
            ["AddScoped", "AddSingleton", "AddTransient", "TryAddScoped", "TryAddSingleton", "TryAddTransient"];

        /// <summary>DI registrations and MassTransit publishes — calls into framework methods
        /// (normally dropped as not-in-source) whose type arguments encode real architecture.</summary>
        private bool TryFrameworkCall(InvocationExpressionSyntax node)
        {
            if (_model.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
                return false;

            // services.AddScoped<IService, Implementation>() → IService bound_to Implementation.
            if (DiMethods.Contains(method.Name) && method.TypeArguments.Length == 2 &&
                Resolve(method.TypeArguments[0]) is { } service && Resolve(method.TypeArguments[1]) is { } implementation)
            {
                AddNode(service.Id, service.Symbol.Name, TypeKindName(service.Symbol), null, null);
                AddNode(implementation.Id, implementation.Symbol.Name, TypeKindName(implementation.Symbol), null, null);
                AddEdge(service.Id, implementation.Id, Relation.BoundTo, Confidence.Extracted, Line(node));
                return true;
            }

            // bus.Publish<TMessage>(…) / Publish(new TMessage()) → publisher publishes TMessage.
            if (method.Name is "Publish" or "Send" &&
                method.ContainingNamespace.ToDisplayString() == "MassTransit")
            {
                var messageType = method.TypeArguments.Length == 1
                    ? method.TypeArguments[0]
                    : node.ArgumentList.Arguments.Count > 0
                        ? _model.GetTypeInfo(node.ArgumentList.Arguments[0].Expression).Type
                        : null;
                if (Resolve(messageType) is { } message)
                {
                    AddNode(message.Id, message.Symbol.Name, TypeKindName(message.Symbol), null, null);
                    AddEdge(_scope.Peek(), message.Id, Relation.Publishes, Confidence.Extracted, Line(node));
                    return true;
                }
            }
            return false;
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            AddCall(_model.GetSymbolInfo(node), node);
            base.VisitObjectCreationExpression(node);
        }

        public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            AddCall(_model.GetSymbolInfo(node), node);
            base.VisitImplicitObjectCreationExpression(node);
        }

        private void AddCall(SymbolInfo info, SyntaxNode node)
        {
            var (symbol, confidence) = info.Symbol is not null
                ? (info.Symbol, Confidence.Extracted)
                : (info.CandidateSymbols.FirstOrDefault(), Confidence.Ambiguous);
            if (symbol is not IMethodSymbol method || !IsInSource(method))
                return;
            // Attribute the call to the original method definition, not a generic instantiation.
            method = method.OriginalDefinition;
            var targetId = SymbolId(method);
            var label = method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name + "()" : method.Name + "()";
            AddNode(targetId, label, "method", null, null);
            AddEdge(_scope.Peek(), targetId, Relation.Calls, confidence, Line(node), context: "call");
        }

        // --- imports ---

        public override void VisitUsingDirective(UsingDirectiveSyntax node)
        {
            if (node.Name is null)
                return;
            var metadata = new Dictionary<string, string>
            {
                ["using_kind"] = node.StaticKeyword != default ? "static" : node.Alias is not null ? "alias" : "namespace",
            };
            if (node.Alias is not null)
                metadata["alias"] = node.Alias.Name.ToString();
            // Target may be external; GraphBuilder drops edges to undeclared nodes.
            _extraction.Edges.Add(new Edge
            {
                Source = _fileId,
                Target = "ns:" + node.Name.ToString(),
                Relation = Relation.Imports,
                Context = "import",
                Confidence = Confidence.Extracted,
                SourceFile = _relPath,
                SourceLocation = Line(node),
            });
            base.VisitUsingDirective(node);
        }

        // --- helpers ---

        private void AddTypeReference(string fromId, ITypeSymbol type, string context, string? location)
        {
            foreach (var target in Flatten(type))
            {
                if (Resolve(target) is not { } resolved)
                    continue;
                AddNode(resolved.Id, resolved.Symbol.Name, TypeKindName(resolved.Symbol), null, null);
                var ctx = ReferenceEquals(target, type) ? context : "generic_arg";
                AddEdge(fromId, resolved.Id, Relation.References, Confidence.Extracted, location, context: ctx);
            }

            static IEnumerable<ITypeSymbol> Flatten(ITypeSymbol t)
            {
                yield return t;
                if (t is INamedTypeSymbol named)
                    foreach (var arg in named.TypeArguments)
                        foreach (var inner in Flatten(arg))
                            yield return inner;
            }
        }

        private (INamedTypeSymbol Symbol, string Id)? Resolve(ITypeSymbol? type)
        {
            if (type?.OriginalDefinition is not INamedTypeSymbol named || !IsInSource(named))
                return null;
            return (named, SymbolId(named));
        }

        private static bool IsInSource(ISymbol symbol) => symbol.Locations.Any(l => l.IsInSource);

        private void AddNamespace(INamespaceSymbol ns, string typeId)
        {
            if (ns.IsGlobalNamespace)
                return;
            var name = ns.ToDisplayString();
            var nsId = "ns:" + name;
            if (_declaredNamespaces.Add(nsId))
                _extraction.Nodes.Add(new Node { Id = nsId, Label = name, Type = "namespace", SourceFile = _relPath });
            AddEdge(nsId, typeId, Relation.Contains, Confidence.Extracted, null);
        }

        private void AddNode(string id, string label, string type, string? location, Dictionary<string, string>? metadata) =>
            _extraction.Nodes.Add(new Node
            {
                Id = id,
                Label = label,
                Type = type,
                SourceFile = location is null ? null : _relPath,
                SourceLocation = location,
                Metadata = metadata,
            });

        private void AddEdge(string source, string target, string relation, string confidence, string? location, string? context = null) =>
            _extraction.Edges.Add(new Edge
            {
                Source = source,
                Target = target,
                Relation = relation,
                Context = context,
                Confidence = confidence,
                SourceFile = _relPath,
                SourceLocation = location,
            });

        private string? _routePrefix;
        private string? _controllerName;

        private static readonly string[] HttpVerbAttributes =
            ["HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch", "HttpHead", "HttpOptions"];

        /// <summary>[HttpGet("{id}")] on a method (plus the type's [Route] prefix) → a
        /// route: node shared with client-side HTTP calls. Attribute names are checked
        /// syntactically — the attribute types are framework code.</summary>
        private void AddHttpRoute(string methodId, MethodDeclarationSyntax node)
        {
            foreach (var verb in HttpVerbAttributes)
            {
                if (!HasAttribute(node.AttributeLists, verb))
                    continue;
                var template = AttributeStringArg(node.AttributeLists, verb);
                var prefix = (_routePrefix ?? "").Replace("[controller]", _controllerName ?? "");
                var segments = new[] { prefix, template }.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!.Trim('/'));
                var path = NormalizeRoute(string.Join('/', segments));
                _extraction.Nodes.Add(new Node
                {
                    Id = "route:" + path,
                    Label = path,
                    FileType = "concept",
                    Type = "route",
                    SourceFile = _relPath,
                    SourceLocation = Line(node),
                    Metadata = new() { ["verb"] = verb["Http".Length..].ToUpperInvariant() },
                });
                AddEdge(methodId, "route:" + path, Relation.References, Confidence.Extracted, Line(node), context: "http_route");
                return; // one route per method is the norm; first verb wins
            }
        }

        private static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string name) =>
            lists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString() is var n && (n == name || n == name + "Attribute"));

        private static string? AttributeStringArg(SyntaxList<AttributeListSyntax> lists, string name)
        {
            var attribute = lists.SelectMany(l => l.Attributes)
                .FirstOrDefault(a => a.Name.ToString() is var n && (n == name || n == name + "Attribute"));
            return attribute?.ArgumentList?.Arguments.FirstOrDefault()?.Expression
                is LiteralExpressionSyntax { Token.Value: string value } ? value : null;
        }

        /// <summary>[JSInvokable] / [JSInvokable("Id")] — name-based (the attribute type is
        /// framework, so it never resolves in-source). The identifier lets JS-side
        /// DotNet.invokeMethodAsync calls resolve to this method.</summary>
        private static Dictionary<string, string>? JsInvokableMetadata(MethodDeclarationSyntax node, string methodName)
        {
            foreach (var attribute in node.AttributeLists.SelectMany(l => l.Attributes))
            {
                var name = attribute.Name.ToString();
                if (name is not ("JSInvokable" or "JSInvokableAttribute"))
                    continue;
                var identifier = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression
                    is LiteralExpressionSyntax { Token.Value: string custom } ? custom : methodName;
                return new() { ["js_invokable"] = identifier };
            }
            return null;
        }

        private string Line(SyntaxNode node) => "L" + (node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);

        private static string TypeKindName(INamedTypeSymbol symbol) => symbol.TypeKind switch
        {
            TypeKind.Interface => "interface",
            TypeKind.Struct => "struct",
            TypeKind.Enum => "enum",
            _ => symbol.IsRecord ? "record" : "class",
        };
    }
}
