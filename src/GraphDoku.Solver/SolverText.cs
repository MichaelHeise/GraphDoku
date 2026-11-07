using System.Globalization;

namespace GraphDoku.Solver;

/// <summary>Gemeinsame deutsche Textbausteine für Solver-Ergebnisse.</summary>
internal static class SolverText
{
    public static string Seconds(TimeSpan time) =>
        time.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s";

    public static string InvalidMessage(IReadOnlyList<string> errors) => errors.Count switch
    {
        0 => "Das Rätsel ist ungültig.",
        1 => $"Das Rätsel ist ungültig: {errors[0]}",
        _ => $"Das Rätsel ist ungültig ({errors.Count} Fehler):{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", errors)}",
    };
}
