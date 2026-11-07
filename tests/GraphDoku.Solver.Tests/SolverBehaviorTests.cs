using System.Diagnostics;
using Google.OrTools.Sat;
using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;
using GraphDoku.Core.Serialization;
using GraphDoku.Solver.OrTools;
using GraphDoku.Solver.Tests.Helpers;
using static GraphDoku.Solver.Tests.Helpers.TestPuzzles;

namespace GraphDoku.Solver.Tests;

/// <summary>Zeitlimit, Abbruch, Wertebereiche, ungültige Rätsel und Erweiterbarkeit des OR-Tools-Solvers.</summary>
public class SolverBehaviorTests
{
    private static readonly SolverOptions LongRunning = new() { TimeLimit = TimeSpan.FromSeconds(60) };

    private readonly OrToolsSolver _solver = new();

    [Fact]
    public void Solver_implements_ISolver_with_a_name()
    {
        ISolver solver = _solver;
        solver.Name.Should().Be("OR-Tools CP-SAT");
    }

    // Zeitlimit

    [Fact]
    public void Solve_reports_timeout_when_the_time_limit_is_reached()
    {
        var puzzle = MycielskiColoring(6);
        var stopwatch = Stopwatch.StartNew();

        var result = _solver.Solve(puzzle, new SolverOptions { TimeLimit = TimeSpan.FromMilliseconds(300) });

        result.Status.Should().Be(SolveStatus.Timeout, result.Message);
        result.Values.Should().BeNull();
        result.Message.Should().Contain("Zeitlimit von 0.3 s");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void CountSolutions_reports_timeout_with_the_solutions_found_so_far()
    {
        var puzzle = TestPuzzles.Load("klassisch-leer.json");

        var result = _solver.CountSolutions(puzzle, int.MaxValue, new SolverOptions { TimeLimit = TimeSpan.FromMilliseconds(300) });

        result.Status.Should().Be(CountStatus.Timeout);
        result.Count.Should().BeGreaterThanOrEqualTo(2);
        result.IsAmbiguous.Should().BeTrue("zwei verschiedene Lösungen beweisen die Mehrdeutigkeit auch ohne vollständige Suche");
        result.IsUnique.Should().BeFalse();
        result.Solutions.Should().HaveCount(SolutionCountResult.MaxStoredSolutions);
        result.Solutions.Should().OnlyContain(s => SolutionVerifier.IsSolution(puzzle, s));
        result.Message.Should().Contain("mehrdeutig").And.Contain("Zeitlimit");
    }

    [Fact]
    public void CountSolutions_timeout_without_two_solutions_leaves_uniqueness_open()
    {
        var result = _solver.CountSolutions(MycielskiColoring(6), 2, new SolverOptions { TimeLimit = TimeSpan.FromMilliseconds(300) });

        result.Status.Should().Be(CountStatus.Timeout);
        result.Count.Should().Be(0);
        result.IsUnsolvable.Should().BeFalse();
        result.IsUnique.Should().BeFalse();
        result.Message.Should().Contain("Eindeutigkeit ist nicht geklärt");
    }

    [Fact]
    public void Default_time_limit_is_thirty_seconds()
    {
        SolverOptions.Default.TimeLimit.Should().Be(TimeSpan.FromSeconds(30));
        new SolverOptions().TimeLimit.Should().Be(SolverOptions.DefaultTimeLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Time_limit_must_be_positive(int milliseconds)
    {
        var act = () => new SolverOptions { TimeLimit = TimeSpan.FromMilliseconds(milliseconds) };

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Zeitlimit*");
    }

    // Abbruch

    [Fact]
    public void Solve_can_be_cancelled_while_running()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();

        var result = _solver.Solve(MycielskiColoring(6), LongRunning, cancellation.Token);

        result.Status.Should().Be(SolveStatus.Cancelled, result.Message);
        result.Message.Should().Be("Die Suche wurde abgebrochen.");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15), "der Abbruch muss die CP-SAT-Suche anhalten");
    }

    [Fact]
    public void CountSolutions_can_be_cancelled_while_running()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();

        var result = _solver.CountSolutions(TestPuzzles.Load("klassisch-leer.json"), int.MaxValue, LongRunning, cancellation.Token);

        result.Status.Should().Be(CountStatus.Cancelled);
        result.Count.Should().BeGreaterThan(0);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Already_cancelled_token_returns_cancelled_without_solving()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var puzzle = TestPuzzles.Load("klassisch-leicht.json");

