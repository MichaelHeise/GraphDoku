using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;
using GraphDoku.Core.Rules.Catalog;
using GraphDoku.Explainer.Tests.Helpers;
using GraphDoku.Solver;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.Explainer.Tests;

/// <summary>Mehrdeutige, unlösbare, ungültige Rätsel, Zeitlimit des Solvers und Abbruch.</summary>
public class ExplanationStatusTests
{
    private static readonly OrToolsSolver Solver = new();

    /// <summary>„Leicht“ mit einer zweiten 5 in Zeile 1 (Knoten 3): Widerspruch über die Kandidaten.</summary>
    private const string ConflictingGivens =
        "535.7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79";

    [Fact]
    public void Ambiguous_puzzle_is_explained_along_one_solver_solution()
    {
        // Zwei Knoten, Werte 1..2, ungleich: genau zwei Lösungen.
        var puzzle = ExplainerPuzzles.WithNodes(2, 1, 2).Edge(1, 2, NotEqualRule.Name);

        var result = new ExplanationEngine(Solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.SolvedAmbiguous);
        result.Message.Should().Contain("mehrdeutig");
        result.BranchCount.Should().Be(1);
        result.SolverSolution.Should().NotBeNull();
        result.Solution.Should().BeEquivalentTo(result.SolverSolution);
        var branch = result.Steps.Should().ContainSingle(s => s.Kind == StepKind.Branch).Subject;
        branch.NodeId.Should().Be(1, "bei Gleichstand der Kandidatenzahl gewinnt die kleinste Id");
        branch.Text.Should().Contain("einer der Lösungen des Solvers");
        ExplanationAcceptanceTests.AssertConsistent(result, result.SolverSolution!);
    }

    [Fact]
    public void Contradiction_found_by_logic_is_reported_as_unsolvable_without_solver()
    {
        var solver = new CountingSolver(Solver);

        var result = new ExplanationEngine(solver).Explain(ExplainerPuzzles.Classic(ConflictingGivens));

        result.Status.Should().Be(ExplanationStatus.Unsolvable);
        result.Message.Should().StartWith("Das Rätsel ist unlösbar. Widerspruch: Knoten");
        result.Contradiction.Should().NotBeNull();
        result.Contradiction!.Text.Should().Contain("hat keinen Kandidaten mehr");
        result.Contradiction.Constraint.Should().NotBeNull("der letzte Streichschritt nennt die verantwortliche Gruppe");
        result.IsComplete.Should().BeFalse();
        result.Solution.Should().BeNull();
        result.Steps.Should().NotBeEmpty();
        solver.CountCalls.Should().Be(0);
        result.GetFinalSnapshot().Candidates.HasContradiction.Should().BeTrue();
    }

    [Fact]
    public void Pigeonhole_contradiction_in_all_different_group()
    {
        var puzzle = ExplainerPuzzles.WithNodes(3, 1, 2).Group(AllDifferentRule.Name, [1, 2, 3], label: "Trio");

        var result = new ExplanationEngine(Solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Unsolvable);
        result.Steps.Should().BeEmpty();
        result.Contradiction!.Constraint.Should().Be(ConstraintRef.ForGroup(1));
        result.Contradiction.Text.Should().Be("Gruppe 1 „Trio“: 3 Knoten brauchen verschiedene Werte, es kommen aber nur noch 2 Werte in Frage (1, 2).");
    }

    [Fact]
    public void Value_without_place_in_cover_range_group_is_a_contradiction()
    {
        var puzzle = ExplainerPuzzles.WithNodes(2, 1, 2).Group(CoverRangeRule.Name, [1, 2]);
        puzzle.Nodes[0].Domain = new ValueDomain(1, 1);
        puzzle.Nodes[1].Domain = new ValueDomain(1, 1);

        var result = new ExplanationEngine(Solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Unsolvable);
        result.Contradiction!.Text.Should().Be("Gruppe 1 muss die 2 enthalten, aber kein Knoten der Gruppe hat sie noch als Kandidaten.");
        result.Contradiction.NodeIds.Should().Equal(1, 2);
    }

    [Fact]
    public void Unsolvable_puzzle_without_logical_contradiction_is_proven_by_the_solver()
    {
        // Vollständiger Graph K4 mit drei Farben: unlösbar, aber ohne festgelegte Werte streicht keine Regel etwas.
        var puzzle = ExplainerPuzzles.WithNodes(4, 1, 3);
        for (var a = 1; a <= 4; a++)
        {
            for (var b = a + 1; b <= 4; b++)
            {
                puzzle.Edge(a, b, NotEqualRule.Name);
            }
        }

        var solver = new CountingSolver(Solver);
        var result = new ExplanationEngine(solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Unsolvable);
        result.Message.Should().Contain("vom Solver bewiesen");
        result.Contradiction.Should().BeNull();
        result.Steps.Should().BeEmpty();
        solver.CountCalls.Should().Be(1);
    }

