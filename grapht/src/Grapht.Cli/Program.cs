using System.CommandLine;
using Grapht.Core;
using Grapht.View;

var pathArg = new Argument<string>("path") { Description = "Directory, .sln/.slnx, or .csproj", DefaultValueFactory = _ => "." };

var outOption = new Option<string?>("--out") { Description = "Output directory (default: <root>/grapht-out)" };

var noHtmlOption = new Option<bool>("--no-html") { Description = "Skip graph.html generation" };

var buildCmd = new Command("build", "Build the knowledge graph for a solution");
buildCmd.Arguments.Add(pathArg);
buildCmd.Options.Add(outOption);
buildCmd.Options.Add(noHtmlOption);
buildCmd.SetAction(async (parseResult, ct) =>
{
    var path = parseResult.GetValue(pathArg)!;
    Console.WriteLine($"Loading {path} ...");
    var result = await GraphPipeline.BuildAsync(path, parseResult.GetValue(outOption), ct);

    foreach (var project in result.Loaded.DistinctProjects)
        Console.WriteLine($"  project {project.Name}: {project.Documents.Count()} documents");
    Console.WriteLine($"  razor files: {result.Loaded.RazorFiles.Count}");

    if (result.Loaded.Diagnostics.Count > 0)
    {
        Console.WriteLine($"\n{result.Loaded.Diagnostics.Count} load diagnostics:");
        foreach (var diagnostic in result.Loaded.Diagnostics.Take(20))
            Console.WriteLine($"  {diagnostic}");
    }

    var (graphJson, report, html) = GraphPipeline.ExportAll(result, GitCommit(result.Loaded.RootDir), html: !parseResult.GetValue(noHtmlOption));
    Console.WriteLine($"\n{result.Graph.NodeCount} nodes, {result.Graph.EdgeCount} edges, {result.Analysis.Communities.Count} communities");
    Console.WriteLine($"  {graphJson}");
    Console.WriteLine($"  {report}");
    if (!parseResult.GetValue(noHtmlOption))
        Console.WriteLine($"  {html}");
    return 0;
});

static string GitCommit(string rootDir)
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
        {
            WorkingDirectory = rootDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(5000);
        return process.ExitCode == 0 ? output : "";
    }
    catch
    {
        return "";
    }
}

// --- query verbs (operate on an existing graph.json) ---

var graphOption = new Option<string>("--graph") { Description = "Path to graph.json", DefaultValueFactory = _ => Path.Combine(GraphPipeline.DefaultOutDir, "graph.json") };

QueryEngine LoadEngine(string graphPath)
{
    if (!File.Exists(graphPath))
    {
        Console.Error.WriteLine($"No graph at {graphPath} — run `grapht build` first.");
        Environment.Exit(1);
    }
    return new QueryEngine(JsonExporter.Read(graphPath));
}

var questionArg = new Argument<string>("question") { Description = "Plain-language question" };
var dfsOption = new Option<bool>("--dfs") { Description = "Depth-first traversal (trace a specific path)" };
var depthOption = new Option<int>("--depth") { Description = "Traversal depth", DefaultValueFactory = _ => 2 };
var budgetOption = new Option<int>("--budget") { Description = "Answer token budget", DefaultValueFactory = _ => 700 };

var queryCmd = new Command("query", "Traverse the graph from the concepts in a question");
queryCmd.Arguments.Add(questionArg);
queryCmd.Options.Add(graphOption);
queryCmd.Options.Add(dfsOption);
queryCmd.Options.Add(depthOption);
queryCmd.Options.Add(budgetOption);
queryCmd.SetAction(parseResult =>
{
    var engine = LoadEngine(parseResult.GetValue(graphOption)!);
    Console.WriteLine(engine.Query(
        parseResult.GetValue(questionArg)!,
        dfs: parseResult.GetValue(dfsOption),
        depth: parseResult.GetValue(depthOption),
        tokenBudget: parseResult.GetValue(budgetOption)));
    return 0;
});

var sourceArg = new Argument<string>("source") { Description = "Start concept" };
var targetArg = new Argument<string>("target") { Description = "End concept" };
var pathCmd = new Command("path", "Shortest path between two concepts");
pathCmd.Arguments.Add(sourceArg);
pathCmd.Arguments.Add(targetArg);
pathCmd.Options.Add(graphOption);
pathCmd.SetAction(parseResult =>
{
    var engine = LoadEngine(parseResult.GetValue(graphOption)!);
    Console.WriteLine(engine.Path(parseResult.GetValue(sourceArg)!, parseResult.GetValue(targetArg)!));
    return 0;
});

