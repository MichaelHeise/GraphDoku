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

        return PropagateCore(state, constraint);
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
    protected void EliminateFixedValues(CandidateState state, IConstraint constraint, List<CandidateChange> changes)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(changes);
        var nodes = constraint.NodeIds;
        foreach (var source in nodes)
        {
            if (state.GetValue(source) is not int value)
            {
                continue;
            }

            foreach (var target in nodes)
            {
                if (target != source && state.GetCandidates(target).Contains(value))
                {
                    var reason = MakeReason(
                        constraint,
                        [source, target],
                        $"{DisplayName} in {Describe(constraint)}: Knoten {source} hat den Wert {value}, also kann Knoten {target} nicht {value} sein.");
                    changes.Add(CandidateChange.Eliminate(target, [value], reason));
                }
            }
        }
    }

    /// <summary>
    /// Kantenkonsistenz für eine zweistellige Relation: Ein Kandidat bleibt nur, wenn der andere Knoten einen
    /// passenden Wert hat. <paramref name="relation"/> erhält (Wert von From, Wert von To).
    /// </summary>
    protected IReadOnlyList<CandidateChange> EliminateUnsupported(
        CandidateState state,
        IConstraint constraint,
        Func<int, int, bool> relation,
        string relationText)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(relation);
        var from = constraint.NodeIds[0];
        var to = constraint.NodeIds[1];
        var fromCandidates = state.GetCandidates(from);
        var toCandidates = state.GetCandidates(to);
        var changes = new List<CandidateChange>();

        var removedFrom = fromCandidates.Where(a => !toCandidates.Any(b => relation(a, b))).ToList();
        if (removedFrom.Count > 0)
        {
            changes.Add(CandidateChange.Eliminate(from, removedFrom, UnsupportedReason(constraint, from, to, removedFrom, relationText)));
        }

        var removedTo = toCandidates.Where(b => !fromCandidates.Any(a => relation(a, b))).ToList();
        if (removedTo.Count > 0)
        {
            changes.Add(CandidateChange.Eliminate(to, removedTo, UnsupportedReason(constraint, to, from, removedTo, relationText)));
        }

        return changes;
    }

    private Reason UnsupportedReason(IConstraint constraint, int target, int other, List<int> removed, string relationText)
    {
        var values = FormatValues(removed);
        var text = removed.Count == 1
            ? $"{DisplayName} ({relationText}) an {Describe(constraint)}: Knoten {target} kann nicht {values} sein, weil Knoten {other} dazu keinen passenden Wert hat."
            : $"{DisplayName} ({relationText}) an {Describe(constraint)}: Knoten {target} kann keinen der Werte {values} haben, weil Knoten {other} dazu keinen passenden Wert hat.";
        return MakeReason(constraint, [target, other], text);
    }
}
