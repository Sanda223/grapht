using System.Text;
using System.Text.RegularExpressions;

namespace ContextGraph.Core;

/// <summary>Plain-language querying over a built graph. Ports the reference tool's
/// semantics: tiered IDF scoring (exact 1000 / prefix 100 / substring 1 / source 0.5),
/// coverage-squared scaling, ≤3 seeds with a 0.2 gap ratio, BFS/DFS traversal with a
/// p99-degree hub guard, and a budgeted plain-text rendering.</summary>
public sealed partial class QueryEngine
{
    [GeneratedRegex(@"\w+")] private static partial Regex TokenRx();

    private static readonly HashSet<string> Stopwords =
    [
        "how", "what", "where", "when", "why", "which", "who", "the", "and", "for", "does",
        "work", "works", "with", "that", "this", "are", "can", "you", "use", "used", "uses",
        "get", "all", "any", "its", "into", "from", "not", "has", "have",
    ];

    private readonly KnowledgeGraph _graph;
    private readonly Dictionary<int, string> _communityLabels;
    private readonly int _hubThreshold;

    public QueryEngine(KnowledgeGraph graph)
    {
        _graph = graph;
        _communityLabels = Louvain.LabelCommunities(graph, CommunitiesFrom(graph));

        var degrees = graph.Nodes.Keys.Select(graph.Degree).OrderBy(d => d).ToList();
        var p99 = degrees.Count == 0 ? 0 : degrees[(int)(degrees.Count * 0.99) is var i && i >= degrees.Count ? degrees.Count - 1 : i];
        _hubThreshold = Math.Max(50, p99);
    }

    public static Dictionary<int, List<string>> CommunitiesFrom(KnowledgeGraph graph) =>
        graph.Nodes.Values
            .GroupBy(n => n.Community ?? 0)
            .ToDictionary(g => g.Key, g => g.Select(n => n.Id).OrderBy(id => id, StringComparer.Ordinal).ToList());

    // --- query ---

    public string Query(string question, bool dfs = false, int depth = 2, int tokenBudget = 2000)
    {
        var terms = Terms(question);
        if (terms.Count == 0)
            return "No usable terms in the question.";

        var seeds = PickSeeds(terms);
        if (seeds.Count == 0)
            return $"No node matching: {string.Join(", ", terms)}";

        var visited = dfs ? Dfs(seeds, depth) : Bfs(seeds, depth);
        return Render(seeds, visited, dfs ? "DFS" : "BFS", depth, tokenBudget);
    }

    // --- path ---

    public string Path(string source, string target, int maxHops = 8)
    {
        var sourceNode = BestMatch(source);
        var targetNode = BestMatch(target);
        if (sourceNode is null)
            return $"No node matching \"{source}\"";
        if (targetNode is null)
            return $"No node matching \"{target}\"";
        if (sourceNode == targetNode)
            return $"\"{source}\" and \"{target}\" resolve to the same node: {_graph.Nodes[sourceNode].Label}";

        var path = _graph.ShortestPath(sourceNode, targetNode, maxHops);
        if (path is null)
            return $"No path found between {_graph.Nodes[sourceNode].Label} and {_graph.Nodes[targetNode].Label} (within {maxHops} hops)";

        var sb = new StringBuilder($"Shortest path ({path.Count - 1} hops):\n  {_graph.Nodes[path[0]].Label}");
        for (var i = 1; i < path.Count; i++)
        {
            var forward = _graph.OutEdges(path[i - 1]).FirstOrDefault(e => e.Target == path[i]);
            var edge = forward ?? _graph.EdgeBetween(path[i - 1], path[i])!;
            sb.Append(forward is not null
                ? $" --{edge.Relation} [{edge.Confidence}]--> {_graph.Nodes[path[i]].Label}"
                : $" <--{edge.Relation} [{edge.Confidence}]-- {_graph.Nodes[path[i]].Label}");
        }
        return sb.ToString();
    }

    // --- explain ---

