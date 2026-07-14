using System.Text.RegularExpressions;

namespace ContextGraph.Core;

/// <summary>Concept-level extraction of declarative infra: Bicep resources/modules,
/// docker-compose services (indentation mini-parser — the common two-space layout,
/// not full YAML), and pipeline YAML's script references.</summary>
public static partial class InfraExtractor
{
    [GeneratedRegex(@"^\s*resource\s+(\w+)\s+'([^']+)'", RegexOptions.Multiline)] private static partial Regex BicepResourceRx();
    [GeneratedRegex(@"^\s*module\s+(\w+)\s+'([^']+)'", RegexOptions.Multiline)] private static partial Regex BicepModuleRx();
    [GeneratedRegex(@"([\w./\\-]+\.(?:ps1|sh))")] private static partial Regex ScriptRefRx();

    public static Extraction ExtractBicep(string filePath, string rootDir)
    {
        var extraction = new Extraction();
        var text = File.ReadAllText(filePath);
        var relPath = Path.GetRelativePath(rootDir, filePath).Replace('\\', '/');
        extraction.Nodes.Add(new Node { Id = relPath, Label = Path.GetFileName(relPath), Type = "bicep_file", SourceFile = relPath });

        foreach (Match match in BicepResourceRx().Matches(text))
        {
            var id = "infra:" + match.Groups[1].Value;
            extraction.Nodes.Add(new Node
            {
                Id = id,
                Label = match.Groups[1].Value,
                FileType = "concept",
                Type = "infra_resource",
                SourceFile = relPath,
                SourceLocation = "L" + (text[..match.Index].Count(c => c == '\n') + 1),
                Metadata = new() { ["resource_type"] = match.Groups[2].Value },
            });
            extraction.Edges.Add(new Edge { Source = relPath, Target = id, Relation = Relation.Contains, SourceFile = relPath });
        }

        foreach (Match match in BicepModuleRx().Matches(text))
        {
            // Module path is relative to this bicep file; the referenced file declares itself.
            var moduleFile = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, match.Groups[2].Value));
            var target = Path.GetRelativePath(rootDir, moduleFile).Replace('\\', '/');
            extraction.Edges.Add(new Edge { Source = relPath, Target = target, Relation = Relation.Imports, SourceFile = relPath });
        }
        return extraction;
    }

    public static Extraction ExtractCompose(string filePath, string rootDir)
    {
        var extraction = new Extraction();
        var relPath = Path.GetRelativePath(rootDir, filePath).Replace('\\', '/');
        extraction.Nodes.Add(new Node { Id = relPath, Label = Path.GetFileName(relPath), Type = "compose_file", SourceFile = relPath });

        string? currentService = null;
        var inServices = false;
        var inDependsOn = false;
        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;
            var indent = line.Length - line.TrimStart().Length;

            if (indent == 0)
            {
                inServices = line.TrimEnd(':') == "services";
                currentService = null;
                continue;
            }
            if (!inServices)
                continue;

            if (indent == 2 && line.TrimStart().EndsWith(':'))
            {
                currentService = line.Trim().TrimEnd(':');
                inDependsOn = false;
                var id = "service:" + currentService;
                extraction.Nodes.Add(new Node { Id = id, Label = currentService, FileType = "concept", Type = "container_service", SourceFile = relPath });
                extraction.Edges.Add(new Edge { Source = relPath, Target = id, Relation = Relation.Contains, SourceFile = relPath });
            }
            else if (currentService is not null && indent >= 4)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("depends_on"))
                    inDependsOn = true;
                else if (inDependsOn && trimmed.StartsWith("- "))
                    extraction.Edges.Add(new Edge
                    {
                        Source = "service:" + currentService,
                        Target = "service:" + trimmed[2..].Trim(),
                        Relation = Relation.References,
                        SourceFile = relPath,
                    });
                else if (!trimmed.StartsWith("- "))
                    inDependsOn = false;
            }
        }
        return extraction;
    }

    public static Extraction ExtractPipeline(string filePath, string rootDir)
    {
        var extraction = new Extraction();
        var text = File.ReadAllText(filePath);
        var relPath = Path.GetRelativePath(rootDir, filePath).Replace('\\', '/');
        extraction.Nodes.Add(new Node { Id = relPath, Label = Path.GetFileName(relPath), Type = "pipeline_file", SourceFile = relPath });

        foreach (var script in ScriptRefRx().Matches(text).Select(m => Path.GetFileName(m.Groups[1].Value)).Distinct())
        {
            var id = "script:" + script;
            extraction.Nodes.Add(new Node { Id = id, Label = script, FileType = "concept", Type = "script" });
            extraction.Edges.Add(new Edge { Source = relPath, Target = id, Relation = Relation.Calls, SourceFile = relPath });
        }
        return extraction;
    }
}
