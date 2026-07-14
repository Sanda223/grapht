using ContextGraph.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// stdio transport: stdout is the protocol channel, so all logging goes to stderr.
var solutionPath = ".";
for (var i = 0; i < args.Length - 1; i++)
    if (args[i] == "--solution")
        solutionPath = args[i + 1];

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(new GraphService(solutionPath));
builder.Services.AddHostedService<GraphInitializer>();
builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "context-graph", Version = "0.1.0" };
        o.ServerInstructions =
            "This server is an INDEX of the codebase, not an answer source. Its tools return " +
            "graph structure (what connects to what) with file:line references. Always follow up " +
            "by reading the referenced files — behavior (ordering, conditions, transactions) lives " +
            "only in the code. Workflow: graph tool -> open the file:line results -> answer from code.";
    })
    .WithStdioServerTransport()
    .WithTools<GraphTools>();

await builder.Build().RunAsync();

/// <summary>Builds the graph in the background at startup so the MCP handshake is instant
/// and the first tool call finds the graph ready (or nearly so).</summary>
sealed class GraphInitializer : BackgroundService
{
    private readonly GraphService _service;
    private readonly ILogger<GraphInitializer> _logger;

    public GraphInitializer(GraphService service, ILogger<GraphInitializer> logger)
    {
        _service = service;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("context-graph: building graph…");
            await _service.InitializeAsync(stoppingToken);
            _logger.LogInformation("context-graph: ready ({Nodes} nodes)", _service.Current.Graph.NodeCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "context-graph: initial build failed");
        }
    }
}
