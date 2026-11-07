using System.Collections.Immutable;
using System.Globalization;
using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;
using GraphDoku.Core.Rules.Catalog;
using GraphDoku.Core.Validation;
using GraphDoku.Solver;

namespace GraphDoku.Explainer;

/// <summary>
/// Erklär-Modus: erzeugt einen menschlich nachvollziehbaren Lösungsweg als geordnete Liste von Schritten.
/// </summary>
/// <remarks>
/// <para>
/// Pro Durchlauf wird der einfachste anwendbare Schluss gesucht, als ein Schritt protokolliert und angewendet; danach
/// beginnt die Suche wieder beim einfachsten Schluss. Reihenfolge:
/// </para>
/// <list type="number">
/// <item>Einziger Kandidat (naked single): Ein noch nicht gesetzter Knoten hat genau einen Kandidaten.</item>
/// <item>Einziger Platz (hidden single) in einer <c>CoverRange</c>-Gruppe (jede Zahl des Bereichs muss vorkommen) oder
/// einer <c>AllDifferent</c>-Gruppe, in der genau so viele Werte möglich sind wie Knoten (Schubfachprinzip).</item>
/// <item>Kandidaten streichen über <c>IRule.Propagate</c> aller Kanten und Gruppen, sortiert nach Regelrang
/// (<see cref="GetRuleRank"/>) und dann nach Reihenfolge im Rätsel (erst Kanten, dann Gruppen).</item>
/// </list>
/// <para>
/// Ein Streichschritt fasst alle Streichungen einer Regel mit gleicher Begründung zusammen (z. B. „Zeile 3 enthält
/// bereits die 5“ streicht die 5 in allen übrigen Knoten der Zeile). Kommt die Schleife ohne Raten nicht weiter, wird
/// einmalig der Solver befragt (<see cref="ISolver.CountSolutions"/>, Limit 2) und ein Schritt „Verzweigung“ mit dem
/// Wert aus dessen erster Lösung angelegt: Knoten mit den wenigsten Kandidaten, bei Gleichstand die kleinste Id.
/// </para>
/// </remarks>
public sealed class ExplanationEngine
{
    public ExplanationEngine(ISolver solver, RuleRegistry? registry = null, SolverOptions? solverOptions = null)
    {
        ArgumentNullException.ThrowIfNull(solver);
        Solver = solver;
        Registry = registry ?? RuleRegistry.Default;
        SolverOptions = solverOptions;
    }

    /// <summary>Liefert die Lösung für Verzweigungen und klärt Eindeutigkeit bzw. Unlösbarkeit.</summary>
    public ISolver Solver { get; }

    public RuleRegistry Registry { get; }

    /// <summary>Optionen für den Solver-Aufruf (Zeitlimit); <c>null</c> = Standard.</summary>
    public SolverOptions? SolverOptions { get; }

    /// <summary>
    /// Rang einer Regel in Stufe 3 (kleiner = früher): 0 = festgelegte Werte streichen (<c>AllDifferent</c>,
    /// <c>CoverRange</c>, <c>NotEqual</c>), 1 = Kantenregeln mit Rechnung (<c>LessThan</c>, <c>Difference</c>,
    /// <c>Ratio</c>), 2 = Gruppenregeln mit Rechnung (<c>Thermometer</c>, <c>Sum</c>), 3 = sonstige registrierte Regeln.
    /// </summary>
    public static int GetRuleRank(string typeName) => typeName switch
    {
        AllDifferentRule.Name or CoverRangeRule.Name or NotEqualRule.Name => 0,
        LessThanRule.Name or DifferenceRule.Name or RatioRule.Name => 1,
        ThermometerRule.Name or SumRule.Name => 2,
        _ => 3,
    };

    /// <summary>Erklärt das Rätsel (synchron, blockierend). Das Rätsel wird nicht verändert.</summary>
    /// <param name="puzzle">Das Rätsel.</param>
    /// <param name="cancellationToken">Bricht ab; das Ergebnis hat dann den Status <see cref="ExplanationStatus.Cancelled"/>.</param>
    public ExplanationResult Explain(Puzzle puzzle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        var errors = PuzzleValidator.Validate(puzzle, Registry);
        if (errors.Count > 0)
        {
            return new ExplanationResult(
                puzzle,
                ExplanationStatus.Invalid,
                $"Das Rätsel ist ungültig und kann nicht erklärt werden: {string.Join(" ", errors)}",
                [],
                [],
                errors,
                null,
                null,
                null);
        }

        return new Session(this, puzzle, cancellationToken).Run();
    }

