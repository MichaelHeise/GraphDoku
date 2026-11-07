using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;
using GraphDoku.Core.Serialization;
using GraphDoku.Core.Validation;
using GraphDoku.Solver.OrTools;
using GraphDoku.Solver.Tests.Helpers;

namespace GraphDoku.Solver.Tests;

/// <summary>
/// Akzeptanz Paket 5: Jedes Beispielrätsel in <c>puzzles/</c> lädt, ist gültig, lösbar und eindeutig (außer dem absichtlich
/// leeren Gitter). Dazu Strukturprüfungen der neuen Beispiele (Killer, Kropki, Hexadoku, Dreiecksgitter, Thermometer).
/// </summary>
public class ExamplePuzzleTests
{
    private const string EmptyGrid = "klassisch-leer.json";

    private static readonly OrToolsSolver Solver = new();

    public static TheoryData<string> AllFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(TestPuzzles.PuzzlesDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    private static IReadOnlyDictionary<int, int> UniqueSolution(Puzzle puzzle)
    {
        var count = Solver.CountSolutions(puzzle, limit: 2);
        count.IsUnique.Should().BeTrue(count.Message);
        return count.Solutions[0];
    }

    private static int Givens(Puzzle puzzle) => puzzle.Nodes.Count(n => n.Given is not null);

    private static GridPoint Point(Puzzle puzzle, int id)
    {
        var node = puzzle.FindNode(id)!;
        return new GridPoint(node.X, node.Y);
    }

    private static bool AreOrthogonalNeighbors(Puzzle puzzle, int a, int b)
    {
        var p = Point(puzzle, a);
        return GridGeometry.GetOrthogonalNeighbors(puzzle.Grid, p.X, p.Y).Contains(Point(puzzle, b));
    }

    /// <summary>true, wenn die Knoten über orthogonale Nachbarschaften (gemeinsame Kanten) zusammenhängen.</summary>
    private static bool IsConnected(Puzzle puzzle, IList<int> nodes)
    {
        var seen = new HashSet<int> { nodes[0] };
        var queue = new Queue<int>(seen);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in nodes.Where(n => !seen.Contains(n) && AreOrthogonalNeighbors(puzzle, current, n)))
            {
                seen.Add(next);
                queue.Enqueue(next);
            }
        }

