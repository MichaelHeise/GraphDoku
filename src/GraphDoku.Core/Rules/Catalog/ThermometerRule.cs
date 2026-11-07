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
        var sets = nodes.Select(state.GetCandidates).ToArray();

        // lower[i]: kleinster Wert, den Position i bei streng steigender Folge davor annehmen kann (nächster
        // Kandidat oberhalb von lower[i − 1]); upper[i] entsprechend von hinten. Genau die Kandidaten in
        // [lower, upper] haben eine passende Fortsetzung in beide Richtungen.
        const long None = long.MaxValue;
        var lower = new long[count];
        var upper = new long[count];
        for (var i = 0; i < count; i++)
        {
            lower[i] = i == 0
                ? sets[0].MinValue
                : lower[i - 1] == None ? None : sets[i].NextAbove(lower[i - 1]) ?? None;
        }

        for (var i = count - 1; i >= 0; i--)
        {
            upper[i] = i == count - 1
                ? sets[i].MaxValue
                : upper[i + 1] == long.MinValue ? long.MinValue : sets[i].NextBelow(upper[i + 1]) ?? long.MinValue;
        }

        var changes = new List<CandidateChange>();
        for (var i = 0; i < count; i++)
        {
            var nodeId = nodes[i];
            var lo = lower[i];
            var hi = upper[i];
            var impossible = sets[i].Where(v => v < lo || v > hi).ToList();
            if (impossible.Count > 0)
            {
                var text = lo < hi
                    ? $"{DisplayName} {Describe(constraint)}: Die Werte steigen streng an, also liegt Knoten {nodeId} (Position {i + 1}) zwischen {lo} und {hi}; streiche {FormatValues(impossible)}."
                    : lo == hi
                        ? $"{DisplayName} {Describe(constraint)}: Die Werte steigen streng an, also muss Knoten {nodeId} (Position {i + 1}) den Wert {lo} haben; streiche {FormatValues(impossible)}."
                        : $"{DisplayName} {Describe(constraint)}: Die Werte können nicht streng ansteigen; für Knoten {nodeId} (Position {i + 1}) bleibt kein Wert.";
                changes.Add(CandidateChange.Eliminate(nodeId, impossible, MakeReason(constraint, nodes, text)));
            }
        }

        return changes;
    }
}
