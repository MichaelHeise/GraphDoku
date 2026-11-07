using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GraphDoku.App.ViewModels;
using GraphDoku.App.Views;

namespace GraphDoku.App.Services;

/// <summary>Dateiauswahl über den Avalonia-StorageProvider des Hauptfensters.</summary>
public sealed class AvaloniaFileDialogService(TopLevel owner) : IFileDialogService
{
    private static readonly FilePickerFileType PuzzleFiles = new("GraphDoku-Rätsel (JSON)")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
        AppleUniformTypeIdentifiers = ["public.json"],
    };

    public async Task<string?> PickOpenPathAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Rätsel öffnen",
            AllowMultiple = false,
            FileTypeFilter = [PuzzleFiles, FilePickerFileTypes.All],
        }).ConfigureAwait(true);
        return files.Count == 0 ? null : LocalPath(files[0]);
    }

    public async Task<string?> PickSavePathAsync(string suggestedFileName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Rätsel speichern",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = [PuzzleFiles],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);
        return file is null ? null : LocalPath(file);
    }

    private static string LocalPath(IStorageItem item) =>
        item.TryGetLocalPath() ?? throw new IOException("Die gewählte Datei hat keinen lokalen Pfad und kann nicht verwendet werden.");
}

/// <summary>Dialoge als modale Fenster über dem Hauptfenster.</summary>
public sealed class AvaloniaDialogService(Window owner) : IDialogService
{
    public async Task<NewPuzzleOptions?> AskNewPuzzleAsync()
    {
        var viewModel = new NewPuzzleViewModel();
        var window = new NewPuzzleWindow { DataContext = viewModel };
        var ok = await window.ShowDialog<bool>(owner).ConfigureAwait(true);
        return ok ? viewModel.ToOptions() : null;
    }

    public async Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string documentName)
    {
        var window = new MessageWindow(
            "Ungespeicherte Änderungen",
            $"„{documentName}“ enthält ungespeicherte Änderungen. Vorher speichern?",
            ["Speichern", "Verwerfen", "Abbrechen"],
            defaultIndex: 0,
            cancelIndex: 2);
        var choice = await window.ShowDialog<int>(owner).ConfigureAwait(true);
        return choice switch
        {
            0 => UnsavedChangesChoice.Save,
            1 => UnsavedChangesChoice.Discard,
            _ => UnsavedChangesChoice.Cancel,
        };
    }

    public Task ShowErrorAsync(string title, string message) =>
        new MessageWindow(title, message, ["OK"]).ShowDialog<int>(owner);
}
