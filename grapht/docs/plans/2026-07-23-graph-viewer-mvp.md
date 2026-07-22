# `grapht view` MVP Implementation Plan (spec steps 1–4)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `grapht view` starts a loopback web server that renders the solution as a community overview, lets you search for any symbol, focus on it to see its neighbours, and expand outward — the minimum useful product from [the design spec](../specs/2026-07-23-graph-viewer-design.md).

**Architecture:** `GraphService` moves from `Grapht.Mcp` to `Grapht.Core` (it is shared infrastructure with no MCP dependency). A new `Grapht.View` project hosts a minimal-API Kestrel server bound to loopback, serving two JSON endpoints plus a hand-rolled front end. `Grapht.Cli` gains a `view` command that starts it. Cytoscape.js is vendored as an embedded resource and served over HTTP.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, System.CommandLine 2.0.9, xunit, Cytoscape.js 3.30.2 (vendored), headless Chrome for front-end verification.

## Global Constraints

- **Loopback only.** The server binds `127.0.0.1`, never `0.0.0.0`.
- **No npm, no bundler.** Front end is hand-written HTML/CSS/JS served as embedded resources.
- **`graph.html` is untouched.** The static export and its vis-network asset keep working exactly as they do today. No file under `HtmlExporter` changes in this plan.
- **Never render the whole graph.** Overview is community-level; focus views are one node plus neighbours.
- **Determinism holds.** Any JSON the server emits orders nodes and edges with `StringComparer.Ordinal`, matching `JsonExporter`.
- **Commits carry no `Co-Authored-By` trailer.** Author is Sanda223 only.
- **Karpathy guidelines apply.** Every changed line traces to a task. No speculative abstraction, no adjacent "improvements".

## File Structure

| File | Responsibility |
|---|---|
| `src/Grapht.Core/GraphService.cs` | *Moved* from `Grapht.Mcp`. Owns the live graph, watcher, incremental rebuild. |
| `src/Grapht.View/Grapht.View.csproj` | New library project. References Core, uses `Microsoft.NET.Sdk.Web`. |
| `src/Grapht.View/ViewServer.cs` | Builds and runs the Kestrel host. Single public entry point. |
| `src/Grapht.View/GraphApi.cs` | Endpoint handlers + the DTOs they return. |
| `src/Grapht.View/Assets/index.html` | Front-end shell: header, three regions. |
| `src/Grapht.View/Assets/app.css` | Design tokens and layout. |
| `src/Grapht.View/Assets/app.js` | Overview → focus → expand interaction. |
| `src/Grapht.View/Assets/cytoscape.min.js` | Vendored library. |
| `src/Grapht.Cli/Program.cs` | Adds the `view` subcommand. |
| `tests/Grapht.Tests/ViewApiTests.cs` | Endpoint shape and error tests. |
| `tests/Grapht.Tests/ViewPageTests.cs` | Headless-Chrome load test. |

---

### Task 1: Move `GraphService` to Core

Shared infrastructure currently lives in one consumer. Moving it is a prerequisite for the view server and must be provably behaviour-preserving.

**Files:**
- Move: `src/Grapht.Mcp/GraphService.cs` → `src/Grapht.Core/GraphService.cs`
- Modify: `src/Grapht.Mcp/Program.cs`, `src/Grapht.Mcp/GraphTools.cs` (remove now-redundant references)

**Interfaces:**
- Consumes: nothing new.
- Produces: `Grapht.Core.GraphService` with unchanged members — `GraphService(string path)`, `Task InitializeAsync(CancellationToken)`, `Task<string> FullReloadAsync(CancellationToken)`, `Snapshot Current`, `bool Ready`, `string RootDir`, and the nested `sealed record Snapshot(KnowledgeGraph Graph, QueryEngine Engine, Analysis Analysis)`.

- [ ] **Step 1: Move the file with git so history follows**

```bash
cd /Users/sandaru/graphify/grapht
git mv src/Grapht.Mcp/GraphService.cs src/Grapht.Core/GraphService.cs
```

- [ ] **Step 2: Change the namespace**

In `src/Grapht.Core/GraphService.cs`, change line 4 from `namespace Grapht.Mcp;` to `namespace Grapht.Core;` and delete line 1 (`using Grapht.Core;`) — it is now self-referential. Keep `using Microsoft.CodeAnalysis.Text;`.

- [ ] **Step 3: Build to find every broken reference**

Run: `dotnet build Grapht.slnx 2>&1 | grep -E "error" | head`
Expected: errors in `Grapht.Mcp` files referencing `GraphService`, or none if `using Grapht.Core;` is already present in them.

- [ ] **Step 4: Fix references in Mcp**

`src/Grapht.Mcp/Program.cs` and `src/Grapht.Mcp/GraphTools.cs` already have `using Grapht.Core;`. If the build reported errors, add that using to any file that lacks it. Change nothing else.

- [ ] **Step 5: Run the full suite — the move must change no behaviour**

