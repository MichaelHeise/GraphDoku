using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using GraphDoku.App.Editing;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.App.ViewModels;

/// <summary>
/// Eingabefeld für einen Regelparameter, erzeugt aus der <see cref="RuleParameter"/>-Beschreibung der Regel.
/// Ganze Zahlen als Text (leer = Parameter weglassen), Wahrheitswerte als Häkchen, Auswahlwerte als Liste.
/// Jede gültige Änderung meldet sich über den Rückruf beim Besitzer, der daraus einen Undo-Schritt macht.
/// </summary>
public sealed partial class RuleParameterViewModel : ViewModelBase
{
    private static readonly IReadOnlyList<string> NoChoices = [];
    private readonly Action<RuleParameterViewModel> _changed;
    private bool _loading;

    public RuleParameterViewModel(RuleParameter definition, RuleParams current, Action<RuleParameterViewModel> changed)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(changed);
        Definition = definition;
        _changed = changed;
        Choices = definition.AllowedValues ?? NoChoices;
        Load(current);
    }

    public RuleParameter Definition { get; }

    /// <summary>JSON-Name des Parameters (Schlüssel in <c>params</c>).</summary>
    public string Name => Definition.Name;

    /// <summary>Deutscher Anzeigename (siehe <see cref="ParameterTexts"/>).</summary>
    public string DisplayName => ParameterTexts.Label(Definition.Name);

    public string Label => Definition.IsRequired ? $"{DisplayName} (Pflicht)" : DisplayName;

    /// <summary>Hinweistext unter dem Feld (deutsch, siehe <see cref="ParameterTexts.Hint"/>).</summary>
    public string Description => ParameterTexts.Hint(Definition);

    public bool IsInteger => Definition.Type == RuleParameterType.Integer;

    public bool IsBoolean => Definition.Type == RuleParameterType.Boolean;

    public bool IsChoice => Definition.Type == RuleParameterType.Choice;

    /// <summary>
    /// Erlaubte Werte (JSON-Werte; die Oberfläche zeigt sie über <see cref="ParameterTexts.ChoiceLabel"/> deutsch an).
    /// Dieselbe Listeninstanz wie in der Regelbeschreibung, damit die wiederverwendete Panel-Vorlage beim
    /// Auswahlwechsel keine Liste austauscht und keine Werte zurückschreibt.
    /// </summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>Text einer ganzzahligen Eingabe.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool BoolValue { get; set; }

    [ObservableProperty]
    public partial string? ChoiceValue { get; set; }

    /// <summary>Deutsche Fehlermeldung zur aktuellen Eingabe (null = in Ordnung).</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Übernimmt die Werte aus den Parametern, ohne eine Änderung zu melden.</summary>
    public void Load(RuleParams current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _loading = true;
        try
        {
            current.TryGetValue(Name, out var value);
            var present = current.Contains(Name);
            switch (Definition.Type)
            {
                case RuleParameterType.Integer:
                    var text = !present ? string.Empty
                        : value.ValueKind == JsonValueKind.Number ? value.GetRawText()
                        : value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty
                        : value.GetRawText();

                    // Gleichwertigen Text nicht überschreiben (Cursor bleibt beim Tippen stehen, z. B. bei „007“ oder „ “).
                    // Ungültiger Text ist nie gleichwertig: Er wird wie eine abweichende Zahl auf den Stand des Modells
                    // zurückgesetzt, statt ohne Fehlermeldung stehen zu bleiben.
                    var equivalent = TryParse(Text, out var currentNumber)
                        ? TryParse(text, out var newNumber) && currentNumber == newNumber
                        : string.IsNullOrWhiteSpace(Text) && string.IsNullOrWhiteSpace(text);
                    if (!equivalent)
                    {
                        Text = text;
                    }

                    break;
                case RuleParameterType.Boolean:
                    BoolValue = present
                        ? value.ValueKind == JsonValueKind.True
                        : Definition.DefaultValue as bool? ?? false;
                    break;
                case RuleParameterType.Choice:
                    ChoiceValue = present && value.ValueKind == JsonValueKind.String
                        ? value.GetString()
                        : Definition.DefaultValue as string;
                    break;
            }

            Error = null;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Schreibt den eingegebenen Wert in <paramref name="target"/> (oder entfernt den Parameter). false bei ungültiger Eingabe.</summary>
    public bool TryApplyTo(RuleParams target)
    {
        ArgumentNullException.ThrowIfNull(target);
        switch (Definition.Type)
        {
            case RuleParameterType.Integer:
                if (string.IsNullOrWhiteSpace(Text))
                {
                    target.Remove(Name);
                    return true;
                }

                if (!TryParse(Text, out var number))
                {
                    return false;
                }

                target.Set(Name, number);
                return true;
            case RuleParameterType.Boolean:
                target.Set(Name, BoolValue);
                return true;
            case RuleParameterType.Choice:
                if (ChoiceValue is null)
                {
                    target.Remove(Name);
                }
                else
                {
                    target.Set(Name, ChoiceValue);
                }

                return true;
            default:
                return false;
        }
    }

    partial void OnTextChanged(string value) => Notify(TryParse(value, out _) || string.IsNullOrWhiteSpace(value)
        ? null
        : "Bitte eine ganze Zahl eingeben.");

    partial void OnBoolValueChanged(bool value) => Notify(null);

    partial void OnChoiceValueChanged(string? value) => Notify(null);

    private void Notify(string? error)
    {
        if (_loading)
        {
            return;
        }

        Error = error;
        if (error is null)
        {
            _changed(this);
        }
    }

    private static bool TryParse(string? text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
}