var labelArg = new Argument<string>("label") { Description = "Node label or id" };
var explainCmd = new Command("explain", "Show a node and all its connections");
explainCmd.Arguments.Add(labelArg);
explainCmd.Options.Add(graphOption);
explainCmd.SetAction(parseResult =>
{
    var engine = LoadEngine(parseResult.GetValue(graphOption)!);
    Console.WriteLine(engine.Explain(parseResult.GetValue(labelArg)!));
    return 0;
});

var statsCmd = new Command("stats", "Graph size and confidence breakdown");
statsCmd.Options.Add(graphOption);
statsCmd.SetAction(parseResult =>
{
    var engine = LoadEngine(parseResult.GetValue(graphOption)!);
    Console.WriteLine(engine.Stats());
    return 0;
});

var logOption = new Option<string>("--log") { Description = "Path to querylog.jsonl", DefaultValueFactory = _ => Path.Combine(GraphPipeline.DefaultOutDir, "querylog.jsonl") };
var usageCmd = new Command("report-usage", "Summarize MCP tool usage from the query log");
usageCmd.Options.Add(logOption);
usageCmd.SetAction(parseResult =>
{
    var logPath = parseResult.GetValue(logOption)!;
    if (!File.Exists(logPath))
    {
        Console.Error.WriteLine($"No query log at {logPath} — the MCP server writes it as tools get used.");
        return 1;
    }
    var byTool = new Dictionary<string, (int Calls, long Chars)>();
    DateTimeOffset? first = null, last = null;
    foreach (var line in File.ReadLines(logPath))
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            var tool = doc.RootElement.GetProperty("tool").GetString()!;
            var chars = doc.RootElement.GetProperty("result_chars").GetInt64();
            var ts = DateTimeOffset.Parse(doc.RootElement.GetProperty("ts").GetString()!);
            var (calls, total) = byTool.GetValueOrDefault(tool);
            byTool[tool] = (calls + 1, total + chars);
            first = first is null || ts < first ? ts : first;
            last = last is null || ts > last ? ts : last;
        }
        catch (Exception)
        {
            // skip malformed lines
        }
    }
    Console.WriteLine($"Query log: {byTool.Values.Sum(v => v.Calls)} calls, {first:yyyy-MM-dd} .. {last:yyyy-MM-dd}\n");
    Console.WriteLine($"{"tool",-18} {"calls",6} {"avg result chars",18}");
    foreach (var (tool, (calls, chars)) in byTool.OrderByDescending(kv => kv.Value.Calls))
        Console.WriteLine($"{tool,-18} {calls,6} {chars / Math.Max(1, calls),18:N0}");
    return 0;
});

var baseArg = new Argument<string>("base") { Description = "Base graph.json (e.g. from the main branch)" };
var headArg = new Argument<string>("head") { Description = "Head graph.json (e.g. from the PR branch)" };
var diffCmd = new Command("diff", "Structural diff between two graph builds (markdown, PR-comment ready)");
diffCmd.Arguments.Add(baseArg);
diffCmd.Arguments.Add(headArg);
diffCmd.SetAction(parseResult =>
{
    var baseGraph = JsonExporter.Read(parseResult.GetValue(baseArg)!);
    var headGraph = JsonExporter.Read(parseResult.GetValue(headArg)!);
    Console.WriteLine(GraphDiff.RenderMarkdown(GraphDiff.Compute(baseGraph, headGraph), headGraph));
    return 0;
});

var viewPathArg = new Argument<string>("path") { Description = "Solution or directory to view", DefaultValueFactory = _ => "." };
var portOption = new Option<int>("--port") { Description = "Port to serve on", DefaultValueFactory = _ => 5050 };
var noOpenOption = new Option<bool>("--no-open") { Description = "Do not open a browser" };
var viewCmd = new Command("view", "Serve an interactive graph viewer on localhost");
viewCmd.Arguments.Add(viewPathArg);
viewCmd.Options.Add(portOption);
viewCmd.Options.Add(noOpenOption);
viewCmd.SetAction(async (parseResult, ct) =>
{
    await ViewServer.RunAsync(
        parseResult.GetValue(viewPathArg)!,
        parseResult.GetValue(portOption),
        !parseResult.GetValue(noOpenOption),
        ct);
    return 0;
});

var root = new RootCommand("grapht: knowledge graph for .NET solutions");
root.Subcommands.Add(buildCmd);
root.Subcommands.Add(queryCmd);
root.Subcommands.Add(pathCmd);
root.Subcommands.Add(explainCmd);
root.Subcommands.Add(statsCmd);
root.Subcommands.Add(usageCmd);
root.Subcommands.Add(diffCmd);
root.Subcommands.Add(viewCmd);
return await root.Parse(args).InvokeAsync();
