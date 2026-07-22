# `grapht view` — interactive graph viewer

**Status:** design, not yet implemented
**Date:** 2026-07-23

## Context

`grapht build` writes a self-contained `graph.html`. Until [PR #7](https://github.com/Sanda223/grapht/pull/7) it rendered blank on every graph — over-escaping corrupted the vendored vis-network bundle, so the page never ran. With that fixed the viewer works, and the real limitation is visible: **eShop's 2,717 nodes on one canvas convey nothing.** It is a hairball. The failure is information design, not styling.

Separately, the tool has no accessible surface beyond the CLI and the MCP server. A developer who wants to understand how a solution hangs together has to either read CLI text output or squint at the hairball.

This spec designs `grapht view`: a local web app that turns the graph into a **code navigator** — you find a thing, see what it connects to, read its source, and jump to it in your editor.

**Scope boundary:** this improves human accessibility. It does **not** improve token efficiency, which remains the tool's primary thesis. It is worth building, but it competes for time with the A/B re-run and private-feed publishing, both of which bear directly on the 15-person pilot.

## Goals

1. A developer unfamiliar with a solution can understand its shape in under a minute.
2. A developer who names one symbol can see everything connected to it, read its code, and open it in VS Code without leaving the viewer.
3. The existing static `graph.html` keeps working and stays shareable (CI artifact, hand it to someone with no install).

## Non-goals

- Editing code from the viewer.
- Remote or multi-user hosting. Loopback only.
- Replacing `graph.html`. Both surfaces ship.
- Changing extraction, clustering, or the query engine.

## Decisions taken

| Decision | Choice | Why |
|---|---|---|
| Primary job | Both onboard and investigate, **investigate by default** | Matches how developers actually arrive: usually with a symbol in mind |
| Foundation | Local web app, not a single file | Live features (source display, editor handoff, refresh) are impossible statically |
| `graph.html` | **Kept**, unchanged | Shareable artifact and CI diff story depend on it |
| Front-end stack | Hand-rolled HTML/CSS/JS, Cytoscape.js vendored | No npm, no bundler, no second language; install stays one `dotnet tool install` |
| Graph library | Cytoscape.js **in the app only** | Better layout control and compound-node support; `graph.html` keeps its vendored vis-network untouched |

Both libraries ship as embedded resources in `Grapht.Core/Assets/`: vis-network serves `graph.html`, Cytoscape serves the app. They coexist; neither replaces the other. The app serves its assets over HTTP rather than inlining them, so the `</script`-escaping hazard from PR #7 does not apply there.

Rejected: **Vite + React + TypeScript** (adds npm and a bundling step to a single-language repo, for UI complexity that doesn't warrant it) and **Blazor WASM** (multi-MB payload plus constant JS interop, since every graph library is JS — worst of both).

## Interaction model

Three states, one continuous flow. The governing rule: **never render the whole graph.**

### 1. Overview — the landing state

The community meta-graph: one node per community (~81 for eShop), sized by member count, edges weighted by how many relationships cross each pair. Legible immediately. `HtmlExporter.WriteMetaGraph` already computes exactly this aggregation; the same logic moves behind `/api/graph`.

A search field with type-ahead over all nodes sits above it — this is how most sessions actually start.

### 2. Focus — the default working state

Selecting a node (via search, or by drilling into a community) shows **that node and its immediate neighbours only** — typically 5–30 nodes. The focus node is visually dominant; neighbours are sized by degree.

### 3. Expand — growth by intent

Clicking a neighbour pulls in *its* neighbours. The view grows only when asked. A breadcrumb records the path (`Overview › Basket › ChangeBasketAsync`); "reset to focus" collapses back to state 2.

## Layout

```
┌─────────────────────────────────────────────────────────┐
│ ⌘K search…          grapht · eShop · 2,717 nodes     ◐  │  56px
├──────────────┬──────────────────────────┬───────────────┤
│  FILTERS     │        CANVAS            │   INSPECTOR   │
│              │                          │               │
│  Node types  │   focus + neighbours     │  identity     │
│  Relations   │                          │  span         │
│  Communities │   ── breadcrumb ──       │  source code  │
│              │                          │  relations    │
│  260px       │        flexible          │     380px     │
└──────────────┴──────────────────────────┴───────────────┘
```

**Left rail — filters.** The highest-value single addition. `contains` accounts for 2,384 of 5,162 edges (46%) and is pure structural noise when tracing behaviour; switching it off is what makes the graph readable. Every toggle carries its live count so you know what you are removing. Grouped: node types (18), relations (9), communities.

**Centre — canvas.** Cytoscape with a force layout, re-run on each focus change with a 150ms transition so the change is followable.

**Right — inspector.** Where the code-navigator promise is kept:
- Identity: label, type, community, degree
- Span: `src/WebApp/Services/BasketState.cs L78–L109`
- **Source at that span**, fetched from disk, syntax-tinted, with real line numbers
- Relations grouped by direction and type (Calls 4 · Called by 7 · Implements 1), each row clickable to re-focus
- "Open in VS Code" → `vscode://file/{abs}:{line}`

Responsive: under 1100px the rails become slide-over drawers; the canvas never shrinks below half the viewport.

## Visual system

A developer tool for reading code, so it borrows from the editor rather than from dashboards. Utilitarian treatment: precise, quiet, no decorative flourish.

### Color

Slate neutrals biased slightly blue-cyan so the app sits beside an editor rather than fighting it. Defined as tokens on `:root`, redefined under `@media (prefers-color-scheme: dark)` and again under `:root[data-theme="…"]` so the in-app toggle wins in both directions.

| Token | Light | Dark |
|---|---|---|
| `--ground` | `#FBFCFD` | `#0F1419` |
| `--surface` | `#FFFFFF` | `#161C23` |
| `--border` | `#DDE3EA` | `#242D38` |
| `--ink` | `#111820` | `#DCE3EB` |
| `--muted` | `#5C6B7A` | `#7D8B9A` |
| `--accent` | `#0E7C86` | `#2AA5B0` |

Node fill stays **community-derived** via the existing golden-angle hue walk (`HtmlExporter.Palette`) — it already produces distinct hues at any count. The accent is reserved for selection, focus rings, and interactive affordances, so it never competes with node color.

**Confidence is encoded as edge style, never hue:** solid = `EXTRACTED`, dashed = `INFERRED`, dotted = `AMBIGUOUS`. This keeps hue meaning one thing only (community) and leaves semantic red/amber/green free for a future diff view.

### Type

Three roles, all inlined as `@font-face` data URIs (no CDN — same offline guarantee as `graph.html`):

- **UI chrome** — a neutral grotesque (Inter or system stack fallback)
- **Identifiers, spans, code** — JetBrains Mono. Every symbol name in the UI is code and is set as code, including in the inspector's relation rows.
- **Section labels** — uppercase, letterspaced `0.06em`, at `--muted`

`font-variant-numeric: tabular-nums` on all counts so filter numbers align.

### Motion

150ms ease on focus transitions and panel changes; nothing else animates. Fully disabled under `prefers-reduced-motion`.

## Architecture

```
grapht view [path] [--port 5050] [--no-open]
   ↓
Kestrel bound to 127.0.0.1 only
   ↓
GraphService  ── FileSystemWatcher ──→ debounced incremental rebuild
   ↓
GET  /api/graph          overview: communities + weighted meta-edges
GET  /api/node/{id}      node + neighbours grouped by relation/direction
GET  /api/source?id=     source text at that node's span
POST /api/query          { mode: query|path|explain, … } → QueryEngine
GET  /api/events         SSE: "graph changed" → client refetches
```

### The one structural change

**`GraphService` moves from `Grapht.Mcp` to `Grapht.Core`.**

It already holds the live graph, watches the workspace, and rebuilds incrementally — exactly what the view server needs — and it has no MCP dependency (`using Grapht.Core;` plus Roslyn only). Its current location is an accident: shared infrastructure living in one consumer. `Grapht.Mcp` continues to work by referencing Core, and its behaviour must not change.

Everything else is reuse, not new logic: `QueryEngine` verbatim for `/api/query`, the meta-graph aggregation from `HtmlExporter.WriteMetaGraph` for `/api/graph`, `Analysis` for community labels.

### Security

The server binds **loopback only** — never `0.0.0.0`.

`/api/source` is the sensitive endpoint: it reads files from disk by node id. It must resolve the node's `SourceFile` against the solution root with `Path.GetFullPath` and reject any result that does not start with that root. Untested, this endpoint reads arbitrary files. Path traversal gets an explicit test.

## Testing

| Layer | Test |
|---|---|
| `GraphService` move | Existing MCP tests pass unchanged — proves the move is behaviour-preserving |
| Endpoints | One test per route: shape, and 404 on unknown node id |
| Path traversal | `/api/source` with a node whose path escapes the root → rejected. Non-negotiable. |
| Front end | Headless Chrome (the PR #7 harness, extended): load, assert **zero console errors**, assert canvas and inspector render |
| Filters | Toggling `contains` off removes exactly 2,384 edges on the eShop fixture |
| `graph.html` | Existing tests unchanged — this work must not touch the static export |

The headless-Chrome check matters more than usual here. PR #7 existed because a test asserted the vis bundle was *present* rather than that it *ran*; embedding is not executing. Any front-end test that does not actually load the page repeats that mistake.

## Build order

Each step ends runnable and verifiable.

1. **Move `GraphService` to Core.** Verify: full suite green, MCP server still serves 8 tools.
2. **Server skeleton + `/api/graph`.** `grapht view` starts, serves the overview JSON. Verify: endpoint test, manual curl.
3. **Overview UI.** Meta-graph renders, communities clickable. Verify: headless load, zero console errors.
4. **Focus + expand + inspector identity.** Search → focus → expand → breadcrumb. Verify: interaction test.
5. **`/api/source` + inspector code display.** Verify: endpoint test **and** the traversal test.
6. **Filters.** Verify: the `contains` edge-count assertion.
7. **`/api/query` + query box.** Verify: results match the CLI for the same input.
8. **SSE live refresh + VS Code handoff.** Verify: edit a file, graph updates without reload.

Steps 1–4 are the minimum useful product. 5 is what makes it a code navigator. 6–8 are refinement.

## Open questions

- **Port collision.** If 5050 is taken, fail with a clear message or auto-increment? Leaning auto-increment, reporting the chosen port.
- **Very large solutions.** Overview is bounded by community count (~81), and focus views are bounded by degree — but a god node with 400 neighbours will still be a hairball. Probably cap displayed neighbours with a "show all" affordance; decide when a real case appears rather than guessing now.
