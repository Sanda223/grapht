using System.ComponentModel;
using System.Text;
using ContextGraph.Core;
using ModelContextProtocol.Server;

namespace ContextGraph.Mcp;

/// <summary>The MCP tool surface. Results are compact text with path:line references so
/// the calling assistant can jump straight to code.</summary>
[McpServerToolType]
public sealed class GraphTools
{
    private readonly GraphService _service;
    private readonly QueryLog _log;

    public GraphTools(GraphService service, QueryLog log)
    {
        _service = service;
        _log = log;
    }

    [McpServerTool(Name = "query_graph")]
    [Description("INDEX, not answer: locates the code relevant to a plain-language question by traversing the knowledge graph. Returns the related nodes/edges with file line-spans — read those spans before answering; the graph shows structure, not behavior.")]
    public string QueryGraph(
        [Description("The question, e.g. \"how does order total get calculated\"")] string question,
        [Description("bfs (broad context, default) or dfs (trace a specific chain)")] string mode = "bfs",
        [Description("Traversal depth, 1-6")] int depth = 3,
        [Description("Token budget for the answer")] int tokenBudget = 700) =>
        _log.Record("query_graph", question,
            _service.Current.Engine.Query(question, dfs: mode.Equals("dfs", StringComparison.OrdinalIgnoreCase), depth: Math.Clamp(depth, 1, 6), tokenBudget: tokenBudget));

    [McpServerTool(Name = "shortest_path")]
    [Description("Trace how two concepts (classes, methods, components, routes) connect, hop by hop. Use the hops as a reading list: open the span of each node on the path to see how the connection actually behaves.")]
    public string ShortestPath(
        [Description("Start concept, e.g. \"Orders.razor\"")] string source,
        [Description("End concept, e.g. \"Order.Total\"")] string target,
        [Description("Maximum hops")] int maxHops = 8) =>
        _log.Record("shortest_path", $"{source} -> {target}", _service.Current.Engine.Path(source, target, maxHops));

    [McpServerTool(Name = "get_node")]
    [Description("Show one node in detail: source span, type, community, and every connection. Follow up by reading the node's span.")]
    public string GetNode([Description("Node label or id, e.g. \"OrderService\"")] string label) =>
        _log.Record("get_node", label, _service.Current.Engine.Explain(label));

    [McpServerTool(Name = "get_neighbors")]
    [Description("List the direct neighbors of a node, optionally filtered by relation (calls, imports, inherits, implements, references, contains).")]
    public string GetNeighbors(
        [Description("Node label or id")] string label,
        [Description("Only edges with this relation (optional)")] string? relationFilter = null)
    {
        var snapshot = _service.Current;
        var full = snapshot.Engine.Explain(label, maxConnections: 200);
        if (relationFilter is null)
            return _log.Record("get_neighbors", label, full);
        var lines = full.Split('\n');
        var kept = lines.TakeWhile(l => !l.StartsWith("Connections"))
            .Concat(lines.Where(l => l.Contains($"[{relationFilter}]")));
        return _log.Record("get_neighbors", $"{label} [{relationFilter}]", string.Join('\n', kept));
    }

    [McpServerTool(Name = "god_nodes")]
    [Description("The most connected nodes — the de-facto core of the architecture.")]
    public string GodNodes([Description("How many to list")] int topN = 10)
    {
        var snapshot = _service.Current;
        var sb = new StringBuilder("God nodes (most connected):\n");
        var gods = Analyzer.GodNodes(snapshot.Graph, topN);
        for (var i = 0; i < gods.Count; i++)
        {
            var node = snapshot.Graph.Nodes[gods[i].Id];
            sb.AppendLine($"  {i + 1}. {gods[i].Label} - {gods[i].Degree} edges ({node.SourceFile} {node.SourceLocation})".TrimEnd());
        }
        return _log.Record("god_nodes", topN.ToString(), sb.ToString().TrimEnd());
    }

    [McpServerTool(Name = "graph_stats")]
    [Description("Graph size, community count, and extraction-confidence breakdown.")]
    public string GraphStats() => _log.Record("graph_stats", "", _service.Current.Engine.Stats());

    [McpServerTool(Name = "impact_of_change")]
    [Description("What depends on this node — everything that could break if it changes, grouped by distance. Read the listed call sites before concluding a change is safe.")]
    public string ImpactOfChange(
        [Description("Node label or id, e.g. \"IRepository\"")] string label,
        [Description("How many dependency hops to follow")] int depth = 2)
    {
        var snapshot = _service.Current;
        var graph = snapshot.Graph;
        var candidates = snapshot.Engine.ResolveNodeCandidates(label);
        if (candidates.Count > 1)
            return snapshot.Engine.DescribeAmbiguity(label, candidates);
        var resolved = candidates.FirstOrDefault();
        if (resolved is null)
            return $"No node matching \"{label}\"";

        var byDistance = new Dictionary<int, List<(string id, Edge edge)>>();
        var seen = new HashSet<string> { resolved };
        var frontier = new List<string> { resolved };
        for (var d = 1; d <= Math.Clamp(depth, 1, 6) && frontier.Count > 0; d++)
        {
            var next = new List<string>();
            foreach (var id in frontier)
                foreach (var edge in graph.InEdges(id).Where(e => e.Relation != Relation.Contains).OrderBy(e => e.Source, StringComparer.Ordinal))
                    if (seen.Add(edge.Source))
                    {
                        byDistance.TryAdd(d, []);
                        byDistance[d].Add((edge.Source, edge));
                        next.Add(edge.Source);
                    }
            frontier = next;
        }

        var total = byDistance.Values.Sum(v => v.Count);
        var sb = new StringBuilder($"Impact of changing {graph.Nodes[resolved].Label}: {total} dependent node(s) within {depth} hop(s)\n");
        foreach (var (distance, dependents) in byDistance.OrderBy(kv => kv.Key))
        {
            sb.AppendLine($"  {(distance == 1 ? "direct" : $"{distance} hops")}:");
            foreach (var (id, edge) in dependents)
            {
                var node = graph.Nodes[id];
                sb.AppendLine($"    {node.Label} [{edge.Relation}] ({node.SourceFile} {node.Span})".TrimEnd());
            }
        }
        return _log.Record("impact_of_change", label, sb.ToString().TrimEnd());
    }

    [McpServerTool(Name = "rebuild_graph")]
    [Description("Force a full re-extract of the whole solution (the graph normally stays fresh automatically via the file watcher).")]
    public async Task<string> RebuildGraph() => _log.Record("rebuild_graph", "", await _service.FullReloadAsync());
}
