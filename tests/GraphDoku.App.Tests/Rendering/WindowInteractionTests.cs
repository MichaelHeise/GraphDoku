using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GraphDoku.App.Controls;
using GraphDoku.App.Editing;
using GraphDoku.App.Services;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.App.Views;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests.Rendering;

/// <summary>
/// Verhalten im echten (headless) Hauptfenster, das reine ViewModel-Tests nicht sehen: Wiederverwendung der
/// Panel-Vorlagen beim Wechsel der Auswahl und Tastatureingaben, wenn ein Panel-Element den Fokus hat.
/// </summary>
[Collection(HeadlessTests.Name)]
public sealed class WindowInteractionTests(HeadlessSessionFixture fixture)
{
    [Fact]
    public Task Switching_between_groups_and_edges_creates_no_undo_steps() => fixture.Session.Dispatch(() =>
    {
        var puzzle = ClassicSudoku.Create();
        puzzle.Groups[1].Color = "#dc2626";
        puzzle.Groups[2].Rule = "Sum";
        puzzle.Groups[2].Params.Set("target", 45);
        puzzle.Groups[3].Color = "#123456"; // eigene Farbe außerhalb der Palette
        puzzle.Groups[4].Rule = "Thermometer";
        puzzle.Edges.Add(new Edge(1, 1, 2, "Difference", new RuleParams().Set("k", 3).Set("mode", "atLeast")));
        puzzle.Edges.Add(new Edge(2, 2, 3, "Difference", new RuleParams().Set("k", 1)));
        puzzle.Edges.Add(new Edge(3, 3, 4, "Ratio", new RuleParams().Set("factor", 3)));
        puzzle.Edges.Add(new Edge(4, 4, 5, "LessThan"));
        puzzle.Edges.Add(new Edge(5, 5, 6, "NotEqual"));
        var reference = puzzle.Clone();

        var (window, viewModel) = OpenWindow(puzzle);
        var editor = viewModel.Editor;
        foreach (var groupId in new[] { 1, 2, 3, 1, 4, 2, 5, 4, 3, 5, 1 })
        {
            editor.SelectGroup(groupId);
            Dispatcher.UIThread.RunJobs();
        }

        foreach (var edgeId in new[] { 1, 2, 1, 3, 4, 1, 5, 2, 3 })
        {
            editor.SelectEdge(edgeId);
            Dispatcher.UIThread.RunJobs();
        }

        editor.SelectGroup(4);
        Dispatcher.UIThread.RunJobs();
        editor.SelectEdge(1);
        Dispatcher.UIThread.RunJobs();
        editor.ClearSelection();
        Dispatcher.UIThread.RunJobs();

        editor.History.UndoCount.Should().Be(0);
        editor.IsDirty.Should().BeFalse();
        viewModel.Title.Should().NotContain("*");
        PuzzleComparer.Differences(reference, editor.Puzzle).Should().BeEmpty();
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Choosing_a_color_in_the_window_is_one_undo_step() => fixture.Session.Dispatch(() =>
    {
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create());
        var editor = viewModel.Editor;
        editor.SelectGroup(3);
        Dispatcher.UIThread.RunJobs();
        var panel = editor.Properties.Should().BeOfType<GroupPropertiesViewModel>().Subject;
        panel.SelectedColor = panel.ColorOptions.First(c => c.Hex == "#16a34a");
        Dispatcher.UIThread.RunJobs();
        editor.SelectGroup(4);
        Dispatcher.UIThread.RunJobs();
        editor.SelectGroup(3);
        Dispatcher.UIThread.RunJobs();

        editor.History.UndoCount.Should().Be(1);
        editor.Puzzle.FindGroup(3)!.Color.Should().Be("#16a34a");
        editor.Puzzle.FindGroup(4)!.Color.Should().BeNull();
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Delete_key_deletes_group_selected_in_list_while_list_has_focus() => fixture.Session.Dispatch(() =>
    {
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create());
        var list = window.GetVisualDescendants().OfType<ListBox>().First();
        list.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        list.ContainerFromIndex(0)!.Focus();
        Dispatcher.UIThread.RunJobs();
        viewModel.Editor.SelectedGroupId.Should().Be(1);

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        Dispatcher.UIThread.RunJobs();

        viewModel.Editor.Puzzle.Groups.Should().HaveCount(26);
        viewModel.Editor.Puzzle.FindGroup(1).Should().BeNull();
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Digits_reach_selected_nodes_after_clicking_a_panel_button() => fixture.Session.Dispatch(() =>
    {
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create());
        viewModel.Editor.SelectNodes([ClassicSudoku.NodeId(0, 0)]);
        Dispatcher.UIThread.RunJobs();
        var button = window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Gruppe erstellen"));
        button.Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.D7, RawInputModifiers.None, PhysicalKey.Digit7, "7");
        Dispatcher.UIThread.RunJobs();

        viewModel.Editor.Puzzle.FindNode(ClassicSudoku.NodeId(0, 0))!.Given.Should().Be(7);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Edit_keys_typed_into_a_text_box_stay_in_the_text_box() => fixture.Session.Dispatch(() =>
    {
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create());
        viewModel.Editor.SelectGroup(1);
        Dispatcher.UIThread.RunJobs();
        var textBox = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.PlaceholderText == "z. B. Zeile 1");
        textBox.Focus().Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        window.FocusManager!.GetFocusedElement().Should().BeOfType<TextBox>();

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        window.KeyPress(Key.D3, RawInputModifiers.None, PhysicalKey.Digit3, "3");
        Dispatcher.UIThread.RunJobs();

        viewModel.Editor.Puzzle.Groups.Should().HaveCount(27);
        viewModel.Editor.Puzzle.Nodes.Should().OnlyContain(n => n.Given == null);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Canvas_keys_still_work_when_canvas_has_focus() => fixture.Session.Dispatch(() =>
    {
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create());
        viewModel.Editor.SelectNodes([ClassicSudoku.NodeId(1, 1)]);
        window.FindControl<GraphCanvas>("Canvas")!.Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.D4, RawInputModifiers.None, PhysicalKey.Digit4, "4");
        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
        window.KeyPress(Key.D5, RawInputModifiers.None, PhysicalKey.Digit5, "5");
        Dispatcher.UIThread.RunJobs();

        viewModel.Editor.Puzzle.FindNode(ClassicSudoku.NodeId(1, 1))!.Given.Should().Be(5);
        viewModel.Editor.History.UndoCount.Should().Be(3);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Space_released_after_focus_left_the_canvas_does_not_keep_panning() => fixture.Session.Dispatch(() =>
    {
        // Review-Befund (Nr. 168): Das Loslassen der Leertaste ging an die fokussierte Schaltfläche, danach verschob
        // jeder Linksklick auf der Zeichenfläche die Ansicht, statt auszuwählen.
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create());
        var editor = viewModel.Editor;
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        canvas.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Einpassen")).Focus().Should().BeTrue();
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();

        var screen = editor.Viewport.ToScreen(GridGeometry.ToWorld(GridType.Square, 0, 0));
        var point = canvas.TranslatePoint(new Point(screen.X, screen.Y), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        editor.IsPanning.Should().BeFalse();
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        editor.SelectedNodeIds.Should().Equal(ClassicSudoku.NodeId(0, 0));
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Right_click_during_an_edge_drag_on_the_canvas_does_not_finish_it() => fixture.Session.Dispatch(() =>
    {
        // Review-Befund (nicht nachvollziehbar): Ein Rechtsklick während des Kantenziehens schließe die Geste ab, weil
        // PointerReleased die Taste nicht prüft. Avalonia meldet eine weitere Taste bei schon gedrückter linker Taste aber
        // nicht als Drücken/Loslassen, sondern als Zeigerbewegung – das Loslassen erreicht den Editor erst mit der letzten
        // Taste. Der Test sichert dieses Verhalten der Zeichenfläche ab.
        var puzzle = new Puzzle("Reihe", GridType.Square);
        puzzle.Nodes.Add(new Node(1, 0, 0));
        puzzle.Nodes.Add(new Node(2, 2, 0));
        puzzle.Nodes.Add(new Node(3, 4, 0));
        var (window, viewModel) = OpenWindow(puzzle);
        var editor = viewModel.Editor;
        editor.Tool = EditorTool.DrawEdge;
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        Point OnWindow(int x)
        {
            var screen = editor.Viewport.ToScreen(GridGeometry.ToWorld(GridType.Square, x, 0));
            return canvas.TranslatePoint(new Point(screen.X, screen.Y), window)!.Value;
        }

        window.MouseDown(OnWindow(0), MouseButton.Left);
        window.MouseMove(OnWindow(2), RawInputModifiers.LeftMouseButton);
        window.MouseDown(OnWindow(2), MouseButton.Right, RawInputModifiers.LeftMouseButton);
        window.MouseUp(OnWindow(2), MouseButton.Right, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        editor.Puzzle.Edges.Should().BeEmpty();
        editor.IsGestureActive.Should().BeTrue();
        window.MouseMove(OnWindow(4), RawInputModifiers.LeftMouseButton);
        window.MouseUp(OnWindow(4), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        editor.Puzzle.Edges.Should().ContainSingle().Which.Should().BeEquivalentTo(new { From = 1, To = 3 });
        editor.IsGestureActive.Should().BeFalse();
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Clicking_the_part_of_a_labelled_dot_over_a_node_selects_the_edge() => fixture.Session.Dispatch(() =>
    {
        // Review-Befund: Beim Standardmaßstab 48 ist ein beschrifteter Kropki-Punkt (k = 2, Radius ≈ 8,5 px) breiter als
        // der Spalt von 11,5 px zwischen den Kreisen. Er liegt über den Knoten, ein Klick dort wählte aber den Knoten.
        var puzzle = new Puzzle("Paar", GridType.Square);
        puzzle.Nodes.Add(new Node(1, 0, 0));
        puzzle.Nodes.Add(new Node(2, 1, 0));
        puzzle.Edges.Add(new Edge(1, 1, 2, "Difference", new RuleParams().Set("k", 2)));
        var (window, viewModel) = OpenWindow(puzzle);
        var editor = viewModel.Editor;
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        editor.Viewport.ZoomAt(new ScreenPoint(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), Viewport.DefaultScale / editor.Viewport.Scale);
        Dispatcher.UIThread.RunJobs();
        var nodeRadius = EditorGeometry.NodeRadius(GridType.Square) * editor.Viewport.Scale;
        var a = editor.Viewport.ToScreen(GridGeometry.ToWorld(GridType.Square, 0, 0));
        var screen = new ScreenPoint(a.X + nodeRadius - 1.5, a.Y);
        editor.HitNode(editor.Viewport.ToWorld(screen))!.Id.Should().Be(1);
        var point = canvas.TranslatePoint(new Point(screen.X, screen.Y), window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        editor.SelectedEdgeId.Should().Be(1);
        editor.SelectedNodeIds.Should().BeEmpty();
        window.Close();
    }, CancellationToken.None);

    [Theory]
    [InlineData(0, UnsavedChangesChoice.Save)]
    [InlineData(1, UnsavedChangesChoice.Discard)]
    [InlineData(2, UnsavedChangesChoice.Cancel)]
    [InlineData(-1, UnsavedChangesChoice.Cancel)]
    public Task Unsaved_changes_dialog_maps_buttons_and_closing_without_a_button_cancels(int button, UnsavedChangesChoice expected) =>
        fixture.Session.Dispatch(async () =>
        {
            // Review-Befund (Nr. 165): Schließen über die Titelleiste (Cmd+W, Alt+F4) lieferte default(int) = 0 und
            // galt damit als „Speichern“ – die Datei wurde überschrieben und Neu/Öffnen/Beenden ging weiter.
            var owner = new Window { Width = 400, Height = 300 };
            owner.Show();
            Dispatcher.UIThread.RunJobs();
            var choiceTask = new AvaloniaDialogService(owner).AskUnsavedChangesAsync("x.json");
            Dispatcher.UIThread.RunJobs();
            var dialog = owner.OwnedWindows.OfType<MessageWindow>().Single();
            if (button < 0)
            {
                dialog.Close();
            }
            else
            {
                dialog.GetVisualDescendants().OfType<Button>().ElementAt(button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            Dispatcher.UIThread.RunJobs();
            choiceTask.IsCompleted.Should().BeTrue();
            (await choiceTask).Should().Be(expected);
            owner.Close();
            return true;
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
}
