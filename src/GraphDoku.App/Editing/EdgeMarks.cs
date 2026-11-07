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
    /// Umriss des Pfeils relativ zu seiner Mitte in Vielfachen von <see cref="ArrowSize"/>: je (längs der Kante, quer
    /// dazu) die Spitze, eine hintere Ecke, die Kerbe und die andere hintere Ecke.
    /// </summary>
    public static IReadOnlyList<(double Along, double Across)> ArrowOutline { get; } = [(1, 0), (-1, 0.75), (-0.45, 0), (-1, -0.75)];

    /// <summary>Abstand der hinteren Pfeilecken von der Mitte in Vielfachen von <see cref="ArrowSize"/>: √(1² + 0,75²).</summary>
    private const double ArrowCornerReach = 1.25;

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
    /// Radius des Kreises um den Mittelpunkt, der die gezeichnete Marke umschließt (Punkt samt Beschriftung, beim Pfeil
    /// bis zu seinen hinteren Ecken).
    /// <paramref name="measureLabel"/> misst eine Beschriftung (Text, Schriftgröße → Breite, Höhe); ohne Messung zählt
    /// nur der unbeschriftete Punkt.
    /// </summary>
    public static double Radius(Edge edge, double nodeRadius, double gap, Func<string, double, (double Width, double Height)>? measureLabel)
    {
        ArgumentNullException.ThrowIfNull(edge);
        if (edge.Rule == "LessThan")
        {
            return ArrowSize(nodeRadius, gap) * ArrowCornerReach;
        }

        var dot = DotRadius(nodeRadius);
        if (measureLabel is null || DotLabel(edge) is not { } label)
        {
            return dot;
        }

        var (width, height) = measureLabel(label, LabelFontSize(dot));
        return LabeledDotRadius(dot, width, height);
    }

    /// <summary>
    /// Liegt ein Punkt auf der gezeichneten Marke? <paramref name="dx"/>/<paramref name="dy"/> ist sein Abstand von der
    /// Markenmitte in Pixeln, <paramref name="unitX"/>/<paramref name="unitY"/> die Richtung der Kante (Einheitsvektor
    /// von „from“ nach „to“). Ein Punkt zählt in seinem Kreis (<see cref="Radius"/>), ein Pfeil genau in seinem Umriss
    /// (<see cref="ArrowOutline"/>) – ohne Toleranz, denn die Marke liegt nur dort über den Knoten, wo sie gezeichnet ist.
    /// </summary>
    public static bool Contains(
        Edge edge,
        double nodeRadius,
        double gap,
        Func<string, double, (double Width, double Height)>? measureLabel,
        double dx,
        double dy,
        double unitX,
        double unitY)
    {
        ArgumentNullException.ThrowIfNull(edge);
        if (edge.Rule == "LessThan")
        {
            return ArrowDistance(nodeRadius, gap, dx, dy, unitX, unitY) == 0;
        }

        var radius = Radius(edge, nodeRadius, gap, measureLabel);
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    /// <summary>
    /// Abstand eines Punkts vom gezeichneten Pfeil in Pixeln: 0 in seinem Umriss (<see cref="ArrowOutline"/>), sonst bis
    /// zum nächsten Rand des Umrisses – nicht bis zu seinem Umkreis (<see cref="Radius"/>), dessen leere Ecken sonst
    /// nahe Linien überdeckten (Nachprüfung PR #1, A1). Parameter wie bei <see cref="Contains"/>.
    /// </summary>
    public static double ArrowDistance(double nodeRadius, double gap, double dx, double dy, double unitX, double unitY)
    {
        var size = ArrowSize(nodeRadius, gap);
        var along = ((dx * unitX) + (dy * unitY)) / size;
        var across = ((dy * unitX) - (dx * unitY)) / size; // Querrichtung (-unitY, unitX) wie beim Zeichnen
        if (InPolygon(along, across, ArrowOutline))
        {
            return 0;
        }

        var nearest = double.MaxValue;
        for (int i = 0, j = ArrowOutline.Count - 1; i < ArrowOutline.Count; j = i++)
        {
            nearest = Math.Min(nearest, DistanceToSegment(along, across, ArrowOutline[j], ArrowOutline[i]));
        }

        return nearest * size;
    }

    /// <summary>Abstand des Punkts (x, y) von der Strecke a–b.</summary>
    private static double DistanceToSegment(double x, double y, (double X, double Y) a, (double X, double Y) b)
    {
        var (sx, sy) = (b.X - a.X, b.Y - a.Y);
        var lengthSquared = (sx * sx) + (sy * sy);
        var t = lengthSquared < 1e-18 ? 0 : Math.Clamp((((x - a.X) * sx) + ((y - a.Y) * sy)) / lengthSquared, 0, 1);
        var (px, py) = (a.X + (t * sx) - x, a.Y + (t * sy) - y);
        return Math.Sqrt((px * px) + (py * py));
    }

    /// <summary>Punkt im Vieleck (Strahlverfahren, gerade/ungerade Kreuzungszahl).</summary>
    private static bool InPolygon(double x, double y, IReadOnlyList<(double X, double Y)> polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (xi, yi) = polygon[i];
            var (xj, yj) = polygon[j];
            if ((yi > y) != (yj > y) && x < ((xj - xi) * (y - yi) / (yj - yi)) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
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
