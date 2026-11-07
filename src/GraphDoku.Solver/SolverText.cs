using System.Globalization;

namespace GraphDoku.Solver;

/// <summary>Gemeinsame deutsche Textbausteine für Solver-Ergebnisse.</summary>
internal static class SolverText
{
    /// <summary>
    /// Deutsches Zahlenformat (Dezimalkomma). Bewusst aus der invarianten Kultur abgeleitet statt „de-DE“ zu laden,
    /// damit die Texte auch ohne ICU-Kulturdaten (z. B. schlanke Linux-Container) gleich aussehen.
    /// </summary>
    private static readonly NumberFormatInfo GermanNumbers = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };

    /// <summary>
    /// Zeitdauer als deutscher Text: ab 1 s in Sekunden (z. B. „1,5 s“), darunter in Millisekunden (z. B. „300 ms“),
    /// unter 1 ms mit so vielen Nachkommastellen, dass ein positives Limit nie als „0“ erscheint (1 Tick = 0,0001 ms).
    /// </summary>
    public static string Duration(TimeSpan time)
    {
        if (time.TotalSeconds >= 1 || time <= TimeSpan.Zero)
        {
            return time.TotalSeconds.ToString("0.###", GermanNumbers) + " s";
        }

        var format = time.TotalMilliseconds >= 1 ? "0.###" : "0.####";
        return time.TotalMilliseconds.ToString(format, GermanNumbers) + " ms";
    }

    public static string InvalidMessage(IReadOnlyList<string> errors) => errors.Count switch
    {
        0 => "Das Rätsel ist ungültig.",
        1 => $"Das Rätsel ist ungültig: {errors[0]}",
        _ => $"Das Rätsel ist ungültig ({errors.Count} Fehler):{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", errors)}",
    };
}
