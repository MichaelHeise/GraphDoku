using System.Diagnostics.CodeAnalysis;

namespace GraphDoku.Solver;

/// <summary>Ergebnis von <see cref="ISolver.Solve"/>.</summary>
public enum SolveStatus
{
    /// <summary>Eine Lösung wurde gefunden (<see cref="SolveResult.Values"/>).</summary>
    Solved,

    /// <summary>Es gibt beweisbar keine Lösung.</summary>
    Unsolvable,

    /// <summary>Das Zeitlimit wurde erreicht, ohne dass eine Lösung gefunden oder die Unlösbarkeit bewiesen wurde.</summary>
    Timeout,

    /// <summary>Die Suche wurde über das CancellationToken abgebrochen.</summary>
    Cancelled,

    /// <summary>Das Rätsel ist ungültig oder lässt sich nicht übersetzen (<see cref="SolveResult.Errors"/>).</summary>
    Invalid,
}

/// <summary>Ergebnis einer Lösungssuche: Status, Werte pro Knoten-Id und eine deutsche Meldung.</summary>
public sealed class SolveResult
{
    private SolveResult(SolveStatus status, IReadOnlyDictionary<int, int>? values, IReadOnlyList<string> errors, string message, TimeSpan elapsed)
    {
        Status = status;
        Values = values;
        Errors = errors;
        Message = message;
        Elapsed = elapsed;
    }

    public SolveStatus Status { get; }

    /// <summary>Wert pro Knoten-Id; nur bei <see cref="SolveStatus.Solved"/> gesetzt.</summary>
    public IReadOnlyDictionary<int, int>? Values { get; }

    /// <summary>Fehlermeldungen bei <see cref="SolveStatus.Invalid"/>, sonst leer.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Kurze deutsche Beschreibung des Ergebnisses für die Oberfläche.</summary>
    public string Message { get; }

    /// <summary>Gesamtdauer des Aufrufs.</summary>
    public TimeSpan Elapsed { get; }

    [MemberNotNullWhen(true, nameof(Values))]
    public bool IsSolved => Status == SolveStatus.Solved;

    public static SolveResult Solved(IReadOnlyDictionary<int, int> values, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new(SolveStatus.Solved, values, [], "Lösung gefunden.", elapsed);
    }

    public static SolveResult Unsolvable(TimeSpan elapsed) =>
        new(SolveStatus.Unsolvable, null, [], "Das Rätsel ist unlösbar: Es gibt keine Belegung, die alle Regeln erfüllt.", elapsed);

    public static SolveResult Timeout(TimeSpan timeLimit, TimeSpan elapsed) =>
        new(SolveStatus.Timeout, null, [], $"Das Zeitlimit von {SolverText.Duration(timeLimit)} wurde erreicht, ohne dass eine Lösung gefunden wurde.", elapsed);

    public static SolveResult Cancelled(TimeSpan elapsed) =>
        new(SolveStatus.Cancelled, null, [], "Die Suche wurde abgebrochen.", elapsed);

    public static SolveResult Invalid(IReadOnlyList<string> errors, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return new(SolveStatus.Invalid, null, errors, SolverText.InvalidMessage(errors), elapsed);
    }

    public override string ToString() => $"{Status}: {Message}";
}
