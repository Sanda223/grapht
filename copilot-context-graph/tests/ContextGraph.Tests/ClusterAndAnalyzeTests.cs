using ContextGraph.Core;

namespace ContextGraph.Tests;

public class ClusterAndAnalyzeTests : IClassFixture<BuiltGraphFixture>
{
    private readonly KnowledgeGraph _graph;

    public ClusterAndAnalyzeTests(BuiltGraphFixture fixture) => _graph = fixture.Graph;

    [Fact]
    public void EveryNodeGetsACommunity()
    {
        var communities = Louvain.Cluster(_graph);
        Assert.All(_graph.Nodes.Values, n => Assert.NotNull(n.Community));
        Assert.Equal(_graph.NodeCount, communities.Values.Sum(m => m.Count));
    }

    [Fact]
    public void ClusteringIsDeterministic()
    {
        var first = Louvain.Cluster(_graph).ToDictionary(kv => kv.Key, kv => string.Join(",", kv.Value));
        var second = Louvain.Cluster(_graph).ToDictionary(kv => kv.Key, kv => string.Join(",", kv.Value));
        Assert.Equal(first, second);
    }

    [Fact]
    public void ConnectedSiblingsShareACommunityAndLegacyIsSeparate()
    {
        Louvain.Cluster(_graph);
        // Order/OrderLine share a file and are directly linked; LegacyBilling touches
        // nothing outside its own project so it must never share their community.
        Assert.Equal(_graph.Nodes["SampleApp.Domain.Order"].Community, _graph.Nodes["SampleApp.Domain.OrderLine"].Community);
        Assert.NotEqual(_graph.Nodes["SampleApp.Domain.Order"].Community, _graph.Nodes["SampleApp.Legacy.LegacyBilling"].Community);
    }

    [Fact]
    public void GodNodesExcludeFilesAndNamespaces()
    {
        var gods = Analyzer.GodNodes(_graph);
        Assert.NotEmpty(gods);
        Assert.All(gods, g => Assert.False(_graph.Nodes[g.Id].Type is "file" or "namespace"));
        // Order is the domain hub of the fixture.
        Assert.Contains(gods.Take(3), g => g.Label == "Order");
    }

    [Fact]
    public void ReportContainsAllSections()
    {
        var communities = Louvain.Cluster(_graph);
        var labels = Louvain.LabelCommunities(_graph, communities);
        var analysis = Analyzer.Analyze(_graph, communities, labels);
        var report = ReportWriter.Render(_graph, analysis, ["some diagnostic"]);

        Assert.Contains("# Graph Report", report);
        Assert.Contains("## Communities", report);
        Assert.Contains("## Most connected", report);
        Assert.Contains("## Suggested questions", report);
        Assert.Contains("## Load diagnostics", report);
    }

    [Fact]
    public void OversizedGraphFallsBackToCommunityMetaGraph()
    {
        var communities = Louvain.Cluster(_graph);
        var labels = Louvain.LabelCommunities(_graph, communities);
        var analysis = Analyzer.Analyze(_graph, communities, labels);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ctxgraph-meta-" + Guid.NewGuid().ToString("N") + ".html");
        try
        {
            HtmlExporter.Write(_graph, analysis, path, maxNodes: 5); // force the fallback
            var html = File.ReadAllText(path);
            Assert.Contains("community:", html);       // aggregated nodes
            Assert.Contains(" links", html);           // weighted inter-community edges
            Assert.DoesNotContain("too large", html);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HtmlExportIsSelfContained()
    {
        var communities = Louvain.Cluster(_graph);
        var labels = Louvain.LabelCommunities(_graph, communities);
        var analysis = Analyzer.Analyze(_graph, communities, labels);
        var path = Path.Combine(Path.GetTempPath(), "ctxgraph-html-" + Guid.NewGuid().ToString("N") + ".html");
        try
        {
            HtmlExporter.Write(_graph, analysis, path);
            var html = File.ReadAllText(path);
            Assert.Contains("vis-network", html);           // vendored lib embedded
            Assert.DoesNotMatch("(src|href)=\"http", html); // nothing loads from the network
            Assert.Contains("const DATA =", html);           // graph data inline
        }
        finally
        {
            File.Delete(path);
        }
    }
}
