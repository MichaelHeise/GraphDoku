using GraphDoku.Solver.OrTools;
using GraphDoku.Solver.Tests.Helpers;

namespace GraphDoku.Solver.Tests;

/// <summary>Akzeptanz Paket 2: klassische Sudokus mit bekannter Lösung, ein unlösbares und ein mehrdeutiges Rätsel.</summary>
public class ClassicSudokuTests
{
    private readonly OrToolsSolver _solver = new();

    /// <summary>Beispieldateien in <c>puzzles/</c> mit ihren Givens und der bekannten (eindeutigen) Lösung.</summary>
    public static TheoryData<string, string, string> KnownPuzzles => new()
    {
        {
            "klassisch-leicht.json",
            "53..7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79",
            "534678912672195348198342567859761423426853791713924856961537284287419635345286179"
        },
        {
            "klassisch-mittel.json",
            "1..92....524.1...........7..5...81.2.........4.27...9..6...........3.945....71..6",
            "176923584524817639893654271957348162638192457412765398265489713781236945349571826"
        },
        {
            "klassisch-schwer.json",
            "8..........36......7..9.2...5...7.......457.....1...3...1....68..85...1..9....4..",
            "812753649943682175675491283154237896369845721287169534521974368438526917796318452"
        },
    };

    [Theory]
    [MemberData(nameof(KnownPuzzles))]
    public void Expected_solutions_are_valid_according_to_an_independent_checker(string file, string givens, string solution)
    {
        ClassicGrid.IsValidSolution(givens, solution).Should().BeTrue($"die hinterlegte Lösung zu {file} muss gültig sein");
        ClassicGrid.GivensOf(TestPuzzles.Load(file)).Should().Be(givens, "die Datei muss die erwarteten Givens enthalten");
    }

    [Theory]
    [MemberData(nameof(KnownPuzzles))]
    public void Solve_finds_the_known_solution(string file, string givens, string solution)
    {
        var puzzle = TestPuzzles.Load(file);

        var result = _solver.Solve(puzzle);

        result.Status.Should().Be(SolveStatus.Solved, result.Message);
        result.Values.Should().NotBeNull();
        ClassicGrid.ToGridString(result.Values!).Should().Be(solution);
        ClassicGrid.IsValidSolution(givens, ClassicGrid.ToGridString(result.Values!)).Should().BeTrue();
        SolutionVerifier.Violations(puzzle, result.Values!).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(KnownPuzzles))]
    public void Known_puzzles_are_unique(string file, string givens, string solution)
    {
        _ = givens;
        var result = _solver.CountSolutions(TestPuzzles.Load(file), limit: 2);

        result.Status.Should().Be(CountStatus.Completed, result.Message);
        result.Count.Should().Be(1);
        result.IsUnique.Should().BeTrue();
        result.Message.Should().Be("Das Rätsel ist eindeutig lösbar.");
        ClassicGrid.ToGridString(result.Solutions.Single()).Should().Be(solution);
    }

    [Fact]
    public void Easy_puzzle_is_solvable_with_singles_only()
    {
        // Voraussetzung für die Akzeptanz des Erklär-Modus (Paket 6): ganz ohne Verzweigung lösbar.
        var givens = ClassicGrid.GivensOf(TestPuzzles.Load("klassisch-leicht.json"));

        ClassicGrid.SolveWithSinglesOnly(givens)
            .Should().Be("534678912672195348198342567859761423426853791713924856961537284287419635345286179");
    }

    [Theory]
    [InlineData("klassisch-mittel.json")]
    [InlineData("klassisch-schwer.json")]
    public void Medium_and_hard_puzzles_need_more_than_singles(string file)
    {
        var givens = ClassicGrid.GivensOf(TestPuzzles.Load(file));

        ClassicGrid.SolveWithSinglesOnly(givens).Should().Contain(".");
    }

    [Fact]
    public void Unsolvable_puzzle_is_reported()
    {
        // Wie „leicht“, aber links oben 1 statt 5: kein direkter Konflikt in Zeile, Spalte oder Block, trotzdem unlösbar.
        const string Givens = "13..7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79";
        var puzzle = ClassicGrid.Create(Givens);

        var solve = _solver.Solve(puzzle);
        var count = _solver.CountSolutions(puzzle);

        solve.Status.Should().Be(SolveStatus.Unsolvable);
        solve.Values.Should().BeNull();
        solve.Message.Should().Contain("unlösbar");
        count.Status.Should().Be(CountStatus.Completed);
        count.Count.Should().Be(0);
        count.IsUnsolvable.Should().BeTrue();
    }

    [Fact]
    public void Ambiguous_puzzle_has_two_solutions()
    {
        // Wie „leicht“, aber ohne die 6 in Zeile 3, Spalte 8: genau zwei Lösungen.
        const string Givens = "53..7....6..195....98......8...6...34..8.3..17...2...6.6....28....419..5....8..79";
        string[] expected =
        [
            "534678912672195348198342567859761423426853791713924856961537284287419635345286179",
            "534678912672195438198342657819764523426853791753921846961537284287419365345286179",
        ];
        expected.Should().OnlyContain(s => ClassicGrid.IsValidSolution(Givens, s));
        var puzzle = ClassicGrid.Create(Givens);

        var limited = _solver.CountSolutions(puzzle, limit: 2);
        var exact = _solver.CountSolutions(puzzle, limit: 10);

        limited.Status.Should().Be(CountStatus.Completed);
        limited.Count.Should().Be(2);
        limited.LimitReached.Should().BeTrue();
        limited.IsAmbiguous.Should().BeTrue();
        limited.IsUnique.Should().BeFalse();
        limited.Message.Should().Contain("mehrdeutig");
        limited.Solutions.Select(ClassicGrid.ToGridString).Should().BeEquivalentTo(expected);
        exact.Count.Should().Be(2);
        exact.LimitReached.Should().BeFalse();
        exact.Message.Should().Be("Das Rätsel ist mehrdeutig: Es gibt genau 2 Lösungen.");

        var solve = _solver.Solve(puzzle);
        solve.Status.Should().Be(SolveStatus.Solved);
        expected.Should().Contain(ClassicGrid.ToGridString(solve.Values!));
    }

    [Fact]
    public void Empty_classic_sudoku_is_solvable_and_ambiguous()
    {
        var puzzle = TestPuzzles.Load("klassisch-leer.json");

        var solve = _solver.Solve(puzzle);
        var count = _solver.CountSolutions(puzzle);

        solve.Status.Should().Be(SolveStatus.Solved);
        SolutionVerifier.Violations(puzzle, solve.Values!).Should().BeEmpty();
        count.Count.Should().Be(2);
        count.IsAmbiguous.Should().BeTrue();
        count.Solutions.Should().HaveCount(2).And.OnlyHaveUniqueItems(s => ClassicGrid.ToGridString(s));
    }
}
