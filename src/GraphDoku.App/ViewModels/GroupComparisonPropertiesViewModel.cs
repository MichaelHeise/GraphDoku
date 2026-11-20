using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GraphDoku.App.Editing;
using GraphDoku.App.Editing.Commands;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.App.ViewModels;

/// <summary>Auswahl in einer Operatorliste, z. B. „≤  kleiner oder gleich“.</summary>
public sealed record OperatorOption(ComparisonOperator Operator, string Text)
{
    /// <summary>
    /// Alle sechs Operatoren, für alle Panels <b>dieselbe</b> Instanz (wie <see cref="GroupPropertiesViewModel.SharedColorOptions"/>):
    /// Avalonia verwendet die Panel-Vorlage beim Wechsel der Auswahl wieder; eine eigene Liste je Panel ließe die
    /// ComboBox die bisherige Auswahl in das neue Panel zurückschreiben (falsche Undo-Schritte „Vergleich ändern“).
    /// </summary>
    public static IReadOnlyList<OperatorOption> Shared { get; } =
        [.. ComparisonOperators.All.Select(op => new OperatorOption(op, $"{ComparisonOperators.ToSymbol(op)}  {ComparisonOperators.ToGermanText(op)}"))];

    /// <summary>Eintrag zum Operator; <c>null</c> bei einem undefinierten Wert.</summary>
    public static OperatorOption? Of(ComparisonOperator op) => Shared.FirstOrDefault(o => o.Operator == op);
}

/// <summary>Auswahl einer Gruppe in einer Liste, Text wie in der Gruppenliste (z. B. „Käfig (2)“).</summary>
public sealed record GroupOption(int Id, string Text);

/// <summary>
/// Eigenschaften des ausgewählten Gruppenvergleichs: Bedeutung, Operator (jede Änderung ein Undo-Schritt), Sprung zu
/// den beiden Gruppen und Löschen. Die Gruppen selbst lassen sich hier nicht tauschen (das leistet die Wahl des
/// Operators) und nicht ändern (Vergleich löschen und neu erstellen).
/// </summary>
public sealed partial class GroupComparisonPropertiesViewModel : PropertiesViewModel
{
    private bool _loading;

    public GroupComparisonPropertiesViewModel(EditorViewModel editor, int comparisonId)
        : base(editor)
    {
        ComparisonId = comparisonId;
        Refresh();
    }

    public int ComparisonId { get; }

    public override string Header => $"Gruppenvergleich {ComparisonId}";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instanzeigenschaft für die kompilierte Bindung im Panel.")]
    public IReadOnlyList<OperatorOption> OperatorOptions => OperatorOption.Shared;

    /// <summary>Gewählter Operator; null nur bei einem undefinierten Wert (Rätsel aus Code, nicht aus einer Datei).</summary>
    [ObservableProperty]
    public partial OperatorOption? SelectedOperator { get; set; }

    /// <summary>Z. B. „Summe von Gruppe 2 „Käfig“ ≤ Summe von Gruppe 5“.</summary>
    [ObservableProperty]
    public partial string Meaning { get; set; } = string.Empty;

    /// <summary>Linke Gruppe mit Regel und Knotenzahl.</summary>
    [ObservableProperty]
    public partial string LeftText { get; set; } = string.Empty;

    /// <summary>Rechte Gruppe mit Regel und Knotenzahl.</summary>
    [ObservableProperty]
    public partial string RightText { get; set; } = string.Empty;

    /// <summary>Hinweis auf Knoten, die in beiden Gruppen liegen (sonst null).</summary>
    [ObservableProperty]
    public partial string? SharedNodesText { get; set; }

    private GroupComparison Comparison => Editor.Puzzle.FindGroupComparison(ComparisonId)
        ?? throw new InvalidOperationException($"Gruppenvergleich {ComparisonId} gibt es nicht mehr.");

    public override bool IsStillValid() => Editor.Puzzle.FindGroupComparison(ComparisonId) is not null;

    public override void Refresh()
    {
        var comparison = Comparison;
        var puzzle = Editor.Puzzle;
        _loading = true;
        try
        {
            SelectedOperator = OperatorOption.Of(comparison.Operator);
            Meaning = GroupComparisonTexts.Meaning(puzzle, comparison);
            LeftText = DescribeSide(comparison.Left);
            RightText = DescribeSide(comparison.Right);
            var shared = GroupComparisonLogic.Resolve(puzzle, comparison)?.SharedNodeIds ?? [];
            SharedNodesText = shared.Count == 0
                ? null
                : $"Gemeinsame Knoten: {string.Join(", ", shared)}. Sie zählen auf beiden Seiten gleich und heben sich auf.";
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnSelectedOperatorChanged(OperatorOption? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        var before = GroupComparisonState.Of(Comparison);
        if (before.Operator != value.Operator)
        {
            // Jede Wahl ein eigener Undo-Schritt (kein Zusammenführen wie beim Tippen).
            Editor.Execute(new ModifyGroupComparisonCommand(ComparisonId, before, before with { Operator = value.Operator }));
        }
    }

    [RelayCommand]
    private void SelectLeftGroup() => SelectSide(Comparison.Left);

    [RelayCommand]
    private void SelectRightGroup() => SelectSide(Comparison.Right);

    [RelayCommand]
    private void Delete() => Editor.Execute(new DeleteElementsCommand(groupComparisonIds: [ComparisonId]));

    private void SelectSide(int groupId)
    {
        if (Editor.Puzzle.FindGroup(groupId) is null)
        {
            Editor.StatusMessage = $"Gruppe {groupId} gibt es nicht.";
            return;
        }

        Editor.SelectGroup(groupId);
    }

    private string DescribeSide(int groupId)
    {
        var name = GroupComparisonTexts.GroupName(Editor.Puzzle, groupId);
        if (Editor.Puzzle.FindGroup(groupId) is not { } group)
        {
            return name;
        }

        var rule = Editor.Registry.TryGet(group.Rule, out var known) ? known.DisplayName : $"{group.Rule} (unbekannt)";
        return $"{name} – {rule}, {group.Nodes.Count} Knoten";
    }
}