        _solver.Solve(puzzle, cancellationToken: cancellation.Token).Status.Should().Be(SolveStatus.Cancelled);
        _solver.CountSolutions(puzzle, cancellationToken: cancellation.Token).Status.Should().Be(CountStatus.Cancelled);
    }

    [Fact]
    public async Task SolveAsync_runs_in_the_background_and_returns_the_solution()
    {
        var puzzle = TestPuzzles.Load("klassisch-leicht.json");
        ISolver solver = _solver;

        var result = await solver.SolveAsync(puzzle);

        result.Status.Should().Be(SolveStatus.Solved);
        SolutionVerifier.Violations(puzzle, result.Values!).Should().BeEmpty();
    }

    [Fact]
    public async Task CountSolutionsAsync_checks_uniqueness()
    {
        ISolver solver = _solver;

        var result = await solver.CountSolutionsAsync(TestPuzzles.Load("klassisch-schwer.json"));

        result.IsUnique.Should().BeTrue();
    }

    [Fact]
    public async Task Async_cancellation_returns_a_result_instead_of_throwing()
    {
        using var cancellation = new CancellationTokenSource();
        ISolver solver = _solver;
        var running = solver.SolveAsync(MycielskiColoring(6), LongRunning, cancellation.Token);
        await Task.Delay(200);
        await cancellation.CancelAsync();

        var result = await running.WaitAsync(TimeSpan.FromSeconds(15));

        result.Status.Should().Be(SolveStatus.Cancelled);
    }

    [Fact]
    public async Task Async_solving_works_on_a_snapshot_of_the_puzzle()
    {
        var puzzle = WithGroup("AllDifferent", 2, min: 1, max: 2).Given(1, 1);
        ISolver solver = _solver;

        var running = solver.SolveAsync(puzzle);
        puzzle.FindNode(1)!.Given = 2;
        var result = await running;

        result.Values![1].Should().Be(1);
    }

    [Fact]
    public async Task CountSolutionsAsync_rejects_invalid_limit_immediately()
    {
        ISolver solver = _solver;

        var act = () => solver.CountSolutionsAsync(WithNodes(1), 0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void CountSolutions_limit_must_be_at_least_one(int limit)
    {
        var act = () => _solver.CountSolutions(WithNodes(1), limit);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CountSolutions_with_limit_one_does_not_claim_uniqueness()
    {
        var result = _solver.CountSolutions(TestPuzzles.Load("klassisch-leicht.json"), limit: 1);

        result.Status.Should().Be(CountStatus.Completed);
        result.Count.Should().Be(1);
        result.LimitReached.Should().BeTrue();
        result.IsUnique.Should().BeFalse();
        result.Message.Should().Be("Es gibt mindestens 1 Lösung (Limit 1 erreicht).");
    }

    // Wertebereiche und Givens

    [Fact]
    public void Node_domain_override_restricts_the_value()
    {
        var puzzle = WithGroup("AllDifferent", 2, min: 1, max: 2).Domain(1, 2, 2);

        var values = _solver.Solve(puzzle).Values!;

        values[1].Should().Be(2);
        values[2].Should().Be(1);
        _solver.CountSolutions(puzzle).IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Node_domain_override_may_lie_outside_the_global_domain()
    {
        var puzzle = WithNodes(2, 1, 3).Domain(2, 10, 12);
        puzzle.Edges.Add(new Edge(1, 1, 2, "Difference", Params(("k", 9))));

        var count = _solver.CountSolutions(puzzle, limit: 100);
        var values = _solver.Solve(puzzle).Values!;

        count.Count.Should().Be(SolutionVerifier.BruteForceCount(puzzle)).And.Be(3);
        values[2].Should().BeInRange(10, 12);
        (values[2] - values[1]).Should().Be(9);
    }

    [Fact]
    public void Given_inside_its_own_domain_but_outside_the_global_domain_is_valid()
    {
        var puzzle = WithNodes(1, 1, 9).Domain(1, 20, 30).Given(1, 25);

        var result = _solver.Solve(puzzle);

        result.Status.Should().Be(SolveStatus.Solved);
        result.Values![1].Should().Be(25);
    }

    [Fact]
    public void Given_outside_the_domain_makes_the_puzzle_invalid()
    {
        var puzzle = WithNodes(2, 1, 4).Given(2, 7);

        var solve = _solver.Solve(puzzle);
        var count = _solver.CountSolutions(puzzle);

        solve.Status.Should().Be(SolveStatus.Invalid);
        solve.Errors.Should().ContainSingle().Which.Should().Contain("Knoten 2").And.Contain("außerhalb des Wertebereichs 1..4");
        solve.Message.Should().StartWith("Das Rätsel ist ungültig: Knoten 2");
        count.Status.Should().Be(CountStatus.Invalid);
        count.Errors.Should().Equal(solve.Errors);
    }

    [Fact]
    public void Given_outside_the_node_domain_override_makes_the_puzzle_invalid()
    {
        var result = _solver.Solve(WithNodes(1, 1, 9).Domain(1, 1, 3).Given(1, 5));

        result.Status.Should().Be(SolveStatus.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Contain("außerhalb des Wertebereichs 1..3");
    }

    [Fact]
    public void Puzzle_without_nodes_is_trivially_solved()
    {
        var result = _solver.Solve(new Puzzle("Leer"));

        result.Status.Should().Be(SolveStatus.Solved);
        result.Values.Should().BeEmpty();
        _solver.CountSolutions(new Puzzle("Leer")).IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Solving_does_not_modify_the_puzzle()
    {
        var puzzle = TestPuzzles.Load("klassisch-mittel.json");
        var before = PuzzleSerializer.Serialize(puzzle);

        _solver.Solve(puzzle);
        _solver.CountSolutions(puzzle);

        PuzzleSerializer.Serialize(puzzle).Should().Be(before);
    }

    // Ungültige Rätsel und Erweiterbarkeit

    [Fact]
    public void Unknown_rule_type_is_reported_with_rule_and_edge()
    {
        var puzzle = WithEdge("Teleport");

        var result = _solver.Solve(puzzle);

        result.Status.Should().Be(SolveStatus.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Kante 1").And.Contain("Unbekannter Regeltyp „Teleport“");
    }

    [Fact]
    public void Invalid_rule_parameters_are_reported()
    {
        var result = _solver.Solve(WithGroup("Sum", 2));

        result.Status.Should().Be(SolveStatus.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Gruppe 1").And.Contain("Pflichtparameter „target“ fehlt");
    }

    [Fact]
    public void Registered_rule_without_adapter_is_reported()
    {
        var rules = RuleRegistry.Default.With(new EvenRule());
        var solver = new OrToolsSolver(rules);
        var puzzle = WithGroup(EvenRule.Name, 2);

        var result = solver.Solve(puzzle);

        result.Status.Should().Be(SolveStatus.Invalid);
        result.Errors.Should().ContainSingle().Which.Should()
            .Be("Gruppe 1: Der Solver kann die Regel „Even“ nicht übersetzen, weil für sie kein OR-Tools-Adapter registriert ist.");
        solver.CountSolutions(puzzle).Status.Should().Be(CountStatus.Invalid);
    }

    [Fact]
    public void New_rules_plug_in_via_both_registries()
    {
        var solver = new OrToolsSolver(RuleRegistry.Default.With(new EvenRule()), OrToolsAdapterRegistry.Default.With(new EvenAdapter()));
        var puzzle = WithGroup(EvenRule.Name, 2, min: 1, max: 5);
        puzzle.Groups.Add(new Group(2, [1, 2], "AllDifferent"));

        var result = solver.CountSolutions(puzzle, limit: 100);

        result.Count.Should().Be(2);
        result.Solutions.SelectMany(s => s.Values).Should().OnlyContain(v => v == 2 || v == 4);
    }

    [Fact]
    public void Default_adapter_registry_covers_every_catalog_rule()
    {
        OrToolsAdapterRegistry.Default.TypeNames.Should().BeEquivalentTo(RuleRegistry.Default.TypeNames);
    }

    [Fact]
    public void Adapter_registry_rejects_duplicates()
    {
        var act = () => OrToolsAdapterRegistry.Default.With(new EvenAdapter()).With(new EvenAdapter());

        act.Should().Throw<ArgumentException>().WithMessage("*„Even“*");
    }

    [Fact]
    public void Model_builder_creates_one_variable_per_node_with_its_domain()
    {
        var puzzle = WithNodes(3, 1, 9).Domain(2, 4, 6).Given(3, 8);

        var build = new OrToolsModelBuilder().Build(puzzle);

        build.Errors.Should().BeEmpty();
        build.Model!.NodeVariables.Select(v => v.NodeId).Should().Equal(1, 2, 3);
        var domains = build.Model.NodeVariables
            .Select(v => build.Model.Model.Model.Variables[v.Variable.GetIndex()].Domain.ToArray())
            .ToList();
        domains[0].Should().Equal(1, 9);
        domains[1].Should().Equal(4, 6);
        domains[2].Should().Equal(8, 8);
    }

    /// <summary>Beispiel für eine neue Regel außerhalb des Katalogs: alle Werte der Gruppe sind gerade.</summary>
    private sealed class EvenRule : IRule
    {
        public const string Name = "Even";

        public string TypeName => Name;

        public string DisplayName => "Gerade";

        public RuleKind Kind => RuleKind.Group;

        public int MinNodes => 1;

        public IReadOnlyList<RuleParameter> Parameters => [];

        public RuleParams CreateDefaultParams() => new();

        public IReadOnlyList<string> Validate(IConstraint constraint, Puzzle puzzle) => [];

        public IReadOnlyList<CandidateChange> Propagate(CandidateState state, IConstraint constraint) => [];
    }

    private sealed class EvenAdapter : IOrToolsRuleAdapter
    {
        public string TypeName => EvenRule.Name;

        public void AddToModel(OrToolsModelContext context, IConstraint constraint)
        {
            foreach (var nodeId in constraint.NodeIds)
            {
                var domain = context.Domain(nodeId);
                var half = context.Model.NewIntVar(domain.Min / 2, (domain.Max / 2) + 1, $"half{nodeId}");
                context.Model.Add(context.Variable(nodeId) == 2 * half);
            }
        }
    }
}
