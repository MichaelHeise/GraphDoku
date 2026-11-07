using GraphDoku.Core.Model;

namespace GraphDoku.Solver;

/// <summary>
/// Löst Rätsel und zählt Lösungen. Implementierungen arbeiten synchron und blockierend, prüfen das Rätsel selbst
/// (ungültig → <see cref="SolveStatus.Invalid"/> statt Ausnahme) und brechen über das CancellationToken
/// mit dem Status „abgebrochen“ ab. Für die Oberfläche gibt es asynchrone Varianten in <see cref="SolverExtensions"/>.
/// </summary>
/// <remarks>
/// Die Schnittstelle kennt nur Typen aus GraphDoku.Core, damit ein späterer eigener Lern-Solver sie ohne
/// OR-Tools-Bezug implementieren kann.
/// </remarks>
public interface ISolver
{
    /// <summary>Anzeigename des Solvers, z. B. „OR-Tools CP-SAT“.</summary>
    string Name { get; }

    /// <summary>Sucht eine Lösung.</summary>
    /// <param name="puzzle">Das Rätsel; es wird nicht verändert.</param>
    /// <param name="options">Zeitlimit usw.; <c>null</c> = <see cref="SolverOptions.Default"/>.</param>
    /// <param name="cancellationToken">Bricht die Suche ab (Ergebnis <see cref="SolveStatus.Cancelled"/>).</param>
    SolveResult Solve(Puzzle puzzle, SolverOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zählt Lösungen, höchstens bis <paramref name="limit"/>. Für die Eindeutigkeitsprüfung reicht <c>limit</c> = 2.
    /// </summary>
    /// <remarks>
    /// Das Ergebnis muss die gefundenen Lösungen selbst enthalten: <see cref="SolutionCountResult.Solutions"/> sind die
    /// ersten min(Count, <see cref="SolutionCountResult.MaxStoredSolutions"/>) Lösungen, jede eine vollständige
    /// Belegung aller Knoten-Ids des Rätsels. Der Erklär-Modus übernimmt daraus den Wert beim Verzweigen, die
    /// Oberfläche zeigt sie an. Die Fabrikmethoden von <see cref="SolutionCountResult"/> prüfen die Anzahl.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> ist kleiner als 1.</exception>
    SolutionCountResult CountSolutions(Puzzle puzzle, int limit = 2, SolverOptions? options = null, CancellationToken cancellationToken = default);
}
