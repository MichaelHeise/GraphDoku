using GraphDoku.Core.Model;

namespace GraphDoku.Core.Tests;

/// <summary>Modell der Gruppenvergleiche: Operatoren, Element, Verweise, Ids, Kopie und Strukturvergleich.</summary>
public class GroupComparisonModelTests
{
    [Fact]
    public void Operator_names_round_trip_in_enum_order()
    {
        ComparisonOperators.Names.Should().Equal("=", "!=", "<", "<=", ">", ">=");
        ComparisonOperators.All.Should().Equal(Enum.GetValues<ComparisonOperator>());
        foreach (var op in Enum.GetValues<ComparisonOperator>())
        {
            ComparisonOperators.TryParse(ComparisonOperators.ToName(op), out var parsed).Should().BeTrue();
            parsed.Should().Be(op);
            ComparisonOperators.Names[(int)op].Should().Be(ComparisonOperators.ToName(op));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("==")]
    [InlineData("≤")]
    [InlineData("≠")]
    [InlineData(" <")]
    [InlineData("=<")]
    [InlineData("le")]
    public void Unknown_operator_names_are_rejected(string? name)
    {
        ComparisonOperators.TryParse(name, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(ComparisonOperator.Equal, "=", "gleich")]
    [InlineData(ComparisonOperator.NotEqual, "≠", "ungleich")]
    [InlineData(ComparisonOperator.Less, "<", "kleiner als")]
    [InlineData(ComparisonOperator.LessOrEqual, "≤", "kleiner oder gleich")]
    [InlineData(ComparisonOperator.Greater, ">", "größer als")]
    [InlineData(ComparisonOperator.GreaterOrEqual, "≥", "größer oder gleich")]
    public void Operators_have_symbols_and_german_texts(ComparisonOperator op, string symbol, string text)
    {
        ComparisonOperators.ToSymbol(op).Should().Be(symbol);
        ComparisonOperators.ToGermanText(op).Should().Be(text);
    }

    [Theory]
    [InlineData(ComparisonOperator.Equal, false, true, false)]
    [InlineData(ComparisonOperator.NotEqual, true, false, true)]
    [InlineData(ComparisonOperator.Less, true, false, false)]
    [InlineData(ComparisonOperator.LessOrEqual, true, true, false)]
    [InlineData(ComparisonOperator.Greater, false, false, true)]
    [InlineData(ComparisonOperator.GreaterOrEqual, false, true, true)]
    public void Holds_compares_left_with_right(ComparisonOperator op, bool whenLess, bool whenEqual, bool whenGreater)
    {
        ComparisonOperators.Holds(op, 3, 4).Should().Be(whenLess);
        ComparisonOperators.Holds(op, 4, 4).Should().Be(whenEqual);
        ComparisonOperators.Holds(op, 5, 4).Should().Be(whenGreater);
        ComparisonOperators.Holds(op, long.MinValue, long.MaxValue).Should().Be(whenLess);
    }

    [Fact]
    public void Undefined_operators_throw_in_conversions()
    {
        var undefined = (ComparisonOperator)42;
        FluentActions.Invoking(() => ComparisonOperators.ToName(undefined)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ComparisonOperators.ToSymbol(undefined)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ComparisonOperators.ToGermanText(undefined)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ComparisonOperators.Holds(undefined, 1, 2)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Comparison_has_ref_text_and_independent_clone()
    {
        var comparison = new GroupComparison(3, 2, 5, ComparisonOperator.LessOrEqual);

        comparison.Ref.Should().Be(ConstraintRef.ForGroupComparison(3));
        comparison.Ref.Kind.Should().Be(ConstraintKind.GroupComparison);
        comparison.Ref.ToString().Should().Be("Gruppenvergleich 3");
        comparison.ToString().Should().Be("Gruppenvergleich 3 (Gruppe 2 ≤ Gruppe 5)");
        new GroupComparison(1, 1, 2).Operator.Should().Be(ComparisonOperator.Equal);
        new GroupComparison(1, 1, 2, (ComparisonOperator)42).ToString().Should().Be("Gruppenvergleich 1 (Gruppe 1 ? Gruppe 2)");

        var copy = comparison.Clone();
        copy.Operator = ComparisonOperator.Greater;
        copy.Left = 9;
        comparison.Operator.Should().Be(ComparisonOperator.LessOrEqual);
        comparison.Left.Should().Be(2);
        copy.Id.Should().Be(3);
        copy.Right.Should().Be(5);
    }

    [Fact]
    public void ConstraintRef_texts_stay_the_same_for_edges_and_groups()
    {
        ConstraintRef.ForEdge(4).ToString().Should().Be("Kante 4");
        ConstraintRef.ForGroup(4).ToString().Should().Be("Gruppe 4");
        ConstraintRef.ForGroupComparison(4).ToString().Should().Be("Gruppenvergleich 4");
        ConstraintRef.ForGroupComparison(4).Should().NotBe(ConstraintRef.ForGroup(4));
    }

    [Fact]
    public void FindConstraint_does_not_resolve_comparisons_as_groups()
    {
        var puzzle = TestPuzzles.WithNodes(2);
        puzzle.Groups.Add(new Group(1, [1], "Sum", TestPuzzles.Params(("target", 3))));
        puzzle.Groups.Add(new Group(2, [2], "Sum", TestPuzzles.Params(("target", 3))));
        puzzle.GroupComparisons.Add(new GroupComparison(1, 1, 2));

        puzzle.FindConstraint(ConstraintRef.ForGroupComparison(1)).Should().BeNull();
        puzzle.FindConstraint(ConstraintRef.ForGroup(1)).Should().BeSameAs(puzzle.Groups[0]);
        puzzle.FindGroupComparison(1).Should().BeSameAs(puzzle.GroupComparisons[0]);
        puzzle.FindGroupComparison(2).Should().BeNull();
        puzzle.Constraints.Should().HaveCount(2, "Gruppenvergleiche gehören nicht zu den Constraints der Registry");
    }

    [Fact]
    public void NextGroupComparisonId_has_its_own_sequence()
    {
        var puzzle = TestPuzzles.WithNodes(2);
        puzzle.NextGroupComparisonId().Should().Be(1);
        puzzle.Groups.Add(new Group(7, [1], "Sum"));
        puzzle.NextGroupComparisonId().Should().Be(1);

        puzzle.GroupComparisons.Add(new GroupComparison(4, 7, 8));
        puzzle.NextGroupComparisonId().Should().Be(5);
        puzzle.NextGroupId().Should().Be(8);
    }

    [Fact]
    public void Clone_copies_comparisons_deeply_and_the_comparer_reports_their_differences()
    {
        var puzzle = TestPuzzles.WithNodes(2);
        puzzle.Groups.Add(new Group(1, [1], "Sum"));
        puzzle.Groups.Add(new Group(2, [2], "Sum"));
        puzzle.GroupComparisons.Add(new GroupComparison(1, 1, 2, ComparisonOperator.Less));

        var copy = puzzle.Clone();
        PuzzleComparer.AreEqual(puzzle, copy).Should().BeTrue();
        copy.GroupComparisons[0].Should().NotBeSameAs(puzzle.GroupComparisons[0]);

        copy.GroupComparisons[0].Operator = ComparisonOperator.GreaterOrEqual;
        copy.GroupComparisons[0].Left = 2;
        copy.GroupComparisons[0].Right = 1;
        copy.GroupComparisons[0].Id = 6;

        puzzle.GroupComparisons[0].Operator.Should().Be(ComparisonOperator.Less);
        PuzzleComparer.Differences(puzzle, copy).Should().Equal(
            "groupComparisons[0].id: 1 ≠ 6",
            "groupComparisons[0].left: 1 ≠ 2",
            "groupComparisons[0].right: 2 ≠ 1",
            "groupComparisons[0].op: Less ≠ GreaterOrEqual");

        copy.GroupComparisons.Add(new GroupComparison(2, 1, 2));
        PuzzleComparer.Differences(puzzle, copy).Should().ContainSingle()
            .Which.Should().Be("groupComparisons: 1 Einträge ≠ 2 Einträge");
    }
}
