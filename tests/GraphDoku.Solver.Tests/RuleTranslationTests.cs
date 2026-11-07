using GraphDoku.Core.Model;
using GraphDoku.Solver.OrTools;
using GraphDoku.Solver.Tests.Helpers;
using static GraphDoku.Solver.Tests.Helpers.TestPuzzles;

namespace GraphDoku.Solver.Tests;

/// <summary>
/// Je Regeltyp kleine Rätsel, die genau diese Regel prüfen. Positive Fälle: Jede gefundene Lösung erfüllt die Regel
/// (unabhängiger Prüfer) und die Lösungsanzahl stimmt mit Handrechnung und vollständigem Durchprobieren überein.
/// Negative Fälle: unlösbare Konstellationen werden als unlösbar erkannt.
/// </summary>
public class RuleTranslationTests
{
    private readonly OrToolsSolver _solver = new();

    private void ShouldHaveSolutions(Puzzle puzzle, int expected)
    {
        SolutionVerifier.BruteForceCount(puzzle).Should().Be(expected, "die Handrechnung muss zum Durchprobieren passen");

        var count = _solver.CountSolutions(puzzle, limit: 10_000);
        count.Status.Should().Be(CountStatus.Completed, count.Message);
        count.Count.Should().Be(expected);
        count.Solutions.Where(s => !SolutionVerifier.IsSolution(puzzle, s)).Should().BeEmpty();

        var solve = _solver.Solve(puzzle);
        if (expected == 0)
        {
            solve.Status.Should().Be(SolveStatus.Unsolvable, solve.Message);
        }
        else
        {
            solve.Status.Should().Be(SolveStatus.Solved, solve.Message);
            SolutionVerifier.Violations(puzzle, solve.Values!).Should().BeEmpty();
        }
    }

    private void ShouldBeUnsolvable(Puzzle puzzle) => ShouldHaveSolutions(puzzle, 0);

    private IReadOnlyDictionary<int, int> SolveUnique(Puzzle puzzle)
    {
        _solver.CountSolutions(puzzle).IsUnique.Should().BeTrue();
        var result = _solver.Solve(puzzle);
        result.Status.Should().Be(SolveStatus.Solved, result.Message);
        return result.Values!;
    }

    // AllDifferent

    [Fact]
    public void AllDifferent_allows_all_permutations() =>
        ShouldHaveSolutions(WithGroup("AllDifferent", 3, min: 1, max: 3), 6);

    [Fact]
    public void AllDifferent_with_too_few_values_is_unsolvable() =>
        ShouldBeUnsolvable(WithGroup("AllDifferent", 3, min: 1, max: 2));

    [Fact]
    public void AllDifferent_respects_givens()
    {
        var values = SolveUnique(WithGroup("AllDifferent", 3, min: 1, max: 3).Given(1, 3).Given(2, 1));
        values[3].Should().Be(2);
        ShouldBeUnsolvable(WithGroup("AllDifferent", 2).Given(1, 4).Given(2, 4));
    }

    // CoverRange

    [Fact]
    public void CoverRange_defaults_to_the_puzzle_domain() =>
        ShouldHaveSolutions(WithGroup("CoverRange", 3, min: 1, max: 3), 6);

    [Fact]
    public void CoverRange_with_explicit_range_uses_each_value_exactly_once()
    {
        var puzzle = WithGroup("CoverRange", 3, Params(("min", 2), ("max", 4)), min: 1, max: 6);
        ShouldHaveSolutions(puzzle, 6);
        _solver.Solve(puzzle).Values!.Values.Order().Should().Equal(2, 3, 4);
    }

    [Fact]
    public void CoverRange_single_default_bound_falls_back_to_the_puzzle_domain() =>
        ShouldHaveSolutions(WithGroup("CoverRange", 2, Params(("max", 2)), min: 1, max: 5), 2);

    [Fact]
    public void CoverRange_rejects_values_outside_the_range() =>
        ShouldBeUnsolvable(WithGroup("CoverRange", 3, Params(("min", 2), ("max", 4)), min: 1, max: 6).Given(1, 5));

