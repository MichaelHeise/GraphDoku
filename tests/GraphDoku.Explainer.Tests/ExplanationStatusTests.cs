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
    public void Solver_with_incomplete_solution_leaves_the_explanation_stuck_instead_of_crashing()
    {
        // Regression (Abschluss-Review): TryBranch indizierte blind in die Lösung des Solvers.
        var puzzle = ExplainerPuzzles.WithNodes(2, 1, 2).Edge(1, 2, NotEqualRule.Name);
        IReadOnlyDictionary<int, int> partial = new Dictionary<int, int> { [2] = 1 };
        var solver = new FixedCountSolver(_ => SolutionCountResult.Completed(1, 2, [partial], TimeSpan.Zero));

        var result = new ExplanationEngine(solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Stuck);
        result.Message.Should().Contain("keine vollständige Lösung").And.Contain("Knoten 1").And.Contain("„Fest“");
        result.IsComplete.Should().BeFalse();
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
        var solver = new CountingSolver(new OrToolsSolver(registry, OrToolsAdapterRegistry.Default.With(new EqualAdapter())));
        var puzzle = ExplainerPuzzles.WithNodes(3).Given(1, 4).Group(EqualRule.Name, [1, 2, 3]);

        var result = new ExplanationEngine(solver, registry).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Solved, result.Message);
        result.Message.Should().Contain("der Solver bestätigt");
        result.Steps.Should().HaveCount(2).And.OnlyContain(s =>
            s.Kind == StepKind.SetValue && s.Technique == ExplanationTechnique.RulePropagation && s.Value == 4);
        result.Steps[0].Constraint.Should().Be(ConstraintRef.ForGroup(1));
        solver.CountCalls.Should().Be(1, "eine eigene Regel könnte einen Verstoß übersehen; der Solver bestätigt die Belegung");
    }

    [Fact]
    public void Custom_rule_without_solver_adapter_is_solved_but_unverified()
    {
        var registry = RuleRegistry.Default.With(new EqualRule());
        var puzzle = ExplainerPuzzles.WithNodes(3).Given(1, 4).Group(EqualRule.Name, [1, 2, 3]);

        var result = new ExplanationEngine(Solver, registry).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.SolvedUnverified, result.Message);
        result.Message.Should().Contain("Ob die Belegung auch die eigenen Regeln erfüllt, ist offen");
        result.Solution.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 4, [2] = 4, [3] = 4 });
    }

    [Fact]
    public void Weak_custom_rule_does_not_yield_a_false_unique_solution()
    {
        // „Alle gerade“ ohne Propagation: Die Logik erreicht 1 = 1, 2 = 2, das verletzt die Regel. Der Solver
        // (hier fest: keine Lösung) widerlegt die Belegung.
        var registry = RuleRegistry.Default.With(new AllEvenRule());
        var puzzle = ExplainerPuzzles.WithNodes(2, 1, 2).Given(1, 1).Given(2, 2).Group(AllEvenRule.Name, [1, 2]);
        var solver = new FixedCountSolver(_ => SolutionCountResult.Completed(0, 2, [], TimeSpan.Zero));

        var result = new ExplanationEngine(solver, registry).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Unsolvable, result.Message);
        result.Message.Should().Contain("vom Solver bewiesen").And.Contain("eine eigene Regel verletzt");
        result.Solution.Should().BeNull();
        result.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void Custom_rule_assigning_an_eliminated_value_is_a_contradiction_before_any_branch()
    {
        // Knoten 2 kann nur 1..3 sein; die Kopierregel verlangt Knoten 2 = Knoten 1 = 4.
        var registry = RuleRegistry.Default.With(new CopyRule());
        var puzzle = ExplainerPuzzles.WithNodes(2, 1, 4).Given(1, 4).Edge(1, 2, CopyRule.Name);
        puzzle.Nodes[1].Domain = new ValueDomain(1, 3);
        var solver = new CountingSolver(Solver);

        var result = new ExplanationEngine(solver, registry).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Unsolvable, result.Message);
        result.Contradiction.Should().NotBeNull();
        result.Contradiction!.Constraint.Should().Be(ConstraintRef.ForEdge(1));
        result.Contradiction.NodeIds.Should().BeEquivalentTo([1, 2]);
        result.Contradiction.Text.Should().Be("Knoten 2 = Knoten 1. Die 4 ist für Knoten 2 aber nicht mehr möglich.");
        result.Message.Should().StartWith("Das Rätsel ist unlösbar. Widerspruch:");
        result.Solution.Should().BeNull();
        solver.CountCalls.Should().Be(0);
    }

    [Fact]
    public void Equal_values_in_a_group_are_a_contradiction_before_any_step_breaks_the_rule()
    {
        // Knoten 1 und 2 können nur 1 sein, alle drei Knoten sind verschieden.
        var puzzle = ExplainerPuzzles.WithNodes(3, 1, 3).Group(AllDifferentRule.Name, [1, 2, 3]);
        puzzle.Nodes[0].Domain = new ValueDomain(1, 1);
        puzzle.Nodes[1].Domain = new ValueDomain(1, 1);

        var result = new ExplanationEngine(Solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Unsolvable);
        result.Steps.Select(s => s.Description).Should().Equal(
            "Knoten 1 = 1. Knoten 1 hat nur noch den Kandidaten 1.",
            "Knoten 2, 3: streiche 1. Gruppe 1 enthält bereits die 1 (Knoten 1). Alle Werte sind verschieden. Also ist die 1 in den übrigen Knoten der Gruppe nicht mehr möglich.");
        result.Contradiction!.Text.Should().Be("Knoten 2 hat keinen Kandidaten mehr (zuletzt gestrichen in Schritt 2).");
    }

    [Theory]
    [InlineData(NotEqualRule.Name)]
    [InlineData(AllDifferentRule.Name)]
    public void Equal_givens_on_a_not_equal_edge_or_group_are_a_contradiction(string rule)
    {
        var puzzle = ExplainerPuzzles.WithNodes(2).Given(1, 5).Given(2, 5);
        _ = rule == NotEqualRule.Name ? puzzle.Edge(1, 2, rule) : puzzle.Group(rule, [1, 2]);

        var result = new ExplanationEngine(Solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Unsolvable);
        result.Steps.Should().NotContain(s => s.Kind != StepKind.EliminateCandidates);
        if (rule == NotEqualRule.Name)
        {
            result.Steps.Should().ContainSingle().Which.Technique.Should().Be(ExplanationTechnique.FixedValueElimination);
            result.Contradiction!.Text.Should().StartWith("Knoten 2 hat keinen Kandidaten mehr");
        }
        else
        {
            // In der Gruppe greift schon vorher das Schubfachprinzip: zwei Knoten, nur noch ein möglicher Wert.
            result.Steps.Should().BeEmpty();
            result.Contradiction!.Text.Should().Be("Gruppe 1: 2 Knoten brauchen verschiedene Werte, es kommt aber nur noch ein Wert in Frage (5).");
        }
    }

    [Fact]
    public void Cancellation_between_rule_propagations_is_reported_as_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var registry = RuleRegistry.Default.With(new CancellingRule(cts));
        var puzzle = ExplainerPuzzles.WithNodes(3).Edge(1, 2, CancellingRule.Name).Edge(2, 3, NotEqualRule.Name);

        var solver = new CountingSolver(Solver);

        var result = new ExplanationEngine(solver, registry).Explain(puzzle, cts.Token);

        result.Status.Should().Be(ExplanationStatus.Cancelled);
        solver.CountCalls.Should().Be(0, "nach dem Abbruch wird keine Verzweigung mehr vorbereitet");
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

    /// <summary>OR-Tools-Adapter für die Testregel „alle gleich“.</summary>
    private sealed class EqualAdapter : IOrToolsRuleAdapter
    {
        public string TypeName => EqualRule.Name;

        public void AddToModel(OrToolsModelContext context, IConstraint constraint)
        {
            var variables = context.Variables(constraint);
            foreach (var variable in variables.Skip(1))
            {
                context.Model.Add(variables[0] == variable);
            }
        }
    }

    /// <summary>Testregel „alle gerade“, deren Propagation nichts ableitet (korrekt, aber schwach).</summary>
    private sealed class AllEvenRule : RuleBase
    {
        public const string Name = "AllEven";

        public override string TypeName => Name;

        public override string DisplayName => "Alle gerade";

        public override RuleKind Kind => RuleKind.Group;

        protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint) => [];
    }

    /// <summary>Testregel „Kopie“: b = a, setzt b ohne Rücksicht auf dessen Kandidaten, sobald a feststeht.</summary>
    private sealed class CopyRule : RuleBase
    {
        public const string Name = "Copy";

        public override string TypeName => Name;

        public override string DisplayName => "Kopie";

        public override RuleKind Kind => RuleKind.Edge;

        protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint)
        {
            var (from, to) = (constraint.NodeIds[0], constraint.NodeIds[1]);
            return state.GetValue(from) is int value && !state.IsFixed(to)
                ? [CandidateChange.Assign(to, value, MakeReason(constraint, [from, to], $"Knoten {to} = Knoten {from}."))]
                : [];
        }
    }

    /// <summary>Testregel, die beim Propagieren den Abbruch auslöst und nichts ableitet.</summary>
    private sealed class CancellingRule(CancellationTokenSource cts) : RuleBase
    {
        public const string Name = "Cancelling";

        public override string TypeName => Name;

        public override string DisplayName => "Abbrechend";

        public override RuleKind Kind => RuleKind.Edge;

        protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint)
        {
            cts.Cancel();
            return [];
        }
    }
}