    /// <summary>
    /// Asynchrone Variante für die Oberfläche: läuft auf dem Thread-Pool mit einer Kopie des Rätsels, damit
    /// Bearbeitungen im Editor die laufende Erklärung nicht stören. Abbruch liefert den Status „abgebrochen“.
    /// </summary>
    public Task<ExplanationResult> ExplainAsync(Puzzle puzzle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        var snapshot = puzzle.Clone();
        return Task.Run(() => Explain(snapshot, cancellationToken), CancellationToken.None);
    }

    /// <summary>Lesbarer Name eines Constraints, z. B. „Gruppe 3 „Zeile 3““ oder „Kante 7“.</summary>
    internal static string Describe(IConstraint constraint) =>
        constraint is Group { Label: { Length: > 0 } label } ? $"{constraint.Ref} „{label}“" : constraint.Ref.ToString();

    internal static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Werteliste; zusammenhängende Folgen ab vier Werten als „1 bis 9“.</summary>
    internal static string FormatValues(IReadOnlyList<int> values)
    {
        if (values.Count >= 4 && (long)values[^1] - values[0] == values.Count - 1)
        {
            return $"{Format(values[0])} bis {Format(values[^1])}";
        }

        return string.Join(", ", values.Select(v => Format(v)));
    }

    /// <summary>Zustand einer laufenden Erklärung.</summary>
    private sealed class Session
    {
        private readonly ExplanationEngine _engine;
        private readonly Puzzle _puzzle;
        private readonly CancellationToken _cancellationToken;
        private readonly WorkingState _state;
        private readonly List<ExplanationStep> _steps = [];
        private readonly List<WorkingState> _checkpoints = [];
        private readonly (IConstraint Constraint, IRule Rule)[] _ruleOrder;
        private readonly Group[] _singleGroups;
        private SolutionCountResult? _solverResult;
        private int _branchCount;

        public Session(ExplanationEngine engine, Puzzle puzzle, CancellationToken cancellationToken)
        {
            _engine = engine;
            _puzzle = puzzle;
            _cancellationToken = cancellationToken;
            var candidates = CandidateState.FromPuzzle(puzzle);
            var givens = puzzle.Nodes.Where(n => n.Given is not null).Select(n => n.Id).ToImmutableHashSet();
            _state = new WorkingState(candidates, givens);
            _ruleOrder =
            [
                .. puzzle.Constraints
                    .Select(c => (Constraint: c, Rule: engine.Registry.Get(c.Rule)))
                    .OrderBy(c => GetRuleRank(c.Rule.TypeName)),
            ];
            _singleGroups = [.. puzzle.Groups.Where(g => g.Rule is AllDifferentRule.Name or CoverRangeRule.Name)];
        }

        private CandidateState Candidates => _state.Candidates;

        public ExplanationResult Run()
        {
            _checkpoints.Add(_state.Clone());
            while (true)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    return Finish(ExplanationStatus.Cancelled, "Die Erklärung wurde abgebrochen.");
                }

                if (FindContradiction() is { } contradiction)
                {
                    if (_branchCount > 0)
                    {
                        throw new InvalidOperationException(
                            $"Interner Fehler: Widerspruch nach einer Verzweigung mit Werten aus der Solver-Lösung ({contradiction.Text}). Eine Regel schließt nicht korrekt.");
                    }

                    return Finish(
                        ExplanationStatus.Unsolvable,
                        $"Das Rätsel ist unlösbar. Widerspruch: {contradiction.Text}",
                        contradiction);
                }

                var step = TryNakedSingle() ?? TryHiddenSingle() ?? TryRules();
                if (step is null)
                {
                    if (Candidates.NodeIds.All(_state.IsSet))
                    {
                        return FinishSolved();
                    }

                    step = TryBranch(out var stop);
                    if (step is null)
                    {
                        return stop!;
                    }
                }

