using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GraphDoku.App.Explaining;
using GraphDoku.Core.Model;
using GraphDoku.Explainer;
using GraphDoku.Solver;

namespace GraphDoku.App.ViewModels;

/// <summary>Ausgang der letzten Erklärung (für Meldung und Farbe der Schrittleiste).</summary>
public enum ExplainOutcome
{
    /// <summary>Noch keine Erklärung (oder neues Dokument).</summary>
    None,

    /// <summary>Vollständig erklärt, Lösung eindeutig.</summary>
    Solved,

    /// <summary>Vollständig erklärt anhand einer von mehreren Lösungen.</summary>
    Ambiguous,

    /// <summary>Vollständig erklärt, Eindeutigkeit offen.</summary>
    Unverified,

    Unsolvable,

    /// <summary>Ohne Raten kein Fortschritt und keine Solver-Lösung (Zeitlimit).</summary>
    Stuck,

    Invalid,

    Cancelled,

    /// <summary>Die Erklärung ist mit einer Ausnahme gescheitert (z. B. „Interner Fehler“ der Engine).</summary>
    Failed,

    /// <summary>Das Rätsel wurde während oder nach der Erklärung geändert; sie gilt nicht mehr.</summary>
    Outdated,
}

/// <summary>
/// Erklär-Modus (Paket 6): berechnet die Erklärung asynchron mit Abbruch (wie <see cref="SolveViewModel"/>), führt
/// durch die Schritte (Vor/Zurück, Anfang/Ende, Schieberegler) und gibt den Zustand des aktuellen Schritts als
/// <see cref="ExplanationView"/> an die Zeichenfläche (<see cref="EditorViewModel.ShowExplanation"/>). Jede inhaltliche
/// Änderung des Rätsels beendet den Modus.
/// </summary>
public sealed partial class ExplainViewModel : ViewModelBase
{
    private const string StartText =
        "Ausgangslage: Givens und die Wertebereiche der Knoten, noch kein Schritt angewendet. „Vor“ (→) zeigt den ersten Schritt.";

    private readonly EditorViewModel _editor;
    private readonly SolveViewModel _solve;
    private CancellationTokenSource? _cancellation;
    private bool _documentReplacedDuringRun;
    private int _position;
    private IReadOnlyList<int> _branchPositions = [];

