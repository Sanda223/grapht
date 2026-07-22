namespace Grapht.Core;

/// <summary>Shared text helpers for the file-level extractors. Centralized because they
/// are identity/correctness-sensitive: RelPath defines node ids (a divergence severs
/// nodes from the graph), and line numbering must agree across extractors.</summary>
internal static class TextUtil
{
    /// <summary>Repo-relative, forward-slashed path — the node-id scheme for files.</summary>
    public static string RelPath(string filePath, string rootDir) =>
        Path.GetRelativePath(rootDir, filePath).Replace('\\', '/');

    /// <summary>Strip generic arguments and namespace qualification: "A.B.IRepo&lt;T&gt;" → "IRepo".</summary>
    public static string ShortName(string identifier)
    {
        var generic = identifier.IndexOf('<');
        var bare = generic >= 0 ? identifier[..generic] : identifier;
        return bare[(bare.LastIndexOf('.') + 1)..];
    }
}

/// <summary>Precomputed newline positions for O(log n) line lookup per match — the naive
/// per-match prefix scan is O(n²) time and allocation on large generated files.</summary>
internal readonly struct LineIndex
{
    private readonly int[] _lineStarts;

    public LineIndex(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                starts.Add(i + 1);
        _lineStarts = [.. starts];
    }

    public int LineAt(int index)
    {
        var position = Array.BinarySearch(_lineStarts, index);
        return position >= 0 ? position + 1 : ~position; // 1-based
    }

    public string LabelAt(int index) => "L" + LineAt(index);
}
