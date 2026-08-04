using Grapht.Core;

namespace Grapht.View;

public sealed record CommunityDto(int Id, string Label, int Size, string Color, List<string> TopMembers);
public sealed record MetaEdgeDto(int Source, int Target, int Weight);
public sealed record OverviewDto(List<CommunityDto> Communities, List<MetaEdgeDto> Edges, int NodeCount, int EdgeCount);
public sealed record NodeDto(string Id, string Label, string? Type, string? Span, int Community, string CommunityLabel, int Degree, string Color);
public sealed record NeighbourDto(NodeDto Node, string Relation, string Direction, string Confidence);
public sealed record FocusDto(NodeDto Node, List<NeighbourDto> Neighbours);

/// <summary>Shapes graph state into the DTOs the front end consumes. Pure functions over a
/// snapshot — no I/O — so every endpoint is directly testable without a running server.</summary>
public static class GraphApi
{
    /// <summary>Community-level view: one node per community, edges weighted by how many
    /// relationships cross each pair. This is the landing state — the full graph is never
    /// sent to the browser.</summary>
    public static OverviewDto BuildOverview(KnowledgeGraph graph, Analysis analysis)
    {
        var communities = analysis.Communities.OrderBy(kv => kv.Key).Select(kv => new CommunityDto(
            kv.Key,
            analysis.CommunityLabels[kv.Key],
            kv.Value.Count,
            Color(kv.Key),
            kv.Value.OrderByDescending(graph.Degree).ThenBy(id => id, StringComparer.Ordinal)
                    .Take(8).Select(id => graph.Nodes[id].Label).ToList()))
            .ToList();

        var crossings = new Dictionary<(int, int), int>();
        foreach (var edge in graph.Edges)
        {
            int a = graph.Nodes[edge.Source].Community ?? 0, b = graph.Nodes[edge.Target].Community ?? 0;
            if (a == b)
                continue;
            var key = a < b ? (a, b) : (b, a);
            crossings[key] = crossings.GetValueOrDefault(key) + 1;
        }

        var edges = crossings.OrderBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2)
            .Select(kv => new MetaEdgeDto(kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList();

        return new OverviewDto(communities, edges, graph.NodeCount, graph.EdgeCount);
    }

    /// <summary>One node plus its immediate neighbours — the working state. Bounded by the
    /// node's degree, not by graph size.</summary>
    public static FocusDto? BuildFocus(KnowledgeGraph graph, Analysis analysis, string nodeId)
    {
        if (!graph.Nodes.TryGetValue(nodeId, out var node))
            return null;

        // A self-loop (source == target) shows up in both OutEdges and InEdges for the same
        // node, which would otherwise emit two NeighbourDto rows for one edge — and, since the
        // front end keys Cytoscape elements by "from|to|relation", two identical ids for the
        // same self-loop. Treat a self-loop as outgoing only, so it produces exactly one row.
        var neighbours = graph.OutEdges(nodeId)
            .Select(e => (Edge: e, Other: e.Target, Direction: "out"))
            .Concat(graph.InEdges(nodeId).Where(e => e.Source != e.Target)
                .Select(e => (Edge: e, Other: e.Source, Direction: "in")))
            .Where(x => graph.Nodes.ContainsKey(x.Other))
            .OrderBy(x => x.Direction, StringComparer.Ordinal)
            .ThenBy(x => x.Other, StringComparer.Ordinal)
            .ThenBy(x => x.Edge.Relation, StringComparer.Ordinal)
            .Select(x => new NeighbourDto(
                Describe(graph, analysis, graph.Nodes[x.Other]),
                x.Edge.Relation, x.Direction, x.Edge.Confidence))
            .ToList();

        return new FocusDto(Describe(graph, analysis, node), neighbours);
    }

    /// <summary>Ranked search for the viewer's typeahead: exact (label, id, or label+"()")
    /// beats prefix beats substring; within a tier, higher degree first. Pure, like the
    /// other builders — the endpoint stays a one-liner.</summary>
    public static List<NodeDto> Search(KnowledgeGraph graph, Analysis analysis, string query, int limit = 10)
    {
        var q = query.Trim();
        if (q.Length == 0)
            return [];

        return graph.Nodes.Values
            .Select(n => (Node: n, Tier: Tier(n)))
            .Where(x => x.Tier < 3)
            .OrderBy(x => x.Tier)
            .ThenByDescending(x => graph.Degree(x.Node.Id))
            .ThenBy(x => x.Node.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select(x => Describe(graph, analysis, x.Node))
            .ToList();

        int Tier(Node n) =>
            n.Label.Equals(q, StringComparison.OrdinalIgnoreCase)
                || n.Id.Equals(q, StringComparison.OrdinalIgnoreCase)
                || n.Label.Equals(q + "()", StringComparison.OrdinalIgnoreCase) ? 0
            : n.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1
            : n.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                || n.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ? 2
            : 3;
    }

    private static NodeDto Describe(KnowledgeGraph graph, Analysis analysis, Node node)
    {
        var community = node.Community ?? 0;
        return new NodeDto(
            node.Id, node.Label, node.Type, node.Span, community,
            analysis.CommunityLabels.GetValueOrDefault(community, $"community {community}"),
            graph.Degree(node.Id), Color(community));
    }

    /// <summary>Golden-angle hue walk — visually distinct at any community count.</summary>
    private static string Color(int community) => $"hsl({(int)(community * 137.508) % 360}, 62%, 55%)";
}
