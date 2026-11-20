using GraphDoku.App.Editing;
using GraphDoku.App.Explaining;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests;

/// <summary>
/// Gruppenvergleiche im Editor: Erstellen aus dem Gruppen-Panel, Operator ändern, Löschen samt Kaskade beim Löschen von
/// Gruppen und Knoten (Undo/Redo exakt), Auswahl, Liste, Treffertest, Status- und Erklärtexte.
/// </summary>
public class GroupComparisonEditorTests
{
    /// <summary>
    /// Knoten 1–4 in einer Zeile auf x = 0, 1, 3, 4 (Wertebereich 1–4, Knoten 1 nur 1–3, Givens 4 und 3);
    /// Gruppe 1 „Links“ = [1, 2], Gruppe 2 „Rechts“ = [3, 4], Gruppe 3 = [1, 3]; ohne Vergleich.
    /// </summary>
    internal static Puzzle Row()
    {
        var puzzle = new Puzzle("Vergleich", GridType.Square, new ValueDomain(1, 4));
        puzzle.Nodes.Add(new Node(1, 0, 0) { Domain = new ValueDomain(1, 3) });
        puzzle.Nodes.Add(new Node(2, 1, 0));
        puzzle.Nodes.Add(new Node(3, 3, 0, given: 4));
        puzzle.Nodes.Add(new Node(4, 4, 0, given: 3));
        puzzle.Groups.Add(new Group(1, [1, 2], "AllDifferent", label: "Links"));
        puzzle.Groups.Add(new Group(2, [3, 4], "AllDifferent", label: "Rechts"));
        puzzle.Groups.Add(new Group(3, [1, 3], "AllDifferent"));
        return puzzle;
    }

    /// <summary><see cref="Row"/> mit Gruppenvergleich 1: Links ≥ Rechts (eindeutige Lösung 3, 4, 4, 3).</summary>
    internal static Puzzle RowWithComparison()
    {
        var puzzle = Row();
        puzzle.GroupComparisons.Add(new GroupComparison(1, 1, 2, ComparisonOperator.GreaterOrEqual));
        return puzzle;
    }

    private static void AssertUndoRedo(EditorViewModel editor, Action edit, Action? afterEdit = null)
    {
        var before = editor.Snapshot();
        var undoCount = editor.History.UndoCount;
        edit();
        afterEdit?.Invoke();
        var after = editor.Snapshot();
        PuzzleComparer.AreEqual(before, after).Should().BeFalse("die Bearbeitung muss etwas ändern");
        editor.History.UndoCount.Should().Be(undoCount + 1, "genau ein Undo-Schritt");
        editor.UndoCommand.Execute(null);
        editor.Puzzle.ShouldEqual(before);
        editor.RedoCommand.Execute(null);
        editor.Puzzle.ShouldEqual(after);
    }