    [Fact]
    public void Solver_timeout_without_solution_leaves_the_explanation_stuck()
    {
        var solver = new FixedCountSolver(_ => SolutionCountResult.Timeout(0, 2, [], TimeSpan.FromSeconds(1)));

        var result = new ExplanationEngine(solver).Explain(ExplainerPuzzles.Load("klassisch-schwer.json"));

        result.Status.Should().Be(ExplanationStatus.Stuck);
        result.Message.Should().Contain("Ohne Raten geht es nicht weiter").And.Contain("unvollständig");
        result.IsComplete.Should().BeFalse();
        result.Steps.Should().NotBeEmpty();
        result.UsedBranching.Should().BeFalse();
    }

    [Fact]
    public void Solver_timeout_with_one_solution_is_solved_but_unverified()
    {
        var real = Solver.Solve(ExplainerPuzzles.Load("klassisch-schwer.json")).Values!;
        var solver = new FixedCountSolver(_ => SolutionCountResult.Timeout(1, 2, [real], TimeSpan.FromSeconds(1)));

        var result = new ExplanationEngine(solver).Explain(ExplainerPuzzles.Load("klassisch-schwer.json"));

        result.Status.Should().Be(ExplanationStatus.SolvedUnverified);
        result.Message.Should().Contain("Ob die Lösung eindeutig ist, ist offen");
        result.Solution.Should().BeEquivalentTo(real);
    }

    [Fact]
    public void Invalid_puzzle_is_rejected_with_german_errors()
    {
        var puzzle = ExplainerPuzzles.WithNodes(2).Edge(1, 2, "Gibtsnicht");

        var result = new ExplanationEngine(Solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Unbekannter Regeltyp „Gibtsnicht“");
        result.Message.Should().StartWith("Das Rätsel ist ungültig");
        result.Steps.Should().BeEmpty();
        result.HasSnapshots.Should().BeFalse();
        result.Invoking(r => r.GetSnapshot(0)).Should().Throw<InvalidOperationException>().WithMessage("*ungültig*");
    }

    [Fact]
    public void Pre_cancelled_token_yields_cancelled_status()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = new ExplanationEngine(Solver).Explain(ExplainerPuzzles.Load("klassisch-leicht.json"), cts.Token);

        result.Status.Should().Be(ExplanationStatus.Cancelled);
        result.Message.Should().Be("Die Erklärung wurde abgebrochen.");
        result.Steps.Should().BeEmpty();
        result.GetFinalSnapshot().AppliedSteps.Should().Be(0);
    }

    [Fact]
    public void Cancellation_during_solver_call_keeps_the_steps_so_far()
    {
        using var cts = new CancellationTokenSource();
        var solver = new CountingSolver(Solver, beforeCount: cts.Cancel);

        var result = new ExplanationEngine(solver).Explain(ExplainerPuzzles.Load("klassisch-schwer.json"), cts.Token);

        result.Status.Should().Be(ExplanationStatus.Cancelled);
        result.Steps.Should().NotBeEmpty();
        result.UsedBranching.Should().BeFalse();
        result.GetFinalSnapshot().AppliedSteps.Should().Be(result.Steps.Count);
    }

    [Fact]
    public async Task ExplainAsync_explains_a_copy_of_the_puzzle()
    {
        var puzzle = ExplainerPuzzles.Load("klassisch-leicht.json");
        var original = puzzle.Clone();

        var result = await new ExplanationEngine(Solver).ExplainAsync(puzzle);

        result.Status.Should().Be(ExplanationStatus.Solved);
        result.Puzzle.Should().NotBeSameAs(puzzle);
        PuzzleComparer.AreEqual(original, puzzle).Should().BeTrue();
        result.Steps.Should().HaveCount(new ExplanationEngine(Solver).Explain(puzzle).Steps.Count);
    }

    [Fact]
    public void Custom_rule_that_sets_values_creates_set_value_steps()
    {
        var registry = RuleRegistry.Default.With(new EqualRule());
        var puzzle = ExplainerPuzzles.WithNodes(3).Given(1, 4).Group(EqualRule.Name, [1, 2, 3]);

        var result = new ExplanationEngine(new CountingSolver(Solver), registry).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Solved, result.Message);
        result.Steps.Should().HaveCount(2).And.OnlyContain(s =>
            s.Kind == StepKind.SetValue && s.Technique == ExplanationTechnique.RulePropagation && s.Value == 4);
        result.Steps[0].Constraint.Should().Be(ConstraintRef.ForGroup(1));
    }

    /// <summary>Testregel „alle gleich“: setzt die übrigen Knoten, sobald einer feststeht.</summary>
    private sealed class EqualRule : RuleBase
    {
        public const string Name = "Equal";

        public override string TypeName => Name;

        public override string DisplayName => "Alle gleich";

        public override RuleKind Kind => RuleKind.Group;

        protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint)
        {
            var fixedNode = constraint.NodeIds.FirstOrDefault(state.IsFixed);
            if (fixedNode == 0)
            {
                return [];
            }

            var value = state.GetValue(fixedNode)!.Value;
            return
            [
                .. constraint.NodeIds
                    .Where(n => n != fixedNode && state.GetCandidates(n).Contains(value) && !state.IsFixed(n))
                    .Select(n => CandidateChange.Assign(n, value, MakeReason(constraint, [fixedNode, n], $"Knoten {n} = Knoten {fixedNode}."))),
            ];
        }
    }
}