                AddStep(step);
            }
        }

        private void AddStep(ExplanationStep step)
        {
            _state.Apply(step);
            _steps.Add(step);
            if (step.Kind == StepKind.Branch)
            {
                _branchCount++;
            }

            if (_steps.Count % ExplanationResult.CheckpointInterval == 0)
            {
                _checkpoints.Add(_state.Clone());
            }
        }

        private ExplanationStep? TryNakedSingle()
        {
            foreach (var nodeId in Candidates.NodeIds)
            {
                if (!_state.IsSet(nodeId) && Candidates.GetValue(nodeId) is int value)
                {
                    var reason = new Reason(null, [nodeId], $"Knoten {nodeId} hat nur noch den Kandidaten {Format(value)}.");
                    return SetStep(StepKind.SetValue, ExplanationTechnique.NakedSingle, nodeId, value, reason);
                }
            }

            return null;
        }

        private ExplanationStep? TryHiddenSingle()
        {
            foreach (var group in _singleGroups)
            {
                if (!TryGetRequiredValues(group, out var values, out var premise))
                {
                    continue;
                }

                foreach (var value in values)
                {
                    var alreadySet = false;
                    var places = 0;
                    var place = 0;
                    foreach (var nodeId in group.Nodes)
                    {
                        if (_state.IsSet(nodeId))
                        {
                            alreadySet |= Candidates.GetValue(nodeId) == value;
                        }
                        else if (Candidates.GetCandidates(nodeId).Contains(value))
                        {
                            places++;
                            place = nodeId;
                        }
                    }

                    if (!alreadySet && places == 1)
                    {
                        var reason = new Reason(
                            group.Ref,
                            [place, .. group.Nodes.Where(n => n != place)],
                            $"{premise} Die {Format(value)} passt dort nur noch in Knoten {place}.");
                        return SetStep(StepKind.SetValue, ExplanationTechnique.HiddenSingle, place, value, reason);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Werte, die in der Gruppe sicher vorkommen müssen: bei <c>CoverRange</c> der ganze Bereich, bei
        /// <c>AllDifferent</c> die Vereinigung der Kandidaten, falls sie genau so viele Werte wie Knoten hat.
        /// </summary>
        private bool TryGetRequiredValues(Group group, out IReadOnlyList<int> values, out string premise)
        {
            if (group.Rule == CoverRangeRule.Name)
            {
                var range = CoverRangeRule.GetRange(group, _puzzle);
                values = [.. range.Values];
                premise = $"{Describe(group)} enthält jede Zahl von {Format(range.Min)} bis {Format(range.Max)} genau einmal.";
                return true;
            }

            var union = Union(group);
            values = union;
            if (union.Count != group.Nodes.Count)
            {
                premise = string.Empty;
                return false;
            }

            premise = $"{Describe(group)}: Die {group.Nodes.Count} Knoten haben verschiedene Werte, und es kommen nur die {union.Count} Werte {FormatValues(union)} in Frage, also kommt jeder davon genau einmal vor.";
            return true;
        }

        private List<int> Union(Group group) =>
            [.. group.Nodes.SelectMany(n => Candidates.GetCandidates(n)).Distinct().Order()];

        private ExplanationStep? TryRules()
        {
            foreach (var (constraint, rule) in _ruleOrder)
            {
                var changes = rule.Propagate(Candidates, constraint).Where(IsEffective).ToList();
                if (changes.Count == 0)
                {
                    continue;
                }

                var first = changes[0];
                if (first.Kind == CandidateChangeKind.Assign)
                {
                    return new ExplanationStep(_steps.Count, StepKind.SetValue, ExplanationTechnique.RulePropagation, [first], first.Reason);
                }

                // Alle Streichungen mit derselben Begründung bilden einen Schritt (pro Knoten zusammengefasst).
                var sameReason = changes
                    .Where(c => c.Kind == CandidateChangeKind.Eliminate
                        && c.Reason.Constraint == first.Reason.Constraint
                        && c.Reason.Text == first.Reason.Text)
                    .ToList();
                var merged = sameReason
                    .GroupBy(c => c.NodeId)
                    .Select(g => CandidateChange.Eliminate(
                        g.Key,
                        g.SelectMany(c => c.Values).Distinct().Where(Candidates.GetCandidates(g.Key).Contains),
                        g.First().Reason))
                    .ToList();
                var involved = sameReason.SelectMany(c => c.Reason.NodeIds).Distinct().ToList();
                var reason = new Reason(first.Reason.Constraint, involved, first.Reason.Text);
                return new ExplanationStep(_steps.Count, StepKind.EliminateCandidates, ExplanationTechnique.RulePropagation, merged, reason);
            }

            return null;
        }

        /// <summary>Ändert die Änderung etwas? Streichungen ohne vorhandenen Kandidaten und Setzen gesetzter Knoten nicht.</summary>
        private bool IsEffective(CandidateChange change)
        {
            var candidates = Candidates.GetCandidates(change.NodeId);
            if (change.Kind == CandidateChangeKind.Eliminate)
            {
                return change.Values.Any(candidates.Contains);
            }

            var value = change.Values[0];
            if (!candidates.Contains(value))
            {
                throw new InvalidOperationException(
                    $"Die Regel an {change.Reason.Constraint} setzt Knoten {change.NodeId} auf {Format(value)}, das ist aber kein Kandidat mehr.");
            }

            return !_state.IsSet(change.NodeId);
        }

        private ExplanationStep? TryBranch(out ExplanationResult? stop)
        {
            stop = null;
            _solverResult ??= _engine.Solver.CountSolutions(_puzzle, 2, _engine.SolverOptions, _cancellationToken);
            switch (_solverResult.Status)
            {
                case CountStatus.Cancelled when _solverResult.Count == 0:
                    stop = Finish(ExplanationStatus.Cancelled, "Die Erklärung wurde abgebrochen.");
                    return null;
                case CountStatus.Invalid:
                    stop = Finish(
                        ExplanationStatus.Invalid,
                        $"Der Solver lehnt das Rätsel ab: {_solverResult.Message}",
                        errors: _solverResult.Errors);
                    return null;
                case CountStatus.Completed when _solverResult.Count == 0:
                    stop = Finish(
                        ExplanationStatus.Unsolvable,
                        "Das Rätsel ist unlösbar (vom Solver bewiesen). Ohne Raten ließ sich bis hierhin kein Widerspruch zeigen.");
                    return null;
                case CountStatus.Timeout when _solverResult.Count == 0:
                    stop = Finish(
                        ExplanationStatus.Stuck,
                        $"Ohne Raten geht es nicht weiter, und der Solver hat keine Lösung für eine Verzweigung gefunden: {_solverResult.Message} Die Erklärung ist unvollständig.");
                    return null;
            }

            if (_cancellationToken.IsCancellationRequested)
            {
                stop = Finish(ExplanationStatus.Cancelled, "Die Erklärung wurde abgebrochen.");
                return null;
            }

            var solution = _solverResult.Solutions[0];
            var nodeId = Candidates.NodeIds
                .Where(id => !_state.IsSet(id))
                .OrderBy(id => Candidates.GetCandidates(id).Count)
                .ThenBy(id => id)
                .First();
            var candidates = Candidates.GetCandidates(nodeId);
            var value = solution[nodeId];
            if (!candidates.Contains(value))
            {
                throw new InvalidOperationException(
                    $"Interner Fehler: Der Lösungswert {Format(value)} für Knoten {nodeId} wurde als Kandidat gestrichen. Eine Regel schließt nicht korrekt.");
            }

            var source = _solverResult.IsAmbiguous ? "aus einer der Lösungen des Solvers (das Rätsel ist mehrdeutig)" : "aus der Lösung des Solvers";
            var reason = new Reason(
                null,
                [nodeId],
                $"Ohne Raten geht es hier nicht weiter. Verzweigung bei Knoten {nodeId}, er hat die wenigsten Kandidaten ({FormatValues([.. candidates])}); der Wert {Format(value)} wird {source} übernommen.");
            return SetStep(StepKind.Branch, ExplanationTechnique.Branch, nodeId, value, reason);
        }

        private ExplanationStep SetStep(StepKind kind, ExplanationTechnique technique, int nodeId, int value, Reason reason) =>
            new(_steps.Count, kind, technique, [CandidateChange.Assign(nodeId, value, reason)], reason);

        /// <summary>Leerer Knoten oder Gruppe, die eine Zahl nirgends mehr unterbringen kann.</summary>
        private Reason? FindContradiction()
        {
            foreach (var nodeId in Candidates.NodeIds)
            {
                if (Candidates.GetCandidates(nodeId).IsEmpty)
                {
                    var last = _steps.LastOrDefault(s => s.NodeIds.Contains(nodeId));
                    var cause = last is null ? string.Empty : $" (zuletzt gestrichen in Schritt {last.Index + 1})";
                    return new Reason(last?.Constraint, last?.InvolvedNodeIds ?? [nodeId], $"Knoten {nodeId} hat keinen Kandidaten mehr{cause}.");
                }
            }

            foreach (var group in _singleGroups)
            {
                if (group.Rule == CoverRangeRule.Name)
                {
                    var range = CoverRangeRule.GetRange(group, _puzzle);
                    foreach (var value in range.Values)
                    {
                        if (!group.Nodes.Any(n => Candidates.GetCandidates(n).Contains(value)))
                        {
                            return new Reason(
                                group.Ref,
                                [.. group.Nodes],
                                $"{Describe(group)} muss die {Format(value)} enthalten, aber kein Knoten der Gruppe hat sie noch als Kandidaten.");
                        }
                    }

                    continue;
                }

                var union = Union(group);
                if (union.Count < group.Nodes.Count)
                {
                    return new Reason(
                        group.Ref,
                        [.. group.Nodes],
                        $"{Describe(group)}: {group.Nodes.Count} Knoten brauchen verschiedene Werte, es kommen aber nur noch {union.Count} Werte in Frage ({FormatValues(union)}).");
                }
            }

            return null;
        }

        private ExplanationResult FinishSolved()
        {
            var count = _steps.Count;
            if (_branchCount == 0)
            {
                return Finish(
                    ExplanationStatus.Solved,
                    $"Das Rätsel wurde in {count} Schritten vollständig erklärt, ohne zu raten. Damit ist die Lösung eindeutig.");
            }

            var branches = _branchCount == 1 ? "eine Verzweigung" : $"{_branchCount} Verzweigungen";
            var needed = _branchCount == 1 ? "war eine Verzweigung nötig" : $"waren {_branchCount} Verzweigungen nötig";
            var result = _solverResult!;
            if (result.IsAmbiguous)
            {
                return Finish(
                    ExplanationStatus.SolvedAmbiguous,
                    $"Das Rätsel ist mehrdeutig. Die Erklärung ({count} Schritte, {branches}) folgt einer der Lösungen des Solvers; die Werte der Verzweigungen stammen aus dieser Lösung.");
            }

            return result.IsUnique
                ? Finish(
                    ExplanationStatus.Solved,
                    $"Das Rätsel wurde in {count} Schritten vollständig erklärt; dabei {needed} (Werte aus der eindeutigen Lösung des Solvers).")
                : Finish(
                    ExplanationStatus.SolvedUnverified,
                    $"Das Rätsel wurde in {count} Schritten vollständig erklärt; dabei {needed}. Ob die Lösung eindeutig ist, ist offen: {result.Message}");
        }

        private ExplanationResult Finish(
            ExplanationStatus status,
            string message,
            Reason? contradiction = null,
            IReadOnlyList<string>? errors = null)
        {
            Dictionary<int, int>? solution = null;
            if (contradiction is null && Candidates.NodeIds.All(_state.IsSet) && !Candidates.HasContradiction)
            {
                solution = Candidates.NodeIds.ToDictionary(id => id, id => Candidates.GetValue(id)!.Value);
            }

            return new ExplanationResult(
                _puzzle,
                status,
                message,
                [.. _steps],
                _checkpoints,
                errors ?? [],
                contradiction,
                _solverResult is { Solutions.Count: > 0 } r ? r.Solutions[0] : null,
                solution);
        }
    }
}
