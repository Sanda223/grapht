using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grapht.Core;

/// <summary>graph.json in NetworkX node_link_data format (edges under "links"),
/// byte-compatible with Graphify's output. Nodes and links are sorted so identical
/// input produces identical bytes.</summary>
public static class JsonExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public sealed class GraphDocument
    {
        [JsonPropertyName("directed")] public bool Directed { get; set; } = true;
        [JsonPropertyName("multigraph")] public bool Multigraph { get; set; }
        [JsonPropertyName("graph")] public Dictionary<string, string> Graph { get; set; } = [];
        [JsonPropertyName("nodes")] public List<Node> Nodes { get; set; } = [];
        [JsonPropertyName("links")] public List<Edge> Links { get; set; } = [];
        [JsonPropertyName("built_at_commit")] public string BuiltAtCommit { get; set; } = "";
    }

    public static void Write(KnowledgeGraph graph, string path, string builtAtCommit = "")
    {
        var document = new GraphDocument
        {
            Nodes = graph.Nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToList(),
            Links = graph.Edges
                .OrderBy(e => e.Source, StringComparer.Ordinal)
                .ThenBy(e => e.Target, StringComparer.Ordinal)
                .ThenBy(e => e.Relation, StringComparer.Ordinal)
                .ToList(),
            BuiltAtCommit = builtAtCommit,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(document, Options));
    }

    public static KnowledgeGraph Read(string path)
    {
        var document = JsonSerializer.Deserialize<GraphDocument>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"Not a graph.json: {path}");
        var graph = new KnowledgeGraph();
        foreach (var node in document.Nodes)
            graph.AddNode(node);
        foreach (var edge in document.Links)
            graph.AddEdge(edge);
        return graph;
    }
}
