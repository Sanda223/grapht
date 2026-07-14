namespace ContextGraph.Core;

public sealed record BuildResult(KnowledgeGraph Graph, Analysis Analysis, LoadedSolution Loaded, string OutDir);

/// <summary>The end-to-end build: load → extract → resolve → build → cluster → analyze.
/// ExportAll writes graph.json, GRAPH_REPORT.md, and graph.html.</summary>
public static class GraphPipeline
{
    public const string DefaultOutDir = "context-graph-out";

    public static async Task<BuildResult> BuildAsync(string path, string? outDir = null, CancellationToken ct = default)
    {
        var loaded = await SolutionLoader.LoadAsync(path, ct);
        var graph = await ExtractAsync(loaded, ct);

        var communities = Louvain.Cluster(graph);
        var resolvedOutDir = outDir ?? Path.Combine(loaded.RootDir, DefaultOutDir);
        if (PreviousAssignment(Path.Combine(resolvedOutDir, "graph.json")) is { } previous)
            communities = Louvain.RemapToPrevious(graph, communities, previous);
        var labels = Louvain.LabelCommunities(graph, communities);
        var analysis = Analyzer.Analyze(graph, communities, labels);
        return new BuildResult(graph, analysis, loaded, resolvedOutDir);
    }

    /// <summary>node id → community from the previous build, so ids stay stable.</summary>
    private static Dictionary<string, int>? PreviousAssignment(string graphJsonPath)
    {
        if (!File.Exists(graphJsonPath))
            return null;
        try
        {
            return JsonExporter.Read(graphJsonPath).Nodes.Values
                .Where(n => n.Community is not null)
                .ToDictionary(n => n.Id, n => n.Community!.Value);
        }
        catch (Exception)
        {
            return null; // corrupt/old-format previous graph: cluster fresh
        }
    }

    public static async Task<KnowledgeGraph> ExtractAsync(LoadedSolution loaded, CancellationToken ct = default)
    {
        var extractor = new RoslynExtractor(loaded.RootDir);
        var razorExtractor = new RazorExtractor(loaded.RootDir);

        var extractions = new List<Extraction>();
        foreach (var project in loaded.DistinctProjects)
            foreach (var document in project.Documents)
                extractions.Add(await extractor.ExtractAsync(document, ct));
        foreach (var razorFile in loaded.RazorFiles)
            extractions.Add(razorExtractor.Extract(razorFile));
        foreach (var extraFile in loaded.ExtraFiles)
            if (ExtraFileExtractor.Extract(extraFile, loaded.RootDir) is { } extraction)
                extractions.Add(extraction);
        extractions.AddRange(CsprojExtractor.ExtractAll(loaded));

        RazorResolver.Resolve(extractions);
        return GraphBuilder.Build(extractions);
    }

    public static (string GraphJson, string Report, string Html) ExportAll(BuildResult result, string builtAtCommit = "", bool html = true)
    {
        var graphJson = Path.Combine(result.OutDir, "graph.json");
        var reportPath = Path.Combine(result.OutDir, "GRAPH_REPORT.md");
        var htmlPath = Path.Combine(result.OutDir, "graph.html");

        JsonExporter.Write(result.Graph, graphJson, builtAtCommit);
        File.WriteAllText(reportPath, ReportWriter.Render(result.Graph, result.Analysis, result.Loaded.Diagnostics));
        if (html)
            HtmlExporter.Write(result.Graph, result.Analysis, htmlPath);
        return (graphJson, reportPath, htmlPath);
    }
}
