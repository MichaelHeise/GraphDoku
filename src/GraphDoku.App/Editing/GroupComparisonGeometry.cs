using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing;

/// <summary>
/// Lage der Verbindung eines Gruppenvergleichs auf der Zeichenfläche: eine gestrichelte Linie zwischen den
/// Schwerpunkten der Knotenmittelpunkte beider Gruppen, darauf ein Etikett mit dem Operator. Gemeinsam für das
/// Zeichnen (<c>GraphCanvas</c>) und den Treffertest des Editors, damit beide immer übereinstimmen.
/// </summary>
public static class GroupComparisonGeometry
{
    /// <summary>Ab diesem Abstand (Welt) zum nächsten Hindernis gilt eine Stelle als frei; weiter weg zählt nicht mehr.</summary>
    private const double ClearanceCap = 1.0;

    /// <summary>Abstand der Kandidaten entlang der Verbindung (Welt).</summary>
    private const double SampleStep = 0.05;

    /// <summary>Schwerpunkt der Knotenmittelpunkte (jede Position einmal); <c>null</c> ohne Knoten.</summary>
    public static WorldPoint? Anchor(GridType grid, IEnumerable<Node> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var centers = members.DistinctBy(n => (n.X, n.Y)).Select(n => EditorGeometry.Center(grid, n)).ToList();
        return centers.Count == 0 ? null : new WorldPoint(centers.Average(c => c.X), centers.Average(c => c.Y));
    }

    /// <summary>Schriftgröße des Operator-Etiketts in Pixeln (wächst mit dem Zoom, in Grenzen).</summary>
    public static double BadgeFontSize(double scale) => Math.Clamp(scale * 0.24, 10, 18);

    /// <summary>Halbe Seitenlänge des (quadratischen) Etiketts in Pixeln; das Symbol ist ein einzelnes Zeichen.</summary>
    public static double BadgeHalfSize(double scale) => BadgeFontSize(scale) * 0.8;

    /// <summary>Liegt der Bildschirmversatz (dx, dy) vom Etikettmittelpunkt auf dem Etikett, mit Toleranz in Pixeln?</summary>
    public static bool BadgeContains(double scale, double dx, double dy, double tolerance = 0)
    {
        var half = BadgeHalfSize(scale) + tolerance;
        return Math.Abs(dx) <= half && Math.Abs(dy) <= half;
    }

    /// <summary>
    /// Angezeigtes Symbol und Drehung (Grad, im Uhrzeigersinn auf dem Bildschirm) des Operator-Etiketts.
    /// (<paramref name="dx"/>, <paramref name="dy"/>) ist die Richtung von der linken zur rechten Gruppe (Bildschirm,
    /// y nach unten). Bei &lt;, ≤, &gt;, ≥ liegt das Zeichen längs der Verbindung, seine Spitze zeigt immer zur kleineren
    /// Seite: Zeigt die Verbindung nach links, steht das gespiegelte Zeichen (&lt; ↔ &gt;, ≤ ↔ ≥) um 180° weniger gedreht –
    /// so bleibt die Schrift aufrecht (Drehung zwischen −90° und 90°). = und ≠ bleiben ungedreht, ebenso alles bei einer
    /// Verbindung ohne Länge.
    /// </summary>
    public static (string Symbol, double Rotation) BadgeGlyph(ComparisonOperator op, double dx, double dy)
    {
        var symbol = GroupComparisonTexts.Symbol(op);
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (op is not (ComparisonOperator.Less or ComparisonOperator.LessOrEqual or ComparisonOperator.Greater or ComparisonOperator.GreaterOrEqual)
            || length < 1e-12)
        {
            return (symbol, 0);
        }

        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        if (dx >= -1e-9 * length)
        {
            return (symbol, angle);
        }

        var mirrored = op switch
        {
            ComparisonOperator.Less => ComparisonOperator.Greater,
            ComparisonOperator.LessOrEqual => ComparisonOperator.GreaterOrEqual,
            ComparisonOperator.Greater => ComparisonOperator.Less,
            _ => ComparisonOperator.LessOrEqual,
        };
        return (GroupComparisonTexts.Symbol(mirrored), angle > 0 ? angle - 180 : angle + 180);
    }

