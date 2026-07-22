using System.Reflection;
using System.Text.Json;

namespace Grapht.Core;

/// <summary>Writes a single self-contained graph.html: vendored vis-network + inline data.
/// No CDN, no network — the file works offline forever.</summary>
public static class HtmlExporter
{
    public const int MaxNodesForViz = 5000;

    public static void Write(KnowledgeGraph graph, Analysis analysis, string path, int maxNodes = MaxNodesForViz)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (graph.NodeCount > maxNodes)
        {
            // Too many nodes to render individually: aggregate to one node per community.
            WriteMetaGraph(graph, analysis, path);
            return;
        }

        var palette = Palette(analysis.Communities.Count);
        var nodes = graph.Nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => new
        {
            id = n.Id,
            label = n.Label,
            group = n.Community ?? 0,
            value = graph.Degree(n.Id),
            color = palette[(n.Community ?? 0) % palette.Count],
            shape = n.Type switch { "method" => "dot", "file" or "component" => "square", "namespace" => "diamond", _ => "dot" },
            meta = new { type = n.Type, source = n.SourceFile, location = n.SourceLocation, community = n.Community ?? 0 },
        });
        var edges = graph.Edges
            .OrderBy(e => e.Source, StringComparer.Ordinal).ThenBy(e => e.Target, StringComparer.Ordinal).ThenBy(e => e.Relation, StringComparer.Ordinal)
            .Select(e => new
            {
                from = e.Source,
                to = e.Target,
                relation = e.Relation,
                confidence = e.Confidence,
                dashes = e.Confidence != Confidence.Extracted,
            });
        WriteHtml(path, nodes, edges, Legend(analysis, palette));
    }

    /// <summary>Single render/escape path for both the full and meta views: the `</`
    /// escaping is an XSS guard for user-controlled labels inside a script tag and must
    /// never diverge between them.</summary>
    private static void WriteHtml(string path, object nodes, object edges, object legend)
    {
        var data = JsonSerializer.Serialize(new { nodes, edges, legend }).Replace("</", "<\\/");
        File.WriteAllText(path, Template.Replace("__VIS_JS__", VisJs()).Replace("__DATA__", data));
    }

    private static object Legend(Analysis analysis, List<string> palette) =>
        analysis.Communities.OrderBy(kv => kv.Key).Select(kv => new
        {
            id = kv.Key,
            name = analysis.CommunityLabels[kv.Key],
            count = kv.Value.Count,
            color = palette[kv.Key % palette.Count],
        });

    /// <summary>One node per community, sized by member count; edges weighted by how many
    /// relationships cross the pair. Readable at any scale.</summary>
    private static void WriteMetaGraph(KnowledgeGraph graph, Analysis analysis, string path)
    {
        var palette = Palette(analysis.Communities.Count);
        var nodes = analysis.Communities.OrderBy(kv => kv.Key).Select(kv => new
        {
            id = "community:" + kv.Key,
            label = analysis.CommunityLabels[kv.Key],
            group = kv.Key,
            value = kv.Value.Count,
            color = palette[kv.Key % palette.Count],
            shape = "dot",
            meta = new
            {
                type = "community",
                source = $"{kv.Value.Count} members; top: " + string.Join(", ",
                    kv.Value.OrderByDescending(graph.Degree).Take(8).Select(id => graph.Nodes[id].Label)),
                location = (string?)null,
                community = kv.Key,
            },
        });

        var crossings = new Dictionary<(int, int), int>();
        foreach (var edge in graph.Edges)
        {
            int a = graph.Nodes[edge.Source].Community ?? 0, b = graph.Nodes[edge.Target].Community ?? 0;
            if (a == b)
                continue;
            var key = a < b ? (a, b) : (b, a);
            crossings[key] = crossings.GetValueOrDefault(key) + 1;
        }
        var edges = crossings.OrderBy(kv => kv.Key).Select(kv => new
        {
            from = "community:" + kv.Key.Item1,
            to = "community:" + kv.Key.Item2,
            relation = $"{kv.Value} links",
            confidence = Confidence.Extracted,
            dashes = false,
        });
        WriteHtml(path, nodes, edges, Legend(analysis, palette));
    }

    private static List<string> Palette(int count)
    {
        // Golden-angle hue walk: visually distinct for any community count.
        var colors = new List<string>();
        for (var i = 0; i < Math.Max(count, 1); i++)
            colors.Add($"hsl({(int)(i * 137.508) % 360}, 62%, 55%)");
        return colors;
    }

    /// <summary>Only `&lt;/script&gt;` can terminate the block early, so escape exactly that
    /// and nothing else. Escaping every `&lt;/` (as the JSON payload does) is fatal here:
    /// vis-network contains the regex literal `/&lt;/`, and rewriting it to `/&lt;\/` stops the
    /// slash from closing the regex — SyntaxError, no `vis` global, blank page.</summary>
    private static string VisJs()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Grapht.Core.Assets.vis-network.min.js")
            ?? throw new InvalidOperationException("vis-network resource missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
    }

    private const string Template = """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<title>grapht</title>
<style>
  * { box-sizing: border-box; margin: 0; }
  body { font: 13px/1.5 -apple-system, "Segoe UI", sans-serif; background: #14161a; color: #d7dae0; display: flex; height: 100vh; }
  #net { flex: 1; }
  #side { width: 320px; padding: 14px; overflow-y: auto; background: #1b1e24; border-left: 1px solid #2a2e36; }
  #side h1 { font-size: 15px; margin-bottom: 10px; }
  #search { width: 100%; padding: 7px 9px; border-radius: 6px; border: 1px solid #2a2e36; background: #14161a; color: #d7dae0; margin-bottom: 12px; }
  .lg { display: flex; align-items: center; gap: 7px; padding: 3px 4px; border-radius: 5px; cursor: pointer; user-select: none; }
  .lg:hover { background: #232730; }
  .lg.off { opacity: .35; }
  .sw { width: 11px; height: 11px; border-radius: 3px; flex: none; }
  #info { margin-top: 14px; padding-top: 12px; border-top: 1px solid #2a2e36; }
  #info .t { font-weight: 600; font-size: 14px; }
  #info .m { color: #8b93a1; font-size: 12px; margin: 2px 0 8px; }
  #info .e { font-size: 12px; padding: 1px 0; color: #aeb6c2; }
  #info .e b { color: #d7dae0; font-weight: 500; }
  .dim { color: #8b93a1; }
  small { color: #6b7280; }
</style>
</head>
<body>
<div id="net"></div>
<div id="side">
  <h1>grapht</h1>
  <input id="search" placeholder="search nodes…" autocomplete="off">
  <div id="legend"></div>
  <div id="info"><span class="dim">Click a node.<br><small>Dashed edges = inferred/ambiguous.</small></span></div>
</div>
<script>__VIS_JS__</script>
<script>
const DATA = __DATA__;
const hidden = new Set();
const nodeMeta = new Map(DATA.nodes.map(n => [n.id, n.meta]));

const nodes = new vis.DataSet(DATA.nodes.map(n => ({...n, title: undefined})));
const edges = new vis.DataSet(DATA.edges.map((e, i) => ({
  id: i, from: e.from, to: e.to, dashes: e.dashes,
  arrows: {to: {enabled: true, scaleFactor: .45}},
  color: {color: '#3d4450', highlight: '#7aa2f7', opacity: e.dashes ? .55 : .8},
})));

const network = new vis.Network(document.getElementById('net'), {nodes, edges}, {
  physics: {
    solver: 'forceAtlas2Based',
    forceAtlas2Based: {gravitationalConstant: -42, springLength: 95, damping: .4},
    stabilization: {iterations: 200},
  },
  nodes: {font: {color: '#c8cdd6', size: 12, strokeWidth: 0}, borderWidth: 0, scaling: {min: 8, max: 34}},
  edges: {smooth: {type: 'continuous'}, width: 1},
  interaction: {hover: true, tooltipDelay: 120},
});
network.once('stabilizationIterationsDone', () => network.setOptions({physics: false}));

// legend
const legendEl = document.getElementById('legend');
for (const c of DATA.legend) {
  const row = document.createElement('div');
  row.className = 'lg';
  row.innerHTML = `<span class="sw" style="background:${c.color}"></span>${c.name} <small>${c.count}</small>`;
  row.onclick = () => {
    row.classList.toggle('off');
    if (hidden.has(c.id)) hidden.delete(c.id); else hidden.add(c.id);
    nodes.update(DATA.nodes.filter(n => n.group === c.id).map(n => ({id: n.id, hidden: hidden.has(c.id)})));
  };
  legendEl.appendChild(row);
}

// info panel
const info = document.getElementById('info');
network.on('click', p => {
  if (!p.nodes.length) return;
  const id = p.nodes[0];
  const n = DATA.nodes.find(x => x.id === id);
  const meta = nodeMeta.get(id);
  const outgoing = DATA.edges.filter(e => e.from === id).map(e => `<div class="e">→ <b>${label(e.to)}</b> <small>${e.relation}${e.dashes ? ' ·' + e.confidence.toLowerCase() : ''}</small></div>`);
  const incoming = DATA.edges.filter(e => e.to === id).map(e => `<div class="e">← <b>${label(e.from)}</b> <small>${e.relation}</small></div>`);
  info.innerHTML = `<div class="t">${n.label}</div>
    <div class="m">${meta.type ?? ''} · community ${meta.community}${meta.source ? '<br>' + meta.source + (meta.location ? ' ' + meta.location : '') : ''}</div>
    ${outgoing.join('')}${incoming.join('')}`;
});
const label = id => (DATA.nodes.find(x => x.id === id) || {label: id}).label;

// search
document.getElementById('search').addEventListener('input', ev => {
  const q = ev.target.value.toLowerCase();
  if (q.length < 2) return;
  const hit = DATA.nodes.find(n => n.label.toLowerCase().includes(q) || n.id.toLowerCase().includes(q));
  if (hit) {
    network.focus(hit.id, {scale: 1.4, animation: true});
    network.selectNodes([hit.id]);
  }
});
</script>
</body>
</html>
""";
}
