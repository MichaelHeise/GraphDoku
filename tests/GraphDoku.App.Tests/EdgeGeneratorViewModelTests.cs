using GraphDoku.App.Editing.Commands;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Generation;
using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests;

/// <summary>Kanten-Generator im Editor (Paket 5): Vorschau, Einfügen als ein Undo-Schritt, Knotenmenge, Richtung, Fehler.</summary>
public class EdgeGeneratorViewModelTests
{
    private static Puzzle Grid(int width, int height, GridType grid = GridType.Square)
    {
        var puzzle = new Puzzle($"{width}x{height}", grid, new ValueDomain(1, 9));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                puzzle.Nodes.Add(new Node((y * width) + x + 1, x, y));
            }
        }

        return puzzle;
    }

    private static EdgeGeneratorViewModel Generator(EditorViewModel editor, string rule = "NotEqual")
    {
        var generator = new EdgeGeneratorViewModel(editor);
        generator.SelectedRule = editor.EdgeRules.Single(r => r.TypeName == rule);
        return generator;
    }

    private static void SelectDistance(EdgeGeneratorViewModel generator, DistanceKind kind) =>
        generator.SelectedDistance = generator.DistanceOptions.Single(o => o.Kind == kind);

    [Fact]
    public void Preview_counts_edges_without_changing_the_puzzle()
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());
        var before = editor.Snapshot();

        var generator = Generator(editor);

        generator.PreviewEdgeCount.Should().Be(144, "9 Zeilen × 8 + 9 Spalten × 8 Nachbarpaare");
        generator.CanApply.Should().BeTrue();
        generator.PreviewSeverity.Should().Be(ResultSeverity.Success);
        generator.PreviewMessage.Should().Be("144 neue Kanten „Ungleich“ (direkte Nachbarn (gemeinsame Kante)).");
        generator.HasPreviewErrors.Should().BeFalse();
        editor.Puzzle.ShouldEqual(before);
        editor.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Apply_inserts_all_edges_as_one_undo_step_and_undo_removes_exactly_them()
    {
        var puzzle = Grid(3, 3);
        puzzle.Edges.Add(new Edge(7, 1, 9, "Ratio", new RuleParams().Set("factor", 2)));
        var editor = EditorDriver.CreateEditor(puzzle);
        var before = editor.Snapshot();
        var generator = Generator(editor, "Difference");
        SelectDistance(generator, DistanceKind.WithDiagonals);

        generator.Apply().Should().BeTrue();

        editor.History.UndoCount.Should().Be(1);
        editor.History.LastCommand.Should().BeOfType<AddGeneratedEdgesCommand>();
        editor.UndoText.Should().Be("Rückgängig: 20 Kanten erzeugen");
        var generated = editor.Puzzle.Edges.Where(e => e.Rule == "Difference").ToList();
        generated.Should().HaveCount(20, "3×3: 6 waagerechte, 6 senkrechte und 8 diagonale Paare");
        generated.Select(e => e.Id).Should().BeEquivalentTo(Enumerable.Range(8, 20), "fortlaufend ab der größten Id + 1");
        generated.Should().OnlyContain(e => e.Params.GetIntOrNull("k") == 1);
        editor.StatusMessage.Should().Be("20 Kanten „Differenz“ erzeugt (Nachbarn inkl. diagonal (gemeinsame Ecke)).");
        var after = editor.Snapshot();

        editor.UndoCommand.Execute(null);
        editor.Puzzle.ShouldEqual(before);
        editor.Puzzle.Edges.Should().ContainSingle(e => e.Id == 7, "die vorhandene Kante bleibt");

        editor.RedoCommand.Execute(null);
        editor.Puzzle.ShouldEqual(after);
        editor.UndoCommand.Execute(null);
        editor.Puzzle.ShouldEqual(before);
        editor.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Existing_edges_are_skipped_and_a_second_run_creates_nothing()
    {
        var puzzle = Grid(3, 1);
        puzzle.Edges.Add(new Edge(1, 2, 1, "NotEqual"));
        var editor = EditorDriver.CreateEditor(puzzle);

        var first = Generator(editor);
        first.PreviewMessage.Should().Be("1 neue Kante „Ungleich“ (direkte Nachbarn (gemeinsame Kante)). 1 Knotenpaar wird übersprungen (Kante bereits vorhanden).");
        first.Apply().Should().BeTrue();
        editor.StatusMessage.Should().Be("1 Kante „Ungleich“ erzeugt (direkte Nachbarn (gemeinsame Kante)). 1 Knotenpaar hatte bereits eine solche Kante.");

        var second = Generator(editor);
        second.CanApply.Should().BeFalse();
        second.PreviewSeverity.Should().Be(ResultSeverity.Warning);
        second.PreviewMessage.Should().Be("Alle 2 passenden Knotenpaare haben bereits eine Kante „Ungleich“ – es entstehen keine Kanten.");
        var steps = editor.History.UndoCount;
        second.Apply().Should().BeFalse();
        editor.History.UndoCount.Should().Be(steps, "ohne neue Kanten entsteht kein Undo-Schritt");
        editor.StatusMessage.Should().StartWith("Keine Kanten erzeugt.");
    }

    [Fact]
    public void Scope_defaults_to_selection_and_can_switch_to_all_nodes()
    {
        var editor = EditorDriver.CreateEditor(Grid(4, 4));
        editor.SelectNodes([1, 2, 3, 6]);

        var generator = Generator(editor);
        generator.HasSelection.Should().BeTrue();
        generator.UseSelection.Should().BeTrue();
        generator.SelectionScopeText.Should().Be("Ausgewählte Knoten (4)");
        generator.AllNodesScopeText.Should().Be("Alle Knoten (16)");
        generator.PreviewEdgeCount.Should().Be(3, "1–2, 2–3 und 2–6; 3–4 und 6–7 liegen nur halb in der Auswahl");
        generator.PreviewMessage.Should().Contain("zwischen den ausgewählten Knoten");

        generator.UseAllNodes = true;
        generator.UseSelection.Should().BeFalse();
        generator.PreviewEdgeCount.Should().Be(24);

        generator.UseSelection = true;
        generator.Apply().Should().BeTrue();
        editor.Puzzle.Edges.Select(e => (e.From, e.To)).Should().BeEquivalentTo(new[] { (1, 2), (2, 3), (2, 6) });
    }

    [Fact]
    public void Without_a_selection_of_two_nodes_the_scope_is_all_nodes()
    {
        var editor = EditorDriver.CreateEditor(Grid(3, 3));
        editor.SelectNodes([5]);

        var generator = Generator(editor);
        generator.HasSelection.Should().BeFalse();
        generator.UseSelection.Should().BeFalse();
        generator.UseSelection = true;
        generator.UseSelection.Should().BeFalse("eine Auswahl unter zwei Knoten ergibt keine Kante");
        generator.PreviewEdgeCount.Should().Be(12);

        // Ohne einen einzigen passenden Partner: Warnung, nichts zu tun.
        var single = EditorDriver.CreateEditor(Grid(1, 1));
        var empty = Generator(single);
        empty.CanApply.Should().BeFalse();
        empty.PreviewMessage.Should().Be("Kein Knotenpaar erfüllt die Abstandsregel „direkte Nachbarn (gemeinsame Kante)“ – es entstehen keine Kanten.");
    }

    [Fact]
    public void Less_than_offers_a_direction_and_edges_follow_it()
    {
        var editor = EditorDriver.CreateEditor(Grid(2, 2));
        var generator = Generator(editor);
        generator.IsDirected.Should().BeFalse();

        generator.SelectedRule = editor.EdgeRules.Single(r => r.TypeName == "LessThan");
        generator.IsDirected.Should().BeTrue();
        generator.SelectedDirection.Policy.Should().Be(EdgeDirectionPolicy.ReadingOrder);
        generator.Preview!.Edges.Select(e => (e.From, e.To)).Should().Equal((1, 2), (1, 3), (2, 4), (3, 4));

        generator.SelectedDirection = generator.DirectionOptions.Single(o => o.Policy == EdgeDirectionPolicy.ReverseReadingOrder);
        generator.Preview!.Edges.Select(e => (e.From, e.To)).Should().Equal((2, 1), (3, 1), (4, 2), (4, 3));
        generator.Apply().Should().BeTrue();
        editor.Puzzle.Edges.Should().OnlyContain(e => e.Rule == "LessThan" && e.From > e.To);

        // Einzelne Kanten dreht danach „Richtung umkehren“ im Panel.
        editor.SelectEdge(editor.Puzzle.Edges[0].Id);
        var panel = editor.Properties.Should().BeOfType<EdgePropertiesViewModel>().Subject;
        panel.IsDirected.Should().BeTrue();
        panel.ReverseCommand.Execute(null);
        editor.Puzzle.Edges[0].Should().Match<Edge>(e => e.From == 1 && e.To == 2);
    }

    [Fact]
    public void Parameters_are_edited_with_the_generic_parameter_fields()
    {
        var editor = EditorDriver.CreateEditor(Grid(3, 1));
        var generator = Generator(editor, "Difference");
        generator.HasParameters.Should().BeTrue();
        generator.Parameters.Select(p => p.Name).Should().Equal("k", "mode");

        generator.Parameters.Single(p => p.Name == "k").Text = "3";
        generator.Parameters.Single(p => p.Name == "mode").ChoiceValue = "atLeast";
        generator.Apply().Should().BeTrue();

        editor.Puzzle.Edges.Should().HaveCount(2).And.OnlyContain(e => e.Params.GetIntOrNull("k") == 3 && e.Params.GetString("mode", null) == "atLeast");
    }

    [Fact]
    public void Edge_id_int_max_value_is_reported_instead_of_crashing_on_apply()
    {
        // Review-Befund: Die Vorschau meldete Erfolg (Ids 1, 2), Apply warf dann InvalidOperationException aus ApplyTo.
        var puzzle = Grid(3, 1);
        puzzle.Edges.Add(new Edge(1, 1, 2, "Ratio", new RuleParams().Set("factor", 2)));
        puzzle.Edges.Add(new Edge(int.MaxValue, 2, 3, "LessThan"));
        var editor = EditorDriver.CreateEditor(puzzle);
        var before = editor.Snapshot();
        var generator = Generator(editor);

        generator.CanApply.Should().BeFalse();
        generator.PreviewErrors.Should().ContainSingle().Which.Should().Contain("keine freien Kanten-Ids");
        generator.Apply().Should().BeFalse();
        editor.StatusMessage.Should().StartWith("Keine Kanten erzeugt: ").And.Contain("keine freien Kanten-Ids");
        editor.Puzzle.ShouldEqual(before);
        editor.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Invalid_parameters_and_unreadable_numbers_show_errors_and_block_apply()
    {
        var editor = EditorDriver.CreateEditor(Grid(3, 3));
        var generator = Generator(editor, "Difference");

        var k = generator.Parameters.Single(p => p.Name == "k");
        k.Text = "x";
        generator.CanApply.Should().BeFalse();
        generator.PreviewSeverity.Should().Be(ResultSeverity.Error);
        generator.PreviewErrors.Should().ContainSingle().Which.Should().Contain("Abstand k");

        k.Text = string.Empty;
        generator.CanApply.Should().BeFalse();
        generator.PreviewErrors.Should().ContainSingle().Which.Should().StartWith("Ungültige Parameter für „Differenz“");

        k.Text = "2";
        generator.CanApply.Should().BeTrue();

        SelectDistance(generator, DistanceKind.Free);
        generator.IsFreeDistance.Should().BeTrue();
        generator.MinDistanceText = "abc";
        generator.CanApply.Should().BeFalse();
        generator.PreviewErrors.Should().ContainSingle().Which.Should().Be("Bitte für „kleinster Abstand“ eine Zahl eingeben (z. B. 1,5), nicht „abc“.");
        generator.PreviewMessage.Should().Be("Mit dieser Einstellung können keine Kanten erzeugt werden:");

        generator.MinDistanceText = "0";
        generator.PreviewErrors.Should().Contain(e => e.Contains("größer als 0", StringComparison.Ordinal));
        var steps = editor.History.UndoCount;
        generator.Apply().Should().BeFalse();
        editor.History.UndoCount.Should().Be(steps);
        editor.StatusMessage.Should().StartWith("Keine Kanten erzeugt: ");
    }

    [Fact]
    public void Free_distance_accepts_decimal_comma_range_and_tolerance()
    {
        var editor = EditorDriver.CreateEditor(Grid(3, 3));
        var generator = Generator(editor);
        SelectDistance(generator, DistanceKind.Free);

        generator.MinDistanceText = "1,414";
        generator.ToleranceText = "0,01";
        generator.PreviewEdgeCount.Should().Be(8, "nur die Diagonalen (Abstand √2)");
        generator.PreviewMessage.Should().Contain("Abstand 1,414");

        generator.MinDistanceText = "1";
        generator.MaxDistanceText = "1.5";
        generator.PreviewEdgeCount.Should().Be(20, "Nachbarn und Diagonalen");
    }

    [Fact]
    public void Knight_move_is_only_offered_on_the_square_grid()
    {
        var square = new EdgeGeneratorViewModel(EditorDriver.CreateEditor(Grid(3, 3)));
        square.DistanceOptions.Select(o => o.Kind).Should().Contain(DistanceKind.KnightMove);
        square.KnightMoveHint.Should().BeNull();
        SelectDistance(square, DistanceKind.KnightMove);
        square.PreviewEdgeCount.Should().Be(8);

        foreach (var grid in new[] { GridType.Triangle, GridType.Hex })
        {
            var other = new EdgeGeneratorViewModel(EditorDriver.CreateEditor(Grid(3, 3, grid)));
            other.DistanceOptions.Select(o => o.Kind).Should().NotContain(DistanceKind.KnightMove);
            other.KnightMoveHint.Should().Contain("nur auf dem Quadratgitter");
            other.FreeDistanceHint.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void Generator_uses_the_toolbar_edge_rule_by_default()
    {
        var editor = EditorDriver.CreateEditor(Grid(2, 1));
        editor.NewEdgeRule = editor.EdgeRules.Single(r => r.TypeName == "Ratio");

        new EdgeGeneratorViewModel(editor).SelectedRule!.TypeName.Should().Be("Ratio");
    }

    [Fact]
    public async Task Main_window_command_applies_only_when_the_dialog_is_confirmed()
    {
        var dialogs = new FakeDialogService();
        var window = new MainWindowViewModel(new FakeFileDialogService(), dialogs, new OrToolsSolver());
        window.Editor.LoadPuzzle(Grid(3, 3));

        await window.GenerateEdgesCommand.ExecuteAsync(null);
        dialogs.LastEdgeGenerator.Should().NotBeNull();
        window.Editor.Puzzle.Edges.Should().BeEmpty("abgebrochen");
        window.Editor.CanUndo.Should().BeFalse();

        dialogs.EdgeGeneratorAnswer = generator =>
        {
            generator.SelectedRule = window.Editor.EdgeRules.Single(r => r.TypeName == "LessThan");
            return true;
        };
        await window.GenerateEdgesCommand.ExecuteAsync(null);
        window.Editor.Puzzle.Edges.Should().HaveCount(12).And.OnlyContain(e => e.Rule == "LessThan");
        window.Editor.History.UndoCount.Should().Be(1);
        window.Title.Should().EndWith("* – GraphDoku");
    }
}
