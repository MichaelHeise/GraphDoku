namespace GraphDoku.Core.Model;

/// <summary>Gittertyp eines Rätsels. Bestimmt, wie ganzzahlige Koordinaten in Weltkoordinaten übersetzt werden.</summary>
public enum GridType
{
    /// <summary>Quadratgitter; Zelle (x, y) mit Mittelpunkt (x, y).</summary>
    Square,

    /// <summary>Dreiecksgitter; Zelle (x, y) zeigt nach oben, wenn (x + y) gerade ist.</summary>
    Triangle,

    /// <summary>Sechseckgitter (spitze Oberseite) in axialen Koordinaten q = x, r = y.</summary>
    Hex,
}

/// <summary>Übersetzung zwischen <see cref="GridType"/> und den Namen im JSON-Format.</summary>
public static class GridTypeNames
{
    public const string Square = "square";
    public const string Triangle = "triangle";
    public const string Hex = "hex";

    public static IReadOnlyList<string> All { get; } = [Square, Triangle, Hex];

    public static string ToName(GridType grid) => grid switch
    {
        GridType.Square => Square,
        GridType.Triangle => Triangle,
        GridType.Hex => Hex,
        _ => throw new ArgumentOutOfRangeException(nameof(grid), grid, "Unbekannter Gittertyp."),
    };

    public static bool TryParse(string? name, out GridType grid)
    {
        switch (name)
        {
            case Square:
                grid = GridType.Square;
                return true;
            case Triangle:
                grid = GridType.Triangle;
                return true;
            case Hex:
                grid = GridType.Hex;
                return true;
            default:
                grid = default;
                return false;
        }
    }
}