    [Fact]
    public void CoverRange_is_unsolvable_when_node_domains_cannot_cover_the_range() =>
        ShouldBeUnsolvable(WithGroup("CoverRange", 3, min: 1, max: 3).Domain(1, 1, 2).Domain(2, 1, 2).Domain(3, 1, 2));

    // Sum

    [Fact]
    public void Sum_without_repeats_excludes_equal_values() =>
        ShouldHaveSolutions(WithGroup("Sum", 2, Params(("target", 10))), 8);

    [Fact]
    public void Sum_with_repeats_allows_equal_values() =>
        ShouldHaveSolutions(WithGroup("Sum", 2, Params(("target", 10), ("allowRepeats", true))), 9);

    [Fact]
    public void Sum_with_three_nodes_matches_brute_force() =>
        ShouldHaveSolutions(WithGroup("Sum", 3, Params(("target", 7)), min: 1, max: 5), 6);

    [Fact]
    public void Sum_of_a_single_node_fixes_its_value() =>
        SolveUnique(WithGroup("Sum", 1, Params(("target", 4))))[1].Should().Be(4);

    [Fact]
    public void Sum_that_needs_repeats_is_unsolvable_without_them()
    {
        ShouldBeUnsolvable(WithGroup("Sum", 2, Params(("target", 18))));
        ShouldHaveSolutions(WithGroup("Sum", 2, Params(("target", 18), ("allowRepeats", true))), 1);
    }

    [Fact]
    public void Sum_out_of_reach_is_unsolvable() =>
        ShouldBeUnsolvable(WithGroup("Sum", 3, Params(("target", 2), ("allowRepeats", true))));

    // Thermometer

    [Fact]
    public void Thermometer_counts_strictly_increasing_sequences() =>
        ShouldHaveSolutions(WithGroup("Thermometer", 3, min: 1, max: 4), 4);

    [Fact]
    public void Thermometer_follows_the_list_order()
    {
        var puzzle = WithNodes(3, 1, 3);
        puzzle.Groups.Add(new Group(1, [3, 1, 2], "Thermometer"));

        var values = SolveUnique(puzzle);

        values[3].Should().Be(1);
        values[1].Should().Be(2);
        values[2].Should().Be(3);
    }

    [Fact]
    public void Thermometer_longer_than_the_domain_is_unsolvable() =>
        ShouldBeUnsolvable(WithGroup("Thermometer", 4, min: 1, max: 3));

    [Fact]
    public void Thermometer_with_equal_or_decreasing_givens_is_unsolvable()
    {
        ShouldBeUnsolvable(WithGroup("Thermometer", 2).Given(1, 5).Given(2, 5));
        ShouldBeUnsolvable(WithGroup("Thermometer", 2).Given(1, 6).Given(2, 2));
    }

    // NotEqual

    [Fact]
    public void NotEqual_excludes_equal_pairs() =>
        ShouldHaveSolutions(WithEdge("NotEqual", min: 1, max: 3), 6);

    [Fact]
    public void NotEqual_with_equal_fixed_values_is_unsolvable()
    {
        ShouldBeUnsolvable(WithEdge("NotEqual").Domain(1, 2, 2).Domain(2, 2, 2));
        ShouldBeUnsolvable(WithEdge("NotEqual").Given(1, 7).Given(2, 7));
    }

    // Difference

    [Fact]
    public void Difference_exact() =>
        ShouldHaveSolutions(WithEdge("Difference", Params(("k", 2), ("mode", "exact")), 1, 4), 4);

    [Fact]
    public void Difference_mode_defaults_to_exact() =>
        ShouldHaveSolutions(WithEdge("Difference", Params(("k", 1)), 1, 9), 16);

    [Fact]
    public void Difference_at_least() =>
        ShouldHaveSolutions(WithEdge("Difference", Params(("k", 3), ("mode", "atLeast")), 1, 4), 2);

    [Fact]
    public void Difference_zero_means_equal() =>
        ShouldHaveSolutions(WithEdge("Difference", Params(("k", 0)), 1, 4), 4);

    [Fact]
    public void Difference_works_in_both_directions()
    {
        SolveUnique(WithEdge("Difference", Params(("k", 4))).Given(1, 3).Domain(2, 5, 9))[2].Should().Be(7);
        SolveUnique(WithEdge("Difference", Params(("k", 4))).Given(1, 8).Domain(2, 1, 6))[2].Should().Be(4);
    }

