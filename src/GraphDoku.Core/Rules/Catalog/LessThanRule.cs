using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Rules.Catalog;

/// <summary><c>LessThan</c>: gerichtete Kante, Wert von From &lt; Wert von To.</summary>
public sealed class LessThanRule : RuleBase
{
    public const string Name = "LessThan";

    public override string TypeName => Name;

    public override string DisplayName => "Kleiner als";

    public override RuleKind Kind => RuleKind.DirectedEdge;

    protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint)
    {
        var from = constraint.NodeIds[0];
        var to = constraint.NodeIds[1];
        var relation = $"Knoten {from} < Knoten {to}";
        return EliminateUnsupported(
            state,
            constraint,
            (a, toCandidates) => a < toCandidates.MaxValue,
            (b, fromCandidates) => b > fromCandidates.MinValue,
            (target, other, removed) => target == from
                ? $"{DisplayName} an {Describe(constraint)} ({relation}): Knoten {to} ist höchstens {Format(state.GetCandidates(to).MaxValue)}, also kann Knoten {from} nicht {FormatValues(removed)} sein."
                : $"{DisplayName} an {Describe(constraint)} ({relation}): Knoten {from} ist mindestens {Format(state.GetCandidates(from).MinValue)}, also kann Knoten {to} nicht {FormatValues(removed)} sein.");
    }
}
