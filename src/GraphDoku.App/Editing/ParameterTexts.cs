using GraphDoku.Core.Rules;
using GraphDoku.Core.Rules.Catalog;

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

    // Hinweise der Katalogregeln, deren Beschreibung im Core JSON-Werte nennt ("exact", false); die Oberfläche zeigt
    // stattdessen die deutschen Texte der Auswahl bzw. des Häkchens. Schlüssel: Name und Core-Beschreibung, damit ein
    // gleichnamiger Parameter einer eigenen Regel seine eigene Beschreibung behält.
    private static readonly Dictionary<(string Name, string Description), string> Hints = new()
    {
        [Key(new DifferenceRule(), "mode")] = "genau k oder mindestens k; Standard: genau k",
        [Key(new SumRule(), "allowRepeats")] = "dürfen sich Werte wiederholen? Standard: nein",
    };

    /// <summary>Deutscher Hinweistext zum Parameter (sonst seine Beschreibung aus dem Core).</summary>
    public static string Hint(RuleParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return Hints.TryGetValue((parameter.Name, parameter.Description), out var hint) ? hint : parameter.Description;
    }

    private static (string Name, string Description) Key(IRule rule, string name) =>
        (name, rule.Parameters.Single(p => p.Name == name).Description);

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
