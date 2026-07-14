using System.Text.RegularExpressions;

namespace ContextGraph.Core;

/// <summary>Regex scan of .sql files (migrations, procs). Object names normalize to their
/// last segment, lowercased and unbracketed, so `dbo.Orders`, `[Orders]`, and a Dapper
/// literal's `FROM Orders` all land on one node.</summary>
public static partial class SqlExtractor
{
    [GeneratedRegex(@"CREATE\s+(?:OR\s+ALTER\s+)?(TABLE|PROCEDURE|PROC|FUNCTION|VIEW)\s+([\w\.\[\]]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateRx();

    public static Extraction Extract(string filePath, string rootDir)
    {
        var extraction = new Extraction();
        var text = File.ReadAllText(filePath);
        var relPath = TextUtil.RelPath(filePath, rootDir);
        var lines = new LineIndex(text);
        extraction.Nodes.Add(new Node { Id = relPath, Label = Path.GetFileName(relPath), Type = "sql_file", SourceFile = relPath });

        foreach (Match match in CreateRx().Matches(text))
        {
            var kind = match.Groups[1].Value.ToUpperInvariant() switch
            {
                "TABLE" => "table",
                "VIEW" => "view",
                "FUNCTION" => "sql_function",
                _ => "proc",
            };
            var id = SqlObjectId(kind, match.Groups[2].Value);
            extraction.Nodes.Add(new Node
            {
                Id = id,
                Label = match.Groups[2].Value.Replace("[", "").Replace("]", ""),
                FileType = "concept",
                Type = kind,
                SourceFile = relPath,
                SourceLocation = lines.LabelAt(match.Index),
            });
            extraction.Edges.Add(new Edge { Source = relPath, Target = id, Relation = Relation.Contains, SourceFile = relPath });
        }
        return extraction;
    }

    public static string SqlObjectId(string kind, string rawName)
    {
        var bare = rawName.Replace("[", "").Replace("]", "");
        return kind + ":" + bare[(bare.LastIndexOf('.') + 1)..].ToLowerInvariant();
    }
}
