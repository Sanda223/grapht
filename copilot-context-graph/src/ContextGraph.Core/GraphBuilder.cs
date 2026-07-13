namespace ContextGraph.Core;

public static class GraphBuilder
{
    /// <summary>Merge per-file extractions into one graph. Edges pointing at nodes no
    /// extraction declared (e.g. `using System;`) are dropped — that is the entire
    /// external-noise filter.</summary>
    public static KnowledgeGraph Build(IEnumerable<Extraction> extractions)
    {
        var graph = new KnowledgeGraph();
        var pending = new List<Edge>();
        foreach (var extraction in extractions)
        {
            foreach (var node in extraction.Nodes)
                graph.AddNode(node);
            pending.AddRange(extraction.Edges);
        }
        foreach (var edge in pending)
            if (graph.Nodes.ContainsKey(edge.Source) && graph.Nodes.ContainsKey(edge.Target))
                graph.AddEdge(edge);
        return graph;
    }
}
