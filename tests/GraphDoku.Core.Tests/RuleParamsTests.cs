using System.Text.Json;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Tests;

public class RuleParamsTests
{
    [Fact]
    public void Typed_accessors_return_values_or_defaults()
    {
        var p = new RuleParams().Set("k", 3).Set("flag", true).Set("mode", "atLeast");

        p.GetInt("k", 0).Should().Be(3);
        p.GetInt("missing", 7).Should().Be(7);
        p.GetIntOrNull("missing").Should().BeNull();
        p.GetBool("flag", false).Should().BeTrue();
        p.GetBool("missing", true).Should().BeTrue();
        p.GetString("mode", "exact").Should().Be("atLeast");
        p.GetString("missing", "exact").Should().Be("exact");
        p.Count.Should().Be(3);
    }

    [Fact]
    public void Wrong_types_throw_german_messages()
    {
        var p = new RuleParams().Set("k", "eins").Set("flag", 1).Set("mode", 2);

        p.Invoking(x => x.GetInt("k", 0)).Should().Throw<RuleParamsException>()
            .WithMessage("Parameter „k“ muss eine ganze Zahl sein, ist aber die Zeichenkette \"eins\".");
        p.Invoking(x => x.GetBool("flag", false)).Should().Throw<RuleParamsException>()
            .WithMessage("*true oder false*");
        p.Invoking(x => x.GetString("mode", "")).Should().Throw<RuleParamsException>()
            .WithMessage("*Zeichenkette*Zahl 2*");
    }

    [Fact]
    public void Fractional_numbers_are_not_integers()
    {
        var p = new RuleParams().SetRaw("k", JsonSerializer.SerializeToElement(1.5));
        p.Invoking(x => x.GetInt("k", 0)).Should().Throw<RuleParamsException>();
    }

    [Fact]
    public void Structural_equality_ignores_order_but_not_values()
    {
        var a = new RuleParams().Set("k", 1).Set("mode", "exact");
        var b = new RuleParams().Set("mode", "exact").Set("k", 1);
        var c = new RuleParams().Set("mode", "exact").Set("k", 2);

        a.StructurallyEquals(b).Should().BeTrue();
        a.StructurallyEquals(c).Should().BeFalse();
        a.StructurallyEquals(new RuleParams()).Should().BeFalse();
        new RuleParams().StructurallyEquals(new RuleParams()).Should().BeTrue();
    }

    [Fact]
    public void Clone_is_independent_and_remove_works()
    {
        var a = new RuleParams().Set("k", 1);
        var b = a.Clone();
        b.Set("k", 5);
        b.Remove("k").Should().BeTrue();

        a.GetInt("k", 0).Should().Be(1);
        b.Contains("k").Should().BeFalse();
    }
}
