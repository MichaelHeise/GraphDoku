namespace GraphDoku.Core.Model;

/// <summary>
/// Vergleicht die Summen zweier Gruppen: <c>Summe(Gruppe Left) OP Summe(Gruppe Right)</c>. Jede Gruppe behält ihre
/// eigene Regel. Kein <see cref="IConstraint"/>: Die beteiligten Knoten ergeben sich erst aus den Gruppen des Rätsels
/// (siehe <see cref="Rules.GroupComparisonLogic.Resolve"/>).
/// </summary>
public sealed class GroupComparison
{
    public GroupComparison(int id, int left, int right, ComparisonOperator op = ComparisonOperator.Equal)
    {
        Id = id;
        Left = left;
        Right = right;
        Operator = op;
    }

    public int Id { get; set; }

    /// <summary>Id der linken Gruppe.</summary>
    public int Left { get; set; }

    /// <summary>Id der rechten Gruppe.</summary>
    public int Right { get; set; }

    public ComparisonOperator Operator { get; set; }

    public ConstraintRef Ref => ConstraintRef.ForGroupComparison(Id);

    public GroupComparison Clone() => new(Id, Left, Right, Operator);

    public override string ToString() =>
        $"Gruppenvergleich {Id} (Gruppe {Left} {ComparisonOperators.ToSymbolOrPlaceholder(Operator)} Gruppe {Right})";
}
