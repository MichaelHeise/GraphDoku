using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Rules.Catalog;

/// <summary><c>Ratio</c>: a = factor·b oder b = factor·a.</summary>
public sealed class RatioRule : RuleBase
{
    public const string Name = "Ratio";

    public override string TypeName => Name;

    public override string DisplayName => "Verhältnis";

    public override RuleKind Kind => RuleKind.Edge;

    public override IReadOnlyList<RuleParameter> Parameters { get; } =
    [
        new("factor", RuleParameterType.Integer, "Faktor zwischen den Werten (ganze Zahl ≥ 1); Standard: 2", DefaultValue: 2),
    ];

    protected override void ValidateValues(IConstraint constraint, Puzzle puzzle, List<string> errors)
    {
        var factor = constraint.Params.GetInt("factor", 2);
        if (factor < 1)
        {
            errors.Add($"Parameter „factor“ muss mindestens 1 sein, ist aber {factor}.");
        }
    }

    protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint)
    {
        long factor = constraint.Params.GetInt("factor", 2);
        if (factor < 1)
        {
            throw new RuleParamsException($"Parameter „factor“ muss mindestens 1 sein, ist aber {factor}.");
        }

        // Partner von a: a·factor oder (falls ganzzahlig) a / factor. Die Relation ist symmetrisch.
        bool Supported(int a, CandidateSet other) =>
            ContainsValue(other, a * factor) || (a % factor == 0 && ContainsValue(other, a / factor));

        return EliminateUnsupported(
            state,
            constraint,
            Supported,
            Supported,
            (target, other, removed) => UnsupportedText(
                constraint, state, target, other, removed, $"ein Wert ist das {factor}-Fache des anderen"));
    }
}
