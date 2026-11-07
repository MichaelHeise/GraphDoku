using System.Collections.Immutable;
using GraphDoku.Core.Candidates;

namespace GraphDoku.Explainer;

/// <summary>
/// Zustand des Lösungswegs nach einer bestimmten Anzahl von Schritten: Kandidaten pro Knoten und die Knoten, deren
/// Wert bereits steht (Givens und gesetzte Werte). Ein Knoten mit nur einem Kandidaten gilt erst als gesetzt, wenn
/// ein Schritt „gesetzter Wert“ ihn gesetzt hat.
/// </summary>
public sealed class ExplanationSnapshot
{
    internal ExplanationSnapshot(int appliedSteps, CandidateState candidates, ImmutableHashSet<int> setNodes)
    {
        AppliedSteps = appliedSteps;
        Candidates = candidates;
        SetNodeIds = setNodes;
    }

    /// <summary>Anzahl der Schritte, die in diesem Zustand angewendet sind (0 = Start).</summary>
    public int AppliedSteps { get; }

    /// <summary>Kandidaten pro Knoten (eigene Kopie; Änderungen wirken nicht auf das Ergebnis zurück).</summary>
    public CandidateState Candidates { get; }

    /// <summary>Knoten mit feststehendem Wert (Givens und gesetzte Werte).</summary>
    public IReadOnlySet<int> SetNodeIds { get; }

    public bool IsSet(int nodeId) => SetNodeIds.Contains(nodeId);

    /// <summary>Der feststehende Wert eines Knotens oder <c>null</c>, wenn er noch nicht gesetzt ist.</summary>
    public int? GetSetValue(int nodeId) => IsSet(nodeId) ? Candidates.GetValue(nodeId) : null;
}

/// <summary>Veränderlicher Arbeitszustand: Kandidaten plus gesetzte Knoten. Schritte werden nur hierüber angewendet.</summary>
internal sealed class WorkingState
{
    public WorkingState(CandidateState candidates, ImmutableHashSet<int> setNodes)
    {
        Candidates = candidates;
        SetNodes = setNodes;
    }

    public CandidateState Candidates { get; }

    public ImmutableHashSet<int> SetNodes { get; private set; }

    public bool IsSet(int nodeId) => SetNodes.Contains(nodeId);

    /// <summary>Wendet einen Schritt an (für die Erklär-Schleife und beim Nachspielen identisch).</summary>
    public void Apply(ExplanationStep step)
    {
        foreach (var change in step.Changes)
        {
            Candidates.Apply(change);
        }

        if (step.Kind != StepKind.EliminateCandidates)
        {
            SetNodes = SetNodes.Add(step.NodeId!.Value);
        }
    }

    public WorkingState Clone() => new(Candidates.Clone(), SetNodes);

    public ExplanationSnapshot ToSnapshot(int appliedSteps) => new(appliedSteps, Candidates.Clone(), SetNodes);
}
