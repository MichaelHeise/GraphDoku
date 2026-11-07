using System.Diagnostics;
using System.Globalization;
using Google.OrTools.Sat;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.Solver.OrTools;

/// <summary>
/// <see cref="ISolver"/> auf Basis von Google OR-Tools CP-SAT. Zustandslos und threadsicher: Jeder Aufruf baut ein
/// eigenes Modell. Abbruch über das CancellationToken ruft <c>StopSearch</c> des laufenden CP-SAT-Solvers auf.
/// </summary>
public sealed class OrToolsSolver : ISolver
{
    private readonly OrToolsModelBuilder _builder;

    public OrToolsSolver(RuleRegistry? rules = null, OrToolsAdapterRegistry? adapters = null)
    {
        _builder = new OrToolsModelBuilder(rules, adapters);
    }

    public string Name => "OR-Tools CP-SAT";

    public SolveResult Solve(Puzzle puzzle, SolverOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        options ??= SolverOptions.Default;
        var stopwatch = Stopwatch.StartNew();
        if (cancellationToken.IsCancellationRequested)
        {
            return SolveResult.Cancelled(stopwatch.Elapsed);
        }

        var build = _builder.Build(puzzle);
        if (build.Model is not { } model)
        {
            return SolveResult.Invalid(build.Errors, stopwatch.Elapsed);
        }

        var response = Run(model, Parameters(options, enumerateAll: false), callback: null, cancellationToken);
        var elapsed = stopwatch.Elapsed;
        return response.Status switch
        {
            CpSolverStatus.Optimal or CpSolverStatus.Feasible => SolveResult.Solved(ReadValues(model, response), elapsed),
            CpSolverStatus.Infeasible => SolveResult.Unsolvable(elapsed),
            CpSolverStatus.Unknown when cancellationToken.IsCancellationRequested => SolveResult.Cancelled(elapsed),
            CpSolverStatus.Unknown => SolveResult.Timeout(options.TimeLimit, elapsed),
            _ => SolveResult.Invalid([UnexpectedStatus(response)], elapsed),
        };
    }

    public SolutionCountResult CountSolutions(Puzzle puzzle, int limit = 2, SolverOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        options ??= SolverOptions.Default;
        var stopwatch = Stopwatch.StartNew();
        if (cancellationToken.IsCancellationRequested)
        {
            return SolutionCountResult.Cancelled(0, limit, [], stopwatch.Elapsed);
        }

        var build = _builder.Build(puzzle);
        if (build.Model is not { } model)
        {
            return SolutionCountResult.Invalid(limit, build.Errors, stopwatch.Elapsed);
        }

        using var callback = new CountingCallback(model, limit);
        var response = Run(model, Parameters(options, enumerateAll: true), callback, cancellationToken);
        var elapsed = stopwatch.Elapsed;
        var (count, solutions) = (callback.Count, callback.Solutions);

        if (count >= limit || response.Status is CpSolverStatus.Optimal or CpSolverStatus.Infeasible)
        {
            // Limit erreicht oder Suche vollständig (bei enumerate_all_solutions meldet CP-SAT dann Optimal bzw. Infeasible).
            return SolutionCountResult.Completed(count, limit, solutions, elapsed);
        }

        if (response.Status is CpSolverStatus.ModelInvalid)
        {
            return SolutionCountResult.Invalid(limit, [UnexpectedStatus(response)], elapsed);
        }

        return cancellationToken.IsCancellationRequested
            ? SolutionCountResult.Cancelled(count, limit, solutions, elapsed)
            : SolutionCountResult.Timeout(count, limit, solutions, elapsed);
    }

    /// <summary>
    /// Führt CP-SAT aus. Verwendet <see cref="SolveWrapper"/> direkt (statt <see cref="CpSolver"/>), damit die
    /// Lebensdauer des nativen Solvers in unserer Hand liegt: <c>StopSearch</c> wirkt auch, wenn der Abbruch kurz vor
    /// dem Start eintrifft, und kann nicht auf einen bereits freigegebenen Solver treffen (die Registrierung wird vor
    /// dem Wrapper aufgelöst).
    /// </summary>
    private static CpSolverResponse Run(OrToolsModel model, string parameters, SolutionCallback? callback, CancellationToken cancellationToken)
    {
        using var wrapper = new SolveWrapper();
        wrapper.SetStringParameters(parameters);
        if (callback is not null)
        {
            wrapper.AddSolutionCallback(callback);
        }

        using (cancellationToken.Register(wrapper.StopSearch))
        {
            return wrapper.Solve(model.Model.Model);
        }
    }

    private static string Parameters(SolverOptions options, bool enumerateAll)
    {
        var seconds = options.TimeLimit.TotalSeconds.ToString("R", CultureInfo.InvariantCulture);
        var parameters = $"max_time_in_seconds:{seconds} log_search_progress:false";

        // Aufzählen aller Lösungen verlangt in CP-SAT einen einzelnen Such-Worker.
        return enumerateAll ? parameters + " enumerate_all_solutions:true num_workers:1" : parameters;
    }

    private static Dictionary<int, int> ReadValues(OrToolsModel model, CpSolverResponse response)
    {
        var values = new Dictionary<int, int>(model.NodeVariables.Count);
        foreach (var (nodeId, variable) in model.NodeVariables)
        {
            values[nodeId] = checked((int)response.Solution[variable.GetIndex()]);
        }

        return values;
    }

    private static string UnexpectedStatus(CpSolverResponse response) =>
        $"OR-Tools meldet den Status {response.Status}{(string.IsNullOrEmpty(response.SolutionInfo) ? "" : $" ({response.SolutionInfo})")}.";

    /// <summary>Zählt Lösungen und hält die Suche beim Limit an. CP-SAT ruft ihn mit einem Worker nacheinander auf.</summary>
    private sealed class CountingCallback(OrToolsModel model, int limit) : CpSolverSolutionCallback
    {
        private readonly List<IReadOnlyDictionary<int, int>> _solutions = [];

        public int Count { get; private set; }

        public IReadOnlyList<IReadOnlyDictionary<int, int>> Solutions => _solutions;

        public override void OnSolutionCallback()
        {
            if (Count >= limit)
            {
                StopSearch();
                return;
            }

            Count++;
            if (_solutions.Count < SolutionCountResult.MaxStoredSolutions)
            {
                var values = new Dictionary<int, int>(model.NodeVariables.Count);
                foreach (var (nodeId, variable) in model.NodeVariables)
                {
                    values[nodeId] = (int)Value(variable);
                }

                _solutions.Add(values);
            }

            if (Count >= limit)
            {
                StopSearch();
            }
        }
    }
}
