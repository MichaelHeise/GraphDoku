using Google.OrTools.Sat;

namespace GraphDoku.Solver.Tests;

/// <summary>Prüft, dass die native OR-Tools-Bibliothek auf der aktuellen Plattform geladen und benutzt werden kann.</summary>
public class OrToolsSmokeTests
{
    [Fact]
    public void CpSat_solves_a_tiny_model()
    {
        var model = new CpModel();
        var a = model.NewIntVar(1, 3, "a");
        var b = model.NewIntVar(1, 3, "b");
        model.Add(a + b == 5);
        model.Add(a < b);

        var solver = new CpSolver();
        var status = solver.Solve(model);

        status.Should().Be(CpSolverStatus.Optimal);
        solver.Value(a).Should().Be(2);
        solver.Value(b).Should().Be(3);
    }
}
