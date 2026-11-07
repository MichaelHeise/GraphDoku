using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;
using GraphDoku.Core.Serialization;
using GraphDoku.Core.Templates;

namespace GraphDoku.Explainer.Tests.Helpers;

/// <summary>Testrätsel: Beispieldateien aus <c>puzzles/</c> und kleine Rätsel, die jeweils eine Regel benutzen.</summary>
internal static class ExplainerPuzzles
{
    public const string EasySolution = "534678912672195348198342567859761423426853791713924856961537284287419635345286179";

    public static Puzzle Load(string fileName) =>
        PuzzleSerializer.Load(Path.Combine(AppContext.BaseDirectory, "puzzles", fileName));

    public static Puzzle WithNodes(int count, int min = 1, int max = 9)
    {
        var puzzle = new Puzzle("Test", GridType.Square, new ValueDomain(min, max));
        for (var i = 1; i <= count; i++)
        {
            puzzle.Nodes.Add(new Node(i, i - 1, 0));
        }

        return puzzle;
    }

    public static Puzzle Given(this Puzzle puzzle, int nodeId, int value)
    {
        puzzle.FindNode(nodeId)!.Given = value;
        return puzzle;
    }

    public static Puzzle Edge(this Puzzle puzzle, int from, int to, string rule, RuleParams? parameters = null)
    {
        puzzle.Edges.Add(new Edge(puzzle.NextEdgeId(), from, to, rule, parameters));
        return puzzle;
    }

    public static Puzzle Group(this Puzzle puzzle, string rule, IEnumerable<int> nodes, RuleParams? parameters = null, string? label = null)
    {
        puzzle.Groups.Add(new Group(puzzle.NextGroupId(), nodes, rule, parameters, label: label));
        return puzzle;
    }

    public static RuleParams Params(params (string Name, object Value)[] values)
    {
        var result = new RuleParams();
        foreach (var (name, value) in values)
        {
            _ = value switch
            {
                int i => result.Set(name, i),
                bool b => result.Set(name, b),
                string s => result.Set(name, s),
                _ => throw new ArgumentException($"Nicht unterstützter Typ {value.GetType()}.", nameof(values)),
            };
        }

        return result;
    }

    /// <summary>Kleine, eindeutige Rätsel, die jeweils (vor allem) eine Regel brauchen; Name = geprüfte Regel.</summary>
    public static IEnumerable<(string Rule, Puzzle Puzzle)> RulePuzzles()
    {
        // NotEqual: Pfad 1–2–3–4 mit zwei Farben.
        yield return (NotEqualRule.Name, WithNodes(4, 1, 2).Given(1, 1)
            .Edge(1, 2, NotEqualRule.Name).Edge(2, 3, NotEqualRule.Name).Edge(3, 4, NotEqualRule.Name));

        // Difference (genau): |a − b| = 8 und |b − c| = 7, c = 2 → b = 9, a = 1.
        yield return (DifferenceRule.Name, WithNodes(3).Given(3, 2)
            .Edge(1, 2, DifferenceRule.Name, Params(("k", 8)))
            .Edge(2, 3, DifferenceRule.Name, Params(("k", 7))));

        // Difference (mindestens): Werte 1..3, Abstand ≥ 2, a = 3 → b = 1; c ≥ 2 von b entfernt → c = 3.
        yield return (DifferenceRule.Name + "/atLeast", WithNodes(3, 1, 3).Given(1, 3)
            .Edge(1, 2, DifferenceRule.Name, Params(("k", 2), ("mode", "atLeast")))
            .Edge(2, 3, DifferenceRule.Name, Params(("k", 2), ("mode", "atLeast"))));

        // Ratio: a = 1, b = 3·a, c = 3·b oder b = 3·c, c ≠ a → (1, 3, 9).
        yield return (RatioRule.Name, WithNodes(3).Given(1, 1)
            .Edge(1, 2, RatioRule.Name, Params(("factor", 3)))
            .Edge(2, 3, RatioRule.Name, Params(("factor", 3)))
            .Edge(1, 3, NotEqualRule.Name));

        // LessThan: 1 < 2 < 3 mit Werten 1..3.
        yield return (LessThanRule.Name, WithNodes(3, 1, 3).Edge(1, 2, LessThanRule.Name).Edge(2, 3, LessThanRule.Name));

        // Thermometer: vier Knoten mit Werten 1..4.
        yield return (ThermometerRule.Name, WithNodes(4, 1, 4).Group(ThermometerRule.Name, [1, 2, 3, 4]));

        // Sum: Käfig 1+2+3 = 6 ohne Wiederholung, 1 < 2 < 3; dazu Summe 18 aus zwei Knoten mit Wiederholung.
        yield return (SumRule.Name, WithNodes(5)
            .Group(SumRule.Name, [1, 2, 3], Params(("target", 6)))
            .Edge(1, 2, LessThanRule.Name).Edge(2, 3, LessThanRule.Name)
            .Group(SumRule.Name, [4, 5], Params(("target", 18), ("allowRepeats", true))));

        // AllDifferent: lateinisches Quadrat 3×3.
        yield return (AllDifferentRule.Name, LatinSquare3());

        // CoverRange: Mini-Sudoku 4×4 (Zeilen, Spalten, Blöcke).
        yield return (CoverRangeRule.Name, MiniSudoku4());

        // Killer-artig: Mini-Sudoku 4×4 ganz ohne Givens, nur über Summen.
        yield return (SumRule.Name + "/Killer", KillerMini4());
    }