Run: `dotnet test Grapht.slnx -c Release`
Expected: `Passed! - Failed: 0, Passed: 68`

- [ ] **Step 6: Verify the MCP server still serves its 8 tools**

Run:
```bash
cd /Users/sandaru/eShop && printf '%s\n' \
 '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"p","version":"1"}}}' \
 '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
 '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' \
 | dotnet run --project /Users/sandaru/graphify/grapht/src/Grapht.Mcp -c Release -- --solution /Users/sandaru/eShop 2>/dev/null | grep -c '"name"'
```
Expected: a non-zero count; the `tools/list` response lists 8 tools.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(core): move GraphService from Mcp to Core

It owns the live graph, watcher and incremental rebuild — shared
infrastructure that the view server needs too, with no MCP dependency.
Its location in Grapht.Mcp was an accident of what was built first.

No behaviour change: 68/68 tests pass and the MCP server still serves
all 8 tools."
```

---

### Task 2: `Grapht.View` project with `/api/graph`

**Files:**
- Create: `src/Grapht.View/Grapht.View.csproj`
- Create: `src/Grapht.View/GraphApi.cs`
- Create: `src/Grapht.View/ViewServer.cs`
- Modify: `Grapht.slnx`
- Test: `tests/Grapht.Tests/ViewApiTests.cs`

**Interfaces:**
- Consumes: `GraphService`, `Analysis`, `KnowledgeGraph.Degree(string)`, `KnowledgeGraph.Edges`, `Node.Community`.
- Produces: `GraphApi.BuildOverview(KnowledgeGraph graph, Analysis analysis)` returning `OverviewDto`; records `OverviewDto(List<CommunityDto> Communities, List<MetaEdgeDto> Edges, int NodeCount, int EdgeCount)`, `CommunityDto(int Id, string Label, int Size, string Color, List<string> TopMembers)`, `MetaEdgeDto(int Source, int Target, int Weight)`.

**JSON casing:** minimal APIs serialize with `JsonSerializerDefaults.Web`, so these PascalCase members reach the browser camelCased — `NodeCount` → `nodeCount`, `TopMembers` → `topMembers`. The front end in Task 5 reads the camelCase names. Do not add a naming policy override.

- [ ] **Step 1: Create the project file**

Create `src/Grapht.View/Grapht.View.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="..\Grapht.Core\Grapht.Core.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputType>Library</OutputType>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Add it to the solution**

Edit `Grapht.slnx`, adding inside the `/src/` folder element:

```xml
    <Project Path="src/Grapht.View/Grapht.View.csproj" />
```

- [ ] **Step 3: Write the failing test**

Create `tests/Grapht.Tests/ViewApiTests.cs`:

```csharp
using Grapht.Core;
using Grapht.View;

namespace Grapht.Tests;

public class ViewApiTests : IClassFixture<BuiltGraphFixture>
{
    private readonly KnowledgeGraph _graph;
    private readonly Analysis _analysis;

    public ViewApiTests(BuiltGraphFixture fixture)
    {
        _graph = fixture.Graph;
        var communities = Louvain.Cluster(_graph);
        var labels = Louvain.LabelCommunities(_graph, communities);
        _analysis = Analyzer.Analyze(_graph, communities, labels);
    }

    [Fact]
    public void OverviewReturnsOneNodePerCommunityAndWeightedCrossings()
    {
        var overview = GraphApi.BuildOverview(_graph, _analysis);

        Assert.Equal(_analysis.Communities.Count, overview.Communities.Count);
        Assert.Equal(_graph.NodeCount, overview.NodeCount);
        Assert.All(overview.Communities, c => Assert.False(string.IsNullOrWhiteSpace(c.Label)));
        // every meta-edge joins two distinct communities and carries a positive weight
        Assert.All(overview.Edges, e =>
        {
            Assert.NotEqual(e.Source, e.Target);
            Assert.True(e.Weight > 0);
        });
        // deterministic ordering
        Assert.Equal(overview.Communities.Select(c => c.Id).Order(), overview.Communities.Select(c => c.Id));
    }
}
```

- [ ] **Step 4: Run it to verify it fails**

Run: `dotnet test Grapht.slnx --filter "OverviewReturnsOneNodePerCommunityAndWeightedCrossings"`
Expected: FAIL — `Grapht.View` namespace does not exist / `GraphApi` not found.

- [ ] **Step 5: Add the test project reference**

In `tests/Grapht.Tests/Grapht.Tests.csproj`, add inside the existing `ItemGroup` holding project references:

```xml
    <ProjectReference Include="..\..\src\Grapht.View\Grapht.View.csproj" />
```

- [ ] **Step 6: Implement `GraphApi.BuildOverview`**

Create `src/Grapht.View/GraphApi.cs`:

