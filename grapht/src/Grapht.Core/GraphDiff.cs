using System.Text;

namespace Grapht.Core;

public sealed record DiffResult(
    List<Node> AddedNodes,
    List<Node> RemovedNodes,
    List<Edge> AddedEdges,
    List<Edge> RemovedEdges)
{
    public bool IsEmpty => AddedNodes.Count == 0 && RemovedNodes.Count == 0 &&
                           AddedEdges.Count == 0 && RemovedEdges.Count == 0;
}

/// <summary>Structural diff between two builds (works because graph.json is
/// deterministic). Base for the PR-impact comment: what a change adds/removes
/// architecturally, with new cross-community dependencies called out.</summary>
public static class GraphDiff
{
    public static DiffResult Compute(KnowledgeGraph baseGraph, KnowledgeGraph headGraph)
    {
        var addedNodes = headGraph.Nodes.Values.Where(n => !baseGraph.Nodes.ContainsKey(n.Id))
            .OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        var removedNodes = baseGraph.Nodes.Values.Where(n => !headGraph.Nodes.ContainsKey(n.Id))
            .OrderBy(n => n.Id, StringComparer.Ordinal).ToList();

        var baseEdges = baseGraph.Edges.Select(EdgeKey).ToHashSet();
        var headEdges = headGraph.Edges.Select(EdgeKey).ToHashSet();
        var addedEdges = headGraph.Edges.Where(e => !baseEdges.Contains(EdgeKey(e)))
            .OrderBy(e => e.Source, StringComparer.Ordinal).ThenBy(e => e.Target, StringComparer.Ordinal).ToList();
        var removedEdges = baseGraph.Edges.Where(e => !headEdges.Contains(EdgeKey(e)))
            .OrderBy(e => e.Source, StringComparer.Ordinal).ThenBy(e => e.Target, StringComparer.Ordinal).ToList();

        return new DiffResult(addedNodes, removedNodes, addedEdges, removedEdges);

        static (string, string, string) EdgeKey(Edge e) => (e.Source, e.Target, e.Relation);
    }

    public static string RenderMarkdown(DiffResult diff, KnowledgeGraph headGraph, int cap = 30)
    {
        if (diff.IsEmpty)
            return "## Graph diff\n\nNo structural changes.";

        var sb = new StringBuilder();
        sb.AppendLine("## Graph diff");
        sb.AppendLine();
        sb.AppendLine($"**+{diff.AddedNodes.Count} / −{diff.RemovedNodes.Count} nodes, " +
                      $"+{diff.AddedEdges.Count} / −{diff.RemovedEdges.Count} edges**");

        // New dependencies that cross community boundaries are the architecture-drift signal.
        var crossCommunity = diff.AddedEdges.Where(e =>
                headGraph.Nodes.TryGetValue(e.Source, out var s) &&
                headGraph.Nodes.TryGetValue(e.Target, out var t) &&
                s.Community != t.Community &&
                e.Relation is not (Relation.Contains or Relation.Imports))
            .ToList();
        Section("⚠ New cross-community dependencies", crossCommunity.Select(edge =>
            $"- `{Label(headGraph, edge.Source)}` --{edge.Relation}--> `{Label(headGraph, edge.Target)}`" +
            (edge.SourceFile is null ? "" : $" ({edge.SourceFile} {edge.SourceLocation})")));

        Section("Added nodes", diff.AddedNodes.Select(n => $"- `{n.Label}` [{n.Type ?? n.FileType}]" +
            (n.SourceFile is null ? "" : $" ({n.SourceFile} {n.SourceLocation})")));
        Section("Removed nodes", diff.RemovedNodes.Select(n => $"- `{n.Label}` [{n.Type ?? n.FileType}]"));
        Section("Added edges", diff.AddedEdges.Select(e => $"- `{e.Source}` --{e.Relation}--> `{e.Target}`"));
        Section("Removed edges", diff.RemovedEdges.Select(e => $"- `{e.Source}` --{e.Relation}--> `{e.Target}`"));
        return sb.ToString().TrimEnd();

        void Section(string title, IEnumerable<string> lines)
        {
            var list = lines.ToList();
            if (list.Count == 0)
                return;
            sb.AppendLine();
            sb.AppendLine($"### {title} ({list.Count})");
            foreach (var line in list.Take(cap))
                sb.AppendLine(line);
            if (list.Count > cap)
                sb.AppendLine($"- … and {list.Count - cap} more");
        }

        static string Label(KnowledgeGraph g, string id) => g.Nodes.TryGetValue(id, out var n) ? n.Label : id;
    }
}
