using GraphDoku.App.Editing;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;
using GraphDoku.Core.Templates;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests;

/// <summary>
/// Paket 5, Editor: alle Katalogregeln anlegbar (Thermometer-Reihenfolge, Richtung von „Kleiner als“), eigener
/// Wertebereich pro Knoten, kontextpassende Statuszeile.
/// </summary>
public class EditorRuleEditingTests
{
    private static Puzzle Grid(int width, int height)
    {
        var puzzle = new Puzzle($"{width}x{height}", GridType.Square, new ValueDomain(1, 9));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                puzzle.Nodes.Add(new Node((y * width) + x + 1, x, y));
            }
        }

        return puzzle;
    }

    [Fact]
    public void Every_catalog_rule_can_be_created_in_the_editor()
    {
        var editor = EditorDriver.CreateEditor(Grid(3, 3));
        editor.EdgeRules.Select(r => r.TypeName).Should().BeEquivalentTo("NotEqual", "Difference", "Ratio", "LessThan");
        editor.GroupRules.Select(r => r.TypeName).Should().BeEquivalentTo("AllDifferent", "CoverRange", "Sum", "Thermometer");

        // Kanten: gezogen mit der Regel der Werkzeugleiste.
        editor.Tool = EditorTool.DrawEdge;
        var from = 1;
        foreach (var rule in editor.EdgeRules)
        {
            editor.NewEdgeRule = rule;
            editor.DragGrid(new GridPoint((from - 1) % 3, (from - 1) / 3), new GridPoint(from % 3, (from - 1) / 3));
            from += from % 3 == 2 ? 2 : 1;
        }

        editor.Puzzle.Edges.Select(e => e.Rule).Should().Equal("NotEqual", "Difference", "Ratio", "LessThan");

        // Gruppen: aus der Auswahl mit der Regel des Panels.
        foreach (var rule in editor.GroupRules)
        {
            editor.NewGroupRule = rule;
            editor.SelectNodes(rule.TypeName == "CoverRange" ? Enumerable.Range(1, 9) : [7, 8, 9]);
            editor.CreateGroupFromSelection();
            if (rule.TypeName == "Sum")
            {
                var panel = editor.Properties.Should().BeOfType<GroupPropertiesViewModel>().Subject;
                panel.Parameters.Single(p => p.Name == "target").Text = "12";
            }
        }

        editor.Puzzle.Groups.Select(g => g.Rule).Should().Equal("AllDifferent", "CoverRange", "Sum", "Thermometer");
        editor.HasValidationProblems.Should().BeFalse(string.Join("; ", editor.ValidationProblems));
        new OrToolsSolver().Solve(editor.Puzzle).Status.Should().Be(GraphDoku.Solver.SolveStatus.Solved);
    }

    [Fact]
    public void Thermometer_order_follows_the_shift_click_order_and_can_be_reversed()
    {
        var editor = EditorDriver.CreateEditor(Grid(3, 3));
        editor.Tool = EditorTool.Select;

        // Bulb unten rechts, dann nach oben und nach links (Klickreihenfolge, nicht Leserichtung).
        editor.Click(new GridPoint(2, 2));
        editor.Click(new GridPoint(2, 1), EditorModifiers.Shift);
        editor.Click(new GridPoint(1, 1), EditorModifiers.Shift);
        editor.Click(new GridPoint(0, 1), EditorModifiers.Shift);
        editor.SelectedNodeIds.Should().Equal(9, 6, 5, 4);
        editor.ShowSelectionOrder.Should().BeTrue("die Auswahlreihenfolge ist als Nummern sichtbar");

        editor.NewGroupRule = editor.GroupRules.Single(r => r.TypeName == "Thermometer");
        editor.CreateGroupFromSelection();
        var group = editor.Puzzle.Groups.Single();
        group.Nodes.Should().Equal(9, 6, 5, 4);

        var panel = editor.Properties.Should().BeOfType<GroupPropertiesViewModel>().Subject;
        panel.IsOrdered.Should().BeTrue();
        panel.NodeOrder.Should().Be("9 → 6 → 5 → 4");

        panel.ReverseOrderCommand.Execute(null);
        editor.Puzzle.Groups.Single().Nodes.Should().Equal(4, 5, 6, 9);
        ((GroupPropertiesViewModel)editor.Properties!).NodeOrder.Should().Be("4 → 5 → 6 → 9");

        editor.UndoCommand.Execute(null);
        editor.Puzzle.Groups.Single().Nodes.Should().Equal(9, 6, 5, 4);

        // Shift-Klick auf einen gewählten Knoten nimmt ihn heraus; ein neuer Shift-Klick hängt ihn hinten an.
        editor.SelectNodes([1, 2, 3]);
        editor.Click(new GridPoint(1, 0), EditorModifiers.Shift);
        editor.Click(new GridPoint(1, 0), EditorModifiers.Shift);
        editor.SelectedNodeIds.Should().Equal(1, 3, 2);
    }

    [Fact]
    public void Less_than_edges_point_from_drag_start_to_drag_end_and_can_be_reversed()
    {
        var editor = EditorDriver.CreateEditor(Grid(2, 1));
        editor.Tool = EditorTool.DrawEdge;
        editor.NewEdgeRule = editor.EdgeRules.Single(r => r.TypeName == "LessThan");

        editor.DragGrid(new GridPoint(1, 0), new GridPoint(0, 0));
        var edge = editor.Puzzle.Edges.Single();
        (edge.From, edge.To).Should().Be((2, 1));

        var panel = editor.Properties.Should().BeOfType<EdgePropertiesViewModel>().Subject;
        panel.Endpoints.Should().Be("Von Knoten 2 nach Knoten 1 (Wert bei 2 < Wert bei 1)");
        panel.ReverseCommand.Execute(null);
        (editor.Puzzle.Edges.Single().From, editor.Puzzle.Edges.Single().To).Should().Be((1, 2));
    }

    [Fact]
    public void Own_value_domain_of_selected_nodes_is_set_and_removed_with_undo()
    {
        var editor = EditorDriver.CreateEditor(Grid(3, 1));
        var before = editor.Snapshot();
        editor.SelectNodes([1, 2]);
        var panel = editor.Properties.Should().BeOfType<NodeSelectionPropertiesViewModel>().Subject;
        panel.DomainInfo.Should().Be("Es gilt der globale Bereich 1..9.");
        panel.HasDomainOverride.Should().BeFalse();
        (panel.DomainMin, panel.DomainMax).Should().Be(("1", "9"));

        panel.DomainMin = "2";
        panel.DomainMax = "4";
        editor.CanUndo.Should().BeFalse("Tippen allein ändert nichts");
        panel.ApplyDomainCommand.Execute(null);

        editor.Puzzle.FindNode(1)!.Domain.Should().Be(new ValueDomain(2, 4));
        editor.Puzzle.FindNode(2)!.Domain.Should().Be(new ValueDomain(2, 4));
        editor.Puzzle.FindNode(3)!.Domain.Should().BeNull();
        editor.History.UndoCount.Should().Be(1);
        editor.StatusMessage.Should().Be("Eigener Wertebereich 2..4 für 2 Knoten gesetzt.");
        var current = (NodeSelectionPropertiesViewModel)editor.Properties!;
        current.DomainInfo.Should().Be("Alle haben den eigenen Bereich 2..4.");
        current.HasDomainOverride.Should().BeTrue();

        // Given-Eingabe beachtet den eigenen Bereich (5 liegt außerhalb).
        editor.EnterValueCharacter('5');
        editor.Puzzle.FindNode(1)!.Given.Should().BeNull();
        editor.StatusMessage.Should().Contain("außerhalb des Wertebereichs 2..4");
        editor.EnterValueCharacter('3');
        editor.Puzzle.FindNode(1)!.Given.Should().Be(3);

        editor.SelectNodes([2, 3]);
        var mixed = (NodeSelectionPropertiesViewModel)editor.Properties!;
        mixed.DomainInfo.Should().Be("Gemischt: 1 von 2 Knoten mit eigenem Bereich.");
        mixed.ClearDomainCommand.Execute(null);
        editor.Puzzle.FindNode(2)!.Domain.Should().BeNull();
        editor.StatusMessage.Should().Be("Eigener Wertebereich für 1 Knoten entfernt; es gilt der globale Bereich 1..9.");

        editor.UndoCommand.Execute(null);
        editor.Puzzle.FindNode(2)!.Domain.Should().Be(new ValueDomain(2, 4));
        editor.UndoCommand.Execute(null); // Given
        editor.UndoCommand.Execute(null); // Bereich
        editor.Puzzle.ShouldEqual(before);
    }

    [Fact]
    public void Invalid_own_domain_is_rejected_and_givens_outside_are_reported()
    {
        var puzzle = Grid(2, 1);
        puzzle.Nodes[0].Given = 9;
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.SelectNodes([1, 2]);
        var panel = (NodeSelectionPropertiesViewModel)editor.Properties!;

        panel.DomainMin = "x";
        panel.ApplyDomainCommand.Execute(null);
        panel.DomainError.Should().Be("Bitte ganze Zahlen eingeben.");
        panel.DomainMin = "5";
        panel.DomainMax = "1";
        panel.ApplyDomainCommand.Execute(null);
        panel.DomainError.Should().Contain("nicht größer");
        editor.CanUndo.Should().BeFalse();

        panel.DomainMin = "1";
        panel.DomainMax = "4";
        panel.ApplyDomainCommand.Execute(null);
        editor.StatusMessage.Should().Contain("1 Given(s) liegen außerhalb");
        editor.HasValidationProblems.Should().BeTrue();
    }

    [Fact]
    public void Typing_in_the_domain_fields_survives_other_edits()
    {
        var editor = EditorDriver.CreateEditor(Grid(2, 1));
        editor.SelectNodes([1]);
        var panel = (NodeSelectionPropertiesViewModel)editor.Properties!;
        panel.DomainMax = "4";

        editor.EnterValueCharacter('2');
        editor.Properties.Should().BeSameAs(panel);
        panel.DomainMax.Should().Be("4", "eine andere Bearbeitung überschreibt die angefangene Eingabe nicht");
    }

    [Fact]
    public void Status_bar_describes_the_current_document_instead_of_a_stale_new_puzzle_hint()
    {
        var window = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        window.Editor.StatusMessage.Should().StartWith("Leeres Rätsel „Unbenannt“ (Quadrat, Werte 1..9). Mit „Knoten setzen“");

        window.Editor.LoadPuzzle(ClassicSudoku.Create());
        window.Editor.StatusMessage.Should().Be("Rätsel „Klassisch 9x9“: 81 Knoten, 0 Kanten, 27 Gruppen.");

        window.Editor.LoadPuzzle(new Puzzle("Leer", GridType.Hex, new ValueDomain(1, 7)));
        window.Editor.StatusMessage.Should().StartWith("Leeres Rätsel „Leer“ (Sechseck, Werte 1..7).");
    }

    [Fact]
    public async Task Status_bar_after_opening_a_file_names_the_file()
    {
        var window = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        var path = Path.Combine(MainWindowViewModelTests.FindRepositoryRoot(), "puzzles", "killer-sudoku.json");

        (await window.OpenFileAsync(path)).Should().BeTrue();

        window.Editor.StatusMessage.Should().Be("„killer-sudoku.json“ geöffnet: 81 Knoten, 0 Kanten, 55 Gruppen.");
    }

    [Fact]
    public void Rule_registry_contains_exactly_the_eight_catalog_rules()
    {
        RuleRegistry.Default.Rules.Should().HaveCount(8);
    }
}