```csharp
using Grapht.Core;

namespace Grapht.View;

public sealed record CommunityDto(int Id, string Label, int Size, string Color, List<string> TopMembers);
public sealed record MetaEdgeDto(int Source, int Target, int Weight);
public sealed record OverviewDto(List<CommunityDto> Communities, List<MetaEdgeDto> Edges, int NodeCount, int EdgeCount);

/// <summary>Shapes graph state into the DTOs the front end consumes. Pure functions over a
/// snapshot — no I/O — so every endpoint is directly testable without a running server.</summary>
public static class GraphApi
{
    /// <summary>Community-level view: one node per community, edges weighted by how many
    /// relationships cross each pair. This is the landing state — the full graph is never
    /// sent to the browser.</summary>
    public static OverviewDto BuildOverview(KnowledgeGraph graph, Analysis analysis)
    {
        var communities = analysis.Communities.OrderBy(kv => kv.Key).Select(kv => new CommunityDto(
            kv.Key,
            analysis.CommunityLabels[kv.Key],
            kv.Value.Count,
            Color(kv.Key),
            kv.Value.OrderByDescending(graph.Degree).ThenBy(id => id, StringComparer.Ordinal)
                    .Take(8).Select(id => graph.Nodes[id].Label).ToList()))
            .ToList();

        var crossings = new Dictionary<(int, int), int>();
        foreach (var edge in graph.Edges)
        {
            int a = graph.Nodes[edge.Source].Community ?? 0, b = graph.Nodes[edge.Target].Community ?? 0;
            if (a == b)
                continue;
            var key = a < b ? (a, b) : (b, a);
            crossings[key] = crossings.GetValueOrDefault(key) + 1;
        }

        var edges = crossings.OrderBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2)
            .Select(kv => new MetaEdgeDto(kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList();

        return new OverviewDto(communities, edges, graph.NodeCount, graph.EdgeCount);
    }

    /// <summary>Golden-angle hue walk — visually distinct at any community count.</summary>
    private static string Color(int community) => $"hsl({(int)(community * 137.508) % 360}, 62%, 55%)";
}
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test Grapht.slnx --filter "OverviewReturnsOneNodePerCommunityAndWeightedCrossings"`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(view): Grapht.View project with community overview DTOs

BuildOverview aggregates to one node per community with crossing-weighted
edges — the landing state, so the browser never receives all 2717 nodes.
Pure function over a snapshot, testable without a server."
```

---

### Task 3: `/api/node/{id}` — focus view

**Files:**
- Modify: `src/Grapht.View/GraphApi.cs`
- Test: `tests/Grapht.Tests/ViewApiTests.cs`

**Interfaces:**
- Consumes: `GraphApi` from Task 2; `KnowledgeGraph.OutEdges(string)`, `InEdges(string)`, `Node.Span`.
- Produces: `GraphApi.BuildFocus(KnowledgeGraph, Analysis, string nodeId)` returning `FocusDto?` (null when the id is unknown); records `FocusDto(NodeDto Node, List<NeighbourDto> Neighbours)`, `NodeDto(string Id, string Label, string? Type, string? Span, int Community, string CommunityLabel, int Degree, string Color)`, `NeighbourDto(NodeDto Node, string Relation, string Direction, string Confidence)` where `Direction` is `"out"` or `"in"`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Grapht.Tests/ViewApiTests.cs`, inside the class:

```csharp
    [Fact]
    public void FocusReturnsNodeWithBothEdgeDirections()
    {
        var id = _graph.Nodes.Values.OrderByDescending(n => _graph.Degree(n.Id))
                                    .ThenBy(n => n.Id, StringComparer.Ordinal).First().Id;

        var focus = GraphApi.BuildFocus(_graph, _analysis, id);

        Assert.NotNull(focus);
        Assert.Equal(id, focus!.Node.Id);
        Assert.Equal(_graph.Degree(id), focus.Node.Degree);
        Assert.NotEmpty(focus.Neighbours);
        Assert.All(focus.Neighbours, n => Assert.Contains(n.Direction, new[] { "in", "out" }));
        Assert.Equal(_graph.OutEdges(id).Count, focus.Neighbours.Count(n => n.Direction == "out"));
        Assert.Equal(_graph.InEdges(id).Count, focus.Neighbours.Count(n => n.Direction == "in"));
    }

    [Fact]
    public void FocusReturnsNullForUnknownNode()
    {
        Assert.Null(GraphApi.BuildFocus(_graph, _analysis, "no-such-node-id"));
    }
```

- [ ] **Step 2: Run to verify both fail**

Run: `dotnet test Grapht.slnx --filter "Focus"`
Expected: FAIL — `BuildFocus` does not exist.

- [ ] **Step 3: Implement `BuildFocus`**

Add to `src/Grapht.View/GraphApi.cs`, inside the `GraphApi` class:

