using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GraphDoku.App.Editing;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.App.Views;
using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests.Rendering;

/// <summary>Paket 7 im echten (headless) Hauptfenster: Menü Ansicht → „Knoten als Kreise“ / „Knoten als Zellen (…)“.</summary>
[Collection(HeadlessTests.Name)]
public sealed class NodeShapeWindowTests(HeadlessSessionFixture fixture)
{
    [Fact]
    public Task View_menu_switches_between_circles_and_cells() => fixture.Session.Dispatch(() =>
    {
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        viewModel.Editor.LoadPuzzle(ClassicSudoku.Create());
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var top = window.GetVisualDescendants().OfType<MenuItem>().Single(m => Equals(m.Header, "_Ansicht") && m.Parent is Menu);
        var circles = top.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Knoten als Kreise"));
        var cells = top.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Knoten als Zellen (Quadrate)"));
        circles.IsChecked.Should().BeTrue();
        cells.IsChecked.Should().BeFalse();

        void Choose(MenuItem item)
        {
            top.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            item.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
        }

        // Der Menüeintrag muss die Einstellung im ViewModel ändern, nicht nur sein eigenes Häkchen (Nr. 114).
        Choose(cells);
        viewModel.Editor.NodeShape.Should().Be(NodeShape.Cell);
        cells.IsChecked.Should().BeTrue();
        circles.IsChecked.Should().BeFalse();
        viewModel.Editor.IsDirty.Should().BeFalse();

        Choose(circles);
        viewModel.Editor.NodeShape.Should().Be(NodeShape.Circle);
        circles.IsChecked.Should().BeTrue();
        cells.IsChecked.Should().BeFalse();

        // Umgekehrt folgt das Menü dem ViewModel, und der Text nennt die Zellform des geladenen Gitters.
        viewModel.Editor.LoadPuzzle(new Puzzle("Waben", GridType.Hex, new ValueDomain(1, 7)));
        viewModel.Editor.NodeShape = NodeShape.Cell;
        Dispatcher.UIThread.RunJobs();
        cells.Header.Should().Be("Knoten als Zellen (Sechsecke)");
        cells.IsChecked.Should().BeTrue();
        circles.IsChecked.Should().BeFalse();
        window.Close();
    }, CancellationToken.None);

    /// <summary>Review-Befund: Der Umschalter „Knoten als ○ Kreise ○ Zellen“ im Statusbereich, mit der Maus bedient.</summary>
    [Fact]
    public Task Status_bar_switch_changes_the_display_by_mouse() => fixture.Session.Dispatch(() =>
    {
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        viewModel.Editor.LoadPuzzle(ClassicSudoku.Create());
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var bar = window.FindControl<StackPanel>("NodeShapeBar")!;
        var circles = bar.Children.OfType<RadioButton>().Single(b => Equals(b.Content, "Kreise"));
        var cells = bar.Children.OfType<RadioButton>().Single(b => Equals(b.Content, "Zellen"));
        circles.IsChecked.Should().BeTrue();

        void ClickOn(Control control)
        {
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        ClickOn(cells);
        viewModel.Editor.NodeShape.Should().Be(NodeShape.Cell);
        cells.IsChecked.Should().BeTrue();
        circles.IsChecked.Should().BeFalse();
        viewModel.Editor.IsDirty.Should().BeFalse();
        viewModel.Editor.ToolHint.Should().Contain("Strg/Cmd + Ziehen", "der Hinweis nennt den Rahmen über Zellen");

        ClickOn(circles);
        viewModel.Editor.NodeShape.Should().Be(NodeShape.Circle);
        circles.IsChecked.Should().BeTrue();
        cells.IsChecked.Should().BeFalse();

        // Umgekehrt folgt der Umschalter dem ViewModel (z. B. nach Ansicht → Knoten als Zellen).
        viewModel.Editor.NodeShape = NodeShape.Cell;
        Dispatcher.UIThread.RunJobs();
        cells.IsChecked.Should().BeTrue();
        circles.IsChecked.Should().BeFalse();
        window.Close();
    }, CancellationToken.None);

    /// <summary>Strg + Ziehen auf der Zeichenfläche (echte Zeigerereignisse) zieht in der Zelldarstellung einen Rahmen (Nr. 159).</summary>
    [Fact]
    public Task Control_drag_on_the_canvas_selects_cells_with_a_rubber_band() => fixture.Session.Dispatch(() =>
    {
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        viewModel.Editor.LoadPuzzle(ClassicSudoku.Create());
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = viewModel.Editor;
        editor.NodeShape = NodeShape.Cell;
        var canvas = window.FindControl<GraphDoku.App.Controls.GraphCanvas>("Canvas")!;
        Point OnWindow(double x, double y)
        {
            var screen = editor.Viewport.ToScreen(new Core.Geometry.WorldPoint(x, y));
            return canvas.TranslatePoint(new Point(screen.X, screen.Y), window)!.Value;
        }

        var start = OnWindow(2.7, 2.7);
        var end = OnWindow(5.3, 5.3);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.Control);
        for (var i = 1; i <= 8; i++)
        {
            window.MouseMove(new Point(start.X + ((end.X - start.X) * i / 8), start.Y + ((end.Y - start.Y) * i / 8)), RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
        }

        window.MouseUp(end, MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        editor.SelectedNodeIds.Should().HaveCount(9);
        editor.Puzzle.Nodes.Where(n => editor.SelectedNodeIds.Contains(n.Id)).Should().OnlyContain(n => n.X >= 3 && n.X <= 5 && n.Y >= 3 && n.Y <= 5);
        editor.CanUndo.Should().BeFalse();
        window.Close();
    }, CancellationToken.None);
}
