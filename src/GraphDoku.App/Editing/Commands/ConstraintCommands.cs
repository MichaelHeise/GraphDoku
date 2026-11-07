using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing.Commands;

/// <summary>Fügt eine Kante hinzu.</summary>
public sealed class AddEdgeCommand : IEditCommand
{
    private readonly Edge _edge;

    public AddEdgeCommand(Edge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        _edge = edge.Clone();
    }

    public string Description => "Kante ziehen";

    public void Execute(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        puzzle.Edges.Add(_edge.Clone());
    }

    public void Undo(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        AddNodesCommand.RemoveWhere(puzzle.Edges, e => e.Id == _edge.Id);
    }

    public bool TryMerge(IEditCommand later) => false;
}

/// <summary>Fügt eine Gruppe hinzu.</summary>
public sealed class AddGroupCommand : IEditCommand
{
    private readonly Group _group;

    public AddGroupCommand(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        _group = group.Clone();
    }

    public string Description => "Gruppe erstellen";

    public void Execute(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        puzzle.Groups.Add(_group.Clone());
    }

    public void Undo(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        AddNodesCommand.RemoveWhere(puzzle.Groups, g => g.Id == _group.Id);
    }

    public bool TryMerge(IEditCommand later) => false;
}

/// <summary>Bearbeitbare Eigenschaften einer Kante.</summary>
public sealed record EdgeState(int From, int To, string Rule, RuleParams Params)
{
    public static EdgeState Of(Edge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return new EdgeState(edge.From, edge.To, edge.Rule, edge.Params.Clone());
    }

    public void ApplyTo(Edge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        edge.From = From;
        edge.To = To;
        edge.Rule = Rule;
        edge.Params = Params.Clone();
    }
}

/// <summary>Bearbeitbare Eigenschaften einer Gruppe.</summary>
public sealed record GroupState(IReadOnlyList<int> Nodes, string Rule, RuleParams Params, string? Color, string? Label)
{
    public static GroupState Of(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new GroupState([.. group.Nodes], group.Rule, group.Params.Clone(), group.Color, group.Label);
    }

    public void ApplyTo(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.Nodes.Clear();
        foreach (var id in Nodes)
        {
            group.Nodes.Add(id);
        }

        group.Rule = Rule;
        group.Params = Params.Clone();
        group.Color = Color;
        group.Label = Label;
    }
}

/// <summary>Ändert Regel, Parameter oder Richtung einer Kante.</summary>
public sealed class ModifyEdgeCommand : EditCommandBase
{
    private readonly int _edgeId;
    private readonly EdgeState _before;
    private EdgeState _after;

    public ModifyEdgeCommand(int edgeId, EdgeState before, EdgeState after, string description, object? mergeKey = null)
        : base(mergeKey)
    {
        _edgeId = edgeId;
        _before = before;
        _after = after;
        Description = description;
    }

    public override string Description { get; }

    public override void Execute(Puzzle puzzle) => Find(puzzle).Let(_after.ApplyTo);

    public override void Undo(Puzzle puzzle) => Find(puzzle).Let(_before.ApplyTo);

    protected override void MergeFrom(EditCommandBase later) => _after = ((ModifyEdgeCommand)later)._after;

    private Edge? Find(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        return puzzle.FindEdge(_edgeId);
    }
}

/// <summary>Ändert Knotenliste, Regel, Parameter, Farbe oder Beschriftung einer Gruppe.</summary>
public sealed class ModifyGroupCommand : EditCommandBase
{
    private readonly int _groupId;
    private readonly GroupState _before;
    private GroupState _after;

    public ModifyGroupCommand(int groupId, GroupState before, GroupState after, string description, object? mergeKey = null)
        : base(mergeKey)
    {
        _groupId = groupId;
        _before = before;
        _after = after;
        Description = description;
    }

    public override string Description { get; }

    public override void Execute(Puzzle puzzle) => Find(puzzle).Let(_after.ApplyTo);

    public override void Undo(Puzzle puzzle) => Find(puzzle).Let(_before.ApplyTo);

    protected override void MergeFrom(EditCommandBase later) => _after = ((ModifyGroupCommand)later)._after;

    private Group? Find(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        return puzzle.FindGroup(_groupId);
    }
}

/// <summary>Bearbeitbare Eigenschaften des Rätsels selbst.</summary>
public sealed record PuzzleState(string Name, ValueDomain Domain)
{
    public static PuzzleState Of(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        return new PuzzleState(puzzle.Name, puzzle.Domain);
    }

    public void ApplyTo(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        puzzle.Name = Name;
        puzzle.Domain = Domain;
    }
}

/// <summary>Ändert Name oder globalen Wertebereich des Rätsels.</summary>
public sealed class ModifyPuzzleCommand : EditCommandBase
{
    private readonly PuzzleState _before;
    private PuzzleState _after;

    public ModifyPuzzleCommand(PuzzleState before, PuzzleState after, string description, object? mergeKey = null)
        : base(mergeKey)
    {
        _before = before;
        _after = after;
        Description = description;
    }

    public override string Description { get; }

    public override void Execute(Puzzle puzzle) => _after.ApplyTo(puzzle);

    public override void Undo(Puzzle puzzle) => _before.ApplyTo(puzzle);

    protected override void MergeFrom(EditCommandBase later) => _after = ((ModifyPuzzleCommand)later)._after;
}

internal static class NullableExtensions
{
    public static void Let<T>(this T? value, Action<T> action)
        where T : class
    {
        if (value is not null)
        {
            action(value);
        }
    }
}
