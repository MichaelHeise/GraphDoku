using System.Diagnostics;
using GraphDoku.Explainer.Tests.Helpers;
using GraphDoku.Solver.OrTools;
using GraphDoku.Solver.Tests.Helpers;
using Xunit.Abstractions;

namespace GraphDoku.Explainer.Tests;

/// <summary>
/// Der Erklär-Modus erklärt jedes Beispielrätsel aus <c>puzzles/</c> konsistent mit der eindeutigen Lösung: Jeder gesetzte
/// Wert ist der Lösungswert, keine gestrichene Zahl ist der Lösungswert. Die Laufzeit wird gemessen und ausgegeben.
/// </summary>
public sealed class ExamplePuzzleExplanationTests(ITestOutputHelper output)
{
    /// <summary>Großzügige Obergrenze pro Erklärung (lokal dauert die langsamste, das Hexadoku, deutlich unter 1 s).</summary>
    private static readonly TimeSpan MaxExplanationTime = TimeSpan.FromSeconds(30);

    /// <summary>Die Beispiele aus Paket 5 wurden so erzeugt, dass sie ohne Verzweigung (ohne Raten) erklärbar sind.</summary>
    private static readonly string[] BranchFreeExamples =
        ["killer-sudoku.json", "kropki-sudoku.json", "hexadoku-16x16.json", "dreiecks-sudoku.json", "thermo-sudoku-6x6.json", "klassisch-leicht.json"];

    private static readonly OrToolsSolver Solver = new();

    public static TheoryData<string> AllFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "puzzles"), "*.json").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllFiles))]
    public void Every_example_is_explained_consistently_with_its_solution(string file)
    {
        var puzzle = ExplainerPuzzles.Load(file);
        var count = Solver.CountSolutions(puzzle, limit: 2);

        var watch = Stopwatch.StartNew();
        var result = new ExplanationEngine(Solver).Explain(puzzle);
        watch.Stop();
        output.WriteLine($"{file}: {result.Steps.Count} Schritte, {result.BranchCount} Verzweigungen, {watch.Elapsed.TotalMilliseconds:0} ms – {result.Message}");

        watch.Elapsed.Should().BeLessThan(MaxExplanationTime);
        result.IsComplete.Should().BeTrue(result.Message);
        if (count.IsAmbiguous)
        {
            // Nur das absichtlich leere Gitter: Die Erklärung folgt einer der Lösungen.
            file.Should().Be("klassisch-leer.json");
            result.Status.Should().Be(ExplanationStatus.SolvedAmbiguous, result.Message);
            SolutionVerifier.Violations(puzzle, result.Solution!).Should().BeEmpty();
            ExplanationAcceptanceTests.AssertConsistent(result, result.Solution!);
            return;
        }

        count.IsUnique.Should().BeTrue(count.Message);
        var solution = count.Solutions[0];
        result.Status.Should().Be(ExplanationStatus.Solved, result.Message);
        result.Solution.Should().BeEquivalentTo(solution);
        ExplanationAcceptanceTests.AssertConsistent(result, solution);
        if (BranchFreeExamples.Contains(file))
        {
            result.UsedBranching.Should().BeFalse($"{file} ist ohne Raten lösbar");
        }
    }

    [Theory]
    [InlineData("killer-sudoku.json", "Sum")]
    [InlineData("kropki-sudoku.json", "Ratio")]
    [InlineData("kropki-sudoku.json", "Difference")]
    [InlineData("hexadoku-16x16.json", "CoverRange")]
    [InlineData("dreiecks-sudoku.json", "NotEqual")]
    [InlineData("dreiecks-sudoku.json", "CoverRange")]
    [InlineData("thermo-sudoku-6x6.json", "Thermometer")]
    [InlineData("thermo-sudoku-6x6.json", "LessThan")]
    public void Characteristic_rule_of_an_example_justifies_steps(string file, string rule)
    {
        var puzzle = ExplainerPuzzles.Load(file);

        var result = new ExplanationEngine(Solver).Explain(puzzle);

        result.Steps.Should().Contain(
            s => s.Constraint != null && puzzle.FindConstraint(s.Constraint.Value)!.Rule == rule,
            $"die Regel {rule} muss in {file} mindestens einen Schritt begründen");
    }
}
