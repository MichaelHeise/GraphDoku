using GraphDoku.Core.Candidates;
using GraphDoku.Core.Rules;
using GraphDoku.Explainer.Tests.Helpers;
using GraphDoku.Solver.OrTools;
using GraphDoku.Solver.Tests.Helpers;

namespace GraphDoku.Explainer.Tests;

/// <summary>Akzeptanz Paket 6: leichtes Sudoku ohne Verzweigung; jeder Schritt ist mit der Endlösung konsistent.</summary>
public class ExplanationAcceptanceTests
{
    private static readonly OrToolsSolver Solver = new();

    public static TheoryData<string> ConsistencyPuzzles()
    {
        var data = new TheoryData<string>
        {
            "klassisch-leicht.json",
            "klassisch-mittel.json",
            "klassisch-schwer.json",
        };
        foreach (var (rule, _) in ExplainerPuzzles.RulePuzzles())
        {
            data.Add(rule);
        }

        return data;
    }

    public static TheoryData<string> RulePuzzleNames()
    {
        var data = new TheoryData<string>();
        foreach (var (rule, _) in ExplainerPuzzles.RulePuzzles())
        {
            data.Add(rule);
        }

        return data;
    }

    private static Core.Model.Puzzle Resolve(string name) => name.EndsWith(".json", StringComparison.Ordinal)
        ? ExplainerPuzzles.Load(name)
        : ExplainerPuzzles.RulePuzzles().Single(p => p.Rule == name).Puzzle;

    [Fact]
    public void Easy_classic_sudoku_is_explained_completely_without_branching()
    {
        var solver = new CountingSolver(Solver);
        var result = new ExplanationEngine(solver).Explain(ExplainerPuzzles.Load("klassisch-leicht.json"));

        result.Status.Should().Be(ExplanationStatus.Solved, result.Message);
        result.UsedBranching.Should().BeFalse();
        result.Steps.Should().NotContain(s => s.Kind == StepKind.Branch || s.Technique == ExplanationTechnique.Branch);
        result.IsComplete.Should().BeTrue();
        ExplainerPuzzles.ToGridString(result.Solution!).Should().Be(ExplainerPuzzles.EasySolution);
        solver.CountCalls.Should().Be(0, "ohne Verzweigung beweist die Logik selbst die Eindeutigkeit");
        result.Message.Should().Contain("ohne zu raten");
        result.Steps.Select(s => s.Technique).Should().Contain(
            [ExplanationTechnique.NakedSingle, ExplanationTechnique.HiddenSingle, ExplanationTechnique.RulePropagation]);
        result.Steps.Count(s => s.Kind == StepKind.SetValue).Should().Be(81 - 30, "jeder leere Knoten wird genau einmal gesetzt");
    }

    [Theory]
    [MemberData(nameof(ConsistencyPuzzles))]
    public void Every_step_is_consistent_with_the_final_solution(string name)
    {
        var puzzle = Resolve(name);
        var count = Solver.CountSolutions(puzzle, 2);
        count.IsUnique.Should().BeTrue($"das Testrätsel {name} muss eindeutig sein ({count.Message})");
        var solution = count.Solutions[0];
        SolutionVerifier.Violations(puzzle, solution).Should().BeEmpty();

        var result = new ExplanationEngine(Solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Solved, result.Message);
        result.Solution.Should().BeEquivalentTo(solution);
        AssertConsistent(result, solution);
    }

    [Theory]
    [MemberData(nameof(RulePuzzleNames))]
    public void Rule_puzzles_are_explained_with_their_rule_and_without_branching(string name)
    {
        var puzzle = Resolve(name);
        var rule = name.Split('/')[0];

        var result = new ExplanationEngine(Solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Solved, result.Message);
        result.UsedBranching.Should().BeFalse();
        result.Steps.Should().Contain(
            s => s.Constraint != null && puzzle.FindConstraint(s.Constraint.Value)!.Rule == rule,
            $"die Regel {rule} muss mindestens einen Schritt begründen");
    }

    [Fact]
    public void Hard_classic_sudoku_needs_branching_and_takes_values_from_the_solver()
    {
        var puzzle = ExplainerPuzzles.Load("klassisch-schwer.json");
        var solver = new CountingSolver(Solver);

        var result = new ExplanationEngine(solver).Explain(puzzle);

        result.Status.Should().Be(ExplanationStatus.Solved, result.Message);
        result.UsedBranching.Should().BeTrue();
        solver.CountCalls.Should().Be(1, "der Solver wird höchstens einmal befragt");
        result.SolverSolution.Should().NotBeNull();
        result.Solution.Should().BeEquivalentTo(result.SolverSolution);
        result.Message.Should().Contain("Verzweigung");
        foreach (var branch in result.Steps.Where(s => s.Kind == StepKind.Branch))
        {
            branch.Technique.Should().Be(ExplanationTechnique.Branch);
            branch.Constraint.Should().BeNull();
            branch.Value.Should().Be(result.SolverSolution![branch.NodeId!.Value]);
            branch.Summary.Should().StartWith("Verzweigung: Knoten");
            branch.Text.Should().Contain("Ohne Raten geht es hier nicht weiter").And.Contain("aus der Lösung des Solvers");
        }
    }

