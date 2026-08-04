'use strict';

const state = {
  overview: null,          // OverviewDto — counts + community lookups
  mode: 'overview',        // 'overview' | 'focus'
  focus: null,             // FocusDto driving the inspector
  focusId: null,
  nbhd: null,              // Set of ids in the current focus neighbourhood
  trail: [],
  hiddenRelations: new Set(),
  hideUnconnected: false,
  connected: new Set(),    // community node ids with at least one meta-edge
  welcomed: false,         // welcome card shown and dismissed once per session
  nodes: new Map(),        // id -> node object; owned by the engine after graphData()
  links: new Map(),        // "from|to|relation" -> link object
};

const el = {};
for (const id of ['search', 'suggestions', 'counts', 'inspector', 'breadcrumb',
                  'relation-filters', 'theme', 'orbit', 'fit', 'canvas', 'labels',
                  'hide-unconnected', 'hidden-count', 'welcome', 'welcome-stats',
                  'start-community', 'start-hub', 'start-search', 'welcome-close'])
  el[id.replace(/-(.)/g, (_, c) => c.toUpperCase())] = document.getElementById(id);

const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const css = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
// hsl(…) → hsla(…) at low alpha — dims nodes outside the focus neighbourhood without removing them.
const faded = colour => colour.replace('hsl(', 'hsla(').replace(')', ', 0.15)');

async function json(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error((await res.json().catch(() => ({}))).error || res.statusText);
  return res.json();
}

const nodeColor = n =>
  state.mode === 'overview' || n.id === state.focusId || state.nbhd?.has(n.id) ? n.color : faded(n.color);

const Graph = ForceGraph3D({ controlType: 'orbit' })(el.canvas)
  .backgroundColor(css('--ground'))
  .nodeVal(n => n.val)
  .nodeColor(nodeColor)
  .nodeLabel(n => `<div class="tip">${esc(n.label)}</div>`)
  .linkColor(() => css('--muted'))
  .linkOpacity(0.4)
  .linkWidth(l => l.weight ? Math.min(4, Math.sqrt(l.weight)) : 1)
  .linkDirectionalArrowLength(l => l.weight ? 0 : 3)   // meta-edges are undirected
  .linkDirectionalArrowRelPos(1)
  .onNodeHover(n => { el.canvas.style.cursor = n ? 'pointer' : ''; })
  .onNodeClick(node => {
    dismissWelcome();
    if (node.kind === 'community') {
      const c = state.overview.communities.find(x => x.id === node.cid);
      if (c && c.topMemberIds.length) focusNode(c.topMemberIds[0]);
      return;
    }
    focusNode(node.id);
  })
  .onBackgroundClick(() => { dismissWelcome(); hideSuggestions(); });

// One-shot "when the simulation settles" hook — used for zoomToFit / camera flights.
let settleCb = null;
Graph.onEngineStop(() => { const cb = settleCb; settleCb = null; if (cb) cb(); });
const settleThen = cb => { settleCb = cb; };

function pushData() {
  Graph.graphData({ nodes: [...state.nodes.values()], links: [...state.links.values()] });
}

async function loadOverview() {
  state.overview = await json('/api/graph');
  state.mode = 'overview'; state.focus = null; state.focusId = null; state.nbhd = null; state.trail = [];
  state.nodes.clear(); state.links.clear();
  for (const c of state.overview.communities)
    state.nodes.set('c' + c.id, { id: 'c' + c.id, cid: c.id, kind: 'community', label: c.label,
                                  color: c.color, val: Math.max(2, c.size / 3) });
  state.connected = new Set();
  for (const e of state.overview.edges) {
    state.links.set(`c${e.source}|c${e.target}`, { source: 'c' + e.source, target: 'c' + e.target, weight: e.weight });
    state.connected.add('c' + e.source);
    state.connected.add('c' + e.target);
  }
  el.counts.textContent = `${state.overview.nodeCount.toLocaleString()} nodes · ${state.overview.edgeCount.toLocaleString()} edges`;
  pushData();
  Graph.nodeColor(nodeColor);
  applyFilters();
  settleThen(() => Graph.zoomToFit(600, 60));
  renderBreadcrumb();
  el.inspector.innerHTML = '<p class="empty">Click a node — or search above — to see what it is and what it touches.</p>';
  el.relationFilters.innerHTML = '<p class="empty">Focus a node to filter its relations.</p>';
  showWelcome();
}

