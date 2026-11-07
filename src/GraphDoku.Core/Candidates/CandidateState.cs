using GraphDoku.Core.Model;

namespace GraphDoku.Core.Candidates;

/// <summary>Kandidatenmenge pro Knoten. Startet aus den Wertebereichen und Givens eines Rätsels.</summary>
public sealed class CandidateState
{
    private readonly Dictionary<int, SortedSet<int>> _candidates;

    private CandidateState(Puzzle puzzle, Dictionary<int, SortedSet<int>> candidates)
    {
        Puzzle = puzzle;
        _candidates = candidates;
    }

    /// <summary>Das Rätsel, zu dem dieser Zustand gehört (z. B. für Standardparameter wie den globalen Wertebereich).</summary>
    public Puzzle Puzzle { get; }

    public IReadOnlyCollection<int> NodeIds => _candidates.Keys;

    /// <summary>true, wenn mindestens ein Knoten keinen Kandidaten mehr hat.</summary>
    public bool HasContradiction => _candidates.Values.Any(c => c.Count == 0);

    /// <summary>true, wenn jeder Knoten genau einen Kandidaten hat.</summary>
    public bool IsSolved => _candidates.Values.All(c => c.Count == 1);

    /// <summary>Erzeugt den Startzustand: Wertebereich pro Knoten, bei Givens nur der vorgegebene Wert.</summary>
    public static CandidateState FromPuzzle(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        var candidates = new Dictionary<int, SortedSet<int>>();
        foreach (var node in puzzle.Nodes)
        {
            var domain = puzzle.GetEffectiveDomain(node);
            if (domain.Size > ValueDomain.MaxSize)
            {
                throw new ArgumentException(
                    $"Knoten {node.Id}: Der Wertebereich {domain} ist zu groß (höchstens {ValueDomain.MaxSize} Werte).",
                    nameof(puzzle));
            }

            candidates[node.Id] = node.Given is int given
                ? (domain.Contains(given) ? [given] : [])
                : [.. domain.Values];
        }

        return new CandidateState(puzzle, candidates);
    }

    public bool Contains(int nodeId) => _candidates.ContainsKey(nodeId);

    /// <summary>Aktuelle Kandidaten eines Knotens (aufsteigend sortiert).</summary>
    public IReadOnlySet<int> GetCandidates(int nodeId) => Get(nodeId);

    public bool IsFixed(int nodeId) => Get(nodeId).Count == 1;

    /// <summary>Der Wert des Knotens, wenn genau ein Kandidat übrig ist, sonst <c>null</c>.</summary>
    public int? GetValue(int nodeId)
    {
        var set = Get(nodeId);
        return set.Count == 1 ? set.Min : null;
    }

    /// <summary>Streicht einen Kandidaten. Liefert true, wenn er vorhanden war.</summary>
    public bool Remove(int nodeId, int value) => Get(nodeId).Remove(value);

    /// <summary>Setzt den Wert eines Knotens; der Wert muss noch Kandidat sein.</summary>
    public void Assign(int nodeId, int value)
    {
        var set = Get(nodeId);
        if (!set.Contains(value))
        {
            throw new InvalidOperationException($"Knoten {nodeId}: {value} ist kein Kandidat mehr.");
        }

        set.Clear();
        set.Add(value);
    }

    /// <summary>Wendet eine Änderung an. Liefert true, wenn sich dadurch etwas geändert hat.</summary>
    public bool Apply(CandidateChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Kind == CandidateChangeKind.Assign)
        {
            var before = Get(change.NodeId).Count;
            Assign(change.NodeId, change.Values[0]);
            return before != 1;
        }

        var changed = false;
        foreach (var value in change.Values)
        {
            changed |= Remove(change.NodeId, value);
        }

        return changed;
    }

    public CandidateState Clone() =>
        new(Puzzle, _candidates.ToDictionary(kv => kv.Key, kv => new SortedSet<int>(kv.Value)));

    private SortedSet<int> Get(int nodeId) => _candidates.TryGetValue(nodeId, out var set)
        ? set
        : throw new KeyNotFoundException($"Knoten {nodeId} ist im Kandidatenzustand nicht vorhanden.");
}
