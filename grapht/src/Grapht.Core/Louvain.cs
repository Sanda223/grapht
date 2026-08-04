namespace Grapht.Core;

/// <summary>Louvain community detection on the undirected view. Deterministic: nodes are
/// processed in sorted-id order and ties break by lowest community id, so identical graphs
/// always yield identical communities (the seed=42 discipline of the reference tool).</summary>
public static class Louvain
{
    private const double OversizedFraction = 0.25;
    private const int MinSplitSize = 10;

    /// <summary>Assigns Node.Community on every node and returns communities ordered by
    /// size descending (community 0 is the largest).</summary>
    public static Dictionary<int, List<string>> Cluster(KnowledgeGraph graph, double resolution = 1.0)
    {
        var ids = graph.Nodes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var indexOf = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);

        var weights = new Dictionary<(int, int), double>();
        foreach (var edge in graph.Edges)
        {
            int a = indexOf[edge.Source], b = indexOf[edge.Target];
            if (a == b)
                continue;
            var key = a < b ? (a, b) : (b, a);
            weights[key] = weights.GetValueOrDefault(key) + edge.Weight;
        }

        var membership = Partition(ids.Count, weights, resolution);
        membership = SplitOversized(ids.Count, weights, membership, resolution);

        // Group, order by size desc (ties by sorted member ids) and re-index.
        var groups = Enumerable.Range(0, ids.Count)
            .GroupBy(i => membership[i])
            .Select(g => g.Select(i => ids[i]).OrderBy(x => x, StringComparer.Ordinal).ToList())
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g[0], StringComparer.Ordinal)
            .ToList();

        var communities = new Dictionary<int, List<string>>();
        for (var c = 0; c < groups.Count; c++)
        {
            communities[c] = groups[c];
            foreach (var id in groups[c])
                graph.Nodes[id].Community = c;
        }
        return communities;
    }

    /// <summary>Renumber freshly clustered communities to match a previous run's ids by
    /// best member overlap, so community 3 stays community 3 across rebuilds (stable ids
    /// make graph diffs and cross-session references readable). Unmatched communities get
    /// fresh ids above the previous maximum. Mutates Node.Community; returns the remapped
    /// dictionary.</summary>
    public static Dictionary<int, List<string>> RemapToPrevious(
        KnowledgeGraph graph,
        Dictionary<int, List<string>> current,
        IReadOnlyDictionary<string, int> previousAssignment)
    {
        if (previousAssignment.Count == 0)
            return current;

        // Single ordered pass: biggest communities claim their best previous id first;
        // unmatched ones take fresh ids above the previous maximum.
        var taken = new HashSet<int>();
        var mapping = new Dictionary<int, int>();
        var nextId = previousAssignment.Values.Max() + 1;
        foreach (var (id, members) in current.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key))
        {
            var overlap = new Dictionary<int, int>();
            foreach (var member in members)
                if (previousAssignment.TryGetValue(member, out var previous))
                    overlap[previous] = overlap.GetValueOrDefault(previous) + 1;
            var best = overlap.Where(kv => !taken.Contains(kv.Key))
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
                .Select(kv => (int?)kv.Key).FirstOrDefault();
            mapping[id] = best ?? nextId++;
            if (best is { } match)
                taken.Add(match);
        }

        var remapped = current.ToDictionary(kv => mapping[kv.Key], kv => kv.Value);
        foreach (var (newId, members) in remapped)
            foreach (var member in members)
                graph.Nodes[member].Community = newId;
        return remapped;
    }

    /// <summary>node id → community id, the input RemapToPrevious expects.</summary>
    public static Dictionary<string, int> AssignmentOf(KnowledgeGraph graph) =>
        graph.Nodes.Values
            .Where(n => n.Community is not null)
            .ToDictionary(n => n.Id, n => n.Community!.Value);

    /// <summary>Rebuild the community table from Node.Community values persisted in a
    /// graph.json, so a loaded graph keeps the clustering it was built with instead of
    /// being re-clustered. Falls back to a fresh Cluster() when the file carries no
    /// assignments at all.</summary>
    public static Dictionary<int, List<string>> StoredOrCluster(KnowledgeGraph graph)
    {
        if (!graph.Nodes.Values.Any(n => n.Community is not null))
            return Cluster(graph);
        return graph.Nodes.Values
            .GroupBy(n => n.Community ?? 0)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key,
                          g => g.Select(n => n.Id).OrderBy(id => id, StringComparer.Ordinal).ToList());
    }

    /// <summary>Name each community after its highest-degree member.</summary>
    public static Dictionary<int, string> LabelCommunities(KnowledgeGraph graph, Dictionary<int, List<string>> communities) =>
        communities.ToDictionary(
            kv => kv.Key,
            kv => graph.Nodes[kv.Value.OrderByDescending(graph.Degree).ThenBy(id => id, StringComparer.Ordinal).First()].Label);

    /// <summary>Fraction of possible intra-community edges that exist (0..1).</summary>
    public static double CohesionScore(KnowledgeGraph graph, List<string> members)
    {
        if (members.Count < 2)
            return 1.0;
        var memberSet = members.ToHashSet();
        var intra = graph.Edges.Count(e => memberSet.Contains(e.Source) && memberSet.Contains(e.Target));
        return intra / (members.Count * (members.Count - 1) / 2.0);
    }

    // --- core algorithm ---

    private static int[] Partition(int n, Dictionary<(int, int), double> weights, double resolution)
    {
        if (n == 0 || weights.Count == 0)
            return Enumerable.Range(0, n).ToArray();

        // Level 0: every original node is its own meta-node.
        var membership = Enumerable.Range(0, n).ToArray();
        var currentWeights = weights;
        var currentSize = n;

        while (true)
        {
            var local = LocalMove(currentSize, currentWeights, resolution, out var improved);
            if (!improved)
                break;

            // Renumber communities densely.
            var renumber = local.Distinct().Order().Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
            for (var i = 0; i < n; i++)
                membership[i] = renumber[local[membership[i]]];

            // Aggregate: communities become the next level's nodes. Intra-community weight
            // becomes a self-loop — dropping it makes every level greedily merge upward.
            var aggregated = new Dictionary<(int, int), double>();
            foreach (var ((a, b), w) in currentWeights)
            {
                int ca = renumber[local[a]], cb = renumber[local[b]];
                var key = ca <= cb ? (ca, cb) : (cb, ca);
                aggregated[key] = aggregated.GetValueOrDefault(key) + w;
            }
            var nextSize = renumber.Count;
            if (nextSize == currentSize)
                break;
            currentSize = nextSize;
            currentWeights = aggregated;
        }
        return membership;
    }

    private static int[] LocalMove(int n, Dictionary<(int, int), double> weights, double resolution, out bool improvedAny)
    {
        var neighbors = new Dictionary<int, double>[n];
        for (var i = 0; i < n; i++)
            neighbors[i] = [];
        var degree = new double[n];
        double m2 = 0; // 2 * total weight
        foreach (var ((a, b), w) in weights)
        {
            if (a == b)
            {
                // Self-loop (aggregated intra-community weight): counts double toward the
                // node's degree, contributes no neighbor link — it cancels in move gains.
                degree[a] += 2 * w;
                m2 += 2 * w;
                continue;
            }
            neighbors[a][b] = neighbors[a].GetValueOrDefault(b) + w;
            neighbors[b][a] = neighbors[b].GetValueOrDefault(a) + w;
            degree[a] += w;
            degree[b] += w;
            m2 += 2 * w;
        }
        if (m2 == 0)
        {
            improvedAny = false;
            return Enumerable.Range(0, n).ToArray();
        }

        var community = Enumerable.Range(0, n).ToArray();
        var communityDegree = (double[])degree.Clone();
        improvedAny = false;
        bool movedThisSweep;
        var sweeps = 0;
        do
        {
            movedThisSweep = false;
            for (var node = 0; node < n; node++)
            {
                var home = community[node];
                communityDegree[home] -= degree[node];

                // Weight from node to each adjacent community.
                var linkTo = new Dictionary<int, double> { [home] = 0 };
                foreach (var (other, w) in neighbors[node])
                    linkTo[community[other]] = linkTo.GetValueOrDefault(community[other]) + w;

                var best = home;
                var bestGain = linkTo[home] - resolution * communityDegree[home] * degree[node] / m2;
                foreach (var (candidate, link) in linkTo.OrderBy(kv => kv.Key))
                {
                    var gain = link - resolution * communityDegree[candidate] * degree[node] / m2;
                    if (gain > bestGain + 1e-12)
                    {
                        bestGain = gain;
                        best = candidate;
                    }
                }

                communityDegree[best] += degree[node];
                community[node] = best;
                if (best != home)
                    movedThisSweep = improvedAny = true;
            }
        } while (movedThisSweep && ++sweeps < 100);
        return community;
    }

    private static int[] SplitOversized(int n, Dictionary<(int, int), double> weights, int[] membership, double resolution)
    {
        if (n < MinSplitSize)
            return membership;
        var limit = (int)(n * OversizedFraction);
        var nextId = membership.DefaultIfEmpty(-1).Max() + 1;

        foreach (var group in membership.Select((c, i) => (c, i)).GroupBy(x => x.c).Where(g => g.Count() > Math.Max(limit, MinSplitSize)).ToList())
        {
            var members = group.Select(x => x.i).ToList();
            var localIndex = members.Select((node, i) => (node, i)).ToDictionary(x => x.node, x => x.i);
            var subWeights = new Dictionary<(int, int), double>();
            foreach (var ((a, b), w) in weights)
                if (localIndex.TryGetValue(a, out var la) && localIndex.TryGetValue(b, out var lb))
                    subWeights[la < lb ? (la, lb) : (lb, la)] = w;

            var sub = Partition(members.Count, subWeights, resolution);
            if (sub.Distinct().Count() <= 1)
                continue;
            var remap = new Dictionary<int, int>();
            foreach (var (node, i) in localIndex)
            {
                if (!remap.TryGetValue(sub[i], out var id))
                    remap[sub[i]] = id = nextId++;
                membership[node] = id;
            }
        }
        return membership;
    }
}
