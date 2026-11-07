using System.Globalization;
using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing;

/// <summary>
/// Anzeige und Eingabe von Werten. Ist der Wertebereich des Rätsels genau 0–15 (Hexadoku), werden 10–15 als A–F
/// angezeigt und eingegeben (Entscheidung Nr. 32, eigene Knotenbereiche: Nr. 150); das Speicherformat bleibt numerisch.
/// </summary>
public static class ValueText
{
    public static bool IsHexDomain(ValueDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return domain.Min == 0 && domain.Max == 15;
    }

    public static string Format(int value, ValueDomain domain) => Format(value, IsHexDomain(domain));

    /// <summary>Wert als Text; mit <paramref name="hex"/> 10–15 als A–F.</summary>
    public static string Format(int value, bool hex) =>
        hex && value is >= 10 and <= 15
            ? ((char)('A' + value - 10)).ToString()
            : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Ob die Werte des Knotens als 0–F angezeigt und eingegeben werden: Entscheidend ist der Bereich des Rätsels
    /// (genau 0–15); ein eigener Bereich des Knotens muss darin liegen (z. B. 8–15 im Hexadoku → weiterhin A–F). Ein
    /// eigener Bereich 0–15 in einem anderen Rätsel schaltet die Hex-Darstellung nicht ein (Entscheidung Nr. 150).
    /// </summary>
    public static bool IsHex(Puzzle puzzle, Node node)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(node);
        var effective = puzzle.GetEffectiveDomain(node);
        return IsHexDomain(puzzle.Domain) && effective.Min >= 0 && effective.Max <= 15;
    }

    /// <summary>Wert eines Knotens als Text (siehe <see cref="IsHex"/>).</summary>
    public static string Format(int value, Puzzle puzzle, Node node) => Format(value, IsHex(puzzle, node));

    /// <summary>Wert eines Hex-Zeichens 0–9, A–F (Groß-/Kleinschreibung egal) oder null.</summary>
    public static int? ParseHexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => null,
    };
}
