using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.Core.Tests;

public class RuleValidationTests
{
    private static IReadOnlyList<string> ValidateGroup(string rule, RuleParams parameters, int nodeCount = 3, int min = 1, int max = 9)
    {
        var puzzle = TestPuzzles.WithNodes(Math.Max(nodeCount, 1), min, max);
        var group = new Group(1, Enumerable.Range(1, nodeCount), rule, parameters);
        return RuleRegistry.Default.Get(rule).Validate(group, puzzle);
    }

    private static IReadOnlyList<string> ValidateEdge(string rule, RuleParams parameters)
    {
        var puzzle = TestPuzzles.WithNodes(2);
        return RuleRegistry.Default.Get(rule).Validate(new Edge(1, 1, 2, rule, parameters), puzzle);
    }

    [Theory]
    [InlineData("NotEqual")]
    [InlineData("Difference")]
    [InlineData("Ratio")]
    [InlineData("LessThan")]
    public void Default_params_of_edge_rules_are_valid(string rule)
    {
        ValidateEdge(rule, RuleRegistry.Default.Get(rule).CreateDefaultParams()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("AllDifferent")]
    [InlineData("Thermometer")]
    public void Default_params_of_group_rules_are_valid(string rule)
    {
        ValidateGroup(rule, RuleRegistry.Default.Get(rule).CreateDefaultParams()).Should().BeEmpty();
    }

    [Fact]
    public void Default_params_have_documented_values()
    {
        var difference = RuleRegistry.Default.Get("Difference").CreateDefaultParams();
        difference.GetInt("k", 0).Should().Be(1);
        difference.GetString("mode", null).Should().Be("exact");
        RuleRegistry.Default.Get("Ratio").CreateDefaultParams().GetInt("factor", 0).Should().Be(2);
        RuleRegistry.Default.Get("Sum").CreateDefaultParams().GetBool("allowRepeats", true).Should().BeFalse();
        RuleRegistry.Default.Get("CoverRange").CreateDefaultParams().Count.Should().Be(0);
    }

    [Theory]
    [InlineData("AllDifferent")]
    [InlineData("Thermometer")]
    [InlineData("NotEqual")]
    [InlineData("LessThan")]
    public void Rules_without_parameters_reject_any_parameter(string rule)
    {
        var parameters = TestPuzzles.Params(("k", 1));
        var errors = RuleRegistry.Default.Get(rule).Kind.AppliesToEdges()
            ? ValidateEdge(rule, parameters)
            : ValidateGroup(rule, parameters);

        errors.Should().ContainSingle().Which.Should().Be("Unbekannter Parameter „k“: Diese Regel hat keine Parameter.");
    }

    [Fact]
    public void Unknown_parameter_lists_allowed_names()
    {
        ValidateEdge("Difference", TestPuzzles.Params(("k", 1), ("abstand", 2)))
            .Should().ContainSingle().Which.Should().Be("Unbekannter Parameter „abstand“. Erlaubt sind: k, mode.");
    }

    [Fact]
    public void Difference_requires_k()
    {
        ValidateEdge("Difference", new RuleParams())
            .Should().ContainSingle().Which.Should().StartWith("Pflichtparameter „k“ fehlt");
    }

    [Fact]
    public void Difference_rejects_wrong_types_and_values()
    {
        ValidateEdge("Difference", TestPuzzles.Params(("k", "1")))
            .Should().ContainSingle().Which.Should().Be("Parameter „k“ muss eine ganze Zahl sein, ist aber die Zeichenkette \"1\".");
        ValidateEdge("Difference", TestPuzzles.Params(("k", 1), ("mode", "genau")))
            .Should().ContainSingle().Which.Should().Be("Parameter „mode“ muss einer der Werte \"exact\", \"atLeast\" sein, ist aber die Zeichenkette \"genau\".");
        ValidateEdge("Difference", TestPuzzles.Params(("k", -1)))
            .Should().ContainSingle().Which.Should().Contain("nicht negativ");
        ValidateEdge("Difference", TestPuzzles.Params(("k", 2), ("mode", "atLeast"))).Should().BeEmpty();
    }

    [Fact]
    public void Ratio_factor_must_be_positive_integer()
    {
        ValidateEdge("Ratio", new RuleParams()).Should().BeEmpty();
        ValidateEdge("Ratio", TestPuzzles.Params(("factor", 3))).Should().BeEmpty();
        ValidateEdge("Ratio", TestPuzzles.Params(("factor", 0))).Should().ContainSingle().Which.Should().Contain("mindestens 1");
        ValidateEdge("Ratio", TestPuzzles.Params(("factor", true))).Should().ContainSingle().Which.Should().Contain("ganze Zahl");
    }

    [Fact]
    public void Sum_requires_target_and_checks_allowRepeats_type()
    {
        ValidateGroup("Sum", new RuleParams()).Should().ContainSingle().Which.Should().StartWith("Pflichtparameter „target“ fehlt");
        ValidateGroup("Sum", TestPuzzles.Params(("target", 10))).Should().BeEmpty();
        ValidateGroup("Sum", TestPuzzles.Params(("target", 10), ("allowRepeats", true))).Should().BeEmpty();
        ValidateGroup("Sum", TestPuzzles.Params(("target", 10), ("allowRepeats", "ja")))
            .Should().ContainSingle().Which.Should().Contain("true oder false");
    }

    [Fact]
    public void CoverRange_uses_puzzle_domain_by_default_and_checks_group_size()
    {
        ValidateGroup("CoverRange", new RuleParams(), nodeCount: 9).Should().BeEmpty();
        ValidateGroup("CoverRange", new RuleParams(), nodeCount: 8).Should().ContainSingle()
            .Which.Should().Be("Die Gruppe hat 8 Knoten, der Bereich 1..9 umfasst aber 9 Werte; beide Anzahlen müssen gleich sein.");
    }

    [Fact]
    public void CoverRange_with_explicit_range()
    {
        ValidateGroup("CoverRange", TestPuzzles.Params(("min", 1), ("max", 4)), nodeCount: 4).Should().BeEmpty();
        ValidateGroup("CoverRange", TestPuzzles.Params(("max", 4)), nodeCount: 4).Should().BeEmpty();
        ValidateGroup("CoverRange", TestPuzzles.Params(("min", 5), ("max", 4)), nodeCount: 4)
            .Should().ContainSingle().Which.Should().Contain("Der Bereich ist leer");
    }

    [Fact]
    public void Ordered_and_group_rules_check_minimum_node_count()
    {
        ValidateGroup("Thermometer", new RuleParams(), nodeCount: 1)
            .Should().ContainSingle().Which.Should().Be("Die Regel braucht mindestens 2 Knoten, es sind aber 1.");
        ValidateGroup("AllDifferent", new RuleParams(), nodeCount: 1).Should().ContainSingle();
        ValidateGroup("Sum", TestPuzzles.Params(("target", 5)), nodeCount: 1).Should().BeEmpty();
    }

    [Fact]
    public void All_parameter_descriptions_are_present()
    {
        foreach (var rule in RuleRegistry.Default.Rules)
        {
            foreach (var parameter in rule.Parameters)
            {
                parameter.Description.Should().NotBeNullOrWhiteSpace($"{rule.TypeName}.{parameter.Name} braucht eine Beschreibung");
                if (parameter.Type == RuleParameterType.Choice)
                {
                    parameter.AllowedValues.Should().NotBeNullOrEmpty();
                }
            }
        }
    }
}
