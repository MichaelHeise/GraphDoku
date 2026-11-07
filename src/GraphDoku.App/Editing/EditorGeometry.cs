using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing;

/// <summary>Maße und Hilfsfunktionen der Zeichenfläche in Weltkoordinaten.</summary>
public static class EditorGeometry
{
    /// <summary>Radius der Knotenkreise. Dreiecksmittelpunkte liegen enger (Abstand 1/√3), daher kleiner.</summary>
    public static double NodeRadius(GridType grid) => grid == GridType.Triangle ? 0.2 : 0.38;

    public static WorldPoint Center(GridType grid, Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return GridGeometry.ToWorld(grid, node.X, node.Y);
    }

    /// <summary>
    /// Gitterpunkt zum Einrasten beim Verschieben: der zum Weltpunkt nächste Punkt, auf dem Dreiecksgitter aber nur
    /// solche mit gleicher Ausrichtung wie <paramref name="origin"/> (gerade Verschiebung in x + y), damit eine
    /// verschobene Knotengruppe ihre Form behält.
    /// </summary>
    public static GridPoint SnapForMove(GridType grid, WorldPoint target, GridPoint origin)
    {
        var nearest = GridGeometry.ToNearestGridPoint(grid, target);
        if (grid != GridType.Triangle || IsEven(nearest.X - origin.X + nearest.Y - origin.Y))
        {
            return nearest;
        }

        var best = nearest;
        var bestDistance = double.MaxValue;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                var candidate = new GridPoint(nearest.X + dx, nearest.Y + dy);
                if (!IsEven(candidate.X - origin.X + candidate.Y - origin.Y))
                {
                    continue;
                }

                var distance = GridGeometry.ToWorld(grid, candidate).DistanceTo(target);
                if (distance < bestDistance - 1e-12)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Zeilenweise Reihenfolge (Rahmenauswahl): nach Gitterzeile (y ist bei allen drei Gittern die Zeile), innerhalb
    /// einer Zeile nach der waagerechten Weltposition, bei Gleichstand nach Id. Nicht nach der Bildschirmhöhe des
    /// Mittelpunkts: Auf dem Dreiecksgitter liegen die Schwerpunkte von Auf- und Ab-Dreiecken einer Zeile auf
    /// verschiedener Höhe, die Zeile würde sonst im Zickzack zerfallen.
    /// </summary>
    public static IEnumerable<Node> RowWiseOrder(GridType grid, IEnumerable<Node> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return nodes
            .OrderBy(n => n.Y)
            .ThenBy(n => Math.Round(GridGeometry.ToWorld(grid, n.X, n.Y).X, 6))
            .ThenBy(n => n.Id);
    }

    /// <summary>Abstand des Punkts <paramref name="p"/> zur Strecke a–b.</summary>
    public static double DistanceToSegment(WorldPoint p, WorldPoint a, WorldPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared < 1e-12)
        {
            return p.DistanceTo(a);
        }

        var t = Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared, 0, 1);
        return p.DistanceTo(new WorldPoint(a.X + (t * dx), a.Y + (t * dy)));
    }

    /// <summary>Umgebendes Rechteck aller Zellen (Weltkoordinaten) oder null bei leerem Rätsel.</summary>
    public static Bounds? ContentBounds(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        if (puzzle.Nodes.Count == 0)
        {
            return null;
        }

        var vertices = puzzle.Nodes.SelectMany(n => GridGeometry.GetCellVertices(puzzle.Grid, n.X, n.Y)).ToList();
        return new Bounds(vertices.Min(v => v.X), vertices.Min(v => v.Y), vertices.Max(v => v.X), vertices.Max(v => v.Y));
    }

    private static bool IsEven(int value) => value % 2 == 0;
}

/// <summary>Deutsche Namen der Gittertypen.</summary>
public static class GridNames
{
    public static string ToGerman(GridType grid) => grid switch
    {
        GridType.Square => "Quadrat",
        GridType.Triangle => "Dreieck",
        GridType.Hex => "Sechseck",
        _ => grid.ToString(),
    };
}
