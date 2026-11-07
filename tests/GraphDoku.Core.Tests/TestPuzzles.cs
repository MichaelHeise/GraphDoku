using GraphDoku.Core.Model;

namespace GraphDoku.Core.Tests;

/// <summary>Hilfen zum Aufbau kleiner Testrätsel.</summary>
internal static class TestPuzzles
{
    /// <summary>Rätsel mit <paramref name="count"/> Knoten (Ids 1..count) in einer Zeile.</summary>
    public static Puzzle WithNodes(int count, int min = 1, int max = 9, GridType grid = GridType.Square)
    {
        var puzzle = new Puzzle("Test", grid, new ValueDomain(min, max));
        for (var i = 1; i <= count; i++)
        {
            puzzle.Nodes.Add(new Node(i, i - 1, 0));
        }

        return puzzle;
    }

    public static RuleParams Params(params (string Name, object Value)[] values)
    {
        var result = new RuleParams();
        foreach (var (name, value) in values)
        {
            switch (value)
            {
                case int i:
                    result.Set(name, i);
                    break;
                case bool b:
                    result.Set(name, b);
                    break;
                case string s:
                    result.Set(name, s);
                    break;
                default:
                    throw new ArgumentException($"Nicht unterstützter Typ {value.GetType()}.", nameof(values));
            }
        }

        return result;
    }
}
