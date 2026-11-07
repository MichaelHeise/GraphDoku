using GraphDoku.Core.Model;
using GraphDoku.Solver;

namespace GraphDoku.Explainer.Tests.Helpers;

/// <summary>Reicht an einen echten Solver weiter und zählt die Aufrufe; kann vor dem Zählen etwas auslösen.</summary>
internal sealed class CountingSolver(ISolver inner, Action? beforeCount = null) : ISolver
{
    public int CountCalls { get; private set; }

    public string Name => $"Zählend ({inner.Name})";

    public SolveResult Solve(Puzzle puzzle, SolverOptions? options = null, CancellationToken cancellationToken = default) =>
        inner.Solve(puzzle, options, cancellationToken);

    public SolutionCountResult CountSolutions(Puzzle puzzle, int limit = 2, SolverOptions? options = null, CancellationToken cancellationToken = default)
    {
        CountCalls++;
        beforeCount?.Invoke();
        return inner.CountSolutions(puzzle, limit, options, cancellationToken);
    }
}

/// <summary>Liefert beim Zählen ein festes Ergebnis (z. B. Zeitlimit).</summary>
internal sealed class FixedCountSolver(Func<Puzzle, SolutionCountResult> result) : ISolver
{
    public string Name => "Fest";

    public SolveResult Solve(Puzzle puzzle, SolverOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public SolutionCountResult CountSolutions(Puzzle puzzle, int limit = 2, SolverOptions? options = null, CancellationToken cancellationToken = default) =>
        result(puzzle);
}