    public string Explain(string label, int maxConnections = 20)
    {
        var id = BestMatch(label);
        if (id is null)
            return $"No node matching \"{label}\"";
        var node = _graph.Nodes[id];

        var sb = new StringBuilder();
        sb.AppendLine($"Node: {node.Label}");
        sb.AppendLine($"  ID:        {node.Id}");
        if (node.SourceFile is not null)
            sb.AppendLine($"  Source:    {node.SourceFile} {node.SourceLocation}".TrimEnd());
        sb.AppendLine($"  Type:      {node.Type ?? node.FileType}");
        sb.AppendLine($"  Community: {CommunityName(node)}");
        sb.AppendLine($"  Degree:    {_graph.Degree(id)}");
        sb.AppendLine();

        var connections = _graph.OutEdges(id).Select(e => (edge: e, other: e.Target, outgoing: true))
            .Concat(_graph.InEdges(id).Select(e => (edge: e, other: e.Source, outgoing: false)))
            .OrderByDescending(c => _graph.Degree(c.other))
            .ThenBy(c => c.other, StringComparer.Ordinal)
            .ToList();
        sb.AppendLine($"Connections ({connections.Count}):");
        foreach (var (edge, other, outgoing) in connections.Take(maxConnections))
            sb.AppendLine(outgoing
                ? $"  --> {_graph.Nodes[other].Label} [{edge.Relation}] [{edge.Confidence}]"
                : $"  <-- {_graph.Nodes[other].Label} [{edge.Relation}] [{edge.Confidence}]");
        if (connections.Count > maxConnections)
            sb.AppendLine($"  ... and {connections.Count - maxConnections} more");
        return sb.ToString().TrimEnd();
    }

    // --- stats ---

    public string Stats()
    {
        var share = Analyzer.ConfidenceShare(_graph);
        return $"""
            Nodes:       {_graph.NodeCount}
            Edges:       {_graph.EdgeCount}
            Communities: {_communityLabels.Count}
            EXTRACTED:   {share.GetValueOrDefault(Confidence.Extracted, 0):0.#}%
            INFERRED:    {share.GetValueOrDefault(Confidence.Inferred, 0):0.#}%
            AMBIGUOUS:   {share.GetValueOrDefault(Confidence.Ambiguous, 0):0.#}%
            """;
    }

    // --- scoring ---

