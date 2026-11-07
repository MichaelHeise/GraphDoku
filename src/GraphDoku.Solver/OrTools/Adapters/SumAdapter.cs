using Google.OrTools.Sat;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;

namespace GraphDoku.Solver.OrTools.Adapters;

/// <summary><c>Sum</c>: Summe = target; ohne <c>allowRepeats</c> zusätzlich AddAllDifferent.</summary>
public sealed class SumAdapter : IOrToolsRuleAdapter
{
    public string TypeName => SumRule.Name;

    public void AddToModel(OrToolsModelContext context, IConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(constraint);
        var target = constraint.Params.GetIntOrNull("target")
            ?? throw new Core.Model.RuleParamsException("Pflichtparameter „target“ fehlt.");
        var allowRepeats = constraint.Params.GetBool("allowRepeats", false);
        var variables = context.Variables(constraint);

        context.Model.Add(LinearExpr.Sum(variables) == target);
        if (!allowRepeats && variables.Count > 1)
        {
            context.Model.AddAllDifferent(variables);
        }
    }
}
