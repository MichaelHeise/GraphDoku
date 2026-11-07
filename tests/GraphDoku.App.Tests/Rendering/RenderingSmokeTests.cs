using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GraphDoku.App.Controls;
using GraphDoku.App.Editing;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.App.Views;
using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;
using GraphDoku.Solver.OrTools;
using Xunit.Abstractions;

namespace GraphDoku.App.Tests.Rendering;

/// <summary>
/// Rendering-Rauchtests: Hauptfenster mit geladenem Rätsel headless rendern, als PNG ablegen
/// (Verzeichnis: Umgebungsvariable GRAPHDOKU_RENDER_DIR, sonst Temp/GraphDoku-Render) und einfache Pixelprüfungen.
/// </summary>
[Collection(HeadlessTests.Name)]
public sealed class RenderingSmokeTests(HeadlessSessionFixture fixture, ITestOutputHelper output)
{
    private const string Givens =
        "53..7...." + "6..195..." + ".98....6." + "8...6...3" + "4..8.3..1" + "7...2...6" + ".6....28." + "...419..5" + "....8..79";

    private static string OutputDirectory
    {
        get
        {
            var directory = Environment.GetEnvironmentVariable("GRAPHDOKU_RENDER_DIR") is { Length: > 0 } configured
                ? configured
                : Path.Combine(Path.GetTempPath(), "GraphDoku-Render");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    [Fact]
    public Task Main_window_renders_classic_sudoku_with_givens() => fixture.Session.Dispatch(() =>
    {
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create(Givens));
        var frame = Capture(window, "klassisch.png");

        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        canvas.Bounds.Width.Should().BeGreaterThan(300);
        canvas.Bounds.Height.Should().BeGreaterThan(300);

        // In der Mitte eines Knotens mit Given muss dunkle Schrift liegen, bei einem leeren Knoten nicht.
        var withGiven = viewModel.Editor.Puzzle.FindNode(ClassicSudoku.NodeId(0, 0))!;
        var empty = viewModel.Editor.Puzzle.FindNode(ClassicSudoku.NodeId(0, 2))!;
        CountDarkPixels(frame, NodeArea(window, canvas, viewModel.Editor, withGiven)).Should().BeGreaterThan(10);
        CountDarkPixels(frame, NodeArea(window, canvas, viewModel.Editor, empty, 0.5)).Should().Be(0);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Edges_groups_and_selection_render_on_all_grid_types() => fixture.Session.Dispatch(() =>
    {
        foreach (var grid in Enum.GetValues<GridType>())
        {
            var puzzle = new Puzzle($"Probe {grid}", grid, new ValueDomain(1, 9));
            var id = 1;
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 5; x++)
                {
                    puzzle.Nodes.Add(new Node(id, x, y, given: id % 3 == 0 ? (id % 9) + 1 : null));
                    id++;
                }
            }

            puzzle.Edges.Add(new Edge(1, 1, 2, "NotEqual"));
            puzzle.Edges.Add(new Edge(2, 2, 3, "Difference", new RuleParams().Set("k", 1)));
            puzzle.Edges.Add(new Edge(3, 3, 4, "Ratio", new RuleParams().Set("factor", 2)));
            puzzle.Edges.Add(new Edge(4, 4, 5, "LessThan"));
            puzzle.Edges.Add(new Edge(5, 6, 7, "Difference", new RuleParams().Set("k", 3).Set("mode", "atLeast")));
            puzzle.Groups.Add(new Group(1, [11, 12, 16, 17], "Sum", new RuleParams().Set("target", 12), label: "Käfig"));
            puzzle.Groups.Add(new Group(2, [8, 13, 18, 19], "Thermometer", color: "#dc2626"));

            var (window, viewModel) = OpenWindow(puzzle);
            viewModel.Editor.SelectNodes([6, 7, 1]);
            var frame = Capture(window, $"gitter-{GridNames.ToGerman(grid).ToLowerInvariant()}.png");
            frame.PixelSize.Width.Should().BeGreaterThan(0);

            viewModel.Editor.SelectGroup(2);
            Capture(window, $"gitter-{GridNames.ToGerman(grid).ToLowerInvariant()}-gruppe.png");
            viewModel.Editor.SelectEdge(5);
            viewModel.Editor.Properties.Should().BeOfType<EdgePropertiesViewModel>();
            Capture(window, $"gitter-{GridNames.ToGerman(grid).ToLowerInvariant()}-kante.png");
            window.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public Task Toolbar_wraps_at_minimum_window_size_instead_of_cutting_off_buttons() => fixture.Session.Dispatch(() =>
    {
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create(Givens));
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        viewModel.Editor.SelectGroup(20);
        Dispatcher.UIThread.RunJobs();
        Capture(window, "mindestgroesse.png");

        foreach (var text in new[] { "Rückgängig", "Wiederholen", "Einpassen" })
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, text));
            var bounds = button.TransformToVisual(window)!.Value.Transform(new Point(button.Bounds.Width, button.Bounds.Height));
            bounds.X.Should().BeLessThanOrEqualTo(window.Bounds.Width, text);
            bounds.Y.Should().BeLessThan(window.Bounds.Height / 3, text);
        }

        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Classic_block_boundaries_are_drawn_stronger_than_cell_boundaries() => fixture.Session.Dispatch(() =>
    {
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create());
        var frame = Capture(window, "klassisch-leer.png");
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        var editor = viewModel.Editor;

        // Dunkle Pixel auf einer waagerechten Linie über die Grenze zwischen Spalte 1|2 (Zellgrenze) bzw. 3|4 (Blockgrenze).
        int DarkAcross(double worldX)
        {
            var screen = editor.Viewport.ToScreen(new Core.Geometry.WorldPoint(worldX, 4.0));
            var point = canvas.TranslatePoint(new Point(screen.X, screen.Y), window)!.Value;
            return Darkness(frame, new PixelRect((int)point.X - 6, (int)point.Y, 12, 1));
        }

        output.WriteLine($"Dunkelheit Zellgrenze {DarkAcross(0.5)}, Blockgrenze {DarkAcross(2.5)}");
        DarkAcross(2.5).Should().BeGreaterThan(DarkAcross(0.5) * 3 / 2);
        DarkAcross(5.5).Should().BeGreaterThan(DarkAcross(4.5) * 3 / 2);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Solved_puzzle_shows_solution_values_distinct_from_givens() => fixture.Session.Dispatch(async () =>
    {
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create(Givens));
        await viewModel.Solve.SolveCommand.ExecuteAsync(null);
        viewModel.Solve.Outcome.Should().Be(SolveOutcome.Solved, viewModel.Solve.ResultMessage);
        var frame = Capture(window, "klassisch-geloest.png");

        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        var editor = viewModel.Editor;
        var given = editor.Puzzle.FindNode(ClassicSudoku.NodeId(0, 0))!;
        var solved = editor.Puzzle.FindNode(ClassicSudoku.NodeId(0, 2))!;

        // Lösungswert blau, Given schwarz.
        CountSolutionBluePixels(frame, NodeArea(window, canvas, editor, solved)).Should().BeGreaterThan(10);
        CountDarkPixels(frame, NodeArea(window, canvas, editor, solved)).Should().Be(0);
        CountSolutionBluePixels(frame, NodeArea(window, canvas, editor, given)).Should().Be(0);
        CountDarkPixels(frame, NodeArea(window, canvas, editor, given)).Should().BeGreaterThan(10);

        // Nach einer Bearbeitung ist die Lösung weg.
        editor.SelectNodes([ClassicSudoku.NodeId(8, 0)]);
        editor.EnterValueCharacter('3');
        editor.ClearSelection();
        var edited = Capture(window, "klassisch-geloest-bearbeitet.png");
        CountSolutionBluePixels(edited, NodeArea(window, canvas, editor, solved)).Should().Be(0);
        window.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Ambiguous_puzzle_marks_differing_nodes() => fixture.Session.Dispatch(async () =>
    {
        // Wie „leicht“, aber ohne die 6 in Zeile 3, Spalte 8: genau zwei Lösungen.
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create(Givens.Remove(25, 1).Insert(25, ".")));
        await viewModel.Solve.CheckUniquenessCommand.ExecuteAsync(null);
        viewModel.Solve.Outcome.Should().Be(SolveOutcome.Ambiguous, viewModel.Solve.ResultMessage);
        Capture(window, "klassisch-mehrdeutig.png");
        viewModel.Solve.ShowSecondSolutionCommand.Execute(null);
        var frame = Capture(window, "klassisch-mehrdeutig-loesung2.png");

        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        var differing = viewModel.Editor.Puzzle.FindNode(viewModel.Solve.SolutionDifferences[0])!;
        CountPixels(frame, NodeArea(window, canvas, viewModel.Editor, differing), (r, g, b) => r > 200 && g is > 60 and < 140 && b < 60)
            .Should().BeGreaterThan(10, "abweichende Lösungswerte sind orange");
        window.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Busy_and_error_states_render() => fixture.Session.Dispatch(async () =>
    {
        using var solver = new FakeSolver { Block = true };
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), solver);
        viewModel.Editor.LoadPuzzle(ClassicSudoku.Create(Givens));
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();

        var running = viewModel.Solve.SolveCommand.ExecuteAsync(null);
        solver.WaitUntilStarted();
        Capture(window, "loesen-laeuft.png");
        var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Abbrechen"));
        cancel.IsEffectivelyVisible.Should().BeTrue();
        cancel.IsEffectivelyEnabled.Should().BeTrue();
        viewModel.Solve.CancelCommand.Execute(null);
        await running;
        Capture(window, "loesen-abgebrochen.png");
        cancel.IsEffectivelyVisible.Should().BeFalse();

        solver.Block = false;
        solver.SolveResult = _ => GraphDoku.Solver.SolveResult.Invalid(["Gruppe 28: Der Parameter „target“ fehlt.", "Kante 3: Der Knoten 99 existiert nicht."], TimeSpan.Zero);
        await viewModel.Solve.SolveCommand.ExecuteAsync(null);
        Capture(window, "loesen-ungueltig.png");

        viewModel.Solve.TimeLimitText = "abc";
        Capture(window, "loesen-zeitlimit-fehler.png");
        window.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task New_puzzle_dialog_renders() => fixture.Session.Dispatch(() =>
    {
        var window = new NewPuzzleWindow { DataContext = new NewPuzzleViewModel { DomainMin = "5", DomainMax = "1" } };
        window.Show();
        var frame = Capture(window, "dialog-neu.png");
        frame.PixelSize.Height.Should().BeGreaterThan(100);
        window.Close();
    }, CancellationToken.None);

    private static (MainWindow Window, MainWindowViewModel ViewModel) OpenWindow(Puzzle puzzle)
    {
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        viewModel.Editor.LoadPuzzle(puzzle);
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    private WriteableBitmap Capture(Window window, string fileName)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Kein Bild gerendert.");
        var path = Path.Combine(OutputDirectory, fileName);
        frame.Save(path, PngBitmapEncoderOptions.Default);
        output.WriteLine($"Gerendert: {path}");
        return frame;
    }

    private static PixelRect NodeArea(Window window, Control canvas, EditorViewModel editor, Node node, double fraction = 0.6)
    {
        var center = editor.Viewport.ToScreen(EditorGeometry.Center(editor.Puzzle.Grid, node));
        var inWindow = canvas.TranslatePoint(new Point(center.X, center.Y), window)!.Value;
        var half = EditorGeometry.NodeRadius(editor.Puzzle.Grid) * editor.Viewport.Scale * fraction;
        return new PixelRect((int)(inWindow.X - half), (int)(inWindow.Y - half), (int)(2 * half), (int)(2 * half));
    }

    private static int CountDarkPixels(WriteableBitmap bitmap, PixelRect area)
    {
        using var buffer = bitmap.Lock();
        var count = 0;
        for (var y = Math.Max(0, area.Y); y < Math.Min(buffer.Size.Height, area.Bottom); y++)
        {
            for (var x = Math.Max(0, area.X); x < Math.Min(buffer.Size.Width, area.Right); x++)
            {
                var (r, g, b) = ReadPixel(buffer, x, y);
                if (r < 90 && g < 90 && b < 90)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Pixel in der Farbe der Lösungswerte (#1d4ed8, auch geglättet), aber nicht das helle Blau der Auswahl.</summary>
    private static int CountSolutionBluePixels(WriteableBitmap bitmap, PixelRect area) =>
        CountPixels(bitmap, area, (r, g, b) => b > 150 && r < 130 && b - r > 80);

    private static int CountPixels(WriteableBitmap bitmap, PixelRect area, Func<byte, byte, byte, bool> predicate)
    {
        using var buffer = bitmap.Lock();
        var count = 0;
        for (var y = Math.Max(0, area.Y); y < Math.Min(buffer.Size.Height, area.Bottom); y++)
        {
            for (var x = Math.Max(0, area.X); x < Math.Min(buffer.Size.Width, area.Right); x++)
            {
                var (r, g, b) = ReadPixel(buffer, x, y);
                if (predicate(r, g, b))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Summe der „Dunkelheit“ (255 − Helligkeit) im Bereich; robuster als ein fester Schwellwert bei geglätteten Linien.</summary>
    private static int Darkness(WriteableBitmap bitmap, PixelRect area)
    {
        using var buffer = bitmap.Lock();
        var sum = 0;
        for (var y = Math.Max(0, area.Y); y < Math.Min(buffer.Size.Height, area.Bottom); y++)
        {
            for (var x = Math.Max(0, area.X); x < Math.Min(buffer.Size.Width, area.Right); x++)
            {
                var (r, g, b) = ReadPixel(buffer, x, y);
                sum += 255 - ((r + g + b) / 3);
            }
        }

        return sum;
    }

    private static (byte R, byte G, byte B) ReadPixel(ILockedFramebuffer buffer, int x, int y)
    {
        var offset = (y * buffer.RowBytes) + (x * 4);
        var bytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + offset, bytes, 0, 4);
        return buffer.Format == PixelFormat.Rgba8888 ? (bytes[0], bytes[1], bytes[2]) : (bytes[2], bytes[1], bytes[0]);
    }
}
