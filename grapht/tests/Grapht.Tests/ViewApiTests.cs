using Grapht.Core;
using Grapht.View;

namespace Grapht.Tests;

public class ViewApiTests : IClassFixture<BuiltGraphFixture>
{
    private readonly KnowledgeGraph _graph;
    private readonly Analysis _analysis;

    public ViewApiTests(BuiltGraphFixture fixture)
    {
        _graph = fixture.Graph;
        var communities = Louvain.Cluster(_graph);
        var labels = Louvain.LabelCommunities(_graph, communities);
        _analysis = Analyzer.Analyze(_graph, communities, labels);
    }

    [Fact]
    public void OverviewReturnsOneNodePerCommunityAndWeightedCrossings()
    {
        var overview = GraphApi.BuildOverview(_graph, _analysis);

        Assert.Equal(_analysis.Communities.Count, overview.Communities.Count);
        Assert.Equal(_graph.NodeCount, overview.NodeCount);
        Assert.All(overview.Communities, c => Assert.False(string.IsNullOrWhiteSpace(c.Label)));
        // every meta-edge joins two distinct communities and carries a positive weight
        Assert.All(overview.Edges, e =>
        {
            Assert.NotEqual(e.Source, e.Target);
            Assert.True(e.Weight > 0);
        });
        // deterministic ordering
        Assert.Equal(overview.Communities.Select(c => c.Id).Order(), overview.Communities.Select(c => c.Id));
    }
}
