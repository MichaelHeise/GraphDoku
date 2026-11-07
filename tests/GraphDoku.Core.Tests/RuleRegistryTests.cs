using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.Core.Tests;

public class RuleRegistryTests
{
    [Theory]
    [InlineData("AllDifferent", RuleKind.Group)]
    [InlineData("CoverRange", RuleKind.Group)]
    [InlineData("Sum", RuleKind.Group)]
    [InlineData("Thermometer", RuleKind.OrderedGroup)]
    [InlineData("NotEqual", RuleKind.Edge)]
    [InlineData("Difference", RuleKind.Edge)]
    [InlineData("Ratio", RuleKind.Edge)]
    [InlineData("LessThan", RuleKind.DirectedEdge)]
    public void Default_registry_contains_all_catalog_rules(string typeName, RuleKind kind)
    {
        var rule = RuleRegistry.Default.Get(typeName);
        rule.TypeName.Should().Be(typeName);
        rule.Kind.Should().Be(kind);
        rule.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Default_registry_has_exactly_eight_rules()
    {
        RuleRegistry.Default.Rules.Should().HaveCount(8);
    }

    [Fact]
    public void Unknown_type_names_are_rejected_with_list_of_known_types()
    {
        RuleRegistry.Default.TryGet("Kropki", out _).Should().BeFalse();
        RuleRegistry.Default.Invoking(r => r.Get("Kropki")).Should().Throw<KeyNotFoundException>()
            .WithMessage("Unbekannter Regeltyp „Kropki“. Bekannt sind: *AllDifferent*LessThan*");
    }

    [Fact]
    public void Type_names_are_case_sensitive()
    {
        RuleRegistry.Default.Contains("alldifferent").Should().BeFalse();
    }

    [Fact]
    public void New_rules_can_be_added_without_touching_the_default_registry()
    {
        var extended = RuleRegistry.Default.With(new DummyRule());

        extended.Contains("Dummy").Should().BeTrue();
        extended.Rules.Should().HaveCount(9);
        RuleRegistry.Default.Contains("Dummy").Should().BeFalse();
    }

    [Fact]
    public void Duplicate_type_names_are_rejected()
    {
        var act = () => new RuleRegistry([new DummyRule(), new DummyRule()]);
        act.Should().Throw<ArgumentException>().WithMessage("*„Dummy“ ist mehrfach registriert*");
    }

    private sealed class DummyRule : RuleBase
    {
        public override string TypeName => "Dummy";

        public override string DisplayName => "Attrappe";

        public override RuleKind Kind => RuleKind.Edge;

        protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint) => [];
    }
}
