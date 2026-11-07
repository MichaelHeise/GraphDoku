namespace GraphDoku.App.Tests;

/// <summary>Selbsttest ohne Oberfläche (<c>--selbsttest</c>), mit dem der Release-Workflow das fertige Paket prüft.</summary>
public sealed class SelfTestTests
{
    private static string Puzzle(string name) => Path.Combine(MainWindowViewModelTests.FindRepositoryRoot(), "puzzles", name);

    [Fact]
    public void Builtin_classic_sudoku_passes() => SelfTest.Run([]).Should().Be(0);

    [Fact]
    public void Unique_puzzle_file_passes() => SelfTest.Run([Puzzle("killer-sudoku.json")]).Should().Be(0);

    [Fact]
    public void Ambiguous_puzzle_fails() => SelfTest.Run([Puzzle("klassisch-leer.json")]).Should().Be(1);

    [Fact]
    public void Missing_file_fails_without_throwing() => SelfTest.Run([Puzzle("gibt-es-nicht.json")]).Should().Be(1);
}
