using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GraphDoku.App.Editing;
using GraphDoku.App.Editing.Commands;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.App.ViewModels;

/// <summary>Inhalt des Eigenschaften-Panels. <see cref="Refresh"/> lädt die Werte nach Undo/Redo neu, ohne die Felder neu anzulegen.</summary>
public abstract class PropertiesViewModel : ViewModelBase
{
    protected PropertiesViewModel(EditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        Editor = editor;
    }

    public EditorViewModel Editor { get; }

    public abstract string Header { get; }

    /// <summary>true, wenn dieses Panel noch zum Gegenstand passt (sonst baut der Editor ein neues).</summary>
    public abstract bool IsStillValid();

    public abstract void Refresh();
}

/// <summary>Gemeinsamer Teil für Kanten und Gruppen: Regelwahl und Parameterfelder aus <see cref="IRule.Parameters"/>.</summary>
public abstract partial class ConstraintPropertiesViewModel : PropertiesViewModel
{
    private bool _loading;

    protected ConstraintPropertiesViewModel(EditorViewModel editor, IReadOnlyList<IRule> rules)
        : base(editor) => Rules = rules;

    public IReadOnlyList<IRule> Rules { get; }

    [ObservableProperty]
    public partial IRule? SelectedRule { get; set; }

    /// <summary>Typname, falls die Regel nicht registriert ist (dann ist <see cref="SelectedRule"/> null).</summary>
    [ObservableProperty]
    public partial string? UnknownRule { get; set; }

    public ObservableCollection<RuleParameterViewModel> Parameters { get; } = [];

    [ObservableProperty]
    public partial bool HasParameters { get; set; }

    protected abstract string CurrentRule { get; }

    protected abstract RuleParams CurrentParams { get; }

    /// <summary>Führt die Änderung von Regel und Parametern als Undo-Schritt aus.</summary>
    protected abstract void ApplyRule(string rule, RuleParams parameters, string description, object? mergeKey);

