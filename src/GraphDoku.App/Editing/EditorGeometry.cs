using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing;

/// <summary>
/// Darstellung der Knoten auf der Zeichenfläche (Paket 7): als Kreis oder als Zelle in der Form des Gitters (Quadrat,
/// Dreieck nach oben/unten, Sechseck). Reine Ansichtseinstellung, nicht Teil des Rätsels und nie in der Rätseldatei.
/// </summary>
public enum NodeShape
{
    /// <summary>Knoten als Kreise (Standard, Festlegung der Spezifikation).</summary>
    Circle,

    /// <summary>Knoten als Zellen des Gitters (Quadrat, Dreieck oder Sechseck je nach Gittertyp).</summary>
    Cell,
}

/// <summary>Maße und Hilfsfunktionen der Zeichenfläche in Weltkoordinaten.</summary>
public static class EditorGeometry
{
    /// <summary>Radius der Knotenkreise. Dreiecksmittelpunkte liegen enger (Abstand 1/√3), daher kleiner.</summary>
    public static double NodeRadius(GridType grid) => grid == GridType.Triangle ? 0.2 : 0.38;

    /// <summary>
    /// Radius des Inhalts eines Knotens (Wert, Kandidatenraster, Marken, Thermometer): bei Kreisen der Kreisradius, bei
    /// Zellen etwas weniger als der Innenkreis der Zelle (Quadrat 0,5, Dreieck 1/(2√3) ≈ 0,29, Sechseck 0,5 – die
    /// Ecken des Sechsecks lassen weniger Platz für das Raster, daher 0,4).
    /// </summary>
    public static double ContentRadius(GridType grid, NodeShape shape) => shape == NodeShape.Circle
        ? NodeRadius(grid)
        : grid switch
        {
            GridType.Triangle => 0.24,
            GridType.Hex => 0.4,
            _ => 0.42,
        };

    /// <summary>
    /// Freihalteradius um einen Knotenmittelpunkt für Kantenlinien und -marken: bei Kreisen der Kreis, bei Zellen das
    /// Innere mit Wert und Kandidaten (80 % des Inhaltsradius) – Linien laufen dort nur über den Zellrand.
    /// </summary>
    public static double ClearRadius(GridType grid, NodeShape shape) => shape == NodeShape.Circle
        ? NodeRadius(grid)
        : ContentRadius(grid, shape) * 0.8;

    /// <summary>Zellen: Länge eines Kantenstegs über einem Zellrand (die Hälfte des Inhaltsradius).</summary>
    public static double CellEdgeStubLength(GridType grid) => ContentRadius(grid, NodeShape.Cell) * 0.5;

    /// <summary>
    /// Zellen: sichtbare Stücke einer Kante – ein kurzer Steg (<see cref="CellEdgeStubLength"/>) über jedem Zellrand,
    /// den die Strecke a–b kreuzt (<see cref="BorderCrossings"/>), ohne die Freihaltekreise um die Enden und die
    /// <paramref name="others"/>. Bei Nachbarzellen ist das ein Steg über die Mitte der gemeinsamen Seite, bei Zellen,
    /// die sich nur in einer Ecke berühren, einer über der Ecke, bei längeren Kanten einer pro Zellrand. So laufen keine
    /// Linien durch das Innere fremder Zellen.
    /// </summary>
    public static IReadOnlyList<(WorldPoint From, WorldPoint To)> CellEdgeStubs(GridType grid, WorldPoint a, WorldPoint b, IEnumerable<WorldPoint> others) =>
        [.. CellEdgeStubTargets(grid, a, b, others).Select(s => (s.From, s.To))];

