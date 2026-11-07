using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;
using GraphDoku.Core.Serialization;

namespace GraphDoku.Solver.Tests.Helpers;

/// <summary>Testrätsel: Beispieldateien aus <c>puzzles/</c> und kleine, gezielt aufgebaute Rätsel.</summary>
internal static class TestPuzzles
{
    public static string PuzzlesDirectory => Path.Combine(AppContext.BaseDirectory, "puzzles");

    public static Puzzle Load(string fileName) => PuzzleSerializer.Load(Path.Combine(PuzzlesDirectory, fileName));

    /// <summary>Rätsel mit <paramref name="count"/> Knoten (Ids 1..count) in einer Zeile.</summary>
    public static Puzzle WithNodes(int count, int min = 1, int max = 9)
    {
        var puzzle = new Puzzle("Test", GridType.Square, new ValueDomain(min, max));
        for (var i = 1; i <= count; i++)
        {
            puzzle.Nodes.Add(new Node(i, i - 1, 0));
        }

        return puzzle;
    }

    public static Puzzle WithEdge(string rule, RuleParams? parameters = null, int min = 1, int max = 9)
    {
        var puzzle = WithNodes(2, min, max);
        puzzle.Edges.Add(new Edge(1, 1, 2, rule, parameters));
        return puzzle;
    }

    public static Puzzle WithGroup(string rule, int nodes, RuleParams? parameters = null, int min = 1, int max = 9)
    {
        var puzzle = WithNodes(nodes, min, max);
        puzzle.Groups.Add(new Group(1, Enumerable.Range(1, nodes), rule, parameters));
        return puzzle;
    }

    public static Puzzle Given(this Puzzle puzzle, int nodeId, int value)
    {
        puzzle.FindNode(nodeId)!.Given = value;
        return puzzle;
    }

    public static Puzzle Domain(this Puzzle puzzle, int nodeId, int min, int max)
    {
        puzzle.FindNode(nodeId)!.Domain = new ValueDomain(min, max);
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

    /// <summary>
    /// Färbungsrätsel auf dem Mycielski-Graphen M<paramref name="order"/> (dreiecksfrei, chromatische Zahl = order)
    /// mit nur order − 1 Farben: unlösbar, aber für CP-SAT ab M6 sehr schwer zu beweisen (lokal &gt; 20 s).
    /// Dient als lang laufende Suche für Zeitlimit- und Abbruchtests.
    /// </summary>
    public static Puzzle MycielskiColoring(int order)
    {
        List<(int, int)> edges = [(0, 1)];
        var count = 2;
        for (var k = 3; k <= order; k++)
        {
            var next = new List<(int, int)>(edges);
            foreach (var (u, v) in edges)
            {
                next.Add((u, count + v));
                next.Add((count + u, v));
            }

            for (var i = 0; i < count; i++)
            {
                next.Add((count + i, 2 * count));
            }

            edges = next;
            count = (2 * count) + 1;
        }

        var puzzle = new Puzzle($"Mycielski M{order}", GridType.Square, new ValueDomain(1, order - 1));
        for (var i = 0; i < count; i++)
        {
            puzzle.Nodes.Add(new Node(i + 1, i, 0));
        }

        var edgeId = 1;
        foreach (var (u, v) in edges)
        {
            puzzle.Edges.Add(new Edge(edgeId++, u + 1, v + 1, NotEqualRule.Name));
        }

        return puzzle;
    }
}
