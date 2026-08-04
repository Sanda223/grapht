namespace Grapht.Tests;

public static class TestPaths
{
    /// <summary>Absolute path to tests/fixtures, found by walking up from the test assembly.</summary>
    public static string FixtureRoot { get; } = Locate("tests/fixtures");

    /// <summary>Absolute path to the repo root (the directory containing Grapht.slnx),
    /// found by walking up from the test assembly.</summary>
    public static string RepoRoot { get; } = Locate("Grapht.slnx", isFile: true);

    public static string SampleAppSolution => Path.Combine(FixtureRoot, "SampleApp", "SampleApp.slnx");

    private static string Locate(string relative, bool isFile = false)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (isFile ? File.Exists(candidate) : Directory.Exists(candidate))
                return isFile ? dir.FullName : candidate;
            dir = dir.Parent!;
        }
        throw new DirectoryNotFoundException($"{relative} not found above " + AppContext.BaseDirectory);
    }
}
