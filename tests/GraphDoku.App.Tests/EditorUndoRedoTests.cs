using GraphDoku.App.Editing;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;
using GraphDoku.Core.Templates;

namespace GraphDoku.App.Tests;

/// <summary>
/// Undo/Redo für jede Art von Bearbeitung, jeweils über die Befehle des Editors ausgelöst. Geprüft wird stets:
/// Undo stellt den Ausgangszustand exakt wieder her (Strukturvergleich), Redo den bearbeiteten Zustand.
/// </summary>
public class EditorUndoRedoTests
{
    private static Puzzle Grid3x3(ValueDomain? domain = null)
    {
        var puzzle = new Puzzle("3x3", GridType.Square, domain ?? new ValueDomain(1, 9));
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                puzzle.Nodes.Add(new Node((y * 3) + x + 1, x, y));
            }
        }

        return puzzle;
    }

    /// <summary>Führt die Bearbeitung aus und prüft Undo und Redo gegen die Zustände davor und danach.</summary>
    private static void AssertUndoRedo(EditorViewModel editor, Action edit, int expectedSteps = 1, Action? afterEdit = null)
    {
        var before = editor.Snapshot();
        var undoCount = editor.History.UndoCount;

        edit();

        var after = editor.Snapshot();
        PuzzleComparer.AreEqual(before, after).Should().BeFalse("die Bearbeitung muss etwas ändern");
        editor.History.UndoCount.Should().Be(undoCount + expectedSteps);
        editor.IsDirty.Should().BeTrue();
        afterEdit?.Invoke();

        for (var i = 0; i < expectedSteps; i++)
        {
            editor.UndoCommand.Execute(null);
        }

        editor.Puzzle.ShouldEqual(before);
        for (var i = 0; i < expectedSteps; i++)
        {
            editor.RedoCommand.Execute(null);
        }

        editor.Puzzle.ShouldEqual(after);
        editor.CanRedo.Should().BeFalse();
    }

    private static IRule Rule(string typeName) => RuleRegistry.Default.Get(typeName);

    [Fact]
    public void Place_node()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3());
        editor.Tool = EditorTool.PlaceNode;
        AssertUndoRedo(editor, () => editor.Click(new GridPoint(5, 5)));
        editor.Puzzle.FindNode(10).Should().BeEquivalentTo(new { X = 5, Y = 5 });
    }

    [Fact]
    public void Paint_nodes()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3());
        editor.Tool = EditorTool.PlaceNode;
        AssertUndoRedo(editor, () => editor.DragGrid(new GridPoint(0, 4), new GridPoint(3, 4), new GridPoint(3, 6)));
        editor.Puzzle.Nodes.Should().HaveCount(9 + 6);
    }

    [Fact]
    public void Move_nodes()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3());
        editor.SelectNodes([1, 2, 3]);
        AssertUndoRedo(editor, () => editor.Drag([editor.ScreenOf(new GridPoint(1, 0)), editor.ScreenOf(1.3, -1.2), editor.ScreenOf(new GridPoint(4, -2))]));
        editor.Puzzle.Nodes.Take(3).Select(n => (n.X, n.Y)).Should().Equal((3, -2), (4, -2), (5, -2));
    }

    [Fact]
    public void Delete_nodes_with_their_edges_and_group_memberships()
    {
        var puzzle = Grid3x3();
        puzzle.Edges.Add(new Edge(1, 1, 2, "NotEqual"));
        puzzle.Edges.Add(new Edge(2, 2, 3, "LessThan"));
        puzzle.Edges.Add(new Edge(3, 4, 5, "Difference", new RuleParams().Set("k", 1)));
        puzzle.Groups.Add(new Group(1, [1, 2, 3], "AllDifferent", label: "oben"));
        puzzle.Groups.Add(new Group(2, [2], "Sum", new RuleParams().Set("target", 5)));
        puzzle.Groups.Add(new Group(3, [3, 2, 1, 4], "Thermometer"));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.SelectNodes([2]);

        AssertUndoRedo(editor, editor.DeleteSelection);

        editor.Puzzle.FindNode(2).Should().BeNull();
        editor.Puzzle.Edges.Select(e => e.Id).Should().Equal(3);
        editor.Puzzle.Groups.Select(g => g.Id).Should().Equal([1, 3], "Gruppe 2 wurde leer und wird mitgelöscht");
        editor.Puzzle.FindGroup(3)!.Nodes.Should().Equal(3, 1, 4);
        editor.SelectedNodeIds.Should().BeEmpty();
    }

    [Fact]
    public void Delete_key_semantics_delete_nodes_backspace_clears_givens()
    {
        var puzzle = Grid3x3();
        puzzle.Nodes[0].Given = 4;
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.SelectNodes([1]);

        editor.ClearGivens();
        editor.Puzzle.Nodes.Should().HaveCount(9);
        editor.Puzzle.FindNode(1)!.Given.Should().BeNull();
        editor.UndoText.Should().Be("Rückgängig: Givens löschen");

        editor.DeleteSelection();
        editor.Puzzle.FindNode(1).Should().BeNull();
    }

    [Fact]
    public void Draw_edge()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3());
        editor.Tool = EditorTool.DrawEdge;
        editor.NewEdgeRule = Rule("Difference");

        AssertUndoRedo(editor, () => editor.DragGrid(new GridPoint(0, 0), new GridPoint(1, 1)), afterEdit: () =>
        {
            editor.SelectedEdgeId.Should().Be(1, "die neue Kante wird gewählt");
            editor.Properties.Should().BeOfType<EdgePropertiesViewModel>();
        });

        var edge = editor.Puzzle.Edges.Should().ContainSingle().Subject;
        edge.Should().BeEquivalentTo(new { Id = 1, From = 1, To = 5, Rule = "Difference" });
        edge.Params.GetInt("k", -1).Should().Be(1, "Vorschlagswert für neue Kanten");
    }

    [Fact]
    public void Edge_drag_to_empty_space_or_same_node_creates_nothing_and_duplicates_are_rejected()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3());
        editor.Tool = EditorTool.DrawEdge;
        editor.Drag([editor.ScreenOf(new GridPoint(0, 0)), editor.ScreenOf(5, 5)]);
        editor.Drag([editor.ScreenOf(new GridPoint(0, 0)), editor.ScreenOf(0.1, 0.1)]);
        editor.Puzzle.Edges.Should().BeEmpty();
        editor.EdgePreview.Should().BeNull();

        editor.DragGrid(new GridPoint(0, 0), new GridPoint(1, 0));
        editor.DragGrid(new GridPoint(1, 0), new GridPoint(0, 0));
        editor.Puzzle.Edges.Should().ContainSingle("ungerichtete Kante gleicher Regel gibt es schon");
        editor.StatusMessage.Should().Contain("bereits eine Kante");

        editor.NewEdgeRule = Rule("LessThan");
        editor.DragGrid(new GridPoint(0, 0), new GridPoint(1, 0));
        editor.DragGrid(new GridPoint(1, 0), new GridPoint(0, 0));
        editor.Puzzle.Edges.Where(e => e.Rule == "LessThan").Select(e => (e.From, e.To)).Should().Equal((1, 2), (2, 1));
    }

    [Fact]
    public void Edge_rule_change_resets_parameters()
    {
        var puzzle = Grid3x3();
        puzzle.Edges.Add(new Edge(1, 1, 2, "NotEqual"));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.SelectEdge(1);
        var panel = (EdgePropertiesViewModel)editor.Properties!;
        panel.Parameters.Should().BeEmpty();

        AssertUndoRedo(editor, () => panel.SelectedRule = Rule("Ratio"));

        editor.Puzzle.FindEdge(1)!.Rule.Should().Be("Ratio");
        editor.Puzzle.FindEdge(1)!.Params.GetInt("factor", 0).Should().Be(2);
        panel.Parameters.Should().ContainSingle().Which.Name.Should().Be("factor");
        panel.SelectedRule!.TypeName.Should().Be("Ratio", "das Panel folgt dem Redo");
        editor.UndoText.Should().Be("Rückgängig: Regel „Verhältnis“ wählen");
    }

    [Fact]
    public void Edge_parameters_typed_continuously_are_one_undo_step()
    {
        var puzzle = Grid3x3();
        puzzle.Edges.Add(new Edge(1, 1, 2, "Difference", new RuleParams().Set("k", 1)));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.SelectEdge(1);
        var panel = (EdgePropertiesViewModel)editor.Properties!;
        var k = panel.Parameters.Single(p => p.Name == "k");
        k.Label.Should().Be("Abstand k (Pflicht)");
        k.Text.Should().Be("1");

        AssertUndoRedo(editor, () =>
        {
            k.Text = "";
            k.Text = "1";
            k.Text = "12";
        });
        editor.Puzzle.FindEdge(1)!.Params.GetInt("k", 0).Should().Be(12);
        k.Text.Should().Be("12");

        k.Text = "abc";
        k.Error.Should().Be("Bitte eine ganze Zahl eingeben.");
        editor.Puzzle.FindEdge(1)!.Params.GetInt("k", 0).Should().Be(12);

        var mode = panel.Parameters.Single(p => p.Name == "mode");
        mode.IsChoice.Should().BeTrue();
        mode.ChoiceValue.Should().Be("exact");
        mode.Label.Should().Be("Modus");
        editor.EdgeRules.Single(r => r.TypeName == "Difference").Parameters.Single(p => p.Name == "mode").AllowedValues
            .Should().BeSameAs(mode.Choices, "die Auswahlliste ist für alle Panels dieselbe Instanz");
        AssertUndoRedo(editor, () => mode.ChoiceValue = "atLeast");
        editor.Puzzle.FindEdge(1)!.Params.GetString("mode", null).Should().Be("atLeast");
    }

    [Fact]
    public void Emptying_an_integer_parameter_removes_it_and_shows_the_validation_problem()
    {
        var puzzle = Grid3x3();
        puzzle.Edges.Add(new Edge(1, 1, 2, "Difference", new RuleParams().Set("k", 1)));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.SelectEdge(1);
        var k = ((EdgePropertiesViewModel)editor.Properties!).Parameters.Single(p => p.Name == "k");

        AssertUndoRedo(editor, () => k.Text = " ");

        editor.Puzzle.FindEdge(1)!.Params.Contains("k").Should().BeFalse();
        editor.ValidationProblems.Should().ContainSingle(p => p.Contains("Pflichtparameter „k“"));
    }

    [Fact]
    public void Reverse_and_delete_edge()
    {
        var puzzle = Grid3x3();
        puzzle.Edges.Add(new Edge(1, 1, 2, "LessThan"));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.SelectEdge(1);
        var panel = (EdgePropertiesViewModel)editor.Properties!;
        panel.IsDirected.Should().BeTrue();
        panel.Endpoints.Should().Contain("Von Knoten 1 nach Knoten 2");

        AssertUndoRedo(editor, () => panel.ReverseCommand.Execute(null));
        editor.Puzzle.FindEdge(1).Should().BeEquivalentTo(new { From = 2, To = 1 });
        panel.Endpoints.Should().Contain("Von Knoten 2 nach Knoten 1");

        AssertUndoRedo(editor, () => panel.DeleteCommand.Execute(null));
        editor.Puzzle.Edges.Should().BeEmpty();
        editor.SelectedEdgeId.Should().BeNull();

        editor.UndoCommand.Execute(null);
        editor.SelectEdge(1);
        AssertUndoRedo(editor, editor.DeleteSelection);
    }

    [Fact]
    public void Create_group_from_selection_keeps_the_selection_order()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3());
        editor.NewGroupRule = Rule("Thermometer");
        editor.Click(new GridPoint(2, 2));
        editor.Click(new GridPoint(1, 1), EditorModifiers.Shift);
        editor.Click(new GridPoint(0, 0), EditorModifiers.Shift);

        AssertUndoRedo(editor, editor.CreateGroupFromSelection, afterEdit: () =>
        {
            var panel = editor.Properties.Should().BeOfType<GroupPropertiesViewModel>().Subject;
            panel.NodeOrder.Should().Be("9 → 5 → 1");
            panel.IsOrdered.Should().BeTrue();
        });

        var group = editor.Puzzle.Groups.Should().ContainSingle().Subject;
        group.Should().BeEquivalentTo(new { Id = 1, Rule = "Thermometer", Color = (string?)null, Label = (string?)null });
        group.Nodes.Should().Equal(9, 5, 1);
        editor.Groups.Should().ContainSingle().Which.Details.Should().Be("Thermometer, 3 Knoten");
    }

    [Fact]
    public void Group_needs_a_selection()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3());
        editor.CreateGroupFromSelection();
        editor.Puzzle.Groups.Should().BeEmpty();
        editor.StatusMessage.Should().Be("Für eine Gruppe zuerst Knoten auswählen.");
    }

    [Fact]
    public void Group_label_color_rule_parameters_and_order()
    {
        var puzzle = Grid3x3();
        puzzle.Groups.Add(new Group(1, [1, 2, 3], "AllDifferent"));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.SelectGroup(1);
        var panel = (GroupPropertiesViewModel)editor.Properties!;

        AssertUndoRedo(editor, () =>
        {
            panel.Label = "K";
            panel.Label = "Kä";
            panel.Label = "Käfig";
        });
        editor.Puzzle.FindGroup(1)!.Label.Should().Be("Käfig");
        editor.Groups[0].Title.Should().Be("Käfig (1)");

        AssertUndoRedo(editor, () => panel.SelectedColor = panel.ColorOptions.Single(c => c.Name == "Grün"));
        editor.Puzzle.FindGroup(1)!.Color.Should().Be("#16a34a");
        editor.Groups[0].ColorHex.Should().Be("#16a34a");

        AssertUndoRedo(editor, () => panel.SelectedRule = Rule("Sum"));
        editor.Puzzle.FindGroup(1)!.Rule.Should().Be("Sum");
        var target = panel.Parameters.Single(p => p.Name == "target");
        target.Text.Should().Be("10");
        var repeats = panel.Parameters.Single(p => p.Name == "allowRepeats");
        repeats.IsBoolean.Should().BeTrue();

        AssertUndoRedo(editor, () => target.Text = "15");
        AssertUndoRedo(editor, () => repeats.BoolValue = true);
        editor.Puzzle.FindGroup(1)!.Params.GetInt("target", 0).Should().Be(15);
        editor.Puzzle.FindGroup(1)!.Params.GetBool("allowRepeats", false).Should().BeTrue();

        AssertUndoRedo(editor, () => panel.ReverseOrderCommand.Execute(null));
        editor.Puzzle.FindGroup(1)!.Nodes.Should().Equal(3, 2, 1);

        AssertUndoRedo(editor, () => panel.SelectedColor = panel.ColorOptions[0]);
        editor.Puzzle.FindGroup(1)!.Color.Should().BeNull("„Automatisch“ entfernt die eigene Farbe");

        AssertUndoRedo(editor, () => panel.Label = "");
        editor.Puzzle.FindGroup(1)!.Label.Should().BeNull();
    }

    [Fact]
    public void Group_panels_share_one_color_list_and_show_custom_colors_separately()
    {
        var puzzle = Grid3x3();
        puzzle.Groups.Add(new Group(1, [1, 2], "AllDifferent", color: "#dc2626"));
        puzzle.Groups.Add(new Group(2, [3, 4], "AllDifferent", color: "#123456"));
        puzzle.Groups.Add(new Group(3, [5, 6], "AllDifferent"));
        var editor = EditorDriver.CreateEditor(puzzle);

        editor.SelectGroup(1);
        var first = (GroupPropertiesViewModel)editor.Properties!;
        first.SelectedColor!.Hex.Should().Be("#dc2626");
        first.CustomColor.Should().BeNull();
        first.Parameters.Should().BeEmpty();

        editor.SelectGroup(2);
        var second = (GroupPropertiesViewModel)editor.Properties!;
        second.ColorOptions.Should().BeSameAs(first.ColorOptions, "eine Liste je Panel ließe die wiederverwendete ComboBox Werte zurückschreiben");
        second.SelectedColor.Should().BeNull();
        second.CustomColor.Should().Contain("#123456");

        editor.SelectGroup(3);
        ((GroupPropertiesViewModel)editor.Properties!).SelectedColor.Should().BeSameAs(GroupPropertiesViewModel.SharedColorOptions[0]);
        editor.History.UndoCount.Should().Be(0);

        editor.SelectGroup(2);
        var again = (GroupPropertiesViewModel)editor.Properties!;
        AssertUndoRedo(editor, () => again.SelectedColor = again.ColorOptions[2]);
        editor.Puzzle.FindGroup(2)!.Color.Should().Be(again.ColorOptions[2].Hex);
        again.CustomColor.Should().BeNull();
    }

    [Fact]
    public void Undo_refreshes_the_group_panel_without_replacing_it()
    {
        var puzzle = Grid3x3();
        puzzle.Groups.Add(new Group(1, [1, 2, 3], "AllDifferent", label: "alt"));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.SelectGroup(1);
        var panel = (GroupPropertiesViewModel)editor.Properties!;

        panel.Label = "neu";
        editor.UndoCommand.Execute(null);

        editor.Properties.Should().BeSameAs(panel);
        panel.Label.Should().Be("alt");
    }

    [Fact]
    public void Delete_group()
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());
        editor.SelectGroup(5);
        var panel = (GroupPropertiesViewModel)editor.Properties!;

        AssertUndoRedo(editor, () => panel.DeleteCommand.Execute(null));
        editor.Puzzle.Groups.Should().HaveCount(26);
        editor.Groups.Should().HaveCount(26);
        editor.Puzzle.Nodes.Should().HaveCount(81, "Gruppe löschen löscht keine Knoten");

        editor.SelectGroup(6);
        AssertUndoRedo(editor, editor.DeleteSelection);
    }

    [Fact]
    public void Group_list_order_survives_undo_of_a_deletion()
    {
        var editor = EditorDriver.CreateEditor(ClassicSudoku.Create());
        editor.SelectGroup(2);
        editor.Groups[2].IsVisible = false;
        editor.DeleteSelection();

        editor.UndoCommand.Execute(null);

        editor.Groups.Select(g => g.Id).Should().Equal(Enumerable.Range(1, 27));
        editor.Groups.Single(g => g.Id == 3).IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Single_digit_givens()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3());
        editor.SelectNodes([5]);

        AssertUndoRedo(editor, () => editor.Type("7"));
        editor.Puzzle.FindNode(5)!.Given.Should().Be(7);

        AssertUndoRedo(editor, () => editor.Type("3"), expectedSteps: 1);
        editor.Puzzle.FindNode(5)!.Given.Should().Be(3, "37 liegt nicht in 1..9, also neue Zahl");

        editor.Type("0");
        editor.Puzzle.FindNode(5)!.Given.Should().Be(3);
        editor.StatusMessage.Should().Be("Der Wert 0 liegt außerhalb des Wertebereichs 1..9.");

        AssertUndoRedo(editor, editor.ClearGivens);
        editor.Puzzle.FindNode(5)!.Given.Should().BeNull();
    }

    [Fact]
    public void Givens_in_several_selected_nodes()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3());
        editor.SelectNodes([1, 2, 3]);
        AssertUndoRedo(editor, () => editor.Type("4"));
        editor.Puzzle.Nodes.Take(3).Should().OnlyContain(n => n.Given == 4);
    }

    [Fact]
    public void Multi_digit_givens_are_one_undo_step()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3(new ValueDomain(1, 16)));
        editor.SelectNodes([1]);

        AssertUndoRedo(editor, () => editor.Type("16"));
        editor.Puzzle.FindNode(1)!.Given.Should().Be(16);

        editor.Type("2");
        editor.Puzzle.FindNode(1)!.Given.Should().Be(2, "162 ist zu groß, also beginnt eine neue Zahl");
        editor.History.UndoCount.Should().Be(2);

        editor.SelectNodes([2]);
        editor.Type("1");
        editor.SelectNodes([3]);
        editor.Type("1");
        editor.Puzzle.FindNode(3)!.Given.Should().Be(1, "neue Auswahl beginnt eine neue Eingabe");
        editor.Puzzle.FindNode(2)!.Given.Should().Be(1);
    }

    [Fact]
    public void Prefix_of_a_larger_domain_value_waits_for_more_digits()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3(new ValueDomain(10, 20)));
        editor.SelectNodes([1]);

        editor.Type("1");
        editor.Puzzle.FindNode(1)!.Given.Should().BeNull();
        editor.StatusMessage.Should().Be("Eingabe: 1 …");
        editor.Type("5");
        editor.Puzzle.FindNode(1)!.Given.Should().Be(15);
        editor.History.UndoCount.Should().Be(1);
    }

    [Fact]
    public void Negative_givens()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3(new ValueDomain(-5, 5)));
        editor.SelectNodes([1]);
        AssertUndoRedo(editor, () => editor.Type("-3"));
        editor.Puzzle.FindNode(1)!.Given.Should().Be(-3);

        var positive = EditorDriver.CreateEditor(Grid3x3());
        positive.SelectNodes([1]);
        positive.Type("-");
        positive.StatusMessage.Should().Be("Der Wertebereich enthält keine negativen Werte.");
    }

    [Fact]
    public void Hex_domain_uses_single_characters_0_to_F()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3(new ValueDomain(0, 15)));
        editor.SelectNodes([1]);

        AssertUndoRedo(editor, () => editor.Type("B"));
        editor.Puzzle.FindNode(1)!.Given.Should().Be(11);
        editor.Type("1");
        editor.Type("0");
        editor.Puzzle.FindNode(1)!.Given.Should().Be(0, "im Hex-Bereich ist jedes Zeichen ein ganzer Wert");
        editor.Type("f");
        editor.Puzzle.FindNode(1)!.Given.Should().Be(15);
        editor.EnterValueCharacter('G').Should().BeFalse();

        var decimalEditor = EditorDriver.CreateEditor(Grid3x3());
        decimalEditor.SelectNodes([1]);
        decimalEditor.EnterValueCharacter('A').Should().BeFalse("A–F nur beim Bereich 0–15");
    }

    [Fact]
    public void Value_characters_without_selection_are_not_handled()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3());
        editor.EnterValueCharacter('5').Should().BeFalse();
        editor.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Givens_respect_a_node_domain()
    {
        var puzzle = Grid3x3();
        puzzle.Nodes[0].Domain = new ValueDomain(1, 3);
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.SelectNodes([1]);
        editor.Type("5");
        editor.Puzzle.FindNode(1)!.Given.Should().BeNull();
        editor.Type("2");
        editor.Puzzle.FindNode(1)!.Given.Should().Be(2);
    }

    [Fact]
    public void Puzzle_name_and_domain()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3());
        var panel = (PuzzlePropertiesViewModel)editor.Properties!;
        panel.GridText.Should().Be("Quadrat");
        panel.Statistics.Should().Be("9 Knoten, 0 Kanten, 0 Gruppen");

        AssertUndoRedo(editor, () =>
        {
            panel.Name = "N";
            panel.Name = "Neu";
        });
        editor.Puzzle.Name.Should().Be("Neu");

        AssertUndoRedo(editor, () =>
        {
            panel.DomainMax = "1";
            panel.DomainMax = "16";
        });
        editor.Puzzle.Domain.Should().Be(new ValueDomain(1, 16));

        panel.DomainMin = "x";
        panel.DomainError.Should().Be("Bitte ganze Zahlen eingeben.");
        panel.DomainMin = "20";
        panel.DomainError.Should().Contain("nicht größer");
        editor.Puzzle.Domain.Should().Be(new ValueDomain(1, 16));
    }

    [Fact]
    public void Undo_and_redo_report_in_the_status_and_are_disabled_when_empty()
    {
        var editor = EditorDriver.CreateEditor(Grid3x3());
        editor.UndoCommand.CanExecute(null).Should().BeFalse();
        editor.RedoCommand.CanExecute(null).Should().BeFalse();

        editor.SelectNodes([1]);
        editor.DeleteSelection();
        editor.UndoCommand.CanExecute(null).Should().BeTrue();

        editor.UndoCommand.Execute(null);
        editor.StatusMessage.Should().Be("Rückgängig: Knoten löschen");
        editor.RedoCommand.CanExecute(null).Should().BeTrue();
        editor.RedoText.Should().Be("Wiederholen: Knoten löschen");
        editor.RedoCommand.Execute(null);
        editor.StatusMessage.Should().Be("Wiederholt: Knoten löschen");
    }

    [Fact]
    public void Long_mixed_session_undoes_back_to_the_start_and_redoes_to_the_end()
    {
        var editor = EditorDriver.CreateEditor(new Puzzle("leer"));
        var start = editor.Snapshot();

        editor.Tool = EditorTool.PlaceNode;
        editor.DragGrid(new GridPoint(0, 0), new GridPoint(3, 0));
        editor.Click(new GridPoint(0, 1));
        editor.Tool = EditorTool.DrawEdge;
        editor.NewEdgeRule = Rule("Ratio");
        editor.DragGrid(new GridPoint(0, 0), new GridPoint(0, 1));
        editor.Tool = EditorTool.Select;
        editor.RubberBand(-0.5, -0.5, 3.5, 0.5);
        editor.CreateGroupFromSelection();
        editor.Click(new GridPoint(2, 0));
        editor.Type("8");
        editor.Drag([editor.ScreenOf(new GridPoint(2, 0)), editor.ScreenOf(new GridPoint(2, 3))]);
        editor.SelectNodes([1]);
        editor.DeleteSelection();
        var end = editor.Snapshot();
        var steps = editor.History.UndoCount;
        steps.Should().Be(7);

        while (editor.CanUndo)
        {
            editor.UndoCommand.Execute(null);
        }

        editor.Puzzle.ShouldEqual(start);
        editor.IsDirty.Should().BeFalse();
        while (editor.CanRedo)
        {
            editor.RedoCommand.Execute(null);
        }

        editor.Puzzle.ShouldEqual(end);
    }
}
