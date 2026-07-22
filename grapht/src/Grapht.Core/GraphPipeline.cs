namespace Grapht.Core;

public sealed record BuildResult(KnowledgeGraph Graph, Analysis Analysis, LoadedSolution Loaded, string OutDir);

/// <summary>The end-to-end build: load → extract → resolve → build → cluster → analyze.
/// ExportAll writes graph.json, GRAPH_REPORT.md, and graph.html.</summary>
public static class GraphPipeline
{
    public const string DefaultOutDir = "grapht-out";

    public static async Task<BuildResult> BuildAsync(string path, string? outDir = null, CancellationToken ct = default)
    {
        var loaded = await SolutionLoader.LoadAsync(path, ct);
        var extractions = await CollectExtractionsAsync(loaded, ct);

        var resolvedOutDir = outDir ?? Path.Combine(loaded.RootDir, DefaultOutDir);
        var (graph, analysis) = Assemble(extractions, PreviousAssignment(Path.Combine(resolvedOutDir, "graph.json")));
        return new BuildResult(graph, analysis, loaded, resolvedOutDir);
    }

    /// <summary>The post-extraction stages, shared by the CLI build and the MCP server's
    /// incremental rebuilds so the two can never drift: resolve placeholders → build →
    /// cluster → remap ids to the previous assignment → label → analyze.</summary>
    public static (KnowledgeGraph Graph, Analysis Analysis) Assemble(
        List<Extraction> extractions, IReadOnlyDictionary<string, int>? previousAssignment)
    {
        RazorResolver.Resolve(extractions);
        var graph = GraphBuilder.Build(extractions);
        var communities = Louvain.Cluster(graph);
        if (previousAssignment is { Count: > 0 })
            communities = Louvain.RemapToPrevious(graph, communities, previousAssignment);
        var labels = Louvain.LabelCommunities(graph, communities);
        return (graph, Analyzer.Analyze(graph, communities, labels));
    }

    /// <summary>node id → community from the previous build, so ids stay stable.</summary>
    private static Dictionary<string, int>? PreviousAssignment(string graphJsonPath)
    {
        if (!File.Exists(graphJsonPath))
            return null;
        try
        {
            // Note: makes local rebuilds stable at the cost of depending on build-dir
            // history. CI stays deterministic because clean checkouts have no previous
            // graph; two dev machines may legitimately number communities differently.
            return Louvain.AssignmentOf(JsonExporter.Read(graphJsonPath));
        }
        catch (Exception)
        {
            return null; // corrupt/old-format previous graph: cluster fresh
        }
    }

    public static async Task<List<Extraction>> CollectExtractionsAsync(LoadedSolution loaded, CancellationToken ct = default)
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
        return extractions;
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
