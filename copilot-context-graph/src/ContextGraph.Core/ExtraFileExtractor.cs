namespace ContextGraph.Core;

/// <summary>Dispatches non-C#/Razor files to the extractor that understands them.
/// Returns null for files nothing handles.</summary>
public static class ExtraFileExtractor
{
    public static Extraction? Extract(string filePath, string rootDir) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".js" => JsExtractor.Extract(filePath, rootDir),
            ".sql" => SqlExtractor.Extract(filePath, rootDir),
            ".bicep" => InfraExtractor.ExtractBicep(filePath, rootDir),
            ".json" => ConfigExtractor.Extract(filePath, rootDir),
            ".yml" or ".yaml" => SniffYaml(filePath, rootDir),
            _ => null,
        };

    /// <summary>YAML is dispatched by content: compose files have a top-level `services:`,
    /// pipelines have stages/jobs/steps. Anything else is ignored.</summary>
    private static Extraction? SniffYaml(string filePath, string rootDir)
    {
        string text;
        try
        {
            text = File.ReadAllText(filePath);
        }
        catch (IOException)
        {
            return null;
        }
        if (text.Contains("\nservices:") || text.StartsWith("services:"))
            return InfraExtractor.ExtractCompose(filePath, rootDir);
        if (text.Contains("steps:") || text.Contains("stages:") || text.Contains("jobs:"))
            return InfraExtractor.ExtractPipeline(filePath, rootDir);
        return null;
    }
}