function addNode(dto, val, near) {
  const existing = state.nodes.get(dto.id);
  if (existing) { existing.val = Math.max(existing.val, val); return; }
  const jitter = () => (Math.random() - 0.5) * 30;
  const seed = near && Number.isFinite(near.x)
    ? { x: near.x + jitter(), y: near.y + jitter(), z: near.z + jitter() } : {};
  state.nodes.set(dto.id, { id: dto.id, label: dto.label, color: dto.color, type: dto.type,
                            span: dto.span, degree: dto.degree, communityLabel: dto.communityLabel,
                            val, ...seed });
}

async function focusNode(id) {
  dismissWelcome();
  const focus = await json('/api/node/' + encodeURIComponent(id));
  if (state.mode === 'overview') { state.nodes.clear(); state.links.clear(); }  // leave the meta-graph
  state.mode = 'focus'; state.focus = focus; state.focusId = focus.node.id;
  if (state.trail[state.trail.length - 1] !== focus.node.id) state.trail.push(focus.node.id);

  addNode(focus.node, 8, null);
  const anchor = state.nodes.get(focus.node.id);
  for (const n of focus.neighbours) {
    addNode(n.node, Math.min(6, 1 + n.node.degree * 0.3), anchor);
    const [from, to] = n.direction === 'out' ? [focus.node.id, n.node.id] : [n.node.id, focus.node.id];
    const key = `${from}|${to}|${n.relation}`;
    if (!state.links.has(key))
      state.links.set(key, { source: from, target: to, relation: n.relation, confidence: n.confidence });
  }
  state.nbhd = new Set([focus.node.id, ...focus.neighbours.map(n => n.node.id)]);
  pushData();
  Graph.nodeColor(nodeColor);
  applyFilters();
  settleThen(() => flyTo(focus.node.id));
  renderInspector(); renderBreadcrumb(); renderRelationFilters();
}

function flyTo(id) {
  const n = state.nodes.get(id);
  if (!n || !Number.isFinite(n.x)) return;
  const r = Math.hypot(n.x, n.y, n.z) || 1, k = 1 + 130 / r;
  Graph.cameraPosition({ x: n.x * k, y: n.y * k, z: n.z * k }, n, 800);
}

// --- welcome card: three concrete ways in, shown once per session ---

function showWelcome() {
  if (state.welcomed) return;
  const o = state.overview;
  el.welcomeStats.textContent =
    `${o.nodeCount.toLocaleString()} nodes · ${o.edgeCount.toLocaleString()} edges · ${o.communities.length} communities`;

  const largest = [...o.communities].sort((a, b) => b.size - a.size)[0];
  const crossWeight = new Map();
  for (const e of o.edges) {
    crossWeight.set(e.source, (crossWeight.get(e.source) || 0) + e.weight);
    crossWeight.set(e.target, (crossWeight.get(e.target) || 0) + e.weight);
  }
  const byCross = [...o.communities].sort((a, b) => (crossWeight.get(b.id) || 0) - (crossWeight.get(a.id) || 0));
  // Two distinct doors in: if the largest community is also the busiest, offer the runner-up.
  const busiest = byCross.find(c => c.id !== largest?.id) ?? byCross[0];

  wire(el.startCommunity, largest, c => `${c.label} · ${c.size} nodes`);
  wire(el.startHub, busiest, c => `${c.topMembers[0]} · busiest neighbourhood`);
  el.welcome.hidden = false;

  function wire(btn, community, sub) {
    if (!community || !community.topMemberIds.length) { btn.hidden = true; return; }
    btn.querySelector('.muted').textContent = sub(community);
    btn.onclick = () => focusNode(community.topMemberIds[0]);
  }
}

function dismissWelcome() {
  if (!state.welcomed) { state.welcomed = true; el.welcome.hidden = true; }
}

el.startSearch.addEventListener('click', () => { dismissWelcome(); el.search.focus(); });
el.welcomeClose.addEventListener('click', dismissWelcome);

// --- relation filters (hide links; hide nodes left with no visible link) ---

const endpointId = e => typeof e === 'object' ? e.id : e;  // engine objectifies link endpoints

function hasVisibleLink(id) {
  for (const l of state.links.values())
    if ((endpointId(l.source) === id || endpointId(l.target) === id) &&
        (!l.relation || !state.hiddenRelations.has(l.relation)))
      return true;
  return false;
}

