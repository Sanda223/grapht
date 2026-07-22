using Grapht.Core;
using Microsoft.CodeAnalysis.Text;

namespace Grapht.Mcp;

/// <summary>Owns the in-memory graph. Loads the solution once, watches the workspace, and
/// incrementally re-extracts changed files (debounced), so tools never see a stale graph.
/// Roslyn extractions are cached per file; razor files are re-extracted every rebuild
/// (regex-cheap, and it keeps placeholder resolution correct as components come and go).
/// Known limit: a cross-file rename fully settles on the next full rebuild — same trade
/// the reference tool makes.</summary>
public sealed class GraphService : IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _rebuildLock = new(1, 1);
    private readonly Dictionary<string, Extraction> _cache = [];
    private readonly HashSet<string> _pendingChanges = [];
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private bool _fullReloadPending;

    private LoadedSolution _loaded = null!;
    private volatile Snapshot _current = null!;

    public sealed record Snapshot(KnowledgeGraph Graph, QueryEngine Engine, Analysis Analysis);

    public GraphService(string path) => _path = path;

    public Snapshot Current => _current ?? throw new InvalidOperationException("Graph not initialized yet — try again in a moment.");
    public bool Ready => _current is not null;
    public string RootDir => _loaded.RootDir;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _loaded = await SolutionLoader.LoadAsync(_path, ct);
        await RebuildAsync(ct);
        StartWatcher();
    }

    public async Task<string> FullReloadAsync(CancellationToken ct = default)
    {
        await _rebuildLock.WaitAsync(ct);
        try
        {
            _cache.Clear();
            _loaded = await SolutionLoader.LoadAsync(_path, ct);
        }
        finally
        {
            _rebuildLock.Release();
        }
        await RebuildAsync(ct);
        return $"Rebuilt: {_current.Graph.NodeCount} nodes, {_current.Graph.EdgeCount} edges";
    }

    private async Task RebuildAsync(CancellationToken ct = default)
    {
        await _rebuildLock.WaitAsync(ct);
        try
        {
            var extractor = new RoslynExtractor(_loaded.RootDir);
            var razorExtractor = new RazorExtractor(_loaded.RootDir);

            var extractions = new List<Extraction>();
            foreach (var project in _loaded.DistinctProjects)
                foreach (var document in project.Documents)
                {
                    if (document.FilePath is null)
                        continue;
                    if (!_cache.TryGetValue(document.FilePath, out var extraction))
                        _cache[document.FilePath] = extraction = await extractor.ExtractAsync(document, ct);
                    extractions.Add(extraction);
                }
            foreach (var razorFile in _loaded.RazorFiles.Where(File.Exists))
                extractions.Add(razorExtractor.Extract(razorFile));
            foreach (var extraFile in _loaded.ExtraFiles.Where(File.Exists))
                if (ExtraFileExtractor.Extract(extraFile, _loaded.RootDir) is { } extraction)
                    extractions.Add(extraction);
            extractions.AddRange(CsprojExtractor.ExtractAll(_loaded));

            // Shared assembly stages (resolve→build→cluster→remap→analyze) so the MCP
            // graph can never drift from what the CLI would build; ids stay stable
            // across incremental rebuilds via the previous snapshot's assignment.
            var (graph, analysis) = GraphPipeline.Assemble(
                extractions, _current is { } previous ? Louvain.AssignmentOf(previous.Graph) : null);
            _current = new Snapshot(graph, new QueryEngine(graph), analysis);
        }
        finally
        {
            _rebuildLock.Release();
        }
    }

    // --- watching ---

    private void StartWatcher()
    {
        _watcher = new FileSystemWatcher(_loaded.RootDir) { IncludeSubdirectories = true, EnableRaisingEvents = true };
        _watcher.Changed += (_, e) => OnFileEvent(e.FullPath, structural: false);
        _watcher.Created += (_, e) => OnFileEvent(e.FullPath, structural: true);
        _watcher.Deleted += (_, e) => OnFileEvent(e.FullPath, structural: true);
        _watcher.Renamed += (_, e) => OnFileEvent(e.FullPath, structural: true);
    }

    private void OnFileEvent(string fullPath, bool structural)
    {
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is not (".cs" or ".razor" or ".cshtml" or ".csproj" or ".sln" or ".slnx"))
            return;
        if (fullPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
            fullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            return;

        lock (_pendingChanges)
        {
            if (structural || extension is ".csproj" or ".sln" or ".slnx")
                _fullReloadPending = true;
            else
                _pendingChanges.Add(fullPath);
            _debounce?.Dispose();
            _debounce = new Timer(_ => _ = ProcessPendingAsync(), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        }
    }

    private async Task ProcessPendingAsync()
    {
        bool fullReload;
        List<string> changed;
        lock (_pendingChanges)
        {
            fullReload = _fullReloadPending;
            _fullReloadPending = false;
            changed = _pendingChanges.ToList();
            _pendingChanges.Clear();
        }

        try
        {
            if (fullReload)
            {
                await FullReloadAsync();
                return;
            }
            foreach (var path in changed.Where(p => Path.GetExtension(p) == ".cs"))
            {
                _cache.Remove(path);
                foreach (var docId in _loaded.Solution.GetDocumentIdsWithFilePath(path))
                {
                    var text = SourceText.From(await File.ReadAllTextAsync(path));
                    _loaded = _loaded with { Solution = _loaded.Solution.WithDocumentText(docId, text) };
                }
            }
            if (changed.Count > 0)
                await RebuildAsync();
        }
        catch (Exception ex)
        {
            // Watcher failures must never kill the server; the next change retries.
            await Console.Error.WriteLineAsync($"[grapht] incremental rebuild failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
        _rebuildLock.Dispose();
    }
}
