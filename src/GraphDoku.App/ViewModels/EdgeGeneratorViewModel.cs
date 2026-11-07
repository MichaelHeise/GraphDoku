using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GraphDoku.App.Editing;
using GraphDoku.App.Editing.Commands;
using GraphDoku.Core.Generation;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.App.ViewModels;

/// <summary>Abstandsregel in der Auswahlliste des Generators.</summary>
public sealed record DistanceOption(DistanceKind Kind, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Richtungsregel für gerichtete Kanten („Kleiner als“) in der Auswahlliste des Generators.</summary>
public sealed record DirectionOption(EdgeDirectionPolicy Policy, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Dialog „Kanten erzeugen“ (Paket 5): Abstandsregel, Regel und Parameter der neuen Kanten, Richtung bei „Kleiner als“
/// und Knotenmenge (Auswahl oder alle Knoten) wählen. Jede Änderung berechnet mit <see cref="EdgeGenerator.Generate"/> eine
/// Vorschau (Anzahl, übersprungene Paare, Fehler); <see cref="Apply"/> fügt die Kanten als <b>ein</b> Undo-Schritt ein.
/// Das Rätsel wird erst durch <see cref="Apply"/> verändert.
/// </summary>
public sealed partial class EdgeGeneratorViewModel : ViewModelBase
{
    private static readonly NumberFormatInfo GermanNumbers = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };
    private readonly EditorViewModel _editor;
    private readonly EdgeGenerator _generator;
    private readonly IReadOnlyList<int> _selection;
    private RuleParams _params = new();

    public EdgeGeneratorViewModel(EditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
        _generator = new EdgeGenerator(editor.Registry);
        _selection = [.. editor.SelectedNodeIds];
        Grid = editor.Puzzle.Grid;
        Rules = editor.EdgeRules;
        DistanceOptions =
        [
            new DistanceOption(DistanceKind.Orthogonal, "Direkte Nachbarn (gemeinsame Kante)"),
            new DistanceOption(DistanceKind.WithDiagonals, "Nachbarn inkl. diagonal (gemeinsame Ecke)"),
            .. GridGeometry.SupportsKnightMove(Grid) ? [new DistanceOption(DistanceKind.KnightMove, "Springerzug")] : Array.Empty<DistanceOption>(),
            new DistanceOption(DistanceKind.Free, "Freier Abstand (Mittelpunkte)"),
        ];
        KnightMoveHint = GridGeometry.SupportsKnightMove(Grid)
            ? null
            : $"Den Springerzug gibt es nur auf dem Quadratgitter (dieses Rätsel: {GridNames.ToGerman(Grid)}). Ein freier Abstand kann ihn ersetzen.";
        FreeDistanceHint = Grid switch
        {
            GridType.Square => "Abstand der Zellmittelpunkte in Zellbreiten: 1 = Nachbar, 1,414 = diagonal, 2,236 = Springerzug.",
            GridType.Triangle => "Abstand der Zellmittelpunkte in Seitenlängen: 0,577 = Kantennachbar, 1 = gleich ausgerichteter Nachbar in der Zeile.",
            _ => "Abstand der Zellmittelpunkte in Zellbreiten: 1 = Nachbar, 1,732 = übernächster Nachbar schräg, 2 = übernächster in einer Linie.",
        };
        DirectionOptions =
        [
            new DirectionOption(EdgeDirectionPolicy.ReadingOrder, "Werte steigen in Leserichtung (nach rechts bzw. unten)"),
            new DirectionOption(EdgeDirectionPolicy.ReverseReadingOrder, "Werte fallen in Leserichtung (steigen nach links bzw. oben)"),
        ];
        SelectionCount = _selection.Count;
        NodeCount = editor.Puzzle.Nodes.Count;
        SelectedDistance = DistanceOptions[0];
        SelectedDirection = DirectionOptions[0];
        UseSelection = HasSelection;
        SelectedRule = editor.NewEdgeRule ?? (Rules.Count > 0 ? Rules[0] : null);
        LoadRuleParameters();
        UpdatePreview();
    }

    public GridType Grid { get; }

    public IReadOnlyList<IRule> Rules { get; }

    public IReadOnlyList<DistanceOption> DistanceOptions { get; }

    public IReadOnlyList<DirectionOption> DirectionOptions { get; }

    /// <summary>Hinweis, warum der Springerzug fehlt (null auf dem Quadratgitter).</summary>
    public string? KnightMoveHint { get; }

    /// <summary>Erklärung der Abstandswerte für das Gitter des Rätsels.</summary>
    public string FreeDistanceHint { get; }

    public int SelectionCount { get; }

    public int NodeCount { get; }

    /// <summary>Nur mit mindestens zwei ausgewählten Knoten lässt sich der Generator auf die Auswahl beschränken.</summary>
    public bool HasSelection => SelectionCount >= 2;

    public string SelectionScopeText => $"Ausgewählte Knoten ({SelectionCount})";

    public string AllNodesScopeText => $"Alle Knoten ({NodeCount})";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFreeDistance))]
    public partial DistanceOption SelectedDistance { get; set; }

    public bool IsFreeDistance => SelectedDistance.Kind == DistanceKind.Free;

    /// <summary>Kleinster Abstand (Text, Dezimalkomma oder -punkt).</summary>
    [ObservableProperty]
    public partial string MinDistanceText { get; set; } = "1";

    /// <summary>Größter Abstand; leer = gleich dem kleinsten (genauer Abstand).</summary>
    [ObservableProperty]
    public partial string MaxDistanceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ToleranceText { get; set; } = Format(GridGeometry.DefaultTolerance);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirected))]
    public partial IRule? SelectedRule { get; set; }

    /// <summary>Gerichtete Regel („Kleiner als“): Richtungswahl anzeigen.</summary>
    public bool IsDirected => SelectedRule?.Kind.IsDirected() ?? false;

    public ObservableCollection<RuleParameterViewModel> Parameters { get; } = [];

    [ObservableProperty]
    public partial bool HasParameters { get; private set; }

    [ObservableProperty]
    public partial DirectionOption SelectedDirection { get; set; }

    /// <summary>true = nur zwischen ausgewählten Knoten, false = alle Knoten.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseAllNodes))]
    public partial bool UseSelection { get; set; }

    public bool UseAllNodes
    {
        get => !UseSelection;
        set => UseSelection = !value;
    }

    /// <summary>Ergebnis der letzten Vorschau (null, solange eine Eingabe nicht lesbar ist).</summary>
    public EdgeGenerationResult? Preview { get; private set; }

    [ObservableProperty]
    public partial string PreviewMessage { get; private set; } = string.Empty;

    public ObservableCollection<string> PreviewErrors { get; } = [];

    [ObservableProperty]
    public partial bool HasPreviewErrors { get; private set; }

    [ObservableProperty]
    public partial ResultSeverity PreviewSeverity { get; private set; }

    /// <summary>true, wenn „Kanten erzeugen“ mindestens eine Kante einfügen würde.</summary>
    [ObservableProperty]
    public partial bool CanApply { get; private set; }

    /// <summary>Anzahl der Kanten, die „Kanten erzeugen“ einfügen würde.</summary>
    [ObservableProperty]
    public partial int PreviewEdgeCount { get; private set; }

    /// <summary>
    /// Erzeugt die Kanten mit den aktuellen Einstellungen auf dem aktuellen Rätsel und fügt sie als ein Undo-Schritt ein.
    /// Die Statuszeile zeigt die Meldung des Generators. false, wenn nichts eingefügt wurde (Fehler oder keine neuen Kanten).
    /// </summary>
    public bool Apply()
    {
        UpdatePreview();
        if (!CanApply || Preview is not { } result)
        {
            _editor.StatusMessage = PreviewErrors.Count > 0 ? $"Keine Kanten erzeugt: {PreviewErrors[0]}" : $"Keine Kanten erzeugt. {PreviewMessage}";
            return false;
        }

        var command = new AddGeneratedEdgesCommand(result);
        try
        {
            _editor.Execute(command);
        }
        catch (InvalidOperationException ex)
        {
            // ApplyTo prüft vor dem Einfügen (Rätsel und Verlauf bleiben unverändert); die Meldung gehört in die
            // Statuszeile, nicht in einen Absturz.
            _editor.StatusMessage = $"Keine Kanten erzeugt: {ex.Message}";
            return false;
        }

        _editor.StatusMessage = result.Message;
        return true;
    }

    /// <summary>Der Auftrag an den Generator; null mit Fehlermeldungen, wenn eine Zahl nicht lesbar ist.</summary>
    public EdgeGenerationRequest? BuildRequest(out IReadOnlyList<string> inputErrors)
    {
        var errors = new List<string>();
        DistanceRule distance;
        switch (SelectedDistance.Kind)
        {
            case DistanceKind.Orthogonal:
                distance = DistanceRule.Orthogonal;
                break;
            case DistanceKind.WithDiagonals:
                distance = DistanceRule.WithDiagonals;
                break;
            case DistanceKind.KnightMove:
                distance = DistanceRule.KnightMove;
                break;
            default:
                var min = ParseNumber(MinDistanceText, "kleinster Abstand", errors);
                var max = string.IsNullOrWhiteSpace(MaxDistanceText) ? min : ParseNumber(MaxDistanceText, "größter Abstand", errors);
                var tolerance = string.IsNullOrWhiteSpace(ToleranceText) ? 0 : ParseNumber(ToleranceText, "Toleranz", errors);
                distance = DistanceRule.Range(min ?? 0, max ?? 0, tolerance ?? 0);
                break;
        }

        foreach (var parameter in Parameters.Where(p => p.Error is not null))
        {
            errors.Add($"{parameter.DisplayName}: {parameter.Error}");
        }

        inputErrors = errors;
        if (errors.Count > 0 || SelectedRule is not { } rule)
        {
            if (SelectedRule is null)
            {
                errors.Add("Keine Kantenregel gewählt.");
            }

            return null;
        }

        return new EdgeGenerationRequest(distance, rule.TypeName)
        {
            Params = _params.Clone(),
            NodeIds = UseSelection ? _selection : null,
            Direction = IsDirected ? SelectedDirection.Policy : EdgeDirectionPolicy.ReadingOrder,
        };
    }

    partial void OnSelectedDistanceChanged(DistanceOption value) => UpdatePreview();

    partial void OnMinDistanceTextChanged(string value) => UpdatePreview();

    partial void OnMaxDistanceTextChanged(string value) => UpdatePreview();

    partial void OnToleranceTextChanged(string value) => UpdatePreview();

    partial void OnSelectedDirectionChanged(DirectionOption value) => UpdatePreview();

    partial void OnUseSelectionChanged(bool value)
    {
        if (value && !HasSelection)
        {
            UseSelection = false;
            return;
        }

        UpdatePreview();
    }

    partial void OnSelectedRuleChanged(IRule? value)
    {
        LoadRuleParameters();
        UpdatePreview();
    }

    private void LoadRuleParameters()
    {
        _params = SelectedRule?.CreateDefaultParams() ?? new RuleParams();
        Parameters.Clear();
        foreach (var definition in SelectedRule?.Parameters ?? [])
        {
            var parameter = new RuleParameterViewModel(definition, _params, OnParameterChanged);

            // Unlesbarer Text meldet sich nicht über den Rückruf, sondern nur über Error: Vorschau dann ebenfalls neu.
            parameter.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RuleParameterViewModel.Error))
                {
                    UpdatePreview();
                }
            };
            Parameters.Add(parameter);
        }

        HasParameters = Parameters.Count > 0;
    }

    private void OnParameterChanged(RuleParameterViewModel parameter)
    {
        var parameters = _params.Clone();
        if (parameter.TryApplyTo(parameters))
        {
            _params = parameters;
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        // Wird schon aus dem Konstruktor (Eigenschaften mit Standardwerten) aufgerufen, bevor alles gesetzt ist.
        if (_editor is null || SelectedDistance is null || SelectedDirection is null)
        {
            return;
        }

        PreviewErrors.Clear();
        var request = BuildRequest(out var inputErrors);
        Preview = request is null ? null : _generator.Generate(_editor.Puzzle, request);
        var errors = request is null ? inputErrors : Preview!.Errors;
        foreach (var error in errors)
        {
            PreviewErrors.Add(error);
        }

        HasPreviewErrors = PreviewErrors.Count > 0;
        PreviewEdgeCount = Preview is { IsSuccess: true } ok ? ok.Edges.Count : 0;
        CanApply = PreviewEdgeCount > 0;
        if (HasPreviewErrors)
        {
            PreviewSeverity = ResultSeverity.Error;
            PreviewMessage = PreviewErrors.Count == 1
                ? "Mit dieser Einstellung können keine Kanten erzeugt werden:"
                : $"Mit dieser Einstellung können keine Kanten erzeugt werden ({PreviewErrors.Count} Fehler):";
            return;
        }

        var result = Preview!;
        var rule = SelectedRule!.DisplayName;
        var distance = request!.Distance.Description;
        var scope = UseSelection ? " zwischen den ausgewählten Knoten" : string.Empty;
        if (result.MatchingPairs == 0)
        {
            PreviewSeverity = ResultSeverity.Warning;
            PreviewMessage = $"Kein Knotenpaar{scope} erfüllt die Abstandsregel „{distance}“ – es entstehen keine Kanten.";
        }
        else if (result.Edges.Count == 0)
        {
            PreviewSeverity = ResultSeverity.Warning;
            PreviewMessage = result.MatchingPairs == 1
                ? $"Das passende Knotenpaar hat bereits eine Kante „{rule}“ – es entstehen keine Kanten."
                : $"Alle {result.MatchingPairs} passenden Knotenpaare haben bereits eine Kante „{rule}“ – es entstehen keine Kanten.";
        }
        else
        {
            PreviewSeverity = ResultSeverity.Success;
            var created = result.Edges.Count == 1 ? $"1 neue Kante „{rule}“" : $"{result.Edges.Count} neue Kanten „{rule}“";
            var skipped = result.SkippedExisting switch
            {
                0 => string.Empty,
                1 => " 1 Knotenpaar wird übersprungen (Kante bereits vorhanden).",
                _ => $" {result.SkippedExisting} Knotenpaare werden übersprungen (Kante bereits vorhanden).",
            };
            PreviewMessage = $"{created}{scope} ({distance}).{skipped}";
        }
    }

    private static double? ParseNumber(string? text, string name, List<string> errors)
    {
        var normalized = (text ?? string.Empty).Trim().Replace(',', '.');
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
        {
            return value;
        }

        errors.Add($"Bitte für „{name}“ eine Zahl eingeben (z. B. 1,5), nicht „{text}“.");
        return null;
    }

    private static string Format(double value) => value.ToString("0.##########", GermanNumbers);
}
