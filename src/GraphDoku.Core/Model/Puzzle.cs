namespace GraphDoku.Core.Model;

/// <summary>Ein Rätsel: Gittertyp, globaler Wertebereich, Knoten, Kanten und Gruppen. Lösungen gehören nicht dazu.</summary>
public sealed class Puzzle
{
    /// <summary>Die einzige derzeit unterstützte Version des Speicherformats.</summary>
    public const int CurrentFormatVersion = 1;

    public Puzzle(string name = "", GridType grid = GridType.Square, ValueDomain? domain = null)
    {
        Name = name;
        Grid = grid;
        Domain = domain ?? new ValueDomain(1, 9);
    }

    public string Name { get; set; }

    public GridType Grid { get; set; }

    /// <summary>Globaler Wertebereich; einzelne Knoten können ihn überschreiben.</summary>
    public ValueDomain Domain { get; set; }

    public IList<Node> Nodes { get; } = new List<Node>();

    public IList<Edge> Edges { get; } = new List<Edge>();

    public IList<Group> Groups { get; } = new List<Group>();

    /// <summary>Alle Kanten und Gruppen als Constraints (erst Kanten, dann Gruppen).</summary>
    public IEnumerable<IConstraint> Constraints => Edges.Cast<IConstraint>().Concat(Groups);

    public Node? FindNode(int id) => Nodes.FirstOrDefault(n => n.Id == id);

    public Edge? FindEdge(int id) => Edges.FirstOrDefault(e => e.Id == id);

    public Group? FindGroup(int id) => Groups.FirstOrDefault(g => g.Id == id);

    public IConstraint? FindConstraint(ConstraintRef reference) => reference.Kind == ConstraintKind.Edge
        ? FindEdge(reference.Id)
        : FindGroup(reference.Id);

    /// <summary>Wertebereich des Knotens: eigener Bereich, sonst der globale.</summary>
    public ValueDomain GetEffectiveDomain(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Domain ?? Domain;
    }

    public int NextNodeId() => Nodes.Count == 0 ? 1 : Nodes.Max(n => n.Id) + 1;

    public int NextEdgeId() => Edges.Count == 0 ? 1 : Edges.Max(e => e.Id) + 1;

    public int NextGroupId() => Groups.Count == 0 ? 1 : Groups.Max(g => g.Id) + 1;

    /// <summary>Tiefe Kopie des Rätsels.</summary>
    public Puzzle Clone()
    {
        var copy = new Puzzle(Name, Grid, Domain);
        foreach (var node in Nodes)
        {
            copy.Nodes.Add(node.Clone());
        }

        foreach (var edge in Edges)
        {
            copy.Edges.Add(edge.Clone());
        }

        foreach (var group in Groups)
        {
            copy.Groups.Add(group.Clone());
        }

        return copy;
    }

    public override string ToString() => $"{Name} ({GridTypeNames.ToName(Grid)}, {Nodes.Count} Knoten)";
}
