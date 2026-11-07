namespace GraphDoku.Core.Model;

/// <summary>Art eines Constraints: Kante zwischen zwei Knoten oder Gruppe von Knoten.</summary>
public enum ConstraintKind
{
    Edge,
    Group,
}

/// <summary>Verweis auf eine Kante oder Gruppe (Art + Id).</summary>
public readonly record struct ConstraintRef(ConstraintKind Kind, int Id)
{
    public static ConstraintRef ForEdge(int id) => new(ConstraintKind.Edge, id);

    public static ConstraintRef ForGroup(int id) => new(ConstraintKind.Group, id);

    public override string ToString() => Kind == ConstraintKind.Edge ? $"Kante {Id}" : $"Gruppe {Id}";
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
