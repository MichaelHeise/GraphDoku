using GraphDoku.App.Editing;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;

namespace GraphDoku.App.Tests;

/// <summary>
/// Geometrie der Gruppenvergleiche: Leserichtung des Operator-Etiketts (Spitze zur kleineren Seite, Schrift aufrecht) und
/// gemeinsame Anordnung der Etiketten (weg von Knotenmittelpunkten, ohne einander zu überdecken).
/// </summary>
public class GroupComparisonGeometryTests
{
    private static readonly ComparisonOperator[] Ordered =
        [ComparisonOperator.Less, ComparisonOperator.LessOrEqual, ComparisonOperator.Greater, ComparisonOperator.GreaterOrEqual];

    /// <summary>Gitterpunkte 0..size-1 × 0..size-1 (Quadratgitter: Weltpunkt = Gitterpunkt).</summary>
    private static List<WorldPoint> Square(int size) =>
        [.. Enumerable.Range(0, size).SelectMany(y => Enumerable.Range(0, size).Select(x => new WorldPoint(x, y)))];

    private static double Nearest(IEnumerable<WorldPoint> obstacles, WorldPoint point) => obstacles.Min(o => o.DistanceTo(point));

    [Theory]
    [InlineData(ComparisonOperator.Less, 1, 0, "<", 0)]
    [InlineData(ComparisonOperator.Less, -1, 0, ">", 0)]
    [InlineData(ComparisonOperator.Greater, -2, 0, "<", 0)]
    [InlineData(ComparisonOperator.Greater, 0, 1, ">", 90)]
    [InlineData(ComparisonOperator.LessOrEqual, 0, -1, "≤", -90)]
    [InlineData(ComparisonOperator.GreaterOrEqual, -1, -1, "≤", 45)]
    [InlineData(ComparisonOperator.LessOrEqual, -1, 1, "≥", -45)]
    [InlineData(ComparisonOperator.Less, 1, 1, "<", 45)]
    [InlineData(ComparisonOperator.Equal, -1, 1, "=", 0)]
    [InlineData(ComparisonOperator.NotEqual, 0, 1, "≠", 0)]
    [InlineData(ComparisonOperator.Less, 0, 0, "<", 0)]
    public void Badge_glyph_lies_along_the_connector_and_stays_upright(ComparisonOperator op, double dx, double dy, string symbol, double rotation)
    {
        var (shown, angle) = GroupComparisonGeometry.BadgeGlyph(op, dx, dy);
        shown.Should().Be(symbol);
        angle.Should().BeApproximately(rotation, 1e-9);
    }

    [Fact]
    public void Badge_glyph_tip_always_points_at_the_smaller_side()
    {
        foreach (var op in Ordered)
        {
            for (var degrees = -179; degrees <= 180; degrees += 7)
            {
                var direction = degrees * Math.PI / 180;
                var (dx, dy) = (Math.Cos(direction), Math.Sin(direction));
                var (symbol, rotation) = GroupComparisonGeometry.BadgeGlyph(op, dx, dy);
                rotation.Should().BeInRange(-90, 90, "die Schrift bleibt aufrecht");

                // Ungedreht zeigt die Spitze von „<“/„≤“ nach links (−x), die von „>“/„≥“ nach rechts (+x).
                var tip = (rotation + (symbol is "<" or "≤" ? 180 : 0)) * Math.PI / 180;
                var leftIsSmaller = op is ComparisonOperator.Less or ComparisonOperator.LessOrEqual;

                // Richtung zur kleineren Seite: zur linken Gruppe gegen die Verbindung, zur rechten mit ihr.
                var smaller = direction + (leftIsSmaller ? Math.PI : 0);
                var agreement = (Math.Cos(tip) * Math.Cos(smaller)) + (Math.Sin(tip) * Math.Sin(smaller));
                agreement.Should().BeApproximately(1, 1e-9, $"{op} bei {degrees}°");
            }
        }
    }

    [Fact]
    public void Badge_without_obstacles_sits_in_the_middle()
    {
        var badges = GroupComparisonGeometry.LayoutBadges([(new WorldPoint(0, 0), new WorldPoint(10, 0))], [], 0.2);
        badges.Should().Equal(new WorldPoint(5, 0));
    }

    [Fact]
    public void Badge_avoids_node_centers_and_lands_on_a_cell_corner_on_a_diagonal()
    {
        // Die Kandidaten liegen im Abstand 0,05 auf der Verbindung: genau auf der Ecke liegt nicht unbedingt einer.
        var obstacles = Square(5);
        var badge = GroupComparisonGeometry.LayoutBadges([(new WorldPoint(0, 0), new WorldPoint(4, 4))], obstacles, 0.2).Single();
        Nearest(obstacles, badge).Should().BeApproximately(Math.Sqrt(0.5), 0.03, "eine Zellecke, nicht die Mitte (2, 2) auf einem Knoten");
        badge.X.Should().BeApproximately(badge.Y, 1e-9, "das Etikett bleibt auf der Verbindung");
        Math.Abs(badge.X - 2).Should().BeApproximately(0.5, 0.03, "eine der beiden zur Mitte nächsten Ecken");

        // Waagerecht durch eine Knotenreihe: auf der Strecke ist die Randmitte die freieste Stelle.
        var row = GroupComparisonGeometry.LayoutBadges([(new WorldPoint(0, 2), new WorldPoint(4, 2))], obstacles, 0.2).Single();
        Nearest(obstacles, row).Should().BeApproximately(0.5, 1e-6);
        row.Y.Should().Be(2);
    }

