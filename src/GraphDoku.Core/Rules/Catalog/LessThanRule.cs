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

    protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint) =>
        EliminateUnsupported(
            state,
            constraint,
            (a, b) => a < b,
            $"Knoten {constraint.NodeIds[0]} < Knoten {constraint.NodeIds[1]}");
}
