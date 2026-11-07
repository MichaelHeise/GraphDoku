using System.ComponentModel;
using GraphDoku.App.Explaining;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;
using GraphDoku.Core.Serialization;
using GraphDoku.Core.Templates;
using GraphDoku.Explainer;
using GraphDoku.Solver;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests;

/// <summary>
/// Paket 6, Oberfläche: Erklär-Modus auf ViewModel-Ebene (Navigation, Hervorhebung, Kandidaten, Ungültigwerden nach
/// Bearbeitung, Abbruch, Meldungen). Gerechnet wird mit der echten <see cref="ExplanationEngine"/>, je nach Test mit dem
/// Ersatzsolver oder OR-Tools; für Ausnahmen und blockierende Läufe gibt es einen Ersatz-Erklärer.
/// </summary>
public sealed class ExplainViewModelTests : IDisposable
{
    private const string Easy = "53..7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79";

    /// <summary>„Leicht“ mit einer zweiten 5 in Zeile 1: Die Logik findet einen Widerspruch.</summary>
    internal const string Conflicting = "535.7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79";

    /// <summary>„Leicht“ ohne die 6 in Zeile 3, Spalte 8: genau zwei Lösungen.</summary>
    private const string Ambiguous = "53..7....6..195....98......8...6...34..8.3..17...2...6.6....28....419..5....8..79";

    private readonly FakeSolver _solver = new();

    public void Dispose()
    {
        _solver.Release();
        _solver.Dispose();
    }

