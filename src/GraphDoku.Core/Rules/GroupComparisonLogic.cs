using System.Globalization;
using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Rules;

/// <summary>Ein Summand der Differenz „Summe links − Summe rechts“: Knoten und Vorfaktor (Anzahl links − Anzahl rechts).</summary>
public readonly record struct GroupComparisonTerm(int NodeId, int Coefficient);

/// <summary>
/// Ein Gruppenvergleich mit aufgelösten Gruppen, wie ihn <see cref="GroupComparisonLogic.Resolve"/> liefert. Der
/// Schnappschuss hängt an den Knotenlisten der Gruppen zum Zeitpunkt des Auflösens.
/// </summary>
public sealed class ResolvedGroupComparison
{
    internal ResolvedGroupComparison(
        GroupComparison comparison,
        Group left,
        Group right,
        IReadOnlyList<GroupComparisonTerm> terms,
        IReadOnlyList<int> nodeIds,
        IReadOnlyList<int> sharedNodeIds)
    {
        Comparison = comparison;
        Left = left;
        Right = right;
        Terms = terms;
        NodeIds = nodeIds;
        SharedNodeIds = sharedNodeIds;
    }

    public GroupComparison Comparison { get; }

    public Group Left { get; }

    public Group Right { get; }

    /// <summary>
    /// Summanden mit Vorfaktor ≠ 0: erst die Knoten der linken Gruppe in deren Reihenfolge, dann die nur rechts
    /// enthaltenen in der Reihenfolge der rechten Gruppe. Der Vergleich gilt genau dann, wenn Σ Vorfaktor · Wert OP 0.
    /// </summary>
    public IReadOnlyList<GroupComparisonTerm> Terms { get; }

    /// <summary>Alle Knoten beider Gruppen ohne Doppelte (erst links, dann die nur rechts enthaltenen).</summary>
    public IReadOnlyList<int> NodeIds { get; }

    /// <summary>Knoten, die auf beiden Seiten gleich oft vorkommen und sich deshalb aufheben (Vorfaktor 0).</summary>
    public IReadOnlyList<int> SharedNodeIds { get; }
}

/// <summary>
/// Logik der Gruppenvergleiche (<c>Summe(Gruppe links) OP Summe(Gruppe rechts)</c>): Auflösen der Gruppen, sichere
/// Schrankenpropagation mit deutschen Begründungen und Prüfung einer vollständigen Belegung.
/// </summary>
/// <remarks>
/// Die Propagation ist auf vollständigen Belegungen vollständig: Ist jeder beteiligte Knoten festgelegt und der Vergleich
/// verletzt, wird mindestens ein Wert gestrichen. Der Erklär-Modus verlässt sich darauf.
/// </remarks>
public static class GroupComparisonLogic
{
    private const string SharedNote = " (gemeinsame Knoten zählen auf beiden Seiten gleich und bleiben außen vor)";

    /// <summary>
    /// Löst die beiden Gruppen des Vergleichs auf. Liefert <c>null</c>, wenn eine der Gruppen fehlt. Bei doppelten
    /// Gruppen-Ids gilt wie bei <see cref="Puzzle.FindGroup"/> die erste.
    /// </summary>
    public static ResolvedGroupComparison? Resolve(Puzzle puzzle, GroupComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(comparison);
        if (puzzle.FindGroup(comparison.Left) is not { } left || puzzle.FindGroup(comparison.Right) is not { } right)
        {
            return null;
        }

        var coefficients = new Dictionary<int, int>();
        var order = new List<int>();
        foreach (var (nodes, sign) in new[] { (left.Nodes, 1), (right.Nodes, -1) })
        {
            foreach (var nodeId in nodes)
            {
                if (coefficients.TryGetValue(nodeId, out var coefficient))
                {
                    coefficients[nodeId] = coefficient + sign;
                }
                else
                {
                    coefficients[nodeId] = sign;
                    order.Add(nodeId);
                }
            }
        }

        var terms = order.Where(id => coefficients[id] != 0).Select(id => new GroupComparisonTerm(id, coefficients[id])).ToList();
        var shared = order.Where(id => coefficients[id] == 0).ToList();
        return new ResolvedGroupComparison(comparison, left, right, terms, order, shared);
    }

