using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GraphDoku.App.Controls;
using GraphDoku.App.Editing;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.App.Views;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;
using GraphDoku.Explainer;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests.Rendering;

/// <summary>
/// Rendering-Rauchtests zu Paket 7 (Zellformen): klassisches 9 × 9, Dreiecks-Sudoku und Hexadoku in der
/// Zelldarstellung – Givens, Auswahl, Gruppe, Kante, Lösung und Erklär-Modus mit Kandidaten. Die Bilder wurden angesehen.
/// </summary>
public sealed partial class RenderingSmokeTests
{
    [Fact]
    public Task Classic_sudoku_renders_as_square_cells() => fixture.Session.Dispatch(async () =>
    {
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create(Givens));
        var editor = viewModel.Editor;
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        var node = editor.Puzzle.FindNode(ClassicSudoku.NodeId(4, 4))!;
        editor.SelectNodes([node.Id]);
        var circles = Capture(window, "zellen-klassisch-kreise.png");
        CountSelectionBluePixels(circles, CellCorner(window, canvas, editor, node, 0)).Should().Be(0, "bei Kreisen ist die Zellecke frei");

        ShapeRadio(window, "Zellen").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        editor.NodeShape.Should().Be(NodeShape.Cell, "der Umschalter im Statusbereich schaltet die Darstellung um");
        var cells = Capture(window, "zellen-klassisch.png");
        CountSelectionBluePixels(cells, CellCorner(window, canvas, editor, node, 0)).Should().BeGreaterThan(8, "die gewählte Zelle ist bis in die Ecke hinterlegt");
        CountDarkPixels(cells, NodeArea(window, canvas, editor, editor.Puzzle.FindNode(ClassicSudoku.NodeId(0, 0))!)).Should().BeGreaterThan(10, "Givens bleiben lesbar");

        editor.SelectNodes([ClassicSudoku.NodeId(0, 2), ClassicSudoku.NodeId(1, 2), ClassicSudoku.NodeId(2, 2)]);
        Capture(window, "zellen-klassisch-auswahl.png");
        editor.SelectGroup(20);
        Capture(window, "zellen-klassisch-gruppe.png");

        await viewModel.Solve.SolveCommand.ExecuteAsync(null);
        var solved = Capture(window, "zellen-klassisch-geloest.png");
        CountSolutionBluePixels(solved, NodeArea(window, canvas, editor, editor.Puzzle.FindNode(ClassicSudoku.NodeId(0, 2))!)).Should().BeGreaterThan(10);

        await viewModel.Explain.ExplainCommand.ExecuteAsync(null);
        var explain = viewModel.Explain;
        var middle = explain.Result!.Steps.Skip(explain.StepCount / 2).First(s => s.Kind == StepKind.EliminateCandidates);
        explain.Position = middle.Index + 1;
        var frame = Capture(window, "zellen-klassisch-erklaerung.png");
        var current = editor.Puzzle.FindNode(middle.NodeIds[0])!;
        CountPixels(frame, CellCorner(window, canvas, editor, current, 0), (r, g, b) => r > 240 && g > 225 && b < 170)
            .Should().BeGreaterThan(8, "die Zelle des aktuellen Schritts ist bis in die Ecke gelb");
        CountPixels(frame, NodeArea(window, canvas, editor, current, 0.9), (r, g, b) => r > 180 && g < 90 && b < 90)
            .Should().BeGreaterThan(3, "der gestrichene Kandidat ist rot");
        editor.Viewport.ZoomAt(editor.ScreenOf(new GridPoint(current.X, current.Y)), 2);
        Capture(window, "zellen-klassisch-erklaerung-zoom.png");
        window.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Triangle_puzzle_renders_as_up_and_down_triangles() => fixture.Session.Dispatch(async () =>
    {
        var (window, viewModel) = OpenWindow(LoadExample("dreiecks-sudoku.json"));
        var editor = viewModel.Editor;
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        editor.NodeShape = NodeShape.Cell;
        var up = editor.Puzzle.Nodes.First(n => n.Given is null && GridGeometry.IsUpTriangle(n.X, n.Y));
        var down = editor.Puzzle.Nodes.First(n => n.Given is null && !GridGeometry.IsUpTriangle(n.X, n.Y));
        editor.SelectNodes([up.Id, down.Id]);
        var frame = Capture(window, "zellen-dreieck.png");
        for (var vertex = 0; vertex < 3; vertex++)
        {
            CountSelectionBluePixels(frame, CellCorner(window, canvas, editor, up, vertex)).Should().BeGreaterThan(3, $"Ecke {vertex} des Auf-Dreiecks");
            CountSelectionBluePixels(frame, CellCorner(window, canvas, editor, down, vertex)).Should().BeGreaterThan(3, $"Ecke {vertex} des Ab-Dreiecks");
        }

        editor.ClearSelection();
        Capture(window, "zellen-dreieck-ohne-auswahl.png");
        editor.SelectEdge(editor.Puzzle.Edges[0].Id);
        Capture(window, "zellen-dreieck-kante.png");

        await viewModel.Solve.SolveCommand.ExecuteAsync(null);
        Capture(window, "zellen-dreieck-geloest.png");
        await viewModel.Explain.ExplainCommand.ExecuteAsync(null);
        var explain = viewModel.Explain;
        explain.Position = explain.StepCount / 3;
        Capture(window, "zellen-dreieck-erklaerung.png");
        var node = editor.Puzzle.FindNode(explain.Result!.Steps[explain.Position - 1].NodeIds[0])!;
        editor.Viewport.ZoomAt(editor.ScreenOf(new GridPoint(node.X, node.Y)), 2.5);
        Capture(window, "zellen-dreieck-erklaerung-zoom.png");
        window.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Hexadoku_renders_as_cells_with_hex_candidates() => fixture.Session.Dispatch(async () =>
    {
        var (window, viewModel) = OpenWindow(LoadExample("hexadoku-16x16.json"));
        var editor = viewModel.Editor;
        editor.NodeShape = NodeShape.Cell;
        Capture(window, "zellen-hexadoku.png");
        await viewModel.Explain.ExplainCommand.ExecuteAsync(null);
        var explain = viewModel.Explain;
        explain.IsActive.Should().BeTrue(explain.StatusMessage);
        explain.Position = 1;
        Capture(window, "zellen-hexadoku-erklaerung.png");
        var node = editor.Puzzle.FindNode(explain.Result!.Steps[0].NodeIds[0])!;
        editor.Viewport.ZoomAt(editor.ScreenOf(new GridPoint(node.X, node.Y)), 2.4);
        Capture(window, "zellen-hexadoku-erklaerung-zoom.png");
        window.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Hex_grid_edges_groups_and_sums_render_as_hexagons() => fixture.Session.Dispatch(() =>
    {
        // Sechseckgitter (kein Beispielrätsel nutzt es): Kanten mit Marken, Summen-Käfig, Thermometer, Auswahl.
        var puzzle = new Puzzle("Waben", GridType.Hex, new ValueDomain(1, 9));
        for (var r = -2; r <= 2; r++)
        {
            for (var q = -2; q <= 2; q++)
            {
                if (Math.Abs(q + r) <= 2)
                {
                    puzzle.Nodes.Add(new Node(puzzle.NextNodeId(), q, r, given: (q + 5 + r) % 4 == 0 ? ((q + 7) % 9) + 1 : null));
                }
            }
        }

        int Id(int q, int r) => puzzle.Nodes.Single(n => n.X == q && n.Y == r).Id;
        puzzle.Edges.Add(new Edge(1, Id(-2, 0), Id(-1, 0), "Difference", new RuleParams().Set("k", 1)));
        puzzle.Edges.Add(new Edge(2, Id(-1, 0), Id(0, 0), "Ratio", new RuleParams().Set("factor", 2)));
        puzzle.Edges.Add(new Edge(3, Id(0, 0), Id(1, 0), "LessThan"));
        puzzle.Edges.Add(new Edge(4, Id(1, 0), Id(2, 0), "NotEqual"));
        puzzle.Edges.Add(new Edge(5, Id(0, -2), Id(0, 0), "Difference", new RuleParams().Set("k", 3)));
        puzzle.Groups.Add(new Group(1, [Id(-1, -1), Id(0, -1), Id(1, -1)], "Sum", new RuleParams().Set("target", 17), color: "#2563eb"));
        puzzle.Groups.Add(new Group(2, [Id(-1, 2), Id(0, 1), Id(1, 0), Id(2, -1)], "Thermometer", color: "#dc2626"));

        var (window, viewModel) = OpenWindow(puzzle);
        var editor = viewModel.Editor;
        editor.NodeShape = NodeShape.Cell;
        editor.SelectNodes([Id(-2, 2)]);
        var frame = Capture(window, "zellen-sechseck.png");
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        for (var vertex = 0; vertex < 6; vertex++)
        {
            CountSelectionBluePixels(frame, CellCorner(window, canvas, editor, editor.Puzzle.FindNode(Id(-2, 2))!, vertex)).Should().BeGreaterThan(3, $"Ecke {vertex} der Wabe");
        }

        editor.SelectEdge(3);
        Capture(window, "zellen-sechseck-kante.png");
        editor.SelectGroup(2);
        Capture(window, "zellen-sechseck-gruppe.png");
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Contradiction_branch_and_edge_rule_render_as_cells() => fixture.Session.Dispatch(async () =>
    {
        // Widerspruch: Zelle gelb (aktueller Schritt) mit rotem Rahmen innen.
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create(ExplainViewModelTests.Conflicting));
        viewModel.Editor.NodeShape = NodeShape.Cell;
        await viewModel.Explain.ExplainCommand.ExecuteAsync(null);
        viewModel.Explain.LastCommand.Execute(null);
        var view = viewModel.Editor.Explanation!;
        var emptyNode = viewModel.Editor.Puzzle.FindNode(view.EmptyNodeIds.First())!;
        var frame = Capture(window, "zellen-erklaerung-widerspruch.png");
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        CountPixels(frame, NodeArea(window, canvas, viewModel.Editor, emptyNode, 1.3), (r, g, b) => r > 180 && g < 90 && b < 90)
            .Should().BeGreaterThan(20, "die Zelle ohne Kandidaten hat einen roten Rahmen");
        CountPixels(frame, CellCorner(window, canvas, viewModel.Editor, emptyNode, 2), (r, g, b) => r > 240 && g > 225 && b < 170)
            .Should().BeGreaterThan(3, "die gelbe Hervorhebung bleibt");
        window.Close();

        // Verzweigung: orange Zelle mit Marke „Raten“.
        var (branchWindow, branchModel) = OpenWindow(LoadExample("klassisch-mittel.json"));
        branchModel.Editor.NodeShape = NodeShape.Cell;
        await branchModel.Explain.ExplainCommand.ExecuteAsync(null);
        var branch = branchModel.Explain.Result!.Steps.First(s => s.Kind == StepKind.Branch);
        branchModel.Explain.Position = branch.Index + 1;
        Capture(branchWindow, "zellen-erklaerung-verzweigung.png");
        branchWindow.Close();

        // Regel einer Kante: grüner Steg über dem gemeinsamen Rand, Endzellen grün bzw. gelb.
        var (chainWindow, chainModel) = OpenWindow(ExplainViewModelTests.LessThanChain());
        chainModel.Editor.NodeShape = NodeShape.Cell;
        await chainModel.Explain.ExplainCommand.ExecuteAsync(null);
        var edgeStep = chainModel.Explain.Result!.Steps.First(s => s.Constraint is { Kind: ConstraintKind.Edge });
        chainModel.Explain.Position = edgeStep.Index + 1;
        Capture(chainWindow, "zellen-erklaerung-kante.png");
        chainWindow.Close();
        return true;
    }, CancellationToken.None);

    [Theory]
    [MemberData(nameof(SolveIntegrationTests.ExamplePuzzles), MemberType = typeof(SolveIntegrationTests))]
    public Task Every_example_puzzle_renders_as_cells(string fileName) => fixture.Session.Dispatch(async () =>
    {
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        viewModel.Editor.NodeShape = NodeShape.Cell;
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        (await viewModel.OpenFileAsync(Path.Combine(MainWindowViewModelTests.FindRepositoryRoot(), "puzzles", fileName))).Should().BeTrue();
        viewModel.Editor.NodeShape.Should().Be(NodeShape.Cell, "die Darstellung bleibt beim Öffnen erhalten");
        Dispatcher.UIThread.RunJobs();
        Capture(window, $"zellen-beispiel-{Path.GetFileNameWithoutExtension(fileName)}.png").PixelSize.Width.Should().BeGreaterThan(0);
        window.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Node_shape_switch_sits_in_the_status_bar_and_the_toolbar_stays_on_one_line() => fixture.Session.Dispatch(() =>
    {
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create(Givens));
        var cells = ShapeRadio(window, "Zellen");
        var circles = ShapeRadio(window, "Kreise");
        cells.IsEffectivelyVisible.Should().BeTrue();
        circles.IsChecked.Should().BeTrue();
        var einpassen = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Einpassen")).TranslatePoint(default, window)!.Value;
        var firstTool = window.GetVisualDescendants().OfType<RadioButton>().First(b => Equals(b.Content, "Auswahl")).TranslatePoint(default, window)!.Value;
        einpassen.Y.Should().BeApproximately(firstTool.Y, 4, "bei 1280 px passt die Werkzeugleiste weiterhin in eine Zeile");
        var position = cells.TranslatePoint(new Point(cells.Bounds.Width, cells.Bounds.Height), window)!.Value;
        position.X.Should().BeLessThanOrEqualTo(window.Bounds.Width);
        position.Y.Should().BeGreaterThan(window.Bounds.Height * 0.8, "der Umschalter liegt unten im Statusbereich");

        cells.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        viewModel.Editor.NodeShape.Should().Be(NodeShape.Cell);
        circles.IsChecked.Should().BeFalse();
        viewModel.Editor.ToggleNodeShapeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        circles.IsChecked.Should().BeTrue("die Anzeige folgt dem ViewModel");
        cells.IsChecked.Should().BeFalse();

        // Mindestgröße: Umschalter bleibt sichtbar und im Fenster.
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Dispatcher.UIThread.RunJobs();
        Capture(window, "zellen-umschalter-mindestgroesse.png");
        cells.TranslatePoint(new Point(cells.Bounds.Width, 0), window)!.Value.X.Should().BeLessThanOrEqualTo(window.Bounds.Width);
        window.Close();
    }, CancellationToken.None);

    private static RadioButton ShapeRadio(Window window, string text) =>
        window.FindControl<StackPanel>("NodeShapeBar")!.GetVisualDescendants().OfType<RadioButton>().Single(b => Equals(b.Content, text));

    /// <summary>Kleiner Bereich innerhalb der Zelle bei Ecke <paramref name="vertex"/> (72 % des Wegs von der Mitte zur Ecke, außerhalb des Knotenkreises).</summary>
    private static PixelRect CellCorner(Window window, Control canvas, EditorViewModel editor, Node node, int vertex)
    {
        var center = EditorGeometry.Center(editor.Puzzle.Grid, node);
        var corner = EditorGeometry.CellVertices(editor.Puzzle.Grid, node)[vertex];
        var world = new WorldPoint(center.X + ((corner.X - center.X) * 0.72), center.Y + ((corner.Y - center.Y) * 0.72));
        var screen = editor.Viewport.ToScreen(world);
        var point = canvas.TranslatePoint(new Point(screen.X, screen.Y), window)!.Value;
        return new PixelRect((int)point.X - 2, (int)point.Y - 2, 4, 4);
    }

    /// <summary>Helles Blau der Auswahl (#d6e6ff, auch leicht überlagert).</summary>
    private static int CountSelectionBluePixels(WriteableBitmap bitmap, PixelRect area) =>
        CountPixels(bitmap, area, (r, g, b) => b > 240 && r is > 190 and < 230 && g is > 210 and < 245);
}