```csharp
    /// <summary>One node plus its immediate neighbours — the working state. Bounded by the
    /// node's degree, not by graph size.</summary>
    public static FocusDto? BuildFocus(KnowledgeGraph graph, Analysis analysis, string nodeId)
    {
        if (!graph.Nodes.TryGetValue(nodeId, out var node))
            return null;

        var neighbours = graph.OutEdges(nodeId)
            .Select(e => (Edge: e, Other: e.Target, Direction: "out"))
            .Concat(graph.InEdges(nodeId).Select(e => (Edge: e, Other: e.Source, Direction: "in")))
            .Where(x => graph.Nodes.ContainsKey(x.Other))
            .OrderBy(x => x.Direction, StringComparer.Ordinal)
            .ThenBy(x => x.Other, StringComparer.Ordinal)
            .ThenBy(x => x.Edge.Relation, StringComparer.Ordinal)
            .Select(x => new NeighbourDto(
                Describe(graph, analysis, graph.Nodes[x.Other]),
                x.Edge.Relation, x.Direction, x.Edge.Confidence))
            .ToList();

        return new FocusDto(Describe(graph, analysis, node), neighbours);
    }

    private static NodeDto Describe(KnowledgeGraph graph, Analysis analysis, Node node)
    {
        var community = node.Community ?? 0;
        return new NodeDto(
            node.Id, node.Label, node.Type, node.Span, community,
            analysis.CommunityLabels.GetValueOrDefault(community, $"community {community}"),
            graph.Degree(node.Id), Color(community));
    }
```

And add the records beside the existing ones at the top of the file:

```csharp
public sealed record NodeDto(string Id, string Label, string? Type, string? Span, int Community, string CommunityLabel, int Degree, string Color);
public sealed record NeighbourDto(NodeDto Node, string Relation, string Direction, string Confidence);
public sealed record FocusDto(NodeDto Node, List<NeighbourDto> Neighbours);
```

- [ ] **Step 4: Run to verify both pass**

Run: `dotnet test Grapht.slnx --filter "Focus"`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(view): focus DTO — one node plus neighbours, both directions

Bounded by node degree rather than graph size. Carries spans and
confidence so the front end can render reading targets and edge style."
```

---

### Task 4: `ViewServer` + `grapht view` command

**Files:**
- Create: `src/Grapht.View/ViewServer.cs`
- Modify: `src/Grapht.Cli/Program.cs`, `src/Grapht.Cli/Grapht.Cli.csproj`

**Interfaces:**
- Consumes: `GraphApi.BuildOverview`, `GraphApi.BuildFocus`, `GraphService`.
- Produces: `ViewServer.RunAsync(string solutionPath, int port, bool openBrowser, CancellationToken ct)`.

- [ ] **Step 1: Implement the server**

Create `src/Grapht.View/ViewServer.cs`:

```csharp
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
            var focus = GraphApi.BuildFocus(snapshot.Graph, snapshot.Analysis, Uri.UnescapeDataString(id));
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
```

- [ ] **Step 2: Embed the asset files**

Add to `src/Grapht.View/Grapht.View.csproj` before `</Project>`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Assets\*" />
  </ItemGroup>
```

- [ ] **Step 3: Add the `view` command to the CLI**

In `src/Grapht.Cli/Grapht.Cli.csproj`, add to the existing `ItemGroup` of project references:

```xml
    <ProjectReference Include="..\Grapht.View\Grapht.View.csproj" />
```

In `src/Grapht.Cli/Program.cs`, add before the `var root = new RootCommand(...)` line:

```csharp
var viewPathArg = new Argument<string>("path") { Description = "Solution or directory to view", DefaultValueFactory = _ => "." };
var portOption = new Option<int>("--port") { Description = "Port to serve on", DefaultValueFactory = _ => 5050 };
var noOpenOption = new Option<bool>("--no-open") { Description = "Do not open a browser" };
var viewCmd = new Command("view", "Serve an interactive graph viewer on localhost");
viewCmd.Arguments.Add(viewPathArg);
viewCmd.Options.Add(portOption);
viewCmd.Options.Add(noOpenOption);
viewCmd.SetAction(async (parseResult, ct) =>
{
    await ViewServer.RunAsync(
        parseResult.GetValue(viewPathArg)!,
        parseResult.GetValue(portOption),
        !parseResult.GetValue(noOpenOption),
        ct);
    return 0;
});
```

And register it beside the other subcommands:

```csharp
root.Subcommands.Add(viewCmd);
```

Add `using Grapht.View;` to the top of the file beside the existing usings.

- [ ] **Step 4: Build**

Run: `dotnet build Grapht.slnx -c Release 2>&1 | grep -E "error|Build succeeded"`
Expected: `Build succeeded.` — assets do not exist yet, so do not run the server until Task 5.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(view): loopback view server and \`grapht view\` command

Binds 127.0.0.1 explicitly — this process serves code from disk and must
not be reachable off-machine. Two endpoints so far: overview and focus."
```

---

### Task 5: Front end — overview, search, focus, expand

**Files:**
- Create: `src/Grapht.View/Assets/index.html`, `app.css`, `app.js`, `cytoscape.min.js`
- Test: `tests/Grapht.Tests/ViewPageTests.cs`