    /// <summary>
    /// Ordnet die Etiketten aller gezeigten Vergleiche gemeinsam an (Ergebnis in derselben Reihenfolge wie
    /// <paramref name="connectors"/>, Welt). Jedes Etikett sitzt auf seiner Verbindung im mittleren Teil (25 % bis 75 %)
    /// an der Stelle mit dem größten Abstand zum nächsten <paramref name="obstacles"/>-Punkt (Knotenmittelpunkte mit
    /// Wert und Kandidaten, Kantenmitten mit ihren Marken); bei Gleichstand an der zur Mitte nächsten Stelle. Bei Zellen
    /// landet es so auf einer Zellecke statt auf einer Randmitte. Überdeckt es ein früheres Etikett, nimmt es die
    /// nächstbeste freie Stelle seiner Verbindung; gibt es keine (gleiche Verbindung, zu kurz), rückt es quer zur
    /// Verbindung zur Seite. Eine Verbindung ohne Länge (beide Schwerpunkte gleich) bekommt eine nahe Stelle neben dem
    /// Schwerpunkt. <paramref name="badgeHalfSize"/> ist die halbe Etikettgröße in Welt (samt gewünschtem Abstand).
    /// </summary>
    public static IReadOnlyList<WorldPoint> LayoutBadges(
        IReadOnlyList<(WorldPoint From, WorldPoint To)> connectors,
        IEnumerable<WorldPoint> obstacles,
        double badgeHalfSize)
    {
        ArgumentNullException.ThrowIfNull(connectors);
        ArgumentNullException.ThrowIfNull(obstacles);
        var buckets = new Dictionary<(long, long), List<WorldPoint>>();
        foreach (var point in obstacles)
        {
            var key = BucketOf(point);
            if (!buckets.TryGetValue(key, out var list))
            {
                buckets[key] = list = [];
            }

            list.Add(point);
        }

        var placed = new List<WorldPoint>(connectors.Count);
        foreach (var (from, to) in connectors)
        {
            var ranked = Candidates(from, to)
                .Select((c, index) => (c.Point, c.Offset, Index: index, Score: Math.Round(Clearance(buckets, c.Point), 6)))
                .OrderByDescending(c => c.Score)
                .ThenBy(c => Math.Round(c.Offset, 9))
                .ThenBy(c => c.Index)
                .Select(c => c.Point)
                .ToList();
            var free = ranked.Where(p => !Overlaps(placed, p, badgeHalfSize)).Select(p => (WorldPoint?)p).FirstOrDefault();
            placed.Add(free ?? Sideways(placed, ranked[0], from, to, badgeHalfSize));
        }

        return placed;
    }

    /// <summary>
    /// Kandidaten mit ihrem Abstand zur Mitte: gleichmäßig auf dem mittleren Teil der Verbindung, ohne Länge auf drei
    /// Kreisen um den Schwerpunkt (der Schwerpunkt selbst zuerst).
    /// </summary>
    private static IEnumerable<(WorldPoint Point, double Offset)> Candidates(WorldPoint from, WorldPoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-9)
        {
            yield return (from, 0);
            // Bei Zellen erreicht der äußere Kreis die Zellecken (Abstand 0,71), der mittlere die Randmitten.
            foreach (var radius in new[] { 0.7, 0.5, 0.35 })
            {
                for (var i = 0; i < 16; i++)
                {
                    var angle = i * Math.PI / 8;
                    yield return (new WorldPoint(from.X + (radius * Math.Cos(angle)), from.Y + (radius * Math.Sin(angle))), radius);
                }
            }

            yield break;
        }

        var count = Math.Clamp((int)Math.Ceiling(length * 0.5 / SampleStep), 10, 400);
        for (var k = 0; k <= count; k++)
        {
            var t = 0.25 + (0.5 * k / count);
            yield return (new WorldPoint(from.X + (dx * t), from.Y + (dy * t)), Math.Abs(t - 0.5) * length);
        }
    }

    /// <summary>Abstand zum nächsten Hindernis, höchstens <see cref="ClearanceCap"/> (Eimer der Kantenlänge 1).</summary>
    private static double Clearance(Dictionary<(long, long), List<WorldPoint>> buckets, WorldPoint point)
    {
        var (bx, by) = BucketOf(point);
        var best = ClearanceCap;
        for (var x = bx - 1; x <= bx + 1; x++)
        {
            for (var y = by - 1; y <= by + 1; y++)
            {
                if (buckets.TryGetValue((x, y), out var list))
                {
                    foreach (var obstacle in list)
                    {
                        best = Math.Min(best, obstacle.DistanceTo(point));
                    }
                }
            }
        }

        return best;
    }

    private static (long, long) BucketOf(WorldPoint point) => ((long)Math.Floor(point.X), (long)Math.Floor(point.Y));

    private static bool Overlaps(List<WorldPoint> placed, WorldPoint point, double half) =>
        placed.Exists(p => Math.Abs(p.X - point.X) < 2 * half && Math.Abs(p.Y - point.Y) < 2 * half);

    /// <summary>Keine freie Stelle auf der Verbindung: quer zur Verbindung abwechselnd zu beiden Seiten ausweichen.</summary>
    private static WorldPoint Sideways(List<WorldPoint> placed, WorldPoint best, WorldPoint from, WorldPoint to, double half)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        var (nx, ny) = length < 1e-9 ? (0.0, 1.0) : (-dy / length, dx / length);
        var step = 2 * half * 1.05;
        for (var k = 1; k <= 64; k++)
        {
            var distance = ((k + 1) / 2) * step * (k % 2 == 1 ? 1 : -1);
            var point = new WorldPoint(best.X + (nx * distance), best.Y + (ny * distance));
            if (!Overlaps(placed, point, half))
            {
                return point;
            }
        }

        return best;
    }
}

/// <summary>Ein gezeigter Gruppenvergleich mit seiner Verbindung und der Mitte seines Etiketts (Welt).</summary>
public readonly record struct GroupComparisonBadge(GroupComparison Comparison, WorldPoint From, WorldPoint To, WorldPoint Center);
