using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;
using GraphDoku.Core.Validation;

namespace GraphDoku.Core.Tests;

public class PuzzleValidatorTests
{
    [Fact]
    public void Classic_sudoku_is_valid()
    {
        PuzzleValidator.Validate(ClassicSudoku.Create()).Should().BeEmpty();
    }

    [Fact]
    public void Invalid_global_domain_is_reported()
    {
        var puzzle = TestPuzzles.WithNodes(1, 9, 1);
        PuzzleValidator.Validate(puzzle).Should().Contain("Der globale Wertebereich ist ungültig: min (9) ist größer als max (1).");
    }

    [Fact]
    public void Too_large_domain_is_reported()
    {
        var puzzle = TestPuzzles.WithNodes(1, 0, 100_000);
        PuzzleValidator.Validate(puzzle).Should().ContainSingle().Which.Should().Contain("zu groß");
    }

    [Fact]
    public void Duplicate_and_non_positive_ids_are_reported()
    {
        var puzzle = TestPuzzles.WithNodes(2);
        puzzle.Nodes.Add(new Node(2, 5, 5));
        puzzle.Nodes.Add(new Node(0, 6, 6));
        puzzle.Edges.Add(new Edge(1, 1, 2, "NotEqual"));
        puzzle.Edges.Add(new Edge(1, 2, 1, "NotEqual"));
        puzzle.Groups.Add(new Group(4, [1, 2], "AllDifferent"));
        puzzle.Groups.Add(new Group(4, [1, 2], "AllDifferent"));

        PuzzleValidator.Validate(puzzle).Should().BeEquivalentTo(
            "Die Knoten-Id 2 ist mehrfach vergeben.",
            "Knoten-Id 0 ist ungültig: Ids müssen positive Ganzzahlen sein.",
            "Die Kanten-Id 1 ist mehrfach vergeben.",
            "Die Gruppen-Id 4 ist mehrfach vergeben.");
    }

    [Fact]
    public void Given_outside_effective_domain_is_reported()
    {
        var puzzle = TestPuzzles.WithNodes(2);
        puzzle.Nodes[0].Given = 10;
        puzzle.Nodes[1].Domain = new ValueDomain(10, 12);
        puzzle.Nodes[1].Given = 10;

        PuzzleValidator.Validate(puzzle).Should().ContainSingle()
            .Which.Should().Be("Knoten 1: Der vorgegebene Wert 10 liegt außerhalb des Wertebereichs 1..9.");
    }

    [Fact]
    public void Invalid_node_domain_is_reported()
    {
        var puzzle = TestPuzzles.WithNodes(1);
        puzzle.Nodes[0].Domain = new ValueDomain(3, 2);
        PuzzleValidator.Validate(puzzle).Should().ContainSingle().Which.Should().StartWith("Knoten 1: Der eigene Wertebereich ist ungültig");
    }

    [Fact]
    public void Dangling_node_ids_are_reported()
    {
        var puzzle = TestPuzzles.WithNodes(2);
        puzzle.Edges.Add(new Edge(1, 1, 99, "NotEqual"));
        puzzle.Groups.Add(new Group(1, [1, 2, 42], "AllDifferent", label: "Käfig"));

        PuzzleValidator.Validate(puzzle).Should().BeEquivalentTo(
            "Kante 1: Der Zielknoten 99 existiert nicht.",
            "Gruppe 1 „Käfig“: Der Knoten 42 existiert nicht.");
    }

    [Fact]
    public void Self_loops_and_duplicate_group_members_are_reported()
    {
        var puzzle = TestPuzzles.WithNodes(2);
        puzzle.Edges.Add(new Edge(1, 2, 2, "LessThan"));
        puzzle.Groups.Add(new Group(1, [1, 2, 1], "Thermometer"));

        var errors = PuzzleValidator.Validate(puzzle);
        errors.Should().Contain(e => e.StartsWith("Kante 1: Start- und Zielknoten sind identisch", StringComparison.Ordinal));
        errors.Should().Contain("Gruppe 1: Der Knoten 1 ist mehrfach enthalten.");
    }

    [Fact]
    public void Unknown_rule_types_name_rule_and_element()
    {
        var puzzle = TestPuzzles.WithNodes(2);
        puzzle.Edges.Add(new Edge(5, 1, 2, "WhiteDot"));
        puzzle.Groups.Add(new Group(3, [1, 2], "Killer"));

        var errors = PuzzleValidator.Validate(puzzle);
        errors.Should().HaveCount(2);
        errors[0].Should().StartWith("Kante 5: Unbekannter Regeltyp „WhiteDot“. Bekannt sind: AllDifferent,");
        errors[1].Should().StartWith("Gruppe 3: Unbekannter Regeltyp „Killer“.");
    }

    [Fact]
    public void Rules_must_match_element_type()
    {
        var puzzle = TestPuzzles.WithNodes(2);
        puzzle.Edges.Add(new Edge(1, 1, 2, "AllDifferent"));
        puzzle.Groups.Add(new Group(1, [1, 2], "LessThan"));

        PuzzleValidator.Validate(puzzle).Should().BeEquivalentTo(
            "Kante 1: Die Regel „AllDifferent“ (Alle verschieden) gilt für Gruppen, nicht für Kanten.",
            "Gruppe 1: Die Regel „LessThan“ (Kleiner als) gilt für gerichtete Kanten, nicht für Gruppen.");
    }

    [Fact]
    public void Rule_parameter_errors_are_prefixed_with_element_and_rule()
    {
        var puzzle = TestPuzzles.WithNodes(3);
        puzzle.Edges.Add(new Edge(2, 1, 2, "Difference", TestPuzzles.Params(("k", "zwei"))));
        puzzle.Groups.Add(new Group(1, [1, 2, 3], "CoverRange", label: "Zeile 1"));

        PuzzleValidator.Validate(puzzle).Should().BeEquivalentTo(
            "Kante 2 (Difference): Parameter „k“ muss eine ganze Zahl sein, ist aber die Zeichenkette \"zwei\".",
            "Gruppe 1 „Zeile 1“ (CoverRange): Die Gruppe hat 3 Knoten, der Bereich 1..9 umfasst aber 9 Werte; beide Anzahlen müssen gleich sein.");
    }

    [Fact]
    public void EnsureValid_throws_with_all_errors()
    {
        var puzzle = TestPuzzles.WithNodes(1);
        puzzle.Nodes[0].Given = 0;
        puzzle.Edges.Add(new Edge(1, 1, 7, "NotEqual"));

        var act = () => PuzzleValidator.EnsureValid(puzzle);
        act.Should().Throw<PuzzleValidationException>()
            .Where(e => e.Errors.Count == 2)
            .WithMessage("Das Rätsel ist ungültig (2 Fehler):*");
    }
}
