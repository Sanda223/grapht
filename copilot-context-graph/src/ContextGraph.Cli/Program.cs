using System.CommandLine;
using ContextGraph.Core;

var pathArg = new Argument<string>("path") { Description = "Directory, .sln/.slnx, or .csproj", DefaultValueFactory = _ => "." };

var buildCmd = new Command("build", "Build the knowledge graph for a solution");
buildCmd.Arguments.Add(pathArg);
buildCmd.SetAction(async (parseResult, ct) =>
{
    var path = parseResult.GetValue(pathArg)!;
    Console.WriteLine($"Loading {path} ...");
    var loaded = await SolutionLoader.LoadAsync(path, ct);

    foreach (var project in loaded.CSharpProjects)
        Console.WriteLine($"  project {project.Name}: {project.Documents.Count()} documents");
    Console.WriteLine($"  razor files: {loaded.RazorFiles.Count}");

    if (loaded.Diagnostics.Count > 0)
    {
        Console.WriteLine($"\n{loaded.Diagnostics.Count} load diagnostics:");
        foreach (var diagnostic in loaded.Diagnostics.Take(20))
            Console.WriteLine($"  {diagnostic}");
    }
    return 0;
});

var root = new RootCommand("context-graph: knowledge graph for .NET solutions");
root.Subcommands.Add(buildCmd);
return await root.Parse(args).InvokeAsync();
