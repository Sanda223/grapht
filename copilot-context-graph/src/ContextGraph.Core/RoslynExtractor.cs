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
            AddNode(id, symbol.Name, kind, Line(node), new() { ["namespace"] = symbol.ContainingNamespace.ToDisplayString() });
            AddNamespace(symbol.ContainingNamespace, id);
            AddEdge(_fileId, id, Relation.Contains, Confidence.Extracted, Line(node));

            if (node.BaseList is not null)
            {
                foreach (var baseType in node.BaseList.Types)
                {
                    if (Resolve(_model.GetTypeInfo(baseType.Type).Type) is not { } target)
                        continue;
                    var relation = target.Symbol.TypeKind == TypeKind.Interface ? Relation.Implements : Relation.Inherits;
                    AddNode(target.Id, target.Symbol.Name, TypeKindName(target.Symbol), null, null);
                    AddEdge(id, target.Id, relation, Confidence.Extracted, Line(baseType));
                }
            }

            _scope.Push(id);
            visitChildren();
            _scope.Pop();
        }

        // --- members ---

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (_model.GetDeclaredSymbol(node) is not IMethodSymbol symbol)
                return;
            var id = SymbolId(symbol);
            AddNode(id, symbol.Name + "()", "method", Line(node), null);
            AddEdge(_scope.Peek(), id, Relation.Contains, Confidence.Extracted, Line(node));
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
            // JS interop: JS.InvokeVoidAsync("fn", ...) — the C#→JavaScript boundary,
            // captured without parsing any JS.
            if (node.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "InvokeAsync" or "InvokeVoidAsync" } &&
                node.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax { Token.Value: string jsName })
            {
                var jsId = "js:" + jsName;
                _extraction.Nodes.Add(new Node { Id = jsId, Label = jsName, FileType = "concept", Type = "js_function", SourceFile = _relPath });
                AddEdge(_scope.Peek(), jsId, Relation.Calls, Confidence.Inferred, Line(node), context: "call");
            }
            else
            {
                AddCall(_model.GetSymbolInfo(node), node);
            }
            base.VisitInvocationExpression(node);
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
