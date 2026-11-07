namespace GraphDoku.Core.Rules;

/// <summary>Woran eine Regel hängt und ob Richtung bzw. Reihenfolge eine Rolle spielt.</summary>
public enum RuleKind
{
    /// <summary>Ungerichtete Kante zwischen zwei Knoten.</summary>
    Edge,

    /// <summary>Gerichtete Kante (From → To ist bedeutsam).</summary>
    DirectedEdge,

    /// <summary>Ungeordnete Knotenmenge.</summary>
    Group,

    /// <summary>Geordnete Knotenmenge (Listenreihenfolge ist bedeutsam).</summary>
    OrderedGroup,
}

public static class RuleKindExtensions
{
    public static bool AppliesToEdges(this RuleKind kind) => kind is RuleKind.Edge or RuleKind.DirectedEdge;

    public static bool AppliesToGroups(this RuleKind kind) => kind is RuleKind.Group or RuleKind.OrderedGroup;

    public static bool IsDirected(this RuleKind kind) => kind == RuleKind.DirectedEdge;

    public static bool IsOrdered(this RuleKind kind) => kind == RuleKind.OrderedGroup;

    /// <summary>Deutsche Bezeichnung, z. B. für Fehlermeldungen.</summary>
    public static string ToGermanText(this RuleKind kind) => kind switch
    {
        RuleKind.Edge => "Kanten",
        RuleKind.DirectedEdge => "gerichtete Kanten",
        RuleKind.Group => "Gruppen",
        RuleKind.OrderedGroup => "geordnete Gruppen",
        _ => kind.ToString(),
    };
}
