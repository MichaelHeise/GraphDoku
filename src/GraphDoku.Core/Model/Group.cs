namespace GraphDoku.Core.Model;

/// <summary>Geordnete Knotenmenge mit Regel sowie optionaler Farbe und Beschriftung.</summary>
public sealed class Group : IConstraint
{
    public Group(int id, IEnumerable<int> nodes, string rule, RuleParams? parameters = null, string? color = null, string? label = null)
    {
        Id = id;
        Nodes = [.. nodes];
        Rule = rule;
        Params = parameters ?? new RuleParams();
        Color = color;
        Label = label;
    }

    public int Id { get; set; }

    /// <summary>Knoten-Ids in Reihenfolge (relevant für geordnete Regeln wie Thermometer).</summary>
    public IList<int> Nodes { get; }

    public string Rule { get; set; }

    public RuleParams Params { get; set; }

    /// <summary>Optionale Farbe, z. B. "#ff8800".</summary>
    public string? Color { get; set; }

    /// <summary>Optionale Beschriftung, z. B. "Zeile 1".</summary>
    public string? Label { get; set; }

    public ConstraintRef Ref => ConstraintRef.ForGroup(Id);

    IReadOnlyList<int> IConstraint.NodeIds => [.. Nodes];

    public Group Clone() => new(Id, Nodes, Rule, Params.Clone(), Color, Label);

    public override string ToString() => Label is null ? $"Gruppe {Id} ({Rule})" : $"Gruppe {Id} „{Label}“ ({Rule})";
}
