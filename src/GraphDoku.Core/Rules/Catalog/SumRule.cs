using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Rules.Catalog;

/// <summary><c>Sum</c>: Die Summe der Gruppe ist <c>target</c>; ohne <c>allowRepeats</c> sind die Werte verschieden.</summary>
public sealed class SumRule : RuleBase
{
    public const string Name = "Sum";

    public override string TypeName => Name;

    public override string DisplayName => "Summe";

    public override RuleKind Kind => RuleKind.Group;

    public override int MinNodes => 1;

    public override IReadOnlyList<RuleParameter> Parameters { get; } =
    [
        new("target", RuleParameterType.Integer, "Zielsumme", IsRequired: true, SuggestedValue: 10),
        new("allowRepeats", RuleParameterType.Boolean, "dürfen sich Werte wiederholen? Standard: false", DefaultValue: false),
    ];

    /// <summary>
    /// Höchstzahl der Suchschritte pro <see cref="RuleBase.Propagate"/>-Aufruf für die vollständige Prüfung.
    /// Ist sie erschöpft, bleiben die übrigen Kandidaten stehen (nur sichere Schlüsse).
    /// </summary>
    public const int SearchBudget = 200_000;

    protected override IReadOnlyList<CandidateChange> PropagateCore(CandidateState state, IConstraint constraint)
    {
        var target = constraint.Params.GetIntOrNull("target")
            ?? throw new RuleParamsException("Pflichtparameter „target“ fehlt.");
        var allowRepeats = constraint.Params.GetBool("allowRepeats", false);
        var nodes = constraint.NodeIds;
        var sets = nodes.Select(state.GetCandidates).ToArray();
        var changes = new List<CandidateChange>();

        // 1. Schranken: Die übrigen Knoten liefern zusammen mindestens die Summe ihrer Minima und höchstens die
        //    Summe ihrer Maxima.
        long totalMin = sets.Sum(s => (long)s.MinValue);
        long totalMax = sets.Sum(s => (long)s.MaxValue);
        var lowest = new long[nodes.Count];
        var highest = new long[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            var othersMin = totalMin - sets[i].MinValue;
            var othersMax = totalMax - sets[i].MaxValue;
            lowest[i] = target - othersMax;
            highest[i] = target - othersMin;
            var impossible = sets[i].Where(v => v < lowest[i] || v > highest[i]).ToList();
            if (impossible.Count > 0)
            {
                var others = nodes.Count == 1
                    ? "der Knoten allein muss die Summe ergeben"
                    : othersMin == othersMax
                        ? $"die übrigen Knoten ergeben zusammen genau {othersMin}"
                        : $"die übrigen Knoten ergeben zusammen {othersMin} bis {othersMax}";
                var reason = MakeReason(
                    constraint,
                    nodes,
                    $"{DisplayName} {target} in {Describe(constraint)}: Knoten {nodes[i]} kann nicht {FormatValues(impossible)} sein, denn {others}.");
                changes.Add(CandidateChange.Eliminate(nodes[i], impossible, reason));
            }
        }

        // 2. Festgelegte Werte dürfen sich ohne allowRepeats nicht wiederholen.
        if (!allowRepeats)
        {
            EliminateFixedValues(state, constraint, changes, "In der Summe wiederholen sich keine Werte.");
        }

        // 3. Vollständige Prüfung: Jeder Kandidat braucht eine Belegung der übrigen Knoten, die die Summe erreicht.
        if (nodes.Count > 1)
        {
            new SupportSearch(sets, target, allowRepeats).Run((index, impossible) =>
            {
                var repeats = allowRepeats ? string.Empty : " (ohne Wiederholung)";
                var reason = MakeReason(
                    constraint,
                    nodes,
                    $"{DisplayName} {target} in {Describe(constraint)}: Knoten {nodes[index]} kann nicht {FormatValues(impossible)} sein, denn mit den Kandidaten der übrigen Knoten{repeats} lässt sich die Summe dann nicht erreichen.");
                changes.Add(CandidateChange.Eliminate(nodes[index], impossible, reason));
            }, lowest, highest);
        }

        return changes;
    }

    /// <summary>Tiefensuche nach einer Belegung der übrigen Knoten pro Kandidat, mit Schrankenabschneiden und Budget.</summary>
    private sealed class SupportSearch(CandidateSet[] sets, long target, bool allowRepeats)
    {
        private readonly HashSet<(int Index, int Value)> _supported = [];
        private readonly HashSet<int> _used = [];
        private readonly int[] _assignment = new int[sets.Length];
        private readonly long[] _remaining = new long[sets.Length + 1];
        private readonly CandidateSet.Enumerator[] _candidates = new CandidateSet.Enumerator[sets.Length];
        private int _budget = SearchBudget;
        private int[] _order = [];
        private long[] _suffixMin = [];
        private long[] _suffixMax = [];

