using GraphDoku.Core.Model;

namespace GraphDoku.Solver.OrTools;

/// <summary>
/// Übersetzt einen Regeltyp (über <see cref="TypeName"/> der Core-Regel zugeordnet) in CP-SAT-Constraints.
/// Das ist die in der Spezifikation genannte Fähigkeit <c>AddToModel</c>, ausgelagert, damit Core frei von OR-Tools bleibt.
/// </summary>
public interface IOrToolsRuleAdapter
{
    /// <summary>Typname der Regel, wie in <see cref="Core.Rules.IRule.TypeName"/>.</summary>
    string TypeName { get; }

    /// <summary>Fügt die Constraints für <paramref name="constraint"/> zum Modell hinzu. Parameter sind bereits geprüft.</summary>
    void AddToModel(OrToolsModelContext context, IConstraint constraint);
}
