using System.Diagnostics;
using GraphDoku.Core.Generation;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;
using GraphDoku.Core.Serialization;
using GraphDoku.Core.Validation;
using GraphDoku.Solver.OrTools;
using Xunit.Abstractions;

namespace GraphDoku.Explainer.Tests.Generation;

/// <summary>
/// Erzeugt die Beispielrätsel aus Paket 5 (<c>puzzles/</c>). Nur Testcode, nicht Teil der App.
/// <para>
/// Läuft nur, wenn die Umgebungsvariable <c>GRAPHDOKU_GENERATE_PUZZLES</c> auf ein Ausgabeverzeichnis zeigt; im normalen
/// Testlauf kehren die Tests sofort zurück. Aufruf z. B.:
/// <c>GRAPHDOKU_GENERATE_PUZZLES=/tmp/out dotnet test tests/GraphDoku.Explainer.Tests -c Release --filter "FullyQualifiedName~ExamplePuzzleGenerator.Killer"</c>.
/// </para>
/// <para>
/// Vorgehen: zufällige Lösung (CP-SAT mit zufälligen Startwerten bzw. aus der Lösung abgeleitete Käfige/Punkte), dann so
/// lange Givens aus dieser Lösung ergänzen, bis der Erklär-Modus das Rätsel ohne Verzweigung löst (damit ist es auch
/// eindeutig), anschließend Givens in zufälliger Reihenfolge wieder entfernen, solange das so bleibt. Die Ergebnisse
/// werden als statisches JSON gespeichert; CP-SAT arbeitet mit mehreren Threads, deshalb ist ein erneuter Lauf nicht
/// zwingend bitgleich.
/// </para>
/// </summary>
public sealed class ExamplePuzzleGenerator(ITestOutputHelper output)
{
    private const string OutputVariable = "GRAPHDOKU_GENERATE_PUZZLES";

    private static readonly OrToolsSolver Solver = new();
    private static readonly ExplanationEngine Engine = new(Solver);

    /// <summary>
    /// Farben für Käfige und Regionen aus der Gruppenpalette des Editors (Blau, Orange, Grün, Rot, Violett, Türkis, Pink),
    /// damit die Farbauswahl im Eigenschaften-Panel sie kennt; benachbarte Gruppen bekommen verschiedene Farben.
    /// </summary>
    private static readonly string[] GroupColors = ["#3b82f6", "#f97316", "#16a34a", "#dc2626", "#9333ea", "#0891b2", "#db2777"];

    private static string? OutputDirectory => Environment.GetEnvironmentVariable(OutputVariable) is { Length: > 0 } dir ? dir : null;

    [Fact]
    public void Killer()
    {
        if (OutputDirectory is null)
        {
            return;
        }

        var rng = new Random(5_001);
        Puzzle? best = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var structure = SquareSudoku(3, 3, 1, AllDifferentRule.Name, "Killer-Sudoku");
            var solution = RandomSolution(structure, rng);
            var puzzle = structure.Clone();
            AddCages(puzzle, solution, rng);
            AddGivensUntilExplainable(puzzle, solution);
            MinimizeGivens(puzzle, rng);
            var givens = puzzle.Nodes.Count(n => n.Given is not null);
            Log($"Killer Versuch {attempt + 1}: {puzzle.Groups.Count - 27} Käfige, {givens} Givens");
            if (best is null || givens < best.Nodes.Count(n => n.Given is not null))
            {
                best = puzzle;
            }

            if (givens == 0)
            {
                break;
            }
        }

