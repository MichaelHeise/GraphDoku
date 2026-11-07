using System.Reflection;

namespace GraphDoku.Explainer.Tests;

/// <summary>
/// Sichert die Abhängigkeitsrichtung Explainer → Solver → Core auf Assembly-Ebene (tatsächlich benutzte Verweise).
/// Die Projektverweise in den .csproj-Dateien prüft zusätzlich <c>GraphDoku.App.Tests.ProjectStructureTests</c>.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] ForbiddenPrefixes = ["Avalonia", "CommunityToolkit"];

    private static string[] ReferencedAssemblies(string assemblyName) =>
        [.. Assembly.Load(assemblyName).GetReferencedAssemblies().Select(a => a.Name!).Order(StringComparer.Ordinal)];

    private static string[] GraphDokuOnly(string[] names) =>
        [.. names.Where(n => n.StartsWith("GraphDoku", StringComparison.Ordinal))];

    [Fact]
    public void Explainer_depends_only_on_solver_and_core()
    {
        var references = ReferencedAssemblies("GraphDoku.Explainer");

        GraphDokuOnly(references).Should().BeSubsetOf(["GraphDoku.Core", "GraphDoku.Solver"]);
        references.Should().NotContain(n => ForbiddenPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void Solver_depends_only_on_core()
    {
        var references = ReferencedAssemblies("GraphDoku.Solver");

        GraphDokuOnly(references).Should().BeSubsetOf(["GraphDoku.Core"]);
        references.Should().NotContain(n => ForbiddenPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)));
    }
}
