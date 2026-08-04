using System.Net;
using Grapht.Core;
using Grapht.View;

namespace Grapht.Tests;

/// <summary>`grapht view --graph` serves a prebuilt graph.json instead of extracting a
/// solution. The graph keeps the clustering it was built with: Node.Community values from
/// the file win over a fresh Louvain pass, so the viewer shows the same communities as the
/// export that produced the file.</summary>
public class ViewGraphJsonTests
{
    private static KnowledgeGraph GraphWithStoredCommunities()
    {
        var graph = new KnowledgeGraph();
        graph.AddNode(new Node { Id = "A", Label = "A", Community = 5 });
        graph.AddNode(new Node { Id = "B", Label = "B", Community = 5 });
        graph.AddNode(new Node { Id = "C", Label = "C", Community = 9 });
        graph.AddEdge(new Edge { Source = "A", Target = "B", Relation = Relation.Calls });
        graph.AddEdge(new Edge { Source = "B", Target = "C", Relation = Relation.Calls });
        return graph;
    }

    [Fact]
    public void StoredOrClusterKeepsCommunityAssignmentsFromTheFile()
    {
        var communities = Louvain.StoredOrCluster(GraphWithStoredCommunities());

        Assert.Equal([5, 9], communities.Keys.Order());
        Assert.Equal(["A", "B"], communities[5]);
        Assert.Equal(["C"], communities[9]);
    }

    [Fact]
    public void StoredOrClusterFallsBackToLouvainWhenTheFileHasNoCommunities()
    {
        var graph = new KnowledgeGraph();
        graph.AddNode(new Node { Id = "A", Label = "A" });
        graph.AddNode(new Node { Id = "B", Label = "B" });
        graph.AddEdge(new Edge { Source = "A", Target = "B", Relation = Relation.Calls });

        var communities = Louvain.StoredOrCluster(graph);

        Assert.NotEmpty(communities);
        Assert.Equal(graph.NodeCount, communities.Values.Sum(m => m.Count));
    }

    [Fact]
    public async Task ServesOverviewFocusAndSearchFromAGraphJsonFile()
    {
        const int port = 5097;
        var path = Path.Combine(Path.GetTempPath(), "grapht-viewjson-" + Guid.NewGuid().ToString("N") + ".json");
        JsonExporter.Write(GraphWithStoredCommunities(), path);
        using var cts = new CancellationTokenSource();
        var serverTask = ViewServer.RunFromGraphJsonAsync(path, port, openBrowser: false, cts.Token);

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            for (var i = 0; i < 30; i++)
            {
                try { if ((await http.GetAsync($"http://127.0.0.1:{port}/api/graph")).IsSuccessStatusCode) break; }
                catch { /* not up yet */ }
                await Task.Delay(500);
            }

            var overview = await http.GetStringAsync($"http://127.0.0.1:{port}/api/graph");
            Assert.Contains("\"nodeCount\":3", overview);
            Assert.Contains("\"id\":5", overview);   // stored community ids survive the round trip
            Assert.Contains("\"id\":9", overview);

            var focus = await http.GetAsync($"http://127.0.0.1:{port}/api/node/B");
            Assert.Equal(HttpStatusCode.OK, focus.StatusCode);

            var search = await http.GetStringAsync($"http://127.0.0.1:{port}/api/search?q=A");
            Assert.Contains("\"id\":\"A\"", search);
        }
        finally
        {
            await cts.CancelAsync();
            await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(5)));
            File.Delete(path);
        }
    }
}
