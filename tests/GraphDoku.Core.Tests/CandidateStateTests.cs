using GraphDoku.Core.Candidates;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Tests;

public class CandidateStateTests
{
    [Fact]
    public void Starts_from_domains_and_givens()
    {
        var puzzle = TestPuzzles.WithNodes(3);
        puzzle.Nodes[1].Given = 4;
        puzzle.Nodes[2].Domain = new ValueDomain(2, 3);

        var state = CandidateState.FromPuzzle(puzzle);

        state.GetCandidates(1).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
        state.GetCandidates(2).Should().Equal(4);
        state.GetValue(2).Should().Be(4);
        state.IsFixed(2).Should().BeTrue();
        state.GetCandidates(3).Should().Equal(2, 3);
        state.GetValue(3).Should().BeNull();
        state.IsSolved.Should().BeFalse();
        state.HasContradiction.Should().BeFalse();
    }

    [Fact]
    public void Given_outside_domain_yields_contradiction()
    {
        var puzzle = TestPuzzles.WithNodes(1);
        puzzle.Nodes[0].Given = 12;

        CandidateState.FromPuzzle(puzzle).HasContradiction.Should().BeTrue();
    }

    [Fact]
    public void Remove_assign_and_apply()
    {
        var state = CandidateState.FromPuzzle(TestPuzzles.WithNodes(2, 1, 4));
        var reason = new Reason(ConstraintRef.ForEdge(1), [1, 2], "Test");

        state.Remove(1, 4).Should().BeTrue();
        state.Remove(1, 4).Should().BeFalse();
        state.Apply(CandidateChange.Eliminate(1, [1, 2], reason)).Should().BeTrue();
        state.GetCandidates(1).Should().Equal(3);
        state.Apply(CandidateChange.Assign(2, 2, reason)).Should().BeTrue();
        state.GetCandidates(2).Should().Equal(2);
        state.IsSolved.Should().BeTrue();
        state.Invoking(s => s.Assign(1, 4)).Should().Throw<InvalidOperationException>().WithMessage("*kein Kandidat*");
    }

    [Fact]
    public void Clone_is_independent()
    {
        var state = CandidateState.FromPuzzle(TestPuzzles.WithNodes(1, 1, 3));
        var copy = state.Clone();
        copy.Remove(1, 1);

        state.GetCandidates(1).Should().HaveCount(3);
        copy.GetCandidates(1).Should().HaveCount(2);
    }

    [Fact]
    public void Unknown_node_throws_german_message()
    {
        var state = CandidateState.FromPuzzle(TestPuzzles.WithNodes(1));
        state.Invoking(s => s.GetCandidates(99)).Should().Throw<KeyNotFoundException>().WithMessage("Knoten 99*");
    }

    [Fact]
    public void Clone_shares_unchanged_sets_and_is_cheap()
    {
        var state = CandidateState.FromPuzzle(TestPuzzles.WithNodes(3));
        var copy = state.Clone();

        copy.GetCandidates(2).Should().BeSameAs(state.GetCandidates(2), "unveränderliche Mengen werden geteilt");
        copy.Remove(2, 5).Should().BeTrue();
        copy.GetCandidates(2).Should().NotBeSameAs(state.GetCandidates(2));
        state.GetCandidates(2).Should().Contain(5);
        copy.HasSameCandidates(state).Should().BeFalse();
        state.Clone().HasSameCandidates(state).Should().BeTrue();
    }

    [Fact]
    public void Returned_candidate_sets_do_not_change_afterwards()
    {
        var state = CandidateState.FromPuzzle(TestPuzzles.WithNodes(1, 1, 3));
        var before = state.GetCandidates(1);

        state.Assign(1, 2);

        before.Should().Equal(1, 2, 3);
        state.GetCandidates(1).Should().Equal(2);
    }

    [Fact]
    public void Node_ids_keep_puzzle_order_and_apply_reports_changes()
    {
        var puzzle = TestPuzzles.WithNodes(0);
        puzzle.Nodes.Add(new Node(7, 0, 0));
        puzzle.Nodes.Add(new Node(3, 1, 0, given: 2));
        var state = CandidateState.FromPuzzle(puzzle);
        var reason = new Reason(null, [7], "Test");

        state.NodeIds.Should().Equal(7, 3);
        state.Apply(CandidateChange.Assign(3, 2, reason)).Should().BeFalse("der Knoten hat bereits nur diesen Wert");
        state.Apply(CandidateChange.Eliminate(7, [10, 11], reason)).Should().BeFalse("die Werte sind keine Kandidaten");
        state.Apply(CandidateChange.Eliminate(7, [1, 10], reason)).Should().BeTrue();
        state.GetCandidates(7).Should().Equal(2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Fact]
    public void Reason_without_constraint_prints_only_the_text()
    {
        new Reason(null, [1], "Knoten 1 hat nur noch den Kandidaten 5.").ToString()
            .Should().Be("Knoten 1 hat nur noch den Kandidaten 5.");
        new Reason(ConstraintRef.ForGroup(2), [1], "Text").ToString().Should().Be("Gruppe 2: Text");
    }
}
