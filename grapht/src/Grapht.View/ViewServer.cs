using System.Diagnostics;
using System.Text.RegularExpressions;
using Grapht.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Grapht.View;

/// <summary>Loopback-only web host over a live GraphService. Binds 127.0.0.1 explicitly —
/// this serves source code from disk and must never be reachable off-machine.</summary>
public static partial class ViewServer
{
    // Matches only the literal escape sequence for '/' (case-insensitive, as clients may send
    // either %2F or %2f) — never a bare '/' or any other escape.
    [GeneratedRegex("%2F", RegexOptions.IgnoreCase)]
    private static partial Regex SlashPattern();
    private static readonly Regex Slash = SlashPattern();

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
            // ASP.NET Core routing decodes most percent-escapes before the handler ever sees
            // them, but deliberately leaves %2F (encoded '/') alone — decoding it there would
            // let a client forge extra path segments. A node id may legitimately contain '/'
            // (file and route ids), so undo that one escape here, and only that one: a blanket
            // Uri.UnescapeDataString would double-decode ids that legitimately contain a
            // literal "%20" sequence (see FocusResolvesIdContainingPercentEncodedSequence...).
            var decodedId = Slash.Replace(id, "/");
            var snapshot = service.Current;
            var focus = GraphApi.BuildFocus(snapshot.Graph, snapshot.Analysis, decodedId);
            return focus is null ? Results.NotFound(new { error = $"No node with id \"{decodedId}\"" }) : Results.Ok(focus);
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
