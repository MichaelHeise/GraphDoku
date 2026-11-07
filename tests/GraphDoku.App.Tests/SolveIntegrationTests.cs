using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;
using GraphDoku.Solver;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests;

/// <summary>
/// Akzeptanz Paket 4 mit dem echten <see cref="OrToolsSolver"/>: Die Beispielrätsel aus <c>puzzles/</c> lassen sich in der
/// App lösen; unlösbare, mehrdeutige und ungültige Rätsel werden klar gemeldet.
/// </summary>
public sealed class SolveIntegrationTests
{
    // Wie puzzles/klassisch-leicht.json bzw. die Abwandlungen aus den Solver-Tests (Paket 2).
    private const string Easy = "53..7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79";
    private const string Unsolvable = "13..7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79";
    private const string Ambiguous = "53..7....6..195....98......8...6...34..8.3..17...2...6.6....28....419..5....8..79";

    private readonly OrToolsSolver _solver = new();

    private MainWindowViewModel CreateWindow(Puzzle? puzzle = null)
    {
        var window = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), _solver);
        window.Editor.SetViewSize(1000, 800);
        if (puzzle is not null)
        {
            window.Editor.LoadPuzzle(puzzle);
        }

        return window;
    }

    public static TheoryData<string> ExamplePuzzles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(MainWindowViewModelTests.FindRepositoryRoot(), "puzzles"), "*.json").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Fact]
    public void There_are_example_puzzles()
    {
        ExamplePuzzles().Count.Should().BeGreaterThanOrEqualTo(4);
    }

    [Theory]
    [MemberData(nameof(ExamplePuzzles))]
    public async Task Every_example_puzzle_can_be_solved_in_the_app(string fileName)
    {
        var window = CreateWindow();
        (await window.OpenFileAsync(Path.Combine(MainWindowViewModelTests.FindRepositoryRoot(), "puzzles", fileName))).Should().BeTrue();
        var puzzle = window.Editor.Puzzle;
        var before = puzzle.Clone();

        await window.Solve.SolveCommand.ExecuteAsync(null);

        window.Solve.Outcome.Should().Be(SolveOutcome.Solved, window.Solve.ResultMessage);
        var values = window.Editor.SolutionValues;
        values.Should().NotBeNull();
        values!.Keys.Should().BeEquivalentTo(puzzle.Nodes.Select(n => n.Id), "jeder Knoten bekommt einen Wert");
        foreach (var node in puzzle.Nodes)
        {
            puzzle.GetEffectiveDomain(node).Contains(values[node.Id]).Should().BeTrue();
            if (node.Given is int given)
            {
                values[node.Id].Should().Be(given);
            }
        }

        // Unabhängige Prüfung der AllDifferent-Gruppen (alle Beispielrätsel bestehen bisher nur daraus) …
        foreach (var group in puzzle.Groups.Where(g => g.Rule == "AllDifferent"))
        {
            group.Nodes.Select(id => values[id]).Should().OnlyHaveUniqueItems($"Gruppe {group.Id}");
        }

        // … und für alle Regeln: Mit der Lösung als Givens muss das Rätsel lösbar bleiben.
        var check = puzzle.Clone();
        foreach (var node in check.Nodes)
        {
            node.Given = values[node.Id];
        }

        _solver.Solve(check).Status.Should().Be(SolveStatus.Solved);

        // Die Lösung ist nur Anzeige: Rätsel unverändert, nicht „ungespeichert“.
        puzzle.ShouldEqual(before);
        window.Editor.IsDirty.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(ExamplePuzzles))]
    public async Task Every_example_puzzle_can_be_checked_for_uniqueness(string fileName)
    {
        var window = CreateWindow();
        (await window.OpenFileAsync(Path.Combine(MainWindowViewModelTests.FindRepositoryRoot(), "puzzles", fileName))).Should().BeTrue();

        await window.Solve.CheckUniquenessCommand.ExecuteAsync(null);

        // Das leere Gitter ist absichtlich mehrdeutig; alle anderen Beispielrätsel sind eindeutig (Paket 2).
        var expected = fileName == "klassisch-leer.json" ? SolveOutcome.Ambiguous : SolveOutcome.Unique;
        window.Solve.Outcome.Should().Be(expected, window.Solve.ResultMessage);
    }

    [Fact]
    public async Task Unsolvable_puzzle_is_reported_clearly()
    {
        var window = CreateWindow(ClassicSudoku.Create(Unsolvable));

        await window.Solve.SolveCommand.ExecuteAsync(null);
        window.Solve.Outcome.Should().Be(SolveOutcome.Unsolvable);
        window.Solve.ResultMessage.Should().Be("Das Rätsel ist unlösbar: Es gibt keine Belegung, die alle Regeln erfüllt.");
        window.Editor.HasSolution.Should().BeFalse();

        await window.Solve.CheckUniquenessCommand.ExecuteAsync(null);
        window.Solve.Outcome.Should().Be(SolveOutcome.Unsolvable);
        window.Solve.ResultMessage.Should().Contain("unlösbar");
    }

    [Fact]
    public async Task Ambiguous_puzzle_shows_two_different_solutions()
    {
        var window = CreateWindow(ClassicSudoku.Create(Ambiguous));
        var solve = window.Solve;
        var editor = window.Editor;

        await solve.CheckUniquenessCommand.ExecuteAsync(null);

        solve.Outcome.Should().Be(SolveOutcome.Ambiguous);
        solve.ResultMessage.Should().Contain("mehrdeutig").And.Contain("mindestens zwei");
        solve.AvailableSolutionCount.Should().Be(2);
        solve.SolutionDifferences.Should().NotBeEmpty();

        solve.ShowFirstSolutionCommand.Execute(null);
        var first = editor.SolutionValues!.ToDictionary();
        solve.ShowSecondSolutionCommand.Execute(null);
        var second = editor.SolutionValues!.ToDictionary();

        first.Should().NotEqual(second);
        first.Where(p => second[p.Key] != p.Value).Select(p => p.Key).Should().BeEquivalentTo(solve.SolutionDifferences);
        solve.SolutionDifferences.Should().OnlyContain(id => editor.IsSolutionDifference(id));
        solve.SolutionDifferences.Should().OnlyContain(id => editor.Puzzle.FindNode(id)!.Given == null);

        // „Lösen“ findet auch bei einem mehrdeutigen Rätsel eine Lösung.
        await solve.SolveCommand.ExecuteAsync(null);
        solve.Outcome.Should().Be(SolveOutcome.Solved);
        editor.SolutionDifferences.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_puzzle_is_reported_with_its_problems()
    {
        var puzzle = ClassicSudoku.Create(Easy);
        puzzle.Groups.Add(new Group(28, [1, 2, 3], "Sum")); // Pflichtparameter „target“ fehlt
        var window = CreateWindow(puzzle);

        await window.Solve.SolveCommand.ExecuteAsync(null);

        window.Solve.Outcome.Should().Be(SolveOutcome.Invalid);
        window.Solve.ResultMessage.Should().Contain("ungültig");
        window.Solve.ResultDetails.Should().ContainSingle().Which.Should().Contain("Gruppe 28").And.Contain("target");
        window.Editor.HasSolution.Should().BeFalse();
    }

    [Fact]
    public async Task Solution_from_real_solver_becomes_stale_after_edit()
    {
        var window = CreateWindow(ClassicSudoku.Create(Easy));
        await window.Solve.SolveCommand.ExecuteAsync(null);
        window.Editor.HasSolution.Should().BeTrue();

        window.Editor.SelectNodes([ClassicSudoku.NodeId(0, 2)]);
        window.Editor.Type("6");

        window.Editor.HasSolution.Should().BeFalse();
        window.Solve.Outcome.Should().Be(SolveOutcome.Outdated);

        // Neu lösen ergibt die Lösung für den neuen Stand (dort steht in der eindeutigen Lösung eine 4, mit der 6 ist es unlösbar).
        await window.Solve.SolveCommand.ExecuteAsync(null);
        window.Solve.Outcome.Should().Be(SolveOutcome.Unsolvable);
    }
}
