using CommunityToolkit.Mvvm.ComponentModel;
using GraphDoku.App.Editing;
using GraphDoku.Core.Model;

namespace GraphDoku.App.ViewModels;

/// <summary>Eintrag der Liste „Gruppenvergleiche“: beide Gruppen mit Operator und die Farben der Gruppen.</summary>
public sealed partial class GroupComparisonItemViewModel : ViewModelBase
{
    private const string MissingColor = "#9ca3af";

    public GroupComparisonItemViewModel(int id) => Id = id;

    public int Id { get; }

    /// <summary>Z. B. „Käfig (2) ≤ Gruppe 5“.</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    /// <summary>Z. B. „Gruppenvergleich 1 · kleiner oder gleich“.</summary>
    [ObservableProperty]
    public partial string Details { get; private set; } = string.Empty;

    /// <summary>Farbe der linken Gruppe als #rrggbb (fehlt sie, grau).</summary>
    [ObservableProperty]
    public partial string LeftColorHex { get; private set; } = MissingColor;

    /// <summary>Farbe der rechten Gruppe als #rrggbb (fehlt sie, grau).</summary>
    [ObservableProperty]
    public partial string RightColorHex { get; private set; } = MissingColor;

    internal void Refresh(Puzzle puzzle, GroupComparison comparison)
    {
        Title = GroupComparisonTexts.ListTitle(puzzle, comparison);
        var text = Enum.IsDefined(comparison.Operator) ? ComparisonOperators.ToGermanText(comparison.Operator) : "unbekannter Operator";
        Details = $"Gruppenvergleich {comparison.Id} · {text}";
        LeftColorHex = ColorOf(puzzle, comparison.Left);
        RightColorHex = ColorOf(puzzle, comparison.Right);
    }

    private static string ColorOf(Puzzle puzzle, int groupId) =>
        puzzle.FindGroup(groupId) is { } group ? GroupPalette.EffectiveColor(group.Color, group.Id) : MissingColor;
}
