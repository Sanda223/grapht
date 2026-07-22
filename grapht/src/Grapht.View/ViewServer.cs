using System.Diagnostics;
using Grapht.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Grapht.View;

/// <summary>Loopback-only web host over a live GraphService. Binds 127.0.0.1 explicitly —
/// this serves source code from disk and must never be reachable off-machine.</summary>
public static class ViewServer
{
    public static async Task RunAsync(string solutionPath, int port, bool openBrowser, CancellationToken ct = default)
    {
        using var service = new GraphService(solutionPath);
        Console.Error.WriteLine("grapht: building graph…");
        await service.InitializeAsync(ct);
        Console.Error.WriteLine($"grapht: ready ({service.Current.Graph.NodeCount} nodes)");

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();

        app.MapGet("/api/graph", () =>
        {
            var snapshot = service.Current;
            return Results.Ok(GraphApi.BuildOverview(snapshot.Graph, snapshot.Analysis));
        });

        app.MapGet("/api/node/{id}", (string id) =>
        {
            var snapshot = service.Current;
            var focus = GraphApi.BuildFocus(snapshot.Graph, snapshot.Analysis, id);
            return focus is null ? Results.NotFound(new { error = $"No node with id \"{id}\"" }) : Results.Ok(focus);
        });

        app.MapGet("/", () => Results.Content(Asset("index.html"), "text/html; charset=utf-8"));
        app.MapGet("/app.css", () => Results.Content(Asset("app.css"), "text/css; charset=utf-8"));
        app.MapGet("/app.js", () => Results.Content(Asset("app.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/cytoscape.min.js", () => Results.Content(Asset("cytoscape.min.js"), "text/javascript; charset=utf-8"));

        var url = $"http://127.0.0.1:{port}";
        Console.Error.WriteLine($"grapht: viewer at {url}  (ctrl-c to stop)");
        if (openBrowser)
            OpenBrowser(url);
        await app.RunAsync(ct);
    }

    private static string Asset(string name)
    {
        using var stream = typeof(ViewServer).Assembly.GetManifestResourceStream($"Grapht.View.Assets.{name}")
            ?? throw new InvalidOperationException($"asset missing: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            var cmd = OperatingSystem.IsWindows() ? "cmd" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            var args = OperatingSystem.IsWindows() ? $"/c start {url}" : url;
            Process.Start(new ProcessStartInfo(cmd, args) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"grapht: could not open a browser ({ex.Message}) — open {url} yourself");
        }
    }
}
