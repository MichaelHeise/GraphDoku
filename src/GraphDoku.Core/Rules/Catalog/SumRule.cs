using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Rules.Catalog;

/// <summary><c>Sum</c>: Die Summe der Gruppe ist <c>target</c>; ohne <c>allowRepeats</c> sind die Werte verschieden.</summary>
public sealed class SumRule : RuleBase
{
    public const string Name = "Sum";

    public override string TypeName => Name;

    public override string DisplayName => "Summe";

    public override RuleKind Kind => RuleKind.Group;

    public override int MinNodes => 1;

    public override IReadOnlyList<RuleParameter> Parameters { get; } =
    [
        new("target", RuleParameterType.Integer, "Zielsumme", IsRequired: true),
        new("allowRepeats", RuleParameterType.Boolean, "dürfen sich Werte wiederholen? Standard: false", DefaultValue: false),
    ];

    protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint)
    {
        var target = constraint.Params.GetIntOrNull("target")
            ?? throw new RuleParamsException("Pflichtparameter „target“ fehlt.");
        var allowRepeats = constraint.Params.GetBool("allowRepeats", false);
        var nodes = constraint.NodeIds;
        var changes = new List<CandidateChange>();

        long totalMin = 0;
        long totalMax = 0;
        foreach (var nodeId in nodes)
        {
            var candidates = state.GetCandidates(nodeId);
            totalMin += candidates.Min();
            totalMax += candidates.Max();
        }

        foreach (var nodeId in nodes)
        {
            var candidates = state.GetCandidates(nodeId);
            var othersMin = totalMin - candidates.Min();
            var othersMax = totalMax - candidates.Max();
            var impossible = candidates.Where(v => target - (long)v < othersMin || target - (long)v > othersMax).ToList();
            if (impossible.Count > 0)
            {
                var others = nodes.Count == 1
                    ? "der Knoten allein muss die Summe ergeben"
                    : $"die übrigen Knoten ergeben zusammen {othersMin} bis {othersMax}";
                var reason = MakeReason(
                    constraint,
                    nodes,
                    $"{DisplayName} {target} in {Describe(constraint)}: Knoten {nodeId} kann nicht {FormatValues(impossible)} sein, denn {others}.");
                changes.Add(CandidateChange.Eliminate(nodeId, impossible, reason));
            }
        }

        if (!allowRepeats)
        {
            EliminateFixedValues(state, constraint, changes);
        }

        return changes;
    }
}