    private static List<string> Terms(string question) =>
        TokenRx().Matches(question.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(t => t.Length > 2 && !Stopwords.Contains(t))
            .Distinct()
            .ToList();

    private Dictionary<string, double> ScoreNodes(List<string> terms)
    {
        // Document frequency per term over labels, for IDF weighting.
        var df = terms.ToDictionary(t => t, t => Math.Max(1, _graph.Nodes.Values.Count(n => n.Label.Contains(t, StringComparison.OrdinalIgnoreCase))));
        var n = Math.Max(1, _graph.NodeCount);

        var scores = new Dictionary<string, double>();
        foreach (var node in _graph.Nodes.Values)
        {
            var labelTokens = TokenRx().Matches(node.Label.ToLowerInvariant()).Select(m => m.Value).ToList();
            var labelLower = node.Label.ToLowerInvariant();
            double strong = 0, weak = 0;
            var matched = 0;
            foreach (var term in terms)
            {
                var idf = 1 + Math.Log((double)n / df[term]);
                if (labelTokens.Contains(term))
                {
                    strong += 1000 * idf;
                    matched++;
                }
                else if (labelTokens.Any(t => t.StartsWith(term)))
                {
                    strong += 100 * idf;
                    matched++;
                }
                else if (labelLower.Contains(term))
                {
                    weak += idf;
                    matched++;
                }
                else if (node.SourceFile?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
                {
                    weak += 0.5;
                }
            }
            var coverage = (double)matched / terms.Count;
            var score = strong * coverage * coverage + weak;
            if (string.Join(" ", labelTokens) == string.Join(" ", terms))
                score *= 10;
            if (score > 0)
                scores[node.Id] = score;
        }
        return scores;
    }

    private List<string> PickSeeds(List<string> terms, int maxSeeds = 3, double gapRatio = 0.2)
    {
        var scores = ScoreNodes(terms);
        var ranked = scores.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        if (ranked.Count == 0)
            return [];

        var seeds = new List<string>();
        var seenLabels = new HashSet<string>();
        var top = ranked[0].Value;
        foreach (var (id, score) in ranked)
        {
            if (seeds.Count >= maxSeeds || score < top * gapRatio)
                break;
            if (seenLabels.Add(_graph.Nodes[id].Label.ToLowerInvariant()))
                seeds.Add(id);
        }

        // Guarantee coverage: every term should be represented by some seed.
        foreach (var term in terms)
        {
            if (seeds.Any(s => _graph.Nodes[s].Label.Contains(term, StringComparison.OrdinalIgnoreCase)))
                continue;
            var best = ranked
                .Where(kv => _graph.Nodes[kv.Key].Label.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(kv => kv.Value).ThenByDescending(kv => _graph.Degree(kv.Key))
                .Select(kv => kv.Key).FirstOrDefault();
            if (best is not null && !seeds.Contains(best) && seeds.Count < maxSeeds + 2)
                seeds.Add(best);
        }
        return seeds;
    }

    /// <summary>Resolve a user-supplied name to a node id (or null). Precedence:
    /// source-file exact → label/id exact → prefix → substring; ties by degree.</summary>
    public string? ResolveNode(string query) => BestMatch(query);

    private string? BestMatch(string query)
    {
        var q = query.ToLowerInvariant();
        // Precedence: source-file exact → label/id exact → prefix → substring.
        var bySource = _graph.Nodes.Values.Where(n => string.Equals(n.SourceFile, query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (bySource.Count > 0)
            return Pick(bySource);
        var exact = _graph.Nodes.Values.Where(n => n.Label.Equals(q, StringComparison.OrdinalIgnoreCase) || n.Id.Equals(q, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 0)
            return Pick(exact);
        var prefix = _graph.Nodes.Values.Where(n => n.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase)).ToList();
        if (prefix.Count > 0)
            return Pick(prefix);
        var substring = _graph.Nodes.Values.Where(n => n.Label.Contains(q, StringComparison.OrdinalIgnoreCase) || n.Id.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        return substring.Count > 0 ? Pick(substring) : null;

        string Pick(List<Node> candidates) =>
            candidates.OrderByDescending(n => _graph.Degree(n.Id)).ThenBy(n => n.Id, StringComparer.Ordinal).First().Id;
    }

    // --- traversal ---

    private List<string> Bfs(List<string> seeds, int depth)
    {
        var visited = new List<string>(seeds);
        var seen = seeds.ToHashSet();
        var frontier = new Queue<(string id, int d)>(seeds.Select(s => (s, 0)));
        while (frontier.Count > 0)
        {
            var (id, d) = frontier.Dequeue();
            if (d >= depth || (IsHub(id) && !seeds.Contains(id)))
                continue;
            foreach (var next in _graph.UndirectedNeighbors(id).OrderBy(x => x, StringComparer.Ordinal))
                if (seen.Add(next))
                {
                    visited.Add(next);
                    frontier.Enqueue((next, d + 1));
                }
        }
        return visited;
    }

    private List<string> Dfs(List<string> seeds, int depth)
    {
        var visited = new List<string>();
        var seen = new HashSet<string>();
        foreach (var seed in seeds)
            Walk(seed, 0);
        return visited;

        void Walk(string id, int d)
        {
            if (!seen.Add(id))
                return;
            visited.Add(id);
            if (d >= depth || (IsHub(id) && !seeds.Contains(id)))
                return;
            foreach (var next in _graph.UndirectedNeighbors(id).OrderBy(x => x, StringComparer.Ordinal))
                Walk(next, d + 1);
        }
    }

    private bool IsHub(string id) => _graph.Degree(id) >= _hubThreshold;

    // --- rendering ---

    private string Render(List<string> seeds, List<string> visited, string mode, int depth, int tokenBudget)
    {
        var charBudget = tokenBudget * 3;
        var included = visited.ToHashSet();
        var sb = new StringBuilder();
        sb.AppendLine($"Traversal: {mode} depth={depth} | Start: [{string.Join(", ", seeds.Select(s => _graph.Nodes[s].Label))}] | {visited.Count} nodes found");
        sb.AppendLine();

        var ordered = seeds.Concat(visited.Except(seeds).OrderByDescending(_graph.Degree).ThenBy(x => x, StringComparer.Ordinal)).ToList();
        var truncated = false;
        foreach (var id in ordered)
        {
            var node = _graph.Nodes[id];
            var line = $"NODE {node.Label} [src={node.SourceFile ?? "-"} loc={node.SourceLocation ?? "-"} community={CommunityName(node)}]";
            if (sb.Length + line.Length > charBudget) { truncated = true; break; }
            sb.AppendLine(line);
        }
        if (!truncated)
            foreach (var edge in _graph.Edges.Where(e => included.Contains(e.Source) && included.Contains(e.Target))
                         .OrderBy(e => e.Source, StringComparer.Ordinal).ThenBy(e => e.Target, StringComparer.Ordinal))
            {
                var ctx = edge.Context is null ? "" : $" context={edge.Context}";
                var line = $"EDGE {_graph.Nodes[edge.Source].Label} --{edge.Relation} [{edge.Confidence}{ctx}]--> {_graph.Nodes[edge.Target].Label}";
                if (sb.Length + line.Length > charBudget) { truncated = true; break; }
                sb.AppendLine(line);
            }
        if (truncated)
            sb.AppendLine($"... truncated at ~{tokenBudget} tokens (raise --budget for more)");
        return sb.ToString().TrimEnd();
    }

    private string CommunityName(Node node) =>
        node.Community is { } c && _communityLabels.TryGetValue(c, out var name) ? name : (node.Community?.ToString() ?? "-");
}
