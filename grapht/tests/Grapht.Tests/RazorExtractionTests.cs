using Grapht.Core;

namespace Grapht.Tests;

public class RazorExtractionTests : IClassFixture<BuiltGraphFixture>
{
    private const string OrdersPage = "src/SampleApp.Web/Pages/Orders.razor";
    private const string OrderCard = "src/SampleApp.Web/Components/OrderCard.razor";

    private readonly KnowledgeGraph _graph;

    public RazorExtractionTests(BuiltGraphFixture fixture) => _graph = fixture.Graph;

    [Fact]
    public void ComponentTagResolvesToComponentFile()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == OrdersPage && e.Target == OrderCard &&
            e.Relation == Relation.Calls && e.Confidence == Confidence.Extracted);
    }

    [Fact]
    public void InjectResolvesToDeclaredServiceType()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == OrdersPage && e.Target == "SampleApp.Services.OrderService" &&
            e.Relation == Relation.Imports);
    }

    [Fact]
    public void CallThroughInjectedServiceIsInferred()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == OrdersPage && e.Target == "SampleApp.Services.OrderService.Lookup" &&
            e.Relation == Relation.Calls && e.Confidence == Confidence.Inferred);
    }

    [Fact]
    public void PageDirectiveCreatesRouteNode()
    {
        Assert.Equal("concept", _graph.Nodes["route:/orders"].FileType);
        Assert.Contains(_graph.Edges, e => e.Source == OrdersPage && e.Target == "route:/orders");
    }

    [Fact]
    public void JsInteropCreatesJsFunctionNode()
    {
        Assert.Equal("js_function", _graph.Nodes["js:highlightCard"].Type);
        Assert.Contains(_graph.Edges, e =>
            e.Source == OrderCard && e.Target == "js:highlightCard" &&
            e.Relation == Relation.Calls && e.Confidence == Confidence.Inferred);
    }

    [Fact]
    public void CodeBlockMethodsBecomeNodes()
    {
        Assert.True(_graph.Nodes.ContainsKey(OrderCard + "::Highlight"));
        Assert.Contains(_graph.Edges, e =>
            e.Source == OrderCard && e.Target == OrderCard + "::Highlight" && e.Relation == Relation.Contains);
    }
}