    [Fact]
    public void Group_panel_offers_the_other_groups_and_creates_a_comparison_as_one_undo_step()
    {
        var editor = EditorDriver.CreateEditor(Row());
        editor.SelectGroup(1);
        var panel = editor.Properties.Should().BeOfType<GroupPropertiesViewModel>().Subject;
        panel.HasComparisonTargets.Should().BeTrue();
        panel.ComparisonTargets.Should().Equal(new GroupOption(2, "Rechts (2)"), new GroupOption(3, "Gruppe 3"));
        panel.NewComparisonTarget.Should().Be(new GroupOption(2, "Rechts (2)"), "die erste andere Gruppe ist vorgewählt");
        panel.NewComparisonOperator!.Operator.Should().Be(ComparisonOperator.Equal);
        panel.OperatorOptions.Select(o => o.Text).Should().Equal("=  gleich", "≠  ungleich", "<  kleiner als", "≤  kleiner oder gleich", ">  größer als", "≥  größer oder gleich");
        panel.ComparisonUsage.Should().BeNull();

        panel.NewComparisonTarget = panel.ComparisonTargets[1];
        panel.NewComparisonOperator = OperatorOption.Of(ComparisonOperator.LessOrEqual);
        editor.History.UndoCount.Should().Be(0, "die Wahl im Panel ist noch keine Bearbeitung");

        AssertUndoRedo(editor, () => panel.CreateComparisonCommand.Execute(null), afterEdit: () =>
        {
            editor.History.UndoDescription.Should().Be("Gruppenvergleich erstellen");
            editor.SelectedGroupComparisonId.Should().Be(1, "der neue Vergleich ist ausgewählt");
            editor.SelectedGroupId.Should().BeNull();
            var comparisonPanel = editor.Properties.Should().BeOfType<GroupComparisonPropertiesViewModel>().Subject;
            comparisonPanel.Header.Should().Be("Gruppenvergleich 1");
            comparisonPanel.Meaning.Should().Be("Summe von Gruppe 1 „Links“ ≤ Summe von Gruppe 3");
            comparisonPanel.LeftText.Should().Be("Gruppe 1 „Links“ – Alle verschieden, 2 Knoten");
            comparisonPanel.SharedNodesText.Should().Be("Gemeinsame Knoten: 1. Sie zählen auf beiden Seiten gleich und heben sich auf.");
        });
        var comparison = editor.Puzzle.GroupComparisons.Should().ContainSingle().Subject;
        comparison.Should().BeEquivalentTo(new { Id = 1, Left = 1, Right = 3, Operator = ComparisonOperator.LessOrEqual });

        // Die nächste Id folgt der größten vorhandenen.
        editor.SelectGroup(2);
        ((GroupPropertiesViewModel)editor.Properties!).CreateComparisonCommand.Execute(null);
        editor.Puzzle.GroupComparisons.Select(c => (c.Id, c.Left, c.Right, c.Operator))
            .Should().Equal((1, 1, 3, ComparisonOperator.LessOrEqual), (2, 2, 1, ComparisonOperator.Equal));
        editor.StatusMessage.Should().Be("Gruppenvergleich 2 erstellt: Summe von Gruppe 2 „Rechts“ = Summe von Gruppe 1 „Links“.");

        editor.SelectGroup(1);
        ((GroupPropertiesViewModel)editor.Properties!).ComparisonUsage.Should().Be("Diese Gruppe kommt in 2 Gruppenvergleichen vor; „Gruppe löschen“ löscht sie mit.");
        editor.SelectGroup(3);
        ((GroupPropertiesViewModel)editor.Properties!).ComparisonUsage.Should().Be("Diese Gruppe kommt in 1 Gruppenvergleich vor; „Gruppe löschen“ löscht ihn mit.");
    }

    [Fact]
    public void Creating_is_rejected_for_the_same_group_missing_groups_and_without_a_second_group()
    {
        var editor = EditorDriver.CreateEditor(Row());
        editor.CreateGroupComparison(1, 1, ComparisonOperator.Less).Should().BeNull();
        editor.StatusMessage.Should().Be("Eine Gruppe lässt sich nicht mit sich selbst vergleichen.");
        editor.CreateGroupComparison(1, 9, ComparisonOperator.Less).Should().BeNull();
        editor.StatusMessage.Should().Be("Gruppe 9 gibt es nicht.");
        editor.CreateGroupComparison(1, 2, (ComparisonOperator)42).Should().BeNull();
        editor.History.UndoCount.Should().Be(0);

        var single = new Puzzle("Eine Gruppe", GridType.Square, new ValueDomain(1, 4));
        single.Nodes.Add(new Node(1, 0, 0));
        single.Groups.Add(new Group(1, [1], "AllDifferent"));
        var lonely = EditorDriver.CreateEditor(single);
        lonely.SelectGroup(1);
        var panel = (GroupPropertiesViewModel)lonely.Properties!;
        panel.HasComparisonTargets.Should().BeFalse();
        panel.NewComparisonTarget.Should().BeNull();
        panel.CreateComparisonCommand.Execute(null);
        lonely.StatusMessage.Should().Be("Zuerst die Gruppe wählen, mit deren Summe verglichen werden soll.");
        lonely.History.UndoCount.Should().Be(0);
    }

    [Fact]
    public void Group_panel_keeps_its_target_list_and_choice_across_unrelated_edits()
    {
        var editor = EditorDriver.CreateEditor(Row());
        editor.SelectGroup(1);
        var panel = (GroupPropertiesViewModel)editor.Properties!;
        var targets = panel.ComparisonTargets;
        panel.NewComparisonTarget = targets[1];
        panel.ReverseOrderCommand.Execute(null);
        editor.Properties.Should().BeSameAs(panel);
        panel.ComparisonTargets.Should().BeSameAs(targets, "eine neue Liste setzte die Auswahl der ComboBox zurück");
        panel.NewComparisonTarget.Should().Be(targets[1]);

        // Beschriftung einer anderen Gruppe ändert die Liste, die Wahl bleibt erhalten.
        editor.Puzzle.FindGroup(3)!.Label = "Mitte";
        editor.SelectGroup(3);
        editor.SelectGroup(1);
        ((GroupPropertiesViewModel)editor.Properties!).ComparisonTargets.Should().Contain(new GroupOption(3, "Mitte (3)"));
    }

