using System.Reflection;

namespace GraphDoku.Explainer.Tests;

/// <summary>Sichert die Abhängigkeitsrichtung Explainer → Solver → Core.</summary>
public class ArchitectureTests
{
    private static string[] ReferencedGraphDokuAssemblies(string assemblyName) =>
        [.. Assembly.Load(assemblyName).GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("GraphDoku", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void Explainer_depends_only_on_solver_and_core()
    {
        ReferencedGraphDokuAssemblies("GraphDoku.Explainer").Should().NotContain("GraphDoku.App");
    }

    [Fact]
    public void Solver_depends_only_on_core()
    {
        ReferencedGraphDokuAssemblies("GraphDoku.Solver").Should().BeSubsetOf(["GraphDoku.Core"]);
    }
}
