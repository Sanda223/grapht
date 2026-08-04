using Grapht.Core;

namespace Grapht.Tests;

public class RoslynExtractionTests : IClassFixture<BuiltGraphFixture>
{
    private readonly KnowledgeGraph _graph;

    public RoslynExtractionTests(BuiltGraphFixture fixture) => _graph = fixture.Graph;

    [Fact]
    public void DeclaredTypesBecomeNodes()
    {
        Assert.Equal("class", _graph.Nodes["SampleApp.Domain.Order"].Type);
        Assert.Equal("interface", _graph.Nodes["SampleApp.Domain.IRepository<T>"].Type);
        Assert.Equal("src/SampleApp.Domain/Order.cs", _graph.Nodes["SampleApp.Domain.Order"].SourceFile);
    }

    [Fact]
    public void BclTypesAreExcluded()
    {
        Assert.DoesNotContain(_graph.Nodes.Keys, id => id.StartsWith("System."));
        Assert.DoesNotContain(_graph.Nodes.Values, n => n.Label is "List" or "Dictionary" or "Math");
    }

    [Fact]
    public void CrossProjectCallsResolveSemantically()
    {
        var calls = _graph.Edges.Where(e => e.Relation == Relation.Calls).ToList();
        Assert.Contains(calls, e =>
            e.Source == "SampleApp.Services.OrderService.PlaceOrder" &&
            e.Target == "SampleApp.Domain.Order.Total" &&
            e.Confidence == Confidence.Extracted);
        Assert.Contains(calls, e =>
            e.Source == "SampleApp.Services.OrderService.Lookup" &&
            e.Target == "SampleApp.Domain.IRepository<T>.Find");
    }

    [Fact]
    public void InterfaceAndImplementationMethodsAreDistinctNodes()
    {
        Assert.True(_graph.Nodes.ContainsKey("SampleApp.Domain.IRepository<T>.Find"));
        Assert.True(_graph.Nodes.ContainsKey("SampleApp.Domain.OrderRepository.Find"));
    }

    [Fact]
    public void ImplementingMethodLinksToInterfaceMethod()
    {
        // Without this edge, impact analysis on an interface method misses its implementations.
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Domain.OrderRepository.Find" &&
            e.Target == "SampleApp.Domain.IRepository<T>.Find" &&
            e.Relation == Relation.Implements && e.Confidence == Confidence.Extracted);
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Domain.OrderRepository.Save" &&
            e.Target == "SampleApp.Domain.IRepository<T>.Save" &&
            e.Relation == Relation.Implements);
    }

    [Fact]
    public void ImplementsAndFieldReferenceEdgesExist()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Domain.OrderRepository" &&
            e.Target == "SampleApp.Domain.IRepository<T>" &&
            e.Relation == Relation.Implements);
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Services.OrderService" &&
            e.Target == "SampleApp.Domain.IRepository<T>" &&
            e.Relation == Relation.References && e.Context == "field");
    }

    [Fact]
    public void ExternalImportsAreDropped()
    {
        // `using SampleApp.Domain;` survives; `using System;` targets no declared node.
        var imports = _graph.Edges.Where(e => e.Relation == Relation.Imports).ToList();
        Assert.Contains(imports, e => e.Target == "ns:SampleApp.Domain");
        Assert.DoesNotContain(imports, e => e.Target.StartsWith("ns:System"));
    }

    [Fact]
    public void LegacyProjectStillYieldsIntraClassEdges()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Legacy.LegacyBilling.CalculateInvoice" &&
            e.Target == "SampleApp.Legacy.LegacyBilling.ComputeTax" &&
            e.Relation == Relation.Calls);
    }
}

/// <summary>Builds the fixture graph once for all extraction tests (solution load is the slow part).</summary>
public sealed class BuiltGraphFixture : IAsyncLifetime
{
    public KnowledgeGraph Graph { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var result = await GraphPipeline.BuildAsync(TestPaths.SampleAppSolution);
        Graph = result.Graph;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
