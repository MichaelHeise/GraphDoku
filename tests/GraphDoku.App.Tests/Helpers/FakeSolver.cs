using GraphDoku.Core.Model;
using GraphDoku.Solver;

namespace GraphDoku.App.Tests.Helpers;

/// <summary>
/// Solver-Ersatz für ViewModel-Tests: liefert festgelegte Ergebnisse, merkt sich die Aufrufe und kann blockieren, bis
/// der Test ihn freigibt oder die Suche abgebrochen wird (für Busy- und Abbruchtests).
/// </summary>
internal sealed class FakeSolver : ISolver, IDisposable
{
    private readonly ManualResetEventSlim _started = new(false);
    private readonly ManualResetEventSlim _release = new(false);

    public string Name => "Testsolver";

    /// <summary>Ergebnis von <see cref="Solve"/> (Standard: alle Knoten ohne Given auf den kleinsten Wert).</summary>
    public Func<Puzzle, SolveResult> SolveResult { get; set; } = puzzle => Solver.SolveResult.Solved(FillAll(puzzle), TimeSpan.FromMilliseconds(12));

    /// <summary>Ergebnis von <see cref="CountSolutions"/>.</summary>
    public Func<Puzzle, int, SolutionCountResult> CountResult { get; set; } =
        (puzzle, limit) => SolutionCountResult.Completed(1, limit, [FillAll(puzzle)], TimeSpan.FromMilliseconds(12));

    /// <summary>true: Jeder Aufruf wartet auf <see cref="Release"/> oder den Abbruch.</summary>
    public bool Block { get; set; }

    /// <summary>Ausnahme, die der nächste Aufruf wirft.</summary>
    public Exception? Throw { get; set; }

    public List<SolverOptions> ReceivedOptions { get; } = [];

    public List<Puzzle> ReceivedPuzzles { get; } = [];

    /// <summary>Wurde der letzte Aufruf über das CancellationToken abgebrochen?</summary>
    public bool WasCancelled { get; private set; }

    public void Release() => _release.Set();

    /// <summary>Wartet, bis der Solver aufgerufen wurde (läuft im Thread-Pool).</summary>
    public void WaitUntilStarted() => _started.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("der Solver sollte gestartet sein");

    public SolveResult Solve(Puzzle puzzle, SolverOptions? options = null, CancellationToken cancellationToken = default)
    {
        Record(puzzle, options);
        return WaitAndCheckCancelled(cancellationToken) ? Solver.SolveResult.Cancelled(TimeSpan.FromMilliseconds(5)) : SolveResult(puzzle);
    }

    public SolutionCountResult CountSolutions(Puzzle puzzle, int limit = 2, SolverOptions? options = null, CancellationToken cancellationToken = default)
    {
        Record(puzzle, options);
        return WaitAndCheckCancelled(cancellationToken) ? SolutionCountResult.Cancelled(0, limit, [], TimeSpan.FromMilliseconds(5)) : CountResult(puzzle, limit);
    }

    public void Dispose()
    {
        _started.Dispose();
        _release.Dispose();
    }

    /// <summary>Belegung: Givens bleiben, alle anderen Knoten bekommen den kleinsten Wert ihres Bereichs + <paramref name="offset"/>.</summary>
    public static Dictionary<int, int> FillAll(Puzzle puzzle, int offset = 0) =>
        puzzle.Nodes.ToDictionary(n => n.Id, n => n.Given ?? (puzzle.GetEffectiveDomain(n).Min + offset));

    private void Record(Puzzle puzzle, SolverOptions? options)
    {
        lock (ReceivedOptions)
        {
            ReceivedOptions.Add(options ?? SolverOptions.Default);
            ReceivedPuzzles.Add(puzzle);
        }

        if (Throw is { } exception)
        {
            Throw = null;
            _started.Set();
            throw exception;
        }
    }

    private bool WaitAndCheckCancelled(CancellationToken token)
    {
        _started.Set();
        if (Block)
        {
            WaitHandle.WaitAny([_release.WaitHandle, token.WaitHandle], TimeSpan.FromSeconds(10));
        }

        WasCancelled = token.IsCancellationRequested;
        return WasCancelled;
    }
}