**Interfaces:**
- Consumes: `GET /api/graph` → `OverviewDto`, `GET /api/node/{id}` → `FocusDto`.
- Produces: no C# surface.

- [ ] **Step 1: Vendor Cytoscape**

```bash
cd /Users/sandaru/graphify/grapht/src/Grapht.View
mkdir -p Assets
curl -sL https://unpkg.com/cytoscape@3.30.2/dist/cytoscape.min.js -o Assets/cytoscape.min.js
grep -c "cytoscape" Assets/cytoscape.min.js
```
Expected: a non-zero count and a file over 300 KB. This is the only network fetch in the build; the file is committed and never fetched again.

- [ ] **Step 2: Write `index.html`**

Create `src/Grapht.View/Assets/index.html`:

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>grapht</title>
<link rel="stylesheet" href="/app.css">
</head>
<body>
<header>
  <input id="search" type="search" placeholder="Search types, methods, files…" autocomplete="off" spellcheck="false">
  <div id="brand"><strong>grapht</strong> <span id="counts" class="muted"></span></div>
  <button id="theme" type="button" title="Toggle theme" aria-label="Toggle theme">◐</button>
</header>
<main>
  <aside id="filters">
    <h2>Relations</h2>
    <div id="relation-filters"></div>
  </aside>
  <section id="canvas-wrap">
    <div id="canvas"></div>
    <nav id="breadcrumb" aria-label="Focus history"></nav>
  </section>
  <aside id="inspector">
    <p class="empty">Search for a symbol, or click a community to explore it.</p>
  </aside>
</main>
<div id="suggestions" role="listbox" hidden></div>
<script src="/cytoscape.min.js"></script>
<script src="/app.js"></script>
</body>
</html>
```

- [ ] **Step 3: Write `app.css`**

Create `src/Grapht.View/Assets/app.css`:

```css
/* Design tokens. Dark is the media-query default; [data-theme] overrides it both ways. */
:root {
  --ground: #FBFCFD; --surface: #FFFFFF; --border: #DDE3EA;
  --ink: #111820; --muted: #5C6B7A; --accent: #0E7C86;
  --mono: ui-monospace, "JetBrains Mono", "SF Mono", Menlo, monospace;
  --ui: -apple-system, "Segoe UI", system-ui, sans-serif;
}
@media (prefers-color-scheme: dark) {
  :root { --ground: #0F1419; --surface: #161C23; --border: #242D38;
          --ink: #DCE3EB; --muted: #7D8B9A; --accent: #2AA5B0; }
}
:root[data-theme="dark"] { --ground: #0F1419; --surface: #161C23; --border: #242D38;
                           --ink: #DCE3EB; --muted: #7D8B9A; --accent: #2AA5B0; }
:root[data-theme="light"] { --ground: #FBFCFD; --surface: #FFFFFF; --border: #DDE3EA;
                            --ink: #111820; --muted: #5C6B7A; --accent: #0E7C86; }

* { box-sizing: border-box; margin: 0; }
body { font: 13px/1.5 var(--ui); background: var(--ground); color: var(--ink);
       height: 100vh; display: flex; flex-direction: column; }
.muted { color: var(--muted); }

header { height: 56px; flex: none; display: flex; align-items: center; gap: 16px;
         padding: 0 16px; background: var(--surface); border-bottom: 1px solid var(--border); }
#search { flex: 1; max-width: 460px; padding: 7px 11px; font: 13px var(--mono);
          color: var(--ink); background: var(--ground);
          border: 1px solid var(--border); border-radius: 6px; }
#search:focus { outline: 2px solid var(--accent); outline-offset: -1px; }
#brand { margin-left: auto; font-size: 12px; }
#counts { font-variant-numeric: tabular-nums; }
#theme { background: none; border: 1px solid var(--border); border-radius: 6px;
         color: var(--muted); cursor: pointer; width: 30px; height: 30px; }

main { flex: 1; display: flex; min-height: 0; }
#filters { width: 260px; flex: none; padding: 16px; overflow-y: auto;
           background: var(--surface); border-right: 1px solid var(--border); }
#filters h2 { font-size: 10px; text-transform: uppercase; letter-spacing: .06em;
              color: var(--muted); margin-bottom: 8px; font-weight: 600; }
.filter { display: flex; align-items: center; gap: 8px; padding: 3px 0; cursor: pointer; }
.filter input { accent-color: var(--accent); }
.filter .count { margin-left: auto; font: 11px var(--mono); color: var(--muted);
                 font-variant-numeric: tabular-nums; }

#canvas-wrap { flex: 1; min-width: 0; display: flex; flex-direction: column; }
#canvas { flex: 1; min-height: 0; }
#breadcrumb { flex: none; padding: 8px 16px; font: 11px var(--mono); color: var(--muted);
              border-top: 1px solid var(--border); background: var(--surface); min-height: 32px; }
#breadcrumb button { background: none; border: none; color: var(--accent);
                     font: inherit; cursor: pointer; padding: 0; }

