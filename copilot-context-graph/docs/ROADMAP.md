# Roadmap

Build order for copilot-context-graph. Each milestone ends runnable and verifiable — no milestone depends on a later one to demonstrate value. Contracts live in [ARCHITECTURE.md](ARCHITECTURE.md); section references below point there.

## M0 — Scaffold

- `ContextGraph.sln`, projects `src/ContextGraph.Core`, `src/ContextGraph.Cli`, `src/ContextGraph.Mcp`, `tests/ContextGraph.Tests` (net10.0).
- Packages: `Microsoft.CodeAnalysis.CSharp.Workspaces`, `Microsoft.CodeAnalysis.Workspaces.MSBuild`, `System.CommandLine`, `ModelContextProtocol`, `xunit`. (Not `Microsoft.Build.Locator` — see §4.3.)
- Test fixture: `tests/fixtures/SampleApp/` — small solution with 2 class-library projects + 1 Razor class library (Blazor components) + 1 legacy-style csproj. The legacy project intentionally does not compile with `dotnet build` on macOS; it exists to exercise load diagnostics and the AMBIGUOUS fallback.

**Done when:** `dotnet build` and `dotnet test` pass (one placeholder test). ✅ 2026-07-13

## M1 — Load a solution

- `SolutionLoader` (§4.3): MSBuildLocator registration, `OpenSolutionAsync`, diagnostics collection, Razor file enumeration from disk.

**Done when:** `context-graph build tests/fixtures/SampleApp` prints every project, document count, and any load diagnostics.

## M2 — Roslyn extraction → graph.json

- Data model types (§3), `RoslynExtractor` (§4.1), `GraphBuilder`, `JsonExporter` writing `node_link_data` format with `links` key.
- Confidence tagging incl. the syntax-only AMBIGUOUS fallback.

**Done when:** `build` on the fixture emits `graph.json` with classes, methods, `contains`/`calls`/`inherits`/`implements`/`imports`/`references` edges; golden-file test locks the output.

## M3 — Razor + Blazor

- `RazorExtractor` (§4.2): component tags, `@inject`, `@page` routes, `@code` blocks, plus JS-interop edge capture in the Roslyn walker.

**Done when:** the fixture's Blazor project shows component→component `calls` edges and route nodes in `graph.json`.

## M4 — Cluster, analyze, report, HTML

- Louvain (§5), Analyzer (§6), `GRAPH_REPORT.md` writer, `HtmlExporter` with vendored vis-network (§10).

**Done when:** `build` emits all three outputs; `graph.html` opens offline, colored by community; report lists god nodes and suggested questions. Determinism test: two runs produce identical `graph.json`.

## M5 — Query engine + CLI

- Scorer, seed picker, BFS/DFS with hub guard, budgeted text renderer, path, explain (§7); wire `query`/`path`/`explain`/`stats` CLI verbs.

**Done when:** query/path/explain against the fixture graph match golden text outputs; run it against a real work solution and sanity-check answers.

## M6 — MCP server + watcher

- `ContextGraph.Mcp` (§8): stdio server, all 8 tools, FileSystemWatcher with debounced incremental splice, re-cluster-on-drift.
- `.vscode/mcp.json` template + copilot-instructions snippet in the repo.

**Done when:** Copilot in VS Code answers "what calls X?" via `query_graph`, and editing a file then re-asking reflects the change without a manual rebuild.

## M7 — Packaging & polish

- Pack CLI and MCP server as dotnet tools; README quick-start; run against the biggest real solution available and fix what falls out (perf, noise, load failures).

**Done when:** `dotnet tool install -g` on a clean machine → working end-to-end in a real project in under 5 minutes.

## Later / explicitly deferred

- MCP sampling for docs/markdown semantic extraction
- JS/TS extraction (tree-sitter bindings) for frontend assets
- `graph_diff` between builds; PR impact tools
- Community renaming via LLM
