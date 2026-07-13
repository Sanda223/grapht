using System.Text;

namespace ContextGraph.Core;

public static class ReportWriter
{
    public static string Render(KnowledgeGraph graph, Analysis analysis, IReadOnlyList<string> diagnostics)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Graph Report");
        sb.AppendLine();
        sb.AppendLine($"**{graph.NodeCount} nodes, {graph.EdgeCount} edges, {analysis.Communities.Count} communities.** " +
                      $"Confidence: {Share(analysis, Confidence.Extracted)}% extracted, " +
                      $"{Share(analysis, Confidence.Inferred)}% inferred, " +
                      $"{Share(analysis, Confidence.Ambiguous)}% ambiguous.");

        sb.AppendLine();
        sb.AppendLine("## Communities");
        sb.AppendLine();
        sb.AppendLine("| # | Name | Members | Cohesion |");
        sb.AppendLine("|---|------|---------|----------|");
        foreach (var (id, members) in analysis.Communities.OrderBy(kv => kv.Key))
            sb.AppendLine($"| {id} | {analysis.CommunityLabels[id]} | {members.Count} | {Louvain.CohesionScore(graph, members):0.00} |");

        sb.AppendLine();
        sb.AppendLine("## Most connected (god nodes)");
        sb.AppendLine();
        for (var i = 0; i < analysis.GodNodes.Count; i++)
        {
            var god = analysis.GodNodes[i];
            var source = graph.Nodes[god.Id].SourceFile;
            sb.AppendLine($"{i + 1}. **{god.Label}** — {god.Degree} edges{(source is null ? "" : $" ({source})")}");
        }

        if (analysis.Surprises.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Surprising connections");
            sb.AppendLine();
            foreach (var surprise in analysis.Surprises)
            {
                var edge = surprise.Edge;
                sb.AppendLine($"- `{graph.Nodes[edge.Source].Label}` --{edge.Relation}--> `{graph.Nodes[edge.Target].Label}` — {surprise.Why}" +
                              (edge.SourceFile is null ? "" : $" ({edge.SourceFile} {edge.SourceLocation})"));
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Suggested questions");
        sb.AppendLine();
        foreach (var question in analysis.Questions)
            sb.AppendLine($"- {question.Question}");

        if (diagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Load diagnostics");
            sb.AppendLine();
            sb.AppendLine("Some projects loaded with problems; their edges may be incomplete or AMBIGUOUS:");
            sb.AppendLine();
            foreach (var diagnostic in diagnostics.Take(20))
                sb.AppendLine($"- {diagnostic}");
        }
        return sb.ToString();
    }

    private static string Share(Analysis analysis, string confidence) =>
        analysis.ConfidenceShare.GetValueOrDefault(confidence, 0).ToString("0.#");
}
