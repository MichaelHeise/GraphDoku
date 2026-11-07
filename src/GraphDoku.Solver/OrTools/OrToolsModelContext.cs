using Google.OrTools.Sat;
using GraphDoku.Core.Model;

namespace GraphDoku.Solver.OrTools;

/// <summary>Zugriff auf Modell, Rätsel und Knotenvariablen für die Regel-Adapter.</summary>
public sealed class OrToolsModelContext
{
    private readonly IReadOnlyDictionary<int, IntVar> _variables;
    private readonly IReadOnlyDictionary<int, ValueDomain> _domains;

    internal OrToolsModelContext(Puzzle puzzle, CpModel model, IReadOnlyDictionary<int, IntVar> variables, IReadOnlyDictionary<int, ValueDomain> domains)
    {
        Puzzle = puzzle;
        Model = model;
        _variables = variables;
        _domains = domains;
    }

    public Puzzle Puzzle { get; }

    public CpModel Model { get; }

    /// <summary>Die Variable des Knotens.</summary>
    public IntVar Variable(int nodeId) => _variables[nodeId];

    /// <summary>Die Variablen der Knoten des Constraints in dessen Reihenfolge (bei Kanten [From, To]).</summary>
    public IReadOnlyList<IntVar> Variables(IConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        return [.. constraint.NodeIds.Select(Variable)];
    }

    /// <summary>Wertebereich der Variablen: eigener oder globaler Bereich, bei einem Given genau dieser Wert.</summary>
    public ValueDomain Domain(int nodeId) => _domains[nodeId];

    /// <summary>Macht das Modell unerfüllbar (für Regeln, die mit den gegebenen Bereichen nie erfüllbar sind).</summary>
    public void AddFalse() => Model.AddBoolOr([Model.FalseLiteral()]);
}