#inspector { width: 380px; flex: none; padding: 16px; overflow-y: auto;
             background: var(--surface); border-left: 1px solid var(--border); }
#inspector .empty { color: var(--muted); }
#inspector h3 { font: 15px/1.3 var(--mono); word-break: break-word; }
#inspector .kind { font-size: 11px; color: var(--muted); margin: 2px 0 12px; }
#inspector .span { font: 11px var(--mono); color: var(--accent); }
.rel-group { margin-top: 14px; }
.rel-group h4 { font-size: 10px; text-transform: uppercase; letter-spacing: .06em;
                color: var(--muted); margin-bottom: 4px; font-weight: 600; }
.rel { display: block; width: 100%; text-align: left; background: none; border: none;
       padding: 3px 6px; border-radius: 4px; font: 12px var(--mono); color: var(--ink);
       cursor: pointer; }
.rel:hover, .rel:focus-visible { background: var(--ground); outline: none; }
.rel .conf { color: var(--muted); font-size: 10px; }

#suggestions { position: absolute; top: 48px; left: 16px; width: 460px; max-height: 320px;
               overflow-y: auto; background: var(--surface); border: 1px solid var(--border);
               border-radius: 6px; z-index: 10; }
#suggestions button { display: block; width: 100%; text-align: left; background: none;
                      border: none; padding: 6px 11px; font: 12px var(--mono);
                      color: var(--ink); cursor: pointer; }
#suggestions button:hover, #suggestions button:focus-visible { background: var(--ground); outline: none; }
#suggestions .where { color: var(--muted); font-size: 10px; }

