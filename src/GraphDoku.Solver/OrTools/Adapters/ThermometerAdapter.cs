using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;

namespace GraphDoku.Solver.OrTools.Adapters;

/// <summary><c>Thermometer</c>: streng steigend in Listenreihenfolge (v[i] &lt; v[i+1]).</summary>
public sealed class ThermometerAdapter : IOrToolsRuleAdapter
{
    public string TypeName => ThermometerRule.Name;

    public void AddToModel(OrToolsModelContext context, IConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(context);
        var variables = context.Variables(constraint);
        for (var i = 0; i + 1 < variables.Count; i++)
        {
            context.Model.Add(variables[i] < variables[i + 1]);
        }
    }
}
