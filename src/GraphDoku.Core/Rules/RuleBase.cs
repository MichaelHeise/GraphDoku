using System.Globalization;
using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Rules;

/// <summary>Gemeinsame Basis der Katalogregeln: Parameterprüfung, Standardwerte und Hilfen für die Propagation.</summary>
public abstract class RuleBase : IRule
{
    public abstract string TypeName { get; }

    public abstract string DisplayName { get; }

    public abstract RuleKind Kind { get; }

    public virtual int MinNodes => 2;

    public virtual IReadOnlyList<RuleParameter> Parameters { get; } = [];

    public RuleParams CreateDefaultParams()
    {
        var result = new RuleParams();
        foreach (var parameter in Parameters)
        {
            switch (parameter.SuggestedValue ?? parameter.DefaultValue)
            {
                case int i:
                    result.Set(parameter.Name, i);
                    break;
                case bool b:
                    result.Set(parameter.Name, b);
                    break;
                case string s:
                    result.Set(parameter.Name, s);
                    break;
            }
        }

        return result;
    }

    public IReadOnlyList<string> Validate(IConstraint constraint, Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(puzzle);
        var errors = new List<string>();
        var parameters = constraint.Params;

        foreach (var name in parameters.Names)
        {
            if (Parameters.All(p => p.Name != name))
            {
                errors.Add(Parameters.Count == 0
                    ? $"Unbekannter Parameter „{name}“: Diese Regel hat keine Parameter."
                    : $"Unbekannter Parameter „{name}“. Erlaubt sind: {string.Join(", ", Parameters.Select(p => p.Name))}.");
            }
        }

        foreach (var definition in Parameters)
        {
            if (!parameters.TryGetValue(definition.Name, out var value))
            {
                if (definition.IsRequired)
                {
                    errors.Add($"Pflichtparameter „{definition.Name}“ fehlt ({definition.Description}).");
                }

                continue;
            }

            switch (definition.Type)
            {
                case RuleParameterType.Integer when !RuleParams.TryReadInt(value, out _):
                    errors.Add($"Parameter „{definition.Name}“ muss eine ganze Zahl sein, ist aber {RuleParams.Describe(value)}.");
                    break;
                case RuleParameterType.Boolean when value.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False):
                    errors.Add($"Parameter „{definition.Name}“ muss true oder false sein, ist aber {RuleParams.Describe(value)}.");
                    break;
                case RuleParameterType.Choice:
                    var allowed = definition.AllowedValues ?? [];
                    if (value.ValueKind != System.Text.Json.JsonValueKind.String || !allowed.Contains(value.GetString()))
                    {
                        errors.Add($"Parameter „{definition.Name}“ muss einer der Werte {string.Join(", ", allowed.Select(a => $"\"{a}\""))} sein, ist aber {RuleParams.Describe(value)}.");
                    }

