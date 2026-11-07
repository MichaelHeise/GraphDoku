using GraphDoku.App.Services;
using GraphDoku.App.ViewModels;

namespace GraphDoku.App.Tests.Helpers;

/// <summary>Dateiauswahl für Tests: liefert vorher festgelegte Pfade.</summary>
internal sealed class FakeFileDialogService : IFileDialogService
{
    public Queue<string?> OpenPaths { get; } = new();

    public Queue<string?> SavePaths { get; } = new();

    public List<string> SuggestedNames { get; } = [];

    /// <summary>Wird von der nächsten Auswahl geworfen (z. B. Datei ohne lokalen Pfad), danach zurückgesetzt.</summary>
    public Exception? NextFailure { get; set; }

    /// <summary>Läuft während der Öffnen-Auswahl (nicht modaler Dialog: der Benutzer bearbeitet weiter).</summary>
    public Action? DuringOpenPick { get; set; }

    public Task<string?> PickOpenPathAsync()
    {
        ThrowIfFailing();
        DuringOpenPick?.Invoke();
        return Task.FromResult(OpenPaths.Count > 0 ? OpenPaths.Dequeue() : null);
    }

    public Task<string?> PickSavePathAsync(string suggestedFileName)
    {
        SuggestedNames.Add(suggestedFileName);
        ThrowIfFailing();
        return Task.FromResult(SavePaths.Count > 0 ? SavePaths.Dequeue() : null);
    }

    private void ThrowIfFailing()
    {
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            throw failure;
        }
    }
}

/// <summary>Dialoge für Tests: feste Antworten, gezeigte Fehler werden gesammelt.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public NewPuzzleOptions? NewPuzzleAnswer { get; set; }

    public UnsavedChangesChoice UnsavedChangesAnswer { get; set; } = UnsavedChangesChoice.Discard;

    public int UnsavedChangesQuestions { get; private set; }

    public List<(string Title, string Message)> Errors { get; } = [];

    public Task<NewPuzzleOptions?> AskNewPuzzleAsync() => Task.FromResult(NewPuzzleAnswer);

    public Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string documentName)
    {
        UnsavedChangesQuestions++;
        return Task.FromResult(UnsavedChangesAnswer);
    }

    public Task ShowErrorAsync(string title, string message)
    {
        Errors.Add((title, message));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Bedient den Dialog „Kanten erzeugen“: stellt das ViewModel ein und liefert true („Kanten erzeugen“) oder false
    /// (Abbrechen). Ohne Antwort wird abgebrochen.
    /// </summary>
    public Func<EdgeGeneratorViewModel, bool>? EdgeGeneratorAnswer { get; set; }

    /// <summary>Zuletzt gezeigter Generator-Dialog.</summary>
    public EdgeGeneratorViewModel? LastEdgeGenerator { get; private set; }

    public Task<bool> ShowEdgeGeneratorAsync(EdgeGeneratorViewModel generator)
    {
        LastEdgeGenerator = generator;
        return Task.FromResult(EdgeGeneratorAnswer?.Invoke(generator) ?? false);
    }
}

/// <summary>Temporäres Verzeichnis, das nach dem Test gelöscht wird.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GraphDoku.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
