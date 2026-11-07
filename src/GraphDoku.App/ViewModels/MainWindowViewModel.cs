using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;
using GraphDoku.Core.Templates;

namespace GraphDoku.App.ViewModels;

/// <summary>ViewModel des Hauptfensters. In Paket 1 ein Platzhalter, der die Verdrahtung mit Core zeigt.</summary>
public partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
        : this(RuleRegistry.Default)
    {
    }

    public MainWindowViewModel(RuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        AvailableRules = [.. registry.Rules.Select(r => $"{r.TypeName} – {r.DisplayName}")];
    }

    public string Title { get; } = "GraphDoku";

    public string Headline { get; } = "GraphDoku – Sudoku-Varianten als Graph";

    public string Placeholder { get; } = "Der Editor folgt in Paket 3. Bis dahin lässt sich hier ein klassisches 9×9-Sudoku als Probe erzeugen.";

    /// <summary>Alle registrierten Regeltypen („Typname – Anzeigename“).</summary>
    public IReadOnlyList<string> AvailableRules { get; }

    [ObservableProperty]
    public partial Puzzle? Puzzle { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Kein Rätsel geladen.";

    [RelayCommand]
    private void CreateClassicSudoku()
    {
        Puzzle = ClassicSudoku.Create();
        Status = $"{Puzzle.Name}: {Puzzle.Nodes.Count} Knoten, {Puzzle.Edges.Count} Kanten, {Puzzle.Groups.Count} Gruppen.";
    }
}