        /// <summary>Meldet pro Knoten-Index die unmöglichen Kandidaten (ohne die schon über Schranken gestrichenen).</summary>
        public void Run(Action<int, List<int>> report, long[] lowest, long[] highest)
        {
            for (var i = 0; i < sets.Length; i++)
            {
                var impossible = new List<int>();
                foreach (var value in sets[i])
                {
                    if (value < lowest[i] || value > highest[i] || _supported.Contains((i, value)))
                    {
                        continue;
                    }

                    var found = HasSupport(i, value);
                    if (found is null)
                    {
                        return; // Budget erschöpft: keine weiteren (unsicheren) Schlüsse.
                    }

                    if (found == false)
                    {
                        impossible.Add(value);
                    }
                }

                if (impossible.Count > 0)
                {
                    report(i, impossible);
                }
            }
        }

        private bool? HasSupport(int index, int value)
        {
            _order = [.. Enumerable.Range(0, sets.Length).Where(j => j != index).OrderBy(j => sets[j].Count)];
            _suffixMin = new long[_order.Length + 1];
            _suffixMax = new long[_order.Length + 1];
            for (var d = _order.Length - 1; d >= 0; d--)
            {
                _suffixMin[d] = _suffixMin[d + 1] + sets[_order[d]].MinValue;
                _suffixMax[d] = _suffixMax[d + 1] + sets[_order[d]].MaxValue;
            }

            _used.Clear();
            if (!allowRepeats)
            {
                _used.Add(value);
            }

            _assignment[index] = value;
            var result = Search(target - value);
            if (result == true)
            {
                for (var j = 0; j < sets.Length; j++)
                {
                    _supported.Add((j, _assignment[j]));
                }
            }

            return result;
        }

        /// <summary>
        /// Tiefensuche über <see cref="_order"/> ab Tiefe 0. Iterativ mit eigenem Stapel (Rest und Kandidaten-Aufzählung
        /// pro Tiefe), damit auch sehr große Gruppen keinen StackOverflow auslösen. Jedes Betreten einer Tiefe kostet
        /// einen Schritt des Budgets.
        /// </summary>
        private bool? Search(long initialRemaining)
        {
            var depth = 0;
            _remaining[0] = initialRemaining;
            var enter = true;
            while (true)
            {
                var remaining = _remaining[depth];
                var descended = false;
                if (enter)
                {
                    if (--_budget < 0)
                    {
                        return null;
                    }

                    if (depth == _order.Length)
                    {
                        if (remaining == 0)
                        {
                            return true;
                        }
                    }
                    else if (remaining >= _suffixMin[depth] && remaining <= _suffixMax[depth])
                    {
                        _candidates[depth] = sets[_order[depth]].GetEnumerator();
                        descended = TryDescend(ref depth, remaining);
                    }
                }
                else
                {
                    descended = TryDescend(ref depth, remaining);
                }

                if (descended)
                {
                    enter = true;
                    continue;
                }

                // Diese Tiefe hat keine Belegung: zurück zur vorigen und dort den nächsten Kandidaten versuchen.
                if (depth == 0)
                {
                    return false;
                }

                depth--;
                if (!allowRepeats)
                {
                    _used.Remove(_assignment[_order[depth]]);
                }

                enter = false;
            }
        }

        /// <summary>Belegt den Knoten der Tiefe mit dem nächsten passenden Kandidaten und geht eine Tiefe weiter.</summary>
        private bool TryDescend(ref int depth, long remaining)
        {
            var node = _order[depth];
            while (_candidates[depth].MoveNext())
            {
                var candidate = _candidates[depth].Current;
                if (candidate > remaining - _suffixMin[depth + 1])
                {
                    return false; // aufsteigend: größere Werte sind erst recht zu groß
                }

                if (candidate < remaining - _suffixMax[depth + 1] || (!allowRepeats && _used.Contains(candidate)))
                {
                    continue;
                }

                _assignment[node] = candidate;
                if (!allowRepeats)
                {
                    _used.Add(candidate);
                }

                _remaining[depth + 1] = remaining - candidate;
                depth++;
                return true;
            }

            return false;
        }
    }
}
