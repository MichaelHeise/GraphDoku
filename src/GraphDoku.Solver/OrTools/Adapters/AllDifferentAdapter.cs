using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;

namespace GraphDoku.Solver.OrTools.Adapters;

/// <summary><c>AllDifferent</c>: AddAllDifferent über alle Knoten der Gruppe.</summary>
public sealed class AllDifferentAdapter : IOrToolsRuleAdapter
{
    public string TypeName => AllDifferentRule.Name;

    public void AddToModel(OrToolsModelContext context, IConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Model.AddAllDifferent(context.Variables(constraint));
    }
}
