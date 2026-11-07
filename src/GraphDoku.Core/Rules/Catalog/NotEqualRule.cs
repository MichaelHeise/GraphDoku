using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Rules.Catalog;

/// <summary><c>NotEqual</c>: a ≠ b.</summary>
public sealed class NotEqualRule : RuleBase
{
    public const string Name = "NotEqual";

    public override string TypeName => Name;

    public override string DisplayName => "Ungleich";

    public override RuleKind Kind => RuleKind.Edge;

    protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint)
    {
        var changes = new List<CandidateChange>();
        EliminateFixedValues(state, constraint, changes);
        return changes;
    }
}
