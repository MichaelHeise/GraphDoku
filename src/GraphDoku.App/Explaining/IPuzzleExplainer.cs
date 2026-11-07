using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;
using GraphDoku.Explainer;
using GraphDoku.Solver;

namespace GraphDoku.App.Explaining;

/// <summary>
/// Berechnet die Erklärung eines Rätsels für den Erklär-Modus der App. In der App ist das
/// <see cref="EnginePuzzleExplainer"/> (die <see cref="ExplanationEngine"/>); Tests können einen Ersatz einsetzen, der
/// blockiert oder eine Ausnahme wirft.
/// </summary>
public interface IPuzzleExplainer
{
    /// <summary>Erklärt das Rätsel im Hintergrund. Das Rätsel wird sofort (im aufrufenden Thread) kopiert.</summary>
    /// <param name="puzzle">Das Rätsel des Editors.</param>
    /// <param name="solverOptions">Optionen für die Solver-Aufrufe der Erklärung (Zeitlimit der Lösen-Leiste).</param>
    /// <param name="cancellationToken">Abbruch; das Ergebnis hat dann den Status „abgebrochen“.</param>
    Task<ExplanationResult> ExplainAsync(Puzzle puzzle, SolverOptions solverOptions, CancellationToken cancellationToken);
}

/// <summary>Erklärung über die <see cref="ExplanationEngine"/> mit dem Solver der App.</summary>
public sealed class EnginePuzzleExplainer : IPuzzleExplainer
{
    private readonly ISolver _solver;
    private readonly RuleRegistry _registry;

    public EnginePuzzleExplainer(ISolver solver, RuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(registry);
        _solver = solver;
        _registry = registry;
    }

    public Task<ExplanationResult> ExplainAsync(Puzzle puzzle, SolverOptions solverOptions, CancellationToken cancellationToken) =>
        new ExplanationEngine(_solver, _registry, solverOptions).ExplainAsync(puzzle, cancellationToken);
}
