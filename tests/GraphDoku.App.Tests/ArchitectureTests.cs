using System.Reflection;

namespace GraphDoku.App.Tests;

/// <summary>Sichert, dass Core weder UI noch Solver kennt.</summary>
public class ArchitectureTests
{
    [Fact]
    public void Core_references_no_other_project_ui_or_solver_library()
    {
        var references = typeof(GraphDoku.Core.Model.Puzzle).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        references.Should().NotContain(n => n.StartsWith("GraphDoku", StringComparison.Ordinal));
        references.Should().NotContain(n => n.StartsWith("Avalonia", StringComparison.Ordinal));
        references.Should().NotContain(n => n.StartsWith("Google", StringComparison.Ordinal));
        references.Should().NotContain(n => n.StartsWith("CommunityToolkit", StringComparison.Ordinal));
    }

    [Fact]
    public void App_assembly_can_be_loaded()
    {
        Assembly.Load("GraphDoku.App").GetName().Name.Should().Be("GraphDoku.App");
    }
}
