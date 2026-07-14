# Architecture

copilot-context-graph builds a knowledge graph from a .NET solution and serves it to GitHub Copilot (VS Code) through MCP. This document is the build spec: contracts first, then each component.

It deliberately mirrors Graphify's data contracts (graph.json schema, confidence tags, relation vocabulary, query semantics) so the reference implementation's behavior and tests can be used as an oracle. Where we diverge, the divergence is called out in §10.

## 1. Goals and non-goals

**Goals**

- Graph any .NET solution: modern SDK-style projects and legacy .NET Framework csproj.
- C# extraction via Roslyn's **semantic model** — real symbol resolution, not syntax guessing.
- Blazor awareness: `.razor`/`.cshtml` component usage, `@inject`, `@page` routes, JS interop calls.
- First-class Copilot integration: a resident MCP stdio server with compact, `path:line`-bearing tool results.
- Always-fresh graph: the server watches the workspace and re-extracts changed files incrementally.
- Deterministic, LLM-free, fully local.

**Non-goals (v1)**

- Other languages (JS/TS, Python, …) — no tree-sitter, no grammar matrix.
- Other assistants/platforms — no skill files, no install matrix.
- Docs/PDF/media semantic extraction — possible later via MCP sampling.
- Graph database export (Neo4j/FalkorDB), Obsidian vault, wiki, SVG.

## 2. Pipeline

```
LoadSolution → Extract (Roslyn + Razor) → BuildGraph → Cluster → Analyze → Export
```

Each stage is a pure-ish component in `ContextGraph.Core`, communicating through the types in §3. No shared state; only `Export` writes to disk (`context-graph-out/`).

| Stage | Component | Input → Output |
|---|---|---|
| Load | `SolutionLoader` | `.sln`/`.csproj` path → `Microsoft.CodeAnalysis.Solution` |
| Extract | `RoslynExtractor` | `Document` → `Extraction { Nodes, Edges }` |
| Extract | `RazorExtractor` | `.razor`/`.cshtml` file → `Extraction` |
| Build | `GraphBuilder` | `Extraction[]` → `KnowledgeGraph` (dedup, id resolution) |
| Cluster | `Louvain` | graph → `community` attribute on every node |
| Analyze | `Analyzer` | graph → `Analysis` (god nodes, surprises, suggested questions) |
| Export | `JsonExporter`, `HtmlExporter`, `ReportWriter` | graph + analysis → `graph.json`, `graph.html`, `GRAPH_REPORT.md` |

## 3. Data model and graph.json contract

The on-disk format is Graphify's exactly: NetworkX `node_link_data` JSON with edges under the key **`links`** (not `edges`).

```jsonc
{
  "directed": true,
  "multigraph": false,
  "graph": {},
  "nodes": [ /* Node objects */ ],
  "links": [ /* Edge objects */ ],
  "built_at_commit": "<git sha or empty>"
}
```

**Node**

| Field | Type | Notes |
|---|---|---|
| `id` | string | canonical, path-qualified, stable across runs (e.g. `src_auth_LoginService_cs::LoginService`) |
| `label` | string | display name (`LoginService`, `Login.razor`) |
| `file_type` | string | `code` \| `concept` (routes, namespaces get `type` too) |
| `type` | string? | `class` \| `interface` \| `struct` \| `record` \| `enum` \| `method` \| `namespace` \| `component` \| `route` |
| `source_file` | string | repo-relative path |
| `source_location` | string? | `L<line>` |
| `community` | int | injected by clustering |
| `metadata` | object? | `namespace`, `is_nested_type`, `project`, … |

**Edge**

| Field | Type | Notes |
|---|---|---|
| `source`, `target` | string | node ids |
| `relation` | string | vocabulary in §4 |
| `context` | string? | `call` \| `import` \| `field` \| `parameter_type` \| `return_type` \| `generic_arg` \| `attribute` |
| `confidence` | string | `EXTRACTED` \| `INFERRED` \| `AMBIGUOUS` |
| `weight` | float | default 1.0 |
| `source_file`, `source_location` | string | where the edge was observed |

**Confidence semantics** (unchanged from Graphify, but Roslyn shifts the distribution):

- `EXTRACTED` — explicit in source **and** semantically resolved (Roslyn `GetSymbolInfo` returned the exact symbol). Most edges land here.
- `INFERRED` — resolved by our own heuristics (e.g. Razor component tag matched to a class by name, delegate/interface dispatch target candidates).
- `AMBIGUOUS` — could not resolve (semantic model unavailable — broken project load — or multiple candidate symbols). Flagged in the report.

