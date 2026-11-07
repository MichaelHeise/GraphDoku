using System.Globalization;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.App.Editing;

/// <summary>
/// Bildschirmmaße der Kantenmarken (Kropki-Punkte, Pfeil) in Pixeln – gemeinsam für das Zeichnen
/// (<c>GraphCanvas</c>) und den Treffertest des Editors, damit ein Klick genau die gezeichnete Marke trifft.
/// </summary>
public static class EdgeMarks
{
    /// <summary>Radius eines unbeschrifteten Punkts relativ zum Knotenradius, damit er auch im engeren Dreiecksgitter zwischen die Kreise passt.</summary>
    public static double DotRadius(double nodeRadius) => Math.Clamp(nodeRadius * 0.32, 3.5, 14);

    /// <summary>Schriftgröße der Beschriftung eines Punkts mit Radius <paramref name="dotRadius"/>.</summary>
    public static double LabelFontSize(double dotRadius) => Math.Max(9, dotRadius * 1.3);

    /// <summary>Radius eines beschrifteten Punkts: groß genug für die Beschriftung (Breite, Höhe in Pixeln) mit Rand.</summary>
    public static double LabeledDotRadius(double dotRadius, double textWidth, double textHeight) =>
        Math.Max(dotRadius, (Math.Max(textWidth, textHeight) / 2) + 3);

    /// <summary>
    /// Halbe Länge des Pfeils („Kleiner als“); höchstens so lang wie der Spalt <paramref name="gap"/> zwischen den Kreisen
    /// (darüber hinaus läge er auf den Werten), aber nie kleiner als 5 px.
    /// </summary>
    public static double ArrowSize(double nodeRadius, double gap) => Math.Max(5, Math.Min(Math.Clamp(nodeRadius * 0.36, 5, 16), gap * 0.42));

    /// <summary>
    /// Beschriftung des Punkts: „Differenz“ k (mit „≥“ bei „mindestens“), nicht bei k = 1; „Verhältnis“ der Faktor, nicht
    /// bei 2. null = ohne Beschriftung (auch bei Regeln ohne Punkt).
    /// </summary>
    public static string? DotLabel(Edge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        switch (edge.Rule)
        {
            case "Difference":
                var k = SafeInt(edge.Params, "k");
                var atLeast = SafeString(edge.Params, "mode") == "atLeast";
                return k == 1 && !atLeast ? null : (atLeast ? "≥" : string.Empty) + (k?.ToString(CultureInfo.InvariantCulture) ?? "?");
            case "Ratio":
                var factor = SafeInt(edge.Params, "factor") ?? 2;
                return factor == 2 ? null : factor.ToString(CultureInfo.InvariantCulture);
            default:
                return null;
        }
    }

    /// <summary>
    /// Radius der gezeichneten Marke um ihren Mittelpunkt (Punkt samt Beschriftung, beim Pfeil seine halbe Länge).
    /// <paramref name="measureLabel"/> misst eine Beschriftung (Text, Schriftgröße → Breite, Höhe); ohne Messung zählt
    /// nur der unbeschriftete Punkt.
    /// </summary>
    public static double Radius(Edge edge, double nodeRadius, double gap, Func<string, double, (double Width, double Height)>? measureLabel)
    {
        ArgumentNullException.ThrowIfNull(edge);
        if (edge.Rule == "LessThan")
        {
            return ArrowSize(nodeRadius, gap);
        }

        var dot = DotRadius(nodeRadius);
        if (measureLabel is null || DotLabel(edge) is not { } label)
        {
            return dot;
        }

        var (width, height) = measureLabel(label, LabelFontSize(dot));
        return LabeledDotRadius(dot, width, height);
    }

    private static int? SafeInt(RuleParams parameters, string name)
    {
        try
        {
            return parameters.GetIntOrNull(name);
        }
        catch (RuleParamsException)
        {
            return null;
        }
    }

    private static string? SafeString(RuleParams parameters, string name)
    {
        try
        {
            return parameters.GetString(name, null);
        }
        catch (RuleParamsException)
        {
            return null;
        }
    }
}
