using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GraphDoku.App.Controls;
using GraphDoku.App.Editing;
using GraphDoku.App.Explaining;
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
        // Auf Knoten 1 (oben links) zoomen, damit die geänderte Zeile 1 im Bild bleibt.
        var hexEditor = viewModel.Editor;
        var first = hexEditor.Puzzle.FindNode(1)!;
        hexEditor.PointerWheel(hexEditor.Viewport.ToScreen(EditorGeometry.Center(hexEditor.Puzzle.Grid, first)), 6);
        var zoomed = Capture(window, "erklaerung-hexadoku-zoom.png");
        var hexCanvas = window.FindControl<GraphCanvas>("Canvas")!;
        hexEditor.Explanation!.GetHighlight(1).Should().Be(ExplanationHighlight.Current);
        CountPixels(zoomed, NodeArea(window, hexCanvas, hexEditor, first, 0.9), (r, g, b) => r > 240 && g > 225 && b < 170)
            .Should().BeGreaterThan(20, "Knoten 1 ist im 16 × 16-Raster gelb hervorgehoben");
        CountPixels(zoomed, NodeArea(window, hexCanvas, hexEditor, first, 0.7), (r, g, b) => r > 180 && g < 90 && b < 90)
            .Should().BeGreaterThan(3, "der gestrichene Kandidat steht rot im 4 × 4-Raster");
        window.Close();

        var (small, smallModel) = OpenWindow(LoadExample("klassisch-leicht.json"));
        small.Width = small.MinWidth;
        small.Height = small.MinHeight;
        await smallModel.Explain.ExplainCommand.ExecuteAsync(null);
        smallModel.Explain.Position = 40;
        var smallFrame = Capture(small, "erklaerung-mindestgroesse.png");
        var canvas = small.FindControl<GraphCanvas>("Canvas")!;
        canvas.Bounds.Height.Should().BeGreaterThan(200, "die Zeichenfläche bleibt im Erklär-Modus auch bei Mindestgröße nutzbar");

        // Zu klein für das Kandidatenraster: Der gestrichene Wert des aktuellen Schritts steht trotzdem rot im Knoten.
        var smallStep = smallModel.Editor.Explanation!.CurrentStep!;
        smallStep.Kind.Should().Be(StepKind.EliminateCandidates);
        var changed = smallModel.Editor.Puzzle.FindNode(smallStep.NodeIds[0])!;
        CountPixels(smallFrame, NodeArea(small, canvas, smallModel.Editor, changed, 0.9), (r, g, b) => r > 180 && g < 90 && b < 90)
            .Should().BeGreaterThan(3, "der gestrichene Kandidat ist auch bei Mindestgröße sichtbar");
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
    public Task Explanation_mode_marks_contradiction_and_edge_rule() => fixture.Session.Dispatch(async () =>
    {
        // Widerspruch: Der Knoten ohne Kandidaten ist zugleich Knoten des letzten (aktuellen) Schritts – gelb UND rot umrandet.
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create(ExplainViewModelTests.Conflicting));
        var explain = viewModel.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.Outcome.Should().Be(ExplainOutcome.Unsolvable, explain.StatusMessage);
        explain.LastCommand.Execute(null);
        var view = viewModel.Editor.Explanation!;
        view.EmptyNodeIds.Should().NotBeEmpty();
        var emptyId = view.EmptyNodeIds.First();
        view.GetHighlight(emptyId).Should().Be(ExplanationHighlight.Current, "die Engine endet mit dem Schritt, der den Knoten leert");
        var frame = Capture(window, "erklaerung-widerspruch.png");
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        var emptyNode = viewModel.Editor.Puzzle.FindNode(emptyId)!;
        CountPixels(frame, NodeArea(window, canvas, viewModel.Editor, emptyNode, 1.3), (r, g, b) => r > 180 && g < 90 && b < 90)
            .Should().BeGreaterThan(20, "der Knoten ohne Kandidaten bekommt einen roten Ring");
        CountPixels(frame, NodeArea(window, canvas, viewModel.Editor, emptyNode, 0.9), (r, g, b) => r > 240 && g > 225 && b < 170)
            .Should().BeGreaterThan(20, "die gelbe Hervorhebung des aktuellen Schritts bleibt");
        window.Close();

        // Regel einer Kante: Die Kante des Schritts ist grün und dick, die andere Kante nicht.
        var (chainWindow, chainModel) = OpenWindow(ExplainViewModelTests.LessThanChain());
        await chainModel.Explain.ExplainCommand.ExecuteAsync(null);
        chainModel.Explain.Outcome.Should().Be(ExplainOutcome.Solved, chainModel.Explain.StatusMessage);
        var edgeStep = chainModel.Explain.Result!.Steps.First(s => s.Constraint is { Kind: ConstraintKind.Edge });
        chainModel.Explain.Position = edgeStep.Index + 1;
        var chainFrame = Capture(chainWindow, "erklaerung-kante.png");
        var chainCanvas = chainWindow.FindControl<GraphCanvas>("Canvas")!;
        var editor = chainModel.Editor;

        int GreenOnEdge(Edge edge)
        {
            // Kurzes Stück der Linie kurz hinter dem Rand des Startknotens (vor der Marke in der Mitte).
            var from = EditorGeometry.Center(editor.Puzzle.Grid, editor.Puzzle.FindNode(edge.From)!);
            var to = EditorGeometry.Center(editor.Puzzle.Grid, editor.Puzzle.FindNode(edge.To)!);
            var a = editor.Viewport.ToScreen(new Core.Geometry.WorldPoint(from.X + ((to.X - from.X) * 0.25), from.Y));
            var b = editor.Viewport.ToScreen(new Core.Geometry.WorldPoint(from.X + ((to.X - from.X) * 0.35), from.Y));
            var left = chainCanvas.TranslatePoint(new Point(a.X, a.Y), chainWindow)!.Value;
            var right = chainCanvas.TranslatePoint(new Point(b.X, b.Y), chainWindow)!.Value;
            return CountPixels(chainFrame, new PixelRect((int)left.X, (int)left.Y - 4, (int)(right.X - left.X), 8), (r, g, b) => r < 90 && g > 120 && b < 120);
        }

        var ruleEdge = editor.Puzzle.FindEdge(edgeStep.Constraint!.Value.Id)!;
        var otherEdge = editor.Puzzle.Edges.Single(e => e.Id != ruleEdge.Id);
        GreenOnEdge(ruleEdge).Should().BeGreaterThan(10, "die Kante der Regel ist grün");
        GreenOnEdge(otherEdge).Should().Be(0, "andere Kanten bleiben normal");
        chainWindow.Close();
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
