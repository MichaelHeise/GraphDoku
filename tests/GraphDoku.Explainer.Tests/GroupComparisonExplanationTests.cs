using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;
using GraphDoku.Core.Rules.Catalog;
using GraphDoku.Explainer.Tests.Helpers;
using GraphDoku.Solver.OrTools;
using GraphDoku.Solver.Tests.Helpers;

namespace GraphDoku.Explainer.Tests;

/// <summary>
/// Gruppenvergleiche im Erklär-Modus: eigene Stufe nach den Regeln mit Rang 0 bis 2 und vor eigenen Regeln, genaue
/// Begründung, Widerspruch ohne Solver und Übereinstimmung mit dem Solver für jeden Operator.
/// </summary>
public class GroupComparisonExplanationTests
{
    private static readonly OrToolsSolver Solver = new();

    [Fact]
    public void Comparison_step_names_both_groups_and_the_ranges()
    {
        var puzzle = ExplainerPuzzles.ComparisonPair();

        var result = new ExplanationEngine(Solver).Explain(puzzle);

        var first = result.Steps[0];
        first.Kind.Should().Be(StepKind.EliminateCandidates);
        first.Technique.Should().Be(ExplanationTechnique.RulePropagation);
        first.Constraint.Should().Be(ConstraintRef.ForGroupComparison(1));
        first.Changes.Should().ContainSingle().Which.Values.Should().Equal(1, 2);
        first.NodeIds.Should().Equal(1);
        first.InvolvedNodeIds.Should().Equal([1, 2, 3, 4], "beide Gruppen sind am Schluss beteiligt");
        first.Text.Should().Be(
            "Gruppenvergleich 1: Summe von Gruppe 1 „Links“ ≥ Summe von Gruppe 2 „Rechts“: Knoten 1 kann nicht 1, 2 sein, denn die Summe von Gruppe 2 „Rechts“ ist genau 7 und die übrigen Knoten von Gruppe 1 „Links“ ergeben zusammen 1 bis 4.");
        first.ToString().Should().Be($"Schritt 1 (gestrichene Kandidaten): Knoten 1: streiche 1, 2. {first.Text}");
    }

    [Theory]
    [InlineData(nameof(ExplainerPuzzles.ComparisonPair))]
    [InlineData(nameof(ExplainerPuzzles.ComparisonChain))]
    public void Comparison_puzzle_is_explained_without_branching_and_without_solver(string name)
    {
        var puzzle = name == nameof(ExplainerPuzzles.ComparisonPair) ? ExplainerPuzzles.ComparisonPair() : ExplainerPuzzles.ComparisonChain();
        var solver = new CountingSolver(Solver);
        var count = Solver.CountSolutions(puzzle, 2);
        count.IsUnique.Should().BeTrue(count.Message);

        var result = new ExplanationEngine(solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Solved, result.Message);
        result.UsedBranching.Should().BeFalse();
        solver.CountCalls.Should().Be(0, "Gruppenvergleiche zählen wie Katalogregeln und brauchen keine Bestätigung durch den Solver");
        result.Solution.Should().BeEquivalentTo(count.Solutions[0]);
        result.Steps.Should().Contain(s => s.Constraint != null && s.Constraint.Value.Kind == ConstraintKind.GroupComparison);
        ExplanationAcceptanceTests.AssertConsistent(result, count.Solutions[0]);
    }

    [Fact]
    public void Shared_nodes_are_left_out_of_the_reason()
    {
        var result = new ExplanationEngine(Solver).Explain(ExplainerPuzzles.ComparisonChain());

        var first = result.Steps[0];
        first.Constraint.Should().Be(ConstraintRef.ForGroupComparison(1));
        first.InvolvedNodeIds.Should().Equal(1, 2, 3);
        first.Text.Should().Be(
            "Gruppenvergleich 1: Summe von Gruppe 1 < Summe von Gruppe 2: Knoten 1 kann nicht 3 sein, denn die Summe von Gruppe 2 liegt zwischen 1 und 3 (gemeinsame Knoten zählen auf beiden Seiten gleich und bleiben außen vor).");
        result.Steps.Where(s => s.Constraint is { Kind: ConstraintKind.GroupComparison })
            .Select(s => s.Constraint!.Value.Id)
            .Should().Contain([1, 2], "beide Vergleiche tragen zur Lösung bei");
    }

