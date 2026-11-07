using GraphDoku.Core.Model;

namespace GraphDoku.Solver;

/// <summary>
/// Asynchrone Varianten für die Oberfläche: Der Solver läuft per <see cref="Task.Run(Func{TResult})"/> auf dem
/// Thread-Pool, die UI bleibt bedienbar. Das Rätsel wird vor dem Start kopiert, damit spätere Änderungen im Editor
/// die laufende Suche nicht beeinflussen. Ein Abbruch liefert ein Ergebnis mit Status „abgebrochen“ und wirft keine
/// <see cref="OperationCanceledException"/>.
/// </summary>
public static class SolverExtensions
{
    public static Task<SolveResult> SolveAsync(
        this ISolver solver,
        Puzzle puzzle,
        SolverOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(puzzle);
        var snapshot = puzzle.Clone();
        return Task.Run(() => solver.Solve(snapshot, options, cancellationToken), CancellationToken.None);
    }

    public static Task<SolutionCountResult> CountSolutionsAsync(
        this ISolver solver,
        Puzzle puzzle,
        int limit = 2,
        SolverOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var snapshot = puzzle.Clone();
        return Task.Run(() => solver.CountSolutions(snapshot, limit, options, cancellationToken), CancellationToken.None);
    }
}
