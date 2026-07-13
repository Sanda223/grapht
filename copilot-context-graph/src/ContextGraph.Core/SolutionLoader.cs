using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace ContextGraph.Core;

public sealed record LoadedSolution(
    Solution Solution,
    IReadOnlyList<string> RazorFiles,
    IReadOnlyList<string> Diagnostics,
    string RootDir)
{
    public IEnumerable<Project> CSharpProjects =>
        Solution.Projects.Where(p => p.Language == LanguageNames.CSharp);
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

        return new LoadedSolution(solution, FindRazorFiles(rootDir), diagnostics, rootDir);
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

    private static List<string> FindRazorFiles(string rootDir)
    {
        var results = new List<string>();
        Walk(new DirectoryInfo(rootDir));
        results.Sort(StringComparer.Ordinal);
        return results;

        void Walk(DirectoryInfo dir)
        {
            if (SkipDirs.Contains(dir.Name, StringComparer.OrdinalIgnoreCase))
                return;
            foreach (var file in dir.EnumerateFiles())
                if (file.Extension is ".razor" or ".cshtml")
                    results.Add(file.FullName);
            foreach (var sub in dir.EnumerateDirectories())
                Walk(sub);
        }
    }
}
