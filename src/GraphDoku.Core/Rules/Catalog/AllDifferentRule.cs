using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Rules.Catalog;

/// <summary><c>AllDifferent</c>: alle Werte der Gruppe sind verschieden.</summary>
public sealed class AllDifferentRule : RuleBase
{
    public const string Name = "AllDifferent";

    public override string TypeName => Name;

    public override string DisplayName => "Alle verschieden";

    public override RuleKind Kind => RuleKind.Group;

    protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint)
    {
        var changes = new List<CandidateChange>();
        EliminateFixedValues(state, constraint, changes);
        return changes;
    }
}
