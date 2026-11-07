using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace GraphDoku.Core.Model;

/// <summary>
/// Parameter einer Regel: eine Zuordnung Name → JSON-Wert mit typisierten Zugriffen.
/// Wird im JSON-Format als Objekt <c>"params"</c> gespeichert.
/// </summary>
public sealed class RuleParams : IEnumerable<KeyValuePair<string, JsonElement>>
{
    private readonly Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);

    public RuleParams()
    {
    }

    public RuleParams(IEnumerable<KeyValuePair<string, JsonElement>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var (name, value) in values)
        {
            _values[name] = value.Clone();
        }
    }

    public int Count => _values.Count;

    public IEnumerable<string> Names => _values.Keys;

    public bool Contains(string name) => _values.ContainsKey(name);

    public bool TryGetValue(string name, out JsonElement value) => _values.TryGetValue(name, out value);

    /// <summary>Liest eine ganze Zahl oder liefert den Standardwert, wenn der Parameter fehlt.</summary>
    /// <exception cref="RuleParamsException">Der Parameter ist vorhanden, aber keine ganze Zahl.</exception>
    public int GetInt(string name, int defaultValue) => GetIntOrNull(name) ?? defaultValue;

    /// <summary>Liest eine ganze Zahl oder <c>null</c>, wenn der Parameter fehlt.</summary>
    /// <exception cref="RuleParamsException">Der Parameter ist vorhanden, aber keine ganze Zahl.</exception>
    public int? GetIntOrNull(string name)
    {
        if (!_values.TryGetValue(name, out var value))
        {
            return null;
        }

        return TryReadInt(value, out var result)
            ? result
            : throw new RuleParamsException($"Parameter „{name}“ muss eine ganze Zahl sein, ist aber {Describe(value)}.");
    }

    /// <summary>Liest einen Wahrheitswert oder liefert den Standardwert, wenn der Parameter fehlt.</summary>
    /// <exception cref="RuleParamsException">Der Parameter ist vorhanden, aber nicht true/false.</exception>
    public bool GetBool(string name, bool defaultValue)
    {
        if (!_values.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new RuleParamsException($"Parameter „{name}“ muss true oder false sein, ist aber {Describe(value)}."),
        };
    }

    /// <summary>Liest eine Zeichenkette oder liefert den Standardwert, wenn der Parameter fehlt.</summary>
    /// <exception cref="RuleParamsException">Der Parameter ist vorhanden, aber keine Zeichenkette.</exception>
    [return: NotNullIfNotNull(nameof(defaultValue))]
    public string? GetString(string name, string? defaultValue)
    {
        if (!_values.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new RuleParamsException($"Parameter „{name}“ muss eine Zeichenkette sein, ist aber {Describe(value)}.");
    }

    public RuleParams Set(string name, int value) => SetRaw(name, JsonSerializer.SerializeToElement(value));

    public RuleParams Set(string name, bool value) => SetRaw(name, JsonSerializer.SerializeToElement(value));

    public RuleParams Set(string name, string value) => SetRaw(name, JsonSerializer.SerializeToElement(value));

    public RuleParams SetRaw(string name, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _values[name] = value.Clone();
        return this;
    }

    public bool Remove(string name) => _values.Remove(name);

    public RuleParams Clone() => new(_values);

    /// <summary>Strukturgleichheit: gleiche Namen und inhaltlich gleiche JSON-Werte.</summary>
    public bool StructurallyEquals(RuleParams? other)
    {
        if (other is null || other.Count != Count)
        {
            return false;
        }

        foreach (var (name, value) in _values)
        {
            if (!other._values.TryGetValue(name, out var otherValue) || !JsonElement.DeepEquals(value, otherValue))
            {
                return false;
            }
        }

        return true;
    }

    public IEnumerator<KeyValuePair<string, JsonElement>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() =>
        "{" + string.Join(", ", _values.Select(kv => $"{kv.Key}: {kv.Value.GetRawText()}")) + "}";

    internal static bool TryReadInt(JsonElement value, out int result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result);
    }

    /// <summary>Kurze deutsche Beschreibung eines JSON-Werts für Fehlermeldungen.</summary>
    internal static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => $"die Zeichenkette \"{value.GetString()}\"",
        JsonValueKind.Number => $"die Zahl {value.GetRawText()}",
        JsonValueKind.True or JsonValueKind.False => $"der Wahrheitswert {value.GetRawText()}",
        JsonValueKind.Null => "null",
        JsonValueKind.Array => "eine Liste",
        JsonValueKind.Object => "ein Objekt",
        _ => value.ValueKind.ToString().ToLower(CultureInfo.InvariantCulture),
    };
}

/// <summary>Ein Regelparameter hat einen unpassenden Typ.</summary>
public sealed class RuleParamsException : Exception
{
    public RuleParamsException()
    {
    }

    public RuleParamsException(string message)
        : base(message)
    {
    }

    public RuleParamsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
