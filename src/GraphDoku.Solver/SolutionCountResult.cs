namespace GraphDoku.Solver;

/// <summary>Wie die Lösungszählung geendet hat.</summary>
public enum CountStatus
{
    /// <summary>Die Suche ist fertig oder das Limit wurde erreicht; <see cref="SolutionCountResult.Count"/> ist verlässlich.</summary>
    Completed,

    /// <summary>Das Zeitlimit wurde vorher erreicht; <see cref="SolutionCountResult.Count"/> ist eine Untergrenze.</summary>
    Timeout,

    /// <summary>Abgebrochen; <see cref="SolutionCountResult.Count"/> ist eine Untergrenze.</summary>
    Cancelled,

    /// <summary>Das Rätsel ist ungültig (<see cref="SolutionCountResult.Errors"/>).</summary>
    Invalid,
}

/// <summary>Ergebnis von <see cref="ISolver.CountSolutions"/>.</summary>
public sealed class SolutionCountResult
{
    /// <summary>So viele gefundene Lösungen werden höchstens mitgeliefert (z. B. um zwei Lösungen zu vergleichen).</summary>
    public const int MaxStoredSolutions = 2;

    private SolutionCountResult(
        CountStatus status,
        int count,
        int limit,
        IReadOnlyList<IReadOnlyDictionary<int, int>> solutions,
        IReadOnlyList<string> errors,
        TimeSpan elapsed)
    {
        Status = status;
        Count = count;
        Limit = limit;
        Solutions = solutions;
        Errors = errors;
        Elapsed = elapsed;
        Message = BuildMessage();
    }

    public CountStatus Status { get; }

    /// <summary>Anzahl gefundener, verschiedener Lösungen (höchstens <see cref="Limit"/>).</summary>
    public int Count { get; }

    public int Limit { get; }

    /// <summary>Die ersten gefundenen Lösungen (höchstens <see cref="MaxStoredSolutions"/>), jeweils Wert pro Knoten-Id.</summary>
    public IReadOnlyList<IReadOnlyDictionary<int, int>> Solutions { get; }

    public IReadOnlyList<string> Errors { get; }

    public string Message { get; }

    public TimeSpan Elapsed { get; }

    /// <summary>Das Limit wurde erreicht; es kann weitere Lösungen geben.</summary>
    public bool LimitReached => Count >= Limit;

    /// <summary>Genau eine Lösung (vollständig geprüft).</summary>
    public bool IsUnique => Status == CountStatus.Completed && Count == 1 && !LimitReached;

    /// <summary>Keine Lösung (vollständig geprüft).</summary>
    public bool IsUnsolvable => Status == CountStatus.Completed && Count == 0;

    /// <summary>Mindestens zwei verschiedene Lösungen gefunden (gilt auch nach Zeitlimit oder Abbruch).</summary>
    public bool IsAmbiguous => Count >= 2;

    public static SolutionCountResult Completed(int count, int limit, IReadOnlyList<IReadOnlyDictionary<int, int>> solutions, TimeSpan elapsed) =>
        new(CountStatus.Completed, count, limit, solutions, [], elapsed);

    public static SolutionCountResult Timeout(int count, int limit, IReadOnlyList<IReadOnlyDictionary<int, int>> solutions, TimeSpan elapsed) =>
        new(CountStatus.Timeout, count, limit, solutions, [], elapsed);

    public static SolutionCountResult Cancelled(int count, int limit, IReadOnlyList<IReadOnlyDictionary<int, int>> solutions, TimeSpan elapsed) =>
        new(CountStatus.Cancelled, count, limit, solutions, [], elapsed);

    public static SolutionCountResult Invalid(int limit, IReadOnlyList<string> errors, TimeSpan elapsed) =>
        new(CountStatus.Invalid, 0, limit, [], errors, elapsed);

    public override string ToString() => $"{Status} ({Count}): {Message}";

    private string BuildMessage()
    {
        var found = Count == 1 ? "1 Lösung" : $"{Count} Lösungen";
        return Status switch
        {
            CountStatus.Invalid => SolverText.InvalidMessage(Errors),
            CountStatus.Timeout => IsAmbiguous
                ? $"Das Rätsel ist mehrdeutig: {found} gefunden, bevor das Zeitlimit erreicht wurde."
                : $"Das Zeitlimit wurde erreicht; bis dahin wurden {found} gefunden. Die Eindeutigkeit ist nicht geklärt.",
            CountStatus.Cancelled => IsAmbiguous
                ? $"Das Rätsel ist mehrdeutig: {found} gefunden, bevor die Suche abgebrochen wurde."
                : $"Die Suche wurde abgebrochen; bis dahin wurden {found} gefunden.",
            _ when Count == 0 => "Das Rätsel ist unlösbar: Es gibt keine Belegung, die alle Regeln erfüllt.",
            _ when LimitReached && Count >= 2 => $"Das Rätsel ist mehrdeutig: Es gibt mindestens {found}.",
            _ when LimitReached => $"Es gibt mindestens {found} (Limit {Limit} erreicht).",
            _ when Count == 1 => "Das Rätsel ist eindeutig lösbar.",
            _ => $"Das Rätsel ist mehrdeutig: Es gibt genau {found}.",
        };
    }
}
