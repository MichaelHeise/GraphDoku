using CommunityToolkit.Mvvm.ComponentModel;
using GraphDoku.App.Editing;
using GraphDoku.Core.Model;

namespace GraphDoku.App.ViewModels;

/// <summary>Eintrag der Liste „Gruppenvergleiche“: einzeilig beide Gruppen mit Operator, die Farben der Gruppen und ein Tooltip.</summary>
public sealed partial class GroupComparisonItemViewModel : ViewModelBase
{
    private const string MissingColor = "#9ca3af";

    public GroupComparisonItemViewModel(int id) => Id = id;

    public int Id { get; }

    /// <summary>Einzeilig, z. B. „Käfig D ≤ Käfig F“.</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    /// <summary>Volle Bedeutung als Tooltip, z. B. „Gruppenvergleich 1: Summe von Gruppe 22 „Käfig D“ ≤ … (kleiner oder gleich)“.</summary>
    [ObservableProperty]
    public partial string ToolTip { get; private set; } = string.Empty;

    /// <summary>Farbe der linken Gruppe als #rrggbb (fehlt sie, grau).</summary>
    [ObservableProperty]
    public partial string LeftColorHex { get; private set; } = MissingColor;

    /// <summary>Farbe der rechten Gruppe als #rrggbb (fehlt sie, grau).</summary>
    [ObservableProperty]
    public partial string RightColorHex { get; private set; } = MissingColor;

    internal void Refresh(Puzzle puzzle, GroupComparison comparison)
    {
        Title = GroupComparisonTexts.ListTitle(puzzle, comparison);
        ToolTip = GroupComparisonTexts.ListToolTip(puzzle, comparison);
        LeftColorHex = ColorOf(puzzle, comparison.Left);
        RightColorHex = ColorOf(puzzle, comparison.Right);
    }

    private static string ColorOf(Puzzle puzzle, int groupId) =>
        puzzle.FindGroup(groupId) is { } group ? GroupPalette.EffectiveColor(group.Color, group.Id) : MissingColor;
}
