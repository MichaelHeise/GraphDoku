using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.Core.Tests;

public class RulePropagationTests
{
    /// <summary>Wendet die Regel einmal an und liefert die Kandidaten danach.</summary>
    private static (CandidateState State, IReadOnlyList<CandidateChange> Changes) Run(
        IConstraint constraint,
        Puzzle p,
        params (int Node, int[] Candidates)[] setup)
    {
        var state = CandidateState.FromPuzzle(p);
        foreach (var (node, candidates) in setup)
        {
            foreach (var value in state.GetCandidates(node).Except(candidates).ToList())
            {
                state.Remove(node, value);
            }
        }

        var changes = RuleRegistry.Default.Get(constraint.Rule).Propagate(state, constraint);
        foreach (var change in changes)
        {
            state.Apply(change);
        }

        return (state, changes);
    }

    [Fact]
    public void Propagate_does_not_modify_the_state()
    {
        var p = TestPuzzles.WithNodes(2);
        var state = CandidateState.FromPuzzle(p);
        state.Assign(1, 5);
        var edge = new Edge(1, 1, 2, "NotEqual");

        var changes = RuleRegistry.Default.Get("NotEqual").Propagate(state, edge);

        changes.Should().ContainSingle();
        state.GetCandidates(2).Should().Contain(5);
    }

    [Fact]
    public void NotEqual_removes_fixed_value_from_other_node_with_reason()
    {
        var p = TestPuzzles.WithNodes(2);
        var (state, changes) = Run(new Edge(3, 1, 2, "NotEqual"), p, (1, [5]));

        state.GetCandidates(2).Should().NotContain(5).And.HaveCount(8);
        var change = changes.Should().ContainSingle().Subject;
        change.NodeId.Should().Be(2);
        change.Kind.Should().Be(CandidateChangeKind.Eliminate);
        change.Values.Should().Equal(5);
        change.Reason.Constraint.Should().Be(ConstraintRef.ForEdge(3));
        change.Reason.NodeIds.Should().Equal(1, 2);
        change.Reason.Text.Should().Be("Ungleich in Kante 3: Knoten 1 hat den Wert 5, also kann Knoten 2 nicht 5 sein.");
    }

    [Fact]
    public void NotEqual_without_fixed_values_changes_nothing()
    {
        var (_, changes) = Run(new Edge(1, 1, 2, "NotEqual"), TestPuzzles.WithNodes(2), (1, [1, 2]), (2, [1, 2]));
        changes.Should().BeEmpty();
    }

    [Fact]
    public void AllDifferent_removes_fixed_values_and_names_group_label()
    {
        var p = TestPuzzles.WithNodes(3);
        var group = new Group(1, [1, 2, 3], "AllDifferent", label: "Zeile 1");
        var (state, changes) = Run(group, p, (1, [3]), (2, [3, 4]), (3, [4, 5, 6]));

        state.GetCandidates(2).Should().Equal(4);
        state.GetCandidates(3).Should().Equal(4, 5, 6);
        changes.Should().ContainSingle().Which.Reason.Text.Should().Contain("Gruppe 1 „Zeile 1“");
    }

    [Fact]
    public void CoverRange_restricts_to_range_and_removes_fixed_values()
    {
        var p = TestPuzzles.WithNodes(3);
        var group = new Group(1, [1, 2, 3], "CoverRange", TestPuzzles.Params(("min", 2), ("max", 4)));
        var (state, _) = Run(group, p, (1, [2]));

        state.GetCandidates(1).Should().Equal(2);
        state.GetCandidates(2).Should().Equal(3, 4);
        state.GetCandidates(3).Should().Equal(3, 4);
    }

    [Fact]
    public void CoverRange_defaults_to_puzzle_domain()
    {
        var p = TestPuzzles.WithNodes(2, 1, 2);
        p.Nodes[0].Domain = new ValueDomain(1, 5);
        var (state, _) = Run(new Group(1, [1, 2], "CoverRange"), p);

        state.GetCandidates(1).Should().Equal(1, 2);
    }

