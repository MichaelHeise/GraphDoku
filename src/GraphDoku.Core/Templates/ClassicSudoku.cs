using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;

namespace GraphDoku.Core.Templates;

/// <summary>Erzeugt ein klassisches 9×9-Sudoku: 81 Knoten auf dem Quadratgitter und 27 <c>AllDifferent</c>-Gruppen.</summary>
public static class ClassicSudoku
{
    public const int Size = 9;
    public const string DefaultName = "Klassisch 9x9";

    /// <summary>Id des Knotens in Zeile <paramref name="row"/> und Spalte <paramref name="column"/> (jeweils 0–8).</summary>
    public static int NodeId(int row, int column) => (row * Size) + column + 1;

    /// <summary>
    /// Erzeugt das Rätsel. Knoten 1–81 zeilenweise mit x = Spalte, y = Zeile; Gruppen 1–9 „Zeile n“,
    /// 10–18 „Spalte n“, 19–27 „Block n“ (Blöcke zeilenweise von links oben).
    /// </summary>
    /// <param name="givens">Optional 81 Zeichen zeilenweise: '1'–'9' für Givens, '0' oder '.' für leer. Leerraum wird ignoriert.</param>
    /// <param name="name">Name des Rätsels.</param>
    /// <exception cref="ArgumentException">Die Givens-Zeichenkette hat nicht 81 Zeichen oder enthält ungültige Zeichen.</exception>
    public static Puzzle Create(string? givens = null, string name = DefaultName)
    {
        var values = ParseGivens(givens);
        var puzzle = new Puzzle(name, GridType.Square, new ValueDomain(1, Size));

        for (var row = 0; row < Size; row++)
        {
            for (var column = 0; column < Size; column++)
            {
                var id = NodeId(row, column);
                puzzle.Nodes.Add(new Node(id, column, row, values?[id - 1]));
            }
        }

        var groupId = 1;
        for (var row = 0; row < Size; row++)
        {
            puzzle.Groups.Add(new Group(groupId++, Enumerable.Range(0, Size).Select(c => NodeId(row, c)), AllDifferentRule.Name, label: $"Zeile {row + 1}"));
        }

        for (var column = 0; column < Size; column++)
        {
            puzzle.Groups.Add(new Group(groupId++, Enumerable.Range(0, Size).Select(r => NodeId(r, column)), AllDifferentRule.Name, label: $"Spalte {column + 1}"));
        }

        for (var block = 0; block < Size; block++)
        {
            var top = block / 3 * 3;
            var left = block % 3 * 3;
            var nodes = Enumerable.Range(0, Size).Select(i => NodeId(top + (i / 3), left + (i % 3)));
            puzzle.Groups.Add(new Group(groupId++, nodes, AllDifferentRule.Name, label: $"Block {block + 1}"));
        }

        return puzzle;
    }

    private static int?[]? ParseGivens(string? givens)
    {
        if (givens is null)
        {
            return null;
        }

        var cells = givens.Where(c => !char.IsWhiteSpace(c)).ToArray();
        if (cells.Length != Size * Size)
        {
            throw new ArgumentException(
                $"Die Vorgaben müssen genau {Size * Size} Zeichen umfassen (ohne Leerraum), es sind aber {cells.Length}.",
                nameof(givens));
        }

        var result = new int?[cells.Length];
        for (var i = 0; i < cells.Length; i++)
        {
            var c = cells[i];
            result[i] = c switch
            {
                '0' or '.' => null,
                >= '1' and <= '9' => c - '0',
                _ => throw new ArgumentException(
                    $"Ungültiges Zeichen „{c}“ an Position {i + 1} der Vorgaben: erlaubt sind 1–9 sowie 0 oder . für leere Felder.",
                    nameof(givens)),
            };
        }

        return result;
    }
}
