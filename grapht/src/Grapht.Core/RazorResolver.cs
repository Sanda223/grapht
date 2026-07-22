namespace Grapht.Core;

/// <summary>Rewires the RazorExtractor's placeholder edge targets against the complete
/// node set, before GraphBuilder runs (which drops edges to unknown nodes):
///   component:X — the .razor component whose file stem is X
///   byname:X    — the unique declared type labelled X (multiple matches → AMBIGUOUS)
///   member:T.M  — method M on the type labelled T
/// Unresolvable placeholders stay as-is and fall out in GraphBuilder (external targets).</summary>
public static class RazorResolver
{
    public static void Resolve(List<Extraction> extractions)
    {
        var byLabel = new Dictionary<string, List<Node>>();
        var componentByStem = new Dictionary<string, string>();
        var jsInvokableById = new Dictionary<string, string>();
        var hubTypeIds = new HashSet<string>();
        var nodeIds = new HashSet<string>();

        var allNodes = extractions.SelectMany(e => e.Nodes).ToList();
        foreach (var node in allNodes)
        {
            nodeIds.Add(node.Id);
            if (!byLabel.TryGetValue(node.Label, out var list))
                byLabel[node.Label] = list = [];
            list.Add(node);
            if (node.Type == "component")
                componentByStem[Path.GetFileNameWithoutExtension(node.Label)] = node.Id;
            if (node.Metadata?.TryGetValue("js_invokable", out var invokableId) == true)
                jsInvokableById[invokableId] = node.Id;
            if (node.Metadata?.ContainsKey("hub") == true)
                hubTypeIds.Add(node.Id);
        }

        // SignalR: hub method name → method node on a Hub-derived type.
        var hubMethodByName = allNodes
            .Where(n => n.Type == "method")
            .Where(n => n.Id.LastIndexOf('.') is var dot && dot > 0 && hubTypeIds.Contains(n.Id[..dot]))
            .GroupBy(n => n.Id[(n.Id.LastIndexOf('.') + 1)..])
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Id, StringComparer.Ordinal).First().Id);

        foreach (var extraction in extractions)
        {
            for (var i = 0; i < extraction.Edges.Count; i++)
            {
                var edge = extraction.Edges[i];
                var (target, confidence) = ResolveTarget(edge.Target, edge.Confidence);
                if (target != edge.Target || confidence != edge.Confidence)
                    extraction.Edges[i] = new Edge
                    {
                        Source = edge.Source, Target = target, Relation = edge.Relation, Context = edge.Context,
                        Confidence = confidence, Weight = edge.Weight,
                        SourceFile = edge.SourceFile, SourceLocation = edge.SourceLocation,
                    };
            }
        }
        return;

        (string Target, string Confidence) ResolveTarget(string target, string confidence)
        {
            if (target.StartsWith("component:"))
            {
                var stem = target["component:".Length..];
                return componentByStem.TryGetValue(stem, out var id) ? (id, confidence) : (target, confidence);
            }
            if (target.StartsWith("byname:"))
            {
                var name = target["byname:".Length..];
                var candidates = TypesLabelled(name);
                if (candidates.Count == 0 && componentByStem.TryGetValue(name, out var component))
                    return (component, confidence);
                return candidates.Count switch
                {
                    0 => (target, confidence),
                    1 => (candidates[0].Id, confidence),
                    _ => (candidates[0].Id, Confidence.Ambiguous),
                };
            }
            if (target.StartsWith("hubmethod:"))
            {
                return hubMethodByName.TryGetValue(target["hubmethod:".Length..], out var hubMethod)
                    ? (hubMethod, confidence)
                    : (target, confidence);
            }
            if (target.StartsWith("dotnet:"))
            {
                // JS → .NET: DotNet.invokeMethodAsync identifier → [JSInvokable] method.
                return jsInvokableById.TryGetValue(target["dotnet:".Length..], out var methodId)
                    ? (methodId, confidence)
                    : (target, confidence);
            }
            if (target.StartsWith("member:"))
            {
                var memberPath = target["member:".Length..];
                var dot = memberPath.LastIndexOf('.');
                if (dot <= 0)
                    return (target, confidence);
                var candidates = TypesLabelled(memberPath[..dot])
                    .Select(t => t.Id + "." + memberPath[(dot + 1)..])
                    .Where(nodeIds.Contains)
                    .ToList();
                return candidates.Count > 0 ? (candidates[0], confidence) : (target, confidence);
            }
            return (target, confidence);
        }

        List<Node> TypesLabelled(string label) =>
            byLabel.TryGetValue(label, out var nodes)
                ? nodes.Where(n => n.Type is "class" or "interface" or "struct" or "record" or "enum" or "component")
                    // Stub declarations repeat the same id; distinct ids define real ambiguity.
                    .DistinctBy(n => n.Id)
                    .OrderBy(n => n.Id, StringComparer.Ordinal).ToList()
                : [];
    }
}
