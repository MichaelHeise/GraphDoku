using GraphDoku.Core.Generation;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules.Catalog;
using GraphDoku.Core.Templates;
using GraphDoku.Core.Validation;

namespace GraphDoku.Core.Tests;

public class EdgeGeneratorTests
{
    private static readonly EdgeGenerator Generator = new();

    /// <summary>Rechteck aus Knoten (Ids zeilenweise ab 1) auf einem beliebigen Gitter.</summary>
    private static Puzzle Patch(GridType grid, int width, int height, int left = 0, int top = 0)
    {
        var puzzle = new Puzzle("Test", grid, new ValueDomain(1, 9));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                puzzle.Nodes.Add(new Node(puzzle.NextNodeId(), left + x, top + y));
            }
        }

        return puzzle;
    }

    /// <summary>Großes, nach oben zeigendes Dreieck mit Seitenlänge <paramref name="size"/> (size² kleine Dreiecke).</summary>
    private static Puzzle BigTriangle(int size)
    {
        var puzzle = new Puzzle("Dreieck", GridType.Triangle, new ValueDomain(1, 9));
        for (var y = 0; y < size; y++)
        {
            for (var x = -y; x <= y; x++)
            {
                puzzle.Nodes.Add(new Node(puzzle.NextNodeId(), x, y));
            }
        }

        return puzzle;
    }

    /// <summary>Sechseck mit Radius <paramref name="radius"/> um (0, 0) in axialen Koordinaten.</summary>
    private static Puzzle HexPatch(int radius)
    {
        var puzzle = new Puzzle("Waben", GridType.Hex, new ValueDomain(1, 9));
        for (var r = -radius; r <= radius; r++)
        {
            for (var q = -radius; q <= radius; q++)
            {
                if (Math.Abs(q + r) <= radius)
                {
                    puzzle.Nodes.Add(new Node(puzzle.NextNodeId(), q, r));
                }
            }
        }

        return puzzle;
    }

    private static EdgeGenerationResult Generate(Puzzle puzzle, DistanceRule distance, string rule = NotEqualRule.Name, IReadOnlyCollection<int>? nodes = null, RuleParams? parameters = null, EdgeDirectionPolicy direction = EdgeDirectionPolicy.ReadingOrder) =>
        Generator.Generate(puzzle, new EdgeGenerationRequest(distance, rule) { NodeIds = nodes, Params = parameters, Direction = direction });

    private static HashSet<(int, int)> Pairs(IEnumerable<Edge> edges) =>
        [.. edges.Select(e => e.From < e.To ? (e.From, e.To) : (e.To, e.From))];

    /// <summary>Unabhängige Referenz: Anzahl gemeinsamer Eckpunkte der Zellumrisse.</summary>
    private static int SharedVertices(GridType grid, Node a, Node b)
    {
        var va = GridGeometry.GetCellVertices(grid, a.X, a.Y);
        var vb = GridGeometry.GetCellVertices(grid, b.X, b.Y);
        return va.Count(p => vb.Any(q => p.DistanceTo(q) < 1e-9));
    }

    private static HashSet<(int, int)> ReferencePairs(Puzzle puzzle, Func<Node, Node, bool> predicate)
    {
        var result = new HashSet<(int, int)>();
        foreach (var a in puzzle.Nodes)
        {
            foreach (var b in puzzle.Nodes.Where(b => b.Id > a.Id && predicate(a, b)))
            {
                result.Add((a.Id, b.Id));
            }
        }

        return result;
    }

    public static TheoryData<GridType> AllGrids() => new() { GridType.Square, GridType.Triangle, GridType.Hex };

    private static Puzzle SamplePuzzle(GridType grid) => grid switch
    {
        GridType.Square => Patch(grid, 5, 4, left: -1, top: 2),
        GridType.Triangle => Patch(grid, 7, 4, left: -3, top: -1),
        _ => HexPatch(2),
    };

    // ---------------------------------------------------------------- Abstandsregeln, abgeglichen mit Zellumrissen

    [Theory]
    [MemberData(nameof(AllGrids))]
    public void Orthogonal_connects_exactly_the_cells_sharing_an_edge(GridType grid)
    {
        var puzzle = SamplePuzzle(grid);

        var result = Generate(puzzle, DistanceRule.Orthogonal);

        result.IsSuccess.Should().BeTrue(result.Message);
        var expected = ReferencePairs(puzzle, (a, b) => SharedVertices(grid, a, b) == 2);
        expected.Should().NotBeEmpty();
        Pairs(result.Edges).Should().BeEquivalentTo(expected);
        result.Edges.Should().HaveCount(expected.Count, "kein Paar doppelt");
        result.MatchingPairs.Should().Be(expected.Count);
    }

    [Theory]
    [MemberData(nameof(AllGrids))]
    public void With_diagonals_connects_exactly_the_cells_sharing_a_corner(GridType grid)
    {
        var puzzle = SamplePuzzle(grid);

        var result = Generate(puzzle, DistanceRule.WithDiagonals);

        result.IsSuccess.Should().BeTrue(result.Message);
        var expected = ReferencePairs(puzzle, (a, b) => SharedVertices(grid, a, b) >= 1);
        Pairs(result.Edges).Should().BeEquivalentTo(expected);
        result.Edges.Should().HaveCount(expected.Count);
    }

    [Theory]
    [MemberData(nameof(AllGrids))]
    public void Free_distance_connects_pairs_whose_centers_have_that_distance(GridType grid)
    {
        var puzzle = SamplePuzzle(grid);
        var distance = grid switch { GridType.Square => Math.Sqrt(5), GridType.Triangle => 1.0, _ => Math.Sqrt(3) };

        var result = Generate(puzzle, DistanceRule.Exact(distance));

        result.IsSuccess.Should().BeTrue(result.Message);
        var expected = ReferencePairs(puzzle, (a, b) => Math.Abs(GridGeometry.Distance(grid, new(a.X, a.Y), new(b.X, b.Y)) - distance) < 1e-6);
        expected.Should().NotBeEmpty();
        Pairs(result.Edges).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Free_distance_of_edge_neighbors_equals_orthogonal_on_the_triangle_grid()
    {
        var puzzle = BigTriangle(4);

        var orthogonal = Generate(puzzle, DistanceRule.Orthogonal);
        var free = Generate(puzzle, DistanceRule.Exact(1 / Math.Sqrt(3)));

        Pairs(free.Edges).Should().BeEquivalentTo(Pairs(orthogonal.Edges));
        orthogonal.Edges.Should().HaveCount(3 * 4 * 3 / 2, "ein Dreieck der Seitenlänge n hat 3n(n−1)/2 innere Kanten");
    }

    [Fact]
    public void Small_square_counts_are_as_expected()
    {
        var puzzle = Patch(GridType.Square, 3, 3);

        Generate(puzzle, DistanceRule.Orthogonal).Edges.Should().HaveCount(12);
        Generate(puzzle, DistanceRule.WithDiagonals).Edges.Should().HaveCount(20);
        Generate(puzzle, DistanceRule.KnightMove).Edges.Should().HaveCount(8);
        Generate(puzzle, DistanceRule.Exact(Math.Sqrt(2))).Edges.Should().HaveCount(8);
        Generate(puzzle, DistanceRule.Exact(2)).Edges.Should().HaveCount(6);
        Generate(puzzle, DistanceRule.Range(1, 1.5)).Edges.Should().HaveCount(20, "Abstand 1 und √2 liegen im Bereich");
        Generate(puzzle, DistanceRule.Range(1.5, 10)).Edges.Should().HaveCount(36 - 20);
    }

    [Fact]
    public void Knight_move_on_classic_sudoku_gives_224_edges()
    {
        var puzzle = ClassicSudoku.Create();

        var result = Generate(puzzle, DistanceRule.KnightMove);

        result.Edges.Should().HaveCount(4 * 8 * 7);
        result.Edges.Should().OnlyContain(e =>
            GridGeometry.Distance(GridType.Square, Point(puzzle, e.From), Point(puzzle, e.To)) > 2.2 &&
            GridGeometry.Distance(GridType.Square, Point(puzzle, e.From), Point(puzzle, e.To)) < 2.3);
    }

    [Fact]
    public void Hex_neighbors_with_and_without_diagonals_are_the_same()
    {
        var puzzle = HexPatch(1);

        var orthogonal = Generate(puzzle, DistanceRule.Orthogonal);

        orthogonal.Edges.Should().HaveCount(12, "6 Speichen und 6 Ringkanten");
        Pairs(Generate(puzzle, DistanceRule.WithDiagonals).Edges).Should().BeEquivalentTo(Pairs(orthogonal.Edges));
        Pairs(Generate(puzzle, DistanceRule.Exact(1)).Edges).Should().BeEquivalentTo(Pairs(orthogonal.Edges));
    }

    [Theory]
    [InlineData(GridType.Triangle, "triangle")]
    [InlineData(GridType.Hex, "hex")]
    public void Knight_move_is_rejected_on_triangle_and_hex_grids(GridType grid, string name)
    {
        var puzzle = SamplePuzzle(grid);

        var result = Generate(puzzle, DistanceRule.KnightMove);

        result.IsSuccess.Should().BeFalse();
        result.Edges.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Should().Contain("nur für das Quadratgitter").And.Contain($"„{name}“");
        result.Message.Should().StartWith("Es wurden keine Kanten erzeugt: Der Springerzug");
    }

    [Theory]
    [InlineData(0, 0, "größer als 0")]
    [InlineData(-1, 1, "größer als 0")]
    [InlineData(2, 1, "kleiner als der kleinste")]
    [InlineData(double.NaN, 1, "endliche Zahlen")]
    public void Invalid_free_distances_are_rejected(double min, double max, string message)
    {
        var result = Generate(Patch(GridType.Square, 3, 3), DistanceRule.Range(min, max));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void Tolerance_must_be_smaller_than_the_distance()
    {
        Generate(Patch(GridType.Square, 3, 3), DistanceRule.Exact(1, tolerance: 1)).Errors.Should().ContainSingle().Which.Should().Contain("Toleranz");
        Generate(Patch(GridType.Square, 3, 3), DistanceRule.Exact(1, tolerance: -0.1)).Errors.Should().ContainSingle().Which.Should().Contain("Toleranz");
    }

    [Fact]
    public void Tolerance_widens_the_free_distance()
    {
        var puzzle = Patch(GridType.Square, 3, 3);

        Generate(puzzle, DistanceRule.Exact(1.05)).Edges.Should().BeEmpty();
        Generate(puzzle, DistanceRule.Exact(1.05, tolerance: 0.1)).Edges.Should().HaveCount(12);
    }

    [Fact]
    public void Distance_rule_descriptions_are_german()
    {
        DistanceRule.Orthogonal.Description.Should().Be("direkte Nachbarn (gemeinsame Kante)");
        DistanceRule.WithDiagonals.Description.Should().Be("Nachbarn inkl. diagonal (gemeinsame Ecke)");
        DistanceRule.KnightMove.Description.Should().Be("Springerzug");
        DistanceRule.Exact(1.5).Description.Should().Be("Abstand 1,5");
        DistanceRule.Range(1, 2).Description.Should().Be("Abstand 1 bis 2");
    }

    [Fact]
    public void Matches_agrees_with_the_generator()
    {
        var puzzle = SamplePuzzle(GridType.Triangle);
        foreach (var distance in new[] { DistanceRule.Orthogonal, DistanceRule.WithDiagonals, DistanceRule.Exact(1) })
        {
            var expected = ReferencePairs(puzzle, (a, b) => distance.Matches(GridType.Triangle, new(a.X, a.Y), new(b.X, b.Y)));
            Pairs(Generate(puzzle, distance).Edges).Should().BeEquivalentTo(expected, distance.Description);
        }

        DistanceRule.Orthogonal.Matches(GridType.Square, new(1, 1), new(1, 1)).Should().BeFalse();
    }

    // ---------------------------------------------------------------- Knotenmenge

    [Fact]
    public void Only_pairs_inside_the_node_set_are_connected()
    {
        var puzzle = Patch(GridType.Square, 3, 3);
        int[] selection = [1, 2, 4, 5, 9];

        var result = Generate(puzzle, DistanceRule.Orthogonal, nodes: selection);

        Pairs(result.Edges).Should().BeEquivalentTo(new[] { (1, 2), (1, 4), (2, 5), (4, 5) });
    }

    [Fact]
    public void Duplicate_ids_in_the_node_set_are_ignored()
    {
        var puzzle = Patch(GridType.Square, 3, 1);

        var result = Generate(puzzle, DistanceRule.Orthogonal, nodes: [1, 2, 2, 1, 3]);

        result.Edges.Should().HaveCount(2);
    }

    [Fact]
    public void Unknown_nodes_in_the_node_set_are_reported()
    {
        var puzzle = Patch(GridType.Square, 3, 1);

        Generate(puzzle, DistanceRule.Orthogonal, nodes: [1, 7]).Errors.Should().ContainSingle().Which.Should().Be("Der Knoten 7 existiert nicht.");
        Generate(puzzle, DistanceRule.Orthogonal, nodes: [9, 7, 9]).Errors.Should().ContainSingle().Which.Should().Be("Die Knoten 7, 9 existieren nicht.");
    }

    [Fact]
    public void Duplicate_node_ids_in_the_puzzle_are_reported()
    {
        var puzzle = Patch(GridType.Square, 3, 1);
        puzzle.Nodes.Add(new Node(2, 5, 5));

        Generate(puzzle, DistanceRule.Orthogonal).Errors.Should().ContainSingle().Which.Should().Contain("mehrfach vergeben");
    }

    [Fact]
    public void Edge_ids_start_at_one_when_existing_edges_have_invalid_ids()
    {
        var puzzle = Patch(GridType.Square, 3, 1);
        puzzle.Edges.Add(new Edge(-5, 1, 2, RatioRule.Name));

        var result = Generate(puzzle, DistanceRule.Orthogonal);

        result.IsSuccess.Should().BeTrue();
        result.Edges.Select(e => e.Id).Should().Equal(1, 2);
        var errorsBefore = PuzzleValidator.Validate(puzzle);
        errorsBefore.Should().NotBeEmpty("die vorhandene Kante mit Id −5 ist ungültig");
        result.ApplyTo(puzzle);
        PuzzleValidator.Validate(puzzle).Should().BeEquivalentTo(errorsBefore, "der Generator darf keine neuen Fehler erzeugen");
    }

    [Fact]
    public void Edge_ids_continue_after_the_largest_existing_id()
    {
        var puzzle = Patch(GridType.Square, 3, 1);
        puzzle.Edges.Add(new Edge(7, 1, 2, RatioRule.Name));

        Generate(puzzle, DistanceRule.Orthogonal).Edges.Select(e => e.Id).Should().Equal(8, 9);
    }

    [Fact]
    public void Nodes_at_the_same_position_are_never_connected_with_each_other()
    {
        var puzzle = Patch(GridType.Square, 2, 1);
        puzzle.Nodes.Add(new Node(3, 0, 0));

        var result = Generate(puzzle, DistanceRule.Orthogonal);
        var free = Generate(puzzle, DistanceRule.Range(1e-3, 5));

        Pairs(result.Edges).Should().BeEquivalentTo(new[] { (1, 2), (2, 3) });
        Pairs(free.Edges).Should().BeEquivalentTo(new[] { (1, 2), (2, 3) });
    }

    [Fact]
    public void Fewer_than_two_nodes_give_no_edges_with_message()
    {
        var puzzle = Patch(GridType.Square, 3, 3);

        var result = Generate(puzzle, DistanceRule.Orthogonal, nodes: [5]);

        result.IsSuccess.Should().BeTrue();
        result.Edges.Should().BeEmpty();
        result.Message.Should().Be("Keine Kanten erzeugt: Kein Knotenpaar erfüllt die Abstandsregel „direkte Nachbarn (gemeinsame Kante)“.");
    }

    // ---------------------------------------------------------------- Doppelte Kanten

    [Fact]
    public void Existing_edges_with_the_same_rule_are_skipped_in_both_directions()
    {
        var puzzle = Patch(GridType.Square, 3, 1);
        puzzle.Edges.Add(new Edge(1, 2, 1, NotEqualRule.Name));

        var result = Generate(puzzle, DistanceRule.Orthogonal);

        Pairs(result.Edges).Should().BeEquivalentTo(new[] { (2, 3) });
        result.SkippedExisting.Should().Be(1);
        result.MatchingPairs.Should().Be(2);
        result.Message.Should().Be("1 Kante „Ungleich“ erzeugt (direkte Nachbarn (gemeinsame Kante)). 1 Knotenpaar hatte bereits eine solche Kante.");
    }

    [Fact]
    public void Existing_edges_with_other_rules_do_not_block()
    {
        var puzzle = Patch(GridType.Square, 3, 1);
        puzzle.Edges.Add(new Edge(1, 1, 2, DifferenceRule.Name, TestPuzzles.Params(("k", 1))));

        var result = Generate(puzzle, DistanceRule.Orthogonal);

        result.Edges.Should().HaveCount(2);
        result.SkippedExisting.Should().Be(0);
    }

    [Fact]
    public void Same_rule_with_other_parameters_counts_as_existing()
    {
        var puzzle = Patch(GridType.Square, 3, 1);
        puzzle.Edges.Add(new Edge(1, 1, 2, DifferenceRule.Name, TestPuzzles.Params(("k", 3))));

        var result = Generate(puzzle, DistanceRule.Orthogonal, DifferenceRule.Name);

        Pairs(result.Edges).Should().BeEquivalentTo(new[] { (2, 3) });
    }

    [Fact]
    public void Applying_twice_adds_nothing_the_second_time()
    {
        var puzzle = ClassicSudoku.Create();
        var first = Generate(puzzle, DistanceRule.WithDiagonals);
        first.ApplyTo(puzzle);

        var second = Generate(puzzle, DistanceRule.WithDiagonals);

        first.Edges.Should().HaveCount((2 * 9 * 8) + (2 * 8 * 8));
        second.IsSuccess.Should().BeTrue();
        second.Edges.Should().BeEmpty();
        second.SkippedExisting.Should().Be(first.Edges.Count);
        second.Message.Should().Be($"Keine Kanten erzeugt: Alle {first.Edges.Count} passenden Knotenpaare haben bereits eine Kante „Ungleich“.");
    }

    [Fact]
    public void Overlapping_runs_only_add_the_missing_pairs()
    {
        var puzzle = ClassicSudoku.Create();
        Generate(puzzle, DistanceRule.Orthogonal).ApplyTo(puzzle);

        var result = Generate(puzzle, DistanceRule.WithDiagonals);

        result.Edges.Should().HaveCount(2 * 8 * 8, "nur die Diagonalen fehlen noch");
        result.SkippedExisting.Should().Be(2 * 9 * 8);
        result.Message.Should().Be("128 Kanten „Ungleich“ erzeugt (Nachbarn inkl. diagonal (gemeinsame Ecke)). 144 Knotenpaare hatten bereits eine solche Kante.");
    }

    // ---------------------------------------------------------------- Regel, Parameter, Ids, Richtung

    [Fact]
    public void New_edges_get_sequential_ids_after_the_largest_existing_id()
    {
        var puzzle = Patch(GridType.Square, 3, 1);
        puzzle.Edges.Add(new Edge(10, 1, 3, RatioRule.Name));

        var result = Generate(puzzle, DistanceRule.Orthogonal);

        result.Edges.Select(e => e.Id).Should().Equal(11, 12);
    }

    [Fact]
    public void Edges_are_ordered_in_reading_order_and_point_forward()
    {
        var puzzle = Patch(GridType.Square, 2, 2);

        var result = Generate(puzzle, DistanceRule.Orthogonal, LessThanRule.Name);

        result.Edges.Select(e => (e.From, e.To)).Should().Equal((1, 2), (1, 3), (2, 4), (3, 4));
        result.Edges.Should().OnlyContain(e => e.Rule == LessThanRule.Name);
    }

    [Fact]
    public void Reverse_reading_order_flips_less_than_edges()
    {
        var puzzle = Patch(GridType.Square, 2, 2);

        var result = Generate(puzzle, DistanceRule.Orthogonal, LessThanRule.Name, direction: EdgeDirectionPolicy.ReverseReadingOrder);

        result.Edges.Select(e => (e.From, e.To)).Should().Equal((2, 1), (3, 1), (4, 2), (4, 3));
    }

    [Fact]
    public void Reading_order_does_not_depend_on_node_ids()
    {
        var puzzle = new Puzzle("T", GridType.Square, new ValueDomain(1, 9));
        puzzle.Nodes.Add(new Node(1, 1, 0));
        puzzle.Nodes.Add(new Node(2, 0, 0));
        puzzle.Nodes.Add(new Node(3, 0, -1));

        var result = Generate(puzzle, DistanceRule.WithDiagonals, LessThanRule.Name);

        result.Edges.Select(e => (e.From, e.To)).Should().Equal((3, 2), (3, 1), (2, 1));
    }

    [Fact]
    public void Reading_order_on_hex_grid_uses_the_horizontal_world_position()
    {
        // Zeile 1: (−1, 1) liegt bei Welt-x −0,5 links von (0, 1) bei 0,5.
        var puzzle = new Puzzle("T", GridType.Hex, new ValueDomain(1, 9));
        puzzle.Nodes.Add(new Node(1, 0, 1));
        puzzle.Nodes.Add(new Node(2, -1, 1));

        Generate(puzzle, DistanceRule.Orthogonal, LessThanRule.Name).Edges.Single().Should().Match<Edge>(e => e.From == 2 && e.To == 1);
    }

    [Fact]
    public void Less_than_edges_in_the_opposite_direction_count_as_existing()
    {
        var puzzle = Patch(GridType.Square, 2, 1);
        puzzle.Edges.Add(new Edge(1, 2, 1, LessThanRule.Name));

        var result = Generate(puzzle, DistanceRule.Orthogonal, LessThanRule.Name);

        result.Edges.Should().BeEmpty("eine Gegenkante wäre ein Widerspruch");
        result.SkippedExisting.Should().Be(1);
        result.Message.Should().Be("Keine Kanten erzeugt: Das passende Knotenpaar hat bereits eine Kante „Kleiner als“.");
    }

    [Fact]
    public void Default_parameters_of_the_rule_are_used()
    {
        var result = Generate(Patch(GridType.Square, 2, 1), DistanceRule.Orthogonal, DifferenceRule.Name);

        result.Edges.Single().Params.GetInt("k", -1).Should().Be(1);
    }

    [Fact]
    public void Given_parameters_are_copied_into_every_edge()
    {
        var parameters = TestPuzzles.Params(("factor", 3));

        var result = Generate(Patch(GridType.Square, 3, 1), DistanceRule.Orthogonal, RatioRule.Name, parameters: parameters);

        result.Edges.Should().HaveCount(2).And.OnlyContain(e => e.Params.GetInt("factor", 0) == 3);
        result.Edges[0].Params.Should().NotBeSameAs(result.Edges[1].Params).And.NotBeSameAs(parameters);
    }

    [Theory]
    [InlineData("k", -1, "darf nicht negativ sein")]
    [InlineData("foo", 1, "Unbekannter Parameter „foo“")]
    public void Invalid_parameters_are_rejected_by_the_rule(string name, int value, string message)
    {
        var result = Generate(Patch(GridType.Square, 2, 1), DistanceRule.Orthogonal, DifferenceRule.Name, parameters: TestPuzzles.Params(("k", 1), (name, value)));

        result.IsSuccess.Should().BeFalse();
        result.Edges.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Should().StartWith("Ungültige Parameter für „Differenz“:").And.Contain(message);
    }

    [Fact]
    public void Missing_required_parameter_is_rejected()
    {
        var result = Generate(Patch(GridType.Square, 2, 1), DistanceRule.Orthogonal, DifferenceRule.Name, parameters: new RuleParams());

        result.Errors.Should().ContainSingle().Which.Should().Contain("Pflichtparameter „k“ fehlt");
    }

    [Fact]
    public void Group_rules_are_rejected()
    {
        var result = Generate(Patch(GridType.Square, 2, 1), DistanceRule.Orthogonal, SumRule.Name);

        result.Errors.Should().ContainSingle().Which.Should().Be("Die Regel „Summe“ (Sum) gilt für Gruppen, nicht für Kanten. Der Generator erzeugt nur Kanten.");
    }

    [Fact]
    public void Unknown_rules_are_rejected()
    {
        var result = Generate(Patch(GridType.Square, 2, 1), DistanceRule.Orthogonal, "Gibtsnicht");

        result.Errors.Should().ContainSingle().Which.Should().StartWith("Unbekannter Regeltyp „Gibtsnicht“");
    }

    [Fact]
    public void Several_errors_are_reported_together()
    {
        var puzzle = SamplePuzzle(GridType.Hex);

        var result = Generate(puzzle, DistanceRule.KnightMove, "Gibtsnicht", nodes: [999]);

        result.Errors.Should().HaveCount(3);
        result.Message.Should().StartWith("Es wurden keine Kanten erzeugt (3 Fehler):");
    }

    // ---------------------------------------------------------------- Rätsel bleibt unverändert, Anwenden

    [Theory]
    [MemberData(nameof(AllGrids))]
    public void Generate_does_not_change_the_puzzle_and_apply_gives_a_valid_puzzle(GridType grid)
    {
        var puzzle = SamplePuzzle(grid);
        puzzle.Edges.Add(new Edge(1, 1, 2, NotEqualRule.Name));
        var before = puzzle.Clone();

        var result = Generate(puzzle, DistanceRule.WithDiagonals, DifferenceRule.Name, parameters: TestPuzzles.Params(("k", 2), ("mode", "atLeast")));

        PuzzleComparer.Differences(before, puzzle).Should().BeEmpty();
        result.ApplyTo(puzzle);
        puzzle.Edges.Should().HaveCount(1 + result.Edges.Count);
        PuzzleValidator.Validate(puzzle).Should().BeEmpty();
        puzzle.Edges.Skip(1).Should().NotContain(e => result.Edges.Contains(e), "angewendet werden Kopien");
    }

    [Fact]
    public void Apply_refuses_failed_results_and_taken_ids()
    {
        var puzzle = Patch(GridType.Square, 2, 1);
        var failed = Generate(puzzle, DistanceRule.Orthogonal, "Gibtsnicht");
        var ok = Generate(puzzle, DistanceRule.Orthogonal);
        puzzle.Edges.Add(new Edge(1, 1, 2, RatioRule.Name));

        ((Action)(() => failed.ApplyTo(puzzle))).Should().Throw<InvalidOperationException>();
        ((Action)(() => ok.ApplyTo(puzzle))).Should().Throw<InvalidOperationException>().WithMessage("*bereits vergeben*");
        puzzle.Edges.Should().HaveCount(1);
    }

    [Fact]
    public void Generated_edges_on_the_big_triangle_follow_the_geometry()
    {
        var puzzle = BigTriangle(9);

        var orthogonal = Generate(puzzle, DistanceRule.Orthogonal);
        var diagonal = Generate(puzzle, DistanceRule.WithDiagonals);

        puzzle.Nodes.Should().HaveCount(81);
        orthogonal.Edges.Should().HaveCount(3 * 9 * 8 / 2);
        Pairs(diagonal.Edges).Should().BeEquivalentTo(ReferencePairs(puzzle, (a, b) => SharedVertices(GridType.Triangle, a, b) >= 1));
        Pairs(orthogonal.Edges).Should().BeSubsetOf(Pairs(diagonal.Edges));
    }

    private static GridPoint Point(Puzzle puzzle, int id)
    {
        var node = puzzle.FindNode(id)!;
        return new GridPoint(node.X, node.Y);
    }
}
