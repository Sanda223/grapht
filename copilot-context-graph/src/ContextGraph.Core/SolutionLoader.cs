using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace ContextGraph.Core;

public sealed record LoadedSolution(
    Solution Solution,
    IReadOnlyList<string> RazorFiles,
    IReadOnlyList<string> ExtraFiles,
    IReadOnlyList<string> Diagnostics,
    string RootDir)
{
    public IEnumerable<Project> CSharpProjects =>
        Solution.Projects.Where(p => p.Language == LanguageNames.CSharp);

    /// <summary>One project per csproj. Multi-targeted projects (MAUI: android/ios/…)
    /// load once per TFM with the same documents; extracting each is 3-4x wasted work.
    /// First TFM wins — code behind other TFMs' #if branches is not extracted.</summary>
    public IEnumerable<Project> DistinctProjects =>
        CSharpProjects.GroupBy(p => p.FilePath ?? p.Name).Select(g => g.First());
}

public static class SolutionLoader
{
    private static readonly string[] SkipDirs = ["bin", "obj", "node_modules", ".git"];

    /// <summary>Load a solution/project. <paramref name="path"/> may be a directory, .sln, .slnx, or .csproj.</summary>
    public static async Task<LoadedSolution> LoadAsync(string path, CancellationToken ct = default)
    {
        var target = ResolveTarget(Path.GetFullPath(path));
        var rootDir = Path.GetDirectoryName(target)!;

        var diagnostics = new List<string>();
        var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e => { lock (diagnostics) diagnostics.Add($"[{e.Diagnostic.Kind}] {e.Diagnostic.Message}"); });

        var solution = target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? (await workspace.OpenProjectAsync(target, cancellationToken: ct)).Solution
            : await workspace.OpenSolutionAsync(target, cancellationToken: ct);

        var (razorFiles, extraFiles) = FindFiles(rootDir);
        return new LoadedSolution(solution, razorFiles, extraFiles, diagnostics, rootDir);
    }

    private static string ResolveTarget(string path)
    {
        if (File.Exists(path))
            return path;
        if (!Directory.Exists(path))
            throw new FileNotFoundException($"No such file or directory: {path}");

        var candidates = Directory.EnumerateFiles(path)
            .Where(f => f.EndsWith(".slnx") || f.EndsWith(".sln") || f.EndsWith(".csproj"))
            .OrderBy(f => Path.GetExtension(f) switch { ".slnx" => 0, ".sln" => 1, _ => 2 })
            .ToList();
        return candidates.FirstOrDefault()
            ?? throw new FileNotFoundException($"No .sln, .slnx, or .csproj found in {path}");
    }

    /// <summary>Razor files plus the "extra" corpus the non-Roslyn extractors handle:
    /// JS (own code only, not vendored bundles), SQL, Bicep, appsettings, compose/pipeline YAML.</summary>
    private static (List<string> Razor, List<string> Extra) FindFiles(string rootDir)
    {
        var razor = new List<string>();
        var extra = new List<string>();
        Walk(new DirectoryInfo(rootDir));
        razor.Sort(StringComparer.Ordinal);
        extra.Sort(StringComparer.Ordinal);
        return (razor, extra);

        void Walk(DirectoryInfo dir)
        {
            if (SkipDirs.Contains(dir.Name, StringComparer.OrdinalIgnoreCase))
                return;
            foreach (var file in dir.EnumerateFiles())
            {
                if (file.Extension is ".razor" or ".cshtml")
                    razor.Add(file.FullName);
                else if (IsExtraFile(file.Name))
                    extra.Add(file.FullName);
            }
            foreach (var sub in dir.EnumerateDirectories())
                Walk(sub);
        }
    }

    private static bool IsExtraFile(string name) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".js" => !name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase),
            ".sql" or ".bicep" => true,
            ".json" => name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase),
            ".yml" or ".yaml" => true, // dispatcher sniffs compose vs pipeline vs other
            _ => false,
        };
}
