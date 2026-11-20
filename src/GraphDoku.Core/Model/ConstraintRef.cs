namespace GraphDoku.Core.Model;

/// <summary>Art eines Constraints: Kante zwischen zwei Knoten, Gruppe von Knoten oder Vergleich zweier Gruppensummen.</summary>
public enum ConstraintKind
{
    Edge,
    Group,

    /// <summary>Ein <see cref="GroupComparison"/>; kein <see cref="IConstraint"/>, sondern ein eigenes Element.</summary>
    GroupComparison,
}

/// <summary>Verweis auf eine Kante, Gruppe oder einen Gruppenvergleich (Art + Id; Ids gelten nur je Art).</summary>
public readonly record struct ConstraintRef(ConstraintKind Kind, int Id)
{
    public static ConstraintRef ForEdge(int id) => new(ConstraintKind.Edge, id);

    public static ConstraintRef ForGroup(int id) => new(ConstraintKind.Group, id);

    public static ConstraintRef ForGroupComparison(int id) => new(ConstraintKind.GroupComparison, id);

    public override string ToString() => Kind switch
    {
        ConstraintKind.Edge => $"Kante {Id}",
        ConstraintKind.Group => $"Gruppe {Id}",
        ConstraintKind.GroupComparison => $"Gruppenvergleich {Id}",
        _ => $"{Kind} {Id}",
    };
}

/// <summary>Gemeinsame Sicht auf Kanten und Gruppen: Regel, Parameter und beteiligte Knoten in Reihenfolge.</summary>
public interface IConstraint
{
    ConstraintRef Ref { get; }

    string Rule { get; }

    RuleParams Params { get; }

    /// <summary>Beteiligte Knoten-Ids; bei Kanten genau [From, To], bei Gruppen in Listenreihenfolge.</summary>
    IReadOnlyList<int> NodeIds { get; }
}
