using System.ComponentModel;
using System.Text.Json;
using GraphDoku.App.Editing;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Serialization;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests;

/// <summary>
/// Paket 7 (Zellformen): Umschalten Kreise/Zellen als reine Ansichtseinstellung, Treffertests und Bedienung in der
/// Zelldarstellung auf allen drei Gittern, Geometrie der Zellen und Kantenstege, unverändertes Speicherformat.
/// </summary>
public sealed class NodeShapeTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<GridType> Grids() => [GridType.Square, GridType.Triangle, GridType.Hex];

    /// <summary>Ein Flecken Knoten um (0, 0) herum (Dreiecke: breiter, weil zwei Zellen pro Welteinheit).</summary>
    private static Puzzle Patch(GridType grid)
    {
        var puzzle = new Puzzle($"Flecken {grid}", grid, new ValueDomain(1, 9));
        var width = grid == GridType.Triangle ? 5 : 3;
        for (var y = -3; y <= 3; y++)
        {
            for (var x = -width; x <= width; x++)
            {
                puzzle.Nodes.Add(new Node(puzzle.NextNodeId(), x, y));
            }
        }

        return puzzle;
    }

    private static Node At(EditorViewModel editor, int x, int y) => editor.Puzzle.Nodes.Single(n => n.X == x && n.Y == y);

    private static WorldPoint Toward(WorldPoint from, WorldPoint to, double fraction) =>
        new(from.X + ((to.X - from.X) * fraction), from.Y + ((to.Y - from.Y) * fraction));

    [Fact]
    public void Default_is_circles_and_toggling_is_a_pure_view_setting()
    {
        var editor = EditorDriver.CreateEditor(Patch(GridType.Square));
        editor.NodeShape.Should().Be(NodeShape.Circle);
        editor.IsCircleShape.Should().BeTrue();
        editor.IsCellShape.Should().BeFalse();
        var before = editor.Snapshot();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)editor).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var redraws = 0;
        editor.VisualChanged += (_, _) => redraws++;
        var contentChanges = 0;
        editor.ContentChanged += (_, _) => contentChanges++;

        editor.ToggleNodeShapeCommand.Execute(null);

        editor.NodeShape.Should().Be(NodeShape.Cell);
        editor.IsCellShape.Should().BeTrue();
        editor.IsCircleShape.Should().BeFalse();
        changed.Should().Contain([nameof(EditorViewModel.NodeShape), nameof(EditorViewModel.IsCircleShape), nameof(EditorViewModel.IsCellShape)]);
        redraws.Should().BeGreaterThan(0, "die Zeichenfläche wird neu gezeichnet");
        contentChanges.Should().Be(0, "das Rätsel ändert sich nicht");
        editor.IsDirty.Should().BeFalse();
        editor.CanUndo.Should().BeFalse("die Darstellung ist kein Bearbeitungsschritt");
        editor.StatusMessage.Should().Contain("Zellen (Quadrate)");
        editor.Puzzle.ShouldEqual(before);

        editor.ToggleNodeShapeCommand.Execute(null);
        editor.NodeShape.Should().Be(NodeShape.Circle);
        editor.StatusMessage.Should().Contain("Kreise");
    }

    [Fact]
    public void Radio_style_properties_switch_both_ways()
    {
        var editor = EditorDriver.CreateEditor();
        editor.IsCellShape = true;
        editor.NodeShape.Should().Be(NodeShape.Cell);
        editor.IsCircleShape = false; // Abwählen der anderen Option ändert nichts mehr
        editor.NodeShape.Should().Be(NodeShape.Cell);
        editor.IsCircleShape = true;
        editor.NodeShape.Should().Be(NodeShape.Circle);
        editor.IsCellShape = false;
        editor.NodeShape.Should().Be(NodeShape.Circle);

        // Ein einzelner Umschalter: Abwählen der aktiven Darstellung wechselt zur anderen.
        editor.IsCircleShape = false;
        editor.NodeShape.Should().Be(NodeShape.Cell);
        editor.IsCellShape = false;
        editor.NodeShape.Should().Be(NodeShape.Circle);
    }

    [Fact]
    public void Display_mode_survives_opening_another_puzzle_and_names_the_cell_shape()
    {
        var editor = EditorDriver.CreateEditor(Patch(GridType.Square));
        editor.CellShapeText.Should().Be("Knoten als Zellen (Quadrate)");
        editor.NodeShape = NodeShape.Cell;
        var notified = new List<string?>();
        ((INotifyPropertyChanged)editor).PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        editor.LoadPuzzle(Patch(GridType.Hex));
        editor.NodeShape.Should().Be(NodeShape.Cell, "die Darstellung ist eine Einstellung der Ansicht, nicht des Rätsels");
        editor.CellShapeText.Should().Be("Knoten als Zellen (Sechsecke)");
        notified.Should().Contain(nameof(EditorViewModel.CellShapeText));

        editor.LoadPuzzle(Patch(GridType.Triangle));
        editor.CellShapeText.Should().Be("Knoten als Zellen (Dreiecke)");
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void Cell_mode_hits_the_whole_cell_polygon(GridType grid)
    {
        var editor = EditorDriver.CreateEditor(Patch(grid));
        var node = At(editor, 0, 0);
        var center = EditorGeometry.Center(grid, node);
        var vertices = EditorGeometry.CellVertices(grid, node);
        vertices.Should().HaveCount(grid switch { GridType.Triangle => 3, GridType.Hex => 6, _ => 4 });

        foreach (var vertex in vertices)
        {
            var nearCorner = Toward(center, vertex, 0.9);
            editor.NodeShape = NodeShape.Circle;
            editor.HitNode(nearCorner).Should().BeNull("bei Kreisen liegt die Zellecke außerhalb des Knotens");
            editor.NodeShape = NodeShape.Cell;
            editor.HitNode(nearCorner).Should().BeSameAs(node, "bei Zellen trifft auch die Ecke die Zelle");
        }

        // Knapp jenseits jeder Seite liegt der Nachbar, der diese Seite teilt.
        var neighbors = GridGeometry.GetOrthogonalNeighbors(grid, 0, 0);
        for (var i = 0; i < vertices.Count; i++)
        {
            var middle = Toward(vertices[i], vertices[(i + 1) % vertices.Count], 0.5);
            var outside = Toward(center, middle, 1.08);
            var hit = editor.HitNode(outside);
            hit.Should().NotBeNull();
            hit.Should().NotBeSameAs(node);
            neighbors.Should().Contain(new GridPoint(hit!.X, hit.Y), "jenseits einer Seite liegt ein Kantennachbar");
            editor.HitNode(Toward(center, middle, 0.92)).Should().BeSameAs(node);
        }
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void Cell_hit_test_matches_the_nearest_grid_point_everywhere(GridType grid)
    {
        var editor = EditorDriver.CreateEditor(Patch(grid));
        editor.NodeShape = NodeShape.Cell;
        var random = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            var point = new WorldPoint((random.NextDouble() * 4) - 2, (random.NextDouble() * 4) - 2);
            var expected = GridGeometry.ToNearestGridPoint(grid, point);
            var hit = editor.HitNode(point);
            hit.Should().NotBeNull($"({point.X:0.###}, {point.Y:0.###}) liegt im Flecken");
            new GridPoint(hit!.X, hit.Y).Should().Be(expected, $"die Zelle um ({point.X:0.###}, {point.Y:0.###}) gehört zum nächsten Gitterpunkt");
        }
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void Clicking_a_cell_corner_selects_the_node_only_in_cell_mode(GridType grid)
    {
        var editor = EditorDriver.CreateEditor(Patch(grid));
        var node = At(editor, 0, 0);
        var corner = Toward(EditorGeometry.Center(grid, node), EditorGeometry.CellVertices(grid, node)[0], 0.85);

        editor.Click(editor.Viewport.ToScreen(corner));
        editor.SelectedNodeIds.Should().BeEmpty("bei Kreisen trifft ein Klick in die Zellecke keinen Knoten");

        editor.NodeShape = NodeShape.Cell;
        editor.Click(editor.Viewport.ToScreen(corner));
        editor.SelectedNodeIds.Should().Equal(node.Id);

        // Shift-Klick in die Ecke einer anderen Zelle hängt an.
        var other = At(editor, 1, 0);
        var otherCorner = Toward(EditorGeometry.Center(grid, other), EditorGeometry.CellVertices(grid, other)[1], 0.85);
        editor.Click(editor.Viewport.ToScreen(otherCorner), EditorModifiers.Shift);
        editor.SelectedNodeIds.Should().Equal(node.Id, other.Id);
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void Edges_are_selected_on_the_shared_border_and_cells_inside(GridType grid)
    {
        var puzzle = Patch(grid);
        var editor = EditorDriver.CreateEditor(puzzle);
        var node = At(editor, 0, 0);
        var neighbors = GridGeometry.GetOrthogonalNeighbors(grid, 0, 0);
        var kropki = At(editor, neighbors[0].X, neighbors[0].Y);
        var plain = At(editor, neighbors[1].X, neighbors[1].Y);
        var edges = puzzle.Clone();
        edges.Edges.Add(new Edge(1, node.Id, kropki.Id, "Difference", new RuleParams().Set("k", 1)));
        edges.Edges.Add(new Edge(2, node.Id, plain.Id, "NotEqual"));
        editor.LoadPuzzle(edges);
        editor.NodeShape = NodeShape.Cell;
        var center = EditorGeometry.Center(grid, node);
        WorldPoint Border(Node other) => Toward(center, EditorGeometry.Center(grid, other), 0.5);

        // Kantenmarke und Steg liegen auf der Mitte der gemeinsamen Seite.
        editor.Click(editor.Viewport.ToScreen(Border(kropki)));
        editor.SelectedEdgeId.Should().Be(1);
        editor.Click(editor.Viewport.ToScreen(Border(plain)));
        editor.SelectedEdgeId.Should().Be(2, "auch „Ungleich“ ohne Marke ist über seinen Steg am Zellrand wählbar");

        // Im Innern der Zelle (Wert, Kandidaten) und in den Ecken trifft man die Zelle.
        editor.Click(editor.Viewport.ToScreen(Toward(center, Border(kropki), 0.5)));
        editor.SelectedNodeIds.Should().Equal(node.Id);
        editor.SelectedEdgeId.Should().BeNull();
        editor.ClearSelection();
        editor.Click(editor.Viewport.ToScreen(Toward(center, EditorGeometry.CellVertices(grid, node)[0], 0.85)));
        editor.SelectedNodeIds.Should().Equal(node.Id);

        // Shift-Klick auf den Rand wählt nicht die Kante, sondern hängt die Zelle an (wie bei Kreisen).
        editor.Click(editor.Viewport.ToScreen(Toward(Border(kropki), EditorGeometry.Center(grid, kropki), 0.05)), EditorModifiers.Shift);
        editor.SelectedNodeIds.Should().Equal(node.Id, kropki.Id);
    }

    [Theory]
    [InlineData(GridType.Square, 2, 1)]
    [InlineData(GridType.Triangle, 2, 0)]
    [InlineData(GridType.Hex, 1, 1)]
    public void Cells_grabbed_near_their_border_move_by_their_centre(GridType grid, int targetX, int targetY)
    {
        var puzzle = new Puzzle("Verschieben", grid, new ValueDomain(1, 9));
        puzzle.Nodes.Add(new Node(1, 0, 0));
        puzzle.Nodes.Add(new Node(2, -3, -3));
        puzzle.Nodes.Add(new Node(3, 4, 3));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.NodeShape = NodeShape.Cell;
        var node = editor.Puzzle.FindNode(1)!;
        var center = EditorGeometry.Center(grid, node);
        var vertices = EditorGeometry.CellVertices(grid, node);
        var grab = Toward(center, Toward(vertices[0], vertices[1], 0.5), 0.85); // nahe der Mitte der ersten Seite

        // Ein kleines Stück über die Seite hinaus: Der Zeiger liegt schon in der Nachbarzelle, der Mittelpunkt nicht.
        var nudge = Toward(center, grab, 1.3);
        editor.Drag([editor.Viewport.ToScreen(grab), editor.Viewport.ToScreen(nudge)]);
        (node.X, node.Y).Should().Be((0, 0), "eingerastet wird der Mittelpunkt der gepackten Zelle, nicht der Zeiger");

        var target = GridGeometry.ToWorld(grid, targetX, targetY);
        var drop = new WorldPoint(grab.X + (target.X - center.X), grab.Y + (target.Y - center.Y));
        editor.Drag([editor.Viewport.ToScreen(grab), editor.Viewport.ToScreen(drop)]);
        (node.X, node.Y).Should().Be((targetX, targetY));
        editor.UndoCommand.Execute(null);
        (node.X, node.Y).Should().Be((0, 0));
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void Edges_can_be_drawn_between_cell_corners(GridType grid)
    {
        var editor = EditorDriver.CreateEditor(Patch(grid));
        editor.NodeShape = NodeShape.Cell;
        editor.Tool = EditorTool.DrawEdge;
        var from = At(editor, 0, 0);
        var to = At(editor, 2, 1);
        var start = Toward(EditorGeometry.Center(grid, from), EditorGeometry.CellVertices(grid, from)[0], 0.85);
        var end = Toward(EditorGeometry.Center(grid, to), EditorGeometry.CellVertices(grid, to)[^1], 0.85);
        editor.Drag([editor.Viewport.ToScreen(start), editor.Viewport.ToScreen(end)]);
        editor.Puzzle.Edges.Should().ContainSingle().Which.Should().Match<Edge>(e => e.From == from.Id && e.To == to.Id);
    }

    [Fact]
    public void Place_tool_selects_an_existing_cell_instead_of_placing()
    {
        var editor = EditorDriver.CreateEditor(Patch(GridType.Square));
        editor.NodeShape = NodeShape.Cell;
        editor.Tool = EditorTool.PlaceNode;
        var count = editor.Puzzle.Nodes.Count;
        var node = At(editor, 1, 1);
        editor.Click(editor.ScreenOf(1.45, 0.55));
        editor.Puzzle.Nodes.Should().HaveCount(count);
        editor.SelectedNodeIds.Should().Equal(node.Id);

        // Außerhalb aller Zellen wird wie bisher gesetzt.
        editor.Click(editor.ScreenOf(new GridPoint(6, 0)));
        editor.Puzzle.Nodes.Should().HaveCount(count + 1);
    }

    [Theory]
    [MemberData(nameof(SolveIntegrationTests.ExamplePuzzles), MemberType = typeof(SolveIntegrationTests))]
    public async Task Toggling_the_display_does_not_change_the_saved_file_and_files_round_trip(string fileName)
    {
        var original = Path.Combine(MainWindowViewModelTests.FindRepositoryRoot(), "puzzles", fileName);
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        viewModel.Editor.SetViewSize(1000, 800);
        (await viewModel.OpenFileAsync(original)).Should().BeTrue();

        var circles = _temp.File("kreise.json");
        (await viewModel.SaveToFileAsync(circles)).Should().BeTrue();
        viewModel.Editor.NodeShape = NodeShape.Cell;
        viewModel.Editor.IsDirty.Should().BeFalse();
        viewModel.Title.Should().NotContain("*", "die Darstellung macht das Dokument nicht ungespeichert");
        var cells = _temp.File("zellen.json");
        (await viewModel.SaveToFileAsync(cells)).Should().BeTrue();
        File.ReadAllBytes(cells).Should().Equal(File.ReadAllBytes(circles), "die Darstellung steht nicht in der Rätseldatei");

        // Die Datei hat genau die Felder des Formats (der Serializer lehnt unbekannte Felder ab – ältere Versionen lesen sie).
        using (var json = JsonDocument.Parse(File.ReadAllText(cells)))
        {
            json.RootElement.EnumerateObject().Select(p => p.Name).Should()
                .BeSubsetOf(["formatVersion", "name", "grid", "domain", "nodes", "edges", "groups"]);
            json.RootElement.GetProperty("formatVersion").GetInt32().Should().Be(1);
        }

        // Round-Trip in der Zelldarstellung: öffnen → gleiches Modell wie das Original, Darstellung bleibt.
        (await viewModel.OpenFileAsync(cells)).Should().BeTrue();
        viewModel.Editor.NodeShape.Should().Be(NodeShape.Cell);
        viewModel.Editor.Puzzle.ShouldEqual(PuzzleSerializer.Load(original, validate: false));
        var again = _temp.File("wieder.json");
        viewModel.Editor.NodeShape = NodeShape.Circle;
        (await viewModel.SaveToFileAsync(again)).Should().BeTrue();
        File.ReadAllBytes(again).Should().Equal(File.ReadAllBytes(circles));
    }

    [Fact]
    public void Content_and_clear_radii_fit_inside_the_cells()
    {
        foreach (var grid in Enum.GetValues<GridType>())
        {
            var vertices = GridGeometry.GetCellVertices(grid, 0, 0);
            var center = GridGeometry.ToWorld(grid, 0, 0);
            var inradius = Enumerable.Range(0, vertices.Count).Min(i => EditorGeometry.DistanceToSegment(center, vertices[i], vertices[(i + 1) % vertices.Count]));
            EditorGeometry.ContentRadius(grid, NodeShape.Cell).Should().BeLessThan(inradius, $"Inhalt passt in die Zelle ({grid})");
            EditorGeometry.ContentRadius(grid, NodeShape.Circle).Should().Be(EditorGeometry.NodeRadius(grid));
            EditorGeometry.ClearRadius(grid, NodeShape.Circle).Should().Be(EditorGeometry.NodeRadius(grid));

            // Zwischen Nachbarzellen bleibt Platz für einen Steg über die gemeinsame Seite.
            var neighbor = GridGeometry.GetOrthogonalNeighbors(grid, 0, 0)[0];
            var distance = GridGeometry.Distance(grid, new GridPoint(0, 0), neighbor);
            (distance - (2 * EditorGeometry.ClearRadius(grid, NodeShape.Cell))).Should().BeGreaterThan(EditorGeometry.CellEdgeStubLength(grid) * 0.99);
        }
    }

    [Fact]
    public void Polygon_test_and_inset_work_for_both_orientations()
    {
        var up = GridGeometry.GetCellVertices(GridType.Triangle, 0, 0);
        var down = GridGeometry.GetCellVertices(GridType.Triangle, 1, 0);
        var upCenter = GridGeometry.ToWorld(GridType.Triangle, 0, 0);
        EditorGeometry.PolygonContains(up, upCenter).Should().BeTrue();
        EditorGeometry.PolygonContains(down, upCenter).Should().BeFalse();
        EditorGeometry.PolygonContains([.. up.Reverse()], upCenter).Should().BeTrue("der Umlaufsinn spielt keine Rolle");
        EditorGeometry.PolygonContains(up, up[0]).Should().BeTrue("der Rand gehört dazu");
        EditorGeometry.PolygonContains([up[0], up[1]], upCenter).Should().BeFalse();

        var square = GridGeometry.GetCellVertices(GridType.Square, 0, 0);
        var inset = EditorGeometry.Inset(square, new WorldPoint(0, 0), 0.1);
        inset[0].X.Should().BeApproximately(-0.4, 1e-9);
        inset[0].Y.Should().BeApproximately(-0.4, 1e-9);
        EditorGeometry.Inset(square, new WorldPoint(0, 0), 2).Should().OnlyContain(v => Math.Abs(v.X) < 1e-12 && Math.Abs(v.Y) < 1e-12);
    }

    [Fact]
    public void Border_crossings_and_stubs_sit_on_cell_borders()
    {
        // Quadrat: Nachbar → Seite bei 0,5; übernächster → 0,5 und 1,5; diagonal → einmal durch die Ecke.
        EditorGeometry.BorderCrossings(GridType.Square, new WorldPoint(0, 0), new WorldPoint(1, 0)).Should().Equal([0.5], (a, b) => Math.Abs(a - b) < 1e-9);
        EditorGeometry.BorderCrossings(GridType.Square, new WorldPoint(0, 0), new WorldPoint(2, 0)).Should().Equal([0.5, 1.5], (a, b) => Math.Abs(a - b) < 1e-9);
        EditorGeometry.BorderCrossings(GridType.Square, new WorldPoint(0, 0), new WorldPoint(1, 1)).Should().ContainSingle()
            .Which.Should().BeApproximately(Math.Sqrt(2) / 2, 1e-4);

        // Dreieck: Zellen, die sich nur in einer Ecke gegenüberliegen, kreuzen genau dort.
        var a = GridGeometry.ToWorld(GridType.Triangle, 0, 0);
        var b = GridGeometry.ToWorld(GridType.Triangle, 0, 1);
        GridGeometry.IsUpTriangle(0, 0).Should().BeTrue();
        var crossings = EditorGeometry.BorderCrossings(GridType.Triangle, a, b);
        crossings.Should().ContainSingle();
        crossings[0].Should().BeApproximately(a.DistanceTo(b) / 2, 1e-4);

        // Steg bei Nachbarzellen: mittig über der gemeinsamen Seite, mit fester Länge.
        foreach (var grid in Enum.GetValues<GridType>())
        {
            var from = GridGeometry.ToWorld(grid, 0, 0);
            var to = GridGeometry.ToWorld(grid, GridGeometry.GetOrthogonalNeighbors(grid, 0, 0)[0]);
            var stub = EditorGeometry.CellEdgeStubs(grid, from, to, []).Should().ContainSingle().Subject;
            var middle = Toward(stub.From, stub.To, 0.5);
            middle.DistanceTo(Toward(from, to, 0.5)).Should().BeLessThan(1e-6, $"{grid}: Steg auf der Seitenmitte");
            stub.From.DistanceTo(stub.To).Should().BeApproximately(EditorGeometry.CellEdgeStubLength(grid), 1e-6);
        }

        // Knoten zwischen den Enden: kein Steg in dessen Inhalt, je ein Steg pro gekreuzter Seite.
        var stubs = EditorGeometry.CellEdgeStubs(GridType.Square, new WorldPoint(0, 0), new WorldPoint(2, 0), [new WorldPoint(1, 0)]);
        stubs.Should().HaveCount(2);
        stubs.Select(s => Toward(s.From, s.To, 0.5).X).Should().Equal([0.5, 1.5], (x, y) => Math.Abs(x - y) < 1e-9);
    }
}