function nodeVisible(n) {
  if (n.kind === 'community')
    return !state.hideUnconnected || state.connected.has(n.id);
  if (n.id === state.focusId || hasVisibleLink(n.id))
    return true;
  // Trail nodes normally linger for orientation; "hide unconnected" culls them too
  // once filters have taken their last visible edge.
  return !state.hideUnconnected && state.trail.includes(n.id);
}

function applyFilters() {
  Graph.linkVisibility(l => !l.relation || !state.hiddenRelations.has(l.relation));
  Graph.nodeVisibility(nodeVisible);
  const hidden = [...state.nodes.values()].filter(n => !nodeVisible(n)).length;
  el.hiddenCount.textContent = hidden ? String(hidden) : '';
  rebuildLabels();
}

function visibleNeighbours() {
  return state.focus.neighbours.filter(n => !state.hiddenRelations.has(n.relation));
}

el.hideUnconnected.addEventListener('change', () => {
  state.hideUnconnected = el.hideUnconnected.checked;
  applyFilters();
});

// --- HTML label overlay: tiered by importance, chip-styled, collision-culled ---
// (the UMD bundle exposes no THREE, so labels live in DOM, not the scene)

let labelled = [];

function labelSet() {
  if (state.mode === 'overview')
    return [...state.nodes.values()].filter(nodeVisible)
      .sort((a, b) => (b.val ?? 0) - (a.val ?? 0))
      .slice(0, 80);
  if (!state.nbhd) return [];
  return [...state.nodes.values()]
    .filter(n => n.id === state.focusId || (state.nbhd.has(n.id) && nodeVisible(n) && hasVisibleLink(n.id)))
    .sort((a, b) => a.id === state.focusId ? -1 : b.id === state.focusId ? 1
                  : (b.degree ?? 0) - (a.degree ?? 0))
    .slice(0, 60);
}

function rebuildLabels() {
  labelled = labelSet();
  el.labels.innerHTML = labelled.map((n, i) => {
    const tier = n.id === state.focusId ? 'lbl-lg focus' : i < 5 ? 'lbl-lg' : i < 20 ? 'lbl-md' : 'lbl-sm';
    return `<div class="lbl ${tier}">${esc(n.label)}</div>`;
  }).join('');
}

let frame = 0;
(function labelTick() {
  frame++;
  const kids = el.labels.children;
  // Greedy screen-space declutter: labels are already in priority order, so on the
  // collision frame each label survives only if it misses everything placed before it.
  const collide = frame % 12 === 0;
  const placed = [];
  for (let i = 0; i < labelled.length && i < kids.length; i++) {
    const n = labelled[i], div = kids[i];
    if (!Number.isFinite(n.x)) { div.style.display = 'none'; continue; }
    const p = Graph.graph2ScreenCoords(n.x, n.y, n.z);
    div.style.display = '';
    div.style.left = p.x + 'px';
    div.style.top = p.y + 'px';
    if (collide) {
      const w = div.offsetWidth, h = div.offsetHeight;
      const rect = { l: p.x - w / 2, r: p.x + w / 2, t: p.y + 10, b: p.y + 10 + h };
      const overlaps = i > 0 && placed.some(q => rect.l < q.r && rect.r > q.l && rect.t < q.b && rect.b > q.t);
      div.style.visibility = overlaps ? 'hidden' : '';
      if (!overlaps) placed.push(rect);
    }
  }
  requestAnimationFrame(labelTick);
})();

// --- inspector / breadcrumb / filters ---

function renderInspector() {
  const f = state.focus;
  const groups = {};
  for (const n of visibleNeighbours()) {
    const key = `${n.direction === 'out' ? '' : 'Incoming '}${n.relation}`;
    (groups[key] ||= []).push(n);
  }
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
    parts.push(`<button data-i="${i}" title="${esc(id)}">${esc(label)}</button>`);
  });
  el.breadcrumb.innerHTML = parts.join('<span class="muted">›</span>');
  el.breadcrumb.querySelectorAll('button').forEach(b => b.addEventListener('click', () => {
    const i = Number(b.dataset.i);
    if (i < 0) return loadOverview();
    const id = state.trail[i];
    state.trail = state.trail.slice(0, i);
    focusNode(id);
  }));
  el.breadcrumb.scrollLeft = el.breadcrumb.scrollWidth;
}