    [Theory]
    [InlineData("klassisch-mittel.json")]
    [InlineData("klassisch-schwer.json")]
    public void Branch_is_taken_only_when_stuck_and_picks_fewest_candidates_then_lowest_id(string file)
    {
        var puzzle = ExplainerPuzzles.Load(file);
        var result = new ExplanationEngine(Solver).Explain(puzzle);
        var branches = result.Steps.Where(s => s.Kind == StepKind.Branch).ToList();
        branches.Should().NotBeEmpty();

        foreach (var branch in branches)
        {
            var before = result.GetSnapshot(branch.Index);
            var unset = before.Candidates.NodeIds.Where(id => !before.IsSet(id)).ToList();
            var fewest = unset.Min(id => before.Candidates.GetCandidates(id).Count);
            branch.NodeId.Should().Be(unset.Where(id => before.Candidates.GetCandidates(id).Count == fewest).Min());

            // Festgefahren: kein Knoten mit nur einem Kandidaten, keine Regel streicht noch etwas.
            unset.Should().OnlyContain(id => before.Candidates.GetCandidates(id).Count > 1);
            foreach (var constraint in puzzle.Constraints)
            {
                RuleRegistry.Default.Get(constraint.Rule).Propagate(before.Candidates, constraint)
                    .Should().BeEmpty($"vor der Verzweigung in Schritt {branch.Index + 1} darf {constraint.Ref} nichts mehr streichen");
            }
        }
    }

    [Fact]
    public void Techniques_are_applied_from_simplest_to_hardest()
    {
        var result = new ExplanationEngine(Solver).Explain(ExplainerPuzzles.Load("klassisch-leicht.json"));

        foreach (var step in result.Steps.Where(s => s.Technique != ExplanationTechnique.NakedSingle))
        {
            var before = result.GetSnapshot(step.Index);
            before.Candidates.NodeIds
                .Where(id => !before.IsSet(id))
                .Should().OnlyContain(
                    id => before.Candidates.GetCandidates(id).Count > 1,
                    $"vor Schritt {step.Index + 1} ({step.Technique}) darf es keinen einzigen Kandidaten geben");
        }
    }

    [Fact]
    public void Explanation_is_deterministic()
    {
        var puzzle = ExplainerPuzzles.Load("klassisch-mittel.json");
        var engine = new ExplanationEngine(Solver);

        var first = engine.Explain(puzzle).Steps.Select(s => s.ToString()).ToList();
        var second = engine.Explain(puzzle).Steps.Select(s => s.ToString()).ToList();

        second.Should().Equal(first);
    }

    /// <summary>Gesetzte Werte = Lösung, keine gestrichene Zahl ist der Lösungswert, jeder Zustand enthält die Lösung.</summary>
    internal static void AssertConsistent(ExplanationResult result, IReadOnlyDictionary<int, int> solution)
    {
        for (var i = 0; i < result.Steps.Count; i++)
        {
            var step = result.Steps[i];
            step.Index.Should().Be(i);
            step.Text.Should().NotBeNullOrWhiteSpace();
            step.InvolvedNodeIds.Should().NotBeEmpty();
            step.NodeIds.Should().OnlyHaveUniqueItems();
            if (step.Kind == StepKind.EliminateCandidates)
            {
                foreach (var change in step.Changes)
                {
                    change.Kind.Should().Be(CandidateChangeKind.Eliminate);
                    change.Values.Should().NotContain(solution[change.NodeId], $"Schritt {i + 1} ({step.Description}) darf den Lösungswert nicht streichen");
                }
            }
            else
            {
                step.Value.Should().Be(solution[step.NodeId!.Value], $"Schritt {i + 1} ({step.Description}) muss den Lösungswert setzen");
            }

            if (step.Technique is ExplanationTechnique.NakedSingle or ExplanationTechnique.Branch)
            {
                step.Constraint.Should().BeNull();
            }
            else
            {
                step.Constraint.Should().NotBeNull();
                result.Puzzle.FindConstraint(step.Constraint!.Value).Should().NotBeNull();
            }
        }

        for (var i = 0; i <= result.Steps.Count; i++)
        {
            var snapshot = result.GetSnapshot(i);
            foreach (var (nodeId, value) in solution)
            {
                snapshot.Candidates.GetCandidates(nodeId).Should().Contain(value, $"nach {i} Schritten muss Knoten {nodeId} den Lösungswert behalten");
            }
        }

        var final = result.GetFinalSnapshot();
        final.SetNodeIds.Should().HaveCount(solution.Count);
        solution.Should().OnlyContain(kv => final.GetSetValue(kv.Key) == kv.Value);
    }
}
