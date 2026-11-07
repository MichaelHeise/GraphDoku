using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing.Commands;

/// <summary>Fügt Knoten hinzu. Beim Malen mit gedrückter Maustaste werden alle Knoten einer Geste zusammengeführt.</summary>
public sealed class AddNodesCommand : EditCommandBase
{
    private readonly List<Node> _nodes;

    public AddNodesCommand(IEnumerable<Node> nodes, object? mergeKey = null)
        : base(mergeKey)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        _nodes = [.. nodes.Select(n => n.Clone())];
    }

    public IReadOnlyList<Node> Nodes => _nodes;

    public override string Description => _nodes.Count == 1 ? "Knoten setzen" : $"{_nodes.Count} Knoten setzen";

    public override void Execute(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        foreach (var node in _nodes)
        {
            puzzle.Nodes.Add(node.Clone());
        }
    }

    public override void Undo(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        var ids = _nodes.Select(n => n.Id).ToHashSet();
        RemoveWhere(puzzle.Nodes, n => ids.Contains(n.Id));
    }

    protected override void MergeFrom(EditCommandBase later) => _nodes.AddRange(((AddNodesCommand)later)._nodes);

    internal static void RemoveWhere<T>(IList<T> list, Func<T, bool> predicate)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (predicate(list[i]))
            {
                list.RemoveAt(i);
            }
        }
    }
}

/// <summary>Verschiebt Knoten. Fortlaufendes Ziehen (gleicher Schlüssel) ergibt einen Rückgängig-Schritt.</summary>
public sealed class MoveNodesCommand : EditCommandBase
{
    private readonly Dictionary<int, (GridPoint From, GridPoint To)> _moves;

    public MoveNodesCommand(IReadOnlyDictionary<int, (GridPoint From, GridPoint To)> moves, object? mergeKey = null)
        : base(mergeKey)
    {
        ArgumentNullException.ThrowIfNull(moves);
        _moves = new Dictionary<int, (GridPoint From, GridPoint To)>(moves);
    }

    public IReadOnlyDictionary<int, (GridPoint From, GridPoint To)> Moves => _moves;

    public override string Description => _moves.Count == 1 ? "Knoten verschieben" : $"{_moves.Count} Knoten verschieben";

    public override void Execute(Puzzle puzzle) => Apply(puzzle, useTarget: true);

    public override void Undo(Puzzle puzzle) => Apply(puzzle, useTarget: false);

    protected override void MergeFrom(EditCommandBase later)
    {
        foreach (var (id, move) in ((MoveNodesCommand)later)._moves)
        {
            _moves[id] = _moves.TryGetValue(id, out var existing) ? (existing.From, move.To) : move;
        }
    }

    private void Apply(Puzzle puzzle, bool useTarget)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        foreach (var (id, move) in _moves)
        {
            if (puzzle.FindNode(id) is { } node)
            {
                var point = useTarget ? move.To : move.From;
                node.X = point.X;
                node.Y = point.Y;
            }
        }
    }
}

/// <summary>Setzt oder löscht Givens. Mehrstellige Eingaben (gleicher Schlüssel) ergeben einen Rückgängig-Schritt.</summary>
public sealed class SetGivensCommand : EditCommandBase
{
    private readonly Dictionary<int, (int? Old, int? New)> _changes;

    public SetGivensCommand(IReadOnlyDictionary<int, (int? Old, int? New)> changes, object? mergeKey = null)
        : base(mergeKey)
    {
        ArgumentNullException.ThrowIfNull(changes);
        _changes = new Dictionary<int, (int? Old, int? New)>(changes);
    }

    public IReadOnlyDictionary<int, (int? Old, int? New)> Changes => _changes;

    public override string Description => _changes.Values.All(c => c.New is null) ? "Givens löschen" : "Given eintragen";

    public override void Execute(Puzzle puzzle) => Apply(puzzle, useNew: true);

    public override void Undo(Puzzle puzzle) => Apply(puzzle, useNew: false);

    protected override void MergeFrom(EditCommandBase later)
    {
        foreach (var (id, change) in ((SetGivensCommand)later)._changes)
        {
            _changes[id] = _changes.TryGetValue(id, out var existing) ? (existing.Old, change.New) : change;
        }
    }

    private void Apply(Puzzle puzzle, bool useNew)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        foreach (var (id, change) in _changes)
        {
            if (puzzle.FindNode(id) is { } node)
            {
                node.Given = useNew ? change.New : change.Old;
            }
        }
    }
}
