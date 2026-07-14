using System.Text.RegularExpressions;

namespace ContextGraph.Core;

/// <summary>Deterministic docs layer: headings become section nodes, relative links become
/// doc↔doc edges, and inline-code mentions (`OrderService`) become INFERRED references
/// resolved against the code graph's labels — connecting intent to implementation without
/// any LLM. Prose words are deliberately not matched; only backticked identifiers, which
/// authors already use to mean "this symbol".</summary>
public static partial class MarkdownExtractor
{
    [GeneratedRegex(@"^(#{1,3})\s+(.+)$", RegexOptions.Multiline)] private static partial Regex HeadingRx();
    [GeneratedRegex(@"(?<!\!)\[[^\]]*\]\(\s*<?([^)\s>#?]+)")] private static partial Regex LinkRx();
    [GeneratedRegex(@"`([A-Za-z_][\w.<>]{2,60})`")] private static partial Regex CodeSpanRx();

    public static Extraction Extract(string filePath, string rootDir)
    {
        var extraction = new Extraction();
        var text = File.ReadAllText(filePath);
        var relPath = TextUtil.RelPath(filePath, rootDir);
        var lines = new LineIndex(text);
        extraction.Nodes.Add(new Node { Id = relPath, Label = Path.GetFileName(relPath), FileType = "doc", Type = "doc", SourceFile = relPath });

        foreach (Match match in HeadingRx().Matches(text))
        {
            var title = match.Groups[2].Value.Trim();
            var id = relPath + "#" + Slug(title);
            extraction.Nodes.Add(new Node
            {
                Id = id,
                Label = title,
                FileType = "doc",
                Type = "doc_section",
                SourceFile = relPath,
                SourceLocation = lines.LabelAt(match.Index),
            });
            extraction.Edges.Add(new Edge { Source = relPath, Target = id, Relation = Relation.Contains, SourceFile = relPath });
        }

        foreach (var target in LinkRx().Matches(text).Select(m => m.Groups[1].Value).Distinct())
        {
            if (target.Contains("://") || target.StartsWith("mailto:"))
                continue;
            var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, target));
            var targetRel = Path.GetRelativePath(rootDir, resolved).Replace('\\', '/');
            // Dropped by GraphBuilder unless some extractor declared the target file.
            extraction.Edges.Add(new Edge { Source = relPath, Target = targetRel, Relation = Relation.Imports, SourceFile = relPath });
        }

        foreach (var mention in CodeSpanRx().Matches(text).Select(m => TextUtil.ShortName(m.Groups[1].Value)).Distinct())
            extraction.Edges.Add(new Edge
            {
                Source = relPath,
                Target = "byname:" + mention,
                Relation = Relation.References,
                Confidence = Confidence.Inferred,
                SourceFile = relPath,
            });
        return extraction;
    }

    private static string Slug(string title) =>
        new(title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
}
