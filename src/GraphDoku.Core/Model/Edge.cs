namespace GraphDoku.Core.Model;

/// <summary>Kante zwischen zwei Knoten mit Regel. Die Richtung From → To ist nur für gerichtete Regeln bedeutsam.</summary>
public sealed class Edge : IConstraint
{
    public Edge(int id, int from, int to, string rule, RuleParams? parameters = null)
    {
        Id = id;
        From = from;
        To = to;
        Rule = rule;
        Params = parameters ?? new RuleParams();
    }

    public int Id { get; set; }

    public int From { get; set; }

    public int To { get; set; }

    public string Rule { get; set; }

    public RuleParams Params { get; set; }

    public ConstraintRef Ref => ConstraintRef.ForEdge(Id);

    public IReadOnlyList<int> NodeIds => [From, To];

    public Edge Clone() => new(Id, From, To, Rule, Params.Clone());

    public override string ToString() => $"Kante {Id} ({Rule}: {From} → {To})";
}