    [Fact]
    public void Sum_uses_bounds_of_other_nodes()
    {
        // 1 + 2 Knoten, Ziel 4, Werte 1..9: der jeweils andere ist mindestens 1, also höchstens 3.
        var p = TestPuzzles.WithNodes(2);
        var parameters = TestPuzzles.Params(("target", 4), ("allowRepeats", true));
        var (state, changes) = Run(new Group(1, [1, 2], "Sum", parameters), p);

        state.GetCandidates(1).Should().Equal(1, 2, 3);
        state.GetCandidates(2).Should().Equal(1, 2, 3);
        changes.Should().HaveCount(2);
        changes[0].Reason.Text.Should().Be("Summe 4 in Gruppe 1: Knoten 1 kann nicht 4, 5, 6, 7, 8, 9 sein, denn die übrigen Knoten ergeben zusammen 1 bis 9.");
    }

    [Fact]
    public void Sum_without_repeats_checks_every_candidate_for_a_complete_assignment()
    {
        // Ziel 4 ohne Wiederholung: nur 1 + 3 und 3 + 1; die 2 bräuchte eine zweite 2.
        var p = TestPuzzles.WithNodes(2);
        var (state, changes) = Run(new Group(1, [1, 2], "Sum", TestPuzzles.Params(("target", 4))), p);

        state.GetCandidates(1).Should().Equal(1, 3);
        state.GetCandidates(2).Should().Equal(1, 3);
        changes.Should().HaveCount(4, "pro Knoten eine Streichung über Schranken und eine über die vollständige Prüfung");
        changes[^1].Reason.Text.Should().Be("Summe 4 in Gruppe 1: Knoten 2 kann nicht 2 sein, denn mit den Kandidaten der übrigen Knoten (ohne Wiederholung) lässt sich die Summe dann nicht erreichen.");
        changes[^1].Reason.NodeIds.Should().Equal(1, 2);
    }

    [Fact]
    public void Sum_cage_of_three_with_target_six_is_one_two_three()
    {
        var p = TestPuzzles.WithNodes(3);
        var (state, _) = Run(new Group(1, [1, 2, 3], "Sum", TestPuzzles.Params(("target", 6))), p);

        foreach (var node in new[] { 1, 2, 3 })
        {
            state.GetCandidates(node).Should().Equal(1, 2, 3);
        }
    }

    [Fact]
    public void Sum_search_respects_holes_in_candidates()
    {
        // Ziel 10, Knoten 2 ∈ {1, 9}, Knoten 3 ∈ {1, 9}: Knoten 1 braucht 10 − (1 + 9) = 0 oder 10 − 2 = 8 bzw. − 8.
        // Mit Wiederholung bleibt nur Knoten 1 = 8 (1 + 1 + 8), ohne Wiederholung ist 8 unmöglich (1 + 1 doppelt).
        var p = TestPuzzles.WithNodes(3);
        var withRepeats = Run(new Group(1, [1, 2, 3], "Sum", TestPuzzles.Params(("target", 10), ("allowRepeats", true))), p, (2, [1, 9]), (3, [1, 9]));
        withRepeats.State.GetCandidates(1).Should().Equal(8);
        withRepeats.State.GetCandidates(2).Should().Equal(1);

        var distinct = Run(new Group(1, [1, 2, 3], "Sum", TestPuzzles.Params(("target", 10))), p, (2, [1, 9]), (3, [1, 9]));
        distinct.State.GetCandidates(1).Should().BeEmpty("ohne Wiederholung gibt es keine Lösung");
    }