    [Fact]
    public void Impossible_comparison_is_a_contradiction_without_solver()
    {
        // Gruppe 1 ergibt 3 (Werte 1 und 2), Gruppe 2 ergibt 17 (Werte 8 und 9); verlangt ist Gruppe 1 > Gruppe 2.
        var puzzle = ExplainerPuzzles.WithNodes(4)
            .Group(SumRule.Name, [1, 2], ExplainerPuzzles.Params(("target", 3)))
            .Group(SumRule.Name, [3, 4], ExplainerPuzzles.Params(("target", 17)))
            .Compare(1, ComparisonOperator.Greater, 2);
        var solver = new CountingSolver(Solver);

        var result = new ExplanationEngine(solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Unsolvable, result.Message);
        solver.CountCalls.Should().Be(0);
        result.UsedBranching.Should().BeFalse();
        var last = result.Steps[^1];
        last.Constraint.Should().Be(ConstraintRef.ForGroupComparison(1));
        last.Changes.Should().ContainSingle().Which.Values.Should().Equal(1, 2);
        last.Text.Should().Be(
            "Gruppenvergleich 1: Summe von Gruppe 1 > Summe von Gruppe 2: Knoten 1 kann nicht 1, 2 sein, denn die Summe von Gruppe 2 liegt zwischen 16 und 18 und die übrigen Knoten von Gruppe 1 ergeben zusammen 1 bis 2.");
        result.Steps.Take(result.Steps.Count - 1).Should().OnlyContain(s => s.Constraint != null && s.Constraint.Value.Kind == ConstraintKind.Group, "erst wirken die Summenregeln");
        result.Contradiction!.Constraint.Should().Be(ConstraintRef.ForGroupComparison(1));
        result.Contradiction.Text.Should().Be($"Knoten 1 hat keinen Kandidaten mehr (zuletzt gestrichen in Schritt {result.Steps.Count}).");
        result.Contradiction.NodeIds.Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void Comparisons_come_after_sum_rules_and_before_custom_rules()
    {
        // Summe 1 + 2 = 4, Gruppe 1 > Gruppe 2 = [3, 4] und die eigene Regel „keine 1“ in Gruppe 3 = [4] können alle
        // streichen. Der Solver kennt die eigene Regel nicht; hier zählt nur die Reihenfolge der Schritte.
        var registry = RuleRegistry.Default.With(new NoOneRule());
        var puzzle = ExplainerPuzzles.WithNodes(4)
            .Group(SumRule.Name, [1, 2], ExplainerPuzzles.Params(("target", 4)))
            .Group(AllDifferentRule.Name, [3, 4])
            .Group(NoOneRule.Name, [4])
            .Compare(1, ComparisonOperator.Greater, 2);

        var result = new ExplanationEngine(Solver, registry).Explain(puzzle);

        var ruleSteps = result.Steps.Where(s => s.Technique == ExplanationTechnique.RulePropagation).ToList();
        var firstComparison = ruleSteps.FindIndex(s => s.Constraint == ConstraintRef.ForGroupComparison(1));
        var firstCustom = ruleSteps.FindIndex(s => s.Constraint == ConstraintRef.ForGroup(3));
        firstComparison.Should().BePositive("erst wirken die Summenregeln (Rang 2)");
        ruleSteps.Take(firstComparison).Should().OnlyContain(s => s.Constraint!.Value.Kind == ConstraintKind.Group && s.Constraint.Value.Id <= 2);
        firstCustom.Should().BeGreaterThan(firstComparison, "eigene Regeln (Rang 3) kommen nach den Gruppenvergleichen");
    }

    [Fact]
    public void Explanations_with_random_comparisons_agree_with_the_solver_for_every_operator()
    {
        var random = new Random(2026);
        var explained = 0;
        foreach (var op in ComparisonOperators.All)
        {
            for (var round = 0; round < 25; round++)
            {
                var puzzle = ExplainerPuzzles.WithNodes(4, 1, 3);
                var groupCount = random.Next(2, 4);
                for (var g = 0; g < groupCount; g++)
                {
                    puzzle.Group(AllDifferentRule.Name, Enumerable.Range(1, 4).OrderBy(_ => random.Next()).Take(random.Next(2, 4)).ToList());
                }

                var left = random.Next(1, groupCount + 1);
                var right = (left % groupCount) + 1;
                puzzle.Compare(left, op, right);
                if (random.Next(2) == 0)
                {
                    puzzle.Compare(right, ComparisonOperators.All[random.Next(6)], (right % groupCount) + 1);
                }

                if (random.Next(2) == 0)
                {
                    puzzle.Given(random.Next(1, 5), random.Next(1, 4));
                }

                if (Core.Validation.PuzzleValidator.Validate(puzzle).Count > 0)
                {
                    continue;
                }

                var context = $"{string.Join(", ", puzzle.GroupComparisons)} mit {string.Join(", ", puzzle.Groups.Select(g => $"[{string.Join(" ", g.Nodes)}]"))}";
                var count = Solver.CountSolutions(puzzle, 2);
                var result = new ExplanationEngine(Solver).Explain(puzzle);
                explained++;

                if (count.Count == 0)
                {
                    result.Status.Should().Be(ExplanationStatus.Unsolvable, context);
                    continue;
                }

                result.Status.Should().Be(count.IsUnique ? ExplanationStatus.Solved : ExplanationStatus.SolvedAmbiguous, context);
                SolutionVerifier.Violations(puzzle, result.Solution!).Should().BeEmpty(context);
                if (count.IsUnique)
                {
                    result.Solution.Should().BeEquivalentTo(count.Solutions[0], context);
                }

                ExplanationAcceptanceTests.AssertConsistent(result, result.Solution!);
            }
        }

        explained.Should().BeGreaterThan(100, "die meisten Zufallsrätsel sind gültig");
    }

    /// <summary>Eigene Testregel „keine 1“: streicht die 1 in allen Knoten der Gruppe (korrekt, eigene Regel mit Rang 3).</summary>
    private sealed class NoOneRule : RuleBase
    {
        public const string Name = "NoOne";

        public override string TypeName => Name;

        public override string DisplayName => "Keine 1";

        public override RuleKind Kind => RuleKind.Group;

        public override int MinNodes => 1;

        protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint) =>
        [
            .. constraint.NodeIds
                .Where(n => state.GetCandidates(n).Contains(1))
                .Select(n => CandidateChange.Eliminate(n, [1], MakeReason(constraint, [n], $"Knoten {n} kann nicht 1 sein."))),
        ];
    }
}
