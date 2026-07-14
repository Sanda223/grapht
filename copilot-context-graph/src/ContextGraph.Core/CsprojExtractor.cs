using System.Xml.Linq;

namespace ContextGraph.Core;

/// <summary>Extracts the dependency inventory from a .csproj: NuGet PackageReferences
/// (version kept on the `uses` edge, so version drift across projects stays visible)
/// and ProjectReferences.</summary>
public static class CsprojExtractor
{
    public static Extraction Extract(string csprojPath, string rootDir)
    {
        var extraction = new Extraction();
        var relPath = TextUtil.RelPath(csprojPath, rootDir);
        var projectId = "project:" + Path.GetFileNameWithoutExtension(csprojPath);
        extraction.Nodes.Add(new Node { Id = projectId, Label = Path.GetFileNameWithoutExtension(csprojPath), Type = "project", SourceFile = relPath });

        XDocument doc;
        try
        {
            doc = XDocument.Load(csprojPath);
        }
        catch (Exception)
        {
            return extraction; // malformed csproj: the load diagnostics already report it
        }

        // Element names are namespace-qualified in legacy csproj, bare in SDK-style.
        foreach (var reference in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
        {
            var name = reference.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(name))
                continue;
            var version = reference.Attribute("Version")?.Value ?? reference.Element("Version")?.Value;
            extraction.Nodes.Add(new Node { Id = "pkg:" + name, Label = name, FileType = "concept", Type = "package" });
            extraction.Edges.Add(new Edge
            {
                Source = projectId,
                Target = "pkg:" + name,
                Relation = Relation.Uses,
                SourceFile = relPath,
                Metadata = version is null ? null : new() { ["version"] = version },
            });
        }

        foreach (var reference in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var include = reference.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(include))
                continue;
            var target = "project:" + Path.GetFileNameWithoutExtension(include.Replace('\\', '/'));
            extraction.Edges.Add(new Edge { Source = projectId, Target = target, Relation = Relation.References, SourceFile = relPath });
        }
        return extraction;
    }

    /// <summary>One extraction per project in the solution.</summary>
    public static IEnumerable<Extraction> ExtractAll(LoadedSolution loaded) =>
        loaded.DistinctProjects
            .Select(p => p.FilePath)
            .Where(p => p is not null && File.Exists(p))
            .Select(p => Extract(p!, loaded.RootDir));
}