    [Fact]
    public void Sum_stays_sound_when_the_search_budget_runs_out()
    {
        // 40 Knoten mit geraden Kandidaten 2..20 und ungerader Zielsumme: unlösbar, aber weder die Schranken noch
        // die Suche innerhalb des Budgets können das zeigen. Dann wird nichts gestrichen (nur sichere Schlüsse).
        var p = TestPuzzles.WithNodes(40, 1, 20);
        var even = Enumerable.Range(1, 10).Select(i => i * 2).ToArray();
        var setup = Enumerable.Range(1, 40).Select(n => (n, even)).ToArray();
        var parameters = TestPuzzles.Params(("target", 401), ("allowRepeats", true));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (state, changes) = Run(new Group(1, Enumerable.Range(1, 40), "Sum", parameters), p, setup);

        changes.Should().BeEmpty();
        state.GetCandidates(17).Should().Equal(even);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "das Suchbudget begrenzt die Laufzeit");
    }

    [Fact]
    public void Sum_without_repeats_removes_fixed_values()
    {
        var p = TestPuzzles.WithNodes(3);
        var parameters = TestPuzzles.Params(("target", 6));
        var (state, _) = Run(new Group(1, [1, 2, 3], "Sum", parameters), p, (1, [2]));

        state.GetCandidates(2).Should().NotContain(2);
        state.GetCandidates(3).Should().NotContain(2);
    }

    [Fact]
    public void Sum_with_repeats_keeps_fixed_values()
    {
        var p = TestPuzzles.WithNodes(3);
        var parameters = TestPuzzles.Params(("target", 6), ("allowRepeats", true));
        var (state, _) = Run(new Group(1, [1, 2, 3], "Sum", parameters), p, (1, [2]));

        state.GetCandidates(2).Should().Contain(2);
        state.GetCandidates(2).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Sum_does_not_eliminate_the_same_value_twice()
    {
        // Ziel 10, Knoten 1 = 9: Die Schrankenprüfung streicht 2..9 bei Knoten 2; die 9 darf nicht ein
        // zweites Mal (als Wiederholungsverbot) gestrichen werden.
        var p = TestPuzzles.WithNodes(2);
        var (state, changes) = Run(new Group(1, [1, 2], "Sum", TestPuzzles.Params(("target", 10))), p, (1, [9]));

        state.GetCandidates(2).Should().Equal(1);
        changes.Should().ContainSingle().Which.Values.Should().Equal(2, 3, 4, 5, 6, 7, 8, 9);
        changes.SelectMany(c => c.Values.Select(v => (c.NodeId, v))).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Sum_of_a_very_large_group_does_not_overflow_the_stack()
    {
        // Regression (Review PR #1): Die Suche war rekursiv mit Tiefe = Gruppengröße. 10.000 Knoten führten zu einem
        // nicht abfangbaren StackOverflow, der die ganze App beendete. Auf einem Thread mit 512 KB Stack ginge das
        // schon bei 20.000 Knoten schief; die iterative Suche braucht keinen Stack pro Knoten.
        const int count = 20_000;
        var p = TestPuzzles.WithNodes(count);
        var group = new Group(1, Enumerable.Range(1, count), "Sum", TestPuzzles.Params(("target", 100_000), ("allowRepeats", true)));
        var state = CandidateState.FromPuzzle(p);
        IReadOnlyList<CandidateChange>? changes = null;
        Exception? error = null;

        var thread = new Thread(
            () =>
            {
                try
                {
                    changes = RuleRegistry.Default.Get("Sum").Propagate(state, group);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            },
            maxStackSize: 512 * 1024);
        thread.Start();
        thread.Join();

        error.Should().BeNull();
        changes.Should().BeEmpty("jeder Wert 1..9 ist mit passenden übrigen Werten möglich");
    }

    [Fact]
    public void Sum_search_matches_brute_force_on_random_small_groups()
    {
        // Die Suche muss genau die Kandidaten behalten, die in einer vollständigen Belegung vorkommen.
        var random = new Random(4711);
        for (var round = 0; round < 1_500; round++)
        {
            var count = random.Next(2, 5);
            var allowRepeats = random.Next(2) == 0;
            var target = random.Next(count, (9 * count) + 1);
            var setup = Enumerable.Range(1, count)
                .Select(n =>
                {
                    var values = Enumerable.Range(1, 9).Where(_ => random.Next(3) > 0).ToArray();
                    return (n, values.Length == 0 ? [random.Next(1, 10)] : values);
                })
                .ToArray();
            var group = new Group(1, Enumerable.Range(1, count), "Sum", TestPuzzles.Params(("target", target), ("allowRepeats", allowRepeats)));

            var (state, _) = Run(group, TestPuzzles.WithNodes(count), setup);

            var expected = BruteForceSumSupport(setup.Select(s => s.Item2).ToArray(), target, allowRepeats);
            for (var i = 0; i < count; i++)
            {
                state.GetCandidates(i + 1).Should().Equal(
                    expected[i],
                    $"Runde {round}: Ziel {target}, Wiederholung {allowRepeats}, Kandidaten {string.Join(" | ", setup.Select(s => string.Join(",", s.Item2)))}");
            }
        }
    }

    /// <summary>Alle Werte pro Knoten, die in einer vollständigen Belegung mit der Zielsumme vorkommen.</summary>
    private static List<int>[] BruteForceSumSupport(int[][] candidates, int target, bool allowRepeats)
    {
        var supported = candidates.Select(_ => new SortedSet<int>()).ToArray();
        var assignment = new int[candidates.Length];

        void Visit(int depth, int sum)
        {
            if (depth == candidates.Length)
            {
                if (sum == target && (allowRepeats || assignment.Distinct().Count() == assignment.Length))
                {
                    for (var i = 0; i < assignment.Length; i++)
                    {
                        supported[i].Add(assignment[i]);
                    }
                }

                return;
            }

            foreach (var value in candidates[depth])
            {
                assignment[depth] = value;
                Visit(depth + 1, sum + value);
            }
        }

        Visit(0, 0);
        return [.. supported.Select(s => s.ToList())];
    }

    [Fact]
    public void CoverRange_does_not_eliminate_the_same_value_twice()
    {
        // Knoten 1 ist auf 1 festgelegt (außerhalb von 2..4, also ein Widerspruch für ihn selbst); Knoten 2 verliert
        // die 1 nur einmal, mit der ersten Begründung („außerhalb des Bereichs“).
        var p = TestPuzzles.WithNodes(3);
        var group = new Group(1, [1, 2, 3], "CoverRange", TestPuzzles.Params(("min", 2), ("max", 4)));
        var state = CandidateState.FromPuzzle(p);
        state.Assign(1, 1);

        var changes = RuleRegistry.Default.Get("CoverRange").Propagate(state, group);

        changes.SelectMany(c => c.Values.Select(v => (c.NodeId, v))).Should().OnlyHaveUniqueItems();
        changes.Where(c => c.NodeId == 2).Should().ContainSingle().Which.Reason.Text.Should().Contain("Nur Werte von 2 bis 4");
        changes.Should().OnlyContain(c => c.Values.Count > 0);
    }

    [Fact]
    public void Single_node_sum_fixes_the_value()
    {
        var (state, _) = Run(new Group(1, [1], "Sum", TestPuzzles.Params(("target", 7))), TestPuzzles.WithNodes(1));
        state.GetCandidates(1).Should().Equal(7);
    }

    [Fact]
    public void Thermometer_bounds_strictly_increasing_values()
    {
        var p = TestPuzzles.WithNodes(3);
        var (state, changes) = Run(new Group(1, [1, 2, 3], "Thermometer"), p);

        state.GetCandidates(1).Should().Equal(1, 2, 3, 4, 5, 6, 7);
        state.GetCandidates(2).Should().Equal(2, 3, 4, 5, 6, 7, 8);
        state.GetCandidates(3).Should().Equal(3, 4, 5, 6, 7, 8, 9);
        changes.Should().HaveCount(3);
        changes[1].Reason.Text.Should().Contain("Position 2").And.Contain("zwischen 2 und 8");
    }

    [Fact]
    public void Thermometer_respects_list_order()
    {
        var p = TestPuzzles.WithNodes(2);
        var (state, _) = Run(new Group(1, [2, 1], "Thermometer"), p, (1, [3]));

        state.GetCandidates(2).Should().Equal(1, 2);
    }

    [Fact]
    public void Difference_exact_keeps_only_supported_values()
    {
        var p = TestPuzzles.WithNodes(2);
        var edge = new Edge(1, 1, 2, "Difference", TestPuzzles.Params(("k", 3)));
        var (state, changes) = Run(edge, p, (1, [1, 5]));

        state.GetCandidates(2).Should().Equal(2, 4, 8);
        state.GetCandidates(1).Should().Equal(1, 5);
        changes.Should().ContainSingle().Which.Reason.Text.Should().Contain("Abstand genau 3");
    }

    [Fact]
    public void Difference_atLeast_removes_values_too_close_to_all_partners()
    {
        var p = TestPuzzles.WithNodes(2);
        var edge = new Edge(1, 1, 2, "Difference", TestPuzzles.Params(("k", 5), ("mode", "atLeast")));
        var (state, _) = Run(edge, p);

        // 5 hat zu keinem Wert in 1..9 einen Abstand ≥ 5.
        state.GetCandidates(1).Should().Equal(1, 2, 3, 4, 6, 7, 8, 9);
        state.GetCandidates(2).Should().Equal(1, 2, 3, 4, 6, 7, 8, 9);
    }

    [Fact]
    public void Ratio_keeps_values_with_partner_at_factor()
    {
        var p = TestPuzzles.WithNodes(2);
        var (state, _) = Run(new Edge(1, 1, 2, "Ratio"), p);

        state.GetCandidates(1).Should().Equal(1, 2, 3, 4, 6, 8);
        state.GetCandidates(2).Should().Equal(1, 2, 3, 4, 6, 8);
    }

    [Fact]
    public void Ratio_with_custom_factor()
    {
        var p = TestPuzzles.WithNodes(2);
        var (state, _) = Run(new Edge(1, 1, 2, "Ratio", TestPuzzles.Params(("factor", 3))), p, (2, [3]));

        state.GetCandidates(1).Should().Equal(1, 9);
    }

    [Fact]
    public void LessThan_is_directed()
    {
        var p = TestPuzzles.WithNodes(2);
        var (state, _) = Run(new Edge(1, 1, 2, "LessThan"), p, (2, [4, 6]));

        state.GetCandidates(1).Should().Equal(1, 2, 3, 4, 5);
        state.GetCandidates(2).Should().Equal(4, 6);

        var (reverse, _) = Run(new Edge(1, 2, 1, "LessThan"), p, (2, [4, 6]));
        reverse.GetCandidates(1).Should().Equal(5, 6, 7, 8, 9);
    }

    [Fact]
    public void Propagation_is_skipped_when_a_node_has_no_candidates()
    {
        var p = TestPuzzles.WithNodes(2);
        var state = CandidateState.FromPuzzle(p);
        foreach (var v in Enumerable.Range(1, 9))
        {
            state.Remove(1, v);
        }

        RuleRegistry.Default.Get("LessThan").Propagate(state, new Edge(1, 1, 2, "LessThan")).Should().BeEmpty();
    }

    [Fact]
    public void Every_catalog_rule_is_sound_on_a_solved_assignment()
    {
        // Eine gültige Belegung darf von keiner Regel verändert werden.
        var p = TestPuzzles.WithNodes(4);
        int[] values = [1, 2, 3, 4];
        for (var i = 0; i < 4; i++)
        {
            p.Nodes[i].Given = values[i];
        }

        IConstraint[] constraints =
        [
            new Group(1, [1, 2, 3, 4], "AllDifferent"),
            new Group(2, [1, 2, 3, 4], "CoverRange", TestPuzzles.Params(("min", 1), ("max", 4))),
            new Group(3, [1, 2, 3, 4], "Sum", TestPuzzles.Params(("target", 10))),
            new Group(4, [1, 2, 3, 4], "Thermometer"),
            new Edge(1, 1, 2, "NotEqual"),
            new Edge(2, 2, 3, "Difference", TestPuzzles.Params(("k", 1))),
            new Edge(3, 1, 3, "Difference", TestPuzzles.Params(("k", 2), ("mode", "atLeast"))),
            new Edge(4, 2, 4, "Ratio"),
            new Edge(5, 1, 4, "LessThan"),
        ];

        var state = CandidateState.FromPuzzle(p);
        foreach (var constraint in constraints)
        {
            var rule = RuleRegistry.Default.Get(constraint.Rule);
            rule.Validate(constraint, p).Should().BeEmpty($"die {constraint.Rule}-Testregel muss gültig sein");
            rule.Propagate(state, constraint)
                .Should().BeEmpty($"{constraint.Rule} darf eine gültige Belegung nicht ändern");
        }
    }
}
