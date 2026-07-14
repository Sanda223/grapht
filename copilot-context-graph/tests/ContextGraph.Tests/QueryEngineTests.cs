using ContextGraph.Core;

namespace ContextGraph.Tests;

public class QueryEngineTests : IClassFixture<BuiltGraphFixture>
{
    private readonly QueryEngine _engine;

    public QueryEngineTests(BuiltGraphFixture fixture)
    {
        Louvain.Cluster(fixture.Graph);
        _engine = new QueryEngine(fixture.Graph);
    }

    [Fact]
    public void QuerySeedsOnRelevantNodesAndListsEdges()
    {
        var answer = _engine.Query("how does order total get calculated");
        Assert.Contains("Start: [", answer);
        Assert.Contains("Total()", answer);
        Assert.Contains("NODE Order ", answer);
        Assert.Contains("EDGE", answer);
    }

    [Fact]
    public void QueryRespectsTokenBudget()
    {
        var answer = _engine.Query("order", tokenBudget: 100);
        Assert.True(answer.Length <= 100 * 3 + 200, $"answer too long: {answer.Length} chars");
        Assert.Contains("truncated", answer);
    }

    [Fact]
    public void QueryWithNoMatchSaysSo()
    {
        Assert.StartsWith("No node matching", _engine.Query("kubernetes ingress zebra"));
    }

    [Fact]
    public void PathTracesRazorToDomain()
    {
        var answer = _engine.Path("Orders.razor", "Total");
        Assert.StartsWith("Shortest path", answer);
        Assert.Contains("OrderService", answer);
        Assert.Contains("Total()", answer);
    }

    [Fact]
    public void PathBetweenUnconnectedNodesReportsNoPath()
    {
        // LegacyBilling touches nothing outside its own project.
        var answer = _engine.Path("LegacyBilling", "OrderCard");
        Assert.StartsWith("No path found", answer);
    }

    [Fact]
    public void ExplainShowsSourceCommunityAndConnections()
    {
        var answer = _engine.Explain("OrderService");
        Assert.Contains("ID:        SampleApp.Services.OrderService", answer);
        Assert.Contains("Source:    src/SampleApp.Services/OrderService.cs", answer);
        Assert.Contains("Connections (", answer);
        Assert.Contains("<-- Orders.razor [imports]", answer);
    }

    [Fact]
    public void AmbiguousMethodNameReturnsCandidatesInsteadOfGuessing()
    {
        // Find() exists on both IRepository<T> and OrderRepository.
        var answer = _engine.Explain("Find");
        Assert.Contains("matches 2 distinct nodes", answer);
        Assert.Contains("SampleApp.Domain.IRepository<T>.Find", answer);
        Assert.Contains("SampleApp.Domain.OrderRepository.Find", answer);
        // Requerying with the unique id resolves normally.
        Assert.Contains("Degree:", _engine.Explain("SampleApp.Domain.OrderRepository.Find"));
    }

    [Fact]
    public void ClassDoesNotCollideWithItsOwnConstructor()
    {
        Assert.Contains("ID:        SampleApp.Services.OrderService", _engine.Explain("OrderService"));
    }

    [Fact]
    public void StatsReportsCountsAndConfidence()
    {
        var stats = _engine.Stats();
        Assert.Contains("Nodes:", stats);
        Assert.Contains("EXTRACTED:", stats);
    }
}
