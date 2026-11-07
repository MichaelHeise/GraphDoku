using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Rules.Catalog;

/// <summary><c>Difference</c>: |a − b| = k (<c>mode</c> = "exact") bzw. |a − b| ≥ k (<c>mode</c> = "atLeast").</summary>
public sealed class DifferenceRule : RuleBase
{
    public const string Name = "Difference";
    public const string ModeExact = "exact";
    public const string ModeAtLeast = "atLeast";

    public override string TypeName => Name;

    public override string DisplayName => "Differenz";

    public override RuleKind Kind => RuleKind.Edge;

    public override IReadOnlyList<RuleParameter> Parameters { get; } =
    [
        new("k", RuleParameterType.Integer, "Abstand der beiden Werte (ganze Zahl ≥ 0)", IsRequired: true, SuggestedValue: 1),
        new("mode", RuleParameterType.Choice, "\"exact\": genau k, \"atLeast\": mindestens k; Standard: \"exact\"", DefaultValue: ModeExact, AllowedValues: [ModeExact, ModeAtLeast]),
    ];

    protected override void ValidateValues(IConstraint constraint, Puzzle puzzle, List<string> errors)
    {
        var k = constraint.Params.GetInt("k", 0);
        if (k < 0)
        {
            errors.Add($"Parameter „k“ darf nicht negativ sein, ist aber {k}.");
        }
    }

    protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint)
    {
        var k = constraint.Params.GetIntOrNull("k") ?? throw new RuleParamsException("Pflichtparameter „k“ fehlt.");
        var atLeast = constraint.Params.GetString("mode", ModeExact) == ModeAtLeast;
        return atLeast
            ? EliminateUnsupported(state, constraint, (a, b) => Math.Abs((long)a - b) >= k, $"Abstand mindestens {k}")
            : EliminateUnsupported(state, constraint, (a, b) => Math.Abs((long)a - b) == k, $"Abstand genau {k}");
    }
}