In-memory, `KnowledgeGraph` is a directed adjacency structure (`Dictionary<string, Node>` + edge list with per-node in/out indexes). No graph library dependency; we need exactly: degree, BFS/DFS, shortest path (bidirectional BFS on the undirected view), and edge iteration.

## 4. Extraction

### 4.1 RoslynExtractor (C#)

Per `Document`: get `SyntaxTree` + `SemanticModel`, walk with a `CSharpSyntaxWalker`.

Nodes emitted: file, namespace, class/interface/struct/record/enum, method (incl. constructors, local functions rolled up to their containing method).

Edges emitted (relation → how):

| Relation | Source construct | Resolution |
|---|---|---|
| `contains` | type → member, file → type | syntax |
| `imports` | `using` directive (metadata: `using_kind` = namespace/static/alias) | syntax |
| `calls` | `InvocationExpressionSyntax`, `ObjectCreationExpressionSyntax` | `SemanticModel.GetSymbolInfo` → exact target = EXTRACTED; `CandidateSymbols` = AMBIGUOUS |
| `inherits` / `implements` | base list | `GetTypeInfo` on each base — Roslyn tells us class vs interface directly (no name-convention guessing) |
| `references` | field/property/parameter/return/generic-arg/attribute types | `GetTypeInfo`, `context` set accordingly |
| `bound_to` | `AddScoped/AddSingleton/AddTransient/TryAdd*<TService, TImpl>()` | name-based, both type args in source — what the DI container actually injects |
| `publishes` / `consumes` | `Publish<T>`/`Send<T>` calls (MassTransit namespace) / `IConsumer<T>` base list | message flow joins publisher and consumer through the contract type |
| `uses` | `PackageReference` in csproj | project → `pkg:` node; version kept on the edge so drift across projects stays visible. `ProjectReference` → project→project `references` |

Cross-technology seams (Tier 2/3) join both sides at shared concept nodes; string-literal matches are tagged `INFERRED`:

| Seam | Node | Sides |
|---|---|---|
| C# ↔ JS | `js:<fn>`, `[JSInvokable]` methods | `InvokeAsync("fn")` stubs merge with function defs scanned from first-party .js; `DotNet.invokeMethodAsync` resolves via `js_invokable` metadata |
| WASM ↔ API | `route:<path>` | `[Route]`/`[Http*]` attributes (server) and HttpClient verb calls with string literals (client) normalize to one path node |
| SignalR | `event:<name>`, hub methods | `Clients.SendAsync` publishes / `connection.On` consumes; `HubConnection.InvokeAsync` resolves to methods on `Hub`-derived types (receiver-type disambiguation vs JS interop) |
| C# ↔ SQL | `table:`/`proc:` | `.sql` `CREATE` statements declare; Dapper-method SQL literals are mined for `FROM/JOIN/INTO/UPDATE/EXEC` (names normalized: last segment, lowercase, unbracketed) |
| Code ↔ config | `config:<A:B>` | appsettings key tree declares; `config["A:B"]`/`GetSection`/`GetConnectionString` reads reference — unread keys are the dead-setting signal |
| Infra | `infra:`, `service:`, `script:` | Bicep resources + module imports; docker-compose services + `depends_on`; pipeline YAML → script references |

Rules:

