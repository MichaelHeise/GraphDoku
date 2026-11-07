using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;
using GraphDoku.Explainer;

namespace GraphDoku.App.Explaining;

/// <summary>Hervorhebung eines Knotens im Erklär-Modus.</summary>
public enum ExplanationHighlight
{
    None,

    /// <summary>Knoten der verantwortlichen Regel (Ursache des Schlusses), nicht selbst geändert.</summary>
    Rule,

    /// <summary>Vom aktuellen Schritt geänderter Knoten (gesetzt oder Kandidaten gestrichen).</summary>
    Current,

    /// <summary>Vom aktuellen Schritt per Verzweigung (Raten) gesetzter Knoten.</summary>
    CurrentBranch,
}

/// <summary>
/// Anzeigezustand des Erklär-Modus für die Zeichenfläche: Zustand nach <see cref="Position"/> Schritten (gesetzte
/// Werte, Kandidaten), der zuletzt angewendete Schritt mit seinen Knoten und seiner Regel und die bisher geratenen
/// Knoten. Reine Daten ohne Avalonia-Typen; das <see cref="ViewModels.ExplainViewModel"/> erzeugt bei jeder Navigation
/// einen neuen Zustand.
/// </summary>
public sealed class ExplanationView
{
    private static readonly IReadOnlyList<int> NoValues = [];

    private readonly Dictionary<int, IReadOnlyList<int>> _removed = [];

    /// <param name="result">Ergebnis der Erklärung (mit Zuständen, <see cref="ExplanationResult.HasSnapshots"/>).</param>
    /// <param name="position">Anzahl der angewendeten Schritte (0 = Ausgangslage).</param>
    /// <param name="showCandidates">Kandidaten in den offenen Knoten anzeigen.</param>
    public ExplanationView(ExplanationResult result, int position, bool showCandidates)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, result.Steps.Count);
        Position = position;
        ShowCandidates = showCandidates;
        Snapshot = result.GetSnapshot(position);
        CurrentStep = position > 0 ? result.Steps[position - 1] : null;
        GuessedNodeIds = result.Steps.Take(position).Where(s => s.Kind == StepKind.Branch).Select(s => s.NodeId!.Value).ToHashSet();

        if (CurrentStep is { } step)
        {
            CurrentNodeIds = step.NodeIds.ToHashSet();
            RuleNodeIds = step.InvolvedNodeIds.Where(id => !CurrentNodeIds.Contains(id)).ToHashSet();
            foreach (var change in step.Changes.Where(c => c.Kind == CandidateChangeKind.Eliminate))
            {
                _removed[change.NodeId] = change.Values;
            }
        }
        else
        {
            CurrentNodeIds = new HashSet<int>();
            RuleNodeIds = new HashSet<int>();
        }

        // Knoten ohne Kandidaten (Widerspruch am Ende einer unlösbaren Erklärung) rot markieren.
        EmptyNodeIds = Snapshot.Candidates.NodeIds.Where(id => Snapshot.Candidates.GetCandidates(id).IsEmpty).ToHashSet();
    }

    /// <summary>Anzahl der angewendeten Schritte (0 = Ausgangslage).</summary>
    public int Position { get; }

    public bool ShowCandidates { get; }

    /// <summary>Kandidaten und gesetzte Knoten nach <see cref="Position"/> Schritten.</summary>
    public ExplanationSnapshot Snapshot { get; }

    /// <summary>Der zuletzt angewendete Schritt (der „aktuelle“), bei der Ausgangslage <c>null</c>.</summary>
    public ExplanationStep? CurrentStep { get; }

    /// <summary>Vom aktuellen Schritt geänderte Knoten.</summary>
    public IReadOnlySet<int> CurrentNodeIds { get; }

    /// <summary>Weitere Knoten der Begründung (Ursachen des Schlusses), ohne die geänderten.</summary>
    public IReadOnlySet<int> RuleNodeIds { get; }

    /// <summary>Verantwortliche Kante oder Gruppe des aktuellen Schritts.</summary>
    public ConstraintRef? Constraint => CurrentStep?.Constraint;

    public bool IsBranch => CurrentStep?.Kind == StepKind.Branch;

    /// <summary>Knoten, deren Wert bis hierhin durch eine Verzweigung (Raten) gesetzt wurde.</summary>
    public IReadOnlySet<int> GuessedNodeIds { get; }

    /// <summary>Knoten ohne Kandidaten (Widerspruch).</summary>
    public IReadOnlySet<int> EmptyNodeIds { get; }

    public ExplanationHighlight GetHighlight(int nodeId) =>
        CurrentNodeIds.Contains(nodeId) ? (IsBranch ? ExplanationHighlight.CurrentBranch : ExplanationHighlight.Current)
        : RuleNodeIds.Contains(nodeId) ? ExplanationHighlight.Rule
        : ExplanationHighlight.None;

    /// <summary>Feststehender Wert (Given oder gesetzt) oder <c>null</c>.</summary>
    public int? GetSetValue(int nodeId) => Snapshot.Candidates.Contains(nodeId) ? Snapshot.GetSetValue(nodeId) : null;

    public bool IsGuessed(int nodeId) => GuessedNodeIds.Contains(nodeId);

    /// <summary>Kandidaten eines noch offenen Knotens (leer, wenn der Knoten nicht zum Rätsel gehört).</summary>
    public IReadOnlyList<int> GetCandidates(int nodeId) =>
        Snapshot.Candidates.Contains(nodeId) ? [.. Snapshot.Candidates.GetCandidates(nodeId)] : NoValues;

    /// <summary>Vom aktuellen Schritt in diesem Knoten gestrichene Werte (werden durchgestrichen angezeigt).</summary>
    public IReadOnlyList<int> GetRemovedCandidates(int nodeId) => _removed.TryGetValue(nodeId, out var values) ? values : NoValues;
}