    [Fact]
    public void Changing_the_operator_is_one_undo_step_per_choice_and_undo_refreshes_the_panel()
    {
        var editor = EditorDriver.CreateEditor(RowWithComparison());
        editor.SelectGroupComparison(1);
        var panel = editor.Properties.Should().BeOfType<GroupComparisonPropertiesViewModel>().Subject;
        panel.SelectedOperator.Should().Be(OperatorOption.Of(ComparisonOperator.GreaterOrEqual));
        panel.OperatorOptions.Should().BeSameAs(OperatorOption.Shared, "alle Panels teilen eine Liste (Wiederverwendung der Vorlage)");

        AssertUndoRedo(editor, () => panel.SelectedOperator = OperatorOption.Of(ComparisonOperator.Less));
        editor.History.UndoDescription.Should().Be("Vergleich ändern");
        panel.SelectedOperator = OperatorOption.Of(ComparisonOperator.NotEqual);
        editor.History.UndoCount.Should().Be(2, "jede Wahl ist ein eigener Schritt");
        panel.SelectedOperator = OperatorOption.Of(ComparisonOperator.NotEqual);
        editor.History.UndoCount.Should().Be(2, "dieselbe Wahl ändert nichts");
        editor.Puzzle.GroupComparisons[0].Operator.Should().Be(ComparisonOperator.NotEqual);

        editor.UndoCommand.Execute(null);
        editor.Properties.Should().BeSameAs(panel, "Undo lädt das Panel neu, statt es zu ersetzen");
        panel.SelectedOperator!.Operator.Should().Be(ComparisonOperator.Less);
        panel.Meaning.Should().Be("Summe von Gruppe 1 „Links“ < Summe von Gruppe 2 „Rechts“");
        editor.History.UndoCount.Should().Be(1, "das Neuladen ist kein Schritt");
        editor.GroupComparisons[0].Title.Should().Be("Links (1) < Rechts (2)");
    }

    [Fact]
    public void Deleting_a_comparison_and_undo_restore_the_same_instances_in_order()
    {
        var puzzle = Row();
        puzzle.GroupComparisons.Add(new GroupComparison(1, 1, 2, ComparisonOperator.Less));
        puzzle.GroupComparisons.Add(new GroupComparison(5, 2, 3, ComparisonOperator.Equal));
        puzzle.GroupComparisons.Add(new GroupComparison(3, 3, 1, ComparisonOperator.Greater));
        var editor = EditorDriver.CreateEditor(puzzle);
        var instances = editor.Puzzle.GroupComparisons.ToList();

        editor.SelectGroupComparison(5);
        var panel = (GroupComparisonPropertiesViewModel)editor.Properties!;
        AssertUndoRedo(editor, () => panel.DeleteCommand.Execute(null));
        editor.History.UndoDescription.Should().Be("Gruppenvergleich löschen");
        editor.Puzzle.GroupComparisons.Select(c => c.Id).Should().Equal(1, 3);
        editor.SelectedGroupComparisonId.Should().BeNull();
        editor.Properties.Should().BeOfType<PuzzlePropertiesViewModel>();

        editor.UndoCommand.Execute(null);
        editor.Puzzle.GroupComparisons.Should().Equal(instances, ReferenceEquals);
        editor.GroupComparisons.Select(c => c.Id).Should().Equal(1, 5, 3);

        editor.SelectGroupComparison(3);
        AssertUndoRedo(editor, editor.DeleteSelection);
        editor.Puzzle.GroupComparisons.Select(c => c.Id).Should().Equal(1, 5);
        editor.Puzzle.Groups.Should().HaveCount(3, "einen Vergleich zu löschen lässt die Gruppen stehen");
    }

