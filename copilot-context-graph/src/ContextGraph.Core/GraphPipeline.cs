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
        var labels = Louvain.LabelCommunities(graph, communities);
        var analysis = Analyzer.Analyze(graph, communities, labels);
        return new BuildResult(graph, analysis, loaded, outDir ?? Path.Combine(loaded.RootDir, DefaultOutDir));
    }

    public static async Task<KnowledgeGraph> ExtractAsync(LoadedSolution loaded, CancellationToken ct = default)
    {
        var extractor = new RoslynExtractor(loaded.RootDir);
        var razorExtractor = new RazorExtractor(loaded.RootDir);

        var extractions = new List<Extraction>();
        foreach (var project in loaded.CSharpProjects)
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
