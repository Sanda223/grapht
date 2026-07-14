using System.CommandLine;
using ContextGraph.Core;

var pathArg = new Argument<string>("path") { Description = "Directory, .sln/.slnx, or .csproj", DefaultValueFactory = _ => "." };

var outOption = new Option<string?>("--out") { Description = "Output directory (default: <root>/context-graph-out)" };

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

    foreach (var project in result.Loaded.CSharpProjects)
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
        Console.Error.WriteLine($"No graph at {graphPath} — run `context-graph build` first.");
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

var root = new RootCommand("context-graph: knowledge graph for .NET solutions");
root.Subcommands.Add(buildCmd);
root.Subcommands.Add(queryCmd);
root.Subcommands.Add(pathCmd);
root.Subcommands.Add(explainCmd);
root.Subcommands.Add(statsCmd);
return await root.Parse(args).InvokeAsync();
