using GraphDoku.App.Editing;
using GraphDoku.App.Editing.Commands;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;

namespace GraphDoku.App.Tests;

public class EditHistoryTests
{
    private static AddNodesCommand AddNode(int id, object? key = null) => new([new Node(id, id, 0)], key);

    [Fact]
    public void Execute_undo_redo_and_redo_is_cleared_by_new_edit()
    {
        var puzzle = new Puzzle();
        var history = new EditHistory(puzzle);

        history.Execute(AddNode(1));
        history.Execute(AddNode(2));
        puzzle.Nodes.Select(n => n.Id).Should().Equal(1, 2);
        history.UndoDescription.Should().Be("Knoten setzen");

        history.Undo().Should().BeTrue();
        puzzle.Nodes.Select(n => n.Id).Should().Equal(1);
        history.CanRedo.Should().BeTrue();

        history.Redo().Should().BeTrue();
        puzzle.Nodes.Select(n => n.Id).Should().Equal(1, 2);

        history.Undo();
        history.Execute(AddNode(3));
        history.CanRedo.Should().BeFalse();
        puzzle.Nodes.Select(n => n.Id).Should().Equal(1, 3);

        history.Undo();
        history.Undo();
        history.Undo().Should().BeFalse();
        history.Redo().Should().BeTrue();
    }

    [Fact]
    public void Commands_with_the_same_merge_key_form_one_undo_step()
    {
        var puzzle = new Puzzle();
        var history = new EditHistory(puzzle);
        var key = new object();

        history.Execute(AddNode(1, key));
        history.Execute(AddNode(2, key));
        history.Execute(AddNode(3, new object()));

        history.UndoCount.Should().Be(2);
        history.Undo();
        history.Undo();
        puzzle.Nodes.Should().BeEmpty();
        history.Redo();
        puzzle.Nodes.Select(n => n.Id).Should().Equal(1, 2);
    }

