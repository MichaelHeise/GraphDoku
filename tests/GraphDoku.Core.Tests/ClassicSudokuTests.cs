using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;

namespace GraphDoku.Core.Tests;

public class ClassicSudokuTests
{
    private const string Givens =
        "53..7...." +
        "6..195..." +
        ".98....6." +
        "8...6...3" +
        "4..8.3..1" +
        "7...2...6" +
        ".6....28." +
        "...419..5" +
        "....8..79";

    [Fact]
    public void Creates_81_nodes_on_square_grid()
    {
        var puzzle = ClassicSudoku.Create();

        puzzle.Name.Should().Be("Klassisch 9x9");
        puzzle.Grid.Should().Be(GridType.Square);
        puzzle.Domain.Should().Be(new ValueDomain(1, 9));
        puzzle.Nodes.Should().HaveCount(81);
        puzzle.Nodes.Select(n => n.Id).Should().Equal(Enumerable.Range(1, 81));
        puzzle.Nodes.Should().OnlyContain(n => n.Id == (n.Y * 9) + n.X + 1 && n.Given == null && n.Domain == null);
        puzzle.Edges.Should().BeEmpty();
    }

    [Fact]
    public void Creates_27_labelled_all_different_groups()
    {
        var puzzle = ClassicSudoku.Create();

        puzzle.Groups.Should().HaveCount(27);
        puzzle.Groups.Select(g => g.Id).Should().Equal(Enumerable.Range(1, 27));
        puzzle.Groups.Should().OnlyContain(g => g.Rule == "AllDifferent" && g.Nodes.Count == 9 && g.Nodes.Distinct().Count() == 9);
        puzzle.Groups[0].Label.Should().Be("Zeile 1");
        puzzle.Groups[9].Label.Should().Be("Spalte 1");
        puzzle.Groups[18].Label.Should().Be("Block 1");
        puzzle.Groups[26].Label.Should().Be("Block 9");
        puzzle.Groups[0].Nodes.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
        puzzle.Groups[9].Nodes.Should().Equal(1, 10, 19, 28, 37, 46, 55, 64, 73);
        puzzle.Groups[19].Nodes.Should().Equal(4, 5, 6, 13, 14, 15, 22, 23, 24);
    }

    [Fact]
    public void Every_node_is_in_exactly_one_row_column_and_block()
    {
        var puzzle = ClassicSudoku.Create();
        foreach (var node in puzzle.Nodes)
        {
            puzzle.Groups.Count(g => g.Nodes.Contains(node.Id)).Should().Be(3);
        }
    }

    [Fact]
    public void Givens_string_is_applied_row_by_row()
    {
        var puzzle = ClassicSudoku.Create(Givens);

        puzzle.Nodes.Count(n => n.Given != null).Should().Be(30);
        puzzle.FindNode(ClassicSudoku.NodeId(0, 0))!.Given.Should().Be(5);
        puzzle.FindNode(ClassicSudoku.NodeId(0, 2))!.Given.Should().BeNull();
        puzzle.FindNode(ClassicSudoku.NodeId(8, 8))!.Given.Should().Be(9);
    }

    [Fact]
    public void Givens_may_contain_whitespace_and_zeros()
    {
        var withZeros = Givens.Replace('.', '0');
        var spaced = string.Join("\n", Enumerable.Range(0, 9).Select(r => withZeros.Substring(r * 9, 9)));

        PuzzleComparer.AreEqual(ClassicSudoku.Create(spaced), ClassicSudoku.Create(Givens)).Should().BeTrue();
    }

    [Theory]
    [InlineData("123")]
    [InlineData("")]
    public void Givens_with_wrong_length_are_rejected(string givens)
    {
        var act = () => ClassicSudoku.Create(givens);
        act.Should().Throw<ArgumentException>().WithMessage("Die Vorgaben müssen genau 81 Zeichen umfassen*");
    }

    [Fact]
    public void Givens_with_invalid_characters_are_rejected()
    {
        var act = () => ClassicSudoku.Create("x" + new string('.', 80));
        act.Should().Throw<ArgumentException>().WithMessage("Ungültiges Zeichen „x“ an Position 1*");
    }
}
