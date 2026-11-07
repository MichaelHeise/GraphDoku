namespace GraphDoku.Core.Serialization;

/// <summary>Eine Rätseldatei konnte nicht gelesen werden; <see cref="Errors"/> enthält alle deutschen Fehlermeldungen.</summary>
public sealed class PuzzleFormatException : Exception
{
    public PuzzleFormatException()
        : this([])
    {
    }

    public PuzzleFormatException(string message)
        : this([message])
    {
    }

    public PuzzleFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [message];
    }

    public PuzzleFormatException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors) => errors.Count switch
    {
        0 => "Die Rätseldatei ist ungültig.",
        1 => $"Die Rätseldatei ist ungültig: {errors[0]}",
        _ => $"Die Rätseldatei ist ungültig ({errors.Count} Fehler):{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", errors)}",
    };
}
