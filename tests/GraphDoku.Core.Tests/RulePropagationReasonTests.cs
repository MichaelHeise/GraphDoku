using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.Core.Tests;

/// <summary>
/// Paket 6: Begründungen der Regeln und Abgleich von <c>Propagate</c> mit vollständigem Durchprobieren
/// (Korrektheit für alle Regeln, Vollständigkeit für die Regeln mit Kantenkonsistenz).
/// </summary>
public class RulePropagationReasonTests
{
    private static IReadOnlyList<CandidateChange> Propagate(IConstraint constraint, CandidateState state) =>
        RuleRegistry.Default.Get(constraint.Rule).Propagate(state, constraint);

    private static CandidateState State(Puzzle puzzle, params (int Node, int[] Candidates)[] setup)
    {
        var state = CandidateState.FromPuzzle(puzzle);
        foreach (var (node, candidates) in setup)
        {
            foreach (var value in state.GetCandidates(node).Except(candidates).ToList())
            {
                state.Remove(node, value);
            }
        }

        return state;
    }

    [Fact]
    public void AllDifferent_uses_the_same_reason_for_all_targets_of_a_placed_value()
    {
        var group = new Group(4, [1, 2, 3, 4], "AllDifferent", label: "Zeile 3");
        var state = State(TestPuzzles.WithNodes(4), (2, [5]));

        var changes = Propagate(group, state);

        changes.Select(c => c.NodeId).Should().Equal(1, 3, 4);
        changes.Select(c => c.Reason.Text).Distinct().Should().ContainSingle()
            .Which.Should().Be("Gruppe 4 „Zeile 3“ enthält bereits die 5 (Knoten 2). Alle Werte sind verschieden. Also ist die 5 in den übrigen Knoten der Gruppe nicht mehr möglich.");
        changes.Should().OnlyContain(c => c.Reason.Constraint == ConstraintRef.ForGroup(4) && c.Reason.NodeIds[0] == 2);
        changes[1].Reason.NodeIds.Should().Equal(2, 3);
    }

    [Fact]
    public void CoverRange_reason_names_the_range()
    {
        var group = new Group(1, [1, 2, 3], "CoverRange", TestPuzzles.Params(("min", 2), ("max", 4)));
        var state = State(TestPuzzles.WithNodes(3), (1, [3]), (2, [2, 3]), (3, [2, 3, 4]));

        var changes = Propagate(group, state);

        changes.Should().HaveCount(2);
        changes[0].Reason.Text.Should().Contain("enthält bereits die 3 (Knoten 1). Jede Zahl von 2 bis 4 kommt genau einmal vor.");
    }

    [Fact]
    public void LessThan_reasons_name_the_bound_of_the_other_node()
    {
        var edge = new Edge(5, 1, 2, "LessThan");
        var state = State(TestPuzzles.WithNodes(2), (1, [3, 4, 7]), (2, [2, 5, 6]));

        var changes = Propagate(edge, state);

        changes.Should().HaveCount(2);
        changes[0].NodeId.Should().Be(1);
        changes[0].Values.Should().Equal(7);
        changes[0].Reason.Text.Should().Be("Kleiner als an Kante 5 (Knoten 1 < Knoten 2): Knoten 2 ist höchstens 6, also kann Knoten 1 nicht 7 sein.");
        changes[1].Values.Should().Equal(2);
        changes[1].Reason.Text.Should().Be("Kleiner als an Kante 5 (Knoten 1 < Knoten 2): Knoten 1 ist mindestens 3, also kann Knoten 2 nicht 2 sein.");
        changes[1].Reason.NodeIds.Should().Equal(2, 1);
    }

    [Fact]
    public void Difference_reason_lists_the_candidates_of_the_other_node()
    {
        var edge = new Edge(1, 1, 2, "Difference", TestPuzzles.Params(("k", 3)));
        var state = State(TestPuzzles.WithNodes(2), (1, [1, 5]));

        var change = Propagate(edge, state).Should().ContainSingle().Subject;

        change.Reason.Text.Should().Be("Differenz (Abstand genau 3) an Kante 1: Knoten 2 kann keinen der Werte 1, 3, 5, 6, 7, 9 haben, weil Knoten 1 (Kandidaten: 1, 5) dazu keinen passenden Wert hat.");
    }

