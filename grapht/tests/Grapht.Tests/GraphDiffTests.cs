using Grapht.Core;

namespace Grapht.Tests;

public class GraphDiffTests
{
    [Fact]
    public void DiffFindsAddedAndRemovedNodesAndEdges()
    {
        var baseGraph = Small();
        var headGraph = Small();
        headGraph.AddNode(new Node { Id = "C", Label = "C", Type = "class", Community = 1 });
        headGraph.AddEdge(new Edge { Source = "A", Target = "C", Relation = Relation.Calls });
        headGraph.RemoveNode("B");

        var diff = GraphDiff.Compute(baseGraph, headGraph);
        Assert.Equal(["C"], diff.AddedNodes.Select(n => n.Id));
        Assert.Equal(["B"], diff.RemovedNodes.Select(n => n.Id));
        Assert.Contains(diff.AddedEdges, e => e.Target == "C");
        Assert.Contains(diff.RemovedEdges, e => e.Target == "B");
    }

    [Fact]
    public void MarkdownFlagsCrossCommunityDependencies()
    {
        var baseGraph = Small();
        var headGraph = Small();
        headGraph.AddNode(new Node { Id = "C", Label = "C", Type = "class", Community = 9 });
        headGraph.AddEdge(new Edge { Source = "A", Target = "C", Relation = Relation.Calls, SourceFile = "a.cs" });

        var markdown = GraphDiff.RenderMarkdown(GraphDiff.Compute(baseGraph, headGraph), headGraph);
        Assert.Contains("New cross-community dependencies", markdown);
        Assert.Contains("`A` --calls--> `C`", markdown);
    }

    [Fact]
    public void IdenticalGraphsDiffToNoChanges()
    {
        var markdown = GraphDiff.RenderMarkdown(GraphDiff.Compute(Small(), Small()), Small());
        Assert.Contains("No structural changes", markdown);
    }

    private static KnowledgeGraph Small()
    {
        var graph = new KnowledgeGraph();
        graph.AddNode(new Node { Id = "A", Label = "A", Type = "class", Community = 0 });
        graph.AddNode(new Node { Id = "B", Label = "B", Type = "class", Community = 0 });
        graph.AddEdge(new Edge { Source = "A", Target = "B", Relation = Relation.Calls });
        return graph;
    }
}
