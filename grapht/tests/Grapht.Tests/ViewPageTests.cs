using System.Diagnostics;

namespace Grapht.Tests;

/// <summary>PR #7 shipped a blank viewer because a test asserted the JS bundle was
/// *present* rather than that it *ran*. This loads the real page in a browser and fails
/// on any console error — the only check that would have caught that bug.</summary>
public class ViewPageTests
{
    [Fact]
    public async Task ViewerLoadsWithoutConsoleErrors()
    {
        const string chrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
        if (!File.Exists(chrome) || !OperatingSystem.IsMacOS())
            Assert.Skip("headless Chrome unavailable — this is the only test that runs the front end");

        var port = 5099;
        var repoRoot = TestPaths.RepoRoot;
        using var server = Process.Start(new ProcessStartInfo("dotnet",
            $"run --project \"{Path.Combine(repoRoot, "src", "Grapht.Cli")}\" -- view \"{TestPaths.SampleAppSolution}\" --port {port} --no-open")
        { RedirectStandardError = true })!;

        try
        {
            await WaitForServer(port);
            var dom = Path.Combine(Path.GetTempPath(), "grapht-view-" + Guid.NewGuid().ToString("N") + ".html");
            var log = dom + ".log";
            using (var chromeProc = Process.Start(new ProcessStartInfo(chrome,
                // three.js needs a WebGL context; --disable-gpu would leave headless Chrome
                // without one, so render through ANGLE's SwiftShader software rasterizer.
                $"--headless --use-angle=swiftshader --no-sandbox --virtual-time-budget=15000 " +
                $"--enable-logging=stderr --v=0 --dump-dom http://127.0.0.1:{port}/")
            { RedirectStandardOutput = true, RedirectStandardError = true })!)
            {
                File.WriteAllText(dom, await chromeProc.StandardOutput.ReadToEndAsync());
                File.WriteAllText(log, await chromeProc.StandardError.ReadToEndAsync());
                await chromeProc.WaitForExitAsync();
            }

            var console = File.ReadAllText(log);
            Assert.DoesNotContain("Uncaught", console);
            Assert.DoesNotContain("SyntaxError", console);
            var html = File.ReadAllText(dom);
            Assert.Contains("<canvas", html);                 // three.js WebGL canvas created
            Assert.Contains("nodes", html);                   // counts populated from /api/graph
            Assert.Contains("id=\"labels\"", html);           // label overlay present
            Assert.DoesNotContain("cytoscape", html);         // old renderer fully gone
            File.Delete(dom);
            File.Delete(log);
        }
        finally
        {
            if (!server.HasExited) server.Kill(entireProcessTree: true);
        }
    }

    private static async Task WaitForServer(int port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
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
