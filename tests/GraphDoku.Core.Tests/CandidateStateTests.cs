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
}
