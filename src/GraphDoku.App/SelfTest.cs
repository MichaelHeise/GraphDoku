using System.Diagnostics;
using GraphDoku.Core.Model;
using GraphDoku.Core.Serialization;
using GraphDoku.Core.Templates;
using GraphDoku.Explainer;
using GraphDoku.Solver;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App;

/// <summary>
/// Selbsttest ohne Oberfläche (<c>GraphDoku.App --selbsttest [Rätseldatei]</c>): löst ein Rätsel mit OR-Tools, prüft
/// die Eindeutigkeit und erklärt es. Damit lässt sich ein fertiges Paket prüfen (native Bibliotheken, Einzeldatei),
/// ohne ein Fenster zu öffnen – der Release-Workflow nutzt ihn für das Linux-Paket.
/// </summary>
internal static class SelfTest
{
    public const string Argument = "--selbsttest";

    // „Klassisch leicht“ aus puzzles/, damit der Test ohne Dateien neben der App auskommt.
    private const string ClassicEasy =
        "530070000600195000098000060800060003400803001700020006060000280000419005000080079";

    public static int Run(IReadOnlyList<string> args)
    {
        try
        {
            var version = typeof(SelfTest).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion ?? "unbekannt";
            Console.WriteLine($"GraphDoku {version} – Selbsttest");

            var puzzle = args.Count > 0 ? PuzzleSerializer.Load(args[0]) : ClassicSudoku.Create(ClassicEasy, "Klassisch leicht");
            Console.WriteLine($"Rätsel: {puzzle.Name} ({puzzle.Nodes.Count} Knoten)");

            var solver = new OrToolsSolver();
            var stopwatch = Stopwatch.StartNew();
            var solved = solver.Solve(puzzle);
            Console.WriteLine($"Lösen: {solved.Status} in {stopwatch.ElapsedMilliseconds} ms – {solved.Message}");
            if (!solved.IsSolved)
            {
                return 1;
            }

            var count = solver.CountSolutions(puzzle);
            Console.WriteLine($"Eindeutigkeit: {count.Message}");

            var explanation = new ExplanationEngine(solver).Explain(puzzle);
            Console.WriteLine($"Erklärung: {explanation.Status}, {explanation.Steps.Count} Schritte, {explanation.BranchCount} Verzweigungen");

            var ok = count.IsUnique && explanation.Status == ExplanationStatus.Solved;
            Console.WriteLine(ok ? "Selbsttest bestanden." : "Selbsttest fehlgeschlagen.");
            return ok ? 0 : 1;
        }
#pragma warning disable CA1031 // Der Selbsttest meldet jeden Fehler als Text und Exit-Code statt abzustürzen.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.Error.WriteLine($"Selbsttest fehlgeschlagen: {ex}");
            return 1;
        }
    }
}
