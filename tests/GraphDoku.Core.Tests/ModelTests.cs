using GraphDoku.Core.Model;

namespace GraphDoku.Core.Tests;

public class ModelTests
{
    [Fact]
    public void EffectiveDomain_uses_node_override_or_global_domain()
    {
        var puzzle = TestPuzzles.WithNodes(2);
        puzzle.Nodes[1].Domain = new ValueDomain(0, 3);

        puzzle.GetEffectiveDomain(puzzle.Nodes[0]).Should().Be(new ValueDomain(1, 9));
        puzzle.GetEffectiveDomain(puzzle.Nodes[1]).Should().Be(new ValueDomain(0, 3));
    }

    [Fact]
    public void NextIds_are_sequential_per_element_type()
    {
        var puzzle = TestPuzzles.WithNodes(3);
        puzzle.NextNodeId().Should().Be(4);
        puzzle.NextEdgeId().Should().Be(1);
        puzzle.NextGroupId().Should().Be(1);

        puzzle.Edges.Add(new Edge(puzzle.NextEdgeId(), 1, 2, "NotEqual"));
        puzzle.Groups.Add(new Group(puzzle.NextGroupId(), [1, 2, 3], "AllDifferent"));

        puzzle.NextEdgeId().Should().Be(2);
        puzzle.NextGroupId().Should().Be(2);
    }

    [Fact]
    public void Clone_is_deep_and_structurally_equal()
    {
        var puzzle = TestPuzzles.WithNodes(3);
        puzzle.Nodes[0].Given = 4;
        puzzle.Edges.Add(new Edge(1, 1, 2, "Difference", TestPuzzles.Params(("k", 1))));
        puzzle.Groups.Add(new Group(1, [1, 2, 3], "AllDifferent", color: "#ff0000", label: "G"));

        var copy = puzzle.Clone();
        PuzzleComparer.AreEqual(puzzle, copy).Should().BeTrue();

        copy.Nodes[0].Given = 5;
        copy.Edges[0].Params.Set("k", 2);
        copy.Groups[0].Nodes.Add(4);

        puzzle.Nodes[0].Given.Should().Be(4);
        puzzle.Edges[0].Params.GetInt("k", 0).Should().Be(1);
        puzzle.Groups[0].Nodes.Should().Equal(1, 2, 3);
        PuzzleComparer.Differences(puzzle, copy).Should().HaveCount(3);
    }

    [Fact]
    public void Constraints_expose_node_ids_and_refs()
    {
        var edge = new Edge(7, 3, 5, "LessThan");
        var group = new Group(2, [4, 1, 9], "Thermometer");

        IConstraint e = edge;
        IConstraint g = group;
        e.NodeIds.Should().Equal(3, 5);
        e.Ref.Should().Be(ConstraintRef.ForEdge(7));
        g.NodeIds.Should().Equal(4, 1, 9);
        g.Ref.ToString().Should().Be("Gruppe 2");
    }

    [Fact]
    public void FindConstraint_resolves_edges_and_groups()
    {
        var puzzle = TestPuzzles.WithNodes(2);
        puzzle.Edges.Add(new Edge(1, 1, 2, "NotEqual"));
        puzzle.Groups.Add(new Group(1, [1, 2], "AllDifferent"));

        puzzle.FindConstraint(ConstraintRef.ForEdge(1)).Should().BeSameAs(puzzle.Edges[0]);
        puzzle.FindConstraint(ConstraintRef.ForGroup(1)).Should().BeSameAs(puzzle.Groups[0]);
        puzzle.FindConstraint(ConstraintRef.ForGroup(2)).Should().BeNull();
    }

    [Theory]
    [InlineData(1, 9, 9)]
    [InlineData(0, 15, 16)]
    [InlineData(3, 3, 1)]
    [InlineData(5, 4, 0)]
    public void ValueDomain_size_and_values(int min, int max, int size)
    {
        var domain = new ValueDomain(min, max);
        domain.Size.Should().Be(size);
        domain.Values.Should().HaveCount(size);
        domain.IsValid.Should().Be(size > 0);
    }

    [Fact]
    public void GridTypeNames_round_trip()
    {
        foreach (var grid in Enum.GetValues<GridType>())
        {
            GridTypeNames.TryParse(GridTypeNames.ToName(grid), out var parsed).Should().BeTrue();
            parsed.Should().Be(grid);
        }

        GridTypeNames.TryParse("Square", out _).Should().BeFalse();
    }

    [Fact]
    public void PuzzleComparer_reports_differences_readably()
    {
        var a = TestPuzzles.WithNodes(2);
        var b = TestPuzzles.WithNodes(2);
        b.Name = "Anders";
        b.Nodes[1].X = 9;

        PuzzleComparer.Differences(a, b).Should().BeEquivalentTo("name: Test ≠ Anders", "nodes[1].x: 1 ≠ 9");
    }
}
