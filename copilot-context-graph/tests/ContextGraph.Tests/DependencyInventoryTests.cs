using ContextGraph.Core;

namespace ContextGraph.Tests;

/// <summary>Tier-1 extensions: NuGet package inventory, DI bindings, MassTransit message flow.</summary>
public class DependencyInventoryTests : IClassFixture<BuiltGraphFixture>
{
    private readonly KnowledgeGraph _graph;

    public DependencyInventoryTests(BuiltGraphFixture fixture) => _graph = fixture.Graph;

    [Fact]
    public void PackageReferencesBecomeUsesEdgesWithVersion()
    {
        Assert.Equal("package", _graph.Nodes["pkg:Microsoft.AspNetCore.Components.Web"].Type);
        var uses = _graph.Edges.Single(e =>
            e.Source == "project:SampleApp.Web" && e.Target == "pkg:Microsoft.AspNetCore.Components.Web" && e.Relation == Relation.Uses);
        Assert.Equal("10.0.0", uses.Metadata?["version"]);
    }

    [Fact]
    public void ProjectReferencesBecomeEdges()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == "project:SampleApp.Web" && e.Target == "project:SampleApp.Services" && e.Relation == Relation.References);
        Assert.Contains(_graph.Edges, e =>
            e.Source == "project:SampleApp.Services" && e.Target == "project:SampleApp.Domain" && e.Relation == Relation.References);
    }

    [Fact]
    public void DiRegistrationCreatesBoundToEdge()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Domain.IRepository<T>" && e.Target == "SampleApp.Domain.OrderRepository" &&
            e.Relation == Relation.BoundTo && e.Confidence == Confidence.Extracted);
    }

    [Fact]
    public void ConsumerDeclarationCreatesConsumesEdge()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Services.OrderPlacedConsumer" && e.Target == "SampleApp.Services.OrderPlacedMessage" &&
            e.Relation == Relation.Consumes);
    }

    [Fact]
    public void PublishCallCreatesPublishesEdge()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Services.OrderPublisher.NotifyPlaced" && e.Target == "SampleApp.Services.OrderPlacedMessage" &&
            e.Relation == Relation.Publishes);
    }

    [Fact]
    public void MessageContractConnectsPublisherAndConsumerDirectly()
    {
        // The message node is the join point: one hop to the publisher, one to the consumer.
        var message = "SampleApp.Services.OrderPlacedMessage";
        Assert.Equal(2, _graph.ShortestPath("SampleApp.Services.OrderPublisher.NotifyPlaced", message)!.Count); // [source, message]
        Assert.Equal(2, _graph.ShortestPath(message, "SampleApp.Services.OrderPlacedConsumer")!.Count);
    }
}
