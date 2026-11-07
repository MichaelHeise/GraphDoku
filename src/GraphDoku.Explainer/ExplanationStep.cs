using System.Globalization;
using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Explainer;

/// <summary>Art eines Schritts im Lösungsweg.</summary>
public enum StepKind
{
    /// <summary>Ein Wert wird gesetzt (logisch hergeleitet).</summary>
    SetValue,

    /// <summary>Kandidaten werden gestrichen.</summary>
    EliminateCandidates,

    /// <summary>Ohne Raten geht es nicht weiter; ein Wert wird aus der Lösung des Solvers übernommen.</summary>
    Branch,
}

/// <summary>Schlussweise eines Schritts, in der festen Reihenfolge vom einfachsten zum schwierigsten.</summary>
public enum ExplanationTechnique
{
    /// <summary>
    /// Der Wert eines Givens oder gesetzten Knotens wird bei den Nachbarn gestrichen (<c>AllDifferent</c>,
    /// <c>CoverRange</c>, <c>NotEqual</c>); dazu Werte außerhalb eines <c>CoverRange</c>-Bereichs.
    /// </summary>
    FixedValueElimination,

    /// <summary>Einziger Kandidat in einem Knoten (naked single).</summary>
    NakedSingle,

    /// <summary>Eine Zahl passt in einer <c>AllDifferent</c>-/<c>CoverRange</c>-Gruppe nur an eine Stelle (hidden single).</summary>
    HiddenSingle,

    /// <summary>Schluss einer Kanten- oder Gruppenregel (<c>IRule.Propagate</c>).</summary>
    RulePropagation,

    /// <summary>Verzweigung mit dem Wert aus der Solver-Lösung.</summary>
    Branch,
}

public static class StepTextExtensions
{
    /// <summary>Deutsche Bezeichnung: „gesetzter Wert“, „gestrichene Kandidaten“, „Verzweigung“.</summary>
    public static string ToGermanText(this StepKind kind) => kind switch
    {
        StepKind.SetValue => "gesetzter Wert",
        StepKind.EliminateCandidates => "gestrichene Kandidaten",
        StepKind.Branch => "Verzweigung",
        _ => kind.ToString(),
    };

    /// <summary>Deutsche Bezeichnung der Schlussweise.</summary>
    public static string ToGermanText(this ExplanationTechnique technique) => technique switch
    {
        ExplanationTechnique.FixedValueElimination => "Feststehenden Wert streichen",
        ExplanationTechnique.NakedSingle => "Einziger Kandidat",
        ExplanationTechnique.HiddenSingle => "Einziger Platz in der Gruppe",
        ExplanationTechnique.RulePropagation => "Regel",
        ExplanationTechnique.Branch => "Verzweigung",
        _ => technique.ToString(),
    };
}

/// <summary>
/// Ein Schritt des Lösungswegs: was sich ändert (<see cref="Changes"/>), warum (<see cref="Reason"/>: Constraint,
/// beteiligte Knoten, deutscher Text) und mit welcher Schlussweise.
/// </summary>
/// <remarks>
/// Ein Schritt „gesetzter Wert“ oder „Verzweigung“ enthält genau eine <see cref="CandidateChangeKind.Assign"/>-Änderung.
/// Ein Schritt „gestrichene Kandidaten“ enthält eine oder mehrere Streichungen mit gemeinsamer Begründung, z. B. alle
/// Streichungen der 5 in Zeile 3, weil die 5 dort schon steht.
/// </remarks>
public sealed class ExplanationStep
{
    public ExplanationStep(int index, StepKind kind, ExplanationTechnique technique, IReadOnlyList<CandidateChange> changes, Reason reason)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(reason);
        if (changes.Count == 0)
        {
            throw new ArgumentException("Ein Schritt braucht mindestens eine Änderung.", nameof(changes));
        }

        var assigns = changes.Count(c => c.Kind == CandidateChangeKind.Assign);
        if (kind == StepKind.EliminateCandidates ? assigns != 0 : assigns != 1 || changes.Count != 1)
        {
            throw new ArgumentException("Setz- und Verzweigungsschritte haben genau eine Zuweisung, Streichschritte keine.", nameof(changes));
        }

        Index = index;
        Kind = kind;
        Technique = technique;
        Changes = changes;
        Reason = reason;
    }

    /// <summary>Position im Lösungsweg (ab 0).</summary>
    public int Index { get; }

    public StepKind Kind { get; }

    public ExplanationTechnique Technique { get; }

    /// <summary>Die Änderungen dieses Schritts (pro Knoten höchstens eine).</summary>
    public IReadOnlyList<CandidateChange> Changes { get; }

    public Reason Reason { get; }

    /// <summary>Verantwortliche Kante oder Gruppe; <c>null</c> bei „einziger Kandidat“ und Verzweigung.</summary>
    public ConstraintRef? Constraint => Reason.Constraint;

    /// <summary>Knoten, die für den Schluss gebraucht werden (zum Hervorheben).</summary>
    public IReadOnlyList<int> InvolvedNodeIds => Reason.NodeIds;

    /// <summary>Deutscher Begründungstext.</summary>
    public string Text => Reason.Text;

    /// <summary>Geänderte Knoten.</summary>
    public IReadOnlyList<int> NodeIds => [.. Changes.Select(c => c.NodeId)];

    /// <summary>Gesetzter Knoten bei „gesetzter Wert“ und Verzweigung, sonst <c>null</c>.</summary>
    public int? NodeId => Kind == StepKind.EliminateCandidates ? null : Changes[0].NodeId;

    /// <summary>Gesetzter Wert bei „gesetzter Wert“ und Verzweigung, sonst <c>null</c>.</summary>
    public int? Value => Kind == StepKind.EliminateCandidates ? null : Changes[0].Values[0];

    /// <summary>Kurzfassung der Wirkung, z. B. „Knoten 21 = 5“ oder „Knoten 19, 20: streiche 5“.</summary>
    public string Summary
    {
        get
        {
            if (Kind != StepKind.EliminateCandidates)
            {
                // Bei einer Verzweigung nennen schon Kind und Begründung das Raten; kein zusätzliches Präfix.
                return $"Knoten {NodeId} = {Format(Value!.Value)}";
            }

            var byValues = Changes.GroupBy(c => string.Join(", ", c.Values.Select(Format)));
            return string.Join("; ", byValues.Select(g =>
                $"Knoten {string.Join(", ", g.Select(c => c.NodeId))}: streiche {g.Key}"));
        }
    }

    /// <summary>Kurzfassung und Begründung, z. B. für eine Liste aller Schritte.</summary>
    public string Description => $"{Summary}. {Text}";

    public override string ToString() => $"Schritt {Index + 1} ({Kind.ToGermanText()}): {Description}";

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
