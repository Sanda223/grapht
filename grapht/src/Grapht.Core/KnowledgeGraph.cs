namespace Grapht.Core;

/// <summary>Directed graph with dedup on add. Node merge keeps the first non-null value per
/// field; edge dedup keys on (source, target, relation) and keeps the strongest confidence.</summary>
public sealed class KnowledgeGraph
{
    private readonly Dictionary<string, Node> _nodes = [];
    private readonly Dictionary<(string, string, string), Edge> _edges = [];
    private Dictionary<string, List<Edge>>? _outIndex;
    private Dictionary<string, List<Edge>>? _inIndex;

    public IReadOnlyDictionary<string, Node> Nodes => _nodes;
    public IEnumerable<Edge> Edges => _edges.Values;
    public int NodeCount => _nodes.Count;
    public int EdgeCount => _edges.Count;

    public void AddNode(Node node)
    {
        if (_nodes.TryGetValue(node.Id, out var existing))
        {
            // Merge: keep existing values, fill gaps from the newcomer.
            _nodes[node.Id] = new Node
            {
                Id = existing.Id,
                Label = existing.Label,
                FileType = existing.FileType,
                Type = existing.Type ?? node.Type,
                SourceFile = existing.SourceFile ?? node.SourceFile,
                SourceLocation = existing.SourceLocation ?? node.SourceLocation,
                EndLine = existing.EndLine ?? node.EndLine,
                Community = existing.Community ?? node.Community,
                Metadata = existing.Metadata ?? node.Metadata,
            };
        }
        else
        {
            _nodes[node.Id] = node;
        }
        _outIndex = _inIndex = null;
    }

    public void AddEdge(Edge edge)
    {
        var key = (edge.Source, edge.Target, edge.Relation);
        if (_edges.TryGetValue(key, out var existing))
        {
            if (Confidence.Rank(edge.Confidence) > Confidence.Rank(existing.Confidence))
                existing.Confidence = edge.Confidence;
            existing.Weight = Math.Max(existing.Weight, edge.Weight);
        }
        else
        {
            _edges[key] = edge;
        }
        _outIndex = _inIndex = null;
    }

    public bool RemoveNode(string id)
    {
        if (!_nodes.Remove(id))
            return false;
        foreach (var key in _edges.Keys.Where(k => k.Item1 == id || k.Item2 == id).ToList())
            _edges.Remove(key);
        _outIndex = _inIndex = null;
        return true;
    }

    public IReadOnlyList<Edge> OutEdges(string id) =>
        (_outIndex ??= BuildIndex(e => e.Source)).GetValueOrDefault(id, []);

    public IReadOnlyList<Edge> InEdges(string id) =>
        (_inIndex ??= BuildIndex(e => e.Target)).GetValueOrDefault(id, []);

    public int Degree(string id) => OutEdges(id).Count + InEdges(id).Count;

    public IEnumerable<string> UndirectedNeighbors(string id) =>
        OutEdges(id).Select(e => e.Target).Concat(InEdges(id).Select(e => e.Source)).Distinct();

    /// <summary>Unweighted shortest path on the undirected view, or null.</summary>
    public List<string>? ShortestPath(string sourceId, string targetId, int maxHops = int.MaxValue)
    {
        if (!_nodes.ContainsKey(sourceId) || !_nodes.ContainsKey(targetId))
            return null;
        if (sourceId == targetId)
            return [sourceId];

        var previous = new Dictionary<string, string> { [sourceId] = sourceId };
        var frontier = new Queue<(string id, int depth)>();
        frontier.Enqueue((sourceId, 0));
        while (frontier.Count > 0)
        {
            var (current, depth) = frontier.Dequeue();
            if (depth >= maxHops)
                continue;
            foreach (var next in UndirectedNeighbors(current).OrderBy(n => n, StringComparer.Ordinal))
            {
                if (previous.ContainsKey(next))
                    continue;
                previous[next] = current;
                if (next == targetId)
                    return Backtrack();
                frontier.Enqueue((next, depth + 1));
            }
        }
        return null;

        List<string> Backtrack()
        {
            var path = new List<string> { targetId };
            while (path[^1] != sourceId)
                path.Add(previous[path[^1]]);
            path.Reverse();
            return path;
        }
    }

    /// <summary>The stored edge between two nodes in either direction (for path rendering).</summary>
    public Edge? EdgeBetween(string a, string b) =>
        OutEdges(a).FirstOrDefault(e => e.Target == b) ?? OutEdges(b).FirstOrDefault(e => e.Target == a);

    private Dictionary<string, List<Edge>> BuildIndex(Func<Edge, string> keyOf)
    {
        var index = new Dictionary<string, List<Edge>>();
        foreach (var edge in _edges.Values)
        {
            if (!index.TryGetValue(keyOf(edge), out var list))
                index[keyOf(edge)] = list = [];
            list.Add(edge);
        }
        return index;
    }
}
