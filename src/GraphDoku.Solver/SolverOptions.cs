namespace GraphDoku.Solver;

/// <summary>Einstellungen für einen Solver-Aufruf.</summary>
public sealed record SolverOptions
{
    /// <summary>Standard-Zeitlimit: 30 Sekunden.</summary>
    public static readonly TimeSpan DefaultTimeLimit = TimeSpan.FromSeconds(30);

    public static SolverOptions Default { get; } = new();

    /// <summary>Maximale Rechenzeit (Wanduhr) pro Aufruf; muss größer als 0 sein.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Der Wert ist nicht positiv.</exception>
    public TimeSpan TimeLimit
    {
        get;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Das Zeitlimit muss größer als 0 sein.");
            }

            field = value;
        }
    } = DefaultTimeLimit;
}