        Save(best!, "killer-sudoku.json");
    }

    [Fact]
    public void Kropki()
    {
        if (OutputDirectory is null)
        {
            return;
        }

        var rng = new Random(5_002);
        var puzzle = SquareSudoku(3, 3, 1, AllDifferentRule.Name, "Kropki-Sudoku");
        var solution = RandomSolution(puzzle, rng);
        var pairs = new EdgeGenerator().Generate(puzzle, new EdgeGenerationRequest(DistanceRule.Orthogonal, NotEqualRule.Name)).Edges;
        foreach (var pair in pairs)
        {
            int a = solution[pair.From], b = solution[pair.To];
            if (a == 2 * b || b == 2 * a)
            {
                // Schwarzer Punkt (auch bei 1–2, das auch ein weißer Punkt sein könnte).
                puzzle.Edges.Add(new Edge(puzzle.NextEdgeId(), pair.From, pair.To, RatioRule.Name, new RuleParams().Set("factor", 2)));
            }
            else if (Math.Abs(a - b) == 1)
            {
                puzzle.Edges.Add(new Edge(puzzle.NextEdgeId(), pair.From, pair.To, DifferenceRule.Name, new RuleParams().Set("k", 1).Set("mode", DifferenceRule.ModeExact)));
            }
        }

        Save(BestOfRestarts(puzzle, solution, rng, restarts: 20), "kropki-sudoku.json");
    }

    [Fact]
    public void Hexadoku()
    {
        if (OutputDirectory is null)
        {
            return;
        }

        var rng = new Random(5_003);
        var puzzle = SquareSudoku(4, 4, 0, CoverRangeRule.Name, "Hexadoku 16x16");
        var solution = RandomSolution(puzzle, rng);
        Save(BestOfRestarts(puzzle, solution, rng, restarts: 6), "hexadoku-16x16.json");
    }

    [Fact]
    public void Triangle()
    {
        if (OutputDirectory is null)
        {
            return;
        }

        var rng = new Random(5_004);
        var puzzle = TriangleSudoku();
        var solution = RandomSolution(puzzle, rng);
        Save(BestOfRestarts(puzzle, solution, rng, restarts: 40), "dreiecks-sudoku.json");
    }

    [Fact]
    public void Thermo()
    {
        if (OutputDirectory is null)
        {
            return;
        }

        var rng = new Random(5_005);
        var puzzle = SquareSudoku(3, 2, 1, AllDifferentRule.Name, "Thermo-Sudoku 6x6");
        var solution = RandomSolution(puzzle, rng);
        AddThermometersAndArrows(puzzle, solution, rng, thermometers: 3, arrows: 4);
        Save(BestOfRestarts(puzzle, solution, rng, restarts: 20), "thermo-sudoku-6x6.json");
    }

    // ---------------------------------------------------------------- Strukturen

    /// <summary>
    /// Quadratisches Sudoku mit Blöcken aus <paramref name="boxHeight"/> Zeilen × <paramref name="boxWidth"/> Spalten,
    /// Werte <paramref name="min"/> .. min + n − 1. Knoten zeilenweise (x = Spalte, y = Zeile), Gruppen „Zeile n“,
    /// „Spalte n“, „Block n“ wie bei der Vorlage.
    /// </summary>
    internal static Puzzle SquareSudoku(int boxWidth, int boxHeight, int min, string rule, string name)
    {
        var size = boxWidth * boxHeight;
        var puzzle = new Puzzle(name, GridType.Square, new ValueDomain(min, min + size - 1));
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                puzzle.Nodes.Add(new Node((row * size) + column + 1, column, row));
            }
        }

        int Id(int row, int column) => (row * size) + column + 1;
        for (var row = 0; row < size; row++)
        {
            puzzle.Groups.Add(new Group(puzzle.NextGroupId(), Enumerable.Range(0, size).Select(c => Id(row, c)), rule, label: $"Zeile {row + 1}"));
        }

        for (var column = 0; column < size; column++)
        {
            puzzle.Groups.Add(new Group(puzzle.NextGroupId(), Enumerable.Range(0, size).Select(r => Id(r, column)), rule, label: $"Spalte {column + 1}"));
        }

        for (var block = 0; block < size; block++)
        {
            var top = block / boxHeight * boxHeight;
            var left = block % boxHeight * boxWidth;
            var nodes = Enumerable.Range(0, size).Select(i => Id(top + (i / boxWidth), left + (i % boxWidth)));
            puzzle.Groups.Add(new Group(puzzle.NextGroupId(), nodes, rule, label: $"Block {block + 1}"));
        }

        return puzzle;
    }

    /// <summary>
    /// Dreiecks-Sudoku: großes Dreieck mit Seitenlänge 9 aus 81 kleinen Dreiecken (Zeile y hat x = −y .. y), Werte 1–9.
    /// Gruppen: 9 Regionen (Dreiecke der Seitenlänge 3, <c>CoverRange</c>), die drei Außenseiten (je 9 Dreiecke mit
    /// einer Kante auf der Seite, <c>AllDifferent</c>) und <c>NotEqual</c> zwischen kantenbenachbarten Dreiecken
    /// verschiedener Regionen (mit dem Kanten-Generator erzeugt).
    /// </summary>
    internal static Puzzle TriangleSudoku()
    {
        var puzzle = new Puzzle("Dreiecks-Sudoku", GridType.Triangle, new ValueDomain(1, 9));
        for (var y = 0; y < 9; y++)
        {
            for (var x = -y; x <= y; x++)
            {
                puzzle.Nodes.Add(new Node(puzzle.NextNodeId(), x, y));
            }
        }

        var regions = TriangleRegions(puzzle);
        var regionOf = new Dictionary<int, int>();
        var regionColors = ColorGroups(regions.Select(r => r.Nodes).ToList(), puzzle);
        for (var i = 0; i < regions.Count; i++)
        {
            puzzle.Groups.Add(new Group(puzzle.NextGroupId(), regions[i].Nodes, CoverRangeRule.Name, color: regionColors[i], label: $"Dreieck {i + 1}"));
            foreach (var id in regions[i].Nodes)
            {
                regionOf[id] = i;
            }
        }

        var nodes = puzzle.Nodes;
        puzzle.Groups.Add(new Group(puzzle.NextGroupId(), nodes.Where(n => n.X == -n.Y).OrderBy(n => n.Y).Select(n => n.Id), AllDifferentRule.Name, label: "Linke Seite"));
        puzzle.Groups.Add(new Group(puzzle.NextGroupId(), nodes.Where(n => n.X == n.Y).OrderBy(n => n.Y).Select(n => n.Id), AllDifferentRule.Name, label: "Rechte Seite"));
        puzzle.Groups.Add(new Group(puzzle.NextGroupId(), nodes.Where(n => n.Y == 8 && GridGeometry.IsUpTriangle(n.X, n.Y)).OrderBy(n => n.X).Select(n => n.Id), AllDifferentRule.Name, label: "Grundseite"));

        var neighbors = new EdgeGenerator().Generate(puzzle, new EdgeGenerationRequest(DistanceRule.WithDiagonals, NotEqualRule.Name)).Edges;
        foreach (var edge in neighbors.Where(e => regionOf[e.From] != regionOf[e.To]))
        {
            puzzle.Edges.Add(new Edge(puzzle.NextEdgeId(), edge.From, edge.To, NotEqualRule.Name));
        }

        return puzzle;
    }

    /// <summary>
    /// Die 9 Regionen des großen Dreiecks: Ein kleines Dreieck gehört zu dem Dreieck der Seitenlänge 3 (grobes
    /// Dreiecksgitter, um den Faktor 3 vergrößert), das seinen Schwerpunkt enthält. Reihenfolge zeilenweise.
    /// </summary>
    internal static List<(int X, int Y, List<int> Nodes)> TriangleRegions(Puzzle puzzle)
    {
        var regions = new List<(int X, int Y, List<int> Nodes)>();
        for (var cy = 0; cy < 3; cy++)
        {
            for (var cx = -cy; cx <= cy; cx++)
            {
                var corners = GridGeometry.GetCellVertices(GridType.Triangle, cx, cy).Select(p => new WorldPoint(p.X * 3, p.Y * 3)).ToArray();
                var members = puzzle.Nodes
                    .Where(n => Contains(corners, GridGeometry.ToWorld(GridType.Triangle, n.X, n.Y)))
                    .Select(n => n.Id)
                    .ToList();
                regions.Add((cx, cy, members));
            }
        }

        return regions;
    }

    private static bool Contains(WorldPoint[] triangle, WorldPoint p)
    {
        static double Cross(WorldPoint a, WorldPoint b, WorldPoint c) => ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
        var d1 = Cross(triangle[0], triangle[1], p);
        var d2 = Cross(triangle[1], triangle[2], p);
        var d3 = Cross(triangle[2], triangle[0], p);
        return (d1 > 0 && d2 > 0 && d3 > 0) || (d1 < 0 && d2 < 0 && d3 < 0);
    }

    /// <summary>Zufällige Käfige (2–5 Zellen, zusammenhängend, ohne doppelte Lösungswerte) als <c>Sum</c>-Gruppen.</summary>
    private static void AddCages(Puzzle puzzle, IReadOnlyDictionary<int, int> solution, Random rng)
    {
        var byPoint = puzzle.Nodes.ToDictionary(n => new GridPoint(n.X, n.Y));
        var cageOf = new Dictionary<int, int>();
        var cages = new List<List<int>>();
        int[] sizes = [2, 2, 2, 3, 3, 3, 4, 4];
        foreach (var start in puzzle.Nodes.OrderBy(_ => rng.Next()))
        {
            if (cageOf.ContainsKey(start.Id))
            {
                continue;
            }

            var cage = new List<int> { start.Id };
            cageOf[start.Id] = cages.Count;
            var target = sizes[rng.Next(sizes.Length)];
            while (cage.Count < target)
            {
                var options = cage
                    .SelectMany(id => Neighbors(puzzle, byPoint, id))
                    .Where(id => !cageOf.ContainsKey(id) && cage.All(c => solution[c] != solution[id]))
                    .Distinct()
                    .ToList();
                if (options.Count == 0)
                {
                    break;
                }

                var next = options[rng.Next(options.Count)];
                cage.Add(next);
                cageOf[next] = cages.Count;
            }

            cages.Add(cage);
        }

        // Einzelne Zellen an einen Nachbarkäfig hängen, wenn es geht (ein Käfig aus einer Zelle verrät den Wert).
        foreach (var single in cages.Where(c => c.Count == 1).ToList())
        {
            var id = single[0];
            var host = Neighbors(puzzle, byPoint, id)
                .Select(n => cageOf[n])
                .Distinct()
                .Select(i => cages[i])
                .Where(c => c.Count is > 1 and < 6 && c.All(m => solution[m] != solution[id]))
                .OrderBy(c => c.Count)
                .FirstOrDefault();
            if (host is not null)
            {
                host.Add(id);
                single.Clear();
                cageOf[id] = cages.IndexOf(host);
            }
        }

        cages.RemoveAll(c => c.Count == 0);
        cages = [.. cages.Select(c => c.Order().ToList()).OrderBy(c => c[0])];
        var colors = ColorGroups(cages, puzzle);
        for (var i = 0; i < cages.Count; i++)
        {
            var parameters = new RuleParams().Set("target", cages[i].Sum(id => solution[id]));
            puzzle.Groups.Add(new Group(puzzle.NextGroupId(), cages[i], SumRule.Name, parameters, colors[i], $"Käfig {i + 1}"));
        }
    }

    /// <summary>Thermometer (steigende Lösungswerte entlang orthogonaler Nachbarn) und „Kleiner als“-Kanten.</summary>
    private static void AddThermometersAndArrows(Puzzle puzzle, IReadOnlyDictionary<int, int> solution, Random rng, int thermometers, int arrows)
    {
        var byPoint = puzzle.Nodes.ToDictionary(n => new GridPoint(n.X, n.Y));
        var used = new HashSet<int>();
        var made = 0;
        for (var attempt = 0; attempt < 500 && made < thermometers; attempt++)
        {
            var start = puzzle.Nodes[rng.Next(puzzle.Nodes.Count)].Id;
            if (used.Contains(start))
            {
                continue;
            }

            var path = new List<int> { start };
            var length = rng.Next(3, 6);
            while (path.Count < length)
            {
                var options = Neighbors(puzzle, byPoint, path[^1])
                    .Where(id => !used.Contains(id) && !path.Contains(id) && solution[id] > solution[path[^1]])
                    .ToList();
                if (options.Count == 0)
                {
                    break;
                }

                path.Add(options[rng.Next(options.Count)]);
            }

            if (path.Count < 3)
            {
                continue;
            }

            used.UnionWith(path);
            made++;
            puzzle.Groups.Add(new Group(puzzle.NextGroupId(), path, ThermometerRule.Name, label: $"Thermometer {made}"));
        }

        var pairs = new EdgeGenerator().Generate(puzzle, new EdgeGenerationRequest(DistanceRule.Orthogonal, LessThanRule.Name)).Edges
            .Where(e => !used.Contains(e.From) && !used.Contains(e.To))
            .OrderBy(_ => rng.Next())
            .ToList();
        var touched = new HashSet<int>();
        foreach (var pair in pairs)
        {
            if (arrows == 0)
            {
                break;
            }

            if (!touched.Add(pair.From) | !touched.Add(pair.To))
            {
                continue;
            }

            var (from, to) = solution[pair.From] < solution[pair.To] ? (pair.From, pair.To) : (pair.To, pair.From);
            puzzle.Edges.Add(new Edge(puzzle.NextEdgeId(), from, to, LessThanRule.Name));
            arrows--;
        }

        // Ids der Kanten in Leserichtung des From-Knotens sortieren, damit die Datei übersichtlich bleibt.
        var sorted = puzzle.Edges.OrderBy(e => e.From).ThenBy(e => e.To).ToList();
        puzzle.Edges.Clear();
        foreach (var edge in sorted)
        {
            puzzle.Edges.Add(new Edge(puzzle.NextEdgeId(), edge.From, edge.To, edge.Rule, edge.Params));
        }
    }

    private static IEnumerable<int> Neighbors(Puzzle puzzle, Dictionary<GridPoint, Node> byPoint, int id)
    {
        var node = puzzle.FindNode(id)!;
        return GridGeometry.GetOrthogonalNeighbors(puzzle.Grid, node.X, node.Y)
            .Where(byPoint.ContainsKey)
            .Select(p => byPoint[p].Id);
    }

    /// <summary>Gierige Färbung: benachbarte Gruppen (orthogonal benachbarte Zellen) bekommen verschiedene Farben.</summary>
    private static string[] ColorGroups(List<List<int>> groups, Puzzle puzzle)
    {
        var byPoint = puzzle.Nodes.ToDictionary(n => new GridPoint(n.X, n.Y));
        var groupOf = new Dictionary<int, int>();
        for (var i = 0; i < groups.Count; i++)
        {
            foreach (var id in groups[i])
            {
                groupOf[id] = i;
            }
        }

        var colors = new string[groups.Count];
        for (var i = 0; i < groups.Count; i++)
        {
            var taken = groups[i]
                .SelectMany(id => Neighbors(puzzle, byPoint, id))
                .Where(groupOf.ContainsKey)
                .Select(id => groupOf[id])
                .Where(g => g != i && colors[g] is not null)
                .Select(g => colors[g])
                .ToHashSet();
            colors[i] = GroupColors.First(c => !taken.Contains(c));
        }

        return colors;
    }

    // ---------------------------------------------------------------- Lösung und Givens

    /// <summary>Zufällige Lösung: zufällige Startwerte setzen, solange das Rätsel lösbar bleibt, dann von CP-SAT lösen lassen.</summary>
    private static Dictionary<int, int> RandomSolution(Puzzle structure, Random rng)
    {
        var puzzle = structure.Clone();
        foreach (var node in puzzle.Nodes)
        {
            node.Given = null;
        }

        var seeds = Math.Max(4, puzzle.Nodes.Count / 6);
        foreach (var node in puzzle.Nodes.OrderBy(_ => rng.Next()).Take(seeds))
        {
            var domain = puzzle.GetEffectiveDomain(node);
            foreach (var value in Enumerable.Range(domain.Min, (int)domain.Size).OrderBy(_ => rng.Next()))
            {
                node.Given = value;
                if (Solver.Solve(puzzle).IsSolved)
                {
                    break;
                }

                node.Given = null;
            }
        }

        var result = Solver.Solve(puzzle);
        result.IsSolved.Should().BeTrue(result.Message);
        return result.Values!.ToDictionary();
    }

    private static bool IsExplainableWithoutBranching(Puzzle puzzle, out ExplanationResult result)
    {
        result = Engine.Explain(puzzle);
        return result.Status == ExplanationStatus.Solved && !result.UsedBranching;
    }

    /// <summary>Ergänzt Givens aus der Lösung an den Knoten, an denen der Erklär-Modus verzweigen müsste.</summary>
    private void AddGivensUntilExplainable(Puzzle puzzle, IReadOnlyDictionary<int, int> solution)
    {
        var watch = Stopwatch.StartNew();
        while (!IsExplainableWithoutBranching(puzzle, out var result))
        {
            var branch = result.Steps.FirstOrDefault(s => s.Kind == StepKind.Branch)
                ?? throw new InvalidOperationException($"Unerwartetes Ergebnis: {result}");
            puzzle.FindNode(branch.NodeId!.Value)!.Given = solution[branch.NodeId.Value];
        }

        Log($"{puzzle.Name}: {puzzle.Nodes.Count(n => n.Given is not null)} Givens nach dem Ergänzen ({watch.Elapsed.TotalSeconds:0.0} s)");
    }

    /// <summary>Entfernt Givens in zufälliger Reihenfolge, solange der Erklär-Modus ohne Verzweigung auskommt.</summary>
    private void MinimizeGivens(Puzzle puzzle, Random rng)
    {
        var watch = Stopwatch.StartNew();
        IsExplainableWithoutBranching(puzzle, out _).Should().BeTrue();
        foreach (var node in puzzle.Nodes.Where(n => n.Given is not null).OrderBy(_ => rng.Next()).ToList())
        {
            var given = node.Given;
            node.Given = null;
            if (!IsExplainableWithoutBranching(puzzle, out _))
            {
                node.Given = given;
            }
        }

        Log($"{puzzle.Name}: {puzzle.Nodes.Count(n => n.Given is not null)} Givens nach dem Verkleinern ({watch.Elapsed.TotalSeconds:0.0} s)");
    }

    /// <summary>Mehrere Verkleinerungen ab dem voll ausgefüllten Rätsel in verschiedenen Reihenfolgen; das Rätsel mit den wenigsten Givens gewinnt.</summary>
    private Puzzle BestOfRestarts(Puzzle structure, IReadOnlyDictionary<int, int> solution, Random rng, int restarts)
    {
        Puzzle? best = null;
        for (var i = 0; i < restarts; i++)
        {
            var puzzle = structure.Clone();
            foreach (var node in puzzle.Nodes)
            {
                node.Given = solution[node.Id];
            }

            MinimizeGivens(puzzle, rng);
            if (best is null || puzzle.Nodes.Count(n => n.Given is not null) < best.Nodes.Count(n => n.Given is not null))
            {
                best = puzzle;
            }
        }

        return best!;
    }

    private void Save(Puzzle puzzle, string fileName)
    {
        PuzzleValidator.Validate(puzzle).Should().BeEmpty();
        var count = Solver.CountSolutions(puzzle, 2);
        count.IsUnique.Should().BeTrue(count.Message);
        var path = Path.Combine(OutputDirectory!, fileName);
        Directory.CreateDirectory(OutputDirectory!);
        PuzzleSerializer.Save(puzzle, path);
        Log($"{fileName}: {puzzle.Nodes.Count} Knoten, {puzzle.Edges.Count} Kanten, {puzzle.Groups.Count} Gruppen, {puzzle.Nodes.Count(n => n.Given is not null)} Givens → {path}");
    }

    private void Log(string text)
    {
        output.WriteLine(text);
        Console.WriteLine(text);
    }
}
