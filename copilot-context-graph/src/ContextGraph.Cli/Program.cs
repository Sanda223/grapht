using System.CommandLine;
using ContextGraph.Core;

var pathArg = new Argument<string>("path") { Description = "Directory, .sln/.slnx, or .csproj", DefaultValueFactory = _ => "." };

var outOption = new Option<string?>("--out") { Description = "Output directory (default: <root>/context-graph-out)" };

var buildCmd = new Command("build", "Build the knowledge graph for a solution");
buildCmd.Arguments.Add(pathArg);
buildCmd.Options.Add(outOption);
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

    var graphPath = Path.Combine(result.OutDir, "graph.json");
    JsonExporter.Write(result.Graph, graphPath, GitCommit(result.Loaded.RootDir));
    Console.WriteLine($"\n{result.Graph.NodeCount} nodes, {result.Graph.EdgeCount} edges -> {graphPath}");
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

var root = new RootCommand("context-graph: knowledge graph for .NET solutions");
root.Subcommands.Add(buildCmd);
return await root.Parse(args).InvokeAsync();
