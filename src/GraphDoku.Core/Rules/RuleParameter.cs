namespace GraphDoku.Core.Rules;

/// <summary>Datentyp eines Regelparameters.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Bezeichnet bewusst den Datentyp des Parameters.")]
public enum RuleParameterType
{
    Integer,
    Boolean,

    /// <summary>Zeichenkette aus einer festen Auswahl (<see cref="RuleParameter.AllowedValues"/>).</summary>
    Choice,
}

/// <summary>
/// Beschreibung eines Regelparameters.
/// </summary>
/// <param name="Name">Name im JSON-Objekt <c>params</c>.</param>
/// <param name="Type">Datentyp.</param>
/// <param name="Description">Kurze deutsche Beschreibung (für Editor und Fehlermeldungen).</param>
/// <param name="IsRequired">Pflichtparameter ohne Standardwert.</param>
/// <param name="DefaultValue">Wert, der gilt, wenn der Parameter fehlt (<c>null</c>: Pflicht oder kontextabhängig, siehe Beschreibung).</param>
/// <param name="SuggestedValue">Vorschlag für neu angelegte Regeln im Editor (nur für Pflichtparameter sinnvoll).</param>
/// <param name="AllowedValues">Erlaubte Werte bei <see cref="RuleParameterType.Choice"/>.</param>
public sealed record RuleParameter(
    string Name,
    RuleParameterType Type,
    string Description,
    bool IsRequired = false,
    object? DefaultValue = null,
    object? SuggestedValue = null,
    IReadOnlyList<string>? AllowedValues = null);
