namespace ContextGraph.Tests;

public class FixtureSmokeTests
{
    [Fact]
    public void SampleAppFixtureIsIntact()
    {
        Assert.True(File.Exists(TestPaths.SampleAppSolution), $"missing {TestPaths.SampleAppSolution}");

        var src = Path.Combine(TestPaths.FixtureRoot, "SampleApp", "src");
        string[] projects =
        [
            "SampleApp.Domain/SampleApp.Domain.csproj",
            "SampleApp.Services/SampleApp.Services.csproj",
            "SampleApp.Web/SampleApp.Web.csproj",
            "SampleApp.Legacy/SampleApp.Legacy.csproj",
        ];
        foreach (var project in projects)
            Assert.True(File.Exists(Path.Combine(src, project)), $"missing {project}");

        Assert.True(File.Exists(Path.Combine(src, "SampleApp.Web", "Pages", "Orders.razor")));
    }
}
