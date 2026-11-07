using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing.Commands;

/// <summary>
/// Löscht Knoten, Kanten und Gruppen. Mit einem Knoten verschwinden auch alle Kanten an ihm, und er wird aus allen
/// Gruppen entfernt; Gruppen, die dadurch leer werden, werden mitgelöscht. Undo stellt Listen und Reihenfolgen exakt
/// wieder her.
/// </summary>
public sealed class DeleteElementsCommand : IEditCommand
{
    private readonly HashSet<int> _nodeIds;
    private readonly HashSet<int> _edgeIds;
    private readonly HashSet<int> _groupIds;
    private Snapshot? _before;

    public DeleteElementsCommand(IEnumerable<int>? nodeIds = null, IEnumerable<int>? edgeIds = null, IEnumerable<int>? groupIds = null)
    {
        _nodeIds = [.. nodeIds ?? []];
        _edgeIds = [.. edgeIds ?? []];
        _groupIds = [.. groupIds ?? []];
    }

    public string Description => (_nodeIds.Count, _edgeIds.Count, _groupIds.Count) switch
    {
        (1, 0, 0) => "Knoten löschen",
        ( > 1, 0, 0) => $"{_nodeIds.Count} Knoten löschen",
        (0, 1, 0) => "Kante löschen",
        (0, 0, 1) => "Gruppe löschen",
        _ => "Auswahl löschen",
    };

    public void Execute(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        _before = new Snapshot(
            [.. puzzle.Nodes],
            [.. puzzle.Edges],
            [.. puzzle.Groups],
            puzzle.Groups.ToDictionary(g => g, g => g.Nodes.ToList()));

        AddNodesCommand.RemoveWhere(puzzle.Nodes, n => _nodeIds.Contains(n.Id));
        AddNodesCommand.RemoveWhere(puzzle.Edges, e => _edgeIds.Contains(e.Id) || _nodeIds.Contains(e.From) || _nodeIds.Contains(e.To));
        foreach (var group in puzzle.Groups)
        {
            AddNodesCommand.RemoveWhere(group.Nodes, _nodeIds.Contains);
        }

        AddNodesCommand.RemoveWhere(puzzle.Groups, g => _groupIds.Contains(g.Id) || (g.Nodes.Count == 0 && _before.GroupNodes[g].Count > 0));
    }

    public void Undo(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        var before = _before ?? throw new InvalidOperationException("Der Befehl wurde noch nicht ausgeführt.");
        Restore(puzzle.Nodes, before.Nodes);
        Restore(puzzle.Edges, before.Edges);
        Restore(puzzle.Groups, before.Groups);
        foreach (var (group, nodes) in before.GroupNodes)
        {
            Restore(group.Nodes, nodes);
        }
    }

    public bool TryMerge(IEditCommand later) => false;

    private static void Restore<T>(IList<T> target, IReadOnlyList<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private sealed record Snapshot(List<Node> Nodes, List<Edge> Edges, List<Group> Groups, Dictionary<Group, List<int>> GroupNodes);
}
