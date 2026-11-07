using GraphDoku.Core.Model;

namespace GraphDoku.Core.Geometry;

/// <summary>Ganzzahlige Gitterkoordinate einer Zelle.</summary>
public readonly record struct GridPoint(int X, int Y);

/// <summary>Punkt in Weltkoordinaten (Einheit: Abstand benachbarter Quadrat- bzw. Sechseck-Mittelpunkte, y wächst nach unten).</summary>
public readonly record struct WorldPoint(double X, double Y)
{
    public double DistanceTo(WorldPoint other) => Math.Sqrt(((X - other.X) * (X - other.X)) + ((Y - other.Y) * (Y - other.Y)));
}

/// <summary>
/// Geometrie der drei Gittertypen: Umrechnung in Weltkoordinaten, Zellumrisse, Nachbarschaften, Springerzug, Abstände.
/// <list type="bullet">
/// <item>Quadrat: Mittelpunkt (x, y), Kantenlänge 1.</item>
/// <item>Dreieck: Kantenlänge 1, Zelle (x, y) zeigt nach oben, wenn (x + y) gerade ist; Mittelpunkte liegen 0,5 auseinander.</item>
/// <item>Sechseck: spitze Oberseite, axiale Koordinaten q = x, r = y; benachbarte Mittelpunkte haben Abstand 1.</item>
/// </list>
/// </summary>
public static class GridGeometry
{
    /// <summary>Standardtoleranz für Abstandsvergleiche in Welteinheiten.</summary>
    public const double DefaultTolerance = 1e-6;

    /// <summary>Höhe einer Dreieckszeile (Kantenlänge 1).</summary>
    public static readonly double TriangleRowHeight = Math.Sqrt(3) / 2;

