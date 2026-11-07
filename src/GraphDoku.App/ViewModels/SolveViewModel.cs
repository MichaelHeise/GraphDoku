using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GraphDoku.App.Solving;
using GraphDoku.Core.Model;
using GraphDoku.Solver;

namespace GraphDoku.App.ViewModels;

/// <summary>Ausgang des letzten Solver-Laufs (für Meldung und Farbe der Ergebnisleiste).</summary>
public enum SolveOutcome
{
    /// <summary>Noch kein Ergebnis (oder neues Dokument).</summary>
    None,

    /// <summary>„Lösen“ hat eine Lösung gefunden.</summary>
    Solved,

    /// <summary>„Eindeutigkeit prüfen“: genau eine Lösung.</summary>
    Unique,

    /// <summary>„Eindeutigkeit prüfen“: mindestens zwei Lösungen.</summary>
    Ambiguous,

    Unsolvable,

    Timeout,

    Cancelled,

    /// <summary>Das Rätsel ist ungültig (Details in <see cref="SolveViewModel.ResultDetails"/>).</summary>
    Invalid,

    /// <summary>Der Solver ist mit einer Ausnahme gescheitert.</summary>
    Failed,

    /// <summary>Das Rätsel wurde während oder nach der Suche geändert; das Ergebnis gilt nicht mehr.</summary>
    Outdated,
}

/// <summary>Einstufung eines Ergebnisses für die Farbe der Ergebnisleiste.</summary>
public enum ResultSeverity
{
    /// <summary>Kein Ergebnis, keine Suche.</summary>
    None,

    /// <summary>Suche läuft.</summary>
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// „Lösen“ und „Eindeutigkeit prüfen“: startet den Solver asynchron (<see cref="SolverExtensions"/>), zeigt den
/// Fortschritt, bricht auf Wunsch ab, meldet das Ergebnis auf Deutsch und trägt Lösungen als Anzeige in den Editor ein
/// (<see cref="EditorViewModel.ShowSolution"/>). Der Solver wird injiziert, damit Tests einen Ersatz verwenden können.
/// </summary>
public sealed partial class SolveViewModel : ViewModelBase
{
    private readonly EditorViewModel _editor;
    private CancellationTokenSource? _cancellation;
    private bool _documentReplacedDuringRun;
    private IReadOnlyList<IReadOnlyDictionary<int, int>> _solutions = [];
    private List<int> _differences = [];

    public SolveViewModel(EditorViewModel editor, ISolver solver)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(solver);
        _editor = editor;
        Solver = solver;
        TimeLimitText = SolverTimeText.FormatSeconds(SolverOptions.DefaultTimeLimit);
        editor.ContentChanged += OnContentChanged;
        editor.PropertyChanged += OnEditorPropertyChanged;
    }

    public ISolver Solver { get; }

    /// <summary>Zeitlimit in Sekunden als Eingabetext (Standard aus <see cref="SolverOptions.DefaultTimeLimit"/>).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SolveCommand), nameof(CheckUniquenessCommand))]
    public partial string TimeLimitText { get; set; }

