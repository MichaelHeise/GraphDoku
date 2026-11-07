namespace GraphDoku.App.Editing;

/// <summary>Benannte Farbe für Gruppen.</summary>
public sealed record PaletteColor(string Name, string Hex);

/// <summary>Farbpalette für Gruppen. Gruppen ohne eigene Farbe werden nach ihrer Id eingefärbt.</summary>
public static class GroupPalette
{
    public static IReadOnlyList<PaletteColor> Colors { get; } =
    [
        new("Blau", "#3b82f6"),
        new("Orange", "#f97316"),
        new("Grün", "#16a34a"),
        new("Rot", "#dc2626"),
        new("Violett", "#9333ea"),
        new("Türkis", "#0891b2"),
        new("Pink", "#db2777"),
        new("Oliv", "#65a30d"),
        new("Braun", "#a16207"),
        new("Grau", "#64748b"),
    ];

    /// <summary>Darzustellende Farbe: eigene Farbe der Gruppe, sonst eine Palettenfarbe nach Id.</summary>
    public static string EffectiveColor(string? color, int groupId) =>
        !string.IsNullOrWhiteSpace(color) && IsValidHex(color)
            ? color
            : Colors[(((groupId - 1) % Colors.Count) + Colors.Count) % Colors.Count].Hex;

    /// <summary>true für #rgb, #rrggbb oder #aarrggbb.</summary>
    public static bool IsValidHex(string? color) =>
        color is { Length: 4 or 7 or 9 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
}
