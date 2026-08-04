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

const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

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
    parts.push(`<button data-i="${i}">${esc(label)}</button>`);
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
      <input type="checkbox" data-rel="${esc(rel)}" ${state.hiddenRelations.has(rel) ? '' : 'checked'}>
      <span>${esc(rel)}</span><span class="count">${count}</span>
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
    el.suggestions.innerHTML = `<button disabled>${esc(err.message)}</button>`;
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
  el.inspector.innerHTML = `<p class="empty">Could not load the graph: ${esc(err.message)}</p>`;
});