    private static readonly GridPoint[] SquareOrthogonal = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    private static readonly GridPoint[] SquareDiagonal =
        [new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)];

    private static readonly GridPoint[] HexNeighbors = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, -1), new(-1, 1)];

    private static readonly GridPoint[] KnightMoves =
        [new(1, 2), new(2, 1), new(2, -1), new(1, -2), new(-1, -2), new(-2, -1), new(-2, 1), new(-1, 2)];

    /// <summary>true, wenn die Dreieckszelle (x, y) nach oben zeigt ((x + y) gerade).</summary>
    public static bool IsUpTriangle(int x, int y) => (((x + y) % 2) + 2) % 2 == 0;

    /// <summary>Mittelpunkt der Zelle in Weltkoordinaten (bei Dreiecken: Schwerpunkt).</summary>
    public static WorldPoint ToWorld(GridType grid, int x, int y) => grid switch
    {
        GridType.Square => new WorldPoint(x, y),
        GridType.Hex => new WorldPoint(x + (y / 2.0), y * Math.Sqrt(3) / 2),
        GridType.Triangle => new WorldPoint(
            x / 2.0,
            (y * TriangleRowHeight) + (IsUpTriangle(x, y) ? 2 * TriangleRowHeight / 3 : TriangleRowHeight / 3)),
        _ => throw new ArgumentOutOfRangeException(nameof(grid), grid, "Unbekannter Gittertyp."),
    };

    public static WorldPoint ToWorld(GridType grid, GridPoint point) => ToWorld(grid, point.X, point.Y);

    /// <summary>Eckpunkte der Zelle in Weltkoordinaten (im Uhrzeigersinn bei nach unten wachsendem y).</summary>
    public static IReadOnlyList<WorldPoint> GetCellVertices(GridType grid, int x, int y)
    {
        var center = ToWorld(grid, x, y);
        switch (grid)
        {
            case GridType.Square:
                return
                [
                    new(center.X - 0.5, center.Y - 0.5),
                    new(center.X + 0.5, center.Y - 0.5),
                    new(center.X + 0.5, center.Y + 0.5),
                    new(center.X - 0.5, center.Y + 0.5),
                ];
            case GridType.Hex:
                var radius = 1 / Math.Sqrt(3);
                return [.. Enumerable.Range(0, 6).Select(i =>
                {
                    var angle = (-90 + (60 * i)) * Math.PI / 180;
                    return new WorldPoint(center.X + (radius * Math.Cos(angle)), center.Y + (radius * Math.Sin(angle)));
                })];
            case GridType.Triangle:
                var top = y * TriangleRowHeight;
                var bottom = (y + 1) * TriangleRowHeight;
                return IsUpTriangle(x, y)
                    ? [new(center.X, top), new(center.X + 0.5, bottom), new(center.X - 0.5, bottom)]
                    : [new(center.X - 0.5, top), new(center.X + 0.5, top), new(center.X, bottom)];
            default:
                throw new ArgumentOutOfRangeException(nameof(grid), grid, "Unbekannter Gittertyp.");
        }
    }

    /// <summary>Direkte Nachbarn: Zellen, die eine Kante teilen (Quadrat 4, Dreieck 3, Sechseck 6).</summary>
    public static IReadOnlyList<GridPoint> GetOrthogonalNeighbors(GridType grid, int x, int y) => grid switch
    {
        GridType.Square => Offset(x, y, SquareOrthogonal),
        GridType.Hex => Offset(x, y, HexNeighbors),
        GridType.Triangle => IsUpTriangle(x, y)
            ? [new(x - 1, y), new(x + 1, y), new(x, y + 1)]
            : [new(x - 1, y), new(x + 1, y), new(x, y - 1)],
        _ => throw new ArgumentOutOfRangeException(nameof(grid), grid, "Unbekannter Gittertyp."),
    };

    /// <summary>
    /// Nachbarn inklusive diagonal: Zellen, die mindestens eine Ecke teilen (Quadrat 8, Dreieck 12, Sechseck 6 –
    /// beim Sechseckgitter teilen Eckennachbarn immer auch eine Kante).
    /// </summary>
    public static IReadOnlyList<GridPoint> GetDiagonalNeighbors(GridType grid, int x, int y)
    {
        switch (grid)
        {
            case GridType.Square:
                return Offset(x, y, SquareDiagonal);
            case GridType.Hex:
                return Offset(x, y, HexNeighbors);
            case GridType.Triangle:
                // Die Zeile an der Spitze hat 3 Eckennachbarn, die eigene Zeile 4, die Zeile an der Grundseite 5.
                var up = IsUpTriangle(x, y);
                var apexRow = up ? y - 1 : y + 1;
                var baseRow = up ? y + 1 : y - 1;
                var result = new List<GridPoint>(12);
                for (var dx = -1; dx <= 1; dx++)
                {
                    result.Add(new GridPoint(x + dx, apexRow));
                }

                for (var dx = -2; dx <= 2; dx++)
                {
                    if (dx != 0)
                    {
                        result.Add(new GridPoint(x + dx, y));
                    }
                }

                for (var dx = -2; dx <= 2; dx++)
                {
                    result.Add(new GridPoint(x + dx, baseRow));
                }

                return result;
            default:
                throw new ArgumentOutOfRangeException(nameof(grid), grid, "Unbekannter Gittertyp.");
        }
    }

    /// <summary>Der Springerzug ist nur für das Quadratgitter definiert.</summary>
    public static bool SupportsKnightMove(GridType grid) => grid == GridType.Square;

    /// <summary>Alle Felder, die per Springerzug erreichbar sind.</summary>
    /// <exception cref="NotSupportedException">Für Dreiecks- und Sechseckgitter.</exception>
    public static IReadOnlyList<GridPoint> GetKnightMoves(GridType grid, int x, int y) => SupportsKnightMove(grid)
        ? Offset(x, y, KnightMoves)
        : throw new NotSupportedException(
            $"Der Springerzug ist nur für das Quadratgitter definiert, nicht für das Gitter „{GridTypeNames.ToName(grid)}“. Bitte stattdessen einen freien Abstand wählen.");

    /// <summary>Euklidischer Abstand der Zellmittelpunkte in Welteinheiten.</summary>
    public static double Distance(GridType grid, GridPoint a, GridPoint b) => ToWorld(grid, a).DistanceTo(ToWorld(grid, b));

    /// <summary>true, wenn der Abstand der Mittelpunkte bis auf <paramref name="tolerance"/> gleich <paramref name="distance"/> ist.</summary>
    public static bool IsAtDistance(GridType grid, GridPoint a, GridPoint b, double distance, double tolerance = DefaultTolerance) =>
        Math.Abs(Distance(grid, a, b) - distance) <= tolerance;

    /// <summary>Die Gitterzelle, deren Mittelpunkt dem Weltpunkt am nächsten liegt (zum Einrasten im Editor).</summary>
    public static GridPoint ToNearestGridPoint(GridType grid, WorldPoint point)
    {
        var estimate = grid switch
        {
            GridType.Square => new GridPoint((int)Math.Round(point.X), (int)Math.Round(point.Y)),
            GridType.Hex => EstimateHex(point),
            GridType.Triangle => new GridPoint((int)Math.Round(point.X * 2), (int)Math.Floor(point.Y / TriangleRowHeight)),
            _ => throw new ArgumentOutOfRangeException(nameof(grid), grid, "Unbekannter Gittertyp."),
        };

        var best = estimate;
        var bestDistance = double.MaxValue;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                var candidate = new GridPoint(estimate.X + dx, estimate.Y + dy);
                var distance = ToWorld(grid, candidate).DistanceTo(point);
                if (distance < bestDistance - 1e-12)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    private static GridPoint EstimateHex(WorldPoint point)
    {
        var r = point.Y * 2 / Math.Sqrt(3);
        var q = point.X - (r / 2);
        return new GridPoint((int)Math.Round(q), (int)Math.Round(r));
    }

    private static GridPoint[] Offset(int x, int y, GridPoint[] offsets) =>
        [.. offsets.Select(o => new GridPoint(x + o.X, y + o.Y))];
}
