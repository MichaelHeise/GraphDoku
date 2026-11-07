using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;

namespace GraphDoku.Solver.OrTools.Adapters;

/// <summary><c>NotEqual</c>: a ≠ b.</summary>
public sealed class NotEqualAdapter : IOrToolsRuleAdapter
{
    public string TypeName => NotEqualRule.Name;

    public void AddToModel(OrToolsModelContext context, IConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(context);
        var variables = context.Variables(constraint);
        context.Model.Add(variables[0] != variables[1]);
    }
}
