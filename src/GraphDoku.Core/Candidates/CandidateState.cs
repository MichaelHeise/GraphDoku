using GraphDoku.Core.Model;

namespace GraphDoku.Core.Candidates;

/// <summary>
/// Kandidatenmenge pro Knoten. Startet aus den Wertebereichen (global bzw. pro Knoten) und Givens eines Rätsels.
/// </summary>
/// <remarks>
/// Jeder Knoten hält eine unveränderliche <see cref="CandidateSet"/>; Änderungen ersetzen die Menge des Knotens.
/// <see cref="Clone"/> kopiert deshalb nur ein Feld von Verweisen (O(Knotenanzahl), keine Mengenkopien) und eignet
/// sich für Schnappschüsse, etwa pro Schritt des Erklär-Modus.
/// </remarks>
public sealed class CandidateState
{
    private readonly NodeIndex _index;
    private readonly CandidateSet[] _sets;

    private CandidateState(Puzzle puzzle, NodeIndex index, CandidateSet[] sets)
    {
        Puzzle = puzzle;
        _index = index;
        _sets = sets;
    }

    /// <summary>Das Rätsel, zu dem dieser Zustand gehört (z. B. für Standardparameter wie den globalen Wertebereich).</summary>
    public Puzzle Puzzle { get; }

    /// <summary>Knoten-Ids in der Reihenfolge des Rätsels.</summary>
    public IReadOnlyList<int> NodeIds => _index.Ids;

    /// <summary>true, wenn mindestens ein Knoten keinen Kandidaten mehr hat.</summary>
    public bool HasContradiction => Array.Exists(_sets, s => s.Count == 0);

    /// <summary>true, wenn jeder Knoten genau einen Kandidaten hat.</summary>
    public bool IsSolved => Array.TrueForAll(_sets, s => s.Count == 1);

    /// <summary>Erzeugt den Startzustand: Wertebereich pro Knoten, bei Givens nur der vorgegebene Wert.</summary>
    /// <remarks>Ein Given außerhalb des Wertebereichs ergibt eine leere Kandidatenmenge (Widerspruch).</remarks>
    public static CandidateState FromPuzzle(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        var ids = new List<int>();
        var positions = new Dictionary<int, int>();
        var sets = new List<CandidateSet>();
        foreach (var node in puzzle.Nodes)
        {
            var domain = puzzle.GetEffectiveDomain(node);
            if (domain.Size > ValueDomain.MaxSize)
            {
                throw new ArgumentException(
                    $"Knoten {node.Id}: Der Wertebereich {domain} ist zu groß (höchstens {ValueDomain.MaxSize} Werte).",
                    nameof(puzzle));
            }

            var set = node.Given is int given ? CandidateSet.FromValue(domain, given) : CandidateSet.FromDomain(domain);
            if (positions.TryGetValue(node.Id, out var existing))
            {
                // Doppelte Ids meldet der Validator; hier gilt wie bisher der letzte Knoten.
                sets[existing] = set;
                continue;
            }

            positions[node.Id] = ids.Count;
            ids.Add(node.Id);
            sets.Add(set);
        }

        return new CandidateState(puzzle, new NodeIndex([.. ids], positions), [.. sets]);
    }

    public bool Contains(int nodeId) => _index.Positions.ContainsKey(nodeId);

    /// <summary>Aktuelle Kandidaten eines Knotens (unveränderlich, aufsteigend sortiert).</summary>
    public CandidateSet GetCandidates(int nodeId) => _sets[Position(nodeId)];

    public bool IsFixed(int nodeId) => GetCandidates(nodeId).Count == 1;

    /// <summary>Der Wert des Knotens, wenn genau ein Kandidat übrig ist, sonst <c>null</c>.</summary>
    public int? GetValue(int nodeId) => GetCandidates(nodeId).SingleValue;

    /// <summary>Streicht einen Kandidaten. Liefert true, wenn er vorhanden war.</summary>
    public bool Remove(int nodeId, int value)
    {
        var position = Position(nodeId);
        var before = _sets[position];
        _sets[position] = before.Without(value);
        return !ReferenceEquals(before, _sets[position]);
    }

    /// <summary>Setzt den Wert eines Knotens; der Wert muss noch Kandidat sein.</summary>
    public void Assign(int nodeId, int value)
    {
        var position = Position(nodeId);
        var set = _sets[position];
        if (!set.Contains(value))
        {
            throw new InvalidOperationException($"Knoten {nodeId}: {value} ist kein Kandidat mehr.");
        }

        _sets[position] = set.Only(value);
    }

    /// <summary>Wendet eine Änderung an. Liefert true, wenn sich dadurch etwas geändert hat.</summary>
    public bool Apply(CandidateChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var position = Position(change.NodeId);
        var before = _sets[position];
        if (change.Kind == CandidateChangeKind.Assign)
        {
            Assign(change.NodeId, change.Values[0]);
        }
        else
        {
            _sets[position] = before.Without(change.Values);
        }

        return !ReferenceEquals(before, _sets[position]);
    }

    /// <summary>Unabhängige Kopie (teilt nur die unveränderlichen Mengen).</summary>
    public CandidateState Clone() => new(Puzzle, _index, (CandidateSet[])_sets.Clone());

    /// <summary>true, wenn beide Zustände dieselben Knoten mit denselben Kandidaten haben.</summary>
    public bool HasSameCandidates(CandidateState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other._sets.Length != _sets.Length)
        {
            return false;
        }

        for (var i = 0; i < _sets.Length; i++)
        {
            var id = _index.Ids[i];
            if (!other.Contains(id) || !other.GetCandidates(id).Equals(_sets[i]))
            {
                return false;
            }
        }

        return true;
    }

    private int Position(int nodeId) => _index.Positions.TryGetValue(nodeId, out var position)
        ? position
        : throw new KeyNotFoundException($"Knoten {nodeId} ist im Kandidatenzustand nicht vorhanden.");

    /// <summary>Unveränderliche Zuordnung Knoten-Id → Position, von allen Kopien gemeinsam genutzt.</summary>
    private sealed class NodeIndex(int[] ids, Dictionary<int, int> positions)
    {
        public int[] Ids { get; } = ids;

        public Dictionary<int, int> Positions { get; } = positions;
    }
}
