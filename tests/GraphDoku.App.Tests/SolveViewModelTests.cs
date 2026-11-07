using GraphDoku.App.Editing;
using GraphDoku.App.Solving;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Serialization;
using GraphDoku.Core.Templates;
using GraphDoku.Solver;

namespace GraphDoku.App.Tests;

/// <summary>Paket 4: Lösen in der App mit einem Solver-Ersatz (Busy-Zustand, Abbruch, Meldungen, veraltete Lösungen).</summary>
public sealed class SolveViewModelTests : IDisposable
{
    private const string Givens =
        "53..7...." + "6..195..." + ".98....6." + "8...6...3" + "4..8.3..1" + "7...2...6" + ".6....28." + "...419..5" + "....8..79";

    private readonly FakeSolver _solver = new();
    private readonly TempDirectory _temp = new();
    private readonly MainWindowViewModel _window;

    public SolveViewModelTests()
    {
        _window = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), _solver);
        _window.Editor.SetViewSize(1000, 800);
        _window.Editor.LoadPuzzle(ClassicSudoku.Create(Givens));
    }

    private SolveViewModel Solve => _window.Solve;

    private EditorViewModel Editor => _window.Editor;

    public void Dispose()
    {
        _solver.Release();
        _solver.Dispose();
        _temp.Dispose();
    }

    /// <summary>Eine inhaltliche Bearbeitung: Given in einen leeren Knoten eintragen.</summary>
    private void EditPuzzle()
    {
        Editor.SelectNodes([ClassicSudoku.NodeId(0, 2)]);
        Editor.Type("4");
        Editor.Puzzle.FindNode(ClassicSudoku.NodeId(0, 2))!.Given.Should().Be(4);
    }

    [Fact]
    public async Task Solve_shows_solution_as_display_state_without_changing_the_puzzle()
    {
        var before = Editor.Snapshot();

        await Solve.SolveCommand.ExecuteAsync(null);

        Solve.Outcome.Should().Be(SolveOutcome.Solved);
        Solve.Severity.Should().Be(ResultSeverity.Success);
        Solve.ResultMessage.Should().StartWith("Lösung gefunden");
        Solve.IsBusy.Should().BeFalse();
        Editor.HasSolution.Should().BeTrue();
        Editor.SolutionValues.Should().HaveCount(81);
        Editor.SolutionValue(ClassicSudoku.NodeId(0, 2)).Should().Be(1);
        Solve.IsSolutionShown.Should().BeTrue();
        Solve.HasStatus.Should().BeTrue();
        Solve.Legend.Should().Contain("Blau").And.Contain("Givens");

        // Reiner Anzeigezustand: Rätsel, Verlauf und Dirty-Anzeige bleiben unverändert.
        Editor.Puzzle.ShouldEqual(before);
        Editor.Puzzle.Nodes.Count(n => n.Given is not null).Should().Be(30);
        Editor.History.UndoCount.Should().Be(0);
        Editor.IsDirty.Should().BeFalse();
        _window.Title.Should().NotContain("*");

        // Der Solver bekommt eine Kopie, nicht das Editor-Rätsel.
        _solver.ReceivedPuzzles.Should().ContainSingle().Which.Should().NotBeSameAs(Editor.Puzzle);
    }

    [Fact]
    public async Task Hide_and_show_solution_commands()
    {
        Solve.HideSolutionCommand.CanExecute(null).Should().BeFalse();
        Solve.ShowFirstSolutionCommand.CanExecute(null).Should().BeFalse();

        await Solve.SolveCommand.ExecuteAsync(null);
        Solve.HideSolutionCommand.CanExecute(null).Should().BeTrue();

        Solve.HideSolutionCommand.Execute(null);
        Editor.HasSolution.Should().BeFalse();
        Solve.IsSolutionShown.Should().BeFalse();
        Solve.HideSolutionCommand.CanExecute(null).Should().BeFalse();
        Solve.ShowFirstSolutionCommand.CanExecute(null).Should().BeTrue();
        Solve.ShowFirstSolutionText.Should().Be("Lösung anzeigen");

        Solve.ShowFirstSolutionCommand.Execute(null);
        Editor.HasSolution.Should().BeTrue();
        Editor.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Search_runs_in_background_shows_busy_state_and_can_be_cancelled()
    {
        _solver.Block = true;
        Solve.CancelCommand.CanExecute(null).Should().BeFalse();

        var running = Solve.SolveCommand.ExecuteAsync(null);
        _solver.WaitUntilStarted();

        running.IsCompleted.Should().BeFalse("die Suche läuft im Hintergrund");
        Solve.IsBusy.Should().BeTrue();
        Solve.BusyText.Should().Contain("läuft");
        Solve.HasStatus.Should().BeTrue();
        Solve.Severity.Should().Be(ResultSeverity.Info);
        Solve.SolveCommand.CanExecute(null).Should().BeFalse();
        Solve.CheckUniquenessCommand.CanExecute(null).Should().BeFalse();
        Solve.CancelCommand.CanExecute(null).Should().BeTrue();

        // Die Oberfläche bleibt bedienbar: Auswahl und Ansicht ändern sich während der Suche.
        Editor.SelectAll();
        Editor.SelectedNodeIds.Should().HaveCount(81);
        Editor.FitView();

        Solve.CancelCommand.Execute(null);
        Solve.BusyText.Should().Be("Wird abgebrochen …");
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        _solver.WasCancelled.Should().BeTrue();
        Solve.IsBusy.Should().BeFalse();
        Solve.Outcome.Should().Be(SolveOutcome.Cancelled);
        Solve.Severity.Should().Be(ResultSeverity.Warning);
        Solve.ResultMessage.Should().Contain("abgebrochen");
        Editor.HasSolution.Should().BeFalse();
        Solve.CancelCommand.CanExecute(null).Should().BeFalse();
        Solve.SolveCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Uniqueness_check_can_be_cancelled()
    {
        _solver.Block = true;
        var running = Solve.CheckUniquenessCommand.ExecuteAsync(null);
        _solver.WaitUntilStarted();
        Solve.IsBusy.Should().BeTrue();
        Solve.BusyText.Should().Contain("Eindeutigkeit");

        Solve.CancelCommand.Execute(null);
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Solve.Outcome.Should().Be(SolveOutcome.Cancelled);
        Solve.ResultMessage.Should().Contain("abgebrochen");
        Solve.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Time_limit_setting_is_passed_to_the_solver()
    {
        Solve.TimeLimitText.Should().Be("30");
        Solve.TimeLimit.Should().Be(SolverOptions.DefaultTimeLimit);

        Solve.TimeLimitText = "2,5";
        await Solve.SolveCommand.ExecuteAsync(null);
        Solve.TimeLimitText = "0.5";
        await Solve.CheckUniquenessCommand.ExecuteAsync(null);

        _solver.ReceivedOptions.Select(o => o.TimeLimit).Should().Equal(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(0.5));
    }

    [Theory]
    [InlineData("", "Bitte ein Zeitlimit")]
    [InlineData("abc", "keine Zahl")]
    [InlineData("0", "zwischen 0,1 und 3600")]
    [InlineData("-3", "keine Zahl")]
    [InlineData("100000", "zwischen 0,1 und 3600")]
    public void Invalid_time_limit_is_reported_and_blocks_starting(string text, string expected)
    {
        Solve.TimeLimitText = text;

        Solve.TimeLimit.Should().BeNull();
        Solve.TimeLimitError.Should().Contain(expected);
        Solve.HasStatus.Should().BeTrue("die Meldung steht in der Ergebnisleiste");
        Solve.SolveCommand.CanExecute(null).Should().BeFalse();
        Solve.CheckUniquenessCommand.CanExecute(null).Should().BeFalse();

        Solve.TimeLimitText = "10";
        Solve.TimeLimitError.Should().BeNull();
        Solve.SolveCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Editing_the_puzzle_hides_a_displayed_solution()
    {
        await Solve.SolveCommand.ExecuteAsync(null);
        Editor.HasSolution.Should().BeTrue();

        EditPuzzle();

        Editor.HasSolution.Should().BeFalse();
        Editor.SolutionValue(ClassicSudoku.NodeId(0, 3)).Should().BeNull();
        Solve.Outcome.Should().Be(SolveOutcome.Outdated);
        Solve.ResultMessage.Should().Contain("ausgeblendet").And.Contain("geändert");
        Solve.ShowFirstSolutionCommand.CanExecute(null).Should().BeFalse("die alte Lösung passt nicht zum neuen Stand");
        Solve.AvailableSolutionCount.Should().Be(0);
    }

    [Fact]
    public async Task Undo_and_redo_also_invalidate_the_solution()
    {
        EditPuzzle();
        await Solve.SolveCommand.ExecuteAsync(null);
        Editor.HasSolution.Should().BeTrue();

        Editor.UndoCommand.Execute(null);
        Editor.HasSolution.Should().BeFalse();

        await Solve.SolveCommand.ExecuteAsync(null);
        Editor.RedoCommand.Execute(null);
        Editor.HasSolution.Should().BeFalse();
        Solve.Outcome.Should().Be(SolveOutcome.Outdated);
    }

    [Fact]
    public async Task Selection_view_and_saving_keep_the_solution()
    {
        await Solve.SolveCommand.ExecuteAsync(null);
        var path = _temp.File("gespeichert.json");

        Editor.SelectAll();
        Editor.ClearSelection();
        Editor.FitView();
        Editor.SetGroupVisible(1, false);
        (await _window.SaveToFileAsync(path)).Should().BeTrue();

        Editor.HasSolution.Should().BeTrue();
        Solve.Outcome.Should().Be(SolveOutcome.Solved);

        // Die Lösung wird nie in die Datei geschrieben.
        PuzzleSerializer.Load(path).ShouldEqual(ClassicSudoku.Create(Givens));
        var json = await File.ReadAllTextAsync(path);
        json.Should().NotContain("solution");
        json.Split("\"given\"").Length.Should().Be(31, "nur die 30 Givens stehen in der Datei");
    }

    [Fact]
    public async Task Result_of_a_search_is_discarded_when_the_puzzle_changed_meanwhile()
    {
        _solver.Block = true;
        var running = Solve.SolveCommand.ExecuteAsync(null);
        _solver.WaitUntilStarted();

        EditPuzzle();
        _solver.Release();
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        _solver.WasCancelled.Should().BeFalse("eine Bearbeitung bricht die Suche nicht ab");
        Editor.HasSolution.Should().BeFalse();
        Solve.Outcome.Should().Be(SolveOutcome.Outdated);
        Solve.ResultMessage.Should().Contain("während der Suche geändert");
        Solve.ShowFirstSolutionCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Loading_another_puzzle_cancels_the_search_and_clears_results()
    {
        await Solve.SolveCommand.ExecuteAsync(null);
        _solver.Block = true;
        var running = Solve.CheckUniquenessCommand.ExecuteAsync(null);
        _solver.WaitUntilStarted();

        await _window.NewClassicCommand.ExecuteAsync(null);
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        _solver.WasCancelled.Should().BeTrue();
        Editor.HasSolution.Should().BeFalse();
        Solve.Outcome.Should().Be(SolveOutcome.Outdated);
        Solve.ResultMessage.Should().Contain("anderes Rätsel");

        _solver.Block = false;
        await Solve.SolveCommand.ExecuteAsync(null);
        Editor.HasSolution.Should().BeTrue();

        Editor.LoadPuzzle(ClassicSudoku.Create(Givens));
        Editor.HasSolution.Should().BeFalse();
        Solve.Outcome.Should().Be(SolveOutcome.None);
        Solve.ResultMessage.Should().BeNull();
        Solve.HasStatus.Should().BeFalse();
    }

    [Fact]
    public async Task Exception_of_a_search_for_a_replaced_puzzle_is_reported_as_outdated()
    {
        _solver.Block = true;
        _solver.ThrowAfterWait = new InvalidOperationException("Fehler im alten Lauf");
        var running = Solve.SolveCommand.ExecuteAsync(null);
        _solver.WaitUntilStarted();

        Editor.LoadPuzzle(ClassicSudoku.Create());
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Solve.Outcome.Should().Be(SolveOutcome.Outdated, "der Fehler gehört zum alten Rätsel, nicht zum neu geladenen");
        Solve.Severity.Should().Be(ResultSeverity.Warning);
        Solve.ResultMessage.Should().Contain("anderes Rätsel");
    }

    [Fact]
    public async Task Exception_of_a_search_after_an_edit_is_reported_as_outdated()
    {
        _solver.Block = true;
        _solver.ThrowAfterWait = new InvalidOperationException("Fehler im alten Lauf");
        var running = Solve.CheckUniquenessCommand.ExecuteAsync(null);
        _solver.WaitUntilStarted();

        EditPuzzle();
        _solver.Release();
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Solve.Outcome.Should().Be(SolveOutcome.Outdated);
        Solve.ResultMessage.Should().Contain("während der Suche geändert");
    }

    [Fact]
    public async Task Missing_native_library_is_reported_with_a_readable_hint()
    {
        _solver.Throw = new TypeInitializationException("Google.OrTools.Sat.operations_research_satPINVOKE", new DllNotFoundException("google-ortools-native"));

        await Solve.SolveCommand.ExecuteAsync(null);

        Solve.Outcome.Should().Be(SolveOutcome.Failed);
        Solve.ResultMessage.Should().Contain("Rechenbibliothek (OR-Tools) konnte nicht geladen werden");
    }

    [Fact]
    public async Task Unsolvable_puzzle_is_reported_as_error()
    {
        await Solve.SolveCommand.ExecuteAsync(null);
        _solver.SolveResult = _ => SolveResult.Unsolvable(TimeSpan.FromMilliseconds(3));

        await Solve.SolveCommand.ExecuteAsync(null);

        Solve.Outcome.Should().Be(SolveOutcome.Unsolvable);
        Solve.Severity.Should().Be(ResultSeverity.Error);
        Solve.ResultMessage.Should().Contain("unlösbar");
        Editor.HasSolution.Should().BeFalse();
        Solve.ShowFirstSolutionCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Timeout_is_reported_with_hint()
    {
        _solver.SolveResult = _ => SolveResult.Timeout(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        await Solve.SolveCommand.ExecuteAsync(null);

        Solve.Outcome.Should().Be(SolveOutcome.Timeout);
        Solve.Severity.Should().Be(ResultSeverity.Warning);
        Solve.ResultMessage.Should().Contain("Zeitlimit von 2 s").And.Contain("höheres Zeitlimit");
        Editor.HasSolution.Should().BeFalse();
    }

    [Fact]
    public async Task Uniqueness_timeout_with_one_solution_offers_to_show_it()
    {
        _solver.CountResult = (puzzle, limit) => SolutionCountResult.Timeout(1, limit, [FakeSolver.FillAll(puzzle)], TimeSpan.FromSeconds(1));

        await Solve.CheckUniquenessCommand.ExecuteAsync(null);

        Solve.Outcome.Should().Be(SolveOutcome.Timeout);
        Solve.ResultMessage.Should().Contain("Eindeutigkeit ist nicht geklärt").And.Contain("angezeigt werden");
        Solve.ShowFirstSolutionCommand.CanExecute(null).Should().BeTrue();
        Solve.ShowSecondSolutionCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_puzzle_lists_the_problems()
    {
        _solver.SolveResult = _ => SolveResult.Invalid(["Gruppe 3: Unbekannte Regel „Foo“.", "Kante 1: Knoten 99 fehlt."], TimeSpan.Zero);

        await Solve.SolveCommand.ExecuteAsync(null);

        Solve.Outcome.Should().Be(SolveOutcome.Invalid);
        Solve.Severity.Should().Be(ResultSeverity.Error);
        Solve.ResultMessage.Should().Contain("ungültig").And.Contain("2 Probleme");
        Solve.HasResultDetails.Should().BeTrue();
        Solve.ResultDetails.Should().Equal("Gruppe 3: Unbekannte Regel „Foo“.", "Kante 1: Knoten 99 fehlt.");

        // Der nächste Lauf leert die Liste.
        _solver.SolveResult = puzzle => SolveResult.Solved(FakeSolver.FillAll(puzzle), TimeSpan.Zero);
        await Solve.SolveCommand.ExecuteAsync(null);
        Solve.HasResultDetails.Should().BeFalse();
        Solve.ResultDetails.Should().BeEmpty();
    }

    [Fact]
    public async Task Solver_exception_is_reported_instead_of_crashing()
    {
        // Fehlende native Bibliothek hat eine eigene Meldung (Missing_native_library_is_reported_with_a_readable_hint).
        _solver.Throw = new IOException("libortools fehlt");

        await Solve.SolveCommand.ExecuteAsync(null);

        Solve.Outcome.Should().Be(SolveOutcome.Failed);
        Solve.Severity.Should().Be(ResultSeverity.Error);
        Solve.ResultMessage.Should().Contain("Fehler").And.Contain("libortools fehlt");
        Solve.IsBusy.Should().BeFalse();
        Solve.SolveCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Unique_puzzle_offers_to_show_the_solution()
    {
        await Solve.CheckUniquenessCommand.ExecuteAsync(null);

        Solve.Outcome.Should().Be(SolveOutcome.Unique);
        Solve.Severity.Should().Be(ResultSeverity.Success);
        Solve.ResultMessage.Should().Contain("eindeutig lösbar").And.Contain("Lösung anzeigen");
        Editor.HasSolution.Should().BeFalse("die Prüfung trägt nichts ein");
        Solve.ShowFirstSolutionCommand.Execute(null);
        Editor.HasSolution.Should().BeTrue();
        Editor.SolutionDifferences.Should().BeEmpty();
    }

    [Fact]
    public async Task Ambiguous_puzzle_offers_both_solutions_and_marks_differences()
    {
        _solver.CountResult = (puzzle, limit) => SolutionCountResult.Completed(
            2, limit, [FakeSolver.FillAll(puzzle), FakeSolver.FillAll(puzzle, offset: 1)], TimeSpan.FromMilliseconds(40));

        await Solve.CheckUniquenessCommand.ExecuteAsync(null);

        Solve.Outcome.Should().Be(SolveOutcome.Ambiguous);
        Solve.Severity.Should().Be(ResultSeverity.Warning);
        Solve.ResultMessage.Should().Contain("mehrdeutig").And.Contain("mindestens zwei").And.Contain("51 Knoten");
        Solve.AvailableSolutionCount.Should().Be(2);
        Solve.ShowFirstSolutionText.Should().Be("Lösung 1 anzeigen");
        Solve.IsSecondSolutionAvailable.Should().BeTrue();
        Solve.SolutionDifferences.Should().HaveCount(51);

        Solve.ShowSecondSolutionCommand.Execute(null);
        var empty = ClassicSudoku.NodeId(0, 2);
        Editor.SolutionValue(empty).Should().Be(2);
        Editor.IsSolutionDifference(empty).Should().BeTrue();
        Editor.IsSolutionDifference(ClassicSudoku.NodeId(0, 0)).Should().BeFalse("Givens sind in beiden Lösungen gleich");
        Solve.Legend.Should().Contain("Orange");

        Solve.ShowFirstSolutionCommand.Execute(null);
        Editor.SolutionValue(empty).Should().Be(1);
    }

    [Fact]
    public async Task Ambiguous_check_after_solve_replaces_the_shown_solution_by_solution_one_with_differences()
    {
        await Solve.SolveCommand.ExecuteAsync(null);
        Editor.HasSolution.Should().BeTrue();
        _solver.CountResult = (puzzle, limit) => SolutionCountResult.Completed(
            2, limit, [FakeSolver.FillAll(puzzle, offset: 1), FakeSolver.FillAll(puzzle, offset: 2)], TimeSpan.FromMilliseconds(40));

        await Solve.CheckUniquenessCommand.ExecuteAsync(null);

        Solve.Outcome.Should().Be(SolveOutcome.Ambiguous);
        var empty = ClassicSudoku.NodeId(0, 2);
        Editor.SolutionValue(empty).Should().Be(2, "die Lösung aus „Lösen“ wurde durch Lösung 1 ersetzt");
        Editor.IsSolutionDifference(empty).Should().BeTrue();
        Solve.Legend.Should().Contain("Orange");
        Solve.ResultMessage.Should().Contain("mehrdeutig").And.Contain("51 Knoten").And.Contain("nur eine davon");
    }

    [Fact]
    public async Task Ambiguous_check_without_shown_solution_still_enters_nothing()
    {
        _solver.CountResult = (puzzle, limit) => SolutionCountResult.Completed(
            2, limit, [FakeSolver.FillAll(puzzle), FakeSolver.FillAll(puzzle, offset: 1)], TimeSpan.FromMilliseconds(40));

        await Solve.CheckUniquenessCommand.ExecuteAsync(null);

        Editor.HasSolution.Should().BeFalse();
        Solve.ResultMessage.Should().Contain("Beim Anzeigen werden diese Knoten orange markiert");
    }

    [Fact]
    public async Task Ambiguous_message_uses_singular_for_one_differing_node()
    {
        var differing = ClassicSudoku.NodeId(0, 2);
        _solver.CountResult = (puzzle, limit) =>
        {
            var first = FakeSolver.FillAll(puzzle);
            var second = new Dictionary<int, int>(first) { [differing] = first[differing] + 1 };
            return SolutionCountResult.Completed(2, limit, [first, second], TimeSpan.FromMilliseconds(5));
        };

        await Solve.CheckUniquenessCommand.ExecuteAsync(null);
        Solve.SolutionDifferences.Should().Equal(differing);
        Solve.ResultMessage.Should().Contain("verschieden in einem Knoten").And.Contain("wird dieser Knoten orange markiert")
            .And.NotContain("diese Knoten");

        Solve.ShowFirstSolutionCommand.Execute(null);
        await Solve.CheckUniquenessCommand.ExecuteAsync(null);
        Solve.ResultMessage.Should().Contain("der abweichende Knoten ist orange markiert");
    }

    [Fact]
    public void Constructor_requires_editor_and_solver()
    {
        var act = () => new SolveViewModel(Editor, null!);
        act.Should().Throw<ArgumentNullException>();
        var act2 = () => new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), null!);
        act2.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("30", 30.0)]
    [InlineData(" 2,5 ", 2.5)]
    [InlineData("0.1", 0.1)]
    [InlineData("3600", 3600.0)]
    public void Time_limit_text_is_parsed(string text, double seconds)
    {
        SolverTimeText.TryParseSeconds(text, out var limit, out var error).Should().BeTrue();
        limit.Should().Be(TimeSpan.FromSeconds(seconds));
        error.Should().BeNull();
    }

    [Fact]
    public void Durations_are_formatted_in_german()
    {
        SolverTimeText.Duration(TimeSpan.FromMilliseconds(12.4)).Should().Be("12 ms");
        SolverTimeText.Duration(TimeSpan.FromSeconds(2.44)).Should().Be("2,4 s");
        SolverTimeText.Duration(TimeSpan.FromSeconds(30)).Should().Be("30 s");
        SolverTimeText.Duration(TimeSpan.Zero).Should().Be("0 ms");
        SolverTimeText.FormatSeconds(TimeSpan.FromSeconds(1.5)).Should().Be("1,5");
        SolverTimeText.FormatSeconds(SolverOptions.DefaultTimeLimit).Should().Be("30");
    }
}

/// <summary>Einpassen bleibt beim Ändern der Zeichenflächengröße erhalten (z. B. wenn die Lösen-Leiste wächst).</summary>
public sealed class ViewRefitTests
{
    [Fact]
    public void Fitted_view_is_refitted_when_the_canvas_size_changes()
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());
        editor.IsViewFitted.Should().BeTrue();
        var scale = editor.Viewport.Scale;

        editor.SetViewSize(1000, 600);

        editor.IsViewFitted.Should().BeTrue();
        editor.Viewport.Scale.Should().BeLessThan(scale);
        editor.Viewport.VisibleWorld.Bottom.Should().BeGreaterThan(8.5, "die unterste Zeile bleibt sichtbar");
    }

    [Fact]
    public void Zoomed_or_panned_view_is_kept_when_the_canvas_size_changes()
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());
        editor.ZoomInCommand.Execute(null);
        editor.IsViewFitted.Should().BeFalse();
        var (scale, x, y) = (editor.Viewport.Scale, editor.Viewport.OffsetX, editor.Viewport.OffsetY);

        editor.SetViewSize(1000, 600);

        (editor.Viewport.Scale, editor.Viewport.OffsetX, editor.Viewport.OffsetY).Should().Be((scale, x, y));
    }

    [Fact]
    public void Refit_waits_until_the_gesture_ends_so_nodes_land_under_the_pointer()
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());
        editor.Tool = EditorTool.PlaceNode;
        var start = editor.ScreenOf(new GridPoint(9, 4));
        var end = editor.ScreenOf(new GridPoint(11, 4));
        var view = (editor.Viewport.Scale, editor.Viewport.OffsetX, editor.Viewport.OffsetY);

        editor.PointerPressed(start, PointerButton.Left);
        editor.SetViewSize(1000, 600); // z. B. weil die Lösen-Leiste nach der ersten Bearbeitung anders aussieht
        (editor.Viewport.Scale, editor.Viewport.OffsetX, editor.Viewport.OffsetY).Should().Be(view, "während der Geste springt die Ansicht nicht");
        editor.PointerMoved(end);
        editor.PointerReleased(end);

        editor.Puzzle.Nodes.Select(n => (n.X, n.Y)).Should().Contain([(9, 4), (10, 4), (11, 4)]);
        editor.IsViewFitted.Should().BeTrue();
        editor.Viewport.Scale.Should().BeLessThan(view.Scale, "nach der Geste wird das zurückgestellte Einpassen nachgeholt");
    }

    [Fact]
    public void Deferred_refit_also_runs_when_the_gesture_is_cancelled()
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());
        var scale = editor.Viewport.Scale;
        editor.PointerPressed(editor.ScreenOf(-0.4, -0.4), PointerButton.Left); // Auswahlrahmen beginnt
        editor.SetViewSize(1000, 600);
        editor.Viewport.Scale.Should().Be(scale);

        editor.Escape();

        editor.Viewport.Scale.Should().BeLessThan(scale);
        editor.IsViewFitted.Should().BeTrue();
    }

    [Fact]
    public void Deferred_refit_is_dropped_when_the_gesture_panned_the_view()
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());
        var start = new ScreenPoint(500, 400);
        editor.PointerPressed(start, PointerButton.Middle);
        editor.SetViewSize(1000, 600);
        editor.PointerMoved(new ScreenPoint(540, 420));
        editor.PointerReleased(new ScreenPoint(540, 420), PointerButton.Middle);
        var (scale, x, y) = (editor.Viewport.Scale, editor.Viewport.OffsetX, editor.Viewport.OffsetY);

        editor.IsViewFitted.Should().BeFalse("der Nutzer hat die Ansicht verschoben");
        editor.SetViewSize(1000, 500);
        (editor.Viewport.Scale, editor.Viewport.OffsetX, editor.Viewport.OffsetY).Should().Be((scale, x, y));
    }
}