    /// <param name="editor">Der Editor, in dessen Zeichenfläche die Erklärung angezeigt wird.</param>
    /// <param name="solve">Lösen-Leiste; deren Zeitlimit gilt auch für die Solver-Aufrufe der Erklärung.</param>
    /// <param name="explainer">Berechnet die Erklärung (in der App die <see cref="ExplanationEngine"/>).</param>
    public ExplainViewModel(EditorViewModel editor, SolveViewModel solve, IPuzzleExplainer explainer)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(solve);
        ArgumentNullException.ThrowIfNull(explainer);
        _editor = editor;
        _solve = solve;
        Explainer = explainer;
        editor.ContentChanged += OnContentChanged;
        editor.PropertyChanged += OnEditorPropertyChanged;
        solve.PropertyChanged += OnSolvePropertyChanged;
    }

    public IPuzzleExplainer Explainer { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExplainCommand), nameof(CancelCommand), nameof(CloseCommand))]
    [NotifyPropertyChangedFor(nameof(Severity), nameof(BarSeverity), nameof(IsBarVisible))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string BusyText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Severity), nameof(BarSeverity))]
    public partial ExplainOutcome Outcome { get; private set; }

    /// <summary>Meldung zum Ergebnis (Text der Engine, ergänzt) oder Hinweis, dass die Erklärung nicht mehr gilt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBarVisible))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    public partial string? StatusMessage { get; private set; }

    /// <summary>Fehler eines ungültigen Rätsels.</summary>
    public ObservableCollection<string> Errors { get; } = [];

    [ObservableProperty]
    public partial bool HasErrors { get; private set; }

    /// <summary>Kandidaten in den offenen Knoten anzeigen (Standard: ein).</summary>
    [ObservableProperty]
    public partial bool ShowCandidates { get; set; } = true;

    /// <summary>Die angezeigte Erklärung; <c>null</c> außerhalb des Erklär-Modus.</summary>
    public ExplanationResult? Result { get; private set; }

    /// <summary>Erklär-Modus aktiv: Es gibt Schritte zum Navigieren, die Zeichenfläche zeigt deren Zustand.</summary>
    public bool IsActive => Result is not null;

    /// <summary>Die Schrittleiste ist sichtbar (Berechnung läuft, Modus aktiv oder eine Meldung steht an).</summary>
    public bool IsBarVisible => IsBusy || IsActive || StatusMessage is not null;

    public int StepCount => Result?.Steps.Count ?? 0;

    /// <summary>Anzahl der angewendeten Schritte, 0 (Ausgangslage) bis <see cref="StepCount"/>; Werte außerhalb werden begrenzt.</summary>
    public int Position
    {
        get => _position;
        set
        {
            var clamped = Math.Clamp(value, 0, StepCount);
            if (SetProperty(ref _position, clamped))
            {
                OnPositionChanged();
            }
            else if (clamped != value)
            {
                // Der Schieberegler soll auf den gültigen Wert zurückspringen.
                OnPropertyChanged(nameof(SliderValue));
            }
        }
    }

    /// <summary>Für den Schieberegler (double); gerundet auf ganze Schritte.</summary>
    public double SliderValue
    {
        get => Position;
        set => Position = double.IsFinite(value) ? (int)Math.Round(value) : 0;
    }

    /// <summary>„Schritt n von N“.</summary>
    public string StepCounterText => $"Schritt {Position} von {StepCount}";

    /// <summary>Der zuletzt angewendete Schritt (bei Position 0 keiner).</summary>
    public ExplanationStep? CurrentStep => Result is { } result && Position > 0 ? result.Steps[Position - 1] : null;

    public bool IsBranchStep => CurrentStep?.Kind == StepKind.Branch;

    /// <summary>Art des Schritts, z. B. „Gestrichene Kandidaten“; bei einer Verzweigung „Verzweigung – geraten“.</summary>
    public string StepKindText => CurrentStep switch
    {
        null => "Ausgangslage",
        { Kind: StepKind.Branch } => "Verzweigung – geraten",
        { Kind: var kind } => Capitalize(kind.ToGermanText()),
    };

    /// <summary>Schlussweise, z. B. „Einziger Kandidat“ (bei einer Verzweigung nicht wiederholt).</summary>
    public string? TechniqueText => CurrentStep is { Kind: not StepKind.Branch } step ? step.Technique.ToGermanText() : null;

    /// <summary>Kurzfassung der Wirkung, z. B. „Knoten 21 = 5“.</summary>
    public string? SummaryText => CurrentStep?.Summary;

    /// <summary>Deutscher Begründungstext des Schritts (Ausgangslage: kurzer Hinweis).</summary>
    public string ReasonText => CurrentStep?.Text ?? (IsActive ? StartText : string.Empty);

    /// <summary>Verantwortliche Regel, z. B. „Gruppe 3 „Zeile 3“ · Alle verschieden“; <c>null</c> ohne Regel.</summary>
    public string? RuleText => CurrentStep?.Constraint is { } constraint && Result is { } result ? DescribeConstraint(result.Puzzle, constraint) : null;

    /// <summary>Wie oft geraten werden musste, mit den Schrittnummern.</summary>
    public string BranchSummary => _branchPositions.Count switch
    {
        0 => "Ohne Verzweigung (ohne Raten).",
        1 => $"1 Verzweigung: Schritt {_branchPositions[0]}.",
        _ => $"{_branchPositions.Count} Verzweigungen: Schritte {FormatList(_branchPositions)}",
    };

    /// <summary>Beschriftung der Schaltfläche zum Beenden bzw. Schließen der Leiste.</summary>
    public string CloseText => IsActive ? "Erklärung beenden" : "Schließen";

    public ResultSeverity Severity => IsBusy ? ResultSeverity.Info : Outcome switch
    {
        ExplainOutcome.Solved => ResultSeverity.Success,
        ExplainOutcome.Ambiguous or ExplainOutcome.Unverified or ExplainOutcome.Stuck or ExplainOutcome.Cancelled or ExplainOutcome.Outdated => ResultSeverity.Warning,
        ExplainOutcome.Unsolvable or ExplainOutcome.Invalid or ExplainOutcome.Failed => ResultSeverity.Error,
        _ => ResultSeverity.None,
    };

    /// <summary>
    /// Farbe der Schrittleiste: im aktiven Modus neutral (das Ergebnis steht farbig im Panel), sonst wie
    /// <see cref="Severity"/> (Berechnung, Meldung ohne Erklärung).
    /// </summary>
    public ResultSeverity BarSeverity => IsActive && !IsBusy ? ResultSeverity.None : Severity;

    private bool CanExplain => !IsBusy && _solve.TimeLimit is not null;

    private bool CanGoBack => IsActive && Position > 0;

    private bool CanGoForward => IsActive && Position < StepCount;

    private bool CanClose => !IsBusy && (IsActive || StatusMessage is not null);

    /// <summary>Berechnet die Erklärung für den aktuellen Stand und startet den Erklär-Modus bei der Ausgangslage.</summary>
    [RelayCommand(CanExecute = nameof(CanExplain))]
    private async Task ExplainAsync()
    {
        if (IsBusy || _solve.TimeLimit is not { } timeLimit)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _documentReplacedDuringRun = false;
        var version = _editor.ContentVersion;
        Deactivate();
        SetStatus(ExplainOutcome.None, null);
        BusyText = "Erklärung wird berechnet …";
        IsBusy = true;
        try
        {
            // Die Erklärung kopiert das Rätsel sofort (hier, im UI-Thread) und rechnet im Hintergrund.
            var result = await Explainer.ExplainAsync(_editor.Puzzle, new SolverOptions { TimeLimit = timeLimit }, cancellation.Token).ConfigureAwait(true);
            if (version != _editor.ContentVersion)
            {
                ReportOutdated();
                return;
            }

            HandleResult(result);
        }
#pragma warning disable CA1031 // Jeder Fehler (z. B. „Interner Fehler“ der Engine, fehlende native Bibliothek) soll als Meldung erscheinen.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Auch ein Fehler gehört zum alten Stand: nach einer Änderung oder einem anderen Rätsel nicht als Fehler melden.
            if (version != _editor.ContentVersion)
            {
                ReportOutdated();
            }
            else
            {
                SetStatus(ExplainOutcome.Failed, ex is InvalidOperationException
                    ? $"Die Erklärung ist fehlgeschlagen. {ex.Message}"
                    : $"Die Erklärung ist mit einem Fehler abgebrochen: {ex.Message}");
            }
        }
        finally
        {
            _cancellation = null;
            IsBusy = false;
        }
    }

    /// <summary>Bricht die laufende Berechnung ab; die bis dahin gefundenen Schritte bleiben ansehbar.</summary>
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        if (_cancellation is { IsCancellationRequested: false } cancellation)
        {
            BusyText = "Wird abgebrochen …";
            cancellation.Cancel();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void First() => Position = 0;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Previous() => Position--;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Next() => Position++;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Last() => Position = StepCount;

    /// <summary>Beendet den Erklär-Modus bzw. schließt die Meldung; die Zeichenfläche zeigt wieder den Editor.</summary>
    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        Deactivate();
        SetStatus(ExplainOutcome.None, null);
    }

    partial void OnShowCandidatesChanged(bool value) => PushView();

    private void ReportOutdated() => SetStatus(ExplainOutcome.Outdated, _documentReplacedDuringRun
        ? "Die Erklärung wurde abgebrochen, weil ein anderes Rätsel geladen wurde."
        : "Das Rätsel wurde während der Berechnung geändert. Die Erklärung passt nicht mehr zum aktuellen Stand und wird verworfen – bitte erneut „Erklären“.");

    private void HandleResult(ExplanationResult result)
    {
        var message = result.Message;
        if (result.Status == ExplanationStatus.Cancelled)
        {
            message += result.Steps.Count switch
            {
                0 => " Bis dahin wurde kein Schritt gefunden.",
                1 => " Der erste Schritt kann angesehen werden.",
                _ => $" Die ersten {result.Steps.Count} Schritte können angesehen werden.",
            };
        }

        if (result.Status == ExplanationStatus.Invalid && result.Errors.Count > 0)
        {
            // Die Fehler stehen als Liste darunter; die Meldung der Engine wiederholt sie nur.
            message = result.Errors.Count == 1
                ? "Das Rätsel ist ungültig und kann nicht erklärt werden. Bitte zuerst dieses Problem beheben:"
                : $"Das Rätsel ist ungültig und kann nicht erklärt werden. Bitte zuerst diese {result.Errors.Count} Probleme beheben:";
        }

        SetStatus(Map(result.Status), message, result.Errors);
        if (result.HasSnapshots && (result.Status != ExplanationStatus.Cancelled || result.Steps.Count > 0))
        {
            Activate(result);
        }
    }

    private void Activate(ExplanationResult result)
    {
        Result = result;
        _branchPositions = [.. result.Steps.Where(s => s.Kind == StepKind.Branch).Select(s => s.Index + 1)];
        _position = 0;
        OnModeChanged();
        OnPositionChanged();
    }

    /// <summary>Beendet den Modus (ohne Meldung zu ändern) und blendet die Anzeige auf der Zeichenfläche aus.</summary>
    private void Deactivate()
    {
        if (Result is null)
        {
            return;
        }

        Result = null;
        _branchPositions = [];
        _position = 0;
        _editor.HideExplanation();
        OnModeChanged();
        OnPositionChanged();
    }

    private void OnModeChanged()
    {
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsBarVisible));
        OnPropertyChanged(nameof(BarSeverity));
        OnPropertyChanged(nameof(StepCount));
        OnPropertyChanged(nameof(BranchSummary));
        OnPropertyChanged(nameof(CloseText));
        CloseCommand.NotifyCanExecuteChanged();
    }

    private void OnPositionChanged()
    {
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(SliderValue));
        OnPropertyChanged(nameof(StepCounterText));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(IsBranchStep));
        OnPropertyChanged(nameof(StepKindText));
        OnPropertyChanged(nameof(TechniqueText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(ReasonText));
        OnPropertyChanged(nameof(RuleText));
        FirstCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        LastCommand.NotifyCanExecuteChanged();
        PushView();
    }

    private void PushView()
    {
        if (Result is { } result)
        {
            _editor.ShowExplanation(new ExplanationView(result, Position, ShowCandidates));
        }
    }

    private void SetStatus(ExplainOutcome outcome, string? message, IEnumerable<string>? errors = null)
    {
        Outcome = outcome;
        StatusMessage = message;
        Errors.Clear();
        foreach (var error in errors ?? [])
        {
            Errors.Add(error);
        }

        HasErrors = Errors.Count > 0;
    }

    /// <summary>
    /// Inhaltliche Änderung: Eine angezeigte Erklärung gilt nicht mehr (der Editor hat sie schon ausgeblendet). Läuft
    /// gerade eine Berechnung, wird ihr Ergebnis am Ende verworfen; bei einem neuen Dokument wird sie abgebrochen.
    /// </summary>
    private void OnContentChanged(object? sender, PuzzleContentChangedEventArgs e)
    {
        if (IsBusy)
        {
            if (e.DocumentReplaced)
            {
                _documentReplacedDuringRun = true;
                _cancellation?.Cancel();
            }

            return;
        }

        Deactivate();
        if (e.DocumentReplaced)
        {
            SetStatus(ExplainOutcome.None, null);
        }
        else if (e.ExplanationWasShown)
        {
            SetStatus(ExplainOutcome.Outdated, "Die Erklärung wurde beendet, weil das Rätsel geändert wurde. „Erklären“ (F7) erklärt den neuen Stand.");
        }
        else if (StatusMessage is not null && Outcome != ExplainOutcome.Outdated)
        {
            SetStatus(ExplainOutcome.Outdated, "Das Rätsel wurde geändert; das letzte Ergebnis der Erklärung gilt nicht mehr.");
        }
    }

    /// <summary>Hat der Editor die Erklärung von sich aus ausgeblendet (z. B. weil eine Lösung angezeigt wird), endet der Modus.</summary>
    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.IsExplaining) && !_editor.IsExplaining && Result is not null)
        {
            Deactivate();
            if (!IsBusy)
            {
                SetStatus(ExplainOutcome.None, null);
            }
        }
    }

    private void OnSolvePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SolveViewModel.TimeLimit))
        {
            ExplainCommand.NotifyCanExecuteChanged();
        }
    }

    private string DescribeConstraint(Puzzle puzzle, ConstraintRef constraint)
    {
        IConstraint? target = constraint.Kind == ConstraintKind.Edge ? puzzle.FindEdge(constraint.Id) : puzzle.FindGroup(constraint.Id);
        if (target is null)
        {
            return constraint.ToString();
        }

        var name = target is Group { Label: { Length: > 0 } label } ? $"{constraint} „{label}“" : constraint.ToString();
        if (target is Edge edge)
        {
            name += $" (Knoten {edge.From} – {edge.To})";
        }

        var rule = _editor.Registry.TryGet(target.Rule, out var known) ? known.DisplayName : target.Rule;
        return $"{name} · {rule}";
    }

    private static ExplainOutcome Map(ExplanationStatus status) => status switch
    {
        ExplanationStatus.Solved => ExplainOutcome.Solved,
        ExplanationStatus.SolvedAmbiguous => ExplainOutcome.Ambiguous,
        ExplanationStatus.SolvedUnverified => ExplainOutcome.Unverified,
        ExplanationStatus.Unsolvable => ExplainOutcome.Unsolvable,
        ExplanationStatus.Stuck => ExplainOutcome.Stuck,
        ExplanationStatus.Invalid => ExplainOutcome.Invalid,
        ExplanationStatus.Cancelled => ExplainOutcome.Cancelled,
        _ => ExplainOutcome.Failed,
    };

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>„57, 103 und 150.“; mehr als acht Einträge gekürzt („1, 2, …, 8 …“).</summary>
    private static string FormatList(IReadOnlyList<int> values)
    {
        var shown = values.Take(8).Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        if (values.Count > 8)
        {
            return string.Join(", ", shown) + " …";
        }

        return string.Join(", ", shown[..^1]) + " und " + shown[^1] + ".";
    }
}
