using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.Core.Tests;

/// <summary>
/// Gruppenvergleiche: Auflösen der Gruppen, Begründungstexte und Abgleich von <c>Propagate</c> mit vollständigem
/// Durchprobieren (Korrektheit) sowie Vollständigkeit auf vollständigen Belegungen.
/// </summary>
public class GroupComparisonLogicTests
{
    private const string SharedNote = " (gemeinsame Knoten zählen auf beiden Seiten gleich und bleiben außen vor)";

    public static TheoryData<ComparisonOperator> AllOperators => [.. ComparisonOperators.All];

    /// <summary>Rätsel mit den Knoten 1..<paramref name="nodeCount"/> und zwei Gruppen samt Vergleich (Id 1).</summary>
    private static (Puzzle Puzzle, ResolvedGroupComparison Resolved) Create(
        int[] left,
        int[] right,
        ComparisonOperator op,
        int nodeCount = 4,
        int min = 1,
        int max = 9,
        string? leftLabel = null,
        string? rightLabel = null,
        int leftId = 1,
        int rightId = 2)
    {
        var puzzle = TestPuzzles.WithNodes(nodeCount, min, max);
        puzzle.Groups.Add(new Group(leftId, left, "AllDifferent", label: leftLabel));
        puzzle.Groups.Add(new Group(rightId, right, "AllDifferent", label: rightLabel));
        var comparison = new GroupComparison(1, leftId, rightId, op);
        puzzle.GroupComparisons.Add(comparison);
        return (puzzle, GroupComparisonLogic.Resolve(puzzle, comparison)!);
    }

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

    private static Dictionary<int, int[]> Eliminations(IReadOnlyList<CandidateChange> changes)
    {
        changes.Where(c => c.Kind != CandidateChangeKind.Eliminate).Should().BeEmpty();
        changes.Select(c => c.NodeId).Should().OnlyHaveUniqueItems();
        return changes.ToDictionary(c => c.NodeId, c => c.Values.ToArray());
    }

    [Fact]
    public void Resolve_builds_terms_with_coefficients_in_left_then_right_order()
    {
        var (_, resolved) = Create([3, 1, 2], [4, 2, 5], ComparisonOperator.Less, nodeCount: 5);

        resolved.Terms.Should().Equal(
            new GroupComparisonTerm(3, 1),
            new GroupComparisonTerm(1, 1),
            new GroupComparisonTerm(4, -1),
            new GroupComparisonTerm(5, -1));
        resolved.NodeIds.Should().Equal(3, 1, 2, 4, 5);
        resolved.SharedNodeIds.Should().Equal(2);
        resolved.Left.Id.Should().Be(1);
        resolved.Right.Id.Should().Be(2);
    }

    [Fact]
    public void Resolve_counts_repeated_nodes_of_unchecked_groups()
    {
        // Ungeprüft geladene Dateien können Knoten doppelt enthalten; die Vorfaktoren zählen dann mit.
        var (_, resolved) = Create([1, 1, 2], [2], ComparisonOperator.Equal, nodeCount: 2);

        resolved.Terms.Should().Equal(new GroupComparisonTerm(1, 2));
        resolved.SharedNodeIds.Should().Equal(2);
        resolved.NodeIds.Should().Equal(1, 2);
    }

    [Fact]
    public void Resolve_returns_null_for_missing_groups_and_no_terms_for_equal_node_sets()
    {
        var puzzle = TestPuzzles.WithNodes(3);
        puzzle.Groups.Add(new Group(1, [1, 2], "AllDifferent"));
        puzzle.Groups.Add(new Group(2, [2, 1], "AllDifferent"));

        GroupComparisonLogic.Resolve(puzzle, new GroupComparison(1, 1, 9)).Should().BeNull();
        GroupComparisonLogic.Resolve(puzzle, new GroupComparison(1, 9, 1)).Should().BeNull();
        var same = GroupComparisonLogic.Resolve(puzzle, new GroupComparison(1, 1, 2))!;
        same.Terms.Should().BeEmpty();
        same.SharedNodeIds.Should().Equal(1, 2);
        GroupComparisonLogic.Propagate(CandidateState.FromPuzzle(puzzle), same).Should().BeEmpty();
    }

