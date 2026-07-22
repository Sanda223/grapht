using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Grapht.Core;

/// <summary>Extracts nodes/edges from .razor/.cshtml markup. No semantic model exists for
/// markup, so cross-file targets are emitted as placeholders ("byname:", "component:",
/// "member:") that RazorResolver rewires against the full node set before graph build.</summary>
public sealed partial class RazorExtractor
{
    [GeneratedRegex(@"^@page\s+""([^""]+)""", RegexOptions.Multiline)] private static partial Regex PageRx();
    [GeneratedRegex(@"^@using\s+([\w.]+)", RegexOptions.Multiline)] private static partial Regex UsingRx();
    [GeneratedRegex(@"^@inject\s+([\w.<>]+)\s+(\w+)", RegexOptions.Multiline)] private static partial Regex InjectRx();
    [GeneratedRegex(@"^@inherits\s+([\w.<>]+)", RegexOptions.Multiline)] private static partial Regex InheritsRx();
    [GeneratedRegex(@"<([A-Z][A-Za-z0-9_]*)")] private static partial Regex ComponentTagRx();

    private readonly string _rootDir;

    public RazorExtractor(string rootDir) => _rootDir = rootDir;

    public Extraction Extract(string filePath)
    {
        var extraction = new Extraction();
        var text = File.ReadAllText(filePath);
        var relPath = Path.GetRelativePath(_rootDir, filePath).Replace('\\', '/');
        var stem = Path.GetFileNameWithoutExtension(filePath);

        extraction.Nodes.Add(new Node { Id = relPath, Label = Path.GetFileName(relPath), Type = "component", SourceFile = relPath });

        var (markup, code, codeStartLine) = SplitCodeBlock(text);
        // markup is a prefix of text, so text line numbers are valid for markup offsets.
        var lines = new LineIndex(text);

        void Add(string target, string relation, string confidence, string? location, string? context = null) =>
            extraction.Edges.Add(new Edge
            {
                Source = relPath, Target = target, Relation = relation, Context = context,
                Confidence = confidence, SourceFile = relPath, SourceLocation = location,
            });

        foreach (Match m in PageRx().Matches(markup))
        {
            var routeId = "route:" + m.Groups[1].Value;
            extraction.Nodes.Add(new Node { Id = routeId, Label = m.Groups[1].Value, FileType = "concept", Type = "route", SourceFile = relPath });
            Add(routeId, Relation.References, Confidence.Extracted, lines.LabelAt(m.Index));
        }

        foreach (Match m in UsingRx().Matches(markup))
            Add("ns:" + m.Groups[1].Value, Relation.Imports, Confidence.Extracted, lines.LabelAt(m.Index), context: "import");

        var injectedTypeByName = new Dictionary<string, string>();
        foreach (Match m in InjectRx().Matches(markup))
        {
            var typeName = ShortName(m.Groups[1].Value);
            injectedTypeByName[m.Groups[2].Value] = typeName;
            Add("byname:" + typeName, Relation.Imports, Confidence.Extracted, lines.LabelAt(m.Index), context: "import");
        }

        foreach (Match m in InheritsRx().Matches(markup))
            Add("byname:" + ShortName(m.Groups[1].Value), Relation.Inherits, Confidence.Extracted, lines.LabelAt(m.Index));

        foreach (var tag in ComponentTagRx().Matches(markup).Select(m => m.Groups[1].Value).Distinct())
            if (tag != stem)
                Add("component:" + tag, Relation.Calls, Confidence.Extracted, null, context: "call");

        if (code.Length > 0)
            ExtractCodeBlock(extraction, relPath, code, codeStartLine, injectedTypeByName);
        return extraction;
    }

    /// <summary>Parses the @code block as a class body (syntax only) for methods, calls
    /// through @inject-ed services, and JS interop literals.</summary>
    private static void ExtractCodeBlock(Extraction extraction, string relPath, string code, int startLine, Dictionary<string, string> injected)
    {
        var tree = CSharpSyntaxTree.ParseText("class __RazorCode {" + code + "}");
        var root = tree.GetRoot();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var methodId = relPath + "::" + method.Identifier.Text;
            extraction.Nodes.Add(new Node
            {
                Id = methodId,
                Label = method.Identifier.Text + "()",
                Type = "method",
                SourceFile = relPath,
                SourceLocation = "L" + (startLine + method.GetLocation().GetLineSpan().StartLinePosition.Line),
            });
            extraction.Edges.Add(new Edge { Source = relPath, Target = methodId, Relation = Relation.Contains, SourceFile = relPath });
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member)
                continue;
            var methodName = member.Name.Identifier.Text;

            if (methodName is "InvokeAsync" or "InvokeVoidAsync" &&
                invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax { Token.Value: string jsName })
            {
                var jsId = "js:" + jsName;
                // No SourceFile: stub — the defining .js file supplies the location on merge.
                extraction.Nodes.Add(new Node { Id = jsId, Label = jsName, FileType = "concept", Type = "js_function" });
                extraction.Edges.Add(new Edge
                {
                    Source = relPath, Target = jsId, Relation = Relation.Calls, Context = "call",
                    Confidence = Confidence.Inferred, SourceFile = relPath,
                });
            }
            else if (member.Expression is IdentifierNameSyntax { Identifier.Text: var receiver } && injected.TryGetValue(receiver, out var serviceType))
            {
                extraction.Edges.Add(new Edge
                {
                    Source = relPath, Target = $"member:{serviceType}.{methodName}", Relation = Relation.Calls, Context = "call",
                    Confidence = Confidence.Inferred, SourceFile = relPath,
                });
            }
        }
    }

    private static (string Markup, string Code, int CodeStartLine) SplitCodeBlock(string text)
    {
        var at = text.IndexOf("@code", StringComparison.Ordinal);
        if (at < 0)
            return (text, "", 0);
        var open = text.IndexOf('{', at);
        if (open < 0)
            return (text, "", 0);
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0)
                return (text[..at], text[(open + 1)..i], new LineIndex(text).LineAt(open));
        }
        return (text[..at], text[(open + 1)..], new LineIndex(text).LineAt(open));
    }

    private static string ShortName(string typeName) => TextUtil.ShortName(typeName);
}
