# Viewer UI/UX redesign — full-bleed canvas, visible floating panels

*2026-08-04. Approved direction from brainstorming: a working tool for understanding
codebases, combining canvas-first presentation with dev-tool discipline. Everything is
point-and-click — keyboard shortcuts exist only as accelerators, never the only path.
The 3D engine and its configuration pipeline are untouched; this is HTML/CSS/JS around
the canvas.*

## Pain points being fixed

1. **"Didn't know where to start"** — the landing screen is bubbles with no guidance.
2. **"Labels/text hard to read"** — overlay labels overlap and have weak contrast.
3. **"Navigation felt disorienting"** — no always-visible way back, controls scattered.

## Layout

The canvas fills the viewport (`position: fixed; inset: 0`). All chrome floats over it
as translucent "glass" panels (blurred backdrop, hairline border, soft shadow):

- **Top bar** — brand + live counts on the left; a permanent, always-visible **search
  box** in the centre (suggestions drop down beneath it, click to jump); theme toggle
  on the right.
- **Left panel "Filters"** — View (hide unconnected) + Relations checkboxes. A visible
  `‹` button collapses it to a small edge tab labelled *Filters*; clicking the tab
  restores it.
- **Right panel "Details"** — the inspector. Same collapse affordance (`›` / edge tab).
  Shows a helpful empty state before any selection.
- **Bottom bar** — breadcrumb trail as clickable chips (first chip is always
  *Overview*), and on the right visible **Fit** (zoom-to-fit) and **Orbit** (toggle,
  pressed-state styled) buttons.
- **Welcome card** — centred over the canvas on first load: graph stats plus three big
  clickable starting points: (1) dive into the largest community, (2) jump to a hub
  node (top member of the most cross-connected community), (3) focus the search box.
  Also an "Explore on my own" dismiss button. Disappears on any navigation and never
  returns during the session.

## Labels (HTML overlay only — engine untouched)

- **Tiers by importance**: within the current label set (community size in overview,
  degree in focus), top 5 render large, next 15 medium, rest small; the focus node is
  always large.
- **Contrast chips**: each label gets a translucent backdrop chip instead of bare text.
- **Collision avoidance**: a greedy screen-space pass (priority order, keep-first)
  hides labels that would overlap an already-placed one; recomputed every ~12 frames,
  positions still updated every frame. The focus label is never hidden.
- Hovering a node shows the existing engine tooltip; the cursor becomes a pointer.

## Element contract (ids the JS relies on)

Kept: `search, suggestions, counts, inspector, breadcrumb, relation-filters, theme,
orbit, canvas, labels, hide-unconnected, hidden-count`. New: `topbar, filters-panel,
inspector-panel, bottombar, fit, welcome, welcome-stats, start-community, start-hub,
start-search, welcome-close`, collapse buttons + edge tabs per side panel.

## Out of scope

Engine/rendering changes, server/API changes, whole-graph mode, minimap, saved layouts.

## Testing

`ViewPageTests` gains presence assertions for `id="welcome"` and `id="bottombar"`
(red-first), and the existing zero-console-errors assertion exercises the rewritten
front end in headless Chrome. Everything else is verified by launch + screenshot,
per the repo's "assert it runs" rule.
