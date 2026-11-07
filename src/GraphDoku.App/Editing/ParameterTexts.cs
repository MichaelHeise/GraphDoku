namespace GraphDoku.App.Editing;

/// <summary>
/// Deutsche Anzeigetexte für Regelparameter und ihre Auswahlwerte im Eigenschaften-Panel. Die JSON-Bezeichner
/// (<c>k</c>, <c>mode</c>, <c>atLeast</c> …) bleiben im Modell unverändert; unbekannte Namen (eigene Regeln) werden
/// unverändert angezeigt.
/// </summary>
public static class ParameterTexts
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["k"] = "Abstand k",
        ["mode"] = "Modus",
        ["factor"] = "Faktor",
        ["target"] = "Zielsumme",
        ["allowRepeats"] = "Wiederholungen erlaubt",
        ["min"] = "Kleinster Wert",
        ["max"] = "Größter Wert",
    };

    private static readonly Dictionary<string, string> Choices = new(StringComparer.Ordinal)
    {
        ["exact"] = "genau k",
        ["atLeast"] = "mindestens k",
    };

    /// <summary>Deutscher Name des Parameters (sonst der JSON-Name).</summary>
    public static string Label(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Labels.TryGetValue(name, out var label) ? label : name;
    }

    /// <summary>Deutscher Text eines Auswahlwerts (sonst der Wert selbst).</summary>
    public static string ChoiceLabel(string? value) =>
        value is null ? string.Empty : Choices.TryGetValue(value, out var label) ? label : value;
}
