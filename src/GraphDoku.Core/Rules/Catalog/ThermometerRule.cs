using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Rules.Catalog;

/// <summary><c>Thermometer</c>: Werte steigen in Listenreihenfolge streng an.</summary>
public sealed class ThermometerRule : RuleBase
{
    public const string Name = "Thermometer";

    public override string TypeName => Name;

    public override string DisplayName => "Thermometer";

    public override RuleKind Kind => RuleKind.OrderedGroup;

    protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint)
    {
        var nodes = constraint.NodeIds;
        var count = nodes.Count;
        var lower = new long[count];
        var upper = new long[count];

        for (var i = 0; i < count; i++)
        {
            var min = state.GetCandidates(nodes[i]).Min();
            lower[i] = i == 0 ? min : Math.Max(min, lower[i - 1] + 1);
        }

        for (var i = count - 1; i >= 0; i--)
        {
            var max = state.GetCandidates(nodes[i]).Max();
            upper[i] = i == count - 1 ? max : Math.Min(max, upper[i + 1] - 1);
        }

        var changes = new List<CandidateChange>();
        for (var i = 0; i < count; i++)
        {
            var nodeId = nodes[i];
            var lo = lower[i];
            var hi = upper[i];
            var impossible = state.GetCandidates(nodeId).Where(v => v < lo || v > hi).ToList();
            if (impossible.Count > 0)
            {
                var text = lo <= hi
                    ? $"{DisplayName} {Describe(constraint)}: Die Werte steigen streng an, also liegt Knoten {nodeId} (Position {i + 1}) zwischen {lo} und {hi}; streiche {FormatValues(impossible)}."
                    : $"{DisplayName} {Describe(constraint)}: Die Werte können nicht streng ansteigen; für Knoten {nodeId} (Position {i + 1}) bleibt kein Wert.";
                changes.Add(CandidateChange.Eliminate(nodeId, impossible, MakeReason(constraint, nodes, text)));
            }
        }

        return changes;
    }
}
