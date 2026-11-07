namespace GraphDoku.Core.Candidates;

/// <summary>Art einer Kandidatenänderung.</summary>
public enum CandidateChangeKind
{
    /// <summary>Kandidaten werden gestrichen.</summary>
    Eliminate,

    /// <summary>Ein Wert wird gesetzt (alle anderen Kandidaten entfallen).</summary>
    Assign,
}

/// <summary>Eine Änderung an den Kandidaten eines Knotens samt Begründung.</summary>
public sealed record CandidateChange(int NodeId, CandidateChangeKind Kind, IReadOnlyList<int> Values, Reason Reason)
{
    public static CandidateChange Eliminate(int nodeId, IEnumerable<int> values, Reason reason) =>
        new(nodeId, CandidateChangeKind.Eliminate, [.. values.Order()], reason);

    public static CandidateChange Assign(int nodeId, int value, Reason reason) =>
        new(nodeId, CandidateChangeKind.Assign, [value], reason);

    public override string ToString() => Kind == CandidateChangeKind.Assign
        ? $"Knoten {NodeId} = {Values[0]} ({Reason})"
        : $"Knoten {NodeId}: streiche {string.Join(", ", Values)} ({Reason})";
}