@media (prefers-reduced-motion: reduce) { * { transition: none !important; animation: none !important; } }
@media (max-width: 1100px) { #filters, #inspector { display: none; } }
```

- [ ] **Step 4: Write `app.js`**

Create `src/Grapht.View/Assets/app.js`:

```javascript
'use strict';

const state = { overview: null, focus: null, trail: [], hiddenRelations: new Set() };

const el = {
  search: document.getElementById('search'),
  suggestions: document.getElementById('suggestions'),
  counts: document.getElementById('counts'),
  inspector: document.getElementById('inspector'),
  breadcrumb: document.getElementById('breadcrumb'),
  relationFilters: document.getElementById('relation-filters'),
  theme: document.getElementById('theme'),
};

const cy = cytoscape({
  container: document.getElementById('canvas'),
  style: [
    { selector: 'node', style: {
        'background-color': 'data(color)', label: 'data(label)', color: '#8b93a1',
        'font-family': 'ui-monospace, monospace', 'font-size': 9,
        'text-valign': 'bottom', 'text-margin-y': 4, width: 'data(size)', height: 'data(size)' } },
    { selector: 'node.focus', style: { 'border-width': 3, 'border-color': '#2AA5B0', 'font-size': 12, color: '#dce3eb' } },
    { selector: 'edge', style: {
        width: 1, 'line-color': '#3d4450', 'target-arrow-color': '#3d4450',
        'target-arrow-shape': 'triangle', 'arrow-scale': .6, 'curve-style': 'bezier' } },
    { selector: 'edge[confidence="INFERRED"]', style: { 'line-style': 'dashed' } },
    { selector: 'edge[confidence="AMBIGUOUS"]', style: { 'line-style': 'dotted' } },
  ],
  layout: { name: 'cose', animate: false },
});

async function json(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error((await res.json().catch(() => ({}))).error || res.statusText);
  return res.json();
}

async function loadOverview() {
  state.overview = await json('/api/graph');
  state.trail = [];
  el.counts.textContent = `${state.overview.nodeCount.toLocaleString()} nodes · ${state.overview.edgeCount.toLocaleString()} edges`;
  cy.elements().remove();
  cy.add(state.overview.communities.map(c => ({ data: {
    id: 'c' + c.id, label: c.label, color: c.color,
    size: Math.min(60, 12 + Math.sqrt(c.size) * 2.2), community: c.id, kind: 'community' } })));
  cy.add(state.overview.edges.map(e => ({ data: {
    id: `c${e.source}-c${e.target}`, source: 'c' + e.source, target: 'c' + e.target } })));
  cy.layout({ name: 'cose', animate: false }).run();
  renderBreadcrumb();
  el.inspector.innerHTML = '<p class="empty">Search for a symbol, or click a community to explore it.</p>';
}

async function focusNode(id) {
  const focus = await json('/api/node/' + encodeURIComponent(id));
  state.focus = focus;
  if (state.trail[state.trail.length - 1] !== id) state.trail.push(id);
  renderCanvas();
  renderInspector();
  renderBreadcrumb();
  renderRelationFilters();
}

function visibleNeighbours() {
  return state.focus.neighbours.filter(n => !state.hiddenRelations.has(n.relation));
}

function renderCanvas() {
  const f = state.focus;
  cy.elements().remove();
  cy.add({ data: { id: f.node.id, label: f.node.label, color: f.node.color, size: 34 }, classes: 'focus' });
  const seen = new Set([f.node.id]);
  for (const n of visibleNeighbours()) {
    if (!seen.has(n.node.id)) {
      seen.add(n.node.id);
      cy.add({ data: { id: n.node.id, label: n.node.label, color: n.node.color,
                       size: Math.min(30, 12 + n.node.degree * 0.6) } });
    }
    const [from, to] = n.direction === 'out' ? [f.node.id, n.node.id] : [n.node.id, f.node.id];
    cy.add({ data: { id: `${from}|${to}|${n.relation}`, source: from, target: to,
                     confidence: n.confidence } });
  }
  cy.layout({ name: 'concentric', animate: false, concentric: e => e.hasClass('focus') ? 10 : 1,
              levelWidth: () => 1, minNodeSpacing: 30 }).run();
}

function renderInspector() {
  const f = state.focus;
  const groups = {};
  for (const n of visibleNeighbours()) {
    const key = `${n.direction === 'out' ? '' : 'Incoming '}${n.relation}`;
    (groups[key] ||= []).push(n);
  }
  const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  el.inspector.innerHTML = `
    <h3>${esc(f.node.label)}</h3>
    <div class="kind">${esc(f.node.type ?? 'node')} · ${esc(f.node.communityLabel)} · degree ${f.node.degree}</div>
    ${f.node.span ? `<div class="span">${esc(f.node.span)}</div>` : ''}
    ${Object.entries(groups).sort().map(([name, list]) => `
      <div class="rel-group">
        <h4>${esc(name)} (${list.length})</h4>
        ${list.map(n => `<button class="rel" data-id="${esc(n.node.id)}">${esc(n.node.label)}
          <span class="conf">${n.confidence === 'EXTRACTED' ? '' : esc(n.confidence.toLowerCase())}</span></button>`).join('')}
      </div>`).join('')}`;
  el.inspector.querySelectorAll('.rel').forEach(b =>
    b.addEventListener('click', () => focusNode(b.dataset.id)));
}

function renderBreadcrumb() {
  const parts = ['<button data-i="-1">Overview</button>'];
  state.trail.forEach((id, i) => {
    const label = i === state.trail.length - 1 && state.focus ? state.focus.node.label : id.split(/[.\/]/).pop();
    parts.push(`<button data-i="${i}">${label}</button>`);
  });
  el.breadcrumb.innerHTML = parts.join(' <span class="muted">›</span> ');
  el.breadcrumb.querySelectorAll('button').forEach(b => b.addEventListener('click', () => {
    const i = Number(b.dataset.i);
    if (i < 0) return loadOverview();
    const id = state.trail[i];
    state.trail = state.trail.slice(0, i);
    focusNode(id);
  }));
}

function renderRelationFilters() {
  const counts = {};
  for (const n of state.focus.neighbours) counts[n.relation] = (counts[n.relation] || 0) + 1;
  el.relationFilters.innerHTML = Object.entries(counts).sort().map(([rel, count]) => `
    <label class="filter">
      <input type="checkbox" data-rel="${rel}" ${state.hiddenRelations.has(rel) ? '' : 'checked'}>
      <span>${rel}</span><span class="count">${count}</span>
    </label>`).join('');
  el.relationFilters.querySelectorAll('input').forEach(i => i.addEventListener('change', () => {
    if (i.checked) state.hiddenRelations.delete(i.dataset.rel);
    else state.hiddenRelations.add(i.dataset.rel);
    renderCanvas();
    renderInspector();
  }));
}

cy.on('tap', 'node', evt => {
  const data = evt.target.data();
  if (data.kind === 'community') {
    const c = state.overview.communities.find(x => x.id === data.community);
    if (c && c.topMembers.length) searchFor(c.topMembers[0]);
    return;
  }
  focusNode(data.id);
});

let searchTimer;
el.search.addEventListener('input', () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => searchFor(el.search.value.trim()), 160);
});

async function searchFor(term) {
  if (term.length < 2) { el.suggestions.hidden = true; return; }
  try {
    await focusNode(term);
    el.suggestions.hidden = true;
    el.search.value = '';
  } catch (err) {
    el.suggestions.hidden = false;
    el.suggestions.innerHTML = `<button disabled>${err.message}</button>`;
  }
}

el.theme.addEventListener('click', () => {
  const now = document.documentElement.getAttribute('data-theme');
  const next = now === 'dark' ? 'light' : now === 'light' ? 'dark'
    : (matchMedia('(prefers-color-scheme: dark)').matches ? 'light' : 'dark');
  document.documentElement.setAttribute('data-theme', next);
});

document.addEventListener('keydown', e => {
  if ((e.metaKey || e.ctrlKey) && e.key === 'k') { e.preventDefault(); el.search.focus(); }
});

