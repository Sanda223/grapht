using Grapht.Core;

namespace Grapht.Tests;

/// <summary>Tier-2 seams: JS interop (both directions), HTTP surface, SignalR.</summary>
public class BoundaryExtractionTests : IClassFixture<BuiltGraphFixture>
{
    private readonly KnowledgeGraph _graph;

    public BoundaryExtractionTests(BuiltGraphFixture fixture) => _graph = fixture.Graph;

    [Fact]
    public void JsFunctionDefinitionsGetRealSourceLocations()
    {
        var node = _graph.Nodes["js:highlightCard"];
        Assert.Equal("src/SampleApp.Web/wwwroot/js/site.js", node.SourceFile);
        Assert.True(_graph.Nodes.ContainsKey("js:dismissCard")); // arrow-function form
    }

    [Fact]
    public void DotNetInvokeResolvesToJsInvokableMethod()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == "src/SampleApp.Web/wwwroot/js/site.js" &&
            e.Target == "SampleApp.Web.Interop.RefreshOrders" &&
            e.Relation == Relation.Calls);
    }

    [Fact]
    public void AttributeRoutesBecomeRouteNodes()
    {
        Assert.Equal("GET", _graph.Nodes["route:/api/orders/{id}"].Metadata?["verb"]);
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Web.Api.OrdersController.GetOrder" &&
            e.Target == "route:/api/orders/{id}" && e.Context == "http_route");
    }

    [Fact]
    public void HttpClientCallSharesRouteNodeWithController()
    {
        // Client GetAsync("api/orders") and [HttpPost] on the controller meet at one node.
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Web.Api.OrdersApiClient.FetchOrders" &&
            e.Target == "route:/api/orders" && e.Confidence == Confidence.Inferred);
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Web.Api.OrdersController.CreateOrder" &&
            e.Target == "route:/api/orders" && e.Confidence == Confidence.Extracted);
    }

    [Fact]
    public void SignalREventJoinsPublisherAndConsumer()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Web.Api.OrderHub.BroadcastOrder" &&
            e.Target == "event:OrderChanged" && e.Relation == Relation.Publishes);
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Web.Api.OrderHubClient.Subscribe" &&
            e.Target == "event:OrderChanged" && e.Relation == Relation.Consumes);
    }

    [Fact]
    public void UserMethodNamedLikeHttpVerbKeepsItsCallEdgeAndMintsNoRoute()
    {
        // cache.GetAsync("user:current") — real call edge preserved, no phantom route node.
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Services.CacheReader.CurrentUser" &&
            e.Target == "SampleApp.Services.CacheStub.GetAsync" &&
            e.Relation == Relation.Calls && e.Confidence == Confidence.Extracted);
        Assert.False(_graph.Nodes.ContainsKey("route:/user:current"));
    }

    [Fact]
    public void ExactNameBeatsFuzzyDegreeRanking()
    {
        // "Total" must resolve to Total(), not the higher-degree prefix match Totals().
        var engine = new QueryEngine(_graph);
        Assert.Contains("ID:        SampleApp.Domain.Order.Total", engine.Explain("Total"));
    }

    [Fact]
    public void HubConnectionInvokeResolvesToHubMethodNotJsInterop()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Web.Api.OrderHubClient.Announce" &&
            e.Target == "SampleApp.Web.Api.OrderHub.BroadcastOrder" &&
            e.Relation == Relation.Calls);
        Assert.False(_graph.Nodes.ContainsKey("js:BroadcastOrder"));
    }
}
