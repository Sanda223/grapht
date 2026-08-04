# 3D graph viewer at scale — research & recommendation

*2026-08-04. Research into (a) building a 3D graph visualiser that handles large node
counts, and (b) whether an existing MIT-licensed library beats building one. Licenses
and scale claims verified against upstream sources as of this date.*

## 1. Reality check: how big are our graphs?

| Case | Nodes | Edges |
|---|---|---|
| grapht itself (dogfood) | 589 | 1,214 |
| eShop reference (design doc) | 2,717 | 5,162 |

The existing API is already the scaling mechanism: `/api/graph` returns O(communities)
(~20–80 elements), `/api/node/{id}` returns O(degree). **The browser never receives the
full graph.** "Massive" is a future concern (monorepos, multi-solution graphs), not a
today problem — which changes what we should build first.

## 2. What's actually wrong with the current viewer

From the code map (all documented in ARCHITECTURE.md §10 and the MVP plan):

1. **Search is exact-id only** — typing `CheckoutAsync` 404s because `/api/node/{id}`
   needs the fully-qualified id. The fuzzy `/api/search` over
   `QueryEngine.ResolveNodeCandidates` was explicitly deferred. This is the headline
   "feels broken" bug.
2. **Community click focuses by label**, which only resolves when label == id — same
   root cause as (1).
3. **Expand replaces the canvas** — `renderCanvas()` clears everything and re-lays-out
   (`concentric`) on every focus. The spec's accretive "growth by intent" was not
   implemented. No positions survive a click, so the view feels unstable.
4. **Overview labels overlap** on small communities (cose layout, no collision).
5. Deferred: source panel, VS Code handoff, SSE live refresh, node-type filters,
   god-node neighbour cap.

None of these are rendering-engine problems; they'd be bugs in a 3D front end too.

## 3. Building a 3D visualiser at 100k–1M nodes (the honest engineering picture)

Full details in the research transcript; the load-bearing facts:

- **Rendering is the easy part.** GPU point-sprites (fragment-shader sphere impostors)
  + one instanced draw call for edges = 2–3 draw calls total; 100k nodes/300k edges is
  comfortable at 60fps on WebGL2. cosmos.gl demonstrates this budget in 2D *with live
  GPU physics on top*. Per-node three.js `Mesh` objects (what naive apps do) die at ~10k.
- **Layout is the hard part.** CPU force layout (d3-force-3d) is live-interactive only
  to ~5–10k nodes; 50k ≈ 30s, 118k ≈ minutes (verified in 3d-force-graph issues #81,
  react-force-graph #202). At real scale you precompute layout server-side (Barnes–Hut
  octree in C#, seconds-to-minutes, cached) and ship coordinates as data; the client
  runs a small local simulation only for expanded neighbourhoods with existing nodes
  pinned.
- **Picking must be GPU color-ID picking** (render IDs to an offscreen buffer, read 1px)
  — raycasting is O(n) per mouse move. **Labels must be SDF text + a visibility budget**
  (~100–500 labels: hovered, selected, top-K by degree).
- **Payloads go binary at scale**: 100k nodes/300k edges ≈ 4.5MB of typed arrays
  (zero parse cost) vs 20–40MB of JSON. Keep JSON for overview/focus endpoints;
  add `application/octet-stream` buffer endpoints only when bulk-loading exists.
- **LOD = what we already do.** Communities as super-nodes, expand on demand, is the
  standard literature answer for large graphs. Our information architecture is already
  correct; only the renderer would change.
- **3D readability caveat (Ware & Franck 1996; Ware & Mitchell 2008):** static 3D
  projected to a monitor is *worse* than good 2D — occlusion, distortion. The ~3×
  comprehension gain of 3D comes almost entirely from **motion parallax** (orbit/idle
  rotation) and stereo. A 3D viewer must keep the camera moving (inertial orbit,
  animated refocus) plus depth fog and dim-non-neighbourhood on focus, or it's a
  downgrade.
- **Custom-build estimate:** rendering core ~1 week with three.js scaffolding; realistic
  total with LOD state machine, labels, camera polish, server precompute + binary
  endpoints: **3–6 engineer-weeks**. The expensive part is layout quality and
  interaction polish, not rendering.

## 4. Library survey (licenses verified 2026-08-04)

| Library | License | 2D/3D | Honest ceiling | Verdict |
|---|---|---|---|---|
| **3d-force-graph** v1.80 | MIT | 3D (three.js) | 10–50k interactive w/ precomputed layout (`fx/fy/fz`, `cooldownTicks(0)`); ~100k view-only | **Pick for 3D.** Single 1.31MB UMD, three.js included, zero external requests, picking/labels/incremental `graphData()` built in. Active (Apr 2026). |
| **@cosmos.gl/graph** v3.4 | MIT (OpenJS since May 2025) | 2D only | ~1M nodes — GPU force layout *and* rendering | **Pick for raw scale**, but abandons 3D. No built-in labels. API churn (v3 is fresh). ⚠️ `@cosmograph/*` packages are **CC-BY-NC** — never install those; only `@cosmos.gl/graph` is MIT. |
| sigma.js v3 + graphology | MIT | 2D WebGL | ~10k nodes / 100k edges | Boring-stable 2D upgrade path; FA2 layout in a worker. |
| cytoscape.js (current) | MIT | 2D canvas | ~1–8k elements (WebGL renderer still preview-grade) | Fine for what the viewer sends today; not a scale or 3D path. |
| deck.gl v9.3 | MIT | 2D/3D rendering, no layout | ~1M points/lines (WebGL2; WebGPU picking broken) | Runner-up at extreme scale with precomputed layout; no graph ergonomics. |
| reagraph | Apache-2.0 | 3D React | 2,600 nodes → 1–3 fps | Disqualified (React peer-dep, poor scale). |
| AntV G6 v5 | MIT | 2D + 0.x 3D extension | 5–10k | Rich features, churny; 3D extension immature. |
| VivaGraph / ngraph.pixel | BSD-3 / MIT | 3D | — | Dead (2017–2019). `ngraph.forcelayout` still useful as a layout ingredient. |
| Gephi / Gephi Lite / Graphia | GPL | — | — | GPL, standalone apps — can't embed. Graphia is the free 3D-at-scale desktop escape hatch. |

## 5. Recommendation

**Phase 0 — fix what's actually broken (independent of any renderer):**
add `/api/search` (fuzzy, over `QueryEngine.ResolveNodeCandidates`), make community
click use real ids, cap god-node neighbours. Small, already spec'd as follow-ups.

**Phase 1 — 3D behind the existing contract (~1 week):**
vendor `3d-force-graph.min.js` next to (or instead of) `cytoscape.min.js`; rewrite
`app.js` against the same two JSON endpoints. Keep overview → focus → expand, but make
expand *accretive* (3d-force-graph's incremental `graphData()` makes this natural —
new nodes seeded at neighbour centroid, existing nodes pinned). Idle orbit + animated
refocus + depth fog per the perception research. Server changes: none beyond Phase 0.
Visible set stays ≤ a few thousand, so the library's honest ceiling is never hit.

**Phase 2 — only if a real 100k+ graph shows up:**
server-side precomputed 3D layout (BH octree in C#, cached), binary buffer endpoint,
custom three.js instanced pipeline (points + instanced edges + GPU picking + SDF
labels). 3–6 weeks. Do not start here.

**If raw scale ever matters more than 3D:** `@cosmos.gl/graph` (MIT) is the only
GPU-layout option and the scale champion — at the cost of being 2D.
