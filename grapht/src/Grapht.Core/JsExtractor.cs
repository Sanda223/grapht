using System.Text.RegularExpressions;

namespace Grapht.Core;

/// <summary>Regex scan of first-party .js files (vendored *.min.js is excluded at discovery).
/// Function definitions become js: nodes — the same id scheme the C#-side interop stubs use,
/// so `JS.InvokeAsync("fn")` placeholders gain a real source location by node merge.
/// DotNet.invokeMethodAsync literals become dotnet: placeholders resolved against
/// [JSInvokable] method metadata.</summary>
public static partial class JsExtractor
{
    [GeneratedRegex(@"(?:^|\s)function\s+(\w+)\s*\(", RegexOptions.Multiline)] private static partial Regex FunctionRx();
    [GeneratedRegex(@"(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?(?:function\b|\()", RegexOptions.Multiline)] private static partial Regex ArrowRx();
    [GeneratedRegex(@"window\.(\w+)\s*=\s*(?:async\s+)?(?:function\b|\()")] private static partial Regex WindowRx();
    [GeneratedRegex(@"DotNet\s*\.\s*invokeMethodAsync\s*\(\s*['""][^'""]*['""]\s*,\s*['""](\w+)['""]")] private static partial Regex DotNetInvokeRx();

    public static Extraction Extract(string filePath, string rootDir)
    {
        var extraction = new Extraction();
        var text = File.ReadAllText(filePath);
        var relPath = TextUtil.RelPath(filePath, rootDir);
        var lines = new LineIndex(text);
        extraction.Nodes.Add(new Node { Id = relPath, Label = Path.GetFileName(relPath), Type = "js_file", SourceFile = relPath });

        var functions = FunctionRx().Matches(text).Concat(ArrowRx().Matches(text)).Concat(WindowRx().Matches(text));
        foreach (var match in functions.DistinctBy(m => m.Groups[1].Value))
        {
            var jsId = "js:" + match.Groups[1].Value;
            extraction.Nodes.Add(new Node
            {
                Id = jsId,
                Label = match.Groups[1].Value,
                FileType = "concept",
                Type = "js_function",
                SourceFile = relPath,
                SourceLocation = lines.LabelAt(match.Index),
            });
            extraction.Edges.Add(new Edge { Source = relPath, Target = jsId, Relation = Relation.Contains, SourceFile = relPath });
        }

        foreach (Match match in DotNetInvokeRx().Matches(text))
            extraction.Edges.Add(new Edge
            {
                Source = relPath,
                Target = "dotnet:" + match.Groups[1].Value,
                Relation = Relation.Calls,
                Context = "call",
                SourceFile = relPath,
                SourceLocation = lines.LabelAt(match.Index),
            });
        return extraction;
    }
}