function renderRelationFilters() {
  const counts = {};
  for (const n of state.focus.neighbours) counts[n.relation] = (counts[n.relation] || 0) + 1;
  el.relationFilters.innerHTML = Object.entries(counts).sort().map(([rel, count]) => `
    <label class="filter">
      <input type="checkbox" data-rel="${esc(rel)}" ${state.hiddenRelations.has(rel) ? '' : 'checked'}>
      <span>${esc(rel)}</span><span class="count">${count}</span>
    </label>`).join('');
  el.relationFilters.querySelectorAll('input').forEach(i => i.addEventListener('change', () => {
    if (i.checked) state.hiddenRelations.delete(i.dataset.rel);
    else state.hiddenRelations.add(i.dataset.rel);
    applyFilters();
    renderInspector();
  }));
}

// --- collapsible side panels: visible buttons, edge tabs to restore ---

document.querySelectorAll('.collapse-btn').forEach(b => b.addEventListener('click', () => {
  document.getElementById(b.dataset.panel).hidden = true;
  document.getElementById(b.dataset.tab).hidden = false;
}));
document.querySelectorAll('.edge-tab').forEach(t => t.addEventListener('click', () => {
  t.hidden = true;
  document.getElementById(t.id === 'filters-tab' ? 'filters-panel' : 'inspector-panel').hidden = false;
}));

// --- search (backed by /api/search) ---

function hideSuggestions() { el.suggestions.hidden = true; }

let searchTimer;
el.search.addEventListener('input', () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => searchFor(el.search.value.trim()), 160);
});
el.search.addEventListener('keydown', e => {
  if (e.key === 'Enter') el.suggestions.querySelector('button[data-id]')?.click();
  if (e.key === 'Escape') hideSuggestions();
});
document.addEventListener('click', e => {
  if (!e.target.closest('#searchbox')) hideSuggestions();
});

async function searchFor(term) {
  if (term.length < 2) { hideSuggestions(); return; }
  const hits = await json('/api/search?q=' + encodeURIComponent(term)).catch(() => []);
  el.suggestions.hidden = false;
  el.suggestions.innerHTML = hits.length
    ? hits.map(h => `<button data-id="${esc(h.id)}">${esc(h.label)}
        <span class="where">${esc(h.type ?? 'node')} · ${esc(h.communityLabel)}${h.span ? ' · ' + esc(h.span) : ''}</span></button>`).join('')
    : '<button disabled>No matches</button>';
  el.suggestions.querySelectorAll('button[data-id]').forEach(b =>
    b.addEventListener('click', () => {
      hideSuggestions();
      el.search.value = '';
      focusNode(b.dataset.id);
    }));
}

// --- view controls (bottom bar) ---

el.fit.addEventListener('click', () => Graph.zoomToFit(600, 60));

// --- orbit (motion parallax is what makes 3D legible — see the research spec) ---

const controls = Graph.controls();
controls.autoRotateSpeed = 0.6;
let orbitWanted = !matchMedia('(prefers-reduced-motion: reduce)').matches;
controls.autoRotate = orbitWanted;
el.orbit.setAttribute('aria-pressed', String(orbitWanted));
let resumeTimer;
controls.addEventListener('start', () => { controls.autoRotate = false; clearTimeout(resumeTimer); });
controls.addEventListener('end', () => {
  clearTimeout(resumeTimer);
  if (orbitWanted) resumeTimer = setTimeout(() => { controls.autoRotate = true; }, 15000);
});
el.orbit.addEventListener('click', () => {
  orbitWanted = !orbitWanted;
  controls.autoRotate = orbitWanted;
  el.orbit.setAttribute('aria-pressed', String(orbitWanted));
});

// --- theme, sizing, shortcuts ---

el.theme.addEventListener('click', () => {
  const now = document.documentElement.getAttribute('data-theme');
  const next = now === 'dark' ? 'light' : now === 'light' ? 'dark'
    : (matchMedia('(prefers-color-scheme: dark)').matches ? 'light' : 'dark');
  document.documentElement.setAttribute('data-theme', next);
  Graph.backgroundColor(css('--ground'));
  Graph.linkColor(() => css('--muted'));
});

new ResizeObserver(() =>
  Graph.width(el.canvas.clientWidth).height(el.canvas.clientHeight)).observe(el.canvas);

document.addEventListener('keydown', e => {
  if ((e.metaKey || e.ctrlKey) && e.key === 'k') { e.preventDefault(); el.search.focus(); }
});

loadOverview().catch(err => {
  el.inspector.innerHTML = `<p class="empty">Could not load the graph: ${esc(err.message)}</p>`;
});