    [Fact]
    public void Describe_uses_labels_and_symbols_and_tolerates_missing_groups()
    {
        var (puzzle, resolved) = Create([1, 2], [3], ComparisonOperator.LessOrEqual, leftLabel: "A", leftId: 2, rightId: 5);

        GroupComparisonLogic.Describe(resolved).Should().Be("Gruppenvergleich 1: Summe von Gruppe 2 „A“ ≤ Summe von Gruppe 5");
        GroupComparisonLogic.Describe(puzzle, new GroupComparison(7, 5, 8, ComparisonOperator.NotEqual))
            .Should().Be("Gruppenvergleich 7: Summe von Gruppe 5 ≠ Summe von Gruppe 8");
        GroupComparisonLogic.Describe(puzzle, new GroupComparison(7, 5, 2, (ComparisonOperator)42))
            .Should().Be("Gruppenvergleich 7: Summe von Gruppe 5 ? Summe von Gruppe 2 „A“");
    }

    [Fact]
    public void Equal_reason_names_the_ranges_of_both_sides()
    {
        var (puzzle, resolved) = Create([1, 2], [3], ComparisonOperator.Equal, leftLabel: "A", rightLabel: "B", leftId: 2, rightId: 5);
        var state = State(puzzle, (3, [1, 2, 3]));

        var changes = GroupComparisonLogic.Propagate(state, resolved);

        Eliminations(changes).Should().BeEquivalentTo(new Dictionary<int, int[]>
        {
            [1] = [3, 4, 5, 6, 7, 8, 9],
            [2] = [3, 4, 5, 6, 7, 8, 9],
            [3] = [1],
        });
        changes[0].Reason.Text.Should().Be(
            "Gruppenvergleich 1: Summe von Gruppe 2 „A“ = Summe von Gruppe 5 „B“: Knoten 1 kann nicht 3, 4, 5, 6, 7, 8, 9 sein, denn die Summe von Gruppe 5 „B“ liegt zwischen 1 und 3 und die übrigen Knoten von Gruppe 2 „A“ ergeben zusammen 1 bis 9.");
        changes[2].Reason.Text.Should().Be(
            "Gruppenvergleich 1: Summe von Gruppe 2 „A“ = Summe von Gruppe 5 „B“: Knoten 3 kann nicht 1 sein, denn die Summe von Gruppe 2 „A“ liegt zwischen 2 und 18.");
        changes.Should().OnlyContain(c => c.Reason.Constraint == ConstraintRef.ForGroupComparison(1));
        changes.Should().OnlyContain(c => c.Reason.NodeIds.SequenceEqual(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void Single_values_are_named_exactly()
    {
        var (puzzle, resolved) = Create([1, 2], [3], ComparisonOperator.Greater);
        var state = State(puzzle, (2, [4]), (3, [8]));

        var changes = GroupComparisonLogic.Propagate(state, resolved);

        // x1 + 4 > 8 → x1 ≥ 5.
        Eliminations(changes).Should().BeEquivalentTo(new Dictionary<int, int[]> { [1] = [1, 2, 3, 4] });
        changes.Single().Reason.Text.Should().Be(
            "Gruppenvergleich 1: Summe von Gruppe 1 > Summe von Gruppe 2: Knoten 1 kann nicht 1, 2, 3, 4 sein, denn die Summe von Gruppe 2 ist genau 8 und die übrigen Knoten von Gruppe 1 ergeben zusammen genau 4.");
    }

    [Fact]
    public void NotEqual_removes_the_value_that_makes_both_sums_equal()
    {
        var (puzzle, resolved) = Create([1], [2, 3], ComparisonOperator.NotEqual);
        var state = State(puzzle, (2, [1]), (3, [3]));

        var changes = GroupComparisonLogic.Propagate(state, resolved);

        Eliminations(changes).Should().BeEquivalentTo(new Dictionary<int, int[]> { [1] = [4] });
        changes.Single().Reason.Text.Should().Be(
            "Gruppenvergleich 1: Summe von Gruppe 1 ≠ Summe von Gruppe 2: Knoten 1 kann nicht 4 sein, denn sonst wären beide Summen gleich (je 4).");
    }

    [Fact]
    public void NotEqual_on_the_right_side_names_the_left_sum()
    {
        var (puzzle, resolved) = Create([1, 2], [3], ComparisonOperator.NotEqual);
        var state = State(puzzle, (1, [2]), (2, [5]));

        var changes = GroupComparisonLogic.Propagate(state, resolved);

        Eliminations(changes).Should().BeEquivalentTo(new Dictionary<int, int[]> { [3] = [7] });
        changes.Single().Reason.Text.Should().EndWith("Knoten 3 kann nicht 7 sein, denn sonst wären beide Summen gleich (je 7).");
    }

    [Fact]
    public void Shared_nodes_cancel_out_and_are_mentioned()
    {
        var (puzzle, resolved) = Create([1, 2], [2, 3], ComparisonOperator.Less);
        var state = State(puzzle, (3, [1, 2]));

        var changes = GroupComparisonLogic.Propagate(state, resolved);

        // x1 + x2 < x2 + x3 ⇔ x1 < x3 ≤ 2; Knoten 2 bleibt unberührt.
        Eliminations(changes).Should().BeEquivalentTo(new Dictionary<int, int[]>
        {
            [1] = [2, 3, 4, 5, 6, 7, 8, 9],
            [3] = [1],
        });
        changes[0].Reason.Text.Should().Be(
            "Gruppenvergleich 1: Summe von Gruppe 1 < Summe von Gruppe 2: Knoten 1 kann nicht 2, 3, 4, 5, 6, 7, 8, 9 sein, denn die Summe von Gruppe 2 liegt zwischen 1 und 2" + SharedNote + ".");
        changes[1].Reason.Text.Should().Be(
            "Gruppenvergleich 1: Summe von Gruppe 1 < Summe von Gruppe 2: Knoten 3 kann nicht 1 sein, denn die Summe von Gruppe 1 liegt zwischen 1 und 9" + SharedNote + ".");
        changes.Should().OnlyContain(c => c.Reason.NodeIds.SequenceEqual(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void A_side_with_only_shared_nodes_is_named_as_such()
    {
        var (puzzle, resolved) = Create([1, 2], [2], ComparisonOperator.LessOrEqual, min: 0, max: 3);
        var state = CandidateState.FromPuzzle(puzzle);

        var changes = GroupComparisonLogic.Propagate(state, resolved);

        // x1 + x2 ≤ x2 ⇔ x1 ≤ 0.
        Eliminations(changes).Should().BeEquivalentTo(new Dictionary<int, int[]> { [1] = [1, 2, 3] });
        changes.Single().Reason.Text.Should().Be(
            "Gruppenvergleich 1: Summe von Gruppe 1 ≤ Summe von Gruppe 2: Knoten 1 kann nicht 1, 2, 3 sein, denn Gruppe 2 besteht nur aus gemeinsamen Knoten" + SharedNote + ".");
    }

    [Theory]
    [MemberData(nameof(AllOperators))]
    public void Satisfiable_full_ranges_give_no_changes_and_the_state_is_not_mutated(ComparisonOperator op)
    {
        var (puzzle, resolved) = Create([1, 2], [3, 4], op);
        var state = CandidateState.FromPuzzle(puzzle);
        var before = state.Clone();

        GroupComparisonLogic.Propagate(state, resolved).Should().BeEmpty();
        state.HasSameCandidates(before).Should().BeTrue();

        var narrowed = State(puzzle, (1, [1]), (2, [1]));
        var snapshot = narrowed.Clone();
        _ = GroupComparisonLogic.Propagate(narrowed, resolved);
        narrowed.HasSameCandidates(snapshot).Should().BeTrue();
    }

    [Theory]
    [InlineData(ComparisonOperator.Equal, new[] { 1, 2, 5, 6, 7, 8, 9 }, new int[0])]
    [InlineData(ComparisonOperator.NotEqual, new int[0], new int[0])]
    [InlineData(ComparisonOperator.Less, new[] { 4, 5, 6, 7, 8, 9 }, new int[0])]
    [InlineData(ComparisonOperator.LessOrEqual, new[] { 5, 6, 7, 8, 9 }, new int[0])]
    [InlineData(ComparisonOperator.Greater, new[] { 1, 2, 3 }, new int[0])]
    [InlineData(ComparisonOperator.GreaterOrEqual, new[] { 1, 2 }, new int[0])]
    public void Every_operator_bounds_the_left_node_against_a_fixed_right_sum(ComparisonOperator op, int[] removedFromLeft, int[] removedFromRight)
    {
        // Gruppe 1 = {1}, Gruppe 2 = {2, 3} mit Knoten 2 = 1 und Knoten 3 ∈ {2, 3}: rechts 3 bis 4.
        var (puzzle, resolved) = Create([1], [2, 3], op);
        var state = State(puzzle, (2, [1]), (3, [2, 3]));

        var changes = Eliminations(GroupComparisonLogic.Propagate(state, resolved));

        changes.GetValueOrDefault(1, []).Should().Equal(removedFromLeft);
        changes.GetValueOrDefault(3, []).Should().Equal(removedFromRight);
        changes.Should().NotContainKey(2);
    }

    [Fact]
    public void Greater_and_less_remove_values_from_the_right_side_too()
    {
        var (puzzle, resolved) = Create([1], [2], ComparisonOperator.Greater);
        var state = State(puzzle, (1, [3, 4]));

        // x1 > x2 mit x1 ≤ 4 → x2 ≤ 3.
        Eliminations(GroupComparisonLogic.Propagate(state, resolved))
            .Should().BeEquivalentTo(new Dictionary<int, int[]> { [2] = [4, 5, 6, 7, 8, 9] });

        var (puzzle2, resolved2) = Create([1], [2], ComparisonOperator.GreaterOrEqual);
        var state2 = State(puzzle2, (2, [6, 7]));

        // x1 ≥ x2 ≥ 6 → x1 ≥ 6.
        Eliminations(GroupComparisonLogic.Propagate(state2, resolved2))
            .Should().BeEquivalentTo(new Dictionary<int, int[]> { [1] = [1, 2, 3, 4, 5] });
    }

    [Fact]
    public void Nothing_is_derived_for_missing_nodes_or_existing_contradictions()
    {
        var (puzzle, resolved) = Create([1, 2], [3], ComparisonOperator.Less);
        var contradiction = State(puzzle, (2, []));
        GroupComparisonLogic.Propagate(contradiction, resolved).Should().BeEmpty();

        var dangling = TestPuzzles.WithNodes(2);
        dangling.Groups.Add(new Group(1, [1, 7], "AllDifferent"));
        dangling.Groups.Add(new Group(2, [2], "AllDifferent"));
        var comparison = new GroupComparison(1, 1, 2, ComparisonOperator.Less);
        var resolvedDangling = GroupComparisonLogic.Resolve(dangling, comparison)!;
        GroupComparisonLogic.Propagate(CandidateState.FromPuzzle(dangling), resolvedDangling).Should().BeEmpty();

        // Ungültiger Operator (nur ungeprüft möglich): keine Schlüsse.
        resolved.Comparison.Operator = (ComparisonOperator)42;
        GroupComparisonLogic.Propagate(CandidateState.FromPuzzle(puzzle), resolved).Should().BeEmpty();
    }

    [Fact]
    public void IsSatisfied_compares_the_raw_sums_of_both_groups()
    {
        var (_, resolved) = Create([1, 2], [2, 3], ComparisonOperator.LessOrEqual);
        int[] values = [0, 4, 9, 4];

        GroupComparisonLogic.IsSatisfied(resolved, id => values[id]).Should().BeTrue();
        values[3] = 3;
        GroupComparisonLogic.IsSatisfied(resolved, id => values[id]).Should().BeFalse();
    }

    [Fact]
    public void Large_values_do_not_overflow()
    {
        var puzzle = new Puzzle("Groß", GridType.Square, new ValueDomain(int.MaxValue - 3, int.MaxValue));
        for (var i = 1; i <= 3; i++)
        {
            puzzle.Nodes.Add(new Node(i, i, 0));
        }

        puzzle.Groups.Add(new Group(1, [1, 2], "AllDifferent"));
        puzzle.Groups.Add(new Group(2, [3], "AllDifferent"));
        var resolved = GroupComparisonLogic.Resolve(puzzle, new GroupComparison(1, 1, 2, ComparisonOperator.Less))!;

        // Zwei große Werte sind immer größer als einer: alles wird gestrichen.
        var changes = Eliminations(GroupComparisonLogic.Propagate(CandidateState.FromPuzzle(puzzle), resolved));
        changes[1].Should().HaveCount(4);
        changes[3].Should().HaveCount(4);
        GroupComparisonLogic.IsSatisfied(resolved, _ => int.MaxValue).Should().BeFalse();
    }

    /// <summary>
    /// Korrektheit gegen vollständiges Durchprobieren: Kein Wert, der in einer erfüllenden Belegung der beteiligten
    /// Knoten vorkommt, wird gestrichen. Zufällige kleine Gruppen (auch mit gemeinsamen Knoten) und Kandidatenmengen.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllOperators))]
    public void Propagation_is_sound_against_brute_force(ComparisonOperator op)
    {
        var random = new Random(1000 + (int)op);
        var checkedCases = 0;
        var removed = 0;
        for (var round = 0; round < 400; round++)
        {
            var (puzzle, resolved, state) = RandomCase(random, op);
            if (resolved.Terms.Count == 0)
            {
                continue;
            }

            var nodes = resolved.NodeIds;
            var supported = new HashSet<(int Node, int Value)>();
            foreach (var assignment in Assignments(state, nodes))
            {
                if (GroupComparisonLogic.IsSatisfied(resolved, id => assignment[id]))
                {
                    foreach (var (node, value) in assignment)
                    {
                        supported.Add((node, value));
                    }
                }
            }

            var changes = GroupComparisonLogic.Propagate(state, resolved);
            foreach (var change in changes)
            {
                change.Kind.Should().Be(CandidateChangeKind.Eliminate);
                foreach (var value in change.Values)
                {
                    state.GetCandidates(change.NodeId).Should().Contain(value);
                    removed++;
                    supported.Should().NotContain((change.NodeId, value),
                        $"Knoten {change.NodeId} = {value} kommt in einer erfüllenden Belegung vor ({Describe(puzzle, state, resolved)})");
                }
            }

            checkedCases++;
        }

        checkedCases.Should().BeGreaterThan(300);
        if (op != ComparisonOperator.NotEqual)
        {
            removed.Should().BeGreaterThan(0, "die Fälle sollen auch Streichungen enthalten");
        }
    }

    /// <summary>
    /// Vollständigkeit auf vollständigen Belegungen: Ist jeder Knoten festgelegt, streicht die Propagation genau dann
    /// etwas, wenn der Vergleich verletzt ist.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllOperators))]
    public void Propagation_is_complete_on_full_assignments(ComparisonOperator op)
    {
        var random = new Random(2000 + (int)op);
        var violated = 0;
        var satisfied = 0;
        for (var round = 0; round < 300; round++)
        {
            var (puzzle, resolved, state) = RandomCase(random, op);
            if (resolved.Terms.Count == 0)
            {
                continue;
            }

            foreach (var assignment in Assignments(state, resolved.NodeIds))
            {
                var fixedState = state.Clone();
                foreach (var (node, value) in assignment)
                {
                    fixedState.Assign(node, value);
                }

                var changes = GroupComparisonLogic.Propagate(fixedState, resolved);
                if (GroupComparisonLogic.IsSatisfied(resolved, id => assignment[id]))
                {
                    changes.Should().BeEmpty(Describe(puzzle, fixedState, resolved));
                    satisfied++;
                }
                else
                {
                    changes.Select(c => c.NodeId).Should().Equal(resolved.Terms.Select(t => t.NodeId),
                        $"die Belegung verletzt den Vergleich ({Describe(puzzle, fixedState, resolved)})");
                    foreach (var change in changes)
                    {
                        fixedState.Apply(change);
                    }

                    fixedState.HasContradiction.Should().BeTrue();
                    violated++;
                }
            }
        }

        violated.Should().BeGreaterThan(50);
        satisfied.Should().BeGreaterThan(50);
    }

    /// <summary>
    /// Gemeinsame Knoten systematisch: Linke und rechte Gruppe teilen sich jeweils mindestens einen Knoten, auf kleinem
    /// Wertebereich mit negativen Werten. Prüft Korrektheit und Vollständigkeit für alle Operatoren.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllOperators))]
    public void Shared_nodes_are_handled_soundly_and_completely(ComparisonOperator op)
    {
        int[][] lefts = [[1, 2], [1, 2, 3], [2], [1, 3, 4]];
        int[][] rights = [[2, 3], [2], [1, 2, 4], [3, 4], [4, 1]];
        foreach (var left in lefts)
        {
            foreach (var right in rights)
            {
                if (!left.Intersect(right).Any())
                {
                    continue;
                }

                var (puzzle, resolved) = Create(left, right, op, min: -1, max: 2);
                if (resolved.Terms.Count == 0)
                {
                    continue;
                }

                var state = CandidateState.FromPuzzle(puzzle);
                var supported = new HashSet<(int, int)>();
                foreach (var assignment in Assignments(state, resolved.NodeIds))
                {
                    var ok = GroupComparisonLogic.IsSatisfied(resolved, id => assignment[id]);
                    if (ok)
                    {
                        supported.UnionWith(assignment.Select(p => (p.Key, p.Value)));
                    }

                    var fixedState = state.Clone();
                    foreach (var (node, value) in assignment)
                    {
                        fixedState.Assign(node, value);
                    }

                    // Verletzt: Jeder Summand verliert seinen einzigen Wert; gemeinsame Knoten bleiben unberührt.
                    GroupComparisonLogic.Propagate(fixedState, resolved).Select(c => c.NodeId)
                        .Should().Equal(ok ? [] : resolved.Terms.Select(t => t.NodeId));
                }

                foreach (var change in GroupComparisonLogic.Propagate(state, resolved))
                {
                    foreach (var value in change.Values)
                    {
                        supported.Should().NotContain((change.NodeId, value));
                    }
                    resolved.SharedNodeIds.Should().NotContain(change.NodeId, "gemeinsame Knoten heben sich auf");
                }
            }
        }
    }

    /// <summary>Zufälliger kleiner Fall: bis zu 5 Knoten (Werte −2..3), Gruppen mit 1–3 Knoten, zufällige Kandidaten.</summary>
    private static (Puzzle Puzzle, ResolvedGroupComparison Resolved, CandidateState State) RandomCase(Random random, ComparisonOperator op)
    {
        const int nodeCount = 5;
        int[] RandomGroup() => [.. Enumerable.Range(1, nodeCount).OrderBy(_ => random.Next()).Take(random.Next(1, 4))];
        var (puzzle, resolved) = Create(RandomGroup(), RandomGroup(), op, nodeCount, min: -2, max: 3);
        var state = CandidateState.FromPuzzle(puzzle);
        foreach (var node in resolved.NodeIds)
        {
            var keep = state.GetCandidates(node).Where(_ => random.Next(3) > 0).ToList();
            if (keep.Count == 0)
            {
                keep.Add(state.GetCandidates(node).ElementAt(random.Next(6)));
            }

            foreach (var value in state.GetCandidates(node).Except(keep).ToList())
            {
                state.Remove(node, value);
            }
        }

        return (puzzle, resolved, state);
    }

    /// <summary>Alle Belegungen der Knoten aus ihren aktuellen Kandidaten.</summary>
    private static IEnumerable<Dictionary<int, int>> Assignments(CandidateState state, IReadOnlyList<int> nodes)
    {
        IEnumerable<Dictionary<int, int>> result = [[]];
        foreach (var node in nodes)
        {
            var candidates = state.GetCandidates(node).ToList();
            result = result.SelectMany(partial => candidates.Select(value => new Dictionary<int, int>(partial) { [node] = value }));
        }

        return result;
    }

    private static string Describe(Puzzle puzzle, CandidateState state, ResolvedGroupComparison resolved) =>
        $"links [{string.Join(", ", resolved.Left.Nodes)}], rechts [{string.Join(", ", resolved.Right.Nodes)}], "
        + string.Join("; ", resolved.NodeIds.Select(id => $"{id}: {state.GetCandidates(id)}"))
        + $" ({puzzle.GroupComparisons[0]})";
}