    [Fact]
    public void Dirty_state_follows_the_saved_position()
    {
        var history = new EditHistory(new Puzzle());
        history.IsDirty.Should().BeFalse();

        history.Execute(AddNode(1));
        history.IsDirty.Should().BeTrue();
        history.MarkSaved();
        history.IsDirty.Should().BeFalse();

        history.Execute(AddNode(2));
        history.IsDirty.Should().BeTrue();
        history.Undo();
        history.IsDirty.Should().BeFalse("der gespeicherte Stand ist wieder erreicht");
        history.Undo();
        history.IsDirty.Should().BeTrue();
        history.Redo();
        history.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Merging_into_the_saved_step_makes_the_document_dirty()
    {
        var history = new EditHistory(new Puzzle());
        var key = new object();
        history.Execute(AddNode(1, key));
        history.MarkSaved();

        history.Execute(AddNode(2, key));

        history.UndoCount.Should().Be(1);
        history.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void Changed_event_is_raised_for_every_change()
    {
        var history = new EditHistory(new Puzzle());
        var count = 0;
        history.Changed += (_, _) => count++;

        history.Execute(AddNode(1));
        history.Undo();
        history.Redo();
        history.MarkSaved();

        count.Should().Be(4);
    }
}

public class ViewportTests
{
    [Fact]
    public void World_and_screen_coordinates_round_trip()
    {
        var viewport = new Viewport();
        viewport.SetSize(800, 600);
        viewport.PanBy(13, -7);
        viewport.ZoomAt(new ScreenPoint(100, 100), 1.7);

        var world = new WorldPoint(3.25, -1.5);
        var back = viewport.ToWorld(viewport.ToScreen(world));
        back.X.Should().BeApproximately(world.X, 1e-9);
        back.Y.Should().BeApproximately(world.Y, 1e-9);
    }

    [Fact]
    public void Zoom_keeps_the_world_point_under_the_cursor()
    {
        var viewport = new Viewport();
        viewport.SetSize(800, 600);
        var anchor = new ScreenPoint(321, 123);
        var before = viewport.ToWorld(anchor);

        viewport.ZoomAt(anchor, 2.5);

        viewport.Scale.Should().BeApproximately(Viewport.DefaultScale * 2.5, 1e-9);
        var after = viewport.ToScreen(before);
        after.X.Should().BeApproximately(anchor.X, 1e-9);
        after.Y.Should().BeApproximately(anchor.Y, 1e-9);
    }

    [Fact]
    public void Zoom_is_clamped()
    {
        var viewport = new Viewport();
        viewport.ZoomAt(new ScreenPoint(0, 0), 1e6);
        viewport.Scale.Should().Be(Viewport.MaxScale);
        viewport.ZoomAt(new ScreenPoint(0, 0), 1e-9);
        viewport.Scale.Should().Be(Viewport.MinScale);
    }

    [Fact]
    public void FitTo_centers_the_content()
    {
        var viewport = new Viewport();
        viewport.SetSize(1000, 500);

        viewport.FitTo(new Bounds(0, 0, 10, 10), marginPixels: 50);

        viewport.Scale.Should().BeApproximately(40, 1e-9);
        var center = viewport.ToScreen(new WorldPoint(5, 5));
        center.X.Should().BeApproximately(500, 1e-9);
        center.Y.Should().BeApproximately(250, 1e-9);
    }

    [Fact]
    public void Pan_moves_the_offset()
    {
        var viewport = new Viewport();
        var before = viewport.ToScreen(new WorldPoint(1, 1));
        viewport.PanBy(30, -20);
        var after = viewport.ToScreen(new WorldPoint(1, 1));
        (after.X - before.X).Should().BeApproximately(30, 1e-9);
        (after.Y - before.Y).Should().BeApproximately(-20, 1e-9);
    }
}

public class EditorGeometryTests
{
    [Theory]
    [InlineData(GridType.Square, 4)]
    [InlineData(GridType.Triangle, 3)]
    [InlineData(GridType.Hex, 6)]
    public void Outline_of_a_single_cell_is_its_polygon(GridType grid, int segments)
    {
        GroupOutline.BoundarySegments(grid, [new GridPoint(2, 3)]).Should().HaveCount(segments);
    }

    [Fact]
    public void Outline_of_a_block_omits_inner_edges()
    {
        var block = from y in Enumerable.Range(0, 3) from x in Enumerable.Range(0, 3) select new GridPoint(x, y);
        var segments = GroupOutline.BoundarySegments(GridType.Square, block);

        segments.Should().HaveCount(12);
        segments.Should().OnlyContain(s => s.A.X == -0.5 || s.A.X == 2.5 || s.A.Y == -0.5 || s.A.Y == 2.5);
    }

    [Fact]
    public void Outline_of_neighbouring_hexagons_shares_one_edge()
    {
        GroupOutline.BoundarySegments(GridType.Hex, [new GridPoint(0, 0), new GridPoint(1, 0)]).Should().HaveCount(10);
    }

    [Fact]
    public void Shared_boundaries_count_groups_per_segment_and_emphasise_classic_blocks()
    {
        var puzzle = Core.Templates.ClassicSudoku.Create();
        var nodes = puzzle.Nodes.ToDictionary(n => n.Id);
        var groups = puzzle.Groups.Select(g => g.Nodes.Select(id => new GridPoint(nodes[id].X, nodes[id].Y))).ToList();

        var shared = GroupOutline.SharedBoundaries(GridType.Square, groups);

        SharedSegment At(double x1, double y1, double x2, double y2) => shared.Single(s =>
            (s.A, s.B) == (new WorldPoint(x1, y1), new WorldPoint(x2, y2)) || (s.A, s.B) == (new WorldPoint(x2, y2), new WorldPoint(x1, y1)));

        At(0.5, -0.5, 0.5, 0.5).Count.Should().Be(2, "Zellgrenze: Spalte 1 und Spalte 2");
        At(2.5, -0.5, 2.5, 0.5).Count.Should().Be(4, "Blockgrenze: zwei Spalten und zwei Blöcke");
        At(-0.5, -0.5, 0.5, -0.5).Count.Should().Be(3, "Rand: Zeile, Spalte, Block");
        shared.Should().OnlyContain(s => s.Count >= 2 && s.Owner == -1);
    }

    [Fact]
    public void Shared_boundaries_name_the_single_owner()
    {
        GridPoint[] left = [new(0, 0)];
        GridPoint[] right = [new(1, 0)];

        var shared = GroupOutline.SharedBoundaries(GridType.Square, [left, right]);

        shared.Should().HaveCount(7);
        shared.Should().ContainSingle(s => s.Count == 2).Which.Owner.Should().Be(-1);
        shared.Where(s => s.Count == 1).Select(s => s.Owner).Should().BeEquivalentTo([0, 0, 0, 1, 1, 1]);
    }

    [Fact]
    public void Parameter_texts_are_german_and_fall_back_to_the_json_name()
    {
        ParameterTexts.Label("k").Should().Be("Abstand k");
        ParameterTexts.Label("mode").Should().Be("Modus");
        ParameterTexts.Label("target").Should().Be("Zielsumme");
        ParameterTexts.Label("eigenerParameter").Should().Be("eigenerParameter");
        ParameterTexts.ChoiceLabel("exact").Should().Be("genau k");
        ParameterTexts.ChoiceLabel("atLeast").Should().Be("mindestens k");
        ParameterTexts.ChoiceLabel("sonstwas").Should().Be("sonstwas");
        ParameterTexts.ChoiceLabel(null).Should().BeEmpty();

        // Jeder Parameter der Katalogregeln hat einen deutschen Namen, jeder Auswahlwert einen deutschen Text.
        foreach (var parameter in Core.Rules.RuleRegistry.Default.Rules.SelectMany(r => r.Parameters))
        {
            ParameterTexts.Label(parameter.Name).Should().NotBe(parameter.Name, parameter.Name);
            foreach (var value in parameter.AllowedValues ?? [])
            {
                ParameterTexts.ChoiceLabel(value).Should().NotBe(value, value);
            }
        }
    }

    [Fact]
    public void Snap_for_move_keeps_triangle_orientation()
    {
        var origin = new GridPoint(0, 0); // zeigt nach oben
        foreach (var x in Enumerable.Range(-3, 7))
        {
            foreach (var y in Enumerable.Range(-3, 7))
            {
                var target = GridGeometry.ToWorld(GridType.Triangle, x, y);
                var snapped = EditorGeometry.SnapForMove(GridType.Triangle, target, origin);
                GridGeometry.IsUpTriangle(snapped.X, snapped.Y).Should().BeTrue();
                if (GridGeometry.IsUpTriangle(x, y))
                {
                    snapped.Should().Be(new GridPoint(x, y));
                }
            }
        }
    }

    [Theory]
    [InlineData(GridType.Square)]
    [InlineData(GridType.Hex)]
    public void Snap_for_move_is_the_nearest_point_on_square_and_hex(GridType grid)
    {
        var world = new WorldPoint(2.2, 3.1);
        EditorGeometry.SnapForMove(grid, world, new GridPoint(0, 0)).Should().Be(GridGeometry.ToNearestGridPoint(grid, world));
    }

    [Fact]
    public void Distance_to_segment()
    {
        var a = new WorldPoint(0, 0);
        var b = new WorldPoint(4, 0);
        EditorGeometry.DistanceToSegment(new WorldPoint(2, 3), a, b).Should().BeApproximately(3, 1e-12);
        EditorGeometry.DistanceToSegment(new WorldPoint(-3, 4), a, b).Should().BeApproximately(5, 1e-12);
        EditorGeometry.DistanceToSegment(new WorldPoint(1, 1), a, a).Should().BeApproximately(Math.Sqrt(2), 1e-12);
    }

    [Fact]
    public void Content_bounds_cover_all_cells()
    {
        var puzzle = new Puzzle();
        EditorGeometry.ContentBounds(puzzle).Should().BeNull();
        puzzle.Nodes.Add(new Node(1, 0, 0));
        puzzle.Nodes.Add(new Node(2, 3, 2));
        EditorGeometry.ContentBounds(puzzle).Should().Be(new Bounds(-0.5, -0.5, 3.5, 2.5));
    }

    [Fact]
    public void Triangle_nodes_are_smaller_than_the_spacing_of_neighbours()
    {
        var spacing = GridGeometry.Distance(GridType.Triangle, new GridPoint(0, 0), new GridPoint(1, 0));
        (2 * EditorGeometry.NodeRadius(GridType.Triangle)).Should().BeLessThan(spacing);
        (2 * EditorGeometry.NodeRadius(GridType.Square)).Should().BeLessThan(1);
    }
}

public class ValueTextTests
{
    [Theory]
    [InlineData(0, 15, 10, "A")]
    [InlineData(0, 15, 15, "F")]
    [InlineData(0, 15, 9, "9")]
    [InlineData(1, 16, 10, "10")]
    [InlineData(1, 9, 5, "5")]
    public void Formats_hex_only_for_the_0_to_15_domain(int min, int max, int value, string expected)
    {
        ValueText.Format(value, new ValueDomain(min, max)).Should().Be(expected);
    }

    [Theory]
    [InlineData('0', 0)]
    [InlineData('9', 9)]
    [InlineData('a', 10)]
    [InlineData('F', 15)]
    [InlineData('g', null)]
    public void Parses_hex_digits(char c, int? expected) => ValueText.ParseHexDigit(c).Should().Be(expected);
}

public class GroupPaletteTests
{
    [Fact]
    public void Uses_own_color_or_palette_by_id()
    {
        GroupPalette.EffectiveColor("#ff8800", 1).Should().Be("#ff8800");
        GroupPalette.EffectiveColor(null, 1).Should().Be(GroupPalette.Colors[0].Hex);
        GroupPalette.EffectiveColor("kaputt", 2).Should().Be(GroupPalette.Colors[1].Hex);
        GroupPalette.EffectiveColor(null, GroupPalette.Colors.Count + 1).Should().Be(GroupPalette.Colors[0].Hex);
    }
}
