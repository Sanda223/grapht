namespace ContextGraph.Tests;

public static class TestPaths
{
    /// <summary>Absolute path to tests/fixtures, found by walking up from the test assembly.</summary>
    public static string FixtureRoot { get; } = Locate();

    public static string SampleAppSolution => Path.Combine(FixtureRoot, "SampleApp", "SampleApp.slnx");

    private static string Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent!;
        }
        throw new DirectoryNotFoundException("tests/fixtures not found above " + AppContext.BaseDirectory);
    }
}
