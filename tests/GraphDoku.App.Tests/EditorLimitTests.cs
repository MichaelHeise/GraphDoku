using System.Diagnostics;
using System.Globalization;
using GraphDoku.App.Editing;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;

namespace GraphDoku.App.Tests;

/// <summary>
/// Grenzfälle aus ungeprüft geöffneten Dateien (Abschluss-Review): Ids an der int-Grenze (Nr. 166) und riesige
/// Wertebereiche bei der Werteingabe (Nr. 167).
/// </summary>
public class EditorLimitTests
{
    private static Puzzle ThreeNodes(int firstNodeId = 1)
    {
        var puzzle = new Puzzle("Grenzen");
        puzzle.Nodes.Add(new Node(firstNodeId, 0, 0));
        puzzle.Nodes.Add(new Node(firstNodeId == int.MaxValue ? 2 : firstNodeId + 1, 1, 0));
        puzzle.Nodes.Add(new Node(firstNodeId == int.MaxValue ? 3 : firstNodeId + 2, 2, 0));
        return puzzle;
    }

    [Theory]
    [InlineData(new int[0], 1)]
    [InlineData(new[] { -5, 0 }, 1)]
    [InlineData(new[] { 3, 7, 2 }, 8)]
    [InlineData(new[] { int.MaxValue - 1 }, int.MaxValue)]
    public void Next_free_id_is_largest_plus_one_and_at_least_one(int[] ids, int expected) =>
        EditorViewModel.NextFreeId(ids).Should().Be(expected);

    [Fact]
    public void Next_free_id_does_not_overflow() =>
        EditorViewModel.NextFreeId([5, int.MaxValue]).Should().BeNull();

    [Fact]
    public void Placing_nodes_after_id_int_max_reports_instead_of_creating_duplicate_negative_ids()
    {
        var editor = EditorDriver.CreateEditor(ThreeNodes(int.MaxValue));
        editor.Tool = EditorTool.PlaceNode;

        editor.Click(new GridPoint(0, 2));
        editor.Click(new GridPoint(1, 2));

        editor.Puzzle.Nodes.Select(n => n.Id).Should().Equal(int.MaxValue, 2, 3);
        editor.StatusMessage.Should().Contain("Keine freie Knoten-Id mehr").And.Contain("2147483647");
        editor.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Placing_a_node_next_to_invalid_ids_uses_id_one()
    {
        var puzzle = new Puzzle("Ungültig");
        puzzle.Nodes.Add(new Node(-5, 0, 0));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.Tool = EditorTool.PlaceNode;

        editor.Click(new GridPoint(2, 0));

        editor.Puzzle.Nodes.Select(n => n.Id).Should().Equal(-5, 1);
    }

    [Fact]
    public void Drawing_edges_after_edge_id_int_max_reports_instead_of_creating_duplicate_ids()
    {
        var puzzle = ThreeNodes();
        puzzle.Edges.Add(new Edge(int.MaxValue, 1, 2, "NotEqual"));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.Tool = EditorTool.DrawEdge;
        editor.NewEdgeRule = editor.EdgeRules.Single(r => r.TypeName == "NotEqual");

        editor.DragGrid(new GridPoint(1, 0), new GridPoint(2, 0));
        editor.DragGrid(new GridPoint(0, 0), new GridPoint(2, 0));

        editor.Puzzle.Edges.Select(e => e.Id).Should().Equal(int.MaxValue);
        editor.StatusMessage.Should().Contain("Keine freie Kanten-Id mehr");
        editor.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Creating_a_group_after_group_id_int_max_reports_instead_of_overflowing()
    {
        var puzzle = ThreeNodes();
        puzzle.Groups.Add(new Group(int.MaxValue, [1, 2], "AllDifferent"));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.SelectNodes([2, 3]);

        editor.CreateGroupFromSelection();

        editor.Puzzle.Groups.Select(g => g.Id).Should().Equal(int.MaxValue);
        editor.StatusMessage.Should().Contain("Keine freie Gruppen-Id mehr");
    }

    [Fact]
    public void Typing_a_digit_with_a_huge_unchecked_domain_returns_at_once()
    {
        var puzzle = ThreeNodes();
        puzzle.Domain = new ValueDomain(1_000_000_000, int.MaxValue);
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.SelectNodes([1]);
        var watch = Stopwatch.StartNew();

        editor.EnterValueCharacter('0').Should().BeTrue();
        editor.StatusMessage.Should().Contain("außerhalb des Wertebereichs");
        editor.Type("2000000000");

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        editor.Puzzle.FindNode(1)!.Given.Should().Be(2_000_000_000);
    }

    public static TheoryData<int, int, int, int> Domains => new()
    {
        { 1, 9, 1, 9 },
        { 1, 16, 1, 16 },
        { -20, 15, -20, 15 },
        { 0, 120, 5, 105 },
        { -130, -7, -200, 0 },
        { 95, 1003, 1, 2000 },
        { 3, 3, 1, 9 },
        { 1, 9, 10, 20 },
    };

    [Theory]
    [MemberData(nameof(Domains))]
    public void Prefix_check_matches_enumerating_all_values(int min1, int max1, int min2, int max2)
    {
        List<ValueDomain> domains = [new(min1, max1), new(min2, max2)];
        string[] texts = ["-", "0", "-0", "1", "2", "9", "10", "12", "-1", "-12", "-2", "100", "15", "16", "20", "1-", "--"];
        foreach (var text in texts)
        {
            var expected = domains[0].Values.Any(v =>
            {
                var s = v.ToString(CultureInfo.InvariantCulture);
                return s.Length > text.Length && s.StartsWith(text, StringComparison.Ordinal) && domains.All(d => d.Contains(v));
            });
            EditorViewModel.IsPrefixOfDomainValue(text, domains).Should().Be(expected, $"Präfix „{text}“ bei {min1}..{max1} ∩ {min2}..{max2}");
        }
    }

    [Theory]
    [InlineData("-", true)]
    [InlineData("-2", true)]
    [InlineData("-214748364", true)]
    [InlineData("-2147483648", false)]
    [InlineData("214748364", true)]
    [InlineData("2147483647", false)]
    [InlineData("3", true)]
    public void Prefix_check_handles_the_full_int_range(string text, bool expected) =>
        EditorViewModel.IsPrefixOfDomainValue(text, [new ValueDomain(int.MinValue, int.MaxValue)]).Should().Be(expected);
}