    /// <summary>
    /// Lesbarer Name, z. B. „Gruppenvergleich 1: Summe von Gruppe 2 „A“ ≤ Summe von Gruppe 5“. Fehlende Gruppen
    /// erscheinen nur mit ihrer Id.
    /// </summary>
    public static string Describe(Puzzle puzzle, GroupComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(comparison);
        return Describe(comparison, puzzle.FindGroup(comparison.Left), puzzle.FindGroup(comparison.Right));
    }

    /// <inheritdoc cref="Describe(Puzzle, GroupComparison)"/>
    public static string Describe(ResolvedGroupComparison resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        return Describe(resolved.Comparison, resolved.Left, resolved.Right);
    }

    /// <summary>
    /// Streicht Kandidaten, die den Vergleich bei keiner Belegung der übrigen Knoten erfüllen können (Schranken über
    /// Σ Vorfaktor · Wert). Der Zustand wird nicht verändert. Ohne Summanden, bei fehlenden Knoten oder wenn ein Knoten
    /// keinen Kandidaten mehr hat, wird nichts abgeleitet.
    /// </summary>
    public static IReadOnlyList<CandidateChange> Propagate(CandidateState state, ResolvedGroupComparison resolved)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(resolved);
        foreach (var nodeId in resolved.NodeIds)
        {
            if (!state.Contains(nodeId) || state.GetCandidates(nodeId).Count == 0)
            {
                return [];
            }
        }

        var op = resolved.Comparison.Operator;
        var terms = resolved.Terms;
        if (terms.Count == 0 || !Enum.IsDefined(op))
        {
            return [];
        }

        // Beitrag jedes Summanden zu D = Σ c·x als Bereich [lowest, highest].
        var sets = terms.Select(t => state.GetCandidates(t.NodeId)).ToArray();
        var lowest = new long[terms.Count];
        var highest = new long[terms.Count];
        long leftMin = 0, leftMax = 0, rightMin = 0, rightMax = 0;
        for (var i = 0; i < terms.Count; i++)
        {
            long c = terms[i].Coefficient;
            lowest[i] = c > 0 ? c * sets[i].MinValue : c * sets[i].MaxValue;
            highest[i] = c > 0 ? c * sets[i].MaxValue : c * sets[i].MinValue;
            if (c > 0)
            {
                leftMin += lowest[i];
                leftMax += highest[i];
            }
            else
            {
                rightMin -= highest[i];
                rightMax -= lowest[i];
            }
        }

        var totalMin = leftMin - rightMax;
        var totalMax = leftMax - rightMin;
        var changes = new List<CandidateChange>();
        for (var i = 0; i < terms.Count; i++)
        {
            long c = terms[i].Coefficient;
            var othersMin = totalMin - lowest[i];
            var othersMax = totalMax - highest[i];
            var impossible = sets[i].Where(v => !Keeps(op, c * v, othersMin, othersMax)).ToList();
            if (impossible.Count == 0)
            {
                continue;
            }

            var onLeft = c > 0;
            var because = op == ComparisonOperator.NotEqual
                ? $"sonst wären beide Summen gleich (je {Format(onLeft ? rightMin : leftMin)})"
                : ExplainRanges(resolved, terms, i, onLeft,
                    otherMin: onLeft ? rightMin : leftMin,
                    otherMax: onLeft ? rightMax : leftMax,
                    ownOthersMin: onLeft ? leftMin - lowest[i] : rightMin + highest[i],
                    ownOthersMax: onLeft ? leftMax - highest[i] : rightMax + lowest[i]);
            if (resolved.SharedNodeIds.Count > 0)
            {
                because += SharedNote;
            }

            var text = $"{Describe(resolved)}: Knoten {terms[i].NodeId} kann nicht {FormatValues(impossible)} sein, denn {because}.";
            changes.Add(CandidateChange.Eliminate(terms[i].NodeId, impossible, new Reason(resolved.Comparison.Ref, resolved.NodeIds, text)));
        }

