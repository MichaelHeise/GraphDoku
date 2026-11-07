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

            // Summe der Dunkelheit statt fester Schwelle je Pixel: Kleine Summen wie „6“ haben je nach Plattform
            // (Hinting, Glättung) nur wenige ganz dunkle Pixel. Die Differenz zur Ecke einer anderen Zelle desselben
            // Käfigs (gleiche Füllfarbe) zeigt die Summe; trüge die andere Zelle auch eine, wäre sie nahe 0.
            var withSum = Darkness(frame, Corner(anchor));
            var without = Darkness(frame, Corner(other));
            output.WriteLine($"Käfig {cage.Id}: Dunkelheit der Ecke mit Summe {withSum}, ohne {without}");
            (withSum - without).Should().BeGreaterThan(1000, $"Käfig {cage.Id} zeigt seine Summe nur in der ersten Zelle");
            CountDarkPixels(frame, Corner(other)).Should().BeLessThan(3, $"nur die erste Zelle von Käfig {cage.Id} trägt die Summe");
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
    public Task Marks_of_longer_edges_do_not_cover_the_node_in_between() => fixture.Session.Dispatch(() =>
    {
        // Review-Befund: Kanten zum übernächsten Knoten (freier Abstand 2) setzten ihre Marke auf die Mitte des Knotens
        // dazwischen; seit die Marken über den Knoten liegen, verdeckte das dessen Wert.
        var puzzle = new Puzzle("Lange Kanten", GridType.Square, new ValueDomain(1, 9));
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                puzzle.Nodes.Add(new Node((y * 5) + x + 1, x, y));
            }
        }

        puzzle.Nodes[1].Given = 8;
        puzzle.Nodes[3].Given = 8;
        var (window, viewModel) = OpenWindow(puzzle);
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        var editor = viewModel.Editor;
        var middles = new[] { 2, 4, 7, 9, 12, 14 }.Select(id => editor.Puzzle.FindNode(id)!).ToList();
        var plain = Capture(window, "lange-kanten-ohne.png");
        var before = middles.Select(n => Darkness(plain, NodeArea(window, canvas, editor, n, 0.55))).ToList();

        var generator = new EdgeGenerator();
        foreach (var (row, rule) in new[] { (0, "Difference"), (1, "LessThan"), (2, "Ratio") })
        {
            var ids = Enumerable.Range((row * 5) + 1, 5).ToList();
            generator.Generate(editor.Puzzle, new EdgeGenerationRequest(DistanceRule.Range(2, 2), rule) { NodeIds = ids }).ApplyTo(editor.Puzzle);
        }

        editor.Puzzle.Edges.Should().HaveCount(9);
        editor.LoadPuzzle(editor.Puzzle.Clone());
        var frame = Capture(window, "lange-kanten.png");
        for (var i = 0; i < middles.Count; i++)
        {
            var after = Darkness(frame, NodeArea(window, canvas, editor, middles[i], 0.55));
            output.WriteLine($"Knoten {middles[i].Id}: Dunkelheit ohne Kanten {before[i]}, mit {after}");
            after.Should().BeCloseTo(before[i], (uint)Math.Max(300, before[i] / 20), $"keine Marke auf Knoten {middles[i].Id}");
        }

        // Die Marken sind trotzdem da, jeweils im ersten Spalt ihrer Kante (Kante 6–8 zwischen 6 und 7, 11–13 zwischen 11
        // und 12); der letzte Spalt einer Zeile (9–10, 14–15) trägt nur die Linie der Kante 8–10 bzw. 13–15.
        PixelRect Gap(int left, int right)
        {
            var a = editor.Viewport.ToScreen(EditorGeometry.Center(GridType.Square, editor.Puzzle.FindNode(left)!));
            var b = editor.Viewport.ToScreen(EditorGeometry.Center(GridType.Square, editor.Puzzle.FindNode(right)!));
            var mid = canvas.TranslatePoint(new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), window)!.Value;
            var half = (int)(editor.Viewport.Scale * 0.08);
            return new PixelRect((int)mid.X - half, (int)mid.Y - half, 2 * half, 2 * half);
        }

        CountDarkPixels(frame, Gap(11, 12)).Should().BeGreaterThan(CountDarkPixels(frame, Gap(14, 15)) + 50, "schwarzer Punkt der Kante 11–13");
        Darkness(frame, Gap(6, 7)).Should().BeGreaterThan(Darkness(frame, Gap(9, 10)) + 2000, "Pfeil der Kante 6–8");

        editor.Viewport.ZoomAt(editor.ScreenOf(new GridPoint(2, 1)), 1.6);
        Capture(window, "lange-kanten-zoom.png");
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Hex_cage_sums_stay_outside_the_node_circle() => fixture.Session.Dispatch(() =>
    {
        // Review-Befund: Auf dem Sechseckgitter lag die Summe im Knotenkreis direkt neben dem Wert („17 5“).
        var puzzle = new Puzzle("Waben-Käfige", GridType.Hex, new ValueDomain(1, 9));
        for (var r = -2; r <= 2; r++)
        {
            for (var q = -2; q <= 2; q++)
            {
                if (Math.Abs(q + r) <= 2)
                {
                    puzzle.Nodes.Add(new Node(puzzle.NextNodeId(), q, r));
                }
            }
        }

        var byPosition = puzzle.Nodes.ToDictionary(n => (n.X, n.Y));
        var anchors = new[] { (0, -2), (-2, 0), (0, 0) };
        var targets = new[] { 23, 17, 30 };
        for (var i = 0; i < anchors.Length; i++)
        {
            var (q, r) = anchors[i];
            var members = new[] { (q, r), (q + 1, r), (q, r + 1) }.Where(byPosition.ContainsKey).Select(p => byPosition[p].Id).ToList();
            byPosition[(q, r)].Given = 5;
            puzzle.Groups.Add(new Group(i + 1, members, "Sum", new RuleParams().Set("target", targets[i]), label: $"Käfig {i + 1}"));
        }

        var (window, viewModel) = OpenWindow(puzzle);
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        var editor = viewModel.Editor;
        var frame = Capture(window, "summen-sechseck.png");
        editor.HideAllGroupsCommand.Execute(null);
        var hidden = Capture(window, "summen-sechseck-ausgeblendet.png");
        foreach (var (q, r) in anchors)
        {
            var node = byPosition[(q, r)];

            // Im Kreis (bis 0,7 Radien um die Mitte) ändert die Summe nichts, in der Ecke oben links ist sie zu sehen.
            var inside = NodeArea(window, canvas, editor, node, 0.7);
            Darkness(frame, inside).Should().BeCloseTo(Darkness(hidden, inside), 400, $"die Summe von Knoten {node.Id} liegt außerhalb des Kreises");
            var center = editor.Viewport.ToScreen(EditorGeometry.Center(GridType.Hex, node));
            var scale = editor.Viewport.Scale;
            var corner = canvas.TranslatePoint(new Point(center.X - (scale * 0.5), center.Y - (scale * 0.3)), window)!.Value;
            var area = new PixelRect((int)(corner.X - (scale * 0.12)), (int)(corner.Y - (scale * 0.12)), (int)(scale * 0.24), (int)(scale * 0.24));
            CountDarkPixels(frame, area).Should().BeGreaterThan(CountDarkPixels(hidden, area) + 5, $"Summe an Knoten {node.Id}");
        }

        editor.Viewport.ZoomAt(editor.ScreenOf(new GridPoint(-2, 0)), 2.5);
        editor.ShowAllGroupsCommand.Execute(null);
        Capture(window, "summen-sechseck-zoom.png");
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
