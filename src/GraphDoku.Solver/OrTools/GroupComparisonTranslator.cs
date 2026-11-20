using Google.OrTools.Sat;
using GraphDoku.Core.Model;

namespace GraphDoku.Solver.OrTools;

/// <summary>
/// Übersetzt Gruppenvergleiche: <c>Summe(Knoten der linken Gruppe) OP Summe(Knoten der rechten Gruppe)</c> als eine
/// lineare Bedingung ohne Hilfsvariablen. Gemeinsame Knoten heben sich im linearen Ausdruck von selbst auf.
/// </summary>
/// <remarks>
/// Gruppenvergleiche sind keine <see cref="IConstraint"/> und laufen deshalb nicht über die Adapter-Registry.
/// </remarks>
internal static class GroupComparisonTranslator
{
    /// <summary>
    /// Fügt den Vergleich dem Modell hinzu. Liefert eine deutsche Fehlermeldung, wenn er sich nicht übersetzen lässt
    /// (nach der Prüfung des Rätsels nicht zu erwarten), sonst <c>null</c>.
    /// </summary>
    public static string? Add(OrToolsModelContext context, GroupComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(comparison);
        var left = context.Puzzle.FindGroup(comparison.Left);
        var right = context.Puzzle.FindGroup(comparison.Right);
        if (left is null || right is null)
        {
            var (role, id) = left is null ? ("linke", comparison.Left) : ("rechte", comparison.Right);
            return $"{comparison.Ref}: Die {role} Gruppe {id} existiert nicht.";
        }

        var lhs = LinearExpr.Sum(left.Nodes.Select(context.Variable));
        var rhs = LinearExpr.Sum(right.Nodes.Select(context.Variable));
        BoundedLinearExpression? constraint = comparison.Operator switch
        {
            ComparisonOperator.Equal => lhs == rhs,
            ComparisonOperator.NotEqual => lhs != rhs,
            ComparisonOperator.Less => lhs < rhs,
            ComparisonOperator.LessOrEqual => lhs <= rhs,
            ComparisonOperator.Greater => lhs > rhs,
            ComparisonOperator.GreaterOrEqual => lhs >= rhs,
            _ => null,
        };
        if (constraint is null)
        {
            return $"{comparison.Ref}: Der Vergleichsoperator {(int)comparison.Operator} ist ungültig.";
        }

        context.Model.Add(constraint);
        return null;
    }
}
