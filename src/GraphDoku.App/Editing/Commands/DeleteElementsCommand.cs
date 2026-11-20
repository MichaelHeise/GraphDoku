using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing.Commands;

/// <summary>
/// Löscht Knoten, Kanten, Gruppen und Gruppenvergleiche. Mit einem Knoten verschwinden auch alle Kanten an ihm, und er
/// wird aus allen Gruppen entfernt; Gruppen, die dadurch leer werden, werden mitgelöscht. Mit einer Gruppe verschwinden
/// alle Gruppenvergleiche, die sie benutzen – auch wenn die Gruppe nur durch das Löschen ihrer Knoten wegfällt. Undo
/// stellt Listen und Reihenfolgen exakt wieder her (dieselben Objekte).
/// </summary>
public sealed class DeleteElementsCommand : IEditCommand
{
    private readonly HashSet<int> _nodeIds;
    private readonly HashSet<int> _edgeIds;
    private readonly HashSet<int> _groupIds;
    private readonly HashSet<int> _groupComparisonIds;
    private Snapshot? _before;

    public DeleteElementsCommand(
        IEnumerable<int>? nodeIds = null,
        IEnumerable<int>? edgeIds = null,
        IEnumerable<int>? groupIds = null,
        IEnumerable<int>? groupComparisonIds = null)
    {
        _nodeIds = [.. nodeIds ?? []];
        _edgeIds = [.. edgeIds ?? []];
        _groupIds = [.. groupIds ?? []];
        _groupComparisonIds = [.. groupComparisonIds ?? []];
    }

    public string Description => (_nodeIds.Count, _edgeIds.Count, _groupIds.Count, _groupComparisonIds.Count) switch
    {
        (1, 0, 0, 0) => "Knoten löschen",
        ( > 1, 0, 0, 0) => $"{_nodeIds.Count} Knoten löschen",
        (0, 1, 0, 0) => "Kante löschen",
        (0, 0, 1, 0) => "Gruppe löschen",
        (0, 0, 0, 1) => "Gruppenvergleich löschen",
        _ => "Auswahl löschen",
    };

    public void Execute(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        _before = new Snapshot(
            [.. puzzle.Nodes],
            [.. puzzle.Edges],
            [.. puzzle.Groups],
            puzzle.Groups.ToDictionary(g => g, g => g.Nodes.ToList()),
            [.. puzzle.GroupComparisons]);

        AddNodesCommand.RemoveWhere(puzzle.Nodes, n => _nodeIds.Contains(n.Id));
        AddNodesCommand.RemoveWhere(puzzle.Edges, e => _edgeIds.Contains(e.Id) || _nodeIds.Contains(e.From) || _nodeIds.Contains(e.To));
        foreach (var group in puzzle.Groups)
        {
            AddNodesCommand.RemoveWhere(group.Nodes, _nodeIds.Contains);
        }

        AddNodesCommand.RemoveWhere(puzzle.Groups, g => _groupIds.Contains(g.Id) || (g.Nodes.Count == 0 && _before.GroupNodes[g].Count > 0));

        // Vergleiche, deren Gruppe dieser Befehl entfernt hat (ausdrücklich oder leer geworden), fallen mit weg. Nur
        // Verweise, die erst jetzt ins Leere zeigen: Bei mehrfach vergebenen Gruppen-Ids (Datei ohne Prüfung, Nr. 57)
        // bleibt ein Vergleich, solange noch eine Gruppe mit der Id da ist, und ein schon vorher hängender Verweis wird
        // nicht nebenbei gelöscht.
        var removedGroupIds = _before.Groups.Except(puzzle.Groups).Select(g => g.Id).Where(id => puzzle.FindGroup(id) is null).ToHashSet();
        AddNodesCommand.RemoveWhere(
            puzzle.GroupComparisons,
            c => _groupComparisonIds.Contains(c.Id) || removedGroupIds.Contains(c.Left) || removedGroupIds.Contains(c.Right));
    }

    public void Undo(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        var before = _before ?? throw new InvalidOperationException("Der Befehl wurde noch nicht ausgeführt.");
        Restore(puzzle.Nodes, before.Nodes);
        Restore(puzzle.Edges, before.Edges);
        Restore(puzzle.Groups, before.Groups);
        Restore(puzzle.GroupComparisons, before.GroupComparisons);
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

    private sealed record Snapshot(
        List<Node> Nodes,
        List<Edge> Edges,
        List<Group> Groups,
        Dictionary<Group, List<int>> GroupNodes,
        List<GroupComparison> GroupComparisons);
}
