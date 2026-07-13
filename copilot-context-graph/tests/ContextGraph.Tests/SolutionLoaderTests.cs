using ContextGraph.Core;

namespace ContextGraph.Tests;

public class SolutionLoaderTests
{
    [Fact]
    public async Task LoadsAllFixtureProjectsAndRazorFiles()
    {
        var loaded = await SolutionLoader.LoadAsync(TestPaths.SampleAppSolution);

        var names = loaded.CSharpProjects.Select(p => p.Name).Order().ToList();
        Assert.Equal(["SampleApp.Domain", "SampleApp.Legacy", "SampleApp.Services", "SampleApp.Web"], names);

        Assert.Equal(3, loaded.RazorFiles.Count);
        Assert.Contains(loaded.RazorFiles, f => f.EndsWith("Orders.razor"));
    }

    [Fact]
    public async Task DirectoryPathResolvesToSolutionFile()
    {
        var dir = Path.GetDirectoryName(TestPaths.SampleAppSolution)!;
        var loaded = await SolutionLoader.LoadAsync(dir);
        Assert.Equal(4, loaded.CSharpProjects.Count());
    }
}
