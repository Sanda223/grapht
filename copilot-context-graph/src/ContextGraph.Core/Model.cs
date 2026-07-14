using System.Text.Json.Serialization;

namespace ContextGraph.Core;

public static class Confidence
{
    public const string Extracted = "EXTRACTED";
    public const string Inferred = "INFERRED";
    public const string Ambiguous = "AMBIGUOUS";

    /// <summary>Higher = more trustworthy.</summary>
    public static int Rank(string confidence) => confidence switch
    {
        Extracted => 2,
        Inferred => 1,
        _ => 0,
    };
}

public static class Relation
{
    public const string Contains = "contains";
    public const string Calls = "calls";
    public const string Imports = "imports";
    public const string Inherits = "inherits";
    public const string Implements = "implements";
    public const string References = "references";
    public const string Uses = "uses";           // project → NuGet package
    public const string BoundTo = "bound_to";    // DI: service interface → registered implementation
    public const string Publishes = "publishes"; // message flow: publisher → message contract
    public const string Consumes = "consumes";   // message flow: consumer → message contract
}

public sealed class Node
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("label")] public required string Label { get; init; }
    [JsonPropertyName("file_type")] public string FileType { get; init; } = "code";
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("source_file")] public string? SourceFile { get; init; }
    [JsonPropertyName("source_location")] public string? SourceLocation { get; init; }
    [JsonPropertyName("community")] public int? Community { get; set; }
    [JsonPropertyName("metadata")] public Dictionary<string, string>? Metadata { get; init; }
}

public sealed class Edge
{
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("target")] public required string Target { get; init; }
    [JsonPropertyName("relation")] public required string Relation { get; init; }
    [JsonPropertyName("context")] public string? Context { get; init; }
    [JsonPropertyName("confidence")] public string Confidence { get; set; } = Core.Confidence.Extracted;
    [JsonPropertyName("weight")] public double Weight { get; set; } = 1.0;
    [JsonPropertyName("source_file")] public string? SourceFile { get; init; }
    [JsonPropertyName("source_location")] public string? SourceLocation { get; init; }
    [JsonPropertyName("metadata")] public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>Nodes and edges pulled from one file; input to GraphBuilder.</summary>
public sealed class Extraction
{
    public List<Node> Nodes { get; } = [];
    public List<Edge> Edges { get; } = [];
}