    [Fact]
    public void Difference_larger_than_the_domain_is_unsolvable()
    {
        ShouldBeUnsolvable(WithEdge("Difference", Params(("k", 5)), 1, 4));
        ShouldBeUnsolvable(WithEdge("Difference", Params(("k", 4), ("mode", "atLeast")), 1, 4));
        ShouldBeUnsolvable(WithEdge("Difference", Params(("k", 3), ("mode", "atLeast"))).Given(1, 5).Domain(2, 3, 7));
    }

    // Ratio

    [Fact]
    public void Ratio_default_factor_is_two_in_both_directions() =>
        ShouldHaveSolutions(WithEdge("Ratio", min: 1, max: 6), 6);

    [Fact]
    public void Ratio_with_factor_three() =>
        ShouldHaveSolutions(WithEdge("Ratio", Params(("factor", 3))), 6);

    [Fact]
    public void Ratio_factor_one_means_equal_without_duplicate_solutions() =>
        ShouldHaveSolutions(WithEdge("Ratio", Params(("factor", 1))), 9);

    [Fact]
    public void Ratio_respects_node_domains()
    {
        ShouldHaveSolutions(WithEdge("Ratio").Given(1, 4), 2);
        SolveUnique(WithEdge("Ratio").Given(1, 3))[2].Should().Be(6);
        SolveUnique(WithEdge("Ratio").Given(2, 4).Domain(1, 1, 5))[1].Should().Be(2);
    }

    [Fact]
    public void Ratio_without_partner_value_is_unsolvable()
    {
        ShouldBeUnsolvable(WithEdge("Ratio").Given(1, 5));
        ShouldBeUnsolvable(WithEdge("Ratio").Domain(1, 5, 5).Domain(2, 6, 9));
        ShouldBeUnsolvable(WithEdge("Ratio", Params(("factor", 3)), 4, 8));
    }

    // LessThan

    [Fact]
    public void LessThan_counts_ordered_pairs() =>
        ShouldHaveSolutions(WithEdge("LessThan", min: 1, max: 3), 3);

    [Fact]
    public void LessThan_is_directed_from_to()
    {
        var puzzle = WithNodes(2, 1, 2);
        puzzle.Edges.Add(new Edge(1, 2, 1, "LessThan"));

        var values = SolveUnique(puzzle);

        values[2].Should().Be(1);
        values[1].Should().Be(2);
    }

    [Fact]
    public void LessThan_with_maximum_at_the_start_is_unsolvable()
    {
        ShouldBeUnsolvable(WithEdge("LessThan", min: 1, max: 3).Given(1, 3));
        ShouldBeUnsolvable(WithEdge("LessThan").Given(1, 4).Given(2, 4));
    }

    // Zusammenspiel

    [Fact]
    public void All_rules_combined_match_brute_force()
    {
        var puzzle = WithNodes(4, 1, 4);
        puzzle.Groups.Add(new Group(1, [1, 2, 3, 4], "CoverRange"));
        puzzle.Groups.Add(new Group(2, [1, 2], "Sum", Params(("target", 5))));
        puzzle.Groups.Add(new Group(3, [3, 4], "Thermometer"));
        puzzle.Groups.Add(new Group(4, [2, 3, 4], "AllDifferent"));
        puzzle.Edges.Add(new Edge(1, 1, 3, "Ratio"));
        puzzle.Edges.Add(new Edge(2, 2, 4, "Difference", Params(("k", 2), ("mode", "atLeast"))));
        puzzle.Edges.Add(new Edge(3, 1, 4, "NotEqual"));
        puzzle.Edges.Add(new Edge(4, 3, 4, "LessThan"));

        var expected = SolutionVerifier.BruteForceCount(puzzle);
        expected.Should().BeGreaterThan(0);
        _solver.CountSolutions(puzzle, limit: 1000).Count.Should().Be(expected);
        SolutionVerifier.Violations(puzzle, _solver.Solve(puzzle).Values!).Should().BeEmpty();
    }
}
