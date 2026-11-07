using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;

namespace GraphDoku.Solver.Tests.Helpers;

/// <summary>
/// Hilfen für klassische 9×9-Sudokus als 81-Zeichen-Zeichenketten (zeilenweise, '.' = leer), unabhängig vom
/// Rätselmodell: Gültigkeitsprüfung des fertigen Gitters und ein Löser, der nur Singles verwendet.
/// </summary>
internal static class ClassicGrid
{
    private static readonly int[][] Units = BuildUnits();

    /// <summary>Werte einer Lösung als 81 Ziffern (Knoten-Id = Zeile·9 + Spalte + 1).</summary>
    public static string ToGridString(IReadOnlyDictionary<int, int> values) =>
        string.Concat(Enumerable.Range(1, 81).Select(id => values[id].ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>Givens eines Rätsels als 81 Zeichen.</summary>
    public static string GivensOf(Puzzle puzzle) =>
        string.Concat(Enumerable.Range(1, 81).Select(id => puzzle.FindNode(id)!.Given is int g ? (char)('0' + g) : '.'));

    /// <summary>Ist <paramref name="solution"/> ein vollständig und regelgerecht ausgefülltes Gitter, das die Givens enthält?</summary>
    public static bool IsValidSolution(string givens, string solution)
    {
        if (solution.Length != 81 || givens.Length != 81 || solution.Any(c => c is < '1' or > '9'))
        {
            return false;
        }

        if (Enumerable.Range(0, 81).Any(i => givens[i] != '.' && givens[i] != solution[i]))
        {
            return false;
        }

        return Units.All(unit => unit.Select(i => solution[i]).Distinct().Count() == 9);
    }

    /// <summary>
    /// Löst nur mit „naked single“ und „hidden single“. Liefert das (ggf. unvollständige) Gitter; '.' bleibt, wo
    /// diese beiden Techniken nicht weiterkommen.
    /// </summary>
    public static string SolveWithSinglesOnly(string givens)
    {
        var grid = givens.ToCharArray();
        var progress = true;
        while (progress)
        {
            progress = false;
            for (var cell = 0; cell < 81; cell++)
            {
                if (grid[cell] == '.' && Candidates(grid, cell) is [var only])
                {
                    grid[cell] = only;
                    progress = true;
                }
            }

            foreach (var unit in Units)
            {
                for (var digit = '1'; digit <= '9'; digit++)
                {
                    if (unit.Any(i => grid[i] == digit))
                    {
                        continue;
                    }

                    var places = unit.Where(i => grid[i] == '.' && Candidates(grid, i).Contains(digit)).ToList();
                    if (places.Count == 1)
                    {
                        grid[places[0]] = digit;
                        progress = true;
                    }
                }
            }
        }

        return new string(grid);
    }

    public static Puzzle Create(string givens, string name = "Test") => ClassicSudoku.Create(givens, name);

    private static List<char> Candidates(char[] grid, int cell)
    {
        var used = Units.Where(u => u.Contains(cell)).SelectMany(u => u).Select(i => grid[i]).ToHashSet();
        return [.. "123456789".Where(d => !used.Contains(d))];
    }

    private static int[][] BuildUnits()
    {
        var units = new List<int[]>();
        for (var i = 0; i < 9; i++)
        {
            units.Add([.. Enumerable.Range(0, 9).Select(c => (i * 9) + c)]);
            units.Add([.. Enumerable.Range(0, 9).Select(r => (r * 9) + i)]);
            var top = i / 3 * 3;
            var left = i % 3 * 3;
            units.Add([.. Enumerable.Range(0, 9).Select(k => ((top + (k / 3)) * 9) + left + (k % 3))]);
        }

        return [.. units];
    }
}
