using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;

namespace GraphDoku.Solver.OrTools.Adapters;

/// <summary>
/// <c>Ratio</c>: a = factor·b oder b = factor·a, als Tabellen-Constraint (AddAllowedAssignments) über alle erlaubten
/// Wertepaare. Ohne Hilfsvariablen bleibt die Lösungsanzahl exakt (auch bei factor = 1, wo beide Richtungen
/// zusammenfallen). Die Wertebereiche sind klein (höchstens <see cref="ValueDomain.MaxSize"/> Werte).
/// </summary>
public sealed class RatioAdapter : IOrToolsRuleAdapter
{
    public string TypeName => RatioRule.Name;

    public void AddToModel(OrToolsModelContext context, IConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(constraint);
        long factor = constraint.Params.GetInt("factor", 2);
        var (fromId, toId) = (constraint.NodeIds[0], constraint.NodeIds[1]);
        var domainB = context.Domain(toId);

        var pairs = new List<(long A, long B)>();
        foreach (long a in context.Domain(fromId).Values)
        {
            var partners = new SortedSet<long> { a * factor };
            if (a % factor == 0)
            {
                partners.Add(a / factor);
            }

            foreach (var b in partners)
            {
                if (b >= domainB.Min && b <= domainB.Max)
                {
                    pairs.Add((a, b));
                }
            }
        }

        if (pairs.Count == 0)
        {
            context.AddFalse();
            return;
        }

        var table = context.Model.AddAllowedAssignments([context.Variable(fromId), context.Variable(toId)]);
        foreach (var (a, b) in pairs)
        {
            table.AddTuple([a, b]);
        }
    }
}
