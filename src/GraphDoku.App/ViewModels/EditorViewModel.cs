using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GraphDoku.App.Editing;
using GraphDoku.App.Editing.Commands;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;
using GraphDoku.Core.Validation;

namespace GraphDoku.App.ViewModels;

/// <summary>
/// Editor für ein Rätsel: Werkzeuge, Auswahl, Bearbeitungen (alle über <see cref="EditHistory"/>), Gruppenliste,
/// Eigenschaften-Panel und Prüfmeldungen. Kennt keine Avalonia-Typen; die Zeichenfläche reicht Zeiger- und
/// Tastaturereignisse in Bildschirmkoordinaten weiter (siehe <c>EditorViewModel.Input.cs</c>).
/// </summary>
public sealed partial class EditorViewModel : ViewModelBase
{
    private readonly List<int> _selectedNodes = [];
    private readonly HashSet<int> _selectedNodeSet = [];
    private readonly HashSet<int> _hiddenGroups = [];
    private EditHistory _history;
    private bool _fitPending;
    private bool _syncingGroupSelection;

    public EditorViewModel(RuleRegistry? registry = null)
    {
        Registry = registry ?? RuleRegistry.Default;
        EdgeRules = [.. Registry.Rules.Where(r => r.Kind.AppliesToEdges())];
        GroupRules = [.. Registry.Rules.Where(r => r.Kind.AppliesToGroups())];
        NewEdgeRule = EdgeRules.FirstOrDefault(r => r.TypeName == "NotEqual") ?? (EdgeRules.Count > 0 ? EdgeRules[0] : null);
        NewGroupRule = GroupRules.FirstOrDefault(r => r.TypeName == "AllDifferent") ?? (GroupRules.Count > 0 ? GroupRules[0] : null);
        Viewport.Changed += OnViewportChanged;
        _history = CreateHistory(new Puzzle("Unbenannt"));
        AfterDocumentReplaced();
    }

    /// <summary>Etwas Sichtbares hat sich geändert (Modell, Auswahl, Ansicht, Gesten): neu zeichnen.</summary>
    public event EventHandler? VisualChanged;

    /// <summary>Das Rätsel wurde bearbeitet, ersetzt oder gespeichert (Titel, Dirty-Anzeige).</summary>
    public event EventHandler? DocumentChanged;

    public RuleRegistry Registry { get; }

    public IReadOnlyList<IRule> EdgeRules { get; }

    public IReadOnlyList<IRule> GroupRules { get; }

    public Puzzle Puzzle => _history.Puzzle;

    public EditHistory History => _history;

    public Viewport Viewport { get; } = new();

    public bool IsDirty => _history.IsDirty;

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public string UndoText => _history.UndoDescription is { } d ? $"Rückgängig: {d}" : "Rückgängig";

    public string RedoText => _history.RedoDescription is { } d ? $"Wiederholen: {d}" : "Wiederholen";

    /// <summary>Regel für neu gezogene Kanten.</summary>
    [ObservableProperty]
    public partial IRule? NewEdgeRule { get; set; }

