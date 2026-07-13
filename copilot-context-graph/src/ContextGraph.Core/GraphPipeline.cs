namespace ContextGraph.Core;

public sealed record BuildResult(KnowledgeGraph Graph, LoadedSolution Loaded, string OutDir);

/// <summary>The end-to-end build: load → extract → build. Export stages attach in later milestones.</summary>
public static class GraphPipeline
{
    public const string DefaultOutDir = "context-graph-out";

    public static async Task<BuildResult> BuildAsync(string path, string? outDir = null, CancellationToken ct = default)
    {
        var loaded = await SolutionLoader.LoadAsync(path, ct);
        var extractor = new RoslynExtractor(loaded.RootDir);

        var extractions = new List<Extraction>();
        foreach (var project in loaded.CSharpProjects)
            foreach (var document in project.Documents)
                extractions.Add(await extractor.ExtractAsync(document, ct));

        var graph = GraphBuilder.Build(extractions);
        return new BuildResult(graph, loaded, outDir ?? Path.Combine(loaded.RootDir, DefaultOutDir));
    }
}
