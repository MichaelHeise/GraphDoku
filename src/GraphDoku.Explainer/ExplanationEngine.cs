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
/// <item>Feststehende Werte streichen: Der Wert eines Givens oder gesetzten Knotens wird bei den Nachbarn in
/// <c>AllDifferent</c>-/<c>CoverRange</c>-Gruppen und über <c>NotEqual</c>-Kanten gestrichen (dazu Werte außerhalb des
/// <c>CoverRange</c>-Bereichs). Diese Stufe steht vor den Singles, damit die Kandidatenanzeige nach jedem gesetzten
/// Wert dessen offensichtliche Folgen zeigt, bevor ein schwierigerer Schluss kommt.</item>
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
/// <para>
/// Enthält das Rätsel eigene (nicht im Katalog enthaltene) Regeln und wurde es ohne Verzweigung vollständig belegt,
/// bestätigt der Solver die Belegung, weil eine eigene Regel einen Verstoß in ihrer Propagation übersehen kann.
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
    /// Streichungen der Katalogregeln mit Rang 0, die nur an Givens und gesetzten Werten hängen, kommen schon in
    /// Stufe 0 (vor den Singles).
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

    /// <summary>
    /// true für Regeln aus dem eingebauten Katalog (Core). Nur für sie gilt: <c>Propagate</c> hängt allein von den
    /// Kandidaten der eigenen Knoten ab (Zwischenspeicher erlaubt) und erkennt jeden Verstoß einer vollständigen
    /// Belegung.
    /// </summary>
    internal static bool IsCatalogRule(IRule rule) => rule.GetType().Assembly == typeof(RuleBase).Assembly;

    /// <summary>„jede Zahl von 1 bis 9“, „jede der Zahlen 1, 3, 5“ oder „die 4“.</summary>
    internal static string EveryValue(IReadOnlyList<int> values)
    {
        if (values.Count == 1)
        {
            return $"die {Format(values[0])}";
        }

        return (long)values[^1] - values[0] == values.Count - 1
            ? $"jede Zahl von {Format(values[0])} bis {Format(values[^1])}"
            : $"jede der Zahlen {string.Join(", ", values.Select(v => Format(v)))}";
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
        private readonly (IConstraint Constraint, IRule Rule)[] _fixedValueRules;
        private readonly Group[] _singleGroups;
        private readonly Dictionary<Group, int> _domainUnionSizes = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<IConstraint, CachedPropagation> _propagationCache = new(ReferenceEqualityComparer.Instance);
        private readonly bool _hasCustomRules;
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
            _fixedValueRules = [.. _ruleOrder.Where(c => GetRuleRank(c.Rule.TypeName) == 0 && IsCatalogRule(c.Rule))];
            _hasCustomRules = _ruleOrder.Any(c => !IsCatalogRule(c.Rule));
            _singleGroups = [.. puzzle.Groups.Where(g => g.Rule is AllDifferentRule.Name or CoverRangeRule.Name)];
            foreach (var group in _singleGroups)
            {
                var domainValues = new HashSet<int>();
                foreach (var node in group.Nodes.Select(puzzle.FindNode).OfType<Node>())
                {
                    domainValues.UnionWith(puzzle.GetEffectiveDomain(node).Values);
                }

                _domainUnionSizes[group] = domainValues.Count;
            }
        }

        private CandidateState Candidates => _state.Candidates;

        public ExplanationResult Run()
        {
            _checkpoints.Add(_state.Clone());
            while (true)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    return Cancelled();
                }

                if (FindContradiction() is { } contradiction)
                {
                    return Contradicted(contradiction);
                }

                var step = TryFixedValueEliminations() ?? TryNakedSingle() ?? TryHiddenSingle();
                if (step is null)
                {
                    step = TryRules(out var ruleContradiction);
                    if (ruleContradiction is not null)
                    {
                        return Contradicted(ruleContradiction);
                    }
                }

                if (step is null)
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        return Cancelled();
                    }

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

        private ExplanationResult Cancelled() => Finish(ExplanationStatus.Cancelled, "Die Erklärung wurde abgebrochen.");

        /// <summary>Ohne Verzweigung ein Beweis der Unlösbarkeit; nach einer Verzweigung ein Fehler einer Regel.</summary>
        private ExplanationResult Contradicted(Reason contradiction)
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

        /// <summary>
        /// Stufe 0: Streichungen der Regeln mit Rang 0, deren Begründung nur an feststehenden Knoten hängt (Givens und
        /// gesetzte Werte), z. B. „Zeile 3 enthält bereits die 5 (Knoten 21)“. Ein Knoten mit nur einem Kandidaten, der
        /// noch nicht gesetzt ist, zählt nicht: Er wird erst als einziger Kandidat gesetzt, dann folgen seine Streichungen.
        /// </summary>
        private ExplanationStep? TryFixedValueEliminations()
        {
            foreach (var (constraint, rule) in _fixedValueRules)
            {
                var changes = Propagate(constraint, rule)
                    .Where(c => c.Kind == CandidateChangeKind.Eliminate
                        && IsEffective(c)
                        && c.Reason.NodeIds.All(n => n == c.NodeId || _state.IsSet(n)))
                    .ToList();
                if (changes.Count > 0)
                {
                    return EliminationStep(changes, ExplanationTechnique.FixedValueElimination);
                }
            }

            return null;
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
                            $"{premise} Die {Format(value)} ist nur noch in Knoten {place} möglich.");
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
        /// <remarks>
        /// Umfassen schon die Wertebereiche der Knoten genau so viele Werte, wie die Gruppe Knoten hat (klassisches
        /// Sudoku), genügt der kurze Satz „In … muss jede Zahl von 1 bis 9 genau einmal vorkommen.“ Sonst nennt die
        /// Begründung das Schubfachprinzip ausdrücklich.
        /// </remarks>
        private bool TryGetRequiredValues(Group group, out IReadOnlyList<int> values, out string premise)
        {
            if (group.Rule == CoverRangeRule.Name)
            {
                var range = CoverRangeRule.GetRange(group, _puzzle);
                values = [.. range.Values];
                premise = $"In {Describe(group)} muss {EveryValue(values)} genau einmal vorkommen.";
                return true;
            }

            var union = Union(group);
            values = union;
            var count = group.Nodes.Count;
            if (union.Count != count)
            {
                premise = string.Empty;
                return false;
            }

            premise = _domainUnionSizes[group] == count
                ? $"In {Describe(group)} muss {EveryValue(union)} genau einmal vorkommen."
                : $"{Describe(group)}: Alle {count} Knoten der Gruppe haben verschiedene Werte, und es kommen nur noch die Werte {FormatValues(union)} in Frage, genauso viele wie Knoten. Also muss {EveryValue(union)} genau einmal vorkommen.";
            return true;
        }

        private List<int> Union(Group group) =>
            [.. group.Nodes.SelectMany(n => Candidates.GetCandidates(n)).Distinct().Order()];

        /// <summary>
        /// Stufe 3: erster Constraint (nach Rang) mit wirksamer Änderung. Setzt eine (eigene) Regel einen Wert, der kein
        /// Kandidat mehr ist, ist das ein Widerspruch (<paramref name="contradiction"/>).
        /// </summary>
        private ExplanationStep? TryRules(out Reason? contradiction)
        {
            contradiction = null;
            foreach (var (constraint, rule) in _ruleOrder)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                var changes = new List<CandidateChange>();
                foreach (var change in Propagate(constraint, rule))
                {
                    if (change.Kind == CandidateChangeKind.Assign && !Candidates.GetCandidates(change.NodeId).Contains(change.Values[0]))
                    {
                        var value = Format(change.Values[0]);
                        contradiction = new Reason(
                            change.Reason.Constraint,
                            [.. change.Reason.NodeIds.Append(change.NodeId).Distinct()],
                            $"{change.Reason.Text} Die {value} ist für Knoten {change.NodeId} aber nicht mehr möglich.");
                        return null;
                    }

                    if (IsEffective(change))
                    {
                        changes.Add(change);
                    }
                }

                if (changes.Count == 0)
                {
                    continue;
                }

                var first = changes[0];
                return first.Kind == CandidateChangeKind.Assign
                    ? new ExplanationStep(_steps.Count, StepKind.SetValue, ExplanationTechnique.RulePropagation, [first], first.Reason)
                    : EliminationStep(changes, ExplanationTechnique.RulePropagation);
            }

            return null;
        }

        /// <summary>Alle Streichungen mit derselben Begründung wie die erste bilden einen Schritt (pro Knoten zusammengefasst).</summary>
        private ExplanationStep EliminationStep(List<CandidateChange> changes, ExplanationTechnique technique)
        {
            var first = changes.First(c => c.Kind == CandidateChangeKind.Eliminate);
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
            return new ExplanationStep(_steps.Count, StepKind.EliminateCandidates, technique, merged, reason);
        }

        /// <summary>
        /// <c>Propagate</c> mit Zwischenspeicher für Katalogregeln: Haben alle Knoten des Constraints noch dieselben
        /// (unveränderlichen) Kandidatenmengen wie beim letzten Aufruf, ist das Ergebnis dasselbe. Das spart vor allem
        /// die teure Prüfung großer Summengruppen, die sonst nach jedem Schritt wiederholt würde.
        /// </summary>
        private IReadOnlyList<CandidateChange> Propagate(IConstraint constraint, IRule rule)
        {
            if (!IsCatalogRule(rule))
            {
                return rule.Propagate(Candidates, constraint);
            }

            var nodes = constraint.NodeIds;
            if (_propagationCache.TryGetValue(constraint, out var cached) && cached.Matches(Candidates, nodes))
            {
                return cached.Changes;
            }

            var inputs = nodes.Select(n => Candidates.Contains(n) ? Candidates.GetCandidates(n) : null).ToArray();
            var changes = rule.Propagate(Candidates, constraint);
            _propagationCache[constraint] = new CachedPropagation(inputs, changes);
            return changes;
        }

        /// <summary>Ändert die Streichung etwas, bzw. setzt die Zuweisung einen noch nicht gesetzten Knoten?</summary>
        private bool IsEffective(CandidateChange change)
        {
            var candidates = Candidates.GetCandidates(change.NodeId);
            return change.Kind == CandidateChangeKind.Eliminate
                ? change.Values.Any(candidates.Contains)
                : candidates.Contains(change.Values[0]) && !_state.IsSet(change.NodeId);
        }

        private ExplanationStep? TryBranch(out ExplanationResult? stop)
        {
            stop = null;
            var result = CountSolutions();
            switch (result.Status)
            {
                case CountStatus.Cancelled when result.Count == 0:
                    stop = Cancelled();
                    return null;
                case CountStatus.Invalid:
                    stop = Finish(
                        ExplanationStatus.Invalid,
                        $"Der Solver lehnt das Rätsel ab: {result.Message}",
                        errors: result.Errors);
                    return null;
                case CountStatus.Completed when result.Count == 0:
                    stop = Finish(
                        ExplanationStatus.Unsolvable,
                        "Das Rätsel ist unlösbar (vom Solver bewiesen). Ohne Raten ließ sich bis hierhin kein Widerspruch zeigen.");
                    return null;
                case CountStatus.Timeout when result.Count == 0:
                    stop = Finish(
                        ExplanationStatus.Stuck,
                        $"Ohne Raten geht es nicht weiter, und der Solver hat keine Lösung für eine Verzweigung gefunden: {result.Message} Die Erklärung ist unvollständig.");
                    return null;
            }

            if (_cancellationToken.IsCancellationRequested)
            {
                stop = Cancelled();
                return null;
            }

            var nodeId = Candidates.NodeIds
                .Where(id => !_state.IsSet(id))
                .OrderBy(id => Candidates.GetCandidates(id).Count)
                .ThenBy(id => id)
                .First();
            var candidates = Candidates.GetCandidates(nodeId);
            if (result.Solutions.Count == 0 || !result.Solutions[0].TryGetValue(nodeId, out var value))
            {
                // Vertrag von ISolver.CountSolutions verletzt (Lösung fehlt oder ist unvollständig): klar melden statt abstürzen.
                stop = Finish(
                    ExplanationStatus.Stuck,
                    $"Ohne Raten geht es nicht weiter, und der Solver „{_engine.Solver.Name}“ hat keine vollständige Lösung mitgeliefert (für Knoten {nodeId} fehlt ein Wert), aus der sich ein Wert übernehmen ließe. Die Erklärung ist unvollständig.");
                return null;
            }

            if (!candidates.Contains(value))
            {
                throw new InvalidOperationException(
                    $"Interner Fehler: Der Lösungswert {Format(value)} für Knoten {nodeId} wurde als Kandidat gestrichen. Eine Regel schließt nicht korrekt.");
            }

            var source = result.IsAmbiguous ? "aus einer der Lösungen des Solvers (das Rätsel ist mehrdeutig)" : "aus der Lösung des Solvers";
            var reason = new Reason(
                null,
                [nodeId],
                $"Ohne Raten geht es hier nicht weiter: Knoten {nodeId} hat die wenigsten Kandidaten ({FormatValues([.. candidates])}); der Wert {Format(value)} wird {source} übernommen.");
            return SetStep(StepKind.Branch, ExplanationTechnique.Branch, nodeId, value, reason);
        }

        /// <summary>Höchstens ein Solver-Aufruf pro Erklärung (Limit 2: Lösung und Eindeutigkeit in einem).</summary>
        private SolutionCountResult CountSolutions() =>
            _solverResult ??= _engine.Solver.CountSolutions(_puzzle, 2, _engine.SolverOptions, _cancellationToken);

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
                        $"{Describe(group)}: {group.Nodes.Count} Knoten brauchen verschiedene Werte, es {(union.Count == 1 ? "kommt aber nur noch ein Wert" : $"kommen aber nur noch {union.Count} Werte")} in Frage ({FormatValues(union)}).");
                }
            }

            return null;
        }

        private ExplanationResult FinishSolved()
        {
            var count = _steps.Count;
            if (_branchCount == 0)
            {
                return _hasCustomRules
                    ? FinishSolvedWithCustomRules()
                    : Finish(
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

        /// <summary>
        /// Vollständig belegt ohne Verzweigung, aber mit eigenen Regeln: Deren <c>Propagate</c> muss einen Verstoß der
        /// vollständigen Belegung nicht erkennen. Der Solver bestätigt deshalb, dass die Belegung eine Lösung ist.
        /// Sind alle Schlüsse korrekt, kann es keine andere Lösung geben.
        /// </summary>
        private ExplanationResult FinishSolvedWithCustomRules()
        {
            var count = _steps.Count;
            var result = CountSolutions();
            var assignment = Candidates.NodeIds.ToDictionary(id => id, id => Candidates.GetValue(id)!.Value);
            if (result.Solutions.Any(s => assignment.Any(a => !s.TryGetValue(a.Key, out var v) || v != a.Value)))
            {
                throw new InvalidOperationException(
                    "Interner Fehler: Der Solver hat eine Lösung gefunden, die von der ohne Raten hergeleiteten Belegung abweicht. Eine eigene Regel schließt nicht korrekt.");
            }

            if (result.Count >= 1)
            {
                return Finish(
                    ExplanationStatus.Solved,
                    $"Das Rätsel wurde in {count} Schritten vollständig erklärt, ohne zu raten; der Solver bestätigt, dass die Belegung alle Regeln erfüllt. Damit ist die Lösung eindeutig.");
            }

            return result.Status switch
            {
                CountStatus.Completed => Finish(
                    ExplanationStatus.Unsolvable,
                    $"Das Rätsel ist unlösbar (vom Solver bewiesen). Die {count} Schritte führen ohne Raten zu einer vollständigen Belegung, die aber eine eigene Regel verletzt; deren Propagation erkennt den Verstoß nicht.",
                    keepSolution: false),
                CountStatus.Cancelled => Cancelled(),
                _ => Finish(
                    ExplanationStatus.SolvedUnverified,
                    $"Das Rätsel wurde in {count} Schritten vollständig erklärt, ohne zu raten. Ob die Belegung auch die eigenen Regeln erfüllt, ist offen, weil der Solver sie nicht prüfen konnte: {result.Message}"),
            };
        }

        private ExplanationResult Finish(
            ExplanationStatus status,
            string message,
            Reason? contradiction = null,
            IReadOnlyList<string>? errors = null,
            bool keepSolution = true)
        {
            Dictionary<int, int>? solution = null;
            if (keepSolution && contradiction is null && Candidates.NodeIds.All(_state.IsSet) && !Candidates.HasContradiction)
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

        /// <summary>Letztes <c>Propagate</c>-Ergebnis eines Constraints mit den Kandidatenmengen, aus denen es entstand.</summary>
        private sealed class CachedPropagation(CandidateSet?[] inputs, IReadOnlyList<CandidateChange> changes)
        {
            public IReadOnlyList<CandidateChange> Changes { get; } = changes;

            public bool Matches(CandidateState state, IReadOnlyList<int> nodes)
            {
                for (var i = 0; i < nodes.Count; i++)
                {
                    var current = state.Contains(nodes[i]) ? state.GetCandidates(nodes[i]) : null;
                    if (!ReferenceEquals(current, inputs[i]))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