    [Fact]
    public void Ratio_handles_factor_one_and_negative_values()
    {
        var p = TestPuzzles.WithNodes(2, -6, 6);
        var same = Propagate(new Edge(1, 1, 2, "Ratio", TestPuzzles.Params(("factor", 1))), State(p, (1, [-3, 2])));
        same.Should().ContainSingle().Which.Values.Should().HaveCount(11, "bei Faktor 1 müssen beide Werte gleich sein");

        var state = State(p, (1, [-6]), (2, [-4, -3, 3, 6]));
        var changes = Propagate(new Edge(2, 1, 2, "Ratio"), state);
        changes.Should().ContainSingle().Which.Values.Should().Equal(-4, 3, 6);
    }

    [Fact]
    public void Thermometer_uses_next_candidates_across_gaps()
    {
        // Knoten 1 = 5, Knoten 2 ∈ {2, 8}, Knoten 3 ∈ {3..9}: Knoten 2 muss 8 sein, Knoten 3 also 9.
        var group = new Group(1, [1, 2, 3], "Thermometer");
        var state = State(TestPuzzles.WithNodes(3), (1, [5]), (2, [2, 8]), (3, [3, 4, 5, 6, 7, 8, 9]));

        var changes = Propagate(group, state);

        changes.Select(c => (c.NodeId, c.Values.ToArray())).Should().BeEquivalentTo(
            new[] { (2, new[] { 2 }), (3, new[] { 3, 4, 5, 6, 7, 8 }) });
        changes[1].Reason.Text.Should().Be("Thermometer Gruppe 1: Die Werte steigen streng an, also muss Knoten 3 (Position 3) den Wert 9 haben; streiche 3, 4, 5, 6, 7, 8.");
    }

    [Fact]
    public void Sum_reason_says_exactly_when_the_rest_is_fixed()
    {
        var group = new Group(1, [1, 2], "Sum", TestPuzzles.Params(("target", 10)));
        var state = State(TestPuzzles.WithNodes(2), (1, [4]));

        var change = Propagate(group, state).Should().ContainSingle().Subject;

        change.NodeId.Should().Be(2);
        change.Reason.Text.Should().EndWith("denn die übrigen Knoten ergeben zusammen genau 4.");
    }

    public static TheoryData<string> RuleNames() =>
        ["AllDifferent", "CoverRange", "Sum", "Sum+repeats", "Thermometer", "NotEqual", "Difference", "Difference+atLeast", "Ratio", "LessThan"];

    /// <summary>
    /// Zufällige Kandidatenmengen: Keine Regel streicht einen Wert, der in einer erfüllenden Belegung vorkommt.
    /// Regeln mit vollständiger Konsistenz streichen zudem jeden Wert, der in keiner vorkommt.
    /// </summary>
    [Theory]
    [MemberData(nameof(RuleNames))]
    public void Propagate_matches_brute_force(string name)
    {
        var random = new Random(4711 + name.Length);
        var rule = name.Split('+')[0];
        var complete = rule is not ("AllDifferent" or "CoverRange");
        for (var trial = 0; trial < 300; trial++)
        {
            var (constraint, puzzle) = RandomConstraint(name, rule, random);
            var state = CandidateState.FromPuzzle(puzzle);
            foreach (var nodeId in constraint.NodeIds)
            {
                var keep = puzzle.Domain.Values.Where(_ => random.Next(3) > 0).ToList();
                if (keep.Count == 0)
                {
                    keep.Add(random.Next(puzzle.Domain.Min, puzzle.Domain.Max + 1));
                }

                foreach (var value in state.GetCandidates(nodeId).Except(keep).ToList())
                {
                    state.Remove(nodeId, value);
                }
            }

            RuleRegistry.Default.Get(rule).Validate(constraint, puzzle).Should().BeEmpty();
            var supported = Supported(constraint, puzzle, state);
            var eliminated = Propagate(constraint, state).SelectMany(c => c.Values.Select(v => (c.NodeId, v))).ToList();

            eliminated.Should().OnlyHaveUniqueItems();
            eliminated.Should().NotIntersectWith(supported, $"{name}, Versuch {trial}: nur sichere Schlüsse");
            if (complete)
            {
                var unsupported = constraint.NodeIds
                    .SelectMany(n => state.GetCandidates(n).Select(v => (n, v)))
                    .Where(pair => !supported.Contains(pair));
                eliminated.Should().BeEquivalentTo(unsupported, $"{name}, Versuch {trial}: Kantenkonsistenz");
            }
        }
    }

