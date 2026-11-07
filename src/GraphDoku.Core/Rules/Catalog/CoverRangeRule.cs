using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Rules.Catalog;

/// <summary>
/// <c>CoverRange</c>: Die Gruppe enthält jede Zahl des Bereichs [min, max] genau einmal.
/// Ohne Parameter gilt der Wertebereich des Rätsels. Die Knotenanzahl muss zur Bereichsgröße passen.
/// </summary>
public sealed class CoverRangeRule : RuleBase
{
    public const string Name = "CoverRange";

    public override string TypeName => Name;

    public override string DisplayName => "Bereich abdecken";

    public override RuleKind Kind => RuleKind.Group;

    public override int MinNodes => 1;

    public override IReadOnlyList<RuleParameter> Parameters { get; } =
    [
        new("min", RuleParameterType.Integer, "kleinster Wert des Bereichs; Standard: Minimum des Rätselbereichs"),
        new("max", RuleParameterType.Integer, "größter Wert des Bereichs; Standard: Maximum des Rätselbereichs"),
    ];

    /// <summary>Der wirksame Bereich: Parameter oder globaler Wertebereich des Rätsels.</summary>
    public static ValueDomain GetRange(IConstraint constraint, Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(puzzle);
        return new ValueDomain(
            constraint.Params.GetInt("min", puzzle.Domain.Min),
            constraint.Params.GetInt("max", puzzle.Domain.Max));
    }

    protected override void ValidateValues(IConstraint constraint, Puzzle puzzle, List<string> errors)
    {
        var range = GetRange(constraint, puzzle);
        if (!range.IsValid)
        {
            errors.Add($"Der Bereich ist leer: min ({range.Min}) ist größer als max ({range.Max}).");
            return;
        }

        var count = constraint.NodeIds.Count;
        if (range.Size != count)
        {
            errors.Add($"Die Gruppe hat {count} Knoten, der Bereich {range} umfasst aber {range.Size} Werte; beide Anzahlen müssen gleich sein.");
        }
    }

    protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint)
    {
        var range = GetRange(constraint, state.Puzzle);
        var changes = new List<CandidateChange>();
        foreach (var nodeId in constraint.NodeIds)
        {
            var outside = state.GetCandidates(nodeId).Where(v => !range.Contains(v)).ToList();
            if (outside.Count > 0)
            {
                var reason = MakeReason(
                    constraint,
                    [nodeId],
                    $"{DisplayName} in {Describe(constraint)}: Nur Werte von {range.Min} bis {range.Max} sind erlaubt, also streiche {FormatValues(outside)} in Knoten {nodeId}.");
                changes.Add(CandidateChange.Eliminate(nodeId, outside, reason));
            }
        }

        EliminateFixedValues(state, constraint, changes);
        return changes;
    }
}
