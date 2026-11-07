using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;

namespace GraphDoku.Solver.OrTools.Adapters;

/// <summary>
/// <c>Difference</c>: Hilfsvariable d = |a − b| (AddAbsEquality), dann d = k (<c>exact</c>) bzw. d ≥ k (<c>atLeast</c>).
/// d ist durch a und b eindeutig bestimmt, verändert also die Lösungsanzahl nicht.
/// </summary>
public sealed class DifferenceAdapter : IOrToolsRuleAdapter
{
    public string TypeName => DifferenceRule.Name;

    public void AddToModel(OrToolsModelContext context, IConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(constraint);
        var k = constraint.Params.GetIntOrNull("k") ?? throw new RuleParamsException("Pflichtparameter „k“ fehlt.");
        var atLeast = constraint.Params.GetString("mode", DifferenceRule.ModeExact) == DifferenceRule.ModeAtLeast;

        var (fromId, toId) = (constraint.NodeIds[0], constraint.NodeIds[1]);
        var (a, b) = (context.Variable(fromId), context.Variable(toId));
        var (da, db) = (context.Domain(fromId), context.Domain(toId));
        var maxDifference = Math.Max(Math.Abs((long)da.Max - db.Min), Math.Abs((long)db.Max - da.Min));

        var difference = context.Model.NewIntVar(0, maxDifference, $"abs_{constraint.Ref.Kind}{constraint.Ref.Id}");
        context.Model.AddAbsEquality(difference, a - b);
        if (atLeast)
        {
            context.Model.Add(difference >= k);
        }
        else
        {
            context.Model.Add(difference == k);
        }
    }
}
