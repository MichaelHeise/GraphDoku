using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GraphDoku.App.Controls;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.App.Views;
using GraphDoku.Core.Model;
using GraphDoku.Core.Serialization;
using GraphDoku.Core.Templates;
using GraphDoku.Explainer;

namespace GraphDoku.App.Tests.Rendering;

/// <summary>Rendering-Rauchtests des Erklär-Modus (Paket 6): mittlerer Schritt mit Kandidaten, Verzweigung, Hexadoku, Zustände der Leiste.</summary>
public sealed partial class RenderingSmokeTests
{
    internal static Puzzle LoadExample(string fileName) =>
        PuzzleSerializer.Load(Path.Combine(MainWindowViewModelTests.FindRepositoryRoot(), "puzzles", fileName));

    /// <summary>16 × 16 mit Werten 0–15 (Zeilen, Spalten, 4 × 4-Blöcke), etwa zwei Drittel Givens.</summary>
    internal static Puzzle Hexadoku()
    {
        var puzzle = new Puzzle("Hexadoku-Probe", GridType.Square, new ValueDomain(0, 15));
        for (var r = 0; r < 16; r++)
        {
            for (var c = 0; c < 16; c++)
            {
                var value = ((4 * (r % 4)) + (r / 4) + c) % 16;
                puzzle.Nodes.Add(new Node((r * 16) + c + 1, c, r, given: ((r * 5) + (c * 7)) % 3 != 0 ? value : null));
            }
        }

        var id = 1;
        for (var i = 0; i < 16; i++)
        {
            puzzle.Groups.Add(new Group(id++, [.. Enumerable.Range(0, 16).Select(c => (i * 16) + c + 1)], "AllDifferent", label: $"Zeile {i + 1}"));
            puzzle.Groups.Add(new Group(id++, [.. Enumerable.Range(0, 16).Select(r => (r * 16) + i + 1)], "AllDifferent", label: $"Spalte {i + 1}"));
            var (top, left) = (i / 4 * 4, i % 4 * 4);
            puzzle.Groups.Add(new Group(id++, [.. Enumerable.Range(0, 16).Select(k => ((top + (k / 4)) * 16) + left + (k % 4) + 1)], "AllDifferent", label: $"Block {i + 1}"));
        }

        return puzzle;
    }

