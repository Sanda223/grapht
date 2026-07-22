using Grapht.Core;

namespace Grapht.Tests;

/// <summary>Known-answer clustering tests. The SampleApp fixture is too small to expose
/// hierarchy bugs — the self-loop-dropping regression (every level merging into one giant
/// community) only showed on a real solution, and these graphs pin it down.</summary>
public class LouvainKnownAnswerTests
{
    [Fact]
    public void TwoCliquesWithOneBridgeYieldTwoCommunities()
    {
        var graph = Cliques(count: 2, size: 8);
        graph.AddEdge(new Edge { Source = "c0n0", Target = "c1n0", Relation = "calls" });

        var communities = Louvain.Cluster(graph);
        Assert.Equal(2, communities.Count);
        Assert.All(communities.Values, members => Assert.Equal(8, members.Count));
    }

    [Fact]
    public void RingOfFourCliquesYieldsFourCommunities()
    {
        var graph = Cliques(count: 4, size: 6);
        for (var c = 0; c < 4; c++)
            graph.AddEdge(new Edge { Source = $"c{c}n0", Target = $"c{(c + 1) % 4}n0", Relation = "calls" });

        var communities = Louvain.Cluster(graph);
        Assert.Equal(4, communities.Count);
        Assert.All(communities.Values, members => Assert.Equal(6, members.Count));
    }

    [Fact]
    public void IsolatedNodesBecomeSingletonCommunities()
    {
        var graph = Cliques(count: 1, size: 4);
        graph.AddNode(new Node { Id = "lonely", Label = "lonely" });

        var communities = Louvain.Cluster(graph);
        Assert.Equal(2, communities.Count);
        Assert.Contains(communities.Values, members => members is ["lonely"]);
    }

    [Fact]
    public void RemapKeepsCommunityIdsStableWhenSizesShift()
    {
        var first = Cliques(count: 2, size: 8);
        first.AddEdge(new Edge { Source = "c0n0", Target = "c1n0", Relation = "calls" });
        Louvain.Cluster(first);
        var previous = first.Nodes.Values.ToDictionary(n => n.Id, n => n.Community!.Value);
        int c0Was = first.Nodes["c0n0"].Community!.Value, c1Was = first.Nodes["c1n0"].Community!.Value;

        // Same shape, but clique 1 grows past clique 0 — fresh clustering would swap the
        // size-ordered ids; the remap must keep them.
        var second = Cliques(count: 2, size: 8);
        second.AddEdge(new Edge { Source = "c0n0", Target = "c1n0", Relation = "calls" });
        for (var i = 0; i < 4; i++)
        {
            second.AddNode(new Node { Id = $"c1x{i}", Label = $"c1x{i}" });
            second.AddEdge(new Edge { Source = $"c1x{i}", Target = "c1n0", Relation = "calls" });
            second.AddEdge(new Edge { Source = $"c1x{i}", Target = "c1n1", Relation = "calls" });
        }
        var communities = Louvain.Cluster(second);
        Louvain.RemapToPrevious(second, communities, previous);

        Assert.Equal(c0Was, second.Nodes["c0n0"].Community);
        Assert.Equal(c1Was, second.Nodes["c1n0"].Community);
        Assert.Equal(c1Was, second.Nodes["c1x0"].Community); // newcomer joins the stable id
    }

    private static KnowledgeGraph Cliques(int count, int size)
    {
        var graph = new KnowledgeGraph();
        for (var c = 0; c < count; c++)
            for (var i = 0; i < size; i++)
            {
                graph.AddNode(new Node { Id = $"c{c}n{i}", Label = $"c{c}n{i}" });
                for (var j = 0; j < i; j++)
                    graph.AddEdge(new Edge { Source = $"c{c}n{i}", Target = $"c{c}n{j}", Relation = "calls" });
            }
        return graph;
    }
}
