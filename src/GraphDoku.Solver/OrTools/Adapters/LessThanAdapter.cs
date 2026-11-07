using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;

namespace GraphDoku.Solver.OrTools.Adapters;

/// <summary><c>LessThan</c>: Wert von From &lt; Wert von To.</summary>
public sealed class LessThanAdapter : IOrToolsRuleAdapter
{
    public string TypeName => LessThanRule.Name;

    public void AddToModel(OrToolsModelContext context, IConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(context);
        var variables = context.Variables(constraint);
        context.Model.Add(variables[0] < variables[1]);
    }
}
