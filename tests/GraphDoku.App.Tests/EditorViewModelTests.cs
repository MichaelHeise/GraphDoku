using GraphDoku.App.Editing;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;

namespace GraphDoku.App.Tests;

/// <summary>Werkzeuge, Auswahl und Zeigergesten des Editors (ohne Oberfläche).</summary>
public class EditorViewModelTests
{
    private static Puzzle Row(int count, GridType grid = GridType.Square)
    {
        var puzzle = new Puzzle("Reihe", grid);
        for (var i = 0; i < count; i++)
        {
            puzzle.Nodes.Add(new Node(i + 1, i * 2, 0));
        }

        return puzzle;
    }

    [Fact]
    public void Starts_with_an_empty_square_puzzle_and_puzzle_properties()
    {
        var editor = EditorDriver.CreateEditor();

        editor.Puzzle.Nodes.Should().BeEmpty();
        editor.Puzzle.Grid.Should().Be(GridType.Square);
        editor.Tool.Should().Be(EditorTool.Select);
        editor.IsDirty.Should().BeFalse();
        editor.CanUndo.Should().BeFalse();
        editor.Properties.Should().BeOfType<PuzzlePropertiesViewModel>();
        editor.ValidationSummary.Should().Be("Keine Probleme gefunden.");
        editor.EdgeRules.Select(r => r.TypeName).Should().BeEquivalentTo(["NotEqual", "Difference", "Ratio", "LessThan"]);
        editor.GroupRules.Select(r => r.TypeName).Should().BeEquivalentTo(["AllDifferent", "CoverRange", "Sum", "Thermometer"]);
    }

    [Fact]
    public void Tool_flags_follow_the_tool()
    {
        var editor = EditorDriver.CreateEditor();
        editor.IsPlaceNodeTool = true;
        editor.Tool.Should().Be(EditorTool.PlaceNode);
        editor.IsSelectTool.Should().BeFalse();
        editor.IsDrawEdgeTool = true;
        editor.Tool.Should().Be(EditorTool.DrawEdge);
        editor.IsPanTool = true;
        editor.IsPanTool.Should().BeTrue();
        editor.IsPanTool = false;
        editor.Tool.Should().Be(EditorTool.Pan, "Abwählen eines Werkzeugs wechselt nicht");
    }