    protected void LoadRule()
    {
        _loading = true;
        try
        {
            var rule = Rules.FirstOrDefault(r => r.TypeName == CurrentRule);
            SelectedRule = rule;
            UnknownRule = rule is null ? CurrentRule : null;
            var definitions = rule?.Parameters ?? [];
            if (!Parameters.Select(p => p.Definition).SequenceEqual(definitions))
            {
                Parameters.Clear();
                foreach (var definition in definitions)
                {
                    Parameters.Add(new RuleParameterViewModel(definition, CurrentParams, OnParameterChanged));
                }
            }
            else
            {
                foreach (var parameter in Parameters)
                {
                    parameter.Load(CurrentParams);
                }
            }

            HasParameters = Parameters.Count > 0;
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnSelectedRuleChanged(IRule? value)
    {
        if (_loading || value is null || value.TypeName == CurrentRule)
        {
            return;
        }

        ApplyRule(value.TypeName, value.CreateDefaultParams(), $"Regel „{value.DisplayName}“ wählen", null);
    }

    private void OnParameterChanged(RuleParameterViewModel parameter)
    {
        var parameters = CurrentParams.Clone();
        if (!parameter.TryApplyTo(parameters) || parameters.StructurallyEquals(CurrentParams))
        {
            return;
        }

        // Fortlaufendes Tippen in dasselbe Feld ergibt einen Undo-Schritt.
        ApplyRule(CurrentRule, parameters, $"Parameter „{parameter.Name}“ ändern", parameter);
    }
}

/// <summary>Eigenschaften der ausgewählten Kante.</summary>
public sealed partial class EdgePropertiesViewModel : ConstraintPropertiesViewModel
{
    public EdgePropertiesViewModel(EditorViewModel editor, int edgeId)
        : base(editor, editor.EdgeRules)
    {
        EdgeId = edgeId;
        Refresh();
    }

    public int EdgeId { get; }

    public override string Header => $"Kante {EdgeId}";

    [ObservableProperty]
    public partial string Endpoints { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDirected { get; set; }

    private Edge Edge => Editor.Puzzle.FindEdge(EdgeId) ?? throw new InvalidOperationException($"Kante {EdgeId} gibt es nicht mehr.");

    protected override string CurrentRule => Edge.Rule;

    protected override RuleParams CurrentParams => Edge.Params;

    public override bool IsStillValid() => Editor.Puzzle.FindEdge(EdgeId) is not null;

    public override void Refresh()
    {
        var edge = Edge;
        LoadRule();
        IsDirected = SelectedRule?.Kind.IsDirected() ?? false;
        Endpoints = IsDirected
            ? $"Von Knoten {edge.From} nach Knoten {edge.To} (Wert bei {edge.From} < Wert bei {edge.To})"
            : $"Zwischen Knoten {edge.From} und Knoten {edge.To}";
    }

    [RelayCommand]
    private void Reverse()
    {
        var before = EdgeState.Of(Edge);
        Editor.Execute(new ModifyEdgeCommand(EdgeId, before, before with { From = before.To, To = before.From }, "Kantenrichtung umkehren"));
    }

    [RelayCommand]
    private void Delete() => Editor.Execute(new DeleteElementsCommand(edgeIds: [EdgeId]));

    protected override void ApplyRule(string rule, RuleParams parameters, string description, object? mergeKey)
    {
        var before = EdgeState.Of(Edge);
        Editor.Execute(new ModifyEdgeCommand(EdgeId, before, before with { Rule = rule, Params = parameters }, description, mergeKey));
    }
}

/// <summary>Auswahl in der Farbliste einer Gruppe (Hex null = automatisch nach Id).</summary>
public sealed record ColorOption(string Name, string? Hex)
{
    public string Swatch => Hex ?? "#00000000";
}

/// <summary>Eigenschaften der ausgewählten Gruppe.</summary>
public sealed partial class GroupPropertiesViewModel : ConstraintPropertiesViewModel
{
    private bool _loadingGroup;

    public GroupPropertiesViewModel(EditorViewModel editor, int groupId)
        : base(editor, editor.GroupRules)
    {
        GroupId = groupId;
        ColorOptions = [new ColorOption("Automatisch", null), .. GroupPalette.Colors.Select(c => new ColorOption(c.Name, c.Hex))];
        Refresh();
    }

    public int GroupId { get; }

    public override string Header => $"Gruppe {GroupId}";

    public IReadOnlyList<ColorOption> ColorOptions { get; }

    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ColorOption? SelectedColor { get; set; }

    /// <summary>Knoten in Gruppenreihenfolge, z. B. „1 → 2 → 3“.</summary>
    [ObservableProperty]
    public partial string NodeOrder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOrdered { get; set; }

    private Group Group => Editor.Puzzle.FindGroup(GroupId) ?? throw new InvalidOperationException($"Gruppe {GroupId} gibt es nicht mehr.");

    protected override string CurrentRule => Group.Rule;

    protected override RuleParams CurrentParams => Group.Params;

    public override bool IsStillValid() => Editor.Puzzle.FindGroup(GroupId) is not null;

    public override void Refresh()
    {
        var group = Group;
        _loadingGroup = true;
        try
        {
            if (Label != (group.Label ?? string.Empty))
            {
                Label = group.Label ?? string.Empty;
            }

            SelectedColor = ColorOptions.FirstOrDefault(c => string.Equals(c.Hex, group.Color, StringComparison.OrdinalIgnoreCase))
                ?? (group.Color is null ? ColorOptions[0] : new ColorOption($"Eigene ({group.Color})", group.Color));
            NodeOrder = group.Nodes.Count == 0 ? "(keine Knoten)" : string.Join(" → ", group.Nodes);
            LoadRule();
            IsOrdered = SelectedRule?.Kind.IsOrdered() ?? false;
        }
        finally
        {
            _loadingGroup = false;
        }
    }

    partial void OnLabelChanged(string value)
    {
        if (_loadingGroup)
        {
            return;
        }

        var label = string.IsNullOrWhiteSpace(value) ? null : value;
        var before = GroupState.Of(Group);
        if (before.Label != label)
        {
            // Fortlaufendes Tippen ergibt einen Undo-Schritt (Schlüssel: dieses Panel).
            Editor.Execute(new ModifyGroupCommand(GroupId, before, before with { Label = label }, "Gruppenbeschriftung ändern", this));
        }
    }

    partial void OnSelectedColorChanged(ColorOption? value)
    {
        if (_loadingGroup || value is null)
        {
            return;
        }

        var before = GroupState.Of(Group);
        if (!string.Equals(before.Color, value.Hex, StringComparison.OrdinalIgnoreCase))
        {
            Editor.Execute(new ModifyGroupCommand(GroupId, before, before with { Color = value.Hex }, "Gruppenfarbe ändern"));
        }
    }

    [RelayCommand]
    private void ReverseOrder()
    {
        var before = GroupState.Of(Group);
        Editor.Execute(new ModifyGroupCommand(GroupId, before, before with { Nodes = [.. before.Nodes.Reverse()] }, "Reihenfolge umkehren"));
    }

    [RelayCommand]
    private void Delete() => Editor.Execute(new DeleteElementsCommand(groupIds: [GroupId]));

    protected override void ApplyRule(string rule, RuleParams parameters, string description, object? mergeKey)
    {
        var before = GroupState.Of(Group);
        Editor.Execute(new ModifyGroupCommand(GroupId, before, before with { Rule = rule, Params = parameters }, description, mergeKey));
        IsOrdered = SelectedRule?.Kind.IsOrdered() ?? false;
    }
}

/// <summary>Panel bei ausgewählten Knoten: Übersicht, Gruppe aus Auswahl erstellen, Givens löschen.</summary>
public sealed partial class NodeSelectionPropertiesViewModel : PropertiesViewModel
{
    public NodeSelectionPropertiesViewModel(EditorViewModel editor)
        : base(editor) => Refresh();

    public override string Header => "Auswahl";

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    public override bool IsStillValid() => Editor.SelectedNodeIds.Count > 0;

    public override void Refresh()
    {
        var ids = Editor.SelectedNodeIds;
        if (ids.Count == 1 && Editor.Puzzle.FindNode(ids[0]) is { } node)
        {
            var given = node.Given is int g ? ValueText.Format(g, Editor.Puzzle.GetEffectiveDomain(node)) : "–";
            Summary = $"Knoten {node.Id} an Position ({node.X}, {node.Y})\nGiven: {given}\nWertebereich: {Editor.Puzzle.GetEffectiveDomain(node)}";
        }
        else
        {
            var givens = ids.Count(id => Editor.Puzzle.FindNode(id)?.Given is not null);
            Summary = $"{ids.Count} Knoten ausgewählt, davon {givens} mit Given.\nReihenfolge der Auswahl: {Shorten(ids)}";
        }
    }

    private static string Shorten(IReadOnlyList<int> ids) =>
        ids.Count <= 12 ? string.Join(", ", ids) : string.Join(", ", ids.Take(12)) + $", … ({ids.Count - 12} weitere)";
}

/// <summary>Panel ohne Auswahl: Eigenschaften des Rätsels (Name, globaler Wertebereich).</summary>
public sealed partial class PuzzlePropertiesViewModel : PropertiesViewModel
{
    private readonly object _nameKey = new();
    private readonly object _domainKey = new();
    private bool _loading;

    public PuzzlePropertiesViewModel(EditorViewModel editor)
        : base(editor) => Refresh();

    public override string Header => "Rätsel";

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DomainMin { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DomainMax { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? DomainError { get; set; }

    [ObservableProperty]
    public partial string GridText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Statistics { get; set; } = string.Empty;

    public override bool IsStillValid() => Editor.SelectedNodeIds.Count == 0;

    public override void Refresh()
    {
        var puzzle = Editor.Puzzle;
        _loading = true;
        try
        {
            if (Name != puzzle.Name)
            {
                Name = puzzle.Name;
            }

            if (!(TryParseDomain(out var current) && current == puzzle.Domain))
            {
                DomainMin = puzzle.Domain.Min.ToString(System.Globalization.CultureInfo.InvariantCulture);
                DomainMax = puzzle.Domain.Max.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            DomainError = null;
            GridText = GridNames.ToGerman(puzzle.Grid);
            Statistics = $"{puzzle.Nodes.Count} Knoten, {puzzle.Edges.Count} Kanten, {puzzle.Groups.Count} Gruppen";
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnNameChanged(string value)
    {
        if (_loading || value == Editor.Puzzle.Name)
        {
            return;
        }

        var before = PuzzleState.Of(Editor.Puzzle);
        Editor.Execute(new ModifyPuzzleCommand(before, before with { Name = value }, "Rätselname ändern", _nameKey));
    }

    partial void OnDomainMinChanged(string value) => ApplyDomain();

    partial void OnDomainMaxChanged(string value) => ApplyDomain();

    private void ApplyDomain()
    {
        if (_loading)
        {
            return;
        }

        if (!TryParseDomain(out var domain))
        {
            DomainError = "Bitte ganze Zahlen eingeben.";
            return;
        }

        DomainError = NewPuzzleViewModel.CheckDomain(domain);
        if (DomainError is not null || domain == Editor.Puzzle.Domain)
        {
            return;
        }

        var before = PuzzleState.Of(Editor.Puzzle);
        Editor.Execute(new ModifyPuzzleCommand(before, before with { Domain = domain }, "Wertebereich ändern", _domainKey));
    }

    private bool TryParseDomain(out ValueDomain domain)
    {
        var style = System.Globalization.NumberStyles.AllowLeadingSign;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (int.TryParse(DomainMin.Trim(), style, culture, out var min) && int.TryParse(DomainMax.Trim(), style, culture, out var max))
        {
            domain = new ValueDomain(min, max);
            return true;
        }

        domain = new ValueDomain(0, 0);
        return false;
    }
}
