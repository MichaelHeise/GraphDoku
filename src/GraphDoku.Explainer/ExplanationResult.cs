using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Explainer;

/// <summary>Wie die Erklärung geendet hat.</summary>
public enum ExplanationStatus
{
    /// <summary>Vollständig erklärt; das Rätsel ist eindeutig (ohne Verzweigung durch die Logik selbst belegt, sonst laut Solver).</summary>
    Solved,

    /// <summary>Vollständig erklärt anhand einer von mehreren Lösungen (Verzweigungswerte aus der ersten Solver-Lösung).</summary>
    SolvedAmbiguous,

    /// <summary>
    /// Vollständig erklärt, aber nicht bestätigt: Nach einer Verzweigung ist die Eindeutigkeit offen (Zeitlimit des
    /// Solvers), oder bei eigenen Regeln konnte der Solver die Belegung nicht prüfen (Zeitlimit, kein Adapter).
    /// </summary>
    SolvedUnverified,

    /// <summary>Unlösbar: Die Logik ist auf einen Widerspruch gestoßen oder der Solver hat die Unlösbarkeit bewiesen.</summary>
    Unsolvable,

    /// <summary>Ohne Raten kein Fortschritt und keine Solver-Lösung für eine Verzweigung (Zeitlimit); unvollständig.</summary>
    Stuck,

    /// <summary>Das Rätsel ist ungültig (<see cref="ExplanationResult.Errors"/>); es gibt keine Schritte.</summary>
    Invalid,

    /// <summary>Abgebrochen; <see cref="ExplanationResult.Steps"/> enthält die Schritte bis dahin.</summary>
    Cancelled,
}

/// <summary>
/// Ergebnis des Erklär-Modus: geordnete Schritte, Status mit deutscher Meldung und Zugriff auf den Kandidatenzustand
/// nach jeder Schrittanzahl (für Vor/Zurück und Schieberegler).
/// </summary>
/// <remarks>
/// Zustände werden durch deterministisches Nachspielen der Schritte ab dem nächstgelegenen Zwischenstand erzeugt
/// (alle <see cref="CheckpointInterval"/> Schritte ein Zwischenstand). So bleibt der Speicherbedarf klein, und jeder
/// Zugriff kostet höchstens <see cref="CheckpointInterval"/> − 1 Schritte.
/// </remarks>
public sealed class ExplanationResult
{
    /// <summary>Abstand der gespeicherten Zwischenstände in Schritten.</summary>
    public const int CheckpointInterval = 16;

    private readonly List<WorkingState> _checkpoints;

    internal ExplanationResult(
        Puzzle puzzle,
        ExplanationStatus status,
        string message,
        IReadOnlyList<ExplanationStep> steps,
        List<WorkingState> checkpoints,
        IReadOnlyList<string> errors,
        Reason? contradiction,
        IReadOnlyDictionary<int, int>? solverSolution,
        IReadOnlyDictionary<int, int>? solution)
    {
        Puzzle = puzzle;
        Status = status;
        Message = message;
        Steps = steps;
        _checkpoints = checkpoints;
        Errors = errors;
        Contradiction = contradiction;
        SolverSolution = solverSolution;
        Solution = solution;
    }

    /// <summary>Das erklärte Rätsel.</summary>
    public Puzzle Puzzle { get; }

    public ExplanationStatus Status { get; }

    /// <summary>Deutsche Meldung zum Ergebnis für die Oberfläche.</summary>
    public string Message { get; }

    /// <summary>Die Schritte in Reihenfolge.</summary>
    public IReadOnlyList<ExplanationStep> Steps { get; }

    /// <summary>Fehler bei <see cref="ExplanationStatus.Invalid"/>, sonst leer.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Der gefundene Widerspruch (Constraint, Knoten, Text), wenn die Logik einen gezeigt hat.</summary>
    public Reason? Contradiction { get; }

    /// <summary>Die Lösung, die der Solver für Verzweigungen geliefert hat; <c>null</c>, wenn er nicht gebraucht wurde.</summary>
    public IReadOnlyDictionary<int, int>? SolverSolution { get; }

    /// <summary>Die erklärte Lösung (alle Knoten gesetzt), sonst <c>null</c>.</summary>
    public IReadOnlyDictionary<int, int>? Solution { get; }

    /// <summary>Alle Knoten sind gesetzt.</summary>
    public bool IsComplete => Solution is not null;

    public int BranchCount => Steps.Count(s => s.Kind == StepKind.Branch);

    public bool UsedBranching => BranchCount > 0;

    /// <summary>Es gibt Zustände zum Navigieren (bei ungültigen Rätseln nicht).</summary>
    public bool HasSnapshots => _checkpoints.Count > 0;

    /// <summary>Zustand nach den ersten <paramref name="appliedSteps"/> Schritten (0 = Start, <c>Steps.Count</c> = Ende).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Außerhalb von 0 bis <c>Steps.Count</c>.</exception>
    /// <exception cref="InvalidOperationException">Ungültiges Rätsel, es gibt keine Zustände.</exception>
    public ExplanationSnapshot GetSnapshot(int appliedSteps)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(appliedSteps);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(appliedSteps, Steps.Count);
        if (!HasSnapshots)
        {
            throw new InvalidOperationException("Für ein ungültiges Rätsel gibt es keine Kandidatenzustände.");
        }

        var checkpoint = Math.Min(appliedSteps / CheckpointInterval, _checkpoints.Count - 1);
        var state = _checkpoints[checkpoint].Clone();
        for (var i = checkpoint * CheckpointInterval; i < appliedSteps; i++)
        {
            state.Apply(Steps[i]);
        }

        return state.ToSnapshot(appliedSteps);
    }

    /// <summary>Startzustand (Wertebereiche und Givens).</summary>
    public ExplanationSnapshot GetInitialSnapshot() => GetSnapshot(0);

    /// <summary>Zustand nach dem letzten Schritt.</summary>
    public ExplanationSnapshot GetFinalSnapshot() => GetSnapshot(Steps.Count);

    public override string ToString() => $"{Status} ({Steps.Count} Schritte): {Message}";
}