                    break;
            }
        }

        var nodeCount = constraint.NodeIds.Count;
        if (nodeCount < MinNodes)
        {
            errors.Add($"Die Regel braucht mindestens {MinNodes} Knoten, es sind aber {nodeCount}.");
        }

        if (errors.Count == 0)
        {
            ValidateValues(constraint, puzzle, errors);
        }

        return errors;
    }

    public IReadOnlyList<CandidateChange> Propagate(CandidateState state, IConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(constraint);

        // Bei fehlenden Knoten oder einem bereits bestehenden Widerspruch wird nichts abgeleitet.
        foreach (var nodeId in constraint.NodeIds)
        {
            if (!state.Contains(nodeId) || state.GetCandidates(nodeId).Count == 0)
            {
                return [];
            }
        }

        return RemoveRedundantEliminations(PropagateCore(state, constraint));
    }

    /// <summary>
    /// Entfernt Streichungen, die im selben Ergebnis schon einmal vorkommen (gleicher Knoten, gleicher Wert).
    /// Mehrere Ableitungsschritte einer Regel lesen denselben unveränderten Zustand und können so denselben
    /// Kandidaten doppelt streichen; die erste Begründung bleibt erhalten, leere Änderungen entfallen.
    /// </summary>
    private static List<CandidateChange> RemoveRedundantEliminations(IReadOnlyList<CandidateChange> changes)
    {
        var eliminated = new HashSet<(int NodeId, int Value)>();
        var result = new List<CandidateChange>(changes.Count);
        foreach (var change in changes)
        {
            if (change.Kind != CandidateChangeKind.Eliminate)
            {
                result.Add(change);
                continue;
            }

            var values = change.Values.Where(v => eliminated.Add((change.NodeId, v))).ToList();
            if (values.Count == change.Values.Count)
            {
                result.Add(change);
            }
            else if (values.Count > 0)
            {
                result.Add(change with { Values = values });
            }
        }

        return result;
    }

    /// <summary>Regelspezifische Prüfung der (typrichtigen) Parameterwerte.</summary>
    protected virtual void ValidateValues(IConstraint constraint, Puzzle puzzle, List<string> errors)
    {
    }

    protected abstract IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint);

    /// <summary>Lesbarer Name des Constraints, z. B. „Gruppe 3 (Zeile 1)“ oder „Kante 7“.</summary>
    protected static string Describe(IConstraint constraint) =>
        constraint is Group { Label: { Length: > 0 } label } ? $"{constraint.Ref} „{label}“" : constraint.Ref.ToString();

    protected static string FormatValues(IEnumerable<int> values) =>
        string.Join(", ", values.Order().Select(v => v.ToString(CultureInfo.InvariantCulture)));

    protected static Reason MakeReason(IConstraint constraint, IEnumerable<int> nodeIds, string text) =>
        new(constraint.Ref, [.. nodeIds.Distinct()], text);

    /// <summary>
    /// Streicht in einer Menge paarweise verschiedener Knoten den Wert jedes festgelegten Knotens bei allen anderen.
    /// </summary>
    /// <param name="state">Kandidatenzustand (wird nicht verändert).</param>
    /// <param name="constraint">Kante oder Gruppe.</param>
    /// <param name="changes">Hier werden die Streichungen angehängt.</param>
    /// <param name="groupRuleSentence">
    /// Bei Gruppen der zweite Satz der Begründung, z. B. „Alle Werte sind verschieden.“ Der Text einer Gruppe hängt
    /// nur vom festgelegten Knoten ab, nicht vom Zielknoten; der Erklär-Modus fasst Streichungen mit gleicher
    /// Begründung zu einem Schritt zusammen („Zeile 3 enthält bereits die 5 …“).
    /// </param>
    protected void EliminateFixedValues(
        CandidateState state,
        IConstraint constraint,
        List<CandidateChange> changes,
        string groupRuleSentence = "Alle Werte sind verschieden.")
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(changes);
        var nodes = constraint.NodeIds;
        var isEdge = Kind.AppliesToEdges();
        foreach (var source in nodes)
        {
            if (state.GetValue(source) is not int value)
            {
                continue;
            }

            foreach (var target in nodes)
            {
                if (target == source || !state.GetCandidates(target).Contains(value))
                {
                    continue;
                }

                var text = isEdge
                    ? $"{DisplayName} in {Describe(constraint)}: Knoten {source} hat den Wert {Format(value)}, also kann Knoten {target} nicht {Format(value)} sein."
                    : $"{Describe(constraint)} enthält bereits die {Format(value)} (Knoten {source}). {groupRuleSentence} Also ist die {Format(value)} in den übrigen Knoten der Gruppe nicht mehr möglich.";
                changes.Add(CandidateChange.Eliminate(target, [value], MakeReason(constraint, [source, target], text)));
            }
        }
    }

    /// <summary>
    /// Kantenkonsistenz für eine zweistellige Relation: Ein Kandidat bleibt nur, wenn der andere Knoten einen
    /// passenden Wert hat. Die Prüfungen erhalten den eigenen Wert und die Kandidaten des anderen Knotens.
    /// </summary>
    /// <param name="state">Kandidatenzustand (wird nicht verändert).</param>
    /// <param name="constraint">Die Kante (From, To).</param>
    /// <param name="fromSupported">Hat der Wert von From einen Partner unter den Kandidaten von To?</param>
    /// <param name="toSupported">Hat der Wert von To einen Partner unter den Kandidaten von From?</param>
    /// <param name="describe">Begründungstext für (Zielknoten, anderer Knoten, gestrichene Werte).</param>
    protected static IReadOnlyList<CandidateChange> EliminateUnsupported(
        CandidateState state,
        IConstraint constraint,
        Func<int, CandidateSet, bool> fromSupported,
        Func<int, CandidateSet, bool> toSupported,
        Func<int, int, IReadOnlyList<int>, string> describe)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(fromSupported);
        ArgumentNullException.ThrowIfNull(toSupported);
        ArgumentNullException.ThrowIfNull(describe);
        var from = constraint.NodeIds[0];
        var to = constraint.NodeIds[1];
        var fromCandidates = state.GetCandidates(from);
        var toCandidates = state.GetCandidates(to);
        var changes = new List<CandidateChange>();

        var removedFrom = fromCandidates.Where(a => !fromSupported(a, toCandidates)).ToList();
        if (removedFrom.Count > 0)
        {
            changes.Add(CandidateChange.Eliminate(from, removedFrom, MakeReason(constraint, [from, to], describe(from, to, removedFrom))));
        }

        var removedTo = toCandidates.Where(b => !toSupported(b, fromCandidates)).ToList();
        if (removedTo.Count > 0)
        {
            changes.Add(CandidateChange.Eliminate(to, removedTo, MakeReason(constraint, [to, from], describe(to, from, removedTo))));
        }

        return changes;
    }

    /// <summary>Standardtext für <see cref="EliminateUnsupported"/>: „… kann nicht 3, 4 sein, weil Knoten 2 (Kandidaten: …) …“.</summary>
    protected string UnsupportedText(IConstraint constraint, CandidateState state, int target, int other, IReadOnlyList<int> removed, string relationText)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(removed);
        var values = FormatValues(removed);
        var otherText = $"Knoten {other} (Kandidaten: {FormatCandidates(state.GetCandidates(other))})";
        return removed.Count == 1
            ? $"{DisplayName} ({relationText}) an {Describe(constraint)}: Knoten {target} kann nicht {values} sein, weil {otherText} dazu keinen passenden Wert hat."
            : $"{DisplayName} ({relationText}) an {Describe(constraint)}: Knoten {target} kann keinen der Werte {values} haben, weil {otherText} dazu keinen passenden Wert hat.";
    }

    /// <summary>true, wenn <paramref name="value"/> im int-Bereich liegt und Kandidat ist.</summary>
    protected static bool ContainsValue(CandidateSet set, long value)
    {
        ArgumentNullException.ThrowIfNull(set);
        return value is >= int.MinValue and <= int.MaxValue && set.Contains((int)value);
    }

    protected static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Kandidaten als Text; lange Listen werden als Bereich mit Anzahl gekürzt.</summary>
    protected static string FormatCandidates(CandidateSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return set.Count switch
        {
            0 => "keine",
            <= 12 => FormatValues(set),
            _ => $"{set.Count} Werte von {Format(set.MinValue)} bis {Format(set.MaxValue)}",
        };
    }
}