    [Fact]
    public void Deleting_a_group_deletes_its_comparisons_in_the_same_step()
    {
        var puzzle = Row();
        puzzle.GroupComparisons.Add(new GroupComparison(1, 1, 2, ComparisonOperator.Less));
        puzzle.GroupComparisons.Add(new GroupComparison(2, 2, 3, ComparisonOperator.Equal));
        puzzle.GroupComparisons.Add(new GroupComparison(3, 3, 1, ComparisonOperator.Greater));
        var editor = EditorDriver.CreateEditor(puzzle);
        var instances = editor.Puzzle.GroupComparisons.ToList();
        var groups = editor.Puzzle.Groups.ToList();

        editor.SelectGroup(3);
        AssertUndoRedo(editor, editor.DeleteSelection);
        editor.History.UndoDescription.Should().Be("Gruppe löschen");
        editor.Puzzle.GroupComparisons.Select(c => c.Id).Should().Equal(1);
        editor.GroupComparisons.Select(c => c.Id).Should().Equal(1);
        editor.HasValidationProblems.Should().BeFalse("kein Vergleich zeigt ins Leere");

        editor.UndoCommand.Execute(null);
        editor.Puzzle.GroupComparisons.Should().Equal(instances, ReferenceEquals);
        editor.Puzzle.Groups.Should().Equal(groups, ReferenceEquals);
        editor.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void Deleting_nodes_that_empty_a_group_deletes_its_comparisons()
    {
        var editor = EditorDriver.CreateEditor(RowWithComparison());
        editor.Puzzle.GroupComparisons.Add(new GroupComparison(2, 3, 1, ComparisonOperator.NotEqual));
        editor.LoadPuzzle(editor.Puzzle.Clone());

        // Knoten 1 gehört zu Gruppe 1 und 3: Beide bleiben bestehen, die Vergleiche auch.
        editor.SelectNodes([1]);
        AssertUndoRedo(editor, editor.DeleteSelection);
        editor.Puzzle.GroupComparisons.Should().HaveCount(2);
        editor.UndoCommand.Execute(null);

        // Knoten 3 und 4 leeren Gruppe 2: Vergleich 1 (mit Gruppe 2) fällt weg, Vergleich 2 bleibt.
        var before = editor.Puzzle.GroupComparisons.ToList();
        editor.SelectNodes([3, 4]);
        AssertUndoRedo(editor, editor.DeleteSelection);
        editor.Puzzle.FindGroup(2).Should().BeNull();
        editor.Puzzle.GroupComparisons.Select(c => c.Id).Should().Equal(2);
        editor.UndoCommand.Execute(null);
        editor.Puzzle.GroupComparisons.Should().Equal(before, ReferenceEquals);
    }

    [Fact]
    public void Dangling_and_duplicate_references_from_unchecked_files_are_not_touched_by_unrelated_deletions()
    {
        var puzzle = Row();
        puzzle.Groups.Add(new Group(2, [1], "AllDifferent")); // zweite Gruppe mit Id 2
        puzzle.GroupComparisons.Add(new GroupComparison(1, 1, 9, ComparisonOperator.Less)); // Gruppe 9 fehlt
        puzzle.GroupComparisons.Add(new GroupComparison(2, 2, 3, ComparisonOperator.Less));
        var editor = EditorDriver.CreateEditor(puzzle);
        editor.GroupComparisons[0].Title.Should().Be("Links (1) < Gruppe 9 (fehlt)");
        editor.GroupComparisonConnector(editor.Puzzle.GroupComparisons[0]).Should().BeNull();

        // Knoten 3 und 4 leeren die erste Gruppe 2, es gibt aber noch eine Gruppe mit Id 2.
        editor.SelectNodes([3, 4]);
        editor.DeleteSelection();
        editor.Puzzle.GroupComparisons.Select(c => c.Id).Should().Equal(1, 2);
    }

    [Fact]
    public void Selection_of_a_comparison_excludes_nodes_edges_and_groups()
    {
        var puzzle = RowWithComparison();
        puzzle.Edges.Add(new Edge(1, 1, 2, "NotEqual"));
        var editor = EditorDriver.CreateEditor(puzzle);

        editor.SelectNodes([1, 2]);
        editor.SelectGroupComparison(1);
        editor.SelectedNodeIds.Should().BeEmpty();
        editor.SelectedGroupComparisonId.Should().Be(1);
        editor.SelectedGroupComparisonItem!.Id.Should().Be(1);

        editor.SelectEdge(1);
        editor.SelectedGroupComparisonId.Should().BeNull();
        editor.SelectedGroupComparisonItem.Should().BeNull();

        editor.SelectGroupComparison(1);
        editor.SelectGroup(2);
        editor.SelectedGroupComparisonId.Should().BeNull();
        editor.SelectedGroupItem!.Id.Should().Be(2);

        editor.SelectGroupComparison(1);
        editor.SelectedGroupId.Should().BeNull();
        editor.SelectedGroupItem.Should().BeNull();
        editor.AddToSelection([3]);
        editor.SelectedGroupComparisonId.Should().BeNull();
        editor.SelectedNodeIds.Should().Equal(3);

        editor.SelectGroupComparison(1);
        editor.ClearSelection();
        editor.SelectedGroupComparisonId.Should().BeNull();
        editor.Properties.Should().BeOfType<PuzzlePropertiesViewModel>();
    }

    [Fact]
    public void Panel_buttons_select_the_groups_and_undo_of_the_creation_prunes_the_selection()
    {
        var editor = EditorDriver.CreateEditor(Row());
        editor.CreateGroupComparison(2, 3, ComparisonOperator.Greater);
        var panel = (GroupComparisonPropertiesViewModel)editor.Properties!;
        panel.SelectRightGroupCommand.Execute(null);
        editor.SelectedGroupId.Should().Be(3);
        editor.SelectGroupComparison(1);
        ((GroupComparisonPropertiesViewModel)editor.Properties!).SelectLeftGroupCommand.Execute(null);
        editor.SelectedGroupId.Should().Be(2);
        editor.History.UndoCount.Should().Be(1, "Auswahl ist kein Undo-Schritt");

        editor.SelectGroupComparison(1);
        editor.UndoCommand.Execute(null);
        editor.Puzzle.GroupComparisons.Should().BeEmpty();
        editor.SelectedGroupComparisonId.Should().BeNull();
        editor.Properties.Should().BeOfType<PuzzlePropertiesViewModel>();
        editor.HasGroupComparisons.Should().BeFalse();
    }

    [Fact]
    public void Comparison_list_is_synchronized_in_place_and_selects_comparisons()
    {
        var editor = EditorDriver.CreateEditor(Row());
        editor.HasGroupComparisons.Should().BeFalse();
        editor.GroupComparisons.Should().BeEmpty();

        editor.CreateGroupComparison(1, 2, ComparisonOperator.LessOrEqual);
        editor.CreateGroupComparison(3, 1, ComparisonOperator.NotEqual);
        editor.HasGroupComparisons.Should().BeTrue();
        editor.GroupComparisons.Select(c => c.Title).Should().Equal("Links (1) ≤ Rechts (2)", "Gruppe 3 ≠ Links (1)");
        editor.GroupComparisons[1].Details.Should().Be("Gruppenvergleich 2 · ungleich");
        editor.GroupComparisons[1].LeftColorHex.Should().Be(GroupPalette.EffectiveColor(null, 3));
        var first = editor.GroupComparisons[0];

        // Beschriftung einer Gruppe ändern: Einträge bleiben, Titel folgen.
        editor.SelectGroup(1);
        ((GroupPropertiesViewModel)editor.Properties!).Label = "West";
        editor.GroupComparisons[0].Should().BeSameAs(first);
        first.Title.Should().Be("West (1) ≤ Rechts (2)");

        editor.SelectedGroupComparisonItem = editor.GroupComparisons[1];
        editor.SelectedGroupComparisonId.Should().Be(2);
        editor.Properties.Should().BeOfType<GroupComparisonPropertiesViewModel>().Which.ComparisonId.Should().Be(2);
        editor.SelectedGroupItem.Should().BeNull();
        editor.SelectedGroupComparisonItem = null;
        editor.SelectedGroupComparisonId.Should().BeNull();

        editor.LoadPuzzle(Row());
        editor.GroupComparisons.Should().BeEmpty();
        editor.HasGroupComparisons.Should().BeFalse();
    }

    [Fact]
    public void Status_and_statistics_mention_comparisons_only_when_there_are_some()
    {
        EditorViewModel.DescribeDocument(Row()).Should().Be("Rätsel „Vergleich“: 4 Knoten, 0 Kanten, 3 Gruppen.");
        EditorViewModel.DescribeDocument(RowWithComparison()).Should().Be("Rätsel „Vergleich“: 4 Knoten, 0 Kanten, 3 Gruppen, 1 Gruppenvergleich.");
        var two = RowWithComparison();
        two.GroupComparisons.Add(new GroupComparison(2, 2, 3));
        GroupComparisonTexts.PuzzleCounts(two).Should().Be("4 Knoten, 0 Kanten, 3 Gruppen, 2 Gruppenvergleiche");

        var editor = EditorDriver.CreateEditor(Row());
        ((PuzzlePropertiesViewModel)editor.Properties!).Statistics.Should().Be("4 Knoten, 0 Kanten, 3 Gruppen");
        editor.LoadPuzzle(two);
        ((PuzzlePropertiesViewModel)editor.Properties!).Statistics.Should().Be("4 Knoten, 0 Kanten, 3 Gruppen, 2 Gruppenvergleiche");
    }

    [Fact]
    public void Clicking_the_badge_or_the_line_selects_the_comparison_but_nodes_win_over_the_line()
    {
        var editor = EditorDriver.CreateEditor(RowWithComparison());
        var (from, to) = editor.GroupComparisonConnector(editor.Puzzle.GroupComparisons[0])!.Value;
        from.Should().Be(new WorldPoint(0.5, 0));
        to.Should().Be(new WorldPoint(3.5, 0));

        editor.Click(editor.ScreenOf(2, 0));
        editor.SelectedGroupComparisonId.Should().Be(1, "das Etikett sitzt in der Mitte der Verbindung");
        editor.ClearSelection();

        editor.Click(editor.ScreenOf(1.6, 0.02));
        editor.SelectedGroupComparisonId.Should().Be(1, "nahe der Linie");
        editor.Click(editor.ScreenOf(1, 0));
        editor.SelectedNodeIds.Should().Equal(2);
        editor.SelectedGroupComparisonId.Should().BeNull("der Knoten liegt über der Linie");

        editor.Click(editor.ScreenOf(1.6, 0.5));
        editor.SelectedNodeIds.Should().BeEmpty("weit neben der Linie trifft nichts");
        editor.SelectedGroupComparisonId.Should().BeNull();

        // Ausgeblendete Gruppe: keine Verbindung, kein Treffer – außer der Vergleich ist gewählt.
        editor.SetGroupVisible(2, false);
        editor.IsGroupComparisonShown(editor.Puzzle.GroupComparisons[0]).Should().BeFalse();
        editor.Click(editor.ScreenOf(2, 0));
        editor.SelectedGroupComparisonId.Should().BeNull();
        editor.SelectGroupComparison(1);
        editor.HitGroupComparison(new WorldPoint(2, 0), badgeOnly: true).Should().NotBeNull();
        editor.History.UndoCount.Should().Be(0);
    }

    [Fact]
    public void Puzzles_without_comparisons_hit_nothing_new()
    {
        var editor = EditorDriver.CreateEditor(Row());
        editor.HitGroupComparison(new WorldPoint(2, 0)).Should().BeNull();
        editor.Click(editor.ScreenOf(2, 0));
        editor.SelectedGroupComparisonId.Should().BeNull();
        editor.SelectedNodeIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Explanation_names_the_comparison_as_rule_and_highlights_both_groups()
    {
        var window = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        window.Editor.SetViewSize(1000, 800);
        window.Editor.LoadPuzzle(RowWithComparison());
        var explain = window.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.Outcome.Should().Be(ExplainOutcome.Solved, explain.StatusMessage);

        var step = explain.Result!.Steps.First(s => s.Constraint is { Kind: ConstraintKind.GroupComparison });
        explain.Position = step.Index + 1;
        explain.RuleText.Should().Be("Gruppenvergleich 1: Summe von Gruppe 1 „Links“ ≥ Summe von Gruppe 2 „Rechts“");
        window.Editor.IsExplainedGroupComparison(1).Should().BeTrue();
        window.Editor.IsGroupComparisonShown(window.Editor.Puzzle.GroupComparisons[0]).Should().BeTrue();
        var view = window.Editor.Explanation!;
        foreach (var nodeId in new[] { 1, 2, 3, 4 })
        {
            view.GetHighlight(nodeId).Should().NotBe(ExplanationHighlight.None, $"Knoten {nodeId} gehört zu einer der beiden Gruppen");
        }
    }
}
