using System.Globalization;

namespace GraphDoku.App.Solving;

/// <summary>Zeitlimit-Eingabe (Sekunden, Dezimalkomma oder -punkt) und deutsche Zeitangaben für die Oberfläche.</summary>
public static class SolverTimeText
{
    /// <summary>Größtes Zeitlimit, das die Oberfläche annimmt: eine Stunde.</summary>
    public static readonly TimeSpan MaxTimeLimit = TimeSpan.FromHours(1);

    /// <summary>Kleinstes Zeitlimit: 0,1 s (darunter findet CP-SAT selbst für kleine Rätsel kaum etwas).</summary>
    public static readonly TimeSpan MinTimeLimit = TimeSpan.FromSeconds(0.1);

    // Bewusst nicht CultureInfo("de-DE"): gleiche Texte auch ohne ICU-Kulturdaten (wie im Solver, Entscheidung Nr. 45).
    private static readonly NumberFormatInfo GermanNumbers = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };

    /// <summary>Sekunden als Eingabetext, z. B. 30 → „30“, 1,5 s → „1,5“.</summary>
    public static string FormatSeconds(TimeSpan time) => time.TotalSeconds.ToString("0.###", GermanNumbers);

    /// <summary>
    /// Liest ein Zeitlimit in Sekunden („30“, „1,5“, „1.5“, Leerraum erlaubt). Gültig von <see cref="MinTimeLimit"/> bis
    /// <see cref="MaxTimeLimit"/>. Bei Fehler steht in <paramref name="error"/> eine deutsche Meldung.
    /// </summary>
    public static bool TryParseSeconds(string? text, out TimeSpan timeLimit, out string? error)
    {
        timeLimit = default;
        var trimmed = text?.Trim().Replace(',', '.') ?? string.Empty;
        if (trimmed.Length == 0)
        {
            error = "Bitte ein Zeitlimit in Sekunden eingeben.";
            return false;
        }

        if (!double.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds))
        {
            error = $"„{text!.Trim()}“ ist keine Zahl. Bitte das Zeitlimit in Sekunden angeben, z. B. 30 oder 2,5.";
            return false;
        }

        if (seconds < MinTimeLimit.TotalSeconds || seconds > MaxTimeLimit.TotalSeconds)
        {
            error = $"Das Zeitlimit muss zwischen {FormatSeconds(MinTimeLimit)} und {FormatSeconds(MaxTimeLimit)} Sekunden liegen.";
            return false;
        }

        timeLimit = TimeSpan.FromSeconds(seconds);
        error = null;
        return true;
    }

    /// <summary>Dauer als kurzer deutscher Text: unter 1 s in Millisekunden („350 ms“), sonst Sekunden („2,4 s“).</summary>
    public static string Duration(TimeSpan time) => time.TotalSeconds < 1
        ? Math.Max(0, Math.Round(time.TotalMilliseconds)).ToString("0", GermanNumbers) + " ms"
        : time.TotalSeconds.ToString("0.#", GermanNumbers) + " s";
}
