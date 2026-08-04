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

    [Fact]
    public void FocusReturnsNodeWithBothEdgeDirections()
    {
        var id = _graph.Nodes.Values.OrderByDescending(n => _graph.Degree(n.Id))
                                    .ThenBy(n => n.Id, StringComparer.Ordinal).First().Id;

        var focus = GraphApi.BuildFocus(_graph, _analysis, id);

        Assert.NotNull(focus);
        Assert.Equal(id, focus!.Node.Id);
        Assert.Equal(_graph.Degree(id), focus.Node.Degree);
        Assert.NotEmpty(focus.Neighbours);
        Assert.All(focus.Neighbours, n => Assert.Contains(n.Direction, new[] { "in", "out" }));
        Assert.Equal(_graph.OutEdges(id).Count, focus.Neighbours.Count(n => n.Direction == "out"));
        Assert.Equal(_graph.InEdges(id).Count, focus.Neighbours.Count(n => n.Direction == "in"));
    }

    [Fact]
    public void FocusReturnsNullForUnknownNode()
    {
        Assert.Null(GraphApi.BuildFocus(_graph, _analysis, "no-such-node-id"));
    }

    [Fact]
    public void FocusCollapsesSelfLoopToOneOutgoingNeighbourRow()
    {
        // Fixture fact: SampleApp.Services.OrderService --bound_to--> itself. Enumerating
        // OutEdges and InEdges separately would otherwise emit this edge twice (once per
        // direction) with an identical node reference — collapse it to a single "out" row.
        const string id = "SampleApp.Services.OrderService";
        Assert.Contains(_graph.Edges, e => e.Source == id && e.Target == id);

        var focus = GraphApi.BuildFocus(_graph, _analysis, id);

        Assert.NotNull(focus);
        var selfRows = focus!.Neighbours.Where(n => n.Node.Id == id).ToList();
        var selfRow = Assert.Single(selfRows);
        Assert.Equal("out", selfRow.Direction);
    }

    [Fact]
    public void OverviewTopMemberIdsAlignWithTopMemberLabelsAndResolve()
    {
        var overview = GraphApi.BuildOverview(_graph, _analysis);

        Assert.All(overview.Communities, c =>
        {
            Assert.Equal(c.TopMembers.Count, c.TopMemberIds.Count);
            for (var i = 0; i < c.TopMemberIds.Count; i++)
            {
                Assert.True(_graph.Nodes.ContainsKey(c.TopMemberIds[i]));
                Assert.Equal(_graph.Nodes[c.TopMemberIds[i]].Label, c.TopMembers[i]);
            }
        });
    }

    [Fact]
    public void SearchRanksExactLabelMatchFirst()
    {
        // Fixture fact: SampleApp.Services.OrderService exists and its label is "OrderService".
        var hits = GraphApi.Search(_graph, _analysis, "OrderService");

        Assert.NotEmpty(hits);
        Assert.Equal("SampleApp.Services.OrderService", hits[0].Id);
        Assert.True(hits.Count <= 10);
    }

    [Fact]
    public void SearchIsCaseInsensitiveAndMatchesSubstrings()
    {
        var hits = GraphApi.Search(_graph, _analysis, "orderserv");

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.True(
            h.Label.Contains("orderserv", StringComparison.OrdinalIgnoreCase) ||
            h.Id.Contains("orderserv", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void SearchFindsMethodsWithoutTheirParenthesesSuffix()
    {
        // Method labels carry a "()" suffix; a user types the bare name.
        var method = _graph.Nodes.Values.First(n => n.Label.EndsWith("()", StringComparison.Ordinal));
        var bare = method.Label[..^2];

        var hits = GraphApi.Search(_graph, _analysis, bare);

        Assert.Contains(hits, h => h.Id == method.Id);
    }

    [Fact]
    public void SearchRespectsLimitAndRejectsBlankQueries()
    {
        Assert.Empty(GraphApi.Search(_graph, _analysis, "   "));
        Assert.Empty(GraphApi.Search(_graph, _analysis, ""));
        var hits = GraphApi.Search(_graph, _analysis, "Sample", limit: 3);
        Assert.True(hits.Count <= 3);
    }

    [Fact]
    public void FocusResolvesIdContainingPercentEncodedSequenceWithoutDoubleDecoding()
    {
        // Route values arrive at BuildFocus already decoded once by ASP.NET Core's routing
        // middleware — a node id may legitimately still contain a literal "%20" sequence.
        const string id = "route:/api/catalog/items/by/wanderer%20black%20hiking%20boots";
        const string doubleDecoded = "route:/api/catalog/items/by/wanderer black hiking boots";

        var graph = new KnowledgeGraph();
        graph.AddNode(new Node { Id = id, Label = "wanderer%20black%20hiking%20boots" });
        var analysis = new Analysis([], [], [], [], [], []);

        var focus = GraphApi.BuildFocus(graph, analysis, id);

        Assert.NotNull(focus);
        Assert.Equal(id, focus!.Node.Id);
        Assert.Null(GraphApi.BuildFocus(graph, analysis, doubleDecoded));
    }
}
