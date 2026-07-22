using Grapht.Core;

namespace Grapht.Tests;

public class MarkdownExtractionTests : IClassFixture<BuiltGraphFixture>
{
    private const string Doc = "docs/architecture.md";

    private readonly KnowledgeGraph _graph;

    public MarkdownExtractionTests(BuiltGraphFixture fixture) => _graph = fixture.Graph;

    [Fact]
    public void HeadingsBecomeSectionNodes()
    {
        Assert.Equal("doc_section", _graph.Nodes[Doc + "#messaging"].Type);
        Assert.Contains(_graph.Edges, e => e.Source == Doc && e.Target == Doc + "#messaging" && e.Relation == Relation.Contains);
    }

    [Fact]
    public void DocLinksBecomeDocToDocEdges()
    {
        Assert.Contains(_graph.Edges, e => e.Source == Doc && e.Target == "docs/messaging.md" && e.Relation == Relation.Imports);
    }

    [Fact]
    public void BacktickedIdentifiersResolveToCodeNodes()
    {
        // `OrderService` in prose → the actual class node, honestly INFERRED.
        Assert.Contains(_graph.Edges, e =>
            e.Source == Doc && e.Target == "SampleApp.Services.OrderService" &&
            e.Relation == Relation.References && e.Confidence == Confidence.Inferred);
        // Generic mention `IRepository<Order>` resolves via its short name.
        Assert.Contains(_graph.Edges, e =>
            e.Source == Doc && e.Target == "SampleApp.Domain.IRepository<T>" && e.Relation == Relation.References);
    }

    [Fact]
    public void DocsAnswerWhereIsThisDocumented()
    {
        var engine = new QueryEngine(_graph);
        var answer = engine.Explain("SampleApp.Services.OrderService");
        Assert.Contains("architecture.md", answer);
    }
}