    [Fact]
    public void Place_node_snaps_to_the_nearest_grid_point_and_selects_it()
    {
        var editor = EditorDriver.CreateEditor();
        editor.Tool = EditorTool.PlaceNode;

        editor.Click(editor.ScreenOf(2.3, 0.8));

        editor.Puzzle.Nodes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = 1, X = 2, Y = 1 });
        editor.SelectedNodeIds.Should().Equal(1);
        editor.IsDirty.Should().BeTrue();
        editor.UndoText.Should().Be("Rückgängig: Knoten setzen");
    }

    [Theory]
    [InlineData(GridType.Triangle)]
    [InlineData(GridType.Hex)]
    public void Place_node_snaps_on_triangle_and_hex_grids(GridType grid)
    {
        var editor = EditorDriver.CreateEditor(new Puzzle("x", grid));
        editor.Tool = EditorTool.PlaceNode;
        var world = GridGeometry.ToWorld(grid, 3, 2);

        editor.Click(editor.ScreenOf(world.X + 0.05, world.Y - 0.05));

        editor.Puzzle.Nodes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { X = 3, Y = 2 });
    }

    [Fact]
    public void Placing_on_an_occupied_point_selects_the_node_instead()
    {
        var editor = EditorDriver.CreateEditor(Row(2));
        editor.Tool = EditorTool.PlaceNode;

        editor.Click(new GridPoint(2, 0));

        editor.Puzzle.Nodes.Should().HaveCount(2);
        editor.SelectedNodeIds.Should().Equal(2);
        editor.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Painting_nodes_by_dragging_is_one_undo_step()
    {
        var editor = EditorDriver.CreateEditor();
        editor.Tool = EditorTool.PlaceNode;

        editor.DragGrid(new GridPoint(0, 0), new GridPoint(4, 0));

        editor.Puzzle.Nodes.Select(n => (n.Id, n.X, n.Y)).Should().Equal((1, 0, 0), (2, 1, 0), (3, 2, 0), (4, 3, 0), (5, 4, 0));
        editor.History.UndoCount.Should().Be(1);
        editor.UndoCommand.Execute(null);
        editor.Puzzle.Nodes.Should().BeEmpty();
        editor.RedoCommand.Execute(null);
        editor.Puzzle.Nodes.Should().HaveCount(5);
    }

    [Fact]
    public void Click_selects_shift_click_toggles_and_click_on_empty_clears()
    {
        var editor = EditorDriver.CreateEditor(Row(3));

        editor.Click(new GridPoint(0, 0));
        editor.SelectedNodeIds.Should().Equal(1);

        editor.Click(new GridPoint(4, 0), EditorModifiers.Shift);
        editor.Click(new GridPoint(2, 0), EditorModifiers.Shift);
        editor.SelectedNodeIds.Should().Equal(1, 3, 2);
        editor.Properties.Should().BeOfType<NodeSelectionPropertiesViewModel>();

        editor.Click(new GridPoint(4, 0), EditorModifiers.Shift);
        editor.SelectedNodeIds.Should().Equal(1, 2);

        editor.Click(editor.ScreenOf(1, 3));
        editor.SelectedNodeIds.Should().BeEmpty();
        editor.Properties.Should().BeOfType<PuzzlePropertiesViewModel>();
    }

    [Fact]
    public void Click_on_a_node_of_a_multi_selection_selects_only_that_node()
    {
        var editor = EditorDriver.CreateEditor(Row(3));
        editor.SelectNodes([1, 2, 3]);

        editor.Click(new GridPoint(2, 0));

        editor.SelectedNodeIds.Should().Equal(2);
    }

    [Fact]
    public void Rubber_band_selects_row_by_row_and_shift_adds()
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());

        editor.RubberBand(0.6, 0.6, 2.4, 2.4);
        editor.SelectedNodeIds.Should().Equal(
            ClassicSudoku.NodeId(1, 1), ClassicSudoku.NodeId(1, 2), ClassicSudoku.NodeId(2, 1), ClassicSudoku.NodeId(2, 2));

        editor.RubberBand(-0.4, -0.4, 0.4, 0.4, EditorModifiers.Shift);
        editor.SelectedNodeIds.Should().HaveCount(5).And.EndWith(1);
        editor.RubberBand.Should().BeNull();
    }

    [Fact]
    public void Rubber_band_orders_triangle_rows_left_to_right_despite_zigzag_centroids()
    {
        var puzzle = new Puzzle("Dreiecke", GridType.Triangle);
        var id = 1;
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                puzzle.Nodes.Add(new Node(id++, x, y));
            }
        }

        var editor = EditorDriver.CreateEditor(puzzle);
        editor.RubberBand(-0.4, -0.1, 2.0, (2 * GridGeometry.TriangleRowHeight) + 0.1);

        editor.SelectedNodeIds.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        editor.CreateGroupFromSelection();
        editor.Puzzle.Groups.Single().Nodes.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
    }

    [Fact]
    public void Rubber_band_orders_hex_rows_by_row_then_left_to_right()
    {
        var puzzle = new Puzzle("Waben", GridType.Hex);
        puzzle.Nodes.Add(new Node(1, 1, 1));
        puzzle.Nodes.Add(new Node(2, 0, 1));
        puzzle.Nodes.Add(new Node(3, 1, 0));
        puzzle.Nodes.Add(new Node(4, 0, 0));

        var editor = EditorDriver.CreateEditor(puzzle);
        editor.RubberBand(-1, -1, 3, 2);

        editor.SelectedNodeIds.Should().Equal(4, 3, 2, 1);
    }

    [Fact]
    public void Row_wise_order_sorts_by_grid_row_then_world_x()
    {
        Node[] nodes = [new(1, 3, 0), new(2, 0, 1), new(3, 1, 0), new(4, 0, 0), new(5, 2, 0)];

        EditorGeometry.RowWiseOrder(GridType.Triangle, nodes).Select(n => n.Id).Should().Equal(4, 3, 5, 1, 2);
        EditorGeometry.RowWiseOrder(GridType.Square, nodes).Select(n => n.Id).Should().Equal(4, 3, 5, 1, 2);
    }

    [Fact]
    public void Duplicate_group_ids_from_an_unvalidated_file_do_not_crash_edits()
    {
        var puzzle = new Puzzle("Doppelt");
        puzzle.Nodes.Add(new Node(1, 0, 0));
        puzzle.Nodes.Add(new Node(2, 1, 0));
        puzzle.Nodes.Add(new Node(3, 2, 0));
        puzzle.Groups.Add(new Group(1, [1], "AllDifferent"));
        puzzle.Groups.Add(new Group(1, [2], "AllDifferent"));
        puzzle.Groups.Add(new Group(2, [3], "AllDifferent"));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.Groups.Should().HaveCount(3);
        editor.ValidationProblems.Should().Contain(p => p.Contains("mehrfach", StringComparison.Ordinal));

        editor.SelectNodes([1]);
        editor.EnterValueCharacter('5');
        editor.Puzzle.FindNode(1)!.Given.Should().Be(5);
        editor.Groups.Should().HaveCount(3);

        editor.SelectGroup(1);
        editor.DeleteSelection();
        editor.Groups.Select(g => g.Id).Should().Equal(2);
        editor.UndoCommand.Execute(null);
        editor.Groups.Select(g => g.Id).Should().Equal(1, 1, 2);
        editor.Groups.Select(g => g.Details).Should().AllSatisfy(d => d.Should().Contain("1 Knoten"));
        editor.SetGroupVisible(1, false);
        editor.Groups.Where(g => g.Id == 1).Should().AllSatisfy(g => g.IsVisible.Should().BeFalse());
    }

    [Fact]
    public void Select_all_and_escape()
    {
        var editor = EditorDriver.CreateEditor(Row(4));
        editor.SelectAll();
        editor.SelectedNodeIds.Should().HaveCount(4);
        editor.Escape();
        editor.SelectedNodeIds.Should().BeEmpty();
    }

    [Fact]
    public void Dragging_a_node_moves_it_with_snapping_as_one_undo_step()
    {
        var editor = EditorDriver.CreateEditor(Row(2));
        var before = editor.Snapshot();

        editor.Drag([editor.ScreenOf(new GridPoint(0, 0)), editor.ScreenOf(0.6, 1.2), editor.ScreenOf(1.1, 2.9)]);

        editor.Puzzle.FindNode(1).Should().BeEquivalentTo(new { X = 1, Y = 3 });
        editor.History.UndoCount.Should().Be(1);
        editor.UndoText.Should().Be("Rückgängig: Knoten verschieben");
        editor.UndoCommand.Execute(null);
        editor.Puzzle.ShouldEqual(before);
        editor.RedoCommand.Execute(null);
        editor.Puzzle.FindNode(1).Should().BeEquivalentTo(new { X = 1, Y = 3 });
    }

    [Fact]
    public void Moving_a_multi_selection_keeps_its_shape()
    {
        var editor = EditorDriver.CreateEditor(Row(3));
        editor.SelectNodes([1, 2]);

        editor.Drag([editor.ScreenOf(new GridPoint(2, 0)), editor.ScreenOf(new GridPoint(3, 5))]);

        editor.Puzzle.Nodes.Select(n => (n.X, n.Y)).Should().Equal((1, 5), (3, 5), (4, 0));
        editor.SelectedNodeIds.Should().Equal(1, 2);
        editor.History.UndoCount.Should().Be(1);
    }

    [Fact]
    public void Nodes_are_not_moved_onto_occupied_points()
    {
        var editor = EditorDriver.CreateEditor(Row(2));

        // Über (1, 0) nach (2, 0): (2, 0) ist belegt, der Knoten bleibt am letzten freien Punkt.
        editor.Drag([editor.ScreenOf(new GridPoint(0, 0)), editor.ScreenOf(new GridPoint(2, 0))]);
        editor.Puzzle.FindNode(1).Should().BeEquivalentTo(new { X = 1, Y = 0 });

        editor.UndoCommand.Execute(null);
        editor.Drag([editor.ScreenOf(new GridPoint(0, 0)), editor.ScreenOf(0, 0.2), editor.ScreenOf(new GridPoint(2, 0))]);
        editor.Puzzle.Nodes.Select(n => (n.X, n.Y)).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void Moving_on_the_triangle_grid_keeps_the_orientation()
    {
        var puzzle = new Puzzle("Dreieck", GridType.Triangle);
        puzzle.Nodes.Add(new Node(1, 0, 0));
        var editor = EditorDriver.CreateEditor(puzzle);
        var target = GridGeometry.ToWorld(GridType.Triangle, 1, 0); // zeigt nach unten

        editor.Drag([editor.ScreenOf(new GridPoint(0, 0)), editor.ScreenOf(target.X, target.Y)]);

        var node = editor.Puzzle.FindNode(1)!;
        GridGeometry.IsUpTriangle(node.X, node.Y).Should().BeTrue();
    }

    [Fact]
    public void Right_or_middle_drag_and_space_drag_pan_the_view_without_editing()
    {
        var editor = EditorDriver.CreateEditor(Row(2));
        var x = editor.Viewport.OffsetX;
        var y = editor.Viewport.OffsetY;

        editor.Drag([new ScreenPoint(500, 500), new ScreenPoint(530, 480)], button: PointerButton.Right);
        editor.Drag([new ScreenPoint(500, 500), new ScreenPoint(510, 500)], button: PointerButton.Middle);
        editor.Viewport.OffsetX.Should().BeApproximately(x + 40, 1e-9);
        editor.Viewport.OffsetY.Should().BeApproximately(y - 20, 1e-9);

        editor.SetSpaceHeld(true);
        var node = editor.ScreenOf(new GridPoint(0, 0));
        editor.Drag([node, new ScreenPoint(node.X + 25, node.Y + 5)]);
        editor.SetSpaceHeld(false);

        editor.Viewport.OffsetX.Should().BeApproximately(x + 65, 1e-9);
        editor.CanUndo.Should().BeFalse();
        editor.Puzzle.FindNode(1).Should().BeEquivalentTo(new { X = 0, Y = 0 });
    }

    [Fact]
    public void Pan_tool_pans_and_wheel_zooms_around_the_cursor()
    {
        var editor = EditorDriver.CreateEditor();
        editor.Tool = EditorTool.Pan;
        var before = editor.Viewport.OffsetX;

        editor.Drag([new ScreenPoint(100, 100), new ScreenPoint(150, 100)]);
        editor.Viewport.OffsetX.Should().BeApproximately(before + 50, 1e-9);

        var cursor = new ScreenPoint(300, 200);
        var world = editor.Viewport.ToWorld(cursor);
        var scale = editor.Viewport.Scale;
        editor.PointerWheel(cursor, 2);
        editor.Viewport.Scale.Should().BeGreaterThan(scale);
        var after = editor.Viewport.ToScreen(world);
        after.X.Should().BeApproximately(cursor.X, 1e-9);
        after.Y.Should().BeApproximately(cursor.Y, 1e-9);
    }

    [Fact]
    public void Fit_view_waits_for_a_size_and_fits_the_content()
    {
        var editor = new EditorViewModel();
        editor.LoadPuzzle(ClassicSudoku.Create());
        editor.SetViewSize(1000, 1000);

        var topLeft = editor.Viewport.ToScreen(new WorldPoint(-0.5, -0.5));
        var bottomRight = editor.Viewport.ToScreen(new WorldPoint(8.5, 8.5));
        topLeft.X.Should().BeApproximately(1000 - bottomRight.X, 1e-6);
        topLeft.Y.Should().BeGreaterThan(0);
        bottomRight.Y.Should().BeLessThan(1000);
    }

    [Fact]
    public void Clicking_near_an_edge_selects_it()
    {
        var puzzle = Row(2);
        puzzle.Edges.Add(new Edge(1, 1, 2, "NotEqual"));
        var editor = EditorDriver.CreateEditor(puzzle);

        editor.Click(editor.ScreenOf(1, 0.01));

        editor.SelectedEdgeId.Should().Be(1);
        editor.SelectedNodeIds.Should().BeEmpty();
        editor.Properties.Should().BeOfType<EdgePropertiesViewModel>();
    }

    [Fact]
    public void Clicking_inside_a_large_mark_off_the_line_selects_the_edge()
    {
        // Review-Befund: In der Kreisdarstellung traf ein Klick eine Kante nur 6 px um die Linie, auch wenn die
        // gezeichnete Marke (eingepasst Maßstab 468: Punkt mit 14 px Radius) viel größer war.
        var puzzle = new Puzzle("Paar", GridType.Square);
        puzzle.Nodes.Add(new Node(1, 0, 0));
        puzzle.Nodes.Add(new Node(2, 1, 0));
        puzzle.Edges.Add(new Edge(1, 1, 2, "Ratio", new RuleParams().Set("factor", 2)));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.Viewport.Scale.Should().BeApproximately(468, 1e-6);
        var mark = editor.ScreenOf(0.5, 0);

        editor.Click(new ScreenPoint(mark.X, mark.Y + 11));

        editor.SelectedEdgeId.Should().Be(1);

        // Außerhalb der Marke und weiter als 6 px von der Linie: weiterhin nichts.
        editor.Click(new ScreenPoint(mark.X, mark.Y + 16));
        editor.SelectedEdgeId.Should().BeNull();
    }

    [Fact]
    public void Clicking_a_mark_that_overlaps_a_node_selects_the_edge()
    {
        // Review-Befund: Die Marke liegt über den Knoten. Beim Standardmaßstab 48 ist ein beschrifteter Punkt (k = 2)
        // breiter als der Spalt zwischen den Kreisen; ein Klick auf den Teil über einem Knotenkreis wählte den Knoten
        // statt der Kante. Die Linie selbst liegt unter den Knoten und bleibt dort unerreichbar.
        var puzzle = new Puzzle("Paar", GridType.Square);
        puzzle.Nodes.Add(new Node(1, 0, 0));
        puzzle.Nodes.Add(new Node(2, 1, 0));
        puzzle.Nodes.Add(new Node(3, 0, 1));
        puzzle.Edges.Add(new Edge(1, 1, 2, "Difference", new RuleParams().Set("k", 2)));
        puzzle.Edges.Add(new Edge(2, 1, 3, "NotEqual"));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.MeasureMarkLabel = (_, size) => (size * 0.6, size * 1.2); // etwa wie die Schrift der Zeichenfläche
        editor.Viewport.ZoomAt(new ScreenPoint(500, 400), Viewport.DefaultScale / editor.Viewport.Scale);
        var a = editor.ScreenOf(new GridPoint(0, 0));
        var mark = editor.ScreenOf(0.5, 0);
        var nodeRadius = EditorGeometry.NodeRadius(GridType.Square) * editor.Viewport.Scale;
        var insideNode = new ScreenPoint(a.X + nodeRadius - 2, a.Y);
        editor.HitNode(editor.Viewport.ToWorld(insideNode))!.Id.Should().Be(1);
        (mark.X - insideNode.X).Should().BeGreaterThan(EditorViewModel.EdgeHitTolerance, "nur die beschriftete Marke reicht bis hierher");

        editor.Click(insideNode);

        editor.SelectedEdgeId.Should().Be(1);
        editor.SelectedNodeIds.Should().BeEmpty();

        // Ohne Marke (Linie unter dem Knoten) gewinnt der Knoten.
        editor.Click(new ScreenPoint(a.X, a.Y + nodeRadius - 2));
        editor.SelectedEdgeId.Should().BeNull();
        editor.SelectedNodeIds.Should().Equal(1);
    }

    [Fact]
    public void Label_measure_enlarges_the_hit_area_of_a_labelled_dot()
    {
        // Beschriftete Punkte (k = 2) sind größer als unbeschriftete; die Zeichenfläche misst die Beschriftung.
        var puzzle = new Puzzle("Paar", GridType.Square);
        puzzle.Nodes.Add(new Node(1, 0, 0));
        puzzle.Nodes.Add(new Node(2, 1, 0));
        puzzle.Edges.Add(new Edge(1, 1, 2, "Difference", new RuleParams().Set("k", 2)));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.MeasureMarkLabel = (_, size) => (size * 0.6, size * 1.2);
        editor.Viewport.ZoomAt(new ScreenPoint(500, 400), Viewport.DefaultScale / editor.Viewport.Scale);
        var mark = editor.ScreenOf(0.5, 0);
        var radius = EdgeMarks.Radius(puzzle.Edges[0], EditorGeometry.NodeRadius(GridType.Square) * editor.Viewport.Scale, double.MaxValue, editor.MeasureMarkLabel);
        radius.Should().BeGreaterThan(EdgeMarks.DotRadius(EditorGeometry.NodeRadius(GridType.Square) * editor.Viewport.Scale));

        editor.Click(new ScreenPoint(mark.X, mark.Y + radius - 0.5));
        editor.SelectedEdgeId.Should().Be(1);
        editor.Click(new ScreenPoint(mark.X, mark.Y + radius + 0.5));
        editor.SelectedEdgeId.Should().BeNull();
    }

    [Fact]
    public void Selecting_a_group_in_the_list_shows_its_properties()
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());

        editor.SelectedGroupItem = editor.Groups[3];

        editor.SelectedGroupId.Should().Be(4);
        editor.Properties.Should().BeOfType<GroupPropertiesViewModel>().Which.Label.Should().Be("Zeile 4");
        editor.Click(editor.ScreenOf(new GridPoint(0, 0)));
        editor.SelectedGroupId.Should().BeNull();
        editor.SelectedGroupItem.Should().BeNull();
    }

    [Fact]
    public void Group_list_mirrors_the_puzzle_and_visibility_is_not_an_edit()
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());
        editor.Groups.Should().HaveCount(27);
        editor.Groups[0].Title.Should().Be("Zeile 1 (1)");
        editor.Groups[0].Details.Should().Be("Alle verschieden, 9 Knoten");
        editor.Groups[0].ColorHex.Should().Be(GroupPalette.Colors[0].Hex);

        var visualChanges = 0;
        editor.VisualChanged += (_, _) => visualChanges++;
        editor.Groups[5].IsVisible = false;
        editor.IsGroupVisible(6).Should().BeFalse();
        visualChanges.Should().BeGreaterThan(0);

        editor.HideAllGroupsCommand.Execute(null);
        editor.Groups.Should().OnlyContain(g => !g.IsVisible);
        editor.ShowAllGroupsCommand.Execute(null);
        editor.Groups.Should().OnlyContain(g => g.IsVisible);
        editor.CanUndo.Should().BeFalse();
        editor.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Validation_problems_are_shown_and_updated()
    {
        var editor = EditorDriver.CreateEditor(Row(2));
        editor.SelectNodes([1]);
        editor.NewGroupRule = editor.GroupRules.Single(r => r.TypeName == "AllDifferent");

        editor.CreateGroupFromSelection();

        editor.HasValidationProblems.Should().BeTrue();
        editor.ValidationSummary.Should().Be("1 Problem:");
        editor.ValidationProblems.Should().ContainSingle().Which.Should().Contain("Gruppe 1").And.Contain("mindestens 2 Knoten");

        editor.UndoCommand.Execute(null);
        editor.HasValidationProblems.Should().BeFalse();
        editor.ValidationProblems.Should().BeEmpty();
    }

    [Fact]
    public void Load_puzzle_resets_history_and_selection()
    {
        var editor = EditorDriver.CreateEditor(Row(2));
        editor.SelectNodes([1]);
        editor.DeleteSelection();
        editor.CanUndo.Should().BeTrue();

        editor.LoadPuzzle(ClassicSudoku.Create());

        editor.CanUndo.Should().BeFalse();
        editor.IsDirty.Should().BeFalse();
        editor.SelectedNodeIds.Should().BeEmpty();
        editor.Groups.Should().HaveCount(27);
    }

    [Fact]
    public void Selection_is_pruned_after_undo_removes_nodes()
    {
        var editor = EditorDriver.CreateEditor();
        editor.Tool = EditorTool.PlaceNode;
        editor.Click(new GridPoint(1, 1));
        editor.SelectedNodeIds.Should().Equal(1);

        editor.UndoCommand.Execute(null);

        editor.SelectedNodeIds.Should().BeEmpty();
        editor.Properties.Should().BeOfType<PuzzlePropertiesViewModel>();
    }
}
