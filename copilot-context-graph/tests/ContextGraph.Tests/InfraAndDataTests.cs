using ContextGraph.Core;

namespace ContextGraph.Tests;

/// <summary>Tier-3 layers: SQL schema + Dapper scan, appsettings + config reads, infra files.</summary>
public class InfraAndDataTests : IClassFixture<BuiltGraphFixture>
{
    private readonly KnowledgeGraph _graph;

    public InfraAndDataTests(BuiltGraphFixture fixture) => _graph = fixture.Graph;

    [Fact]
    public void MigrationObjectsAndDapperCallsMeetAtOneNode()
    {
        // Schema declared in the migration…
        Assert.Contains(_graph.Edges, e =>
            e.Source == "db/migrations/001_create_orders.sql" && e.Target == "table:orders" && e.Relation == Relation.Contains);
        // …and referenced from the SQL literal in C#, normalized to the same node.
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Services.OrderQueries.Totals" && e.Target == "table:orders" &&
            e.Relation == Relation.Uses && e.Confidence == Confidence.Inferred);
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Services.OrderQueries.RecalculateAll" && e.Target == "proc:getordertotals" &&
            e.Relation == Relation.Calls);
    }

    [Fact]
    public void InterpolatedSqlStringsAreMinedToo()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Services.OrderQueries.TotalsAbove" && e.Target == "table:orders" &&
            e.Relation == Relation.Uses && e.Confidence == Confidence.Inferred);
    }

    [Fact]
    public void ConfigKeysAndReadsConnect()
    {
        // Indexer, GetConnectionString, and GetSection forms all resolve to declared keys.
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Web.FeatureFlags.HighlightEnabled" && e.Target == "config:Features:EnableHighlight" && e.Context == "config");
        Assert.Contains(_graph.Edges, e =>
            e.Source == "SampleApp.Web.FeatureFlags.HighlightEnabled" && e.Target == "config:ConnectionStrings:Default");
        // A key nothing reads still exists — the dead-setting signal.
        Assert.Equal(0, _graph.InEdges("config:Features:UnusedFlag").Count(e => e.Context == "config"));
        Assert.True(_graph.Nodes.ContainsKey("config:Features:UnusedFlag"));
    }

    [Fact]
    public void BicepResourcesAndModuleLinksExtract()
    {
        Assert.Equal("Microsoft.Web/serverfarms@2023-12-01", _graph.Nodes["infra:appPlan"].Metadata?["resource_type"]);
        Assert.Contains(_graph.Edges, e =>
            e.Source == "infra/main.bicep" && e.Target == "infra/modules/sql.bicep" && e.Relation == Relation.Imports);
    }

    [Fact]
    public void ComposeServicesAndDependenciesExtract()
    {
        Assert.True(_graph.Nodes.ContainsKey("service:web"));
        Assert.Contains(_graph.Edges, e =>
            e.Source == "service:web" && e.Target == "service:db" && e.Relation == Relation.References);
    }

    [Fact]
    public void PipelineScriptReferencesExtract()
    {
        Assert.Contains(_graph.Edges, e =>
            e.Source == "azure-pipelines.yml" && e.Target == "script:deploy.ps1" && e.Relation == Relation.Calls);
    }
}