        return seen.Count == nodes.Count;
    }

    [Fact]
    public void The_required_examples_of_package_5_exist()
    {
        Directory.GetFiles(TestPuzzles.PuzzlesDirectory, "*.json").Select(Path.GetFileName).Should().Contain(
            ["killer-sudoku.json", "kropki-sudoku.json", "hexadoku-16x16.json", "dreiecks-sudoku.json", "thermo-sudoku-6x6.json"]);
    }

    [Theory]
    [MemberData(nameof(AllFiles))]
    public void Every_example_loads_validates_and_is_solvable_and_unique(string file)
    {
        var puzzle = PuzzleSerializer.Load(Path.Combine(TestPuzzles.PuzzlesDirectory, file));
        PuzzleValidator.Validate(puzzle).Should().BeEmpty();
        puzzle.Name.Should().NotBeNullOrWhiteSpace();
        puzzle.Nodes.Select(n => (n.X, n.Y)).Should().OnlyHaveUniqueItems("jede Gitterposition ist höchstens einmal belegt");

        var count = Solver.CountSolutions(puzzle, limit: 2);

        if (file == EmptyGrid)
        {
            count.IsAmbiguous.Should().BeTrue("das leere Gitter ist absichtlich mehrdeutig");
            return;
        }

        count.Status.Should().Be(CountStatus.Completed, count.Message);
        count.Count.Should().Be(1, $"{file} muss eindeutig sein ({count.Message})");
        count.IsUnique.Should().BeTrue();
        SolutionVerifier.Violations(puzzle, count.Solutions[0]).Should().BeEmpty("Givens und alle Regeln müssen zur Lösung passen");
    }

    [Fact]
    public void Killer_sudoku_has_sum_cages_covering_every_cell_once_and_few_givens()
    {
        var puzzle = TestPuzzles.Load("killer-sudoku.json");
        var cages = puzzle.Groups.Where(g => g.Rule == SumRule.Name).ToList();

        puzzle.Grid.Should().Be(GridType.Square);
        puzzle.Nodes.Should().HaveCount(81);
        puzzle.Groups.Count(g => g.Rule == AllDifferentRule.Name).Should().Be(27, "Zeilen, Spalten und Blöcke");
        Givens(puzzle).Should().BeLessThanOrEqualTo(5, "ein Killer-Sudoku hat wenige oder keine Givens");
        cages.Should().HaveCountGreaterThan(15);
        cages.SelectMany(c => c.Nodes).Should().BeEquivalentTo(puzzle.Nodes.Select(n => n.Id), "jede Zelle liegt in genau einem Käfig");
        cages.Should().OnlyContain(c => c.Nodes.Count >= 2 && c.Nodes.Count <= 6);
        cages.Should().OnlyContain(c => !c.Params.GetBool("allowRepeats", false), "Ziffern wiederholen sich im Käfig nicht");
        cages.Should().OnlyContain(c => IsConnected(puzzle, c.Nodes), "Käfige sind zusammenhängend");
        cages.Should().OnlyContain(c => c.Color != null && c.Label != null && c.Label.StartsWith("Käfig ", StringComparison.Ordinal));

        // Benachbarte Käfige haben verschiedene Farben.
        var cageOf = cages.SelectMany(c => c.Nodes.Select(n => (Node: n, Cage: c))).ToDictionary(x => x.Node, x => x.Cage);
        foreach (var a in puzzle.Nodes)
        {
            foreach (var b in puzzle.Nodes.Where(b => AreOrthogonalNeighbors(puzzle, a.Id, b.Id) && cageOf[a.Id] != cageOf[b.Id]))
            {
                cageOf[a.Id].Color.Should().NotBe(cageOf[b.Id].Color, $"Käfige an Knoten {a.Id} und {b.Id} grenzen aneinander");
            }
        }

        var solution = UniqueSolution(puzzle);
        cages.Should().OnlyContain(c => c.Nodes.Sum(n => solution[n]) == c.Params.GetInt("target", 0));
    }

    [Fact]
    public void Kropki_sudoku_has_black_and_white_dots_between_orthogonal_neighbors()
    {
        var puzzle = TestPuzzles.Load("kropki-sudoku.json");
        var solution = UniqueSolution(puzzle);

        puzzle.Groups.Should().HaveCount(27).And.OnlyContain(g => g.Rule == AllDifferentRule.Name);
        puzzle.Edges.Should().Contain(e => e.Rule == RatioRule.Name).And.Contain(e => e.Rule == DifferenceRule.Name);
        foreach (var edge in puzzle.Edges)
        {
            AreOrthogonalNeighbors(puzzle, edge.From, edge.To).Should().BeTrue($"{edge} verbindet Nachbarn");
            int a = solution[edge.From], b = solution[edge.To];
            if (edge.Rule == RatioRule.Name)
            {
                edge.Params.GetInt("factor", 2).Should().Be(2, "schwarzer Punkt");
                (a == 2 * b || b == 2 * a).Should().BeTrue();
            }
            else
            {
                edge.Rule.Should().Be(DifferenceRule.Name);
                edge.Params.GetInt("k", 0).Should().Be(1, "weißer Punkt");
                edge.Params.GetString("mode", DifferenceRule.ModeExact).Should().Be(DifferenceRule.ModeExact);
                Math.Abs(a - b).Should().Be(1);
            }
        }

        puzzle.Edges.Select(e => (Math.Min(e.From, e.To), Math.Max(e.From, e.To))).Should().OnlyHaveUniqueItems("höchstens ein Punkt pro Zellpaar");
    }

    [Fact]
    public void Hexadoku_has_256_cells_with_values_0_to_15_and_48_cover_range_groups()
    {
        var puzzle = TestPuzzles.Load("hexadoku-16x16.json");

        puzzle.Grid.Should().Be(GridType.Square);
        puzzle.Domain.Should().Be(new ValueDomain(0, 15));
        puzzle.Nodes.Should().HaveCount(256).And.OnlyContain(n => n.X >= 0 && n.X < 16 && n.Y >= 0 && n.Y < 16 && n.Domain == null);
        puzzle.Groups.Should().HaveCount(48).And.OnlyContain(g => g.Rule == CoverRangeRule.Name && g.Nodes.Count == 16);

        var expected = new List<HashSet<(int, int)>>();
        for (var i = 0; i < 16; i++)
        {
            expected.Add([.. Enumerable.Range(0, 16).Select(c => (c, i))]);
            expected.Add([.. Enumerable.Range(0, 16).Select(r => (i, r))]);
            expected.Add([.. Enumerable.Range(0, 16).Select(k => ((i % 4 * 4) + (k % 4), (i / 4 * 4) + (k / 4)))]);
        }

        var actual = puzzle.Groups.Select(g => g.Nodes.Select(id => (puzzle.FindNode(id)!.X, puzzle.FindNode(id)!.Y)).ToHashSet()).ToList();
        foreach (var set in expected)
        {
            actual.Should().Contain(a => a.SetEquals(set), "jede Zeile, Spalte und jeder 4×4-Block ist eine Gruppe");
        }

        var solution = UniqueSolution(puzzle);
        solution.Values.Should().OnlyContain(v => v >= 0 && v <= 15);
    }

    [Fact]
    public void Triangle_puzzle_sits_on_valid_triangle_coordinates_with_nine_regions_and_three_sides()
    {
        var puzzle = TestPuzzles.Load("dreiecks-sudoku.json");

        puzzle.Grid.Should().Be(GridType.Triangle);
        puzzle.Domain.Should().Be(new ValueDomain(1, 9));
        puzzle.Nodes.Should().HaveCount(81).And.OnlyContain(n => n.Y >= 0 && n.Y < 9 && Math.Abs(n.X) <= n.Y);
        for (var y = 0; y < 9; y++)
        {
            puzzle.Nodes.Count(n => n.Y == y).Should().Be((2 * y) + 1, $"Zeile {y} des großen Dreiecks");
        }

        // Alle Zellen am Rand der Zeile zeigen nach oben – sonst wäre das große Dreieck gezackt.
        puzzle.Nodes.Where(n => Math.Abs(n.X) == n.Y).Should().OnlyContain(n => GridGeometry.IsUpTriangle(n.X, n.Y));

        var regions = puzzle.Groups.Where(g => g.Rule == CoverRangeRule.Name).ToList();
        regions.Should().HaveCount(9);
        regions.SelectMany(r => r.Nodes).Should().BeEquivalentTo(puzzle.Nodes.Select(n => n.Id), "die Regionen zerlegen das Dreieck");
        foreach (var region in regions)
        {
            region.Nodes.Should().HaveCount(9);
            IsConnected(puzzle, region.Nodes).Should().BeTrue($"{region} hängt über gemeinsame Kanten zusammen");
            var up = region.Nodes.Count(id => GridGeometry.IsUpTriangle(puzzle.FindNode(id)!.X, puzzle.FindNode(id)!.Y));
            up.Should().BeOneOf([6, 3], $"{region} ist selbst ein Dreieck der Seitenlänge 3 (6 + 3 kleine Dreiecke)");
        }

        regions.Count(r => r.Nodes.Count(id => GridGeometry.IsUpTriangle(puzzle.FindNode(id)!.X, puzzle.FindNode(id)!.Y)) == 6)
            .Should().Be(6, "6 Regionen zeigen nach oben, 3 nach unten");

        var sides = puzzle.Groups.Where(g => g.Rule == AllDifferentRule.Name).ToDictionary(g => g.Label!);
        sides.Keys.Should().BeEquivalentTo(["Linke Seite", "Rechte Seite", "Grundseite"]);
        sides["Linke Seite"].Nodes.Should().BeEquivalentTo(puzzle.Nodes.Where(n => n.X == -n.Y).Select(n => n.Id));
        sides["Rechte Seite"].Nodes.Should().BeEquivalentTo(puzzle.Nodes.Where(n => n.X == n.Y).Select(n => n.Id));
        sides["Grundseite"].Nodes.Should().BeEquivalentTo(puzzle.Nodes.Where(n => n.Y == 8 && GridGeometry.IsUpTriangle(n.X, n.Y)).Select(n => n.Id));

        // Ungleich-Kanten: sich berührende Dreiecke (gemeinsame Ecke) aus verschiedenen Regionen, jedes solche Paar genau einmal.
        var regionOf = regions.SelectMany(r => r.Nodes.Select(n => (Node: n, Region: r.Id))).ToDictionary(x => x.Node, x => x.Region);
        var expectedPairs = new HashSet<(int, int)>();
        foreach (var a in puzzle.Nodes)
        {
            foreach (var p in GridGeometry.GetDiagonalNeighbors(GridType.Triangle, a.X, a.Y))
            {
                var b = puzzle.Nodes.FirstOrDefault(n => n.X == p.X && n.Y == p.Y);
                if (b is not null && regionOf[a.Id] != regionOf[b.Id])
                {
                    expectedPairs.Add((Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id)));
                }
            }
        }

        puzzle.Edges.Should().OnlyContain(e => e.Rule == NotEqualRule.Name);
        puzzle.Edges.Select(e => (Math.Min(e.From, e.To), Math.Max(e.From, e.To))).Should().BeEquivalentTo(expectedPairs);

        var solution = UniqueSolution(puzzle);
        foreach (var region in regions)
        {
            region.Nodes.Select(id => solution[id]).Should().BeEquivalentTo(Enumerable.Range(1, 9));
        }
    }

    [Fact]
    public void Thermo_sudoku_has_thermometers_and_arrows_along_orthogonal_neighbors()
    {
        var puzzle = TestPuzzles.Load("thermo-sudoku-6x6.json");
        var solution = UniqueSolution(puzzle);

        puzzle.Domain.Should().Be(new ValueDomain(1, 6));
        puzzle.Nodes.Should().HaveCount(36);
        puzzle.Groups.Count(g => g.Rule == AllDifferentRule.Name).Should().Be(18);
        var thermometers = puzzle.Groups.Where(g => g.Rule == ThermometerRule.Name).ToList();
        thermometers.Should().NotBeEmpty();
        foreach (var thermometer in thermometers)
        {
            thermometer.Nodes.Zip(thermometer.Nodes.Skip(1)).Should().OnlyContain(p => AreOrthogonalNeighbors(puzzle, p.First, p.Second));
            thermometer.Nodes.Select(id => solution[id]).Should().BeInAscendingOrder();
        }

        puzzle.Edges.Should().NotBeEmpty().And.OnlyContain(e => e.Rule == LessThanRule.Name && AreOrthogonalNeighbors(puzzle, e.From, e.To));
        puzzle.Edges.Should().OnlyContain(e => solution[e.From] < solution[e.To]);
    }
}
