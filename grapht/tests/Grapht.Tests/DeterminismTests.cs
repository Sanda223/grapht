using Grapht.Core;

namespace Grapht.Tests;

public class DeterminismTests
{
    [Fact]
    public async Task TwoBuildsProduceIdenticalGraphJson()
    {
        var first = await GraphPipeline.BuildAsync(TestPaths.SampleAppSolution);
        var second = await GraphPipeline.BuildAsync(TestPaths.SampleAppSolution);

        var dir = Path.Combine(Path.GetTempPath(), "ctxgraph-determinism-" + Guid.NewGuid().ToString("N"));
        try
        {
            JsonExporter.Write(first.Graph, Path.Combine(dir, "a.json"));
            JsonExporter.Write(second.Graph, Path.Combine(dir, "b.json"));
            Assert.Equal(File.ReadAllText(Path.Combine(dir, "a.json")), File.ReadAllText(Path.Combine(dir, "b.json")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GraphJsonRoundTripsThroughRead()
    {
        var result = await GraphPipeline.BuildAsync(TestPaths.SampleAppSolution);
        var path = Path.Combine(Path.GetTempPath(), "ctxgraph-roundtrip-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            JsonExporter.Write(result.Graph, path);
            var reread = JsonExporter.Read(path);
            Assert.Equal(result.Graph.NodeCount, reread.NodeCount);
            Assert.Equal(result.Graph.EdgeCount, reread.EdgeCount);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