    /// <summary>Regel für neu erstellte Gruppen.</summary>
    [ObservableProperty]
    public partial IRule? NewGroupRule { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectTool), nameof(IsPlaceNodeTool), nameof(IsDrawEdgeTool), nameof(IsPanTool), nameof(ToolHint))]
    public partial EditorTool Tool { get; set; }

    public bool IsSelectTool
    {
        get => Tool == EditorTool.Select;
        set => SetToolIf(value, EditorTool.Select);
    }

    public bool IsPlaceNodeTool
    {
        get => Tool == EditorTool.PlaceNode;
        set => SetToolIf(value, EditorTool.PlaceNode);
    }

    public bool IsDrawEdgeTool
    {
        get => Tool == EditorTool.DrawEdge;
        set => SetToolIf(value, EditorTool.DrawEdge);
    }

    public bool IsPanTool
    {
        get => Tool == EditorTool.Pan;
        set => SetToolIf(value, EditorTool.Pan);
    }

    public string ToolHint => Tool switch
    {
        EditorTool.Select => "Auswahl: Klick wählt, Shift-Klick schaltet um, Rahmen ziehen wählt mehrere, ausgewählte Knoten ziehen verschiebt. Ziffern tragen Givens ein, ⌫ löscht sie, Entf löscht die Auswahl.",
        EditorTool.PlaceNode => "Knoten setzen: Klick oder Ziehen setzt Knoten auf freie Gitterpunkte.",
        EditorTool.DrawEdge => "Kante ziehen: von einem Knoten zu einem anderen ziehen. Regel in der Werkzeugleiste wählen.",
        EditorTool.Pan => "Ansicht verschieben: Ziehen verschiebt, Mausrad zoomt.",
        _ => string.Empty,
    };

    /// <summary>Ausgewählte Knoten in Auswahlreihenfolge (bestimmt die Reihenfolge neuer Gruppen).</summary>
    public IReadOnlyList<int> SelectedNodeIds => _selectedNodes;

    [ObservableProperty]
    public partial int? SelectedEdgeId { get; private set; }

    [ObservableProperty]
    public partial int? SelectedGroupId { get; private set; }

    /// <summary>Auswahlreihenfolge als kleine Zahlen an den Knoten anzeigen (ab zwei ausgewählten Knoten).</summary>
    [ObservableProperty]
    public partial bool ShowSelectionOrder { get; set; } = true;

    public ObservableCollection<GroupItemViewModel> Groups { get; } = [];

    /// <summary>In der Gruppenliste markierter Eintrag (gleichbedeutend mit <see cref="SelectedGroupId"/>).</summary>
    [ObservableProperty]
    public partial GroupItemViewModel? SelectedGroupItem { get; set; }

    [ObservableProperty]
    public partial PropertiesViewModel? Properties { get; private set; }

    public ObservableCollection<string> ValidationProblems { get; } = [];

    [ObservableProperty]
    public partial string ValidationSummary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasValidationProblems { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public bool IsNodeSelected(int id) => _selectedNodeSet.Contains(id);

    public bool IsGroupVisible(int id) => !_hiddenGroups.Contains(id);

    /// <summary>Ersetzt das Rätsel (Neu, Öffnen, Vorlage). Der Verlauf beginnt leer und gilt als gespeichert.</summary>
    public void LoadPuzzle(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        CancelGesture();
        _history.Changed -= OnHistoryChanged;
        _history = CreateHistory(puzzle);
        _hiddenGroups.Clear();
        AfterDocumentReplaced();
    }

    /// <summary>Führt eine Bearbeitung als Undo-Schritt aus.</summary>
    public void Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _history.Execute(command);
        StatusMessage = command.Description;
    }

    /// <summary>Merkt den aktuellen Stand als gespeichert (nach erfolgreichem Speichern).</summary>
    public void MarkSaved() => _history.MarkSaved();

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        var description = _history.UndoDescription;
        CancelGesture();
        if (_history.Undo())
        {
            StatusMessage = $"Rückgängig: {description}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        var description = _history.RedoDescription;
        CancelGesture();
        if (_history.Redo())
        {
            StatusMessage = $"Wiederholt: {description}";
        }
    }

    /// <summary>Löscht die Auswahl: Knoten (mit ihren Kanten), die gewählte Kante oder die gewählte Gruppe.</summary>
    [RelayCommand]
    public void DeleteSelection()
    {
        if (_selectedNodes.Count > 0)
        {
            Execute(new DeleteElementsCommand(nodeIds: _selectedNodes));
        }
        else if (SelectedEdgeId is int edgeId)
        {
            Execute(new DeleteElementsCommand(edgeIds: [edgeId]));
        }
        else if (SelectedGroupId is int groupId)
        {
            Execute(new DeleteElementsCommand(groupIds: [groupId]));
        }
        else
        {
            StatusMessage = "Nichts ausgewählt.";
        }
    }

    /// <summary>Löscht die Givens der ausgewählten Knoten.</summary>
    [RelayCommand]
    public void ClearGivens()
    {
        EndValueEntry();
        var changes = _selectedNodes
            .Select(Puzzle.FindNode)
            .Where(n => n?.Given is not null)
            .ToDictionary(n => n!.Id, n => (n!.Given, (int?)null));
        if (changes.Count == 0)
        {
            StatusMessage = "Die ausgewählten Knoten haben keine Givens.";
            return;
        }

        Execute(new SetGivensCommand(changes));
    }

    /// <summary>Erstellt aus den ausgewählten Knoten (in Auswahlreihenfolge) eine Gruppe mit <see cref="NewGroupRule"/>.</summary>
    [RelayCommand]
    public void CreateGroupFromSelection()
    {
        if (_selectedNodes.Count == 0)
        {
            StatusMessage = "Für eine Gruppe zuerst Knoten auswählen.";
            return;
        }

        if (NewGroupRule is not { } rule)
        {
            StatusMessage = "Keine Gruppenregel gewählt.";
            return;
        }

        var group = new Group(Puzzle.NextGroupId(), _selectedNodes, rule.TypeName, rule.CreateDefaultParams());
        Execute(new AddGroupCommand(group));
        SelectGroup(group.Id);
        StatusMessage = $"Gruppe {group.Id} ({rule.DisplayName}) mit {group.Nodes.Count} Knoten erstellt.";
    }

    [RelayCommand]
    public void SelectAll() => SelectNodes(Puzzle.Nodes.Select(n => n.Id));

    /// <summary>Passt die Ansicht an den Inhalt an.</summary>
    [RelayCommand]
    public void FitView()
    {
        if (!Viewport.HasSize)
        {
            _fitPending = true;
            return;
        }

        _fitPending = false;
        var bounds = EditorGeometry.ContentBounds(Puzzle) ?? new Bounds(-0.5, -0.5, 9.5, 9.5);
        Viewport.FitTo(bounds);
    }

    [RelayCommand]
    private void ZoomIn() => Viewport.ZoomAt(new ScreenPoint(Viewport.Width / 2, Viewport.Height / 2), 1.25);

    [RelayCommand]
    private void ZoomOut() => Viewport.ZoomAt(new ScreenPoint(Viewport.Width / 2, Viewport.Height / 2), 1 / 1.25);

    [RelayCommand]
    private void ShowAllGroups() => SetAllGroupsVisible(true);

    [RelayCommand]
    private void HideAllGroups() => SetAllGroupsVisible(false);

    /// <summary>Wählt genau diese Knoten (in der angegebenen Reihenfolge).</summary>
    public void SelectNodes(IEnumerable<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ClearSelectionCore();
        foreach (var id in ids)
        {
            if (_selectedNodeSet.Add(id))
            {
                _selectedNodes.Add(id);
            }
        }

        OnSelectionChanged();
    }

    /// <summary>Hängt Knoten an die Auswahl an (bereits gewählte bleiben an ihrer Stelle).</summary>
    public void AddToSelection(IEnumerable<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var hadOther = SelectedEdgeId is not null || SelectedGroupId is not null;
        if (hadOther)
        {
            var keep = _selectedNodes.ToList();
            ClearSelectionCore();
            _selectedNodes.AddRange(keep);
            _selectedNodeSet.UnionWith(keep);
        }

        foreach (var id in ids)
        {
            if (_selectedNodeSet.Add(id))
            {
                _selectedNodes.Add(id);
            }
        }

        OnSelectionChanged();
    }

    /// <summary>Shift-Klick: Knoten zur Auswahl hinzufügen oder aus ihr entfernen.</summary>
    public void ToggleNodeSelection(int id)
    {
        if (_selectedNodeSet.Remove(id))
        {
            _selectedNodes.Remove(id);
            OnSelectionChanged();
        }
        else
        {
            AddToSelection([id]);
        }
    }

    public void SelectEdge(int id)
    {
        ClearSelectionCore();
        SelectedEdgeId = id;
        OnSelectionChanged();
    }

    public void SelectGroup(int id)
    {
        ClearSelectionCore();
        SelectedGroupId = id;
        OnSelectionChanged();
    }

    public void ClearSelection()
    {
        ClearSelectionCore();
        OnSelectionChanged();
    }

    /// <summary>Gruppenhervorhebung ein- oder ausblenden (Ansichtseinstellung, kein Undo-Schritt, wird nicht gespeichert).</summary>
    public void SetGroupVisible(int id, bool visible)
    {
        var changed = visible ? _hiddenGroups.Remove(id) : _hiddenGroups.Add(id);
        if (changed)
        {
            foreach (var item in Groups.Where(g => g.Id == id))
            {
                item.SetVisibleSilently(visible);
            }

            RaiseVisualChanged();
        }
    }

    partial void OnToolChanged(EditorTool value)
    {
        CancelGesture();
        RaiseVisualChanged();
    }

    partial void OnShowSelectionOrderChanged(bool value) => RaiseVisualChanged();

    partial void OnSelectedGroupItemChanged(GroupItemViewModel? value)
    {
        if (_syncingGroupSelection)
        {
            return;
        }

        if (value is not null)
        {
            SelectGroup(value.Id);
        }
        else if (SelectedGroupId is not null)
        {
            ClearSelection();
        }
    }

    private EditHistory CreateHistory(Puzzle puzzle)
    {
        var history = new EditHistory(puzzle);
        history.Changed += OnHistoryChanged;
        return history;
    }

    private void AfterDocumentReplaced()
    {
        ClearSelectionCore();
        EndValueEntry();
        OnPropertyChanged(nameof(Puzzle));
        OnPropertyChanged(nameof(History));
        Groups.Clear();
        OnModelChanged();
        OnSelectionChanged();
        FitView();
    }

    private void OnHistoryChanged(object? sender, EventArgs e) => OnModelChanged();

    /// <summary>Nach jeder Änderung am Modell: Auswahl bereinigen, Listen, Panel und Prüfung aktualisieren.</summary>
    private void OnModelChanged()
    {
        var selectionChanged = PruneSelection();
        SyncGroups();
        Validate();
        if (selectionChanged || Properties is null || !Properties.IsStillValid())
        {
            RebuildProperties();
        }
        else
        {
            Properties.Refresh();
        }

        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoText));
        OnPropertyChanged(nameof(RedoText));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        RaiseVisualChanged();
    }

    private bool PruneSelection()
    {
        var changed = false;
        var removed = _selectedNodes.RemoveAll(id => Puzzle.FindNode(id) is null);
        if (removed > 0)
        {
            _selectedNodeSet.Clear();
            _selectedNodeSet.UnionWith(_selectedNodes);
            changed = true;
        }

        if (SelectedEdgeId is int edgeId && Puzzle.FindEdge(edgeId) is null)
        {
            SelectedEdgeId = null;
            changed = true;
        }

        if (SelectedGroupId is int groupId && Puzzle.FindGroup(groupId) is null)
        {
            SelectedGroupId = null;
            changed = true;
        }

        return changed;
    }

    private void ClearSelectionCore()
    {
        _selectedNodes.Clear();
        _selectedNodeSet.Clear();
        SelectedEdgeId = null;
        SelectedGroupId = null;
    }

    private void OnSelectionChanged()
    {
        EndValueEntry();
        _syncingGroupSelection = true;
        try
        {
            SelectedGroupItem = SelectedGroupId is int id ? Groups.FirstOrDefault(g => g.Id == id) : null;
        }
        finally
        {
            _syncingGroupSelection = false;
        }

        OnPropertyChanged(nameof(SelectedNodeIds));
        RebuildProperties();
        RaiseVisualChanged();
    }

    private void RebuildProperties()
    {
        Properties = SelectedGroupId is int groupId ? new GroupPropertiesViewModel(this, groupId)
            : SelectedEdgeId is int edgeId ? new EdgePropertiesViewModel(this, edgeId)
            : _selectedNodes.Count > 0 ? new NodeSelectionPropertiesViewModel(this)
            : new PuzzlePropertiesViewModel(this);
    }

    /// <summary>Gleicht die Gruppenliste mit dem Modell ab und behält dabei vorhandene Einträge (Sichtbarkeit, Markierung).</summary>
    private void SyncGroups()
    {
        _syncingGroupSelection = true;
        try
        {
            // Mehrfach vergebene Gruppen-Ids (Datei ohne inhaltliche Prüfung geöffnet, Nr. 57) sind möglich: vorhandene
            // Einträge je Id der Reihe nach zuordnen statt über ein Dictionary mit eindeutigen Schlüsseln.
            var existing = Groups.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => new Queue<GroupItemViewModel>(g));
            var target = Puzzle.Groups
                .Select(g => existing.TryGetValue(g.Id, out var items) && items.Count > 0 ? items.Dequeue() : new GroupItemViewModel(this, g.Id))
                .ToList();
            for (var i = Groups.Count - 1; i >= 0; i--)
            {
                if (!target.Contains(Groups[i]))
                {
                    Groups.RemoveAt(i);
                }
            }

            for (var i = 0; i < target.Count; i++)
            {
                if (i >= Groups.Count || !ReferenceEquals(Groups[i], target[i]))
                {
                    var current = Groups.IndexOf(target[i]);
                    if (current >= 0)
                    {
                        Groups.Move(current, i);
                    }
                    else
                    {
                        Groups.Insert(i, target[i]);
                    }
                }

                target[i].Refresh(Puzzle.Groups[i]);
            }

            _hiddenGroups.IntersectWith(Puzzle.Groups.Select(g => g.Id));
            SelectedGroupItem = SelectedGroupId is int id ? Groups.FirstOrDefault(g => g.Id == id) : null;
        }
        finally
        {
            _syncingGroupSelection = false;
        }
    }

    private void SetAllGroupsVisible(bool visible)
    {
        foreach (var group in Puzzle.Groups)
        {
            SetGroupVisible(group.Id, visible);
        }
    }

    private void Validate()
    {
        var problems = PuzzleValidator.Validate(Puzzle, Registry);
        ValidationProblems.Clear();
        foreach (var problem in problems)
        {
            ValidationProblems.Add(problem);
        }

        HasValidationProblems = problems.Count > 0;
        ValidationSummary = problems.Count switch
        {
            0 => "Keine Probleme gefunden.",
            1 => "1 Problem:",
            _ => $"{problems.Count} Probleme:",
        };
    }

    private void SetToolIf(bool value, EditorTool tool)
    {
        if (value)
        {
            Tool = tool;
        }
    }

    private void OnViewportChanged(object? sender, EventArgs e)
    {
        if (_fitPending && Viewport.HasSize)
        {
            FitView();
            return;
        }

        RaiseVisualChanged();
    }

    private void RaiseVisualChanged() => VisualChanged?.Invoke(this, EventArgs.Empty);
}