    [Fact]
    public void Badge_avoids_edge_marks_at_edge_midpoints_too()
    {
        // Senkrechte Verbindung zwischen zwei Knotenspalten: ohne Marken ist (0,5; 1,5) zwischen vier Knoten die freieste
        // Stelle. Liegt dort eine Marke, weicht das Etikett auf eine Stelle aus, die Knoten und Marke meidet.
        var nodes = new List<WorldPoint> { new(0, 0), new(1, 0), new(0, 1), new(1, 1), new(0, 2), new(1, 2), new(0, 3), new(1, 3) };
        var marks = new List<WorldPoint> { new(0.5, 1.5) };
        var connector = (new WorldPoint(0.5, 0), new WorldPoint(0.5, 3));
        var free = GroupComparisonGeometry.LayoutBadges([connector], nodes, 0.2).Single();
        var withMarks = GroupComparisonGeometry.LayoutBadges([connector], [.. nodes, .. marks], 0.2).Single();
        free.Should().Be(new WorldPoint(0.5, 1.5));
        withMarks.X.Should().Be(0.5);
        Nearest(marks, withMarks).Should().BeGreaterThan(0.5);
        Nearest(nodes, withMarks).Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void Zero_length_connector_puts_the_badge_next_to_the_center()
    {
        var obstacles = Square(5);
        var badge = GroupComparisonGeometry.LayoutBadges([(new WorldPoint(2, 2), new WorldPoint(2, 2))], obstacles, 0.2).Single();
        badge.Should().NotBe(new WorldPoint(2, 2));
        badge.DistanceTo(new WorldPoint(2, 2)).Should().BeLessThanOrEqualTo(0.7 + 1e-9, "nahe am Schwerpunkt");
        Nearest(obstacles, badge).Should().BeApproximately(0.7, 1e-6, "auf einer Zellecke neben dem Knoten");

        // Ohne Hindernis bleibt es auf dem Schwerpunkt.
        GroupComparisonGeometry.LayoutBadges([(new WorldPoint(2, 2), new WorldPoint(2, 2))], [], 0.2)
            .Should().Equal(new WorldPoint(2, 2));
    }

    [Fact]
    public void Crossing_connectors_of_rows_and_columns_get_separate_badges()
    {
        // Klassisch 9×9: Zeile 1 gegen Zeile 9 und Spalte 1 gegen Spalte 9 – beide Mitten lägen auf (4, 4).
        var obstacles = Square(9);
        var connectors = new[] { (new WorldPoint(4, 0), new WorldPoint(4, 8)), (new WorldPoint(0, 4), new WorldPoint(8, 4)) };
        const double half = 0.2;
        var badges = GroupComparisonGeometry.LayoutBadges(connectors, obstacles, half);
        badges.Should().HaveCount(2);
        badges[0].X.Should().Be(4, "auf der eigenen Verbindung");
        badges[1].Y.Should().Be(4, "auf der eigenen Verbindung");
        foreach (var badge in badges)
        {
            Nearest(obstacles, badge).Should().BeApproximately(0.5, 1e-6, "zwischen zwei Knoten statt auf einem");
        }

        Separated(badges, half).Should().BeTrue();
    }

    [Fact]
    public void Identical_connectors_get_offsets_so_that_every_badge_stays_visible()
    {
        var obstacles = new List<WorldPoint> { new(0, 0), new(1, 0) };
        var connector = (new WorldPoint(0, 0), new WorldPoint(1, 0));
        const double half = 0.3;
        var badges = GroupComparisonGeometry.LayoutBadges([connector, connector, connector, connector], obstacles, half);
        badges[0].Should().Be(new WorldPoint(0.5, 0), "das erste Etikett bekommt die beste Stelle");
        Separated(badges, half).Should().BeTrue("kein Etikett überdeckt ein anderes");
        badges.Select(b => b.X).Should().AllSatisfy(x => x.Should().BeApproximately(0.5, 1e-9), "zu kurz: quer zur Verbindung ausgewichen");

        // Lange gleiche Verbindung: Die Etiketten verteilen sich entlang der Verbindung.
        var along = GroupComparisonGeometry.LayoutBadges([(new WorldPoint(0, 0), new WorldPoint(8, 0)), (new WorldPoint(0, 0), new WorldPoint(8, 0))], [], half);
        along.Should().OnlyContain(b => b.Y == 0);
        Separated(along, half).Should().BeTrue();
    }

    [Fact]
    public void Layout_is_deterministic()
    {
        var obstacles = Square(6);
        var connectors = new[]
        {
            (new WorldPoint(3.3, 1.3), new WorldPoint(2, 3.5)),
            (new WorldPoint(0.3, 2.7), new WorldPoint(0, 4.5)),
            (new WorldPoint(2, 3.5), new WorldPoint(4.5, 3)),
        };
        GroupComparisonGeometry.LayoutBadges(connectors, obstacles, 0.2)
            .Should().Equal(GroupComparisonGeometry.LayoutBadges(connectors, obstacles.AsEnumerable().Reverse(), 0.2));
    }

    private static bool Separated(IReadOnlyList<WorldPoint> badges, double half)
    {
        for (var i = 0; i < badges.Count; i++)
        {
            for (var j = i + 1; j < badges.Count; j++)
            {
                if (Math.Abs(badges[i].X - badges[j].X) < 2 * half && Math.Abs(badges[i].Y - badges[j].Y) < 2 * half)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
