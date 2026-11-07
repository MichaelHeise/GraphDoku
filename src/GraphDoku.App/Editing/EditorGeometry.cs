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

    /// <summary>
    /// Stelle der Kantenmarke (Kropki-Punkt, Pfeil) auf der Strecke <paramref name="a"/>–<paramref name="b"/> und die
    /// Länge des freien Stücks dort (für die Pfeilgröße). Normalfall: die Mitte, frei ist das Stück zwischen den beiden
    /// Endkreisen. Liegt die Mitte in einem anderen Knotenkreis (<paramref name="others"/>, z. B. Kante zum übernächsten
    /// Knoten), wandert die Marke in das freie Stück der Strecke, dessen Mitte der Streckenmitte am nächsten liegt (bei
    /// Gleichstand das zum Anfang hin) – sonst deckte sie den Wert dieses Knotens ab (die Marken liegen über den Knoten).
    /// Ohne freies Stück bleibt es bei der Mitte.
    /// </summary>
    public static (WorldPoint Position, double Gap) EdgeMarkPosition(WorldPoint a, WorldPoint b, double radius, IEnumerable<WorldPoint> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        var middle = new WorldPoint(a.X + (dx / 2), a.Y + (dy / 2));
        var inner = length - (2 * radius);
        if (length < 1e-9 || inner <= 0)
        {
            return (middle, inner);
        }

        var ux = dx / length;
        var uy = dy / length;

        // Von anderen Kreisen bedeckte Stücke [von, bis] entlang der Strecke (0 = a), nur zwischen den Endkreisen.
        var blocked = new List<(double From, double To)>();
        foreach (var c in others)
        {
            var t = ((c.X - a.X) * ux) + ((c.Y - a.Y) * uy);
            var d = Math.Abs(((c.X - a.X) * uy) - ((c.Y - a.Y) * ux));
            if (d >= radius)
            {
                continue;
            }

            var half = Math.Sqrt((radius * radius) - (d * d));
            var from = Math.Max(radius, t - half);
            var to = Math.Min(length - radius, t + half);
            if (to > from)
            {
                blocked.Add((from, to));
            }
        }

        if (blocked.Count == 0)
        {
            return (middle, inner);
        }

        var free = new List<(double From, double To)>();
        var position = radius;
        foreach (var (from, to) in blocked.OrderBy(x => x.From))
        {
            if (from > position)
            {
                free.Add((position, from));
            }

            position = Math.Max(position, to);
        }

        if (length - radius > position)
        {
            free.Add((position, length - radius));
        }

        var mid = length / 2;
        if (free.FirstOrDefault(f => f.From <= mid && mid <= f.To) is { To: > 0 } containing)
        {
            // Mitte frei (die anderen Kreise streifen die Strecke nur): Marke bleibt dort, Platz nach beiden Seiten.
            return (middle, 2 * Math.Min(mid - containing.From, containing.To - mid));
        }

        if (free.Count == 0)
        {
            return (middle, inner);
        }

        var best = free.MinBy(f => Math.Abs(((f.From + f.To) / 2) - mid) + (1e-9 * f.From));
        var at = (best.From + best.To) / 2;
        return (new WorldPoint(a.X + (ux * at), a.Y + (uy * at)), best.To - best.From);
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