    /// <summary>Givens des 4×4-Sudokus, zeilenweise, '.' = leer.</summary>
    public const string MiniSudokuGivens = "1..4" + "..1." + "2..." + ".3.1";

    public static Puzzle LatinSquare3()
    {
        var puzzle = WithNodes(9, 1, 3).Given(1, 1).Given(2, 2).Given(6, 1);
        for (var r = 0; r < 3; r++)
        {
            puzzle.Group(AllDifferentRule.Name, Enumerable.Range(0, 3).Select(c => (r * 3) + c + 1), label: $"Zeile {r + 1}");
        }

        for (var c = 0; c < 3; c++)
        {
            puzzle.Group(AllDifferentRule.Name, Enumerable.Range(0, 3).Select(r => (r * 3) + c + 1), label: $"Spalte {c + 1}");
        }

        return puzzle;
    }

    /// <summary>
    /// 4×4-Sudoku mit Lösung 1234 / 3412 / 2143 / 4321 und <c>CoverRange</c>-Gruppen. Ohne <paramref name="givens"/>
    /// werden fünf Givens gesetzt, die die Lösung eindeutig machen (im Test mit dem Solver geprüft).
    /// </summary>
    public static Puzzle MiniSudoku4(string? givens = MiniSudokuGivens, string rule = "CoverRange")
    {
        var puzzle = WithNodes(16, 1, 4);
        if (givens is not null)
        {
            for (var i = 0; i < 16; i++)
            {
                if (givens[i] != '.')
                {
                    puzzle.Given(i + 1, givens[i] - '0');
                }
            }
        }

        for (var r = 0; r < 4; r++)
        {
            puzzle.Group(rule, Enumerable.Range(0, 4).Select(c => (r * 4) + c + 1), label: $"Zeile {r + 1}");
        }

        for (var c = 0; c < 4; c++)
        {
            puzzle.Group(rule, Enumerable.Range(0, 4).Select(r => (r * 4) + c + 1), label: $"Spalte {c + 1}");
        }

        for (var b = 0; b < 4; b++)
        {
            var top = b / 2 * 2;
            var left = b % 2 * 2;
            puzzle.Group(rule, Enumerable.Range(0, 4).Select(k => ((top + (k / 2)) * 4) + left + (k % 2) + 1), label: $"Block {b + 1}");
        }

        return puzzle;
    }

    /// <summary>4×4-Sudoku (Lösung 1234 / 3412 / 2143 / 4321) ohne Givens mit Summenkäfigen.</summary>
    public static Puzzle KillerMini4()
    {
        var puzzle = MiniSudoku4(givens: null, rule: AllDifferentRule.Name);
        puzzle.Name = "Killer 4x4";
        puzzle.Group(SumRule.Name, [1, 2], Params(("target", 3)), "Käfig 1+2");
        puzzle.Group(SumRule.Name, [3, 4, 8], Params(("target", 9)), "Käfig 3+4+8");
        puzzle.Group(SumRule.Name, [5, 9], Params(("target", 5)), "Käfig 5+9");
        puzzle.Group(SumRule.Name, [6, 7], Params(("target", 5)), "Käfig 6+7");
        puzzle.Group(SumRule.Name, [10, 11, 15], Params(("target", 7)), "Käfig 10+11+15");
        puzzle.Group(SumRule.Name, [12, 16], Params(("target", 4)), "Käfig 12+16");
        puzzle.Group(SumRule.Name, [13, 14], Params(("target", 7)), "Käfig 13+14");
        return puzzle;
    }

    public static string ToGridString(IReadOnlyDictionary<int, int> values) =>
        string.Concat(Enumerable.Range(1, values.Count).Select(id => values[id].ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public static Puzzle Classic(string givens) => ClassicSudoku.Create(givens);
}