    /// <summary>
    /// Zellen: die Stege aus <see cref="CellEdgeStubs"/> samt der Stelle, an der die Kante den Zellrand kreuzt
    /// (<c>Crossing</c>, auf den Steg geklemmt, falls ein Freihaltekreis ihn kürzt) – für den Treffertest, der nur
    /// rund um diese Stelle greift (Nr. 158).
    /// </summary>
    public static IReadOnlyList<(WorldPoint From, WorldPoint To, WorldPoint Crossing)> CellEdgeStubTargets(GridType grid, WorldPoint a, WorldPoint b, IEnumerable<WorldPoint> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        var length = a.DistanceTo(b);
        var clear = ClearRadius(grid, NodeShape.Cell);
        if (length < 1e-9 || length <= 2 * clear)
        {
            return [];
        }

        var free = FreeIntervals(a, b, clear, others, out _);
        var half = CellEdgeStubLength(grid) / 2;
        WorldPoint At(double t) => new(a.X + ((b.X - a.X) * t / length), a.Y + ((b.Y - a.Y) * t / length));
        var result = new List<(WorldPoint From, WorldPoint To, WorldPoint Crossing)>();
        foreach (var crossing in BorderCrossings(grid, a, b))
        {
            foreach (var (freeFrom, freeTo) in free)
            {
                var from = Math.Max(crossing - half, freeFrom);
                var to = Math.Min(crossing + half, freeTo);
                if (to - from > 1e-6)
                {
                    result.Add((At(from), At(to), At(Math.Clamp(crossing, from, to))));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Stellen (Abstand von <paramref name="a"/>), an denen die Strecke a–b einen Zellrand des Gitters kreuzt, aufsteigend;
    /// Durchgänge durch eine Ecke zählen einmal. Läuft von Zelle zu Zelle (Austritt aus dem konvexen Umriss), daher
    /// unabhängig von der Länge der Strecke nur so viele Schritte wie gekreuzte Zellen.
    /// </summary>
    public static IReadOnlyList<double> BorderCrossings(GridType grid, WorldPoint a, WorldPoint b)
    {
        var length = a.DistanceTo(b);
        if (length < 1e-9)
        {
            return [];
        }

        var ux = (b.X - a.X) / length;
        var uy = (b.Y - a.Y) / length;
        var target = GridGeometry.ToNearestGridPoint(grid, b);
        var cell = GridGeometry.ToNearestGridPoint(grid, a);
        var crossings = new List<double>();
        var t = 0.0;
        for (var guard = 0; guard < 100000 && cell != target; guard++)
        {
            var vertices = GridGeometry.GetCellVertices(grid, cell.X, cell.Y);
            var center = GridGeometry.ToWorld(grid, cell);
            var exit = double.MaxValue;
            for (var i = 0; i < vertices.Count; i++)
            {
                var p = vertices[i];
                var q = vertices[(i + 1) % vertices.Count];

                // Nach außen zeigende Normale der Seite p–q; nur Seiten, auf die die Strecke zuläuft, kommen als Austritt in Frage.
                var (nx, ny) = (q.Y - p.Y, p.X - q.X);
                if ((nx * (((p.X + q.X) / 2) - center.X)) + (ny * (((p.Y + q.Y) / 2) - center.Y)) < 0)
                {
                    (nx, ny) = (-nx, -ny);
                }

                var toward = (nx * ux) + (ny * uy);
                if (toward <= 1e-12)
                {
                    continue;
                }

                var s = ((nx * (p.X - a.X)) + (ny * (p.Y - a.Y))) / toward;
                if (s > t + 1e-9)
                {
                    exit = Math.Min(exit, s);
                }
            }

            if (exit >= length)
            {
                break;
            }

            if (crossings.Count == 0 || exit - crossings[^1] > 1e-4)
            {
                crossings.Add(exit);
            }

            t = exit;
            cell = GridGeometry.ToNearestGridPoint(grid, new WorldPoint(a.X + (ux * (t + 1e-6)), a.Y + (uy * (t + 1e-6))));
        }

        return crossings;
    }

    /// <summary>Umriss der Zelle eines Knotens (Weltkoordinaten).</summary>
    public static IReadOnlyList<WorldPoint> CellVertices(GridType grid, Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return GridGeometry.GetCellVertices(grid, node.X, node.Y);
    }

    /// <summary>
    /// true, wenn der Punkt im Knoten liegt: bei Kreisen im Knotenkreis, bei Zellen im Zellumriss (Rand eingeschlossen).
    /// </summary>
    public static bool Contains(GridType grid, NodeShape shape, Node node, WorldPoint point)
    {
        ArgumentNullException.ThrowIfNull(node);
        return shape == NodeShape.Circle
            ? Center(grid, node).DistanceTo(point) <= NodeRadius(grid)
            : PolygonContains(CellVertices(grid, node), point);
    }

    /// <summary>true, wenn der Punkt im konvexen Vieleck liegt (Rand eingeschlossen, Umlaufsinn beliebig).</summary>
    public static bool PolygonContains(IReadOnlyList<WorldPoint> polygon, WorldPoint point)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        if (polygon.Count < 3)
        {
            return false;
        }

        var sign = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var cross = ((b.X - a.X) * (point.Y - a.Y)) - ((b.Y - a.Y) * (point.X - a.X));
            if (Math.Abs(cross) < 1e-12)
            {
                continue;
            }

            var current = Math.Sign(cross);
            if (sign != 0 && current != sign)
            {
                return false;
            }

            sign = current;
        }

        return true;
    }

    /// <summary>
    /// Vieleck zur Mitte hin um <paramref name="inset"/> verkleinert (zentrische Streckung, gleiche Einheit wie die
    /// Ecken) – für Rahmen, die innerhalb der Zelle bleiben sollen und nicht in die Nachbarzelle ragen.
    /// </summary>
    public static IReadOnlyList<WorldPoint> Inset(IReadOnlyList<WorldPoint> polygon, WorldPoint center, double inset)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var apothem = double.MaxValue;
        for (var i = 0; i < polygon.Count; i++)
        {
            apothem = Math.Min(apothem, DistanceToSegment(center, polygon[i], polygon[(i + 1) % polygon.Count]));
        }

        var factor = apothem is > 1e-12 and < double.MaxValue ? Math.Max(0, 1 - (inset / apothem)) : 1;
        return [.. polygon.Select(v => new WorldPoint(center.X + ((v.X - center.X) * factor), center.Y + ((v.Y - center.Y) * factor)))];
    }

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
    /// Ohne freies Stück bleibt es bei der Mitte. Bei Zellen (Paket 7) ist <paramref name="radius"/> der Freihalteradius
    /// (<see cref="ClearRadius"/>); die Marke liegt dann auf dem Rand zwischen zwei Zellen, bei Nachbarzellen genau auf
    /// der Mitte der gemeinsamen Seite.
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
        var free = FreeIntervals(a, b, radius, others, out var blocked);
        if (!blocked)
        {
            return (middle, inner);
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

    /// <summary>
    /// Freie Stücke [von, bis] entlang a–b (0 = a) zwischen den Endkreisen, ohne die von anderen Kreisen bedeckten.
    /// <paramref name="blocked"/>: ob überhaupt ein anderer Kreis die Strecke bedeckt.
    /// </summary>
    private static List<(double From, double To)> FreeIntervals(WorldPoint a, WorldPoint b, double radius, IEnumerable<WorldPoint> others, out bool blocked)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        var ux = dx / length;
        var uy = dy / length;

        // Von anderen Kreisen bedeckte Stücke [von, bis] entlang der Strecke (0 = a), nur zwischen den Endkreisen.
        var covered = new List<(double From, double To)>();
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
                covered.Add((from, to));
            }
        }

        blocked = covered.Count > 0;
        var free = new List<(double From, double To)>();
        var position = radius;
        foreach (var (from, to) in covered.OrderBy(x => x.From))
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

        return free;
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
