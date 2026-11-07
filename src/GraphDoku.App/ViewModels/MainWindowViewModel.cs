using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GraphDoku.App.Services;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;
using GraphDoku.Core.Serialization;
using GraphDoku.Core.Templates;

namespace GraphDoku.App.ViewModels;

/// <summary>Hauptfenster: Datei-Befehle (Neu, Vorlage, Öffnen, Speichern, Speichern unter), Titel mit Dirty-Anzeige und der Editor.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    public const string AppName = "GraphDoku";

    private readonly IFileDialogService _files;
    private readonly IDialogService _dialogs;

    /// <summary>Für den Designer: ohne Dialoge.</summary>
    public MainWindowViewModel()
        : this(NullFileDialogService.Instance, NullDialogService.Instance)
    {
    }

    public MainWindowViewModel(IFileDialogService files, IDialogService dialogs, RuleRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(dialogs);
        _files = files;
        _dialogs = dialogs;
        Editor = new EditorViewModel(registry);
        Editor.DocumentChanged += (_, _) => UpdateTitle();
        Editor.StatusMessage = "Neues Rätsel. Mit „Knoten setzen“ Knoten auf das Gitter setzen oder eine Vorlage wählen.";
        UpdateTitle();
    }

    public EditorViewModel Editor { get; }

    /// <summary>Pfad der aktuellen Datei (null = noch nie gespeichert).</summary>
    [ObservableProperty]
    public partial string? FilePath { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = AppName;

    /// <summary>Name für Titel und Rückfragen: Dateiname, sonst Rätselname, sonst „Unbenannt“.</summary>
    public string DocumentName => FilePath is not null
        ? Path.GetFileName(FilePath)
        : string.IsNullOrWhiteSpace(Editor.Puzzle.Name) ? "Unbenannt" : Editor.Puzzle.Name;

    /// <summary>Neues leeres Rätsel (Gittertyp und Wertebereich per Dialog).</summary>
    [RelayCommand]
    private async Task NewAsync()
    {
        if (!await ConfirmDiscardChangesAsync().ConfigureAwait(true))
        {
            return;
        }

        if (await _dialogs.AskNewPuzzleAsync().ConfigureAwait(true) is not { } options)
        {
            return;
        }

        ReplaceDocument(new Puzzle(options.Name, options.Grid, options.Domain), null);
        Editor.StatusMessage = $"Neues Rätsel „{options.Name}“ ({Editing.GridNames.ToGerman(options.Grid)}, Werte {options.Domain}).";
    }

    /// <summary>Vorlage „Klassisch 9×9“: 81 Knoten und 27 AllDifferent-Gruppen.</summary>
    [RelayCommand]
    private async Task NewClassicAsync()
    {
        if (!await ConfirmDiscardChangesAsync().ConfigureAwait(true))
        {
            return;
        }

        ReplaceDocument(ClassicSudoku.Create(), null);
        Editor.StatusMessage = "Vorlage „Klassisch 9×9“ geladen: 81 Knoten, 27 Gruppen.";
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardChangesAsync().ConfigureAwait(true))
        {
            return;
        }

        if (await _files.PickOpenPathAsync().ConfigureAwait(true) is { } path)
        {
            await OpenFileAsync(path).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task SaveAsync() => await SaveDocumentAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task SaveAsAsync() => await SaveDocumentAsAsync().ConfigureAwait(true);

    /// <summary>
    /// Öffnet die Datei. Geprüft wird nur das Format; inhaltliche Probleme (z. B. unfertige Gruppen) erscheinen in der
    /// Statusleiste, damit auch unfertig gespeicherte Rätsel weiter bearbeitet werden können.
    /// </summary>
    public async Task<bool> OpenFileAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Puzzle puzzle;
        try
        {
            puzzle = PuzzleSerializer.Load(path, Editor.Registry, validate: false);
        }
        catch (PuzzleFormatException ex)
        {
            await ReportErrorAsync("Öffnen fehlgeschlagen", $"Die Datei „{Path.GetFileName(path)}“ ist keine gültige Rätseldatei.\n\n{FormatErrors(ex.Errors)}").ConfigureAwait(true);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            await ReportErrorAsync("Öffnen fehlgeschlagen", $"Die Datei „{Path.GetFileName(path)}“ konnte nicht gelesen werden: {ex.Message}").ConfigureAwait(true);
            return false;
        }

        ReplaceDocument(puzzle, path);
        Editor.StatusMessage = $"„{Path.GetFileName(path)}“ geöffnet: {puzzle.Nodes.Count} Knoten, {puzzle.Edges.Count} Kanten, {puzzle.Groups.Count} Gruppen.";
        return true;
    }

    /// <summary>Speichert unter dem bisherigen Pfad, ohne Pfad wie „Speichern unter“. true bei Erfolg.</summary>
    public Task<bool> SaveDocumentAsync() => FilePath is { } path ? SaveToFileAsync(path) : SaveDocumentAsAsync();

    public async Task<bool> SaveDocumentAsAsync()
    {
        var path = await _files.PickSavePathAsync(SuggestFileName()).ConfigureAwait(true);
        return path is not null && await SaveToFileAsync(path).ConfigureAwait(true);
    }

    /// <summary>Speichert (ohne inhaltliche Prüfung, Entscheidung Nr. 26) und merkt den Pfad. true bei Erfolg.</summary>
    public async Task<bool> SaveToFileAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            PuzzleSerializer.Save(Editor.Puzzle, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            await ReportErrorAsync("Speichern fehlgeschlagen", $"Die Datei „{Path.GetFileName(path)}“ konnte nicht geschrieben werden: {ex.Message}").ConfigureAwait(true);
            return false;
        }

        FilePath = path;
        Editor.MarkSaved();
        Editor.StatusMessage = Editor.HasValidationProblems
            ? $"Gespeichert: {Path.GetFileName(path)} (das Rätsel hat noch {Editor.ValidationProblems.Count} Problem(e))."
            : $"Gespeichert: {Path.GetFileName(path)}";
        UpdateTitle();
        return true;
    }

    /// <summary>Fragt bei ungespeicherten Änderungen nach. true = fortfahren (gespeichert oder verworfen).</summary>
    public async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!Editor.IsDirty)
        {
            return true;
        }

        return await _dialogs.AskUnsavedChangesAsync(DocumentName).ConfigureAwait(true) switch
        {
            UnsavedChangesChoice.Save => await SaveDocumentAsync().ConfigureAwait(true),
            UnsavedChangesChoice.Discard => true,
            _ => false,
        };
    }

    private void ReplaceDocument(Puzzle puzzle, string? path)
    {
        FilePath = path;
        Editor.LoadPuzzle(puzzle);
        UpdateTitle();
    }

    private async Task ReportErrorAsync(string title, string message)
    {
        Editor.StatusMessage = $"{title}.";
        await _dialogs.ShowErrorAsync(title, message).ConfigureAwait(true);
    }

    private static string FormatErrors(IReadOnlyList<string> errors) =>
        errors.Count == 0 ? "Unbekannter Fehler." : string.Join("\n", errors.Take(20).Select(e => "• " + e))
            + (errors.Count > 20 ? $"\n… und {errors.Count - 20} weitere" : string.Empty);

    private string SuggestFileName()
    {
        if (FilePath is not null)
        {
            return Path.GetFileName(FilePath);
        }

        var name = string.IsNullOrWhiteSpace(Editor.Puzzle.Name) ? "Unbenannt" : Editor.Puzzle.Name.Trim();
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }

        return builder + ".json";
    }

    private void UpdateTitle()
    {
        Title = $"{DocumentName}{(Editor.IsDirty ? " *" : string.Empty)} – {AppName}";
        OnPropertyChanged(nameof(DocumentName));
    }
}