    /// <summary>Gelesenes Zeitlimit; null, solange die Eingabe ungültig ist.</summary>
    public TimeSpan? TimeLimit { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? TimeLimitError { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SolveCommand), nameof(CheckUniquenessCommand), nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(HasStatus), nameof(Severity))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Text neben der Fortschrittsanzeige, z. B. „Suche nach einer Lösung läuft …“.</summary>
    [ObservableProperty]
    public partial string BusyText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Severity))]
    public partial SolveOutcome Outcome { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? ResultMessage { get; private set; }

    /// <summary>Einzelne Fehler (bei ungültigem Rätsel).</summary>
    public ObservableCollection<string> ResultDetails { get; } = [];

    [ObservableProperty]
    public partial bool HasResultDetails { get; private set; }

    public ResultSeverity Severity => IsBusy ? ResultSeverity.Info : Outcome switch
    {
        SolveOutcome.Solved or SolveOutcome.Unique => ResultSeverity.Success,
        SolveOutcome.Ambiguous or SolveOutcome.Timeout or SolveOutcome.Cancelled or SolveOutcome.Outdated => ResultSeverity.Warning,
        SolveOutcome.Unsolvable or SolveOutcome.Invalid or SolveOutcome.Failed => ResultSeverity.Error,
        _ => ResultSeverity.None,
    };

    /// <summary>
    /// Gibt es etwas zu melden (Suche läuft, Ergebnis, Eingabefehler oder angezeigte Lösung)? Sonst zeigt die Leiste einen
    /// kurzen Hinweis zur Bedienung.
    /// </summary>
    public bool HasStatus => IsBusy || ResultMessage is not null || TimeLimitError is not null || _editor.HasSolution;

    /// <summary>Anzahl der zuletzt gefundenen Lösungen, die angezeigt werden können (0–2).</summary>
    public int AvailableSolutionCount => _solutions.Count;

    /// <summary>Knoten, in denen sich die beiden gefundenen Lösungen unterscheiden (nur bei mehrdeutigen Rätseln).</summary>
    public IReadOnlyList<int> SolutionDifferences => _differences;

    public string ShowFirstSolutionText => _solutions.Count > 1 ? "Lösung 1 anzeigen" : "Lösung anzeigen";

    public bool IsSolutionAvailable => _solutions.Count > 0;

    /// <summary>„Lösung (1) anzeigen“ zeigen: bei zwei Lösungen immer, bei einer nur, solange sie nicht angezeigt wird.</summary>
    public bool IsShowFirstSolutionVisible => _solutions.Count > 1 || (_solutions.Count == 1 && !_editor.HasSolution);

    /// <summary>Gibt es Schaltflächen zum Anzeigen oder Ausblenden einer Lösung?</summary>
    public bool HasSolutionActions => _solutions.Count > 0 || _editor.HasSolution;

    public bool IsSecondSolutionAvailable => _solutions.Count > 1;

    public bool IsSolutionShown => _editor.HasSolution;

    /// <summary>Erklärt die Farben der angezeigten Lösung.</summary>
    public string Legend => _editor.SolutionDifferences.Count > 0
        ? "Blau: Lösungswerte · Schwarz: Givens · Orange: Knoten, in denen sich die beiden Lösungen unterscheiden"
        : "Blau: Lösungswerte · Schwarz: Givens";

    private bool CanStart => !IsBusy && TimeLimit is not null;

    private bool CanShowFirstSolution => _solutions.Count > 0;

    private bool CanShowSecondSolution => _solutions.Count > 1;

    /// <summary>Sucht eine Lösung und zeigt sie in den Knoten an.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task SolveAsync() => RunAsync(
        "Suche nach einer Lösung läuft …",
        (puzzle, options, token) => Solver.SolveAsync(puzzle, options, token),
        HandleSolveResult);

    /// <summary>Zählt Lösungen bis 2: eindeutig, mehrdeutig oder unlösbar.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task CheckUniquenessAsync() => RunAsync(
        "Eindeutigkeit wird geprüft (Suche nach bis zu zwei Lösungen) …",
        (puzzle, options, token) => Solver.CountSolutionsAsync(puzzle, limit: 2, options, token),
        HandleCountResult);

    /// <summary>Bricht die laufende Suche ab.</summary>
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        if (_cancellation is { IsCancellationRequested: false } cancellation)
        {
            BusyText = "Wird abgebrochen …";
            cancellation.Cancel();
        }
    }

    [RelayCommand(CanExecute = nameof(CanShowFirstSolution))]
    private void ShowFirstSolution() => ShowSolution(0);

    [RelayCommand(CanExecute = nameof(CanShowSecondSolution))]
    private void ShowSecondSolution() => ShowSolution(1);

    [RelayCommand(CanExecute = nameof(IsSolutionShown))]
    private void HideSolution() => _editor.HideSolution();

    partial void OnTimeLimitTextChanged(string value)
    {
        TimeLimit = SolverTimeText.TryParseSeconds(value, out var limit, out var error) ? limit : null;
        TimeLimitError = error;
        OnPropertyChanged(nameof(TimeLimit));
    }

    private async Task RunAsync<T>(string busyText, Func<Puzzle, SolverOptions, CancellationToken, Task<T>> start, Action<T> handle)
    {
        if (IsBusy || TimeLimit is not { } timeLimit)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _documentReplacedDuringRun = false;
        var version = _editor.ContentVersion;
        BusyText = busyText;
        SetResult(SolveOutcome.None, null);
        IsBusy = true;
        try
        {
            // SolveAsync/CountSolutionsAsync kopieren das Rätsel sofort (hier, im UI-Thread) und rechnen im Hintergrund.
            var result = await start(_editor.Puzzle, new SolverOptions { TimeLimit = timeLimit }, cancellation.Token).ConfigureAwait(true);
            if (version != _editor.ContentVersion)
            {
                SetResult(SolveOutcome.Outdated, _documentReplacedDuringRun
                    ? "Die Suche wurde abgebrochen, weil ein anderes Rätsel geladen wurde."
                    : "Das Rätsel wurde während der Suche geändert. Das Ergebnis passt nicht mehr zum aktuellen Stand und wird verworfen – bitte erneut starten.");
                return;
            }

            handle(result);
        }
#pragma warning disable CA1031 // Jeder Solver-Fehler (z. B. native Bibliothek fehlt) soll als Meldung erscheinen statt die App zu beenden.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            SetResult(SolveOutcome.Failed, $"Der Solver ist mit einem Fehler abgebrochen: {ex.Message}");
        }
        finally
        {
            _cancellation = null;
            IsBusy = false;
        }
    }

    private void HandleSolveResult(SolveResult result)
    {
        switch (result.Status)
        {
            case SolveStatus.Solved:
                SetSolutions([result.Values!]);
                ShowSolution(0);
                SetResult(SolveOutcome.Solved, $"Lösung gefunden ({SolverTimeText.Duration(result.Elapsed)}) und in die Knoten eingetragen. Ob sie die einzige ist, zeigt „Eindeutigkeit prüfen“.");
                break;
            case SolveStatus.Unsolvable:
                DiscardSolutions();
                SetResult(SolveOutcome.Unsolvable, result.Message);
                break;
            case SolveStatus.Timeout:
                SetResult(SolveOutcome.Timeout, $"{result.Message} Ein höheres Zeitlimit kann helfen.");
                break;
            case SolveStatus.Cancelled:
                SetResult(SolveOutcome.Cancelled, "Die Suche wurde abgebrochen; es wurde keine Lösung eingetragen.");
                break;
            default:
                ReportInvalid(result.Errors);
                break;
        }
    }

    private void HandleCountResult(SolutionCountResult result)
    {
        var elapsed = SolverTimeText.Duration(result.Elapsed);
        if (result.Status == CountStatus.Invalid)
        {
            ReportInvalid(result.Errors);
        }
        else if (result.IsAmbiguous)
        {
            SetSolutions(result.Solutions.Take(2).ToList());
            SetResult(SolveOutcome.Ambiguous,
                $"Das Rätsel ist mehrdeutig: Es gibt mindestens zwei verschiedene Lösungen ({elapsed}). Sie unterscheiden sich in "
                + $"{(_differences.Count == 1 ? "einem Knoten" : $"{_differences.Count} Knoten")}; „Lösung 1 anzeigen“ bzw. „Lösung 2 anzeigen“ trägt eine davon ein "
                + "und markiert diese Knoten orange.");
        }
        else if (result.IsUnique)
        {
            SetSolutions(result.Solutions);
            SetResult(SolveOutcome.Unique, $"Das Rätsel ist eindeutig lösbar ({elapsed})."
                + (_editor.HasSolution ? string.Empty : " „Lösung anzeigen“ trägt die Lösung ein."));
        }
        else if (result.IsUnsolvable)
        {
            DiscardSolutions();
            SetResult(SolveOutcome.Unsolvable, result.Message);
        }
        else
        {
            // Zeitlimit oder Abbruch mit 0 oder 1 gefundenen Lösungen: Eindeutigkeit offen.
            if (result.Solutions.Count > 0)
            {
                SetSolutions(result.Solutions.Take(1).ToList());
            }

            var hint = result.Solutions.Count > 0 ? " Die gefundene Lösung kann angezeigt werden." : string.Empty;
            SetResult(
                result.Status == CountStatus.Timeout ? SolveOutcome.Timeout : SolveOutcome.Cancelled,
                result.Message + (result.Status == CountStatus.Timeout ? " Ein höheres Zeitlimit kann helfen." : string.Empty) + hint);
        }
    }

    private void ReportInvalid(IReadOnlyList<string> errors)
    {
        DiscardSolutions();
        SetResult(
            SolveOutcome.Invalid,
            errors.Count == 1
                ? "Das Rätsel ist ungültig und kann nicht gelöst werden. Bitte zuerst dieses Problem beheben:"
                : $"Das Rätsel ist ungültig und kann nicht gelöst werden. Bitte zuerst diese {errors.Count} Probleme beheben:",
            errors);
    }

    private void ShowSolution(int index)
    {
        if (index < _solutions.Count)
        {
            _editor.ShowSolution(_solutions[index], _solutions.Count > 1 ? _differences : null);
        }
    }

    private void SetResult(SolveOutcome outcome, string? message, IEnumerable<string>? details = null)
    {
        Outcome = outcome;
        ResultMessage = message;
        ResultDetails.Clear();
        foreach (var detail in details ?? [])
        {
            ResultDetails.Add(detail);
        }

        HasResultDetails = ResultDetails.Count > 0;
    }

    private void SetSolutions(IReadOnlyList<IReadOnlyDictionary<int, int>> solutions)
    {
        _solutions = solutions;
        _differences = solutions.Count > 1
            ? solutions[0].Where(pair => !solutions[1].TryGetValue(pair.Key, out var other) || other != pair.Value).Select(pair => pair.Key).Order().ToList()
            : [];
        OnSolutionsChanged();
    }

    /// <summary>Gefundene Lösungen verwerfen und die Anzeige leeren (z. B. bei unlösbarem oder ungültigem Rätsel).</summary>
    private void DiscardSolutions()
    {
        _editor.HideSolution();
        SetSolutions([]);
    }

    private void OnSolutionsChanged()
    {
        OnPropertyChanged(nameof(AvailableSolutionCount));
        OnPropertyChanged(nameof(SolutionDifferences));
        OnPropertyChanged(nameof(ShowFirstSolutionText));
        OnPropertyChanged(nameof(IsSolutionAvailable));
        OnPropertyChanged(nameof(IsSecondSolutionAvailable));
        OnPropertyChanged(nameof(IsShowFirstSolutionVisible));
        OnPropertyChanged(nameof(HasSolutionActions));
        ShowFirstSolutionCommand.NotifyCanExecuteChanged();
        ShowSecondSolutionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Inhaltliche Änderung: Gefundene Lösungen gelten nicht mehr (die Anzeige hat der Editor schon ausgeblendet). Läuft
    /// gerade eine Suche, rechnet sie weiter und ihr Ergebnis wird am Ende verworfen; bei einem neuen Dokument wird sie
    /// abgebrochen.
    /// </summary>
    private void OnContentChanged(object? sender, PuzzleContentChangedEventArgs e)
    {
        SetSolutions([]);
        if (IsBusy)
        {
            if (e.DocumentReplaced)
            {
                _documentReplacedDuringRun = true;
                _cancellation?.Cancel();
            }

            return;
        }

        if (e.DocumentReplaced)
        {
            SetResult(SolveOutcome.None, null);
        }
        else if (e.SolutionWasShown || (Outcome != SolveOutcome.None && Outcome != SolveOutcome.Outdated))
        {
            SetResult(SolveOutcome.Outdated, e.SolutionWasShown
                ? "Die Lösung wurde ausgeblendet, weil das Rätsel geändert wurde. „Lösen“ sucht für den neuen Stand."
                : "Das Rätsel wurde geändert; das letzte Solver-Ergebnis gilt nicht mehr.");
        }
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.HasSolution))
        {
            OnPropertyChanged(nameof(IsSolutionShown));
            OnPropertyChanged(nameof(HasStatus));
            OnPropertyChanged(nameof(Legend));
            OnPropertyChanged(nameof(IsShowFirstSolutionVisible));
            OnPropertyChanged(nameof(HasSolutionActions));
            HideSolutionCommand.NotifyCanExecuteChanged();
        }
    }
}
