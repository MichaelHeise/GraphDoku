using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GraphDoku.App.ViewModels;

namespace GraphDoku.App.Views;

/// <summary>Kleine Wertkonverter für die Oberfläche.</summary>
public static class Converters
{
    private static readonly IBrush Problem = new ImmutableSolidColorBrush(Color.Parse("#b42318"));
    private static readonly IBrush Ok = new ImmutableSolidColorBrush(Color.Parse("#15803d"));

    private static readonly IBrush NeutralBackground = new ImmutableSolidColorBrush(Color.Parse("#f7f7f5"));
    private static readonly IBrush NeutralBorder = new ImmutableSolidColorBrush(Color.Parse("#dddddd"));
    private static readonly IBrush InfoBackground = new ImmutableSolidColorBrush(Color.Parse("#eef4ff"));
    private static readonly IBrush SuccessBackground = new ImmutableSolidColorBrush(Color.Parse("#ecfdf3"));
    private static readonly IBrush WarningBackground = new ImmutableSolidColorBrush(Color.Parse("#fff7e6"));
    private static readonly IBrush ErrorBackground = new ImmutableSolidColorBrush(Color.Parse("#fef3f2"));
    private static readonly IBrush InfoBorder = new ImmutableSolidColorBrush(Color.Parse("#b2ccff"));
    private static readonly IBrush SuccessBorder = new ImmutableSolidColorBrush(Color.Parse("#75e0a7"));
    private static readonly IBrush WarningBorder = new ImmutableSolidColorBrush(Color.Parse("#f5b041"));
    private static readonly IBrush ErrorBorder = new ImmutableSolidColorBrush(Color.Parse("#fda29b"));

    /// <summary>Hintergrund der Ergebnisleiste des Solvers je nach Einstufung.</summary>
    public static FuncValueConverter<ResultSeverity, IBrush> SeverityBackground { get; } = new(severity => severity switch
    {
        ResultSeverity.Success => SuccessBackground,
        ResultSeverity.Warning => WarningBackground,
        ResultSeverity.Error => ErrorBackground,
        ResultSeverity.Info => InfoBackground,
        _ => NeutralBackground,
    });

    /// <summary>Rahmen der Ergebnisleiste des Solvers je nach Einstufung.</summary>
    public static FuncValueConverter<ResultSeverity, IBrush> SeverityBorder { get; } = new(severity => severity switch
    {
        ResultSeverity.Success => SuccessBorder,
        ResultSeverity.Warning => WarningBorder,
        ResultSeverity.Error => ErrorBorder,
        ResultSeverity.Info => InfoBorder,
        _ => NeutralBorder,
    });

    private static readonly IBrush BranchBadge = new ImmutableSolidColorBrush(Color.Parse("#ea580c"));
    private static readonly IBrush StepBadge = new ImmutableSolidColorBrush(Color.Parse("#fef08a"));
    private static readonly IBrush DarkText = new ImmutableSolidColorBrush(Color.Parse("#111111"));

    /// <summary>Etikett der Schrittart im Erklär-Modus: Verzweigung orange, sonst gelb wie der aktuelle Knoten.</summary>
    public static FuncValueConverter<bool, IBrush> StepBadgeBackground { get; } = new(isBranch => isBranch ? BranchBadge : StepBadge);

    public static FuncValueConverter<bool, IBrush> StepBadgeForeground { get; } = new(isBranch => isBranch ? Brushes.White : DarkText);

    /// <summary>true (es gibt Probleme) → Rot, sonst Grün.</summary>
    public static FuncValueConverter<bool, IBrush> ProblemBrush { get; } = new(hasProblems => hasProblems ? Problem : Ok);

    /// <summary>Auswahlwert eines Regelparameters (JSON, z. B. „atLeast“) → deutscher Text („mindestens k“).</summary>
    public static FuncValueConverter<string?, string> ChoiceLabel { get; } = new(GraphDoku.App.Editing.ParameterTexts.ChoiceLabel);
}
