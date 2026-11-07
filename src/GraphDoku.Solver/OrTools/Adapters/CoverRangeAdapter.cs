using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;

namespace GraphDoku.Solver.OrTools.Adapters;

/// <summary>
/// <c>CoverRange</c>: Jeder Knoten liegt in [min, max] (Standard: Rätselbereich) und alle Werte sind verschieden.
/// Weil die Knotenanzahl der Bereichsgröße entspricht (vom Validator geprüft), kommt so jeder Wert genau einmal vor.
/// </summary>
public sealed class CoverRangeAdapter : IOrToolsRuleAdapter
{
    public string TypeName => CoverRangeRule.Name;

    public void AddToModel(OrToolsModelContext context, IConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(context);
        var range = CoverRangeRule.GetRange(constraint, context.Puzzle);
        var variables = context.Variables(constraint);
        foreach (var variable in variables)
        {
            context.Model.AddLinearConstraint(variable, range.Min, range.Max);
        }

        context.Model.AddAllDifferent(variables);
    }
}
