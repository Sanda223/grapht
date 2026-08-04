using System.Net;
using Grapht.View;

namespace Grapht.Tests;

/// <summary>Boots the real Kestrel server over SampleApp once for every fact in this class —
/// graph extraction is the expensive part, and these tests exercise routing/serialization
/// behaviour that unit tests over GraphApi cannot see (e.g. the %2F routing bug).</summary>
public sealed class ViewServerFixture : IAsyncLifetime
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;

    public const int Port = 5098;
    public HttpClient Client { get; } = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async ValueTask InitializeAsync()
    {
        _serverTask = ViewServer.RunAsync(TestPaths.SampleAppSolution, Port, openBrowser: false, _cts.Token);
        for (var i = 0; i < 90; i++)
        {
            try
            {
                if ((await Client.GetAsync($"http://127.0.0.1:{Port}/api/graph")).IsSuccessStatusCode)
                    return;
            }
            catch { /* not up yet */ }
            await Task.Delay(1000);
        }
        throw new TimeoutException("view server did not start within 90s");
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_serverTask is not null)
            await Task.WhenAny(_serverTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Client.Dispose();
        _cts.Dispose();
    }
}

public class ViewServerHttpTests : IClassFixture<ViewServerFixture>
{
    private readonly ViewServerFixture _server;

    public ViewServerHttpTests(ViewServerFixture server) => _server = server;

    private static string Url(string pathAndQuery) => $"http://127.0.0.1:{ViewServerFixture.Port}{pathAndQuery}";

    [Fact]
    public async Task NodeWithSlashInIdReturnsOkOverRealHttp()
    {
        // Fixture fact: SampleApp has file nodes whose id contains '/', e.g.
        // "src/SampleApp.Domain/IRepository.cs". The front end encodes it with
        // encodeURIComponent, which turns '/' into %2F.
        const string id = "src/SampleApp.Domain/IRepository.cs";
        var response = await _server.Client.GetAsync(Url($"/api/node/{Uri.EscapeDataString(id)}"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"\"id\":\"{id}\"", body);
    }

    [Fact]
    public async Task SearchReturnsRankedCamelCasedHits()
    {
        var response = await _server.Client.GetAsync(Url("/api/search?q=OrderService"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"id\":\"SampleApp.Services.OrderService\"", body);
        Assert.Contains("\"communityLabel\":", body); // camelCase — Web serializer defaults intact
    }

    [Fact]
    public async Task SearchWithoutQueryReturnsEmptyArrayNotAnError()
    {
        var response = await _server.Client.GetAsync(Url("/api/search"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }
}
