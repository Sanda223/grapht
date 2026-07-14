namespace ContextGraph.Core;

/// <summary>Dispatches non-C#/Razor files to the extractor that understands them.
/// Returns null for files nothing handles.</summary>
public static class ExtraFileExtractor
{
    public static Extraction? Extract(string filePath, string rootDir) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".js" => JsExtractor.Extract(filePath, rootDir),
            _ => null,
        };
}