loadOverview().catch(err => {
  el.inspector.innerHTML = `<p class="empty">Could not load the graph: ${err.message}</p>`;
});
```

- [ ] **Step 5: Write the headless-Chrome test**

Create `tests/Grapht.Tests/ViewPageTests.cs`:

```csharp
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
            return; // headless Chrome unavailable — skip rather than fail the suite

        var port = 5099;
        var repoRoot = TestPaths.RepoRoot;
        using var server = Process.Start(new ProcessStartInfo("dotnet",
            $"run --project \"{Path.Combine(repoRoot, "src", "Grapht.Cli")}\" -- view \"{TestPaths.Fixture}\" --port {port} --no-open")
        { RedirectStandardError = true })!;

        try
        {
            await WaitForServer(port);
            var dom = Path.Combine(Path.GetTempPath(), "grapht-view-" + Guid.NewGuid().ToString("N") + ".html");
            var log = dom + ".log";
            using (var chromeProc = Process.Start(new ProcessStartInfo(chrome,
                $"--headless --disable-gpu --no-sandbox --virtual-time-budget=15000 " +
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
            Assert.Contains("<canvas", html);     // cytoscape rendered
            Assert.Contains("nodes", html);       // counts populated from /api/graph
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
```

- [ ] **Step 6: Add `TestPaths.RepoRoot` if it does not exist**

Check `tests/Grapht.Tests/TestPaths.cs`. If it has no `RepoRoot` member, add one beside the existing members, following whatever pattern the file already uses to locate the fixture directory.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test Grapht.slnx -c Release`
Expected: `Failed: 0`, with the new `ViewerLoadsWithoutConsoleErrors` among the passes.

- [ ] **Step 8: Verify by hand on eShop**

```bash
cd /Users/sandaru/eShop
dotnet run --project /Users/sandaru/graphify/grapht/src/Grapht.Cli -c Release -- view . --port 5050
```
Expected: browser opens on the community overview; typing `CheckoutAsync` focuses that node; its neighbours are clickable; the breadcrumb grows; unchecking `contains` visibly thins the graph.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(view): overview → focus → expand front end

Never renders the whole graph: lands on ~81 community nodes, one search
puts you in a focused neighbourhood, clicking a neighbour expands it.
Relation filters carry live counts because \`contains\` is 46% of edges
and hiding it is what makes the graph readable.

Headless-Chrome test asserts the page runs, not just that assets exist —
the check that PR #7's blank viewer would have failed."
```

---

### Task 6: Documentation

**Files:**
- Modify: `README.md`, `grapht/docs/ARCHITECTURE.md`

- [ ] **Step 1: Document the command in the README**

In the root `README.md`, add `view` to the command list in the Quick start section:

```
grapht view                    # interactive viewer at http://127.0.0.1:5050
```

Add a short subsection under the CLI usage area explaining that `view` serves a live viewer (overview → search → focus → expand), that it binds loopback only, and that `--port` and `--no-open` exist.

- [ ] **Step 2: Add a section to ARCHITECTURE.md**

Add a numbered section after the CLI section describing `Grapht.View`: the two endpoints, the DTO shapes, that `GraphService` now lives in Core and is shared by the MCP server and the view server, and that assets are embedded resources served over HTTP.

- [ ] **Step 3: Commit**

```bash
git add README.md grapht/docs/ARCHITECTURE.md
git commit -m "docs: document \`grapht view\`"
```

---

## Verification

End-to-end, after every task:

```bash
cd /Users/sandaru/graphify/grapht
dotnet test Grapht.slnx -c Release          # all tests green
cd /Users/sandaru/eShop
dotnet run --project /Users/sandaru/graphify/grapht/src/Grapht.Cli -c Release -- view . --port 5050
```

Manual acceptance:
1. Overview shows ~81 community bubbles, sized by membership.
2. `⌘K`, type `CheckoutAsync`, Enter → focused neighbourhood.
3. Clicking a neighbour expands; breadcrumb records the path; clicking "Overview" returns.
4. Unchecking `contains` visibly thins the graph and the inspector's groups.
5. Theme toggle flips light/dark, and both remain legible.
6. `grapht build` still writes a working `graph.html` — this work does not touch it.

## Known limitation carried by this plan

The spec describes search as "type-ahead over all nodes". This plan implements it by
posting the term straight to `/api/node/{id}`, which resolves exact ids only — good enough
to prove the interaction, but it will miss `CheckoutAsync` when the id is
`eShop.WebApp.BasketState.CheckoutAsync`. The proper fix is a `/api/search` endpoint over
`QueryEngine.ResolveNodeCandidates`, which already does fuzzy resolution and ambiguity
reporting. That is deliberately deferred to the next plan alongside `/api/query`; if
manual verification in Task 5 Step 8 proves search too frustrating to use, promote it into
this plan rather than shipping around it.

## Out of scope (spec steps 5–8, a later plan)

`/api/source` and in-panel code display, "Open in VS Code", `/api/query`, and SSE live refresh. Steps 1–4 stand alone as a useful product; those four make it a code navigator and get their own plan once this lands.
