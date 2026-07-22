using Grapht.Core;

namespace Grapht.View;

public sealed record CommunityDto(int Id, string Label, int Size, string Color, List<string> TopMembers);
public sealed record MetaEdgeDto(int Source, int Target, int Weight);
public sealed record OverviewDto(List<CommunityDto> Communities, List<MetaEdgeDto> Edges, int NodeCount, int EdgeCount);

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

    /// <summary>Golden-angle hue walk — visually distinct at any community count.</summary>
    private static string Color(int community) => $"hsl({(int)(community * 137.508) % 360}, 62%, 55%)";
}
