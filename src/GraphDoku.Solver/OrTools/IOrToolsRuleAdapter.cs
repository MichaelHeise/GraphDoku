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

    /// <summary>
    /// Fügt die Constraints für <paramref name="constraint"/> zum Modell hinzu. Parameter sind bereits geprüft.
    /// </summary>
    /// <remarks>
    /// Hilfsvariablen sollten durch die Knotenwerte eindeutig bestimmt sein (z. B. <c>d = |a − b|</c> statt einer
    /// freien Booleschen Variablen mit <c>OnlyEnforceIf</c>). Die Lösungszählung zählt zwar nur verschiedene
    /// Knotenbelegungen und bleibt deshalb auch mit freien Hilfsvariablen richtig, CP-SAT muss deren Varianten beim
    /// Aufzählen aber trotzdem alle durchlaufen, was die Eindeutigkeitsprüfung verlangsamt.
    /// </remarks>
    void AddToModel(OrToolsModelContext context, IConstraint constraint);
}
