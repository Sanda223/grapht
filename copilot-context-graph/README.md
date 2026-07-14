# copilot-context-graph

Turn a .NET solution into a queryable knowledge graph that GitHub Copilot (VS Code chat / agent mode) uses instead of grepping.

Point it at a `.sln` and it builds a graph of every type, method, namespace, Blazor component, route, and JS-interop call — who calls what, who implements what, which page renders which component. A resident MCP server keeps the graph fresh as you edit and gives Copilot `query`, `path`, `explain`, and `impact` tools. Extraction is Roslyn's semantic model: deterministic, no LLM, fully local.

Inspired by [Graphify](https://github.com/Graphify-Labs/graphify) (MIT, © Safi Shamsi), rebuilt in C# and narrowed to one job: **C#/Blazor codebases, consumed through Copilot in VS Code.**

## Quick start

```bash
# from the repo root
dotnet pack src/ContextGraph.Cli -o nupkg
dotnet pack src/ContextGraph.Mcp -o nupkg
dotnet tool install -g ContextGraph.Cli --add-source ./nupkg
dotnet tool install -g ContextGraph.Mcp --add-source ./nupkg
```

Build a graph and poke at it from a terminal:

```bash
cd ~/code/MySolution
context-graph build            # → context-graph-out/{graph.json, GRAPH_REPORT.md, graph.html}
context-graph query "how does the basket checkout work"
context-graph path "Checkout.razor" "Order"
context-graph explain "IBasketService"
context-graph stats
```

`graph.html` is a single self-contained file (vendored vis-network, zero network access) — open it in any browser for the clickable community-colored map.

## Copilot integration

1. Copy `templates/mcp.json` into your solution as `.vscode/mcp.json`.
2. Append `templates/copilot-instructions-snippet.md` to your `.github/copilot-instructions.md`.
3. Open the workspace — VS Code starts `context-graph-mcp`, which builds the graph in the background and then keeps it fresh via a file watcher (a save is re-extracted incrementally in ~seconds).

Copilot then answers architecture questions through the graph:

| Tool | What Copilot gets |
|---|---|
| `query_graph` | plain-language question → relevant subgraph with `path:line` refs |
| `shortest_path` | hop-by-hop trace between any two concepts |
| `get_node` / `get_neighbors` | one symbol in full detail |
| `impact_of_change` | reverse-dependency closure — what breaks if this changes |
| `god_nodes` / `graph_stats` | the de-facto core of the architecture / graph health |
| `rebuild_graph` | force a full re-extract |

## What the graph knows

- **C# (Roslyn semantic):** classes, interfaces, structs, records, enums, methods; `calls` (resolved to the exact symbol, cross-project), `inherits`/`implements`, `imports`, typed `references` (field / parameter / return / generic / attribute). Only symbols declared in your source become nodes — BCL/NuGet noise never enters.
- **Blazor/Razor (scanner):** components, `@page` routes, `@inject` wiring, component-tag usage, `@code` methods, calls through injected services, and `IJSRuntime` interop (`js:` nodes) — the C#→JavaScript boundary without parsing JS.
- **The seams between technologies** — the places grep fails hardest: NuGet packages per project (with version drift visible), DI registrations (`bound_to` — what the container *actually* injects), MassTransit publish/consume flows joined through message contracts, both directions of JS interop, attribute routes shared between API controllers and HttpClient callers, SignalR events and hub methods, SQL tables/procs linked from migrations *and* Dapper call sites, appsettings keys with their readers (and unread keys exposed), Bicep/docker-compose/pipeline infra.
- **Every edge carries a confidence tag:** `EXTRACTED` (semantically resolved), `INFERRED` (heuristic, e.g. razor service calls), `AMBIGUOUS` (couldn't resolve — e.g. degraded legacy-project loads). Legacy .NET Framework csproj files load with diagnostics instead of failing.
- **Louvain community detection** (deterministic, hand-rolled) names each cluster after its hub; `GRAPH_REPORT.md` lists god nodes, surprising cross-project connections, and suggested questions.

Real-world scale: eShopOnWeb (~200 files) → 1,147 nodes / 2,190 edges / 65 communities in ~8 s.

## Layout

```
src/ContextGraph.Core/    pipeline: load → extract → resolve → build → cluster → analyze → export
src/ContextGraph.Cli/     dotnet tool `context-graph`: build/query/path/explain/stats
src/ContextGraph.Mcp/     dotnet tool `context-graph-mcp`: stdio MCP server + file watcher
tests/ContextGraph.Tests/ 34 tests incl. byte-determinism and known-answer clustering
docs/                     ARCHITECTURE.md (the build spec) and ROADMAP.md
```

Requires the .NET 10 SDK. Analyzed projects can be anything Roslyn loads — SDK-style and legacy .NET Framework csproj (the latter needs MSBuild/VS Build Tools for full resolution; otherwise it degrades to AMBIGUOUS-tagged syntax extraction).

## License

MIT. Design credit: [Graphify](https://github.com/Graphify-Labs/graphify), MIT License, Copyright (c) 2026 Safi Shamsi. `graph.html` embeds [vis-network](https://visjs.github.io/vis-network/) (MIT/Apache-2.0).
