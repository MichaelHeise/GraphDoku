using System.Xml.Linq;

namespace GraphDoku.App.Tests;

/// <summary>
/// Sichert die beschlossene Abhängigkeitsrichtung App → Explainer → Solver → Core auf Projektebene:
/// Geprüft werden die <c>ProjectReference</c>- und <c>PackageReference</c>-Einträge der .csproj-Dateien,
/// damit auch noch ungenutzte, aber falsche Verweise auffallen.
/// </summary>
public class ProjectStructureTests
{
    private static readonly string SourceDirectory = Path.Combine(FindRepositoryRoot(), "src");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GraphDoku.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Das Repository-Verzeichnis (mit GraphDoku.sln) wurde nicht gefunden.");
    }

    private static XDocument LoadProject(string project) =>
        XDocument.Load(Path.Combine(SourceDirectory, project, project + ".csproj"));

    private static string[] ProjectReferences(string project) =>
        [.. LoadProject(project).Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)];

    private static string[] PackageReferences(string project) =>
        [.. LoadProject(project).Descendants("PackageReference")
            .Select(e => e.Attribute("Include")!.Value)
            .Order(StringComparer.Ordinal)];

    [Theory]
    [InlineData("GraphDoku.Core", new string[0])]
    [InlineData("GraphDoku.Solver", new[] { "GraphDoku.Core" })]
    [InlineData("GraphDoku.Explainer", new[] { "GraphDoku.Core", "GraphDoku.Solver" })]
    [InlineData("GraphDoku.App", new[] { "GraphDoku.Core", "GraphDoku.Explainer", "GraphDoku.Solver" })]
    public void Project_references_follow_the_dependency_direction(string project, string[] allowed)
    {
        ProjectReferences(project).Should().BeSubsetOf(allowed);
    }

    [Theory]
    [InlineData("GraphDoku.Core", new string[0])]
    [InlineData("GraphDoku.Solver", new[] { "Google.OrTools" })]
    [InlineData("GraphDoku.Explainer", new string[0])]
    public void Library_projects_use_only_allowed_packages(string project, string[] allowed)
    {
        PackageReferences(project).Should().BeSubsetOf(allowed);
    }

    [Fact]
    public void All_source_projects_are_checked()
    {
        Directory.GetDirectories(SourceDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal)
            .Should().Equal("GraphDoku.App", "GraphDoku.Core", "GraphDoku.Explainer", "GraphDoku.Solver");
    }
}
