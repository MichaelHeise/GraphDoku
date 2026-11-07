using GraphDoku.Core.Model;

namespace GraphDoku.App.Services;

/// <summary>Einstellungen für ein neues, leeres Rätsel.</summary>
public sealed record NewPuzzleOptions(string Name, GridType Grid, ValueDomain Domain);

/// <summary>Antwort auf die Frage nach ungespeicherten Änderungen.</summary>
public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Dateiauswahl (in der App über den Avalonia-StorageProvider, in Tests ersetzt).</summary>
public interface IFileDialogService
{
    /// <summary>Lokaler Pfad der zu öffnenden Datei oder null bei Abbruch.</summary>
    Task<string?> PickOpenPathAsync();

    /// <summary>Lokaler Pfad zum Speichern oder null bei Abbruch.</summary>
    Task<string?> PickSavePathAsync(string suggestedFileName);
}

/// <summary>Dialoge und Meldungen (in der App als Fenster, in Tests ersetzt).</summary>
public interface IDialogService
{
    /// <summary>Fragt Name, Gittertyp und Wertebereich ab; null bei Abbruch.</summary>
    Task<NewPuzzleOptions?> AskNewPuzzleAsync();

    Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string documentName);

    Task ShowErrorAsync(string title, string message);
}

/// <summary>Ersatz ohne Oberfläche (Designer): bricht jede Dateiauswahl ab.</summary>
public sealed class NullFileDialogService : IFileDialogService
{
    public static NullFileDialogService Instance { get; } = new();

    public Task<string?> PickOpenPathAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickSavePathAsync(string suggestedFileName) => Task.FromResult<string?>(null);
}

/// <summary>Ersatz ohne Oberfläche (Designer): bricht jeden Dialog ab.</summary>
public sealed class NullDialogService : IDialogService
{
    public static NullDialogService Instance { get; } = new();

    public Task<NewPuzzleOptions?> AskNewPuzzleAsync() => Task.FromResult<NewPuzzleOptions?>(null);

    public Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string documentName) => Task.FromResult(UnsavedChangesChoice.Cancel);

    public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
}
