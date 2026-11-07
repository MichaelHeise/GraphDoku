using GraphDoku.Core.Model;

namespace GraphDoku.Core.Candidates;

/// <summary>
/// Begründung einer Kandidatenänderung: verantwortlicher Constraint, beteiligte Knoten und kurzer deutscher Text.
/// </summary>
/// <param name="Constraint">
/// Verantwortliche Kante oder Gruppe; <c>null</c> bei Schlüssen, die an keiner Regel hängen (z. B. „einziger
/// Kandidat“ oder eine Verzweigung im Erklär-Modus). Regeln setzen ihn immer.
/// </param>
/// <param name="NodeIds">Knoten, die für den Schluss gebraucht werden (Ursache und Ziel), ohne Doppelte.</param>
/// <param name="Text">Kurzer deutscher Begründungstext.</param>
public sealed record Reason(ConstraintRef? Constraint, IReadOnlyList<int> NodeIds, string Text)
{
    public override string ToString() => Constraint is { } constraint ? $"{constraint}: {Text}" : Text;
}
