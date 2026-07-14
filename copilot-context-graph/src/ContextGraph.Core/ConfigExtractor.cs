using System.Text.Json;

namespace ContextGraph.Core;

/// <summary>appsettings*.json → a config: node per key path (colon-joined, matching
/// IConfiguration addressing). Code-side reads reference these ids; keys nothing reads
/// still appear, hanging off their file — that's the "is this setting dead?" signal.</summary>
public static class ConfigExtractor
{
    public static Extraction Extract(string filePath, string rootDir)
    {
        var extraction = new Extraction();
        var relPath = Path.GetRelativePath(rootDir, filePath).Replace('\\', '/');
        extraction.Nodes.Add(new Node { Id = relPath, Label = Path.GetFileName(relPath), Type = "config_file", SourceFile = relPath });

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(filePath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            return extraction;
        }

        using (doc)
        {
            Walk(doc.RootElement, "");
        }
        return extraction;

        void Walk(JsonElement element, string prefix)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return;
            foreach (var property in element.EnumerateObject())
            {
                var path = prefix.Length == 0 ? property.Name : prefix + ":" + property.Name;
                extraction.Nodes.Add(new Node { Id = "config:" + path, Label = path, FileType = "concept", Type = "config_key", SourceFile = relPath });
                extraction.Edges.Add(new Edge { Source = relPath, Target = "config:" + path, Relation = Relation.Contains, SourceFile = relPath });
                Walk(property.Value, path);
            }
        }
    }
}
