using CommunityToolkit.Mvvm.ComponentModel;
using GraphDoku.App.Editing;
using GraphDoku.Core.Model;

namespace GraphDoku.App.ViewModels;

/// <summary>Eintrag der Gruppenliste: Name, Farbe, Regel und Sichtbarkeit der Hervorhebung.</summary>
public sealed partial class GroupItemViewModel : ViewModelBase
{
    private readonly EditorViewModel _editor;
    private bool _silent;

    public GroupItemViewModel(EditorViewModel editor, int id)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
        Id = id;
        IsVisible = editor.IsGroupVisible(id);
    }

    public int Id { get; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Details { get; private set; } = string.Empty;

    /// <summary>Darzustellende Farbe (eigene oder automatische) als #rrggbb.</summary>
    [ObservableProperty]
    public partial string ColorHex { get; private set; } = "#000000";

    /// <summary>Hervorhebung auf der Zeichenfläche ein- oder ausblenden.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    internal void Refresh(Group group)
    {
        Title = string.IsNullOrWhiteSpace(group.Label) ? $"Gruppe {group.Id}" : $"{group.Label} ({group.Id})";
        var rule = _editor.Registry.TryGet(group.Rule, out var r) ? r.DisplayName : $"{group.Rule} (unbekannt)";
        Details = $"{rule}, {group.Nodes.Count} Knoten";
        ColorHex = GroupPalette.EffectiveColor(group.Color, group.Id);
    }

    internal void SetVisibleSilently(bool visible)
    {
        _silent = true;
        try
        {
            IsVisible = visible;
        }
        finally
        {
            _silent = false;
        }
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (!_silent)
        {
            _editor.SetGroupVisible(Id, value);
        }
    }
}
