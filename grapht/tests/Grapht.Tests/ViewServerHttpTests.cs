using System.Net;
using Grapht.View;

namespace Grapht.Tests;

/// <summary>Exercises the real Kestrel + ASP.NET Core routing pipeline for /api/node/{id} —
/// unlike ViewApiTests (which calls GraphApi.BuildFocus directly), this is the only test that
/// would have caught the %2F routing bug: ASP.NET Core's router deliberately does not decode
/// %2F before handing the route value to the handler, so a unit test bypassing routing cannot
/// see this failure mode.</summary>
public class ViewServerHttpTests
{
    [Fact]
    public async Task NodeWithSlashInIdReturnsOkOverRealHttp()
    {
        const int port = 5098;
        using var cts = new CancellationTokenSource();
        var serverTask = ViewServer.RunAsync(TestPaths.SampleAppSolution, port, openBrowser: false, cts.Token);

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            await WaitForServer(http, port);

            // Fixture fact: SampleApp has file nodes whose id contains '/', e.g.
            // "src/SampleApp.Domain/IRepository.cs". The front end encodes it with
            // encodeURIComponent, which turns '/' into %2F.
            const string id = "src/SampleApp.Domain/IRepository.cs";
            var response = await http.GetAsync($"http://127.0.0.1:{port}/api/node/{Uri.EscapeDataString(id)}");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"\"id\":\"{id}\"", body);
        }
        finally
        {
            await cts.CancelAsync();
            await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(5)));
        }
    }

    private static async Task WaitForServer(HttpClient http, int port)
    {
        for (var i = 0; i < 90; i++)
        {
            try
            {
                if ((await http.GetAsync($"http://127.0.0.1:{port}/api/graph")).IsSuccessStatusCode)
                    return;
            }
            catch { /* not up yet */ }
            await Task.Delay(1000);
        }
        throw new TimeoutException("view server did not start within 90s");
    }
}