    [Fact]
    public Task Explanation_mode_shows_middle_step_with_candidates() => fixture.Session.Dispatch(async () =>
    {
        var (window, viewModel) = OpenWindow(LoadExample("klassisch-leicht.json"));
        var explain = viewModel.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.Outcome.Should().Be(ExplainOutcome.Solved, explain.StatusMessage);
        Capture(window, "erklaerung-leicht-start.png");

        // Mittlerer Schritt, möglichst ein Streichschritt (zeigt gestrichene Kandidaten rot).
        var middle = explain.Result!.Steps.Skip(explain.StepCount / 2).First(s => s.Kind == StepKind.EliminateCandidates);
        explain.Position = middle.Index + 1;
        var frame = Capture(window, "erklaerung-leicht-mitte.png");

        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        var editor = viewModel.Editor;
        var current = editor.Puzzle.FindNode(middle.NodeIds[0])!;
        CountPixels(frame, NodeArea(window, canvas, editor, current, 0.9), (r, g, b) => r > 240 && g > 225 && b < 170)
            .Should().BeGreaterThan(20, "der Knoten des aktuellen Schritts ist gelb hinterlegt");
        CountPixels(frame, NodeArea(window, canvas, editor, current, 0.7), (r, g, b) => r > 180 && g < 90 && b < 90)
            .Should().BeGreaterThan(3, "der gestrichene Kandidat ist rot");

        // Ein offener Knoten ohne Hervorhebung zeigt Kandidaten (grau), ohne Kandidatenanzeige nicht.
        var snapshot = explain.Result.GetSnapshot(explain.Position);
        var open = editor.Puzzle.Nodes.First(n => !snapshot.IsSet(n.Id) && !middle.InvolvedNodeIds.Contains(n.Id) && !middle.NodeIds.Contains(n.Id));
        bool Gray(byte r, byte g, byte b) => r < 160 && g < 170 && b < 190 && Math.Abs(r - b) < 60;
        CountPixels(frame, NodeArea(window, canvas, editor, open, 0.6), Gray).Should().BeGreaterThan(10);

        explain.ShowCandidates = false;
        var without = Capture(window, "erklaerung-leicht-mitte-ohne-kandidaten.png");
        CountPixels(without, NodeArea(window, canvas, editor, open, 0.6), Gray).Should().Be(0);

        explain.ShowCandidates = true;
        explain.LastCommand.Execute(null);
        Capture(window, "erklaerung-leicht-ende.png");

        // Die Begründung steht im Panel rechts, das Eigenschaften-Panel ist solange ausgeblendet.
        window.FindControl<StackPanel>("ExplanationPanel")!.IsEffectivelyVisible.Should().BeTrue();
        window.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Explanation_mode_marks_branch_steps() => fixture.Session.Dispatch(async () =>
    {
        var (window, viewModel) = OpenWindow(LoadExample("klassisch-mittel.json"));
        var explain = viewModel.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        var branch = explain.Result!.Steps.First(s => s.Kind == StepKind.Branch);
        explain.Position = branch.Index + 1;
        explain.IsBranchStep.Should().BeTrue();
        var frame = Capture(window, "erklaerung-mittel-verzweigung.png");

        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        var node = viewModel.Editor.Puzzle.FindNode(branch.NodeId!.Value)!;
        CountPixels(frame, NodeArea(window, canvas, viewModel.Editor, node, 1.6), (r, g, b) => r > 200 && g is > 60 and < 140 && b < 60)
            .Should().BeGreaterThan(30, "die Verzweigung ist orange markiert (Ring und Marke „Raten“)");

        // Ein paar Schritte später: geratener Wert orange, folgender Schritt normal.
        explain.Position = branch.Index + 4;
        Capture(window, "erklaerung-mittel-nach-verzweigung.png");
        window.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Explanation_mode_renders_hex_candidates_and_minimum_size() => fixture.Session.Dispatch(async () =>
    {
        var (window, viewModel) = OpenWindow(Hexadoku());
        var explain = viewModel.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.IsActive.Should().BeTrue(explain.StatusMessage);
        explain.Position = 1;
        Capture(window, "erklaerung-hexadoku.png");
        window.Width = 1920;
        window.Height = 1200;
        Capture(window, "erklaerung-hexadoku-gross.png");
        viewModel.Editor.ZoomInCommand.Execute(null);
        viewModel.Editor.ZoomInCommand.Execute(null);
        Capture(window, "erklaerung-hexadoku-zoom.png");
        window.Close();

        var (small, smallModel) = OpenWindow(LoadExample("klassisch-leicht.json"));
        small.Width = small.MinWidth;
        small.Height = small.MinHeight;
        await smallModel.Explain.ExplainCommand.ExecuteAsync(null);
        smallModel.Explain.Position = 40;
        Capture(small, "erklaerung-mindestgroesse.png");
        var canvas = small.FindControl<GraphCanvas>("Canvas")!;
        canvas.Bounds.Height.Should().BeGreaterThan(200, "die Zeichenfläche bleibt im Erklär-Modus auch bei Mindestgröße nutzbar");
        foreach (var text in new[] { "« Anfang", "Vor ›", "Ende »", "Erklärung beenden" })
        {
            var button = small.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, text) && b.IsEffectivelyVisible);
            button.IsEffectivelyVisible.Should().BeTrue(text);
            var corner = button.TranslatePoint(new Point(button.Bounds.Width, 0), small)!.Value;
            corner.X.Should().BeLessThanOrEqualTo(small.Bounds.Width, text);
        }

        small.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Explanation_busy_cancelled_and_outdated_states_render() => fixture.Session.Dispatch(async () =>
    {
        // Leeres Sudoku: Die Logik sitzt sofort fest, die Engine fragt den (blockierenden) Ersatzsolver.
        using var solver = new FakeSolver { Block = true };
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), solver);
        viewModel.Editor.LoadPuzzle(ClassicSudoku.Create());
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        var explain = viewModel.Explain;

        var running = explain.ExplainCommand.ExecuteAsync(null);
        solver.WaitUntilStarted();
        Capture(window, "erklaerung-laeuft.png");
        var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Abbrechen") && b.IsEffectivelyVisible);
        cancel.IsEffectivelyEnabled.Should().BeTrue();
        explain.CancelCommand.Execute(null);
        await running;
        explain.Outcome.Should().Be(ExplainOutcome.Cancelled);
        Capture(window, "erklaerung-abgebrochen.png");
        window.Close();

        var (window2, viewModel2) = OpenWindow(ClassicSudoku.Create(Givens));
        await viewModel2.Explain.ExplainCommand.ExecuteAsync(null);
        viewModel2.Explain.Position = 10;
        viewModel2.Editor.SelectNodes([ClassicSudoku.NodeId(0, 2)]);
        viewModel2.Editor.EnterValueCharacter('4');
        viewModel2.Explain.Outcome.Should().Be(ExplainOutcome.Outdated);
        Capture(window2, "erklaerung-veraltet.png");

        var invalid = ClassicSudoku.Create(Givens);
        invalid.Groups[0].Rule = "Sum";
        viewModel2.Editor.LoadPuzzle(invalid);
        await viewModel2.Explain.ExplainCommand.ExecuteAsync(null);
        viewModel2.Explain.Outcome.Should().Be(ExplainOutcome.Invalid);
        Capture(window2, "erklaerung-ungueltig.png");
        window2.Close();
        Dispatcher.UIThread.RunJobs();
        return true;
    }, CancellationToken.None);
}
