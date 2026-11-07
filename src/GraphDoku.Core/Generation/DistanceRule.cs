using System.Globalization;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Generation;

/// <summary>Art der Abstandsregel des Kanten-Generators.</summary>
public enum DistanceKind
{
    /// <summary>Direkte Nachbarn: Zellen mit gemeinsamer Kante (Quadrat 4, Dreieck 3, Sechseck 6).</summary>
    Orthogonal,

    /// <summary>Nachbarn inklusive diagonal: Zellen mit gemeinsamer Ecke (Quadrat 8, Dreieck 12, Sechseck 6).</summary>
    WithDiagonals,

    /// <summary>Springerzug (nur Quadratgitter).</summary>
    KnightMove,

    /// <summary>Freier Abstand: Abstand der Zellmittelpunkte in Welteinheiten liegt in [Min, Max] (mit Toleranz).</summary>
    Free,
}

/// <summary>
/// Abstandsregel des Kanten-Generators: Welche Knotenpaare sollen eine Kante bekommen?
/// Nachbarschaften und Springerzug kommen aus <see cref="GridGeometry"/>; der freie Abstand vergleicht die
/// euklidischen Abstände der Zellmittelpunkte (Weltkoordinaten) mit einem Bereich [Min, Max] samt Toleranz.
/// Ein genauer Abstand ist der Bereich [d, d].
/// </summary>
public sealed record DistanceRule
{
    private static readonly NumberFormatInfo GermanNumbers = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };

    private DistanceRule(DistanceKind kind, double minDistance, double maxDistance, double tolerance)
    {
        Kind = kind;
        MinDistance = minDistance;
        MaxDistance = maxDistance;
        Tolerance = tolerance;
    }

    public DistanceKind Kind { get; }

    /// <summary>Kleinster Abstand (nur bei <see cref="DistanceKind.Free"/>).</summary>
    public double MinDistance { get; }

    /// <summary>Größter Abstand (nur bei <see cref="DistanceKind.Free"/>; gleich <see cref="MinDistance"/> = genauer Abstand).</summary>
    public double MaxDistance { get; }

    /// <summary>Toleranz für den Abstandsvergleich (nur bei <see cref="DistanceKind.Free"/>).</summary>
    public double Tolerance { get; }

    public static DistanceRule Orthogonal { get; } = new(DistanceKind.Orthogonal, 0, 0, 0);

    public static DistanceRule WithDiagonals { get; } = new(DistanceKind.WithDiagonals, 0, 0, 0);

    public static DistanceRule KnightMove { get; } = new(DistanceKind.KnightMove, 0, 0, 0);

    /// <summary>Genau der Abstand <paramref name="distance"/> (Welteinheiten), bis auf <paramref name="tolerance"/>.</summary>
    public static DistanceRule Exact(double distance, double tolerance = GridGeometry.DefaultTolerance) =>
        new(DistanceKind.Free, distance, distance, tolerance);

    /// <summary>Abstand zwischen <paramref name="minDistance"/> und <paramref name="maxDistance"/> (jeweils einschließlich, mit Toleranz).</summary>
    public static DistanceRule Range(double minDistance, double maxDistance, double tolerance = GridGeometry.DefaultTolerance) =>
        new(DistanceKind.Free, minDistance, maxDistance, tolerance);

    /// <summary>Deutsche Beschreibung, z. B. „direkte Nachbarn (gemeinsame Kante)“ oder „Abstand 1,5“.</summary>
    public string Description => Kind switch
    {
        DistanceKind.Orthogonal => "direkte Nachbarn (gemeinsame Kante)",
        DistanceKind.WithDiagonals => "Nachbarn inkl. diagonal (gemeinsame Ecke)",
        DistanceKind.KnightMove => "Springerzug",
        _ => MinDistance.Equals(MaxDistance)
            ? $"Abstand {Format(MinDistance)}"
            : $"Abstand {Format(MinDistance)} bis {Format(MaxDistance)}",
    };

    /// <summary>Prüft die Regel für einen Gittertyp. Liefert deutsche Fehlermeldungen (leer = gültig).</summary>
    public IReadOnlyList<string> Validate(GridType grid)
    {
        var errors = new List<string>();
        switch (Kind)
        {
            case DistanceKind.KnightMove when !GridGeometry.SupportsKnightMove(grid):
                errors.Add($"Der Springerzug ist nur für das Quadratgitter definiert, nicht für das Gitter „{GridTypeNames.ToName(grid)}“. Bitte stattdessen einen freien Abstand wählen.");
                break;
            case DistanceKind.Free:
                if (!double.IsFinite(MinDistance) || !double.IsFinite(MaxDistance) || !double.IsFinite(Tolerance))
                {
                    errors.Add("Abstände und Toleranz müssen endliche Zahlen sein.");
                }
                else
                {
                    if (MinDistance <= 0)
                    {
                        errors.Add($"Der Abstand muss größer als 0 sein, ist aber {Format(MinDistance)}.");
                    }

                    if (MaxDistance < MinDistance)
                    {
                        errors.Add($"Der größte Abstand ({Format(MaxDistance)}) ist kleiner als der kleinste ({Format(MinDistance)}).");
                    }

                    if (Tolerance < 0 || (MinDistance > 0 && Tolerance >= MinDistance))
                    {
                        errors.Add($"Die Toleranz muss mindestens 0 und kleiner als der Abstand sein, ist aber {Format(Tolerance)}.");
                    }
                }

                break;
        }

        return errors;
    }

    /// <summary>true, wenn die Zellen <paramref name="a"/> und <paramref name="b"/> die Abstandsregel erfüllen (Regel muss gültig sein).</summary>
    public bool Matches(GridType grid, GridPoint a, GridPoint b)
    {
        if (a == b)
        {
            return false;
        }

        return Kind switch
        {
            DistanceKind.Orthogonal => GridGeometry.GetOrthogonalNeighbors(grid, a.X, a.Y).Contains(b),
            DistanceKind.WithDiagonals => GridGeometry.GetDiagonalNeighbors(grid, a.X, a.Y).Contains(b),
            DistanceKind.KnightMove => GridGeometry.GetKnightMoves(grid, a.X, a.Y).Contains(b),
            _ => IsInRange(GridGeometry.Distance(grid, a, b)),
        };
    }

    internal bool IsInRange(double distance) =>
        distance >= MinDistance - Tolerance && distance <= MaxDistance + Tolerance;

    /// <summary>Gitterpunkte, die von <paramref name="point"/> aus die Regel erfüllen (nicht für <see cref="DistanceKind.Free"/>).</summary>
    internal IReadOnlyList<GridPoint> GetPartners(GridType grid, GridPoint point) => Kind switch
    {
        DistanceKind.Orthogonal => GridGeometry.GetOrthogonalNeighbors(grid, point.X, point.Y),
        DistanceKind.WithDiagonals => GridGeometry.GetDiagonalNeighbors(grid, point.X, point.Y),
        DistanceKind.KnightMove => GridGeometry.GetKnightMoves(grid, point.X, point.Y),
        _ => throw new InvalidOperationException("Beim freien Abstand gibt es keine feste Partnerliste."),
    };

    private static string Format(double value) => value.ToString("0.######", GermanNumbers);
}