- Only user-declared symbols become nodes. References to BCL/NuGet types are dropped (they're the noise Graphify fights with `_BUILTIN_NOISE_LABELS`; we cut them at the source by checking `symbol.Locations.Any(l => l.IsInSource)`).
- Node ids come from Roslyn's fully-qualified symbol name (`SymbolDisplayFormat.FullyQualifiedFormat`), sanitized. This makes cross-file/cross-project resolution automatic — the entire resolver layer Graphify needs (`symbol_resolution.py`, `extractors/csharp.py` cross-file passes) does not exist here.
- If a project loads without a usable semantic model (common with broken legacy Framework references), degrade to syntax-only extraction and tag every resolution-dependent edge `AMBIGUOUS`.

### 4.2 RazorExtractor (Blazor)

`.razor`/`.cshtml` are not in the semantic model by default, so this is a line-oriented scanner (same approach as reference `extractors/razor.py`), plus one semantic assist:

- `@using`, `@inject` → `imports` (EXTRACTED)
- `@inherits` → `inherits` (EXTRACTED)
- `@page "/route"` → `references` to a `route:<path>` concept node (EXTRACTED)
- `<PascalCase …>` tags that are not known HTML elements → `calls` edge to the component; INFERRED, upgraded to EXTRACTED when a matching component class/`.razor` file exists in the graph
- `@code { }` block → parsed with Roslyn as a detached snippet; methods become nodes, `contains` edges
- **JS interop**: `IJSRuntime.InvokeAsync<T>("identifier", …)` string literals (found by the Roslyn walker in §4.1, not here) → `calls` edge to a `js:<identifier>` concept node, INFERRED. This captures the C#→JS boundary without parsing any JavaScript.

### 4.3 SolutionLoader

- `MSBuildWorkspace.OpenSolutionAsync`, collect `WorkspaceFailed` diagnostics; report them in `GRAPH_REPORT.md` instead of failing the run. (No `Microsoft.Build.Locator` needed: Roslyn ≥ 4.12 discovers MSBuild via its out-of-process BuildHost, and Locator's build-time guard actually breaks the build if referenced alongside it.)
- Legacy .NET Framework csproj: loads via the same path when VS Build Tools/MSBuild are present; missing reference assemblies produce a degraded semantic model → §4.1's AMBIGUOUS fallback keeps output honest.
- Razor files are enumerated from disk (`AdditionalDocuments` is unreliable across project styles).

## 5. Clustering

Louvain community detection, implemented in `ContextGraph.Core` (~200 lines; no .NET package has a maintained Leiden). Ported behaviors from reference `cluster.py`:

- Seeded/deterministic: sort nodes and edges before processing; fixed iteration order (reference uses `seed=42` for the same reason).
- Cluster the undirected view; isolated nodes become singleton communities.
- Split oversized communities (> 25% of all nodes, min size 10) with a second pass.
- Re-index community ids by size descending, ties broken by sorted member ids → stable ids run-to-run.
- Community labels: name each community after its highest-degree member (LLM-free, reference `label_communities_by_hub`).

## 6. Analysis

Port of reference `analyze.py` heuristics, simplified because Roslyn kills most noise classes:

- **God nodes**: top-N by degree, excluding file nodes and concept nodes.
- **Surprising connections**: score edges — confidence bonus (AMBIGUOUS 3 / INFERRED 2 / EXTRACTED 1), cross-community +1, cross-project +2, peripheral→hub +1; skip `contains`/`imports` edges; dedupe one per community pair; each result carries a `why` string.
- **Suggested questions**: any AMBIGUOUS edge; top bridge nodes by betweenness (sampled `k=min(100, N)` when N > 1000); high-degree nodes with ≥ 2 INFERRED edges; isolated nodes; low-cohesion communities (cohesion < 0.15, ≥ 5 members).

Output feeds `GRAPH_REPORT.md`: stats, communities, god nodes, surprises, suggested questions, load diagnostics.

## 7. Query engine

Lives in `ContextGraph.Core` so the CLI and MCP server share it. Port of reference `serve.py` semantics:

**Seed matching** — tokenize (`\w+`, lowercased, diacritics stripped), drop stopwords, then tiered scoring with IDF weighting: exact-match 1000 × idf, prefix 100 × idf, substring 1 × idf, source-file match 0.5; whole-query exact/prefix tier × 10; scale exact/prefix by (matched terms / total terms)². Pick ≤ 3 seeds, cut when score < 0.2 × top score, dedupe by normalized label, guarantee one seed per distinct query term (break ties by degree).

**Traversal** — BFS (default) or DFS from seeds, depth 2 (CLI) / ≤ 6 (MCP param, default 3). Hub guard: nodes with degree ≥ max(50, p99 of degree distribution) are not expanded as transit unless they are seeds.

**Rendering** — plain text, budgeted (`token_budget` default 2000; char budget = 3 × tokens):

```
Traversal: BFS depth=2 | Start: [LoginService] | 23 nodes found

NODE LoginService [src=src/Auth/LoginService.cs loc=L14 community=Auth]
EDGE LoginService --calls [EXTRACTED context=call]--> TokenValidator
...
```

**path** — resolve both endpoints with the same scorer, bidirectional BFS on the undirected view, print hop-by-hop with original edge directions. **explain** — resolve one node (precedence: source-file exact → label/id exact → prefix → substring), print id/source/type/community/degree plus connections capped at 20 by degree.

## 8. MCP server (`ContextGraph.Mcp`)

Built on the official `ModelContextProtocol` NuGet package, stdio transport. This is the product; the CLI is a debugging convenience.

**Lifecycle**: on start, load `context-graph-out/graph.json` if present, else build from the workspace solution. Hold the graph in memory. A `FileSystemWatcher` on `*.cs`, `*.razor`, `*.cshtml`, `*.csproj`, `*.sln` triggers debounced (~2 s) incremental re-extraction of changed files: drop that file's nodes/edges, re-run the extractor, splice, re-cluster only when node/edge count drifts > 10% since last clustering. Copilot never sees a stale graph, and there is no cache subsystem.

**Tools** (names shared with Graphify where behavior matches; all results are compact text with `path:line` references so Copilot can jump to code):

| Tool | Params | Returns |
|---|---|---|
| `query_graph` | `question` (req), `mode` bfs/dfs, `depth` ≤ 6, `token_budget`, | traversal text (§7) |
| `shortest_path` | `source`, `target` (req), `max_hops` = 8 | hop-by-hop path text |
| `get_node` | `label` (req) | node detail block (= `explain`) |
| `get_neighbors` | `label` (req), `relation_filter` | in/out edge list |
| `god_nodes` | `top_n` = 10 | ranked hub list |
| `graph_stats` | — | node/edge/community counts, confidence % |
| `impact_of_change` | `label` (req), `depth` = 2 | reverse-dependency closure: what breaks if this changes |
| `rebuild_graph` | `full` bool | forces re-extract; returns new stats |

`impact_of_change` and `rebuild_graph` are ours; the rest mirror reference `serve.py`.

**Registration**: ship a `.vscode/mcp.json` template:

```jsonc
{
  "servers": {
    "context-graph": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["context-graph-mcp", "--solution", "${workspaceFolder}"]
    }
  }
}
```

plus a 3-line `.github/copilot-instructions.md` snippet: *"For questions about architecture, relationships, call paths, or impact of changes, call the context-graph MCP tools (query_graph, shortest_path, impact_of_change) before reading or grepping files."* That snippet is the entire per-project integration.

## 9. CLI (`ContextGraph.Cli`)

System.CommandLine, packaged as a dotnet tool (`dotnet tool install -g ContextGraph.Cli`).

```
context-graph build [path] [--out DIR] [--no-html]
context-graph query "<question>" [--dfs] [--depth N] [--budget N]
context-graph path <A> <B>
context-graph explain <label>
context-graph stats
```

All query commands operate on `context-graph-out/graph.json`; `build` writes `graph.json`, `graph.html`, `GRAPH_REPORT.md`.

## 10. Divergences from Graphify

| Area | Graphify | Us | Why |
|---|---|---|---|
| Parsing | tree-sitter (syntactic) + resolver layer | Roslyn semantic model | exact symbol binding; deletes the whole resolution subsystem |
| Freshness | batch runs + cache/checkpoint + `--watch` flags | resident server, in-memory graph, incremental splice | the cache layer is where upstream bugs concentrate; a resident server doesn't need it |
| Assistant integration | 15+ skill markdown files | one MCP server + 3-line instructions snippet | Copilot follows tools reliably, long skill docs unreliably |
| Clustering | Leiden (graspologic) → Louvain fallback | Louvain only, hand-rolled | no maintained .NET Leiden; determinism rules preserved |
| graph.html | vis-network from CDN | vis-network **vendored inline** (single-file, offline) | self-contained beats 30 KB saved |
| Semantic pass (docs/PDF) | host-LLM via skill steps | absent (future: MCP sampling) | code-only covers the actual use case |
| Noise control | post-hoc label blocklists | `IsInSource` filter at extraction | cut noise at the source |

## 11. Testing

xUnit, one test class per component. The reference repo's 145 test files are the regression catalog — port the *scenarios* (not the code) that apply to C#/Razor: generic symbol flooding, indirect dispatch, cross-project resolution, dedup, zero-node solutions, budget truncation, seed selection. Fixtures: a small multi-project solution under `tests/fixtures/` including one legacy-style csproj and a Blazor component tree. Golden-file tests for `graph.json` (stable ids and deterministic ordering make byte-stable output feasible) and for query/path/explain text output.
