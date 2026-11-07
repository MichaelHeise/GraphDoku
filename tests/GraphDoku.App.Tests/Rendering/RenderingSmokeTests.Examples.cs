using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GraphDoku.App.Controls;
using GraphDoku.App.Editing;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.App.Views;
using GraphDoku.Core.Generation;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests.Rendering;

/// <summary>
/// Rendering-Rauchtests zu Paket 5: jede Datei aus <c>puzzles/</c> über „Öffnen“ laden und rendern (die Bilder wurden
/// angesehen), Killer-Summen auf der Zeichenfläche, Dialog „Kanten erzeugen“ und eigener Wertebereich im Panel.
/// </summary>
public sealed partial class RenderingSmokeTests
{
    [Theory]
    [MemberData(nameof(SolveIntegrationTests.ExamplePuzzles), MemberType = typeof(SolveIntegrationTests))]
    public Task Every_example_puzzle_renders(string fileName) => fixture.Session.Dispatch(async () =>
    {
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        (await viewModel.OpenFileAsync(Path.Combine(MainWindowViewModelTests.FindRepositoryRoot(), "puzzles", fileName))).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        var frame = Capture(window, $"beispiel-{Path.GetFileNameWithoutExtension(fileName)}.png");
        frame.PixelSize.Width.Should().BeGreaterThan(0);
        viewModel.Editor.StatusMessage.Should().Contain(fileName, "die Statuszeile nennt die geöffnete Datei");
        window.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Killer_cage_sums_are_drawn_in_the_top_left_cell_of_each_cage() => fixture.Session.Dispatch(() =>
    {
        var (window, viewModel) = OpenWindow(LoadExample("killer-sudoku.json"));
        var frame = Capture(window, "killer-summen.png");
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        var editor = viewModel.Editor;

        // Linke obere Ecke einer Zelle (innerhalb der Gruppenlinien, außerhalb des Knotenkreises).
        PixelRect Corner(Node node)
        {
            var corner = editor.Viewport.ToScreen(new WorldPoint(node.X - 0.5, node.Y - 0.5));
            var point = canvas.TranslatePoint(new Point(corner.X, corner.Y), window)!.Value;
            var size = (int)(editor.Viewport.Scale * 0.2);
            return new PixelRect((int)point.X + 3, (int)point.Y + 3, size, size - 2);
        }

        var cages = editor.Puzzle.Groups.Where(g => g.Rule == "Sum").ToList();
        cages.Should().HaveCount(28);
        foreach (var cage in cages.Take(6))
        {
            var members = cage.Nodes.Select(id => editor.Puzzle.FindNode(id)!).ToList();
            var anchor = EditorGeometry.RowWiseOrder(GridType.Square, members).First();
            var other = members.First(n => n != anchor);
            var withSum = CountDarkPixels(frame, Corner(anchor));
            var without = CountDarkPixels(frame, Corner(other));
            output.WriteLine($"Käfig {cage.Id}: Ecke mit Summe {withSum}, ohne {without}");
            withSum.Should().BeGreaterThan(8, $"Käfig {cage.Id} zeigt seine Summe");
            without.Should().BeLessThan(withSum / 2, $"nur die erste Zelle von Käfig {cage.Id} trägt die Summe");
        }

        // Zoom auf die linke obere Ecke, um die Lesbarkeit zu prüfen.
        editor.Viewport.ZoomAt(editor.ScreenOf(new GridPoint(0, 0)), 2.2);
        Capture(window, "killer-summen-zoom.png");

        // Ausgeblendete Gruppen zeigen keine Summe.
        editor.HideAllGroupsCommand.Execute(null);
        Capture(window, "killer-summen-ausgeblendet.png");
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Edge_generator_dialog_renders_preview_and_errors() => fixture.Session.Dispatch(() =>
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());
        editor.SelectNodes([1, 2, 3, 10, 11, 12]);
        var generator = new EdgeGeneratorViewModel(editor);
        var window = new EdgeGeneratorWindow { DataContext = generator };
        window.Show();
        Capture(window, "generator-auswahl.png");
        window.FindControl<Button>("ApplyButton")!.IsEffectivelyEnabled.Should().BeTrue();

        generator.SelectedRule = editor.EdgeRules.Single(r => r.TypeName == "LessThan");
        generator.UseAllNodes = true;
        Capture(window, "generator-kleiner-als.png");

        generator.SelectedRule = editor.EdgeRules.Single(r => r.TypeName == "Difference");
        generator.SelectedDistance = generator.DistanceOptions.Single(o => o.Kind == DistanceKind.Free);
        generator.MinDistanceText = "0";
        generator.Parameters.Single(p => p.Name == "k").Text = string.Empty;
        Dispatcher.UIThread.RunJobs();
        Capture(window, "generator-fehler.png");
        generator.CanApply.Should().BeFalse();
        window.FindControl<Button>("ApplyButton")!.IsEffectivelyEnabled.Should().BeFalse();
        window.Close();

        var triangle = new Puzzle("Dreieck", GridType.Triangle, new ValueDomain(1, 9));
        triangle.Nodes.Add(new Node(1, 0, 0));
        triangle.Nodes.Add(new Node(2, 1, 0));
        var triangleWindow = new EdgeGeneratorWindow { DataContext = new EdgeGeneratorViewModel(EditorDriver.CreateEditor(triangle)) };
        triangleWindow.Show();
        Capture(triangleWindow, "generator-dreieck.png");
        triangleWindow.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Selection_panel_shows_own_value_domain() => fixture.Session.Dispatch(() =>
    {
        var (window, viewModel) = OpenWindow(ClassicSudoku.Create(Givens));
        viewModel.Editor.SelectNodes([ClassicSudoku.NodeId(0, 2), ClassicSudoku.NodeId(0, 3)]);
        var panel = (NodeSelectionPropertiesViewModel)viewModel.Editor.Properties!;
        panel.DomainMin = "1";
        panel.DomainMax = "4";
        panel.ApplyDomainCommand.Execute(null);
        Capture(window, "auswahl-wertebereich.png");
        window.Close();
    }, CancellationToken.None);
}
