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
        var relationText = atLeast ? $"Abstand mindestens {k}" : $"Abstand genau {k}";

        // Ein Wert a hat einen Partner, wenn der andere Knoten a ± k (genau) bzw. einen Wert ≤ a − k oder ≥ a + k
        // (mindestens) als Kandidaten hat.
        Func<int, CandidateSet, bool> supported = atLeast
            ? (a, other) => other.MinValue <= (long)a - k || other.MaxValue >= (long)a + k
            : (a, other) => ContainsValue(other, (long)a + k) || ContainsValue(other, (long)a - k);
        return EliminateUnsupported(
            state,
            constraint,
            supported,
            supported,
            (target, other, removed) => UnsupportedText(constraint, state, target, other, removed, relationText));
    }
}