        return changes;
    }

    /// <summary>true, wenn die Summen der beiden Gruppen unter der Belegung <paramref name="value"/> den Vergleich erfüllen.</summary>
    public static bool IsSatisfied(ResolvedGroupComparison resolved, Func<int, int> value)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(value);
        var left = resolved.Left.Nodes.Sum(id => (long)value(id));
        var right = resolved.Right.Nodes.Sum(id => (long)value(id));
        return ComparisonOperators.Holds(resolved.Comparison.Operator, left, right);
    }

    /// <summary>
    /// Bleibt ein Wert mit Beitrag <paramref name="own"/> möglich, wenn die übrigen Summanden zusammen zwischen
    /// <paramref name="othersMin"/> und <paramref name="othersMax"/> liegen? Gefordert ist <c>own + übrige OP 0</c>.
    /// </summary>
    private static bool Keeps(ComparisonOperator op, long own, long othersMin, long othersMax) => op switch
    {
        ComparisonOperator.Equal => othersMin <= -own && -own <= othersMax,
        ComparisonOperator.NotEqual => othersMin != othersMax || own + othersMin != 0,
        ComparisonOperator.Less => own + othersMin <= -1,
        ComparisonOperator.LessOrEqual => own + othersMin <= 0,
        ComparisonOperator.Greater => own + othersMax >= 1,
        ComparisonOperator.GreaterOrEqual => own + othersMax >= 0,
        _ => true,
    };

    private static string ExplainRanges(
        ResolvedGroupComparison resolved,
        IReadOnlyList<GroupComparisonTerm> terms,
        int index,
        bool onLeft,
        long otherMin,
        long otherMax,
        long ownOthersMin,
        long ownOthersMax)
    {
        var own = onLeft ? resolved.Left : resolved.Right;
        var other = onLeft ? resolved.Right : resolved.Left;
        var otherHasTerms = terms.Any(t => t.Coefficient > 0 != onLeft);
        var ownHasOthers = terms.Where((t, j) => j != index && t.Coefficient > 0 == onLeft).Any();

        var parts = new List<string>
        {
            !otherHasTerms
                ? $"{DescribeGroup(other)} besteht nur aus gemeinsamen Knoten"
                : otherMin == otherMax
                    ? $"die Summe von {DescribeGroup(other)} ist genau {Format(otherMin)}"
                    : $"die Summe von {DescribeGroup(other)} liegt zwischen {Format(otherMin)} und {Format(otherMax)}",
        };
        if (ownHasOthers)
        {
            parts.Add(ownOthersMin == ownOthersMax
                ? $"die übrigen Knoten von {DescribeGroup(own)} ergeben zusammen genau {Format(ownOthersMin)}"
                : $"die übrigen Knoten von {DescribeGroup(own)} ergeben zusammen {Format(ownOthersMin)} bis {Format(ownOthersMax)}");
        }

        return string.Join(" und ", parts);
    }

    private static string Describe(GroupComparison comparison, Group? left, Group? right) =>
        $"{comparison.Ref}: Summe von {DescribeGroup(left, comparison.Left)} {ComparisonOperators.ToSymbolOrPlaceholder(comparison.Operator)} Summe von {DescribeGroup(right, comparison.Right)}";

    private static string DescribeGroup(Group group) => DescribeGroup(group, group.Id);

    private static string DescribeGroup(Group? group, int id) =>
        group is { Label: { Length: > 0 } label } ? $"Gruppe {id} „{label}“" : $"Gruppe {id}";

    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatValues(IEnumerable<int> values) =>
        string.Join(", ", values.Order().Select(v => v.ToString(CultureInfo.InvariantCulture)));
}