    private MainWindowViewModel CreateWindow(Puzzle? puzzle = null, ISolver? solver = null, IPuzzleExplainer? explainer = null)
    {
        var window = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), solver ?? _solver, explainer: explainer);
        window.Editor.SetViewSize(1000, 800);
        window.Editor.LoadPuzzle(puzzle ?? ClassicSudoku.Create(Easy));
        return window;
    }

    private static string PuzzlePath(string fileName) => Path.Combine(MainWindowViewModelTests.FindRepositoryRoot(), "puzzles", fileName);

    private static Puzzle LoadExample(string fileName) => PuzzleSerializer.Load(PuzzlePath(fileName));

    [Fact]
    public async Task Explain_enters_the_mode_at_the_initial_state_without_changing_the_puzzle()
    {
        var window = CreateWindow();
        var explain = window.Explain;
        var before = window.Editor.Puzzle.Clone();
        explain.IsBarVisible.Should().BeFalse();

        await explain.ExplainCommand.ExecuteAsync(null);

        explain.IsBusy.Should().BeFalse();
        explain.IsActive.Should().BeTrue();
        explain.IsBarVisible.Should().BeTrue();
        explain.Outcome.Should().Be(ExplainOutcome.Solved);
        explain.Severity.Should().Be(ResultSeverity.Success);
        explain.BarSeverity.Should().Be(ResultSeverity.None, "im aktiven Modus ist die Leiste neutral, das Ergebnis steht im Panel");
        explain.StatusMessage.Should().Contain("ohne zu raten");
        explain.StepCount.Should().Be(explain.Result!.Steps.Count).And.BeGreaterThan(50);
        explain.Position.Should().Be(0);
        explain.StepCounterText.Should().Be($"Schritt 0 von {explain.StepCount}");
        explain.CurrentStep.Should().BeNull();
        explain.StepKindText.Should().Be("Ausgangslage");
        explain.ReasonText.Should().StartWith("Ausgangslage");
        explain.BranchSummary.Should().Be("Ohne Verzweigung (ohne Raten).");
        explain.CloseText.Should().Be("Erklärung beenden");

        window.Editor.IsExplaining.Should().BeTrue();
        var view = window.Editor.Explanation!;
        view.Position.Should().Be(0);
        view.CurrentNodeIds.Should().BeEmpty();
        view.GetSetValue(ClassicSudoku.NodeId(0, 0)).Should().Be(5, "Givens stehen fest");
        view.GetSetValue(ClassicSudoku.NodeId(0, 2)).Should().BeNull();
        view.GetCandidates(ClassicSudoku.NodeId(0, 2)).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);

        // Reiner Anzeigezustand: Rätsel, Verlauf und Dirty-Anzeige bleiben unverändert; der Erklärer bekam eine Kopie.
        PuzzleComparer.Differences(before, window.Editor.Puzzle).Should().BeEmpty();
        window.Editor.History.UndoCount.Should().Be(0);
        window.Editor.IsDirty.Should().BeFalse();
        explain.Result.Puzzle.Should().NotBeSameAs(window.Editor.Puzzle);
    }

    [Fact]
    public async Task Navigation_respects_bounds_and_keeps_the_slider_in_sync()
    {
        var explain = CreateWindow().Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        var count = explain.StepCount;
        var changed = new List<string?>();
        ((INotifyPropertyChanged)explain).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        explain.FirstCommand.CanExecute(null).Should().BeFalse();
        explain.PreviousCommand.CanExecute(null).Should().BeFalse();
        explain.NextCommand.CanExecute(null).Should().BeTrue();
        explain.LastCommand.CanExecute(null).Should().BeTrue();

        explain.NextCommand.Execute(null);
        explain.Position.Should().Be(1);
        explain.SliderValue.Should().Be(1);
        explain.StepCounterText.Should().Be($"Schritt 1 von {count}");
        explain.CurrentStep.Should().BeSameAs(explain.Result!.Steps[0]);
        changed.Should().Contain([nameof(ExplainViewModel.Position), nameof(ExplainViewModel.SliderValue), nameof(ExplainViewModel.StepCounterText), nameof(ExplainViewModel.ReasonText)]);
        explain.FirstCommand.CanExecute(null).Should().BeTrue();

        explain.LastCommand.Execute(null);
        explain.Position.Should().Be(count);
        explain.NextCommand.CanExecute(null).Should().BeFalse();
        explain.LastCommand.CanExecute(null).Should().BeFalse();
        explain.PreviousCommand.CanExecute(null).Should().BeTrue();

        explain.PreviousCommand.Execute(null);
        explain.Position.Should().Be(count - 1);

        // Schieberegler: double, auf ganze Schritte gerundet und begrenzt.
        explain.SliderValue = 57.4;
        explain.Position.Should().Be(57);
        explain.SliderValue = 57.6;
        explain.Position.Should().Be(58);
        explain.SliderValue = -3;
        explain.Position.Should().Be(0);
        explain.SliderValue = count + 50;
        explain.Position.Should().Be(count);
        explain.SliderValue = double.NaN;
        explain.Position.Should().Be(0);

        explain.Position = 9999;
        explain.Position.Should().Be(count);
        explain.Position = -1;
        explain.Position.Should().Be(0);

        explain.LastCommand.Execute(null);
        explain.FirstCommand.Execute(null);
        explain.Position.Should().Be(0);
        explain.CurrentStep.Should().BeNull();
    }

    [Fact]
    public async Task Each_step_highlights_its_nodes_and_the_responsible_rule()
    {
        var window = CreateWindow();
        var explain = window.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        var result = explain.Result!;

        foreach (var step in result.Steps)
        {
            explain.Position = step.Index + 1;
            var view = window.Editor.Explanation!;
            view.Position.Should().Be(step.Index + 1);
            view.CurrentStep.Should().BeSameAs(step);
            view.CurrentNodeIds.Should().BeEquivalentTo(step.NodeIds);
            view.RuleNodeIds.Should().BeEquivalentTo(step.InvolvedNodeIds.Except(step.NodeIds));
            view.Constraint.Should().Be(step.Constraint);
            foreach (var id in step.NodeIds)
            {
                view.GetHighlight(id).Should().Be(ExplanationHighlight.Current);
            }

            foreach (var id in view.RuleNodeIds)
            {
                view.GetHighlight(id).Should().Be(ExplanationHighlight.Rule);
            }

            foreach (var change in step.Changes)
            {
                if (change.Kind == CandidateChangeKind.Eliminate)
                {
                    view.GetRemovedCandidates(change.NodeId).Should().Equal(change.Values);
                    view.GetCandidates(change.NodeId).Should().NotIntersectWith(change.Values);
                }
                else
                {
                    view.GetSetValue(change.NodeId).Should().Be(change.Values[0]);
                }
            }

            view.Snapshot.Candidates.HasSameCandidates(result.GetSnapshot(step.Index + 1).Candidates).Should().BeTrue();
        }

        // Ein Streichschritt einer Gruppe nennt Gruppe und Regel; die Ursache (Knoten mit dem Wert) ist „Regel“-Knoten.
        var groupStep = result.Steps.First(s => s.Kind == StepKind.EliminateCandidates && s.Constraint is { Kind: ConstraintKind.Group });
        explain.Position = groupStep.Index + 1;
        explain.RuleText.Should().MatchRegex(@"^Gruppe \d+ „(Zeile|Spalte|Block) \d+“ · Alle verschieden$");
        explain.StepKindText.Should().Be("Gestrichene Kandidaten");
        explain.TechniqueText.Should().Be("Feststehenden Wert streichen");
        explain.SummaryText.Should().Be(groupStep.Summary);
        explain.ReasonText.Should().Be(groupStep.Text);
        window.Editor.Explanation!.RuleNodeIds.Should().NotBeEmpty();

        var single = result.Steps.First(s => s.Technique == ExplanationTechnique.NakedSingle);
        explain.Position = single.Index + 1;
        explain.RuleText.Should().BeNull("„einziger Kandidat“ hängt an keiner Regel");
        explain.StepKindText.Should().Be("Gesetzter Wert");
        window.Editor.Explanation!.RuleNodeIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Candidate_display_can_be_switched_off_and_on()
    {
        var window = CreateWindow();
        var explain = window.Explain;
        explain.ShowCandidates.Should().BeTrue("die Kandidaten sind standardmäßig sichtbar");
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.Position = 30;
        window.Editor.Explanation!.ShowCandidates.Should().BeTrue();
        var redraws = 0;
        window.Editor.VisualChanged += (_, _) => redraws++;

        explain.ShowCandidates = false;
        window.Editor.Explanation!.ShowCandidates.Should().BeFalse();
        window.Editor.Explanation.Position.Should().Be(30, "der Schritt bleibt");
        redraws.Should().BeGreaterThan(0);

        explain.Position = 31;
        window.Editor.Explanation!.ShowCandidates.Should().BeFalse("die Einstellung gilt für alle Schritte");
        explain.ShowCandidates = true;
        window.Editor.Explanation!.ShowCandidates.Should().BeTrue();
    }

    [Fact]
    public async Task Editing_the_puzzle_ends_the_mode_and_reports_it()
    {
        var window = CreateWindow();
        var explain = window.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.Position = 10;

        window.Editor.SelectNodes([ClassicSudoku.NodeId(0, 2)]);
        window.Editor.Type("4");

        explain.IsActive.Should().BeFalse();
        explain.Result.Should().BeNull();
        window.Editor.IsExplaining.Should().BeFalse();
        explain.Outcome.Should().Be(ExplainOutcome.Outdated);
        explain.Severity.Should().Be(ResultSeverity.Warning);
        explain.StatusMessage.Should().Contain("beendet, weil das Rätsel geändert wurde");
        explain.IsBarVisible.Should().BeTrue();
        explain.CloseText.Should().Be("Schließen");
        explain.NextCommand.CanExecute(null).Should().BeFalse();

        explain.CloseCommand.Execute(null);
        explain.IsBarVisible.Should().BeFalse();
        explain.StatusMessage.Should().BeNull();

        // Auch Rückgängig beendet eine neue Erklärung.
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.IsActive.Should().BeTrue();
        window.Editor.UndoCommand.Execute(null);
        explain.IsActive.Should().BeFalse();
        explain.Outcome.Should().Be(ExplainOutcome.Outdated);

        // Ein neues Dokument beendet den Modus ohne Meldung.
        await explain.ExplainCommand.ExecuteAsync(null);
        window.Editor.LoadPuzzle(ClassicSudoku.Create(Easy));
        explain.IsActive.Should().BeFalse();
        explain.IsBarVisible.Should().BeFalse();
        explain.Outcome.Should().Be(ExplainOutcome.None);
    }

    [Fact]
    public async Task Selection_and_view_changes_keep_the_mode()
    {
        var window = CreateWindow();
        var explain = window.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.Position = 5;

        window.Editor.SelectNodes([ClassicSudoku.NodeId(4, 4)]);
        window.Editor.SelectGroup(3);
        window.Editor.ClearSelection();
        window.Editor.FitView();
        window.Editor.SetGroupVisible(2, false);

        explain.IsActive.Should().BeTrue();
        explain.Position.Should().Be(5);
        window.Editor.IsExplaining.Should().BeTrue();
    }

    [Fact]
    public async Task Leave_command_returns_to_the_editor()
    {
        var window = CreateWindow();
        var explain = window.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.Position = 20;
        explain.CloseCommand.CanExecute(null).Should().BeTrue();

        explain.CloseCommand.Execute(null);

        explain.IsActive.Should().BeFalse();
        explain.IsBarVisible.Should().BeFalse();
        explain.Position.Should().Be(0);
        window.Editor.IsExplaining.Should().BeFalse();
        explain.CloseCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Solution_and_explanation_exclude_each_other_on_the_canvas()
    {
        var window = CreateWindow();
        await window.Solve.SolveCommand.ExecuteAsync(null);
        window.Editor.HasSolution.Should().BeTrue();

        await window.Explain.ExplainCommand.ExecuteAsync(null);
        window.Editor.HasSolution.Should().BeFalse("der Erklär-Modus blendet die Lösung aus");
        window.Solve.IsShowFirstSolutionVisible.Should().BeTrue("die Lösung kann wieder angezeigt werden");

        window.Solve.ShowFirstSolutionCommand.Execute(null);
        window.Editor.HasSolution.Should().BeTrue();
        window.Explain.IsActive.Should().BeFalse("eine angezeigte Lösung beendet den Erklär-Modus");
        window.Editor.IsExplaining.Should().BeFalse();
        window.Explain.IsBarVisible.Should().BeFalse();
    }

    [Fact]
    public async Task Explain_runs_in_background_and_can_be_cancelled_keeping_found_steps()
    {
        // „Mittel“: Die Logik kommt ein Stück weit, dann fragt die Engine den (blockierenden) Solver für eine Verzweigung.
        _solver.Block = true;
        var window = CreateWindow(LoadExample("klassisch-mittel.json"));
        var explain = window.Explain;

        var running = explain.ExplainCommand.ExecuteAsync(null);
        _solver.WaitUntilStarted();
        explain.IsBusy.Should().BeTrue();
        explain.Severity.Should().Be(ResultSeverity.Info);
        explain.IsBarVisible.Should().BeTrue();
        explain.BusyText.Should().Contain("berechnet");
        explain.ExplainCommand.CanExecute(null).Should().BeFalse();
        explain.CancelCommand.CanExecute(null).Should().BeTrue();
        explain.CloseCommand.CanExecute(null).Should().BeFalse();

        explain.CancelCommand.Execute(null);
        await running;

        _solver.WasCancelled.Should().BeTrue();
        explain.IsBusy.Should().BeFalse();
        explain.Outcome.Should().Be(ExplainOutcome.Cancelled);
        explain.Severity.Should().Be(ResultSeverity.Warning);
        explain.IsActive.Should().BeTrue("die bis zum Abbruch gefundenen Schritte bleiben ansehbar");
        explain.StepCount.Should().BeGreaterThan(10);
        explain.StatusMessage.Should().Contain("abgebrochen").And.Contain($"ersten {explain.StepCount} Schritte");
        explain.Result!.Steps.Should().NotContain(s => s.Kind == StepKind.Branch);
        explain.LastCommand.Execute(null);
        window.Editor.Explanation!.Position.Should().Be(explain.StepCount);
    }

    [Fact]
    public async Task Cancelled_without_steps_shows_only_a_message()
    {
        _solver.Block = true;
        var window = CreateWindow(ClassicSudoku.Create());
        var explain = window.Explain;

        var running = explain.ExplainCommand.ExecuteAsync(null);
        _solver.WaitUntilStarted();
        explain.CancelCommand.Execute(null);
        await running;

        explain.Outcome.Should().Be(ExplainOutcome.Cancelled);
        explain.IsActive.Should().BeFalse();
        explain.StatusMessage.Should().Contain("kein Schritt");
        explain.IsBarVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Editing_during_the_calculation_discards_the_result()
    {
        using var explainer = new FakeExplainer();
        var window = CreateWindow(explainer: explainer);
        var explain = window.Explain;

        var running = explain.ExplainCommand.ExecuteAsync(null);
        explain.IsBusy.Should().BeTrue();
        window.Editor.SelectNodes([ClassicSudoku.NodeId(0, 2)]);
        window.Editor.Type("4");
        explainer.Complete();
        await running;

        explain.Outcome.Should().Be(ExplainOutcome.Outdated);
        explain.StatusMessage.Should().Contain("während der Berechnung geändert");
        explain.IsActive.Should().BeFalse();
        window.Editor.IsExplaining.Should().BeFalse();
        explainer.ReceivedPuzzles.Should().ContainSingle().Which.Should().NotBeSameAs(window.Editor.Puzzle);
    }

    [Fact]
    public async Task Loading_another_puzzle_cancels_the_calculation()
    {
        using var explainer = new FakeExplainer();
        var window = CreateWindow(explainer: explainer);
        var explain = window.Explain;

        var running = explain.ExplainCommand.ExecuteAsync(null);
        window.Editor.LoadPuzzle(ClassicSudoku.Create());
        explainer.LastToken.IsCancellationRequested.Should().BeTrue();
        explainer.Complete();
        await running;

        explain.Outcome.Should().Be(ExplainOutcome.Outdated);
        explain.StatusMessage.Should().Contain("anderes Rätsel");
        explain.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Internal_error_of_the_engine_is_shown_as_message()
    {
        // Ein falscher (Ersatz-)Solver liefert für das leere Sudoku überall die 1: Bei der zweiten Verzweigung ist die 1
        // längst gestrichen, und die echte Engine wirft „Interner Fehler …“.
        var window = CreateWindow(ClassicSudoku.Create());
        var explain = window.Explain;

        await explain.ExplainCommand.ExecuteAsync(null);

        explain.IsBusy.Should().BeFalse();
        explain.Outcome.Should().Be(ExplainOutcome.Failed);
        explain.Severity.Should().Be(ResultSeverity.Error);
        explain.StatusMessage.Should().StartWith("Die Erklärung ist fehlgeschlagen. Interner Fehler:");
        explain.IsActive.Should().BeFalse();
        explain.IsBarVisible.Should().BeTrue();
        explain.ExplainCommand.CanExecute(null).Should().BeTrue("nach einem Fehler kann erneut erklärt werden");
    }

    [Fact]
    public async Task Other_exceptions_are_shown_as_message()
    {
        using var explainer = new FakeExplainer { Throw = new DllNotFoundException("ortools fehlt") };
        var explain = CreateWindow(explainer: explainer).Explain;

        await explain.ExplainCommand.ExecuteAsync(null);

        explain.Outcome.Should().Be(ExplainOutcome.Failed);
        explain.StatusMessage.Should().Be("Die Erklärung ist mit einem Fehler abgebrochen: ortools fehlt");
    }

    [Fact]
    public async Task Invalid_puzzle_lists_its_problems_without_entering_the_mode()
    {
        var puzzle = ClassicSudoku.Create(Easy);
        puzzle.Groups[0].Rule = "Sum";
        var explain = CreateWindow(puzzle).Explain;

        await explain.ExplainCommand.ExecuteAsync(null);

        explain.Outcome.Should().Be(ExplainOutcome.Invalid);
        explain.Severity.Should().Be(ResultSeverity.Error);
        explain.IsActive.Should().BeFalse();
        explain.StatusMessage.Should().Be("Das Rätsel ist ungültig und kann nicht erklärt werden. Bitte zuerst dieses Problem beheben:");
        explain.HasErrors.Should().BeTrue();
        explain.Errors.Should().ContainSingle().Which.Should().Contain("target");
    }

    [Fact]
    public async Task Contradiction_is_reported_as_unsolvable_and_steps_stay_navigable()
    {
        var window = CreateWindow(ClassicSudoku.Create(Conflicting));
        var explain = window.Explain;

        await explain.ExplainCommand.ExecuteAsync(null);

        explain.Outcome.Should().Be(ExplainOutcome.Unsolvable);
        explain.Severity.Should().Be(ResultSeverity.Error);
        explain.StatusMessage.Should().StartWith("Das Rätsel ist unlösbar. Widerspruch:");
        explain.IsActive.Should().BeTrue();
        explain.LastCommand.Execute(null);
        window.Editor.Explanation!.EmptyNodeIds.Should().NotBeEmpty("der Knoten ohne Kandidaten wird rot markiert");
    }

    [Fact]
    public async Task Ambiguous_and_stuck_results_are_warnings()
    {
        var ambiguous = CreateWindow(ClassicSudoku.Create(Ambiguous), new OrToolsSolver()).Explain;
        await ambiguous.ExplainCommand.ExecuteAsync(null);
        ambiguous.Outcome.Should().Be(ExplainOutcome.Ambiguous);
        ambiguous.Severity.Should().Be(ResultSeverity.Warning);
        ambiguous.StatusMessage.Should().Contain("mehrdeutig");
        ambiguous.IsActive.Should().BeTrue();

        // Zeitlimit des Solvers ohne Lösung: festgefahren, die Schritte bis dahin bleiben ansehbar.
        _solver.CountResult = (_, limit) => SolutionCountResult.Timeout(0, limit, [], TimeSpan.FromSeconds(1));
        var stuck = CreateWindow(LoadExample("klassisch-mittel.json")).Explain;
        await stuck.ExplainCommand.ExecuteAsync(null);
        stuck.Outcome.Should().Be(ExplainOutcome.Stuck, stuck.StatusMessage);
        stuck.Severity.Should().Be(ResultSeverity.Warning);
        stuck.IsActive.Should().BeTrue();
        stuck.StepCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Time_limit_of_the_solve_bar_is_used_and_must_be_valid()
    {
        using var explainer = new FakeExplainer { CompleteImmediately = true };
        var window = CreateWindow(explainer: explainer);

        window.Solve.TimeLimitText = "2,5";
        await window.Explain.ExplainCommand.ExecuteAsync(null);
        explainer.ReceivedOptions.Should().ContainSingle().Which.TimeLimit.Should().Be(TimeSpan.FromSeconds(2.5));

        window.Solve.TimeLimitText = "abc";
        window.Explain.ExplainCommand.CanExecute(null).Should().BeFalse();
        window.Solve.TimeLimitText = "10";
        window.Explain.ExplainCommand.CanExecute(null).Should().BeTrue();
    }

    /// <summary>
    /// Drei Knoten (Werte 1–3) in einer Reihe mit Abstand 2, verbunden durch „Kleiner als“-Kanten 1 → 2 → 3; ohne
    /// Gruppen, damit jede Begründung an einer Kante hängt.
    /// </summary>
    internal static Puzzle LessThanChain()
    {
        var puzzle = new Puzzle("Kette", GridType.Square, new ValueDomain(1, 3));
        puzzle.Nodes.Add(new Node(1, 0, 0));
        puzzle.Nodes.Add(new Node(2, 2, 0));
        puzzle.Nodes.Add(new Node(3, 4, 0));
        puzzle.Edges.Add(new Edge(1, 1, 2, "LessThan"));
        puzzle.Edges.Add(new Edge(2, 2, 3, "LessThan"));
        return puzzle;
    }

    [Fact]
    public async Task Edge_rule_steps_name_the_edge_and_highlight_its_other_node()
    {
        var window = CreateWindow(LessThanChain(), new OrToolsSolver());
        var explain = window.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.Outcome.Should().Be(ExplainOutcome.Solved, explain.StatusMessage);
        var edgeSteps = explain.Result!.Steps.Where(s => s.Constraint is { Kind: ConstraintKind.Edge }).ToList();
        edgeSteps.Should().NotBeEmpty("ohne Gruppen hängt die Begründung an den Kanten");

        foreach (var step in edgeSteps)
        {
            explain.Position = step.Index + 1;
            var edge = window.Editor.Puzzle.FindEdge(step.Constraint!.Value.Id)!;
            explain.RuleText.Should().Be($"Kante {edge.Id} (Knoten {edge.From} – {edge.To}) · Kleiner als");
            var view = window.Editor.Explanation!;
            view.Constraint.Should().Be(step.Constraint);
            view.RuleNodeIds.Should().BeEquivalentTo(step.InvolvedNodeIds.Except(step.NodeIds));
            view.RuleNodeIds.Should().BeSubsetOf([edge.From, edge.To], "die Begründung einer Kante nennt nur ihre Endknoten");
            foreach (var id in step.NodeIds)
            {
                view.GetHighlight(id).Should().Be(ExplanationHighlight.Current);
            }

            foreach (var id in view.RuleNodeIds)
            {
                view.GetHighlight(id).Should().Be(ExplanationHighlight.Rule);
            }
        }

        // Der erste Schritt streicht an Kante 1 die 3 aus Knoten 1; Knoten 2 ist die Ursache.
        explain.Position = 1;
        explain.RuleText.Should().Be("Kante 1 (Knoten 1 – 2) · Kleiner als");
        window.Editor.Explanation!.CurrentNodeIds.Should().Equal(1);
        window.Editor.Explanation.RuleNodeIds.Should().Equal(2);
    }

    [Fact]
    public async Task Branch_steps_are_marked()
    {
        var window = CreateWindow(LoadExample("klassisch-mittel.json"), new OrToolsSolver());
        var explain = window.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        var branches = explain.Result!.Steps.Where(s => s.Kind == StepKind.Branch).ToList();
        branches.Should().NotBeEmpty("„mittel“ braucht Verzweigungen");
        explain.BranchSummary.Should().StartWith($"{branches.Count} Verzweigung");

        var branch = branches[0];
        explain.Position = branch.Index + 1;
        explain.IsBranchStep.Should().BeTrue();
        explain.StepKindText.Should().Be("Verzweigung – geraten");
        explain.TechniqueText.Should().BeNull();
        explain.ReasonText.Should().Contain("Ohne Raten");
        var view = window.Editor.Explanation!;
        view.IsBranch.Should().BeTrue();
        view.GetHighlight(branch.NodeId!.Value).Should().Be(ExplanationHighlight.CurrentBranch);
        view.IsGuessed(branch.NodeId.Value).Should().BeTrue();

        explain.Position = branch.Index;
        window.Editor.Explanation!.IsGuessed(branch.NodeId.Value).Should().BeFalse("vor der Verzweigung ist der Knoten noch offen");
        explain.IsBranchStep.Should().BeFalse();
        explain.LastCommand.Execute(null);
        window.Editor.Explanation!.GuessedNodeIds.Should().BeEquivalentTo(branches.Select(b => b.NodeId!.Value));
    }

    /// <summary>
    /// Akzeptanz Paket 6 auf ViewModel-Ebene: „klassisch-leicht.json“ wird über „Öffnen“ geladen und ganz ohne Verzweigung
    /// erklärt; der letzte Zustand ist die Lösung des Solvers, jeder gesetzte Wert ist mit ihr konsistent.
    /// </summary>
    [Fact]
    public async Task Easy_classic_sudoku_is_explained_without_branching_and_ends_in_the_solution()
    {
        var solver = new OrToolsSolver();
        var window = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), solver);
        (await window.OpenFileAsync(PuzzlePath("klassisch-leicht.json"))).Should().BeTrue();
        var solution = solver.Solve(window.Editor.Puzzle).Values!;
        var explain = window.Explain;

        await explain.ExplainCommand.ExecuteAsync(null);

        explain.Outcome.Should().Be(ExplainOutcome.Solved, explain.StatusMessage);
        explain.Result!.Steps.Should().NotContain(s => s.Kind == StepKind.Branch);
        explain.BranchSummary.Should().Be("Ohne Verzweigung (ohne Raten).");
        for (var position = 1; position <= explain.StepCount; position++)
        {
            explain.NextCommand.Execute(null);
            explain.IsBranchStep.Should().BeFalse();
            var view = window.Editor.Explanation!;
            view.GuessedNodeIds.Should().BeEmpty();
            foreach (var id in view.CurrentNodeIds)
            {
                (view.GetSetValue(id) ?? solution[id]).Should().Be(solution[id]);
                view.GetCandidates(id).Should().Contain(solution[id], "kein Schritt streicht den Lösungswert");
            }
        }

        explain.Position.Should().Be(explain.StepCount);
        explain.NextCommand.CanExecute(null).Should().BeFalse();
        var final = window.Editor.Explanation!;
        foreach (var node in window.Editor.Puzzle.Nodes)
        {
            final.GetSetValue(node.Id).Should().Be(solution[node.Id], $"Knoten {node.Id}");
        }

        explain.Result.Solution.Should().BeEquivalentTo(solution);
    }
}

/// <summary>
/// Ersatz-Erklärer: hält die Berechnung an, bis der Test sie freigibt (<see cref="Complete"/>), oder wirft eine
/// Ausnahme. Das Ergebnis berechnet die echte Engine (mit einem Ersatzsolver), weil <see cref="ExplanationResult"/> keinen
/// öffentlichen Konstruktor hat.
/// </summary>
internal sealed class FakeExplainer : IPuzzleExplainer, IDisposable
{
    private readonly FakeSolver _solver = new();
    private readonly List<TaskCompletionSource> _pending = [];

    public Exception? Throw { get; set; }

    public bool CompleteImmediately { get; set; }

    public List<SolverOptions> ReceivedOptions { get; } = [];

    public List<Puzzle> ReceivedPuzzles { get; } = [];

    public CancellationToken LastToken { get; private set; }

    public void Complete()
    {
        foreach (var pending in _pending)
        {
            pending.TrySetResult();
        }
    }

    public async Task<ExplanationResult> ExplainAsync(Puzzle puzzle, SolverOptions solverOptions, CancellationToken cancellationToken)
    {
        var copy = puzzle.Clone();
        ReceivedPuzzles.Add(copy);
        ReceivedOptions.Add(solverOptions);
        LastToken = cancellationToken;
        if (Throw is { } exception)
        {
            throw exception;
        }

        if (!CompleteImmediately)
        {
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(pending);
            await pending.Task.ConfigureAwait(false);
        }

        return new ExplanationEngine(_solver).Explain(copy, cancellationToken);
    }

    public void Dispose() => _solver.Dispose();
}
