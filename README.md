# grapht

Turn a .NET solution into a queryable knowledge graph that GitHub Copilot (VS Code chat / agent mode) uses instead of grepping.

Point it at a `.sln` and it builds a graph of every type, method, namespace, Blazor component, route, and JS-interop call — who calls what, who implements what, which page renders which component. A resident MCP server keeps the graph fresh as you edit and gives Copilot `query`, `path`, `explain`, and `impact` tools. Extraction is Roslyn's semantic model: deterministic, no LLM, fully local.

Inspired by [Graphify](https://github.com/Graphify-Labs/graphify) (MIT, © Safi Shamsi), rebuilt in C# and narrowed to one job: **C#/Blazor codebases, consumed through Copilot in VS Code.**

## Quick start

Full fresh-machine instructions (prerequisites, PATH setup, macOS/Windows/Linux notes, troubleshooting): see the **[Full setup guide](#full-setup-guide-fresh-machine)** at the bottom of this file.

```bash
# from the repo root
cd grapht
dotnet pack src/Grapht.Cli -o nupkg
dotnet pack src/Grapht.Mcp -o nupkg
dotnet tool install -g Grapht.Cli --add-source ./nupkg
dotnet tool install -g Grapht.Mcp --add-source ./nupkg
```

Build a graph and poke at it from a terminal:

```bash
cd ~/code/MySolution
grapht build            # → grapht-out/{graph.json, GRAPH_REPORT.md, graph.html}
grapht query "how does the basket checkout work"
grapht path "Checkout.razor" "Order"
grapht explain "IBasketService"
grapht stats
grapht diff base.json head.json   # structural diff between two builds (PR-ready markdown)
grapht report-usage               # summarize MCP tool usage from the query log
grapht view                       # interactive viewer at http://127.0.0.1:5050
```

`graph.html` is a single self-contained file (vendored vis-network, zero network access) — open it in any browser for the clickable community-colored map.

## Interactive viewer

`grapht view [path] [--port N] [--no-open]` serves a live graph viewer on `127.0.0.1` (loopback only, never reachable off-machine). It lands on a community overview — bubbles sized by membership — and lets you search or click a node to focus on it and its neighbours, click a neighbour to expand outward, and use the breadcrumb to navigate back. Relation filters show live counts, and there's a light/dark theme toggle.

`--port` picks the port (default `5050`); `--no-open` skips launching a browser. Unlike `grapht build`, which writes graph.json once, `view` keeps a `GraphService` running and re-extracts on file changes, so the graph stays current as you edit.

Use `graph.html` when you want a shareable, offline artifact — email it, attach it to a CI run. Use `view` when you're working in a solution and want a live, clickable map of it; it does not replace `graph.html`, and `grapht build` is unaffected.

Note: search currently resolves exact node ids only (e.g. `eShop.WebApp.BasketState.CheckoutAsync`, not `CheckoutAsync`) — a fuzzy search endpoint is planned for a follow-up.

## Copilot integration

1. Copy `templates/mcp.json` into your solution as `.vscode/mcp.json`.
2. Append `templates/copilot-instructions-snippet.md` to your `.github/copilot-instructions.md`.
3. Open the workspace — VS Code starts `grapht-mcp`, which builds the graph in the background and then keeps it fresh via a file watcher (a save is re-extracted incrementally in ~seconds).

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
- **Docs linked to code** — markdown headings and doc↔doc links are graphed, and backticked identifiers in prose resolve to the code they mention, so "which doc explains X" is a graph lookup.
- **The seams between technologies** — the places grep fails hardest: NuGet packages per project (with version drift visible), DI registrations (`bound_to` — what the container *actually* injects), MassTransit publish/consume flows joined through message contracts, both directions of JS interop, attribute routes shared between API controllers and HttpClient callers, SignalR events and hub methods, SQL tables/procs linked from migrations *and* Dapper call sites, appsettings keys with their readers (and unread keys exposed), Bicep/docker-compose/pipeline infra.
- **Every edge carries a confidence tag:** `EXTRACTED` (semantically resolved), `INFERRED` (heuristic, e.g. razor service calls), `AMBIGUOUS` (couldn't resolve — e.g. degraded legacy-project loads). Legacy .NET Framework csproj files load with diagnostics instead of failing.
- **Louvain community detection** (deterministic, hand-rolled) names each cluster after its hub; `GRAPH_REPORT.md` lists god nodes, surprising cross-project connections, and suggested questions.

Real-world scale: eShopOnWeb (~200 files) → 1,147 nodes / 2,190 edges / 65 communities in ~8 s.

## Layout

```
grapht/src/Grapht.Core/    pipeline: load → extract → resolve → build → cluster → analyze → export
src/Grapht.Cli/     dotnet tool `grapht`: build/query/path/explain/stats
src/Grapht.Mcp/     dotnet tool `grapht-mcp`: stdio MCP server + file watcher
tests/Grapht.Tests/ 34 tests incl. byte-determinism and known-answer clustering
docs/                     ARCHITECTURE.md (the build spec) and ROADMAP.md
```

Requires the .NET 10 SDK. Analyzed projects can be anything Roslyn loads — SDK-style and legacy .NET Framework csproj (the latter needs MSBuild/VS Build Tools for full resolution; otherwise it degrades to AMBIGUOUS-tagged syntax extraction).

---

# Full setup guide (fresh machine)

Everything below assumes a machine that has never seen this tool. Commands are identical on macOS, Windows, and Linux unless a platform note says otherwise (macOS: Terminal/zsh; Windows: PowerShell; Linux: bash).

## 1. Prerequisites

| Requirement | Why | Check |
|---|---|---|
| **.NET 10 SDK** | builds and runs the tools | `dotnet --list-sdks` shows a `10.x` entry |
| **Git** | clone this repo | `git --version` |
| **VS Code + GitHub Copilot extension** | only needed for the MCP/Copilot integration — the CLI works without it | Copilot Chat opens in VS Code |

- .NET 10 SDK: <https://dotnet.microsoft.com/download/dotnet/10.0> (macOS: pkg installer; Windows: exe installer, or `winget install Microsoft.DotNet.SDK.10`; Linux: distro package, e.g. Ubuntu `sudo apt-get install -y dotnet-sdk-10.0`, or the [dotnet-install script](https://learn.microsoft.com/dotnet/core/install/linux) for any distro).
- **Windows only, optional:** to analyze legacy .NET *Framework* (non-SDK-style) projects with full symbol resolution, have Visual Studio or [VS Build Tools](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022) installed. Without them, legacy projects still load but degrade to `AMBIGUOUS`-tagged extraction (the report tells you when this happens). Modern SDK-style projects need nothing extra on any OS. (On macOS and Linux there is no Build Tools option — legacy Framework projects always take the degraded path there.)

## 2. Get the code and build the packages

```bash
git clone https://github.com/Sanda223/grapht.git
cd grapht/grapht
dotnet pack src/Grapht.Cli -o nupkg
dotnet pack src/Grapht.Mcp -o nupkg
```

This produces two local NuGet packages in `./nupkg`. Nothing is published anywhere — the tool is not on nuget.org, which is why every install command below needs `--add-source`.

## 3. Install the two tools globally

```bash
dotnet tool install -g Grapht.Cli --add-source ./nupkg
dotnet tool install -g Grapht.Mcp --add-source ./nupkg
```

This installs two commands machine-wide (it does not matter what folder you run this from):

- `grapht` — the CLI (build/query/path/explain/stats)
- `grapht-mcp` — the MCP server VS Code will launch for Copilot

## 4. Make sure the tools are on PATH

Global dotnet tools live in `~/.dotnet/tools` (macOS/Linux) / `%USERPROFILE%\.dotnet\tools` (Windows).

- **Windows:** the .NET installer adds this to PATH automatically. Open a *new* terminal and run `grapht --help`. If it's not found: `setx PATH "%PATH%;%USERPROFILE%\.dotnet\tools"` and open a new terminal.
- **macOS:** the installer's PATH entry is broken on some setups (it writes a literal unexpanded `~` into `/etc/paths.d/dotnet-cli-tools`). Fix it once:

  ```bash
  cat << \EOF >> ~/.zprofile
  export PATH="$PATH:$HOME/.dotnet/tools"
  EOF
  export PATH="$PATH:$HOME/.dotnet/tools"   # current session too
  ```

- **Linux:** package-manager installs usually do not touch PATH. Add it once (use `~/.zshrc` if you run zsh):

  ```bash
  echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc
  export PATH="$PATH:$HOME/.dotnet/tools"   # current session too
  ```

**Verify:** `grapht --help` prints the command list. If so, installation is done.

## 5. Use the CLI on any solution

```bash
cd path/to/YourSolution
dotnet restore            # important: full symbol resolution needs restored packages
grapht build
```

Outputs land in `grapht-out/`:

| File | What it is |
|---|---|
| `graph.html` | clickable visual map — open in any browser, works fully offline |
| `GRAPH_REPORT.md` | written summary: communities, god nodes, surprises, suggested questions, load diagnostics |
| `graph.json` | the raw graph (stable documented format) |

Then query from the terminal anytime:

```bash
grapht query "how does checkout work"
grapht path "Checkout.razor" "Order"
grapht explain "IBasketService"
grapht stats
grapht diff base.json head.json   # structural diff (PR-comment ready)
grapht report-usage               # MCP tool-usage summary
```

## 6. Wire up Copilot (the MCP server)

Do this once per solution you want Copilot to understand.

**a.** Create `.vscode/mcp.json` in the solution root:

```json
{
  "servers": {
    "grapht": {
      "type": "stdio",
      "command": "grapht-mcp",
      "args": ["--solution", "${workspaceFolder}"]
    }
  }
}
```

**b.** Copy the contents of [`templates/copilot-instructions-snippet.md`](grapht/templates/copilot-instructions-snippet.md) into the solution's `.github/copilot-instructions.md` (create the file if it doesn't exist). This teaches Copilot the intended workflow: *use the graph to locate code, then read the referenced files, then answer from code.*

**c.** Open the solution folder in VS Code (or reload the window). Approve the MCP server if VS Code asks. The server builds the graph in the background — the first minute after opening, answers may come from a still-warming graph.

**d.** Verify: open Copilot Chat in **agent mode**, click the tools icon in the chat input — a `grapht` server should list 8 tools (`query_graph`, `shortest_path`, `impact_of_change`, `get_node`, `get_neighbors`, `god_nodes`, `graph_stats`, `rebuild_graph`).

From here there is no daily workflow: the file watcher keeps the graph fresh on every save, and Copilot calls the tools on its own when you ask structural questions ("what calls X?", "what breaks if I change Y?", "how do these connect?").

## 7. Updating the tool later

```bash
cd grapht/grapht
git pull
dotnet pack src/Grapht.Cli -o nupkg
dotnet pack src/Grapht.Mcp -o nupkg
dotnet tool update -g Grapht.Cli --add-source ./nupkg
dotnet tool update -g Grapht.Mcp --add-source ./nupkg
```

(`dotnet tool update` only swaps binaries when the version number changed; bump `<Version>` in both csproj files when making local changes.) After updating, reload any VS Code window running the MCP server.

## 8. Uninstall

```bash
dotnet tool uninstall -g Grapht.Cli
dotnet tool uninstall -g Grapht.Mcp
```

Per-project traces are just the two files you added (`.vscode/mcp.json`, the instructions section) plus the `grapht-out/` folder.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `command not found: grapht` | PATH — see step 4; open a new terminal after fixing |
| `Grapht.Cli is not found in NuGet feeds` | you forgot `--add-source ./nupkg`, or you're not in the repo folder — the packages are local-only |
| Graph builds but has few `calls` edges | you skipped `dotnet restore` before `grapht build` |
| Lots of `[Failure] Msbuild failed … vulnerability` diagnostics | NuGet audit noise from the target solution; extraction continues, ignore |
| `Android SDK directory could not be found` diagnostics | the solution has MAUI mobile targets you can't build locally; those targets load degraded, the rest is unaffected |
| Copilot doesn't show the tools | check `.vscode/mcp.json` location/spelling; View → Output → "MCP" for the server log; make sure Copilot Chat is in **agent mode** |
| First tool call is slow after opening VS Code | expected — the graph is building in the background; big solutions take a minute or two |
| `graph.html` says "too large to render" | the graph exceeds 5,000 nodes; use the CLI/MCP queries and the report instead (a community-aggregated view is on the roadmap) |

## License

MIT. Design credit: [Graphify](https://github.com/Graphify-Labs/graphify), MIT License, Copyright (c) 2026 Safi Shamsi. `graph.html` embeds [vis-network](https://visjs.github.io/vis-network/) (MIT/Apache-2.0).
