using System.Globalization;
using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing;

/// <summary>
/// Anzeige und Eingabe von Werten. Ist der Wertebereich genau 0–15 (Hexadoku), werden 10–15 als A–F angezeigt und
/// eingegeben (Entscheidung Nr. 32); das Speicherformat bleibt numerisch.
/// </summary>
public static class ValueText
{
    public static bool IsHexDomain(ValueDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return domain.Min == 0 && domain.Max == 15;
    }

    public static string Format(int value, ValueDomain domain) =>
        IsHexDomain(domain) && value is >= 10 and <= 15
            ? ((char)('A' + value - 10)).ToString()
            : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Wert eines Hex-Zeichens 0–9, A–F (Groß-/Kleinschreibung egal) oder null.</summary>
    public static int? ParseHexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => null,
    };
}
