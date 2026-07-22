namespace Grapht.Core;

public sealed record GodNode(string Id, string Label, int Degree);
public sealed record Surprise(Edge Edge, int Score, string Why);
public sealed record SuggestedQuestion(string Kind, string Question);

public sealed record Analysis(
    List<GodNode> GodNodes,
    List<Surprise> Surprises,
    List<SuggestedQuestion> Questions,
    Dictionary<int, List<string>> Communities,
    Dictionary<int, string> CommunityLabels,
    Dictionary<string, double> ConfidenceShare);

/// <summary>Heuristics ported from Graphify's analyze.py, minus the noise filters that
/// Roslyn's in-source-only extraction makes unnecessary.</summary>
public static class Analyzer
{
    public static Analysis Analyze(KnowledgeGraph graph, Dictionary<int, List<string>> communities, Dictionary<int, string> communityLabels)
    {
        return new Analysis(
            GodNodes(graph),
            Surprises(graph),
            Questions(graph, communities),
            communities,
            communityLabels,
            ConfidenceShare(graph));
    }

    public static List<GodNode> GodNodes(KnowledgeGraph graph, int topN = 10) =>
        graph.Nodes.Values
            .Where(n => n.Type is not ("file" or "namespace") && n.FileType != "concept")
            .Select(n => new GodNode(n.Id, n.Label, graph.Degree(n.Id)))
            .OrderByDescending(g => g.Degree)
            .ThenBy(g => g.Id, StringComparer.Ordinal)
            .Take(topN)
            .ToList();

    public static List<Surprise> Surprises(KnowledgeGraph graph, int topN = 5)
    {
        var scored = new List<Surprise>();
        foreach (var edge in graph.Edges)
        {
            if (edge.Relation is Relation.Contains or Relation.Imports)
                continue;
            var source = graph.Nodes[edge.Source];
            var target = graph.Nodes[edge.Target];
            if (source.Type is "file" or "namespace" || target.Type is "file" or "namespace")
                continue;

            var score = edge.Confidence switch
            {
                Confidence.Ambiguous => 3,
                Confidence.Inferred => 2,
                _ => 1,
            };
            var why = new List<string> { edge.Confidence.ToLowerInvariant() + " " + edge.Relation };

            if (source.Community != target.Community)
            {
                score += 1;
                why.Add("crosses communities");
            }
            if (ProjectOf(source) is { } p1 && ProjectOf(target) is { } p2 && p1 != p2)
            {
                score += 2;
                why.Add($"crosses projects ({p1} -> {p2})");
            }
            int d1 = graph.Degree(edge.Source), d2 = graph.Degree(edge.Target);
            if (Math.Min(d1, d2) <= 2 && Math.Max(d1, d2) >= 5)
            {
                score += 1;
                why.Add("peripheral node touching a hub");
            }
            scored.Add(new Surprise(edge, score, string.Join("; ", why)));
        }

        // One per community pair, best first.
        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Edge.Source, StringComparer.Ordinal)
            .ThenBy(s => s.Edge.Target, StringComparer.Ordinal)
            .GroupBy(s => CommunityPair(graph, s.Edge))
            .Select(g => g.First())
            .OrderByDescending(s => s.Score)
            .Take(topN)
            .ToList();
    }

    public static List<SuggestedQuestion> Questions(KnowledgeGraph graph, Dictionary<int, List<string>> communities, int topN = 7)
    {
        var questions = new List<SuggestedQuestion>();

        foreach (var edge in graph.Edges.Where(e => e.Confidence == Confidence.Ambiguous)
                     .OrderBy(e => e.Source, StringComparer.Ordinal).Take(3))
            questions.Add(new SuggestedQuestion("ambiguous_edge",
                $"Is `{graph.Nodes[edge.Source].Label}` really connected to `{graph.Nodes[edge.Target].Label}` ({edge.Relation})? The extractor couldn't resolve it unambiguously."));

        foreach (var node in graph.Nodes.Values
                     .Where(n => n.Type is not ("file" or "namespace"))
                     .Where(n => graph.OutEdges(n.Id).Concat(graph.InEdges(n.Id)).Count(e => e.Confidence == Confidence.Inferred) >= 2)
                     .OrderByDescending(n => graph.Degree(n.Id)).ThenBy(n => n.Id, StringComparer.Ordinal).Take(2))
            questions.Add(new SuggestedQuestion("verify_inferred",
                $"`{node.Label}` has several inferred connections — worth verifying how it is actually wired."));

        foreach (var node in graph.Nodes.Values
                     .Where(n => n.Type is not ("file" or "namespace") && n.FileType != "concept" && graph.Degree(n.Id) <= 1)
                     .OrderBy(n => n.Id, StringComparer.Ordinal).Take(2))
            questions.Add(new SuggestedQuestion("isolated_node",
                $"`{node.Label}` ({node.SourceFile}) is barely connected — dead code, or a missed relationship?"));

        foreach (var (id, members) in communities.Where(kv => kv.Value.Count >= 5)
                     .Where(kv => Louvain.CohesionScore(graph, kv.Value) < 0.15).Take(2))
            questions.Add(new SuggestedQuestion("low_cohesion",
                $"Community {id} has {members.Count} members but few internal links — is it really one concept?"));

        if (questions.Count == 0)
            questions.Add(new SuggestedQuestion("no_signal", "The graph looks clean — try `query` on the god nodes to explore the core architecture."));
        return questions.Take(topN).ToList();
    }

    public static Dictionary<string, double> ConfidenceShare(KnowledgeGraph graph)
    {
        var total = Math.Max(1, graph.EdgeCount);
        return graph.Edges
            .GroupBy(e => e.Confidence)
            .ToDictionary(g => g.Key, g => Math.Round(100.0 * g.Count() / total, 1));
    }

    /// <summary>First two path segments of the source file, e.g. "src/SampleApp.Domain".</summary>
    private static string? ProjectOf(Node node)
    {
        if (node.SourceFile is null)
            return null;
        var parts = node.SourceFile.Split('/');
        return parts.Length >= 2 ? parts[0] + "/" + parts[1] : parts[0];
    }

    private static (int, int) CommunityPair(KnowledgeGraph graph, Edge edge)
    {
        int a = graph.Nodes[edge.Source].Community ?? -1, b = graph.Nodes[edge.Target].Community ?? -1;
        return a < b ? (a, b) : (b, a);
    }
}
