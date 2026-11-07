using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GraphDoku.App.Views;

/// <summary>Kleine Wertkonverter für die Oberfläche.</summary>
public static class Converters
{
    private static readonly IBrush Problem = new ImmutableSolidColorBrush(Color.Parse("#b42318"));
    private static readonly IBrush Ok = new ImmutableSolidColorBrush(Color.Parse("#15803d"));

    /// <summary>true (es gibt Probleme) → Rot, sonst Grün.</summary>
    public static FuncValueConverter<bool, IBrush> ProblemBrush { get; } = new(hasProblems => hasProblems ? Problem : Ok);
}
