using System.Diagnostics.CodeAnalysis;
using GraphDoku.Core.Rules.Catalog;

namespace GraphDoku.Core.Rules;

/// <summary>
/// Zuordnung Typname → Regel. Unveränderlich; neue Regeln werden über <see cref="With"/> ergänzt,
/// ohne dass sich das JSON-Format ändert.
/// </summary>
public sealed class RuleRegistry
{
    private readonly Dictionary<string, IRule> _rules;

    public RuleRegistry(IEnumerable<IRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = new Dictionary<string, IRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (!_rules.TryAdd(rule.TypeName, rule))
            {
                throw new ArgumentException($"Der Regeltyp „{rule.TypeName}“ ist mehrfach registriert.", nameof(rules));
            }
        }
    }

    /// <summary>Registry mit den acht Regeln des Katalogs (Version 1).</summary>
    public static RuleRegistry Default { get; } = new(
    [
        new AllDifferentRule(),
        new CoverRangeRule(),
        new SumRule(),
        new ThermometerRule(),
        new NotEqualRule(),
        new DifferenceRule(),
        new RatioRule(),
        new LessThanRule(),
    ]);

    public IReadOnlyCollection<IRule> Rules => _rules.Values;

    public IEnumerable<string> TypeNames => _rules.Keys;

    public bool Contains(string typeName) => _rules.ContainsKey(typeName);

    public bool TryGet(string typeName, [NotNullWhen(true)] out IRule? rule) => _rules.TryGetValue(typeName, out rule);

    /// <exception cref="KeyNotFoundException">Unbekannter Typname.</exception>
    public IRule Get(string typeName) => TryGet(typeName, out var rule)
        ? rule
        : throw new KeyNotFoundException(UnknownRuleMessage(typeName));

    /// <summary>Neue Registry mit zusätzlicher Regel.</summary>
    public RuleRegistry With(IRule rule) => new(_rules.Values.Append(rule));

    /// <summary>Deutsche Fehlermeldung für einen unbekannten Typnamen inklusive der bekannten Typen.</summary>
    public string UnknownRuleMessage(string typeName) =>
        $"Unbekannter Regeltyp „{typeName}“. Bekannt sind: {string.Join(", ", _rules.Keys)}.";
}
