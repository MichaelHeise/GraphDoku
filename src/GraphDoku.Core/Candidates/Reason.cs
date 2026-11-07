using GraphDoku.Core.Model;

namespace GraphDoku.Core.Candidates;

/// <summary>Begründung einer Kandidatenänderung: verantwortlicher Constraint, beteiligte Knoten und kurzer deutscher Text.</summary>
public sealed record Reason(ConstraintRef Constraint, IReadOnlyList<int> NodeIds, string Text)
{
    public override string ToString() => $"{Constraint}: {Text}";
}
