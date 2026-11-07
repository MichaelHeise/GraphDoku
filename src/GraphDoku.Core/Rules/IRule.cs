using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Rules;

/// <summary>
/// Ein Regeltyp aus dem Katalog. Die Übersetzung in OR-Tools-Constraints liegt im Solver-Projekt
/// (Adapter pro <see cref="TypeName"/>), damit Core frei von OR-Tools bleibt.
/// </summary>
public interface IRule
{
    /// <summary>Eindeutiger Typname, wie er im JSON-Feld <c>rule</c> steht (z. B. "AllDifferent").</summary>
    string TypeName { get; }

    /// <summary>Deutscher Anzeigename (z. B. "Alle verschieden").</summary>
    string DisplayName { get; }

    RuleKind Kind { get; }

    /// <summary>Mindestanzahl beteiligter Knoten.</summary>
    int MinNodes { get; }

    IReadOnlyList<RuleParameter> Parameters { get; }

    /// <summary>Parameter für eine neu angelegte Regel (Standard- bzw. Vorschlagswerte).</summary>
    RuleParams CreateDefaultParams();

    /// <summary>
    /// Prüft Parameter und Knotenanzahl des Constraints. Liefert deutsche Fehlermeldungen (leer = gültig).
    /// Strukturelle Prüfungen (existieren die Knoten?) übernimmt der <see cref="Validation.PuzzleValidator"/>.
    /// </summary>
    IReadOnlyList<string> Validate(IConstraint constraint, Puzzle puzzle);

    /// <summary>
    /// Leitet aus dem aktuellen Kandidatenzustand Streichungen oder gesetzte Werte ab.
    /// Der Zustand wird dabei nicht verändert; der Aufrufer wendet die Änderungen mit
    /// <see cref="CandidateState.Apply"/> an. Jede Änderung trägt eine <see cref="Reason"/>.
    /// Die Parameter müssen gültig sein (<see cref="Validate"/>).
    /// </summary>
    /// <remarks>
    /// Die Schlüsse müssen korrekt sein: Ein Wert darf nur gestrichen werden, wenn keine erfüllende Belegung der
    /// beteiligten Knoten ihn nutzt (der Erklär-Modus verlässt sich darauf). Das Ergebnis soll nur von den Kandidaten
    /// der Knoten des Constraints abhängen. Empfohlen: Bei einer vollständig festgelegten Belegung, die die Regel
    /// verletzt, einen Wert streichen (Widerspruch). Der Erklär-Modus lässt Belegungen mit Regeln außerhalb des
    /// Katalogs zusätzlich vom Solver bestätigen.
    /// </remarks>
    IReadOnlyList<CandidateChange> Propagate(CandidateState state, IConstraint constraint);
}