    private static (IConstraint Constraint, Puzzle Puzzle) RandomConstraint(string name, string rule, Random random)
    {
        var puzzle = TestPuzzles.WithNodes(4, 1, 6);
        int[] Nodes(int count) => [.. Enumerable.Range(1, 4).OrderBy(_ => random.Next()).Take(count)];
        switch (name)
        {
            case "Difference" or "Difference+atLeast":
                var mode = name == "Difference" ? "exact" : "atLeast";
                var pair = Nodes(2);
                return (new Edge(1, pair[0], pair[1], rule, TestPuzzles.Params(("k", random.Next(0, 5)), ("mode", mode))), puzzle);
            case "Ratio":
                var ratioPair = Nodes(2);
                return (new Edge(1, ratioPair[0], ratioPair[1], rule, TestPuzzles.Params(("factor", random.Next(1, 4)))), puzzle);
            case "NotEqual" or "LessThan":
                var edgePair = Nodes(2);
                return (new Edge(1, edgePair[0], edgePair[1], rule), puzzle);
            case "CoverRange":
                var size = random.Next(1, 5);
                var min = random.Next(1, 8 - size);
                return (new Group(1, Nodes(size), rule, TestPuzzles.Params(("min", min), ("max", min + size - 1))), puzzle);
            case "Sum" or "Sum+repeats":
                var count = random.Next(1, 5);
                var parameters = TestPuzzles.Params(("target", random.Next(count, (6 * count) + 1)), ("allowRepeats", name == "Sum+repeats"));
                return (new Group(1, Nodes(count), rule, parameters), puzzle);
            default:
                return (new Group(1, Nodes(random.Next(2, 5)), rule), puzzle);
        }
    }

    /// <summary>Alle (Knoten, Wert), die in einer erfüllenden Belegung aus den Kandidaten vorkommen.</summary>
    private static HashSet<(int, int)> Supported(IConstraint constraint, Puzzle puzzle, CandidateState state)
    {
        var nodes = constraint.NodeIds;
        var result = new HashSet<(int, int)>();
        var values = new int[nodes.Count];
        Enumerate(0);
        return result;

        void Enumerate(int index)
        {
            if (index == nodes.Count)
            {
                if (Satisfies(constraint, puzzle, values))
                {
                    for (var i = 0; i < nodes.Count; i++)
                    {
                        result.Add((nodes[i], values[i]));
                    }
                }

                return;
            }

            foreach (var value in state.GetCandidates(nodes[index]))
            {
                values[index] = value;
                Enumerate(index + 1);
            }
        }
    }

    /// <summary>Bedeutung der Regeln laut Katalog, unabhängig vom Regelcode nachprogrammiert.</summary>
    private static bool Satisfies(IConstraint constraint, Puzzle puzzle, int[] v)
    {
        var p = constraint.Params;
        var distinct = v.Distinct().Count() == v.Length;
        return constraint.Rule switch
        {
            "AllDifferent" => distinct,
            "CoverRange" => v.Order().SequenceEqual(Enumerable.Range(p.GetInt("min", puzzle.Domain.Min), v.Length)),
            "Sum" => v.Sum() == p.GetInt("target", 0) && (p.GetBool("allowRepeats", false) || distinct),
            "Thermometer" => v.Zip(v.Skip(1)).All(x => x.First < x.Second),
            "NotEqual" => v[0] != v[1],
            "Difference" => p.GetString("mode", "exact") == "atLeast"
                ? Math.Abs(v[0] - v[1]) >= p.GetInt("k", 0)
                : Math.Abs(v[0] - v[1]) == p.GetInt("k", 0),
            "Ratio" => v[0] == p.GetInt("factor", 2) * v[1] || v[1] == p.GetInt("factor", 2) * v[0],
            "LessThan" => v[0] < v[1],
            _ => throw new NotSupportedException(constraint.Rule),
        };
    }
}
