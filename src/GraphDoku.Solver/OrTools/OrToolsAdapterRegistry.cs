using System.Diagnostics.CodeAnalysis;
using GraphDoku.Solver.OrTools.Adapters;

namespace GraphDoku.Solver.OrTools;

/// <summary>Zuordnung Typname → OR-Tools-Adapter. Unveränderlich; neue Adapter kommen über <see cref="With"/> hinzu.</summary>
public sealed class OrToolsAdapterRegistry
{
    private readonly Dictionary<string, IOrToolsRuleAdapter> _adapters;

    public OrToolsAdapterRegistry(IEnumerable<IOrToolsRuleAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = new Dictionary<string, IOrToolsRuleAdapter>(StringComparer.Ordinal);
        foreach (var adapter in adapters)
        {
            if (!_adapters.TryAdd(adapter.TypeName, adapter))
            {
                throw new ArgumentException($"Für den Regeltyp „{adapter.TypeName}“ ist mehr als ein OR-Tools-Adapter registriert.", nameof(adapters));
            }
        }
    }

    /// <summary>Adapter für alle acht Regeln des Katalogs (Version 1).</summary>
    public static OrToolsAdapterRegistry Default { get; } = new(
    [
        new AllDifferentAdapter(),
        new CoverRangeAdapter(),
        new SumAdapter(),
        new ThermometerAdapter(),
        new NotEqualAdapter(),
        new DifferenceAdapter(),
        new RatioAdapter(),
        new LessThanAdapter(),
    ]);

    public IEnumerable<string> TypeNames => _adapters.Keys;

    public bool Contains(string typeName) => _adapters.ContainsKey(typeName);

    public bool TryGet(string typeName, [NotNullWhen(true)] out IOrToolsRuleAdapter? adapter) =>
        _adapters.TryGetValue(typeName, out adapter);

    public OrToolsAdapterRegistry With(IOrToolsRuleAdapter adapter) => new(_adapters.Values.Append(adapter));
}
