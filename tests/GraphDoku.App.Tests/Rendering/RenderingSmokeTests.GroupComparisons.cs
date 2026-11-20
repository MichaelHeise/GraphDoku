using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GraphDoku.App.Controls;
using GraphDoku.App.Editing;
using GraphDoku.App.Explaining;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;

namespace GraphDoku.App.Tests.Rendering;

/// <summary>Gruppenvergleiche im echten Hauptfenster: Verbindung, Etikett, Auswahl, Erklär-Hervorhebung, Panelwechsel.</summary>
public sealed partial class RenderingSmokeTests
{
    [Fact]
    public Task Group_comparison_connector_renders_on_all_grid_types_and_turns_blue_when_selected() => fixture.Session.Dispatch(() =>
    {
        foreach (var grid in Enum.GetValues<GridType>())
        {
            var puzzle = new Puzzle($"Vergleich {grid}", grid, new ValueDomain(1, 9));
            var id = 1;
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 5; x++)
                {
                    puzzle.Nodes.Add(new Node(id++, x, y));
                }
            }

            puzzle.Groups.Add(new Group(1, [1, 2, 6, 7], "Sum", new RuleParams().Set("target", 12), label: "Käfig"));
            puzzle.Groups.Add(new Group(2, [14, 15, 19, 20], "AllDifferent", color: "#dc2626"));
            puzzle.GroupComparisons.Add(new GroupComparison(1, 1, 2, ComparisonOperator.LessOrEqual));

            var (window, viewModel) = OpenWindow(puzzle);
            var editor = viewModel.Editor;
            var canvas = window.FindControl<GraphCanvas>("Canvas")!;
            var name = $"vergleich-{GridNames.ToGerman(grid).ToLowerInvariant()}";
            var shown = Capture(window, $"{name}.png");
            var badge = BadgeArea(window, canvas, editor, editor.Puzzle.GroupComparisons[0]);
            var gray = ComparisonGrayPixels(shown, badge);
            gray.Should().BeGreaterThan(15, $"{grid}: Verbindung und Etikett sind grau gezeichnet");
            var blueBefore = CountPixels(shown, badge, IsAccentBlue);

            editor.SetGroupVisible(2, false);
            var hidden = Capture(window, $"{name}-ausgeblendet.png");
            ComparisonGrayPixels(hidden, badge).Should().BeLessThan(gray / 3, $"{grid}: bei ausgeblendeter Gruppe keine Verbindung");

            editor.SelectGroupComparison(1);
            editor.Properties.Should().BeOfType<GroupComparisonPropertiesViewModel>();
            var selected = Capture(window, $"{name}-gewaehlt.png");
            CountPixels(selected, badge, IsAccentBlue).Should().BeGreaterThan(blueBefore + 15, $"{grid}: gewählt blau, auch bei ausgeblendeter Gruppe");
            editor.History.UndoCount.Should().Be(0);
            window.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public Task Explanation_step_of_a_comparison_draws_the_connector_green() => fixture.Session.Dispatch(async () =>
    {
        var (window, viewModel) = OpenWindow(GroupComparisonEditorTests.RowWithComparison());
        var explain = viewModel.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.Outcome.Should().Be(ExplainOutcome.Solved, explain.StatusMessage);
        var canvas = window.FindControl<GraphCanvas>("Canvas")!;
        var editor = viewModel.Editor;
        var badge = BadgeArea(window, canvas, editor, editor.Puzzle.GroupComparisons[0]);

        var start = Capture(window, "erklaerung-vergleich-anfang.png");
        CountPixels(start, badge, IsRuleGreen).Should().Be(0, "in der Ausgangslage ist der Vergleich nicht die Regel");

        var step = explain.Result!.Steps.First(s => s.Constraint is { Kind: ConstraintKind.GroupComparison });
        explain.Position = step.Index + 1;
        var frame = Capture(window, "erklaerung-vergleich.png");
        CountPixels(frame, badge, IsRuleGreen).Should().BeGreaterThan(15, "Verbindung und Etikett des verantwortlichen Vergleichs sind grün");

        // Beide Gruppen grün umrandet: grüne Pixel auf dem Rand jeder Gruppe (links von Knoten 1, rechts von Knoten 4).
        int GreenNear(double worldX)
        {
            var screen = editor.Viewport.ToScreen(new WorldPoint(worldX, 0));
            var point = canvas.TranslatePoint(new Point(screen.X, screen.Y), window)!.Value;
            return CountPixels(frame, new PixelRect((int)point.X - 4, (int)point.Y - 10, 8, 20), IsRuleGreen);
        }

        GreenNear(-0.5).Should().BeGreaterThan(5, "linke Gruppe grün umrandet");
        GreenNear(4.5).Should().BeGreaterThan(5, "rechte Gruppe grün umrandet");
        window.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Switching_between_groups_edges_and_comparisons_creates_no_undo_steps() => fixture.Session.Dispatch(() =>
    {
        var puzzle = ClassicSudoku.Create();
        puzzle.Groups[1].Color = "#dc2626";
        puzzle.Groups[2].Rule = "Sum";
        puzzle.Groups[2].Params.Set("target", 45);
        puzzle.Edges.Add(new Edge(1, 1, 2, "Difference", new RuleParams().Set("k", 3)));
        puzzle.Edges.Add(new Edge(2, 2, 3, "LessThan"));
        puzzle.GroupComparisons.Add(new GroupComparison(1, 1, 2, ComparisonOperator.Equal));
        puzzle.GroupComparisons.Add(new GroupComparison(2, 3, 4, ComparisonOperator.Less));
        puzzle.GroupComparisons.Add(new GroupComparison(3, 5, 1, ComparisonOperator.GreaterOrEqual));
        puzzle.GroupComparisons.Add(new GroupComparison(4, 2, 6, ComparisonOperator.NotEqual));
        var reference = puzzle.Clone();

        var (window, viewModel) = OpenWindow(puzzle);
        var editor = viewModel.Editor;
        void Run(Action select)
        {
            select();
            Dispatcher.UIThread.RunJobs();
        }

        foreach (var comparisonId in new[] { 1, 2, 3, 1, 4, 2, 4, 3 })
        {
            Run(() => editor.SelectGroupComparison(comparisonId));
            var panel = editor.Properties.Should().BeOfType<GroupComparisonPropertiesViewModel>().Subject;
            panel.SelectedOperator!.Operator.Should().Be(reference.FindGroupComparison(comparisonId)!.Operator, "keine Rückschreibung des vorigen Operators");
        }

        foreach (var groupId in new[] { 1, 2, 3, 1, 5, 2, 4 })
        {
            Run(() => editor.SelectGroup(groupId));
            var panel = editor.Properties.Should().BeOfType<GroupPropertiesViewModel>().Subject;
            panel.NewComparisonOperator.Should().NotBeNull();
            panel.ComparisonTargets.Should().NotContain(t => t.Id == groupId);
            panel.NewComparisonTarget.Should().Be(panel.ComparisonTargets[0], "die Vorwahl des neuen Panels wird nicht überschrieben");
        }

        foreach (var action in new Action[]
        {
            () => editor.SelectEdge(1), () => editor.SelectGroupComparison(2), () => editor.SelectGroup(3), () => editor.SelectGroupComparison(3),
            () => editor.SelectEdge(2), () => editor.SelectGroupComparison(1), () => editor.SelectNodes([1, 2]), () => editor.SelectGroupComparison(4),
            editor.ClearSelection,
        })
        {
            Run(action);
        }

        // Auswahl über die Liste im Fenster.
        var list = window.FindControl<ListBox>("GroupComparisonList")!;
        list.IsEffectivelyVisible.Should().BeTrue();
        Run(() => list.SelectedIndex = 2);
        editor.SelectedGroupComparisonId.Should().Be(3);
        Run(() => list.SelectedIndex = 0);
        editor.SelectedGroupComparisonId.Should().Be(1);

        editor.History.UndoCount.Should().Be(0);
        editor.IsDirty.Should().BeFalse();
        viewModel.Title.Should().NotContain("*");
        PuzzleComparer.Differences(reference, editor.Puzzle).Should().BeEmpty();
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Comparison_panel_operator_choice_in_the_window_is_one_undo_step() => fixture.Session.Dispatch(() =>
    {
        var puzzle = ClassicSudoku.Create();
        puzzle.GroupComparisons.Add(new GroupComparison(1, 1, 2, ComparisonOperator.Equal));
        puzzle.GroupComparisons.Add(new GroupComparison(2, 3, 4, ComparisonOperator.Less));
        var (window, viewModel) = OpenWindow(puzzle);
        var editor = viewModel.Editor;
        editor.SelectGroupComparison(1);
        Dispatcher.UIThread.RunJobs();
        var combo = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.IsEffectivelyVisible && ReferenceEquals(c.ItemsSource, OperatorOption.Shared));
        combo.SelectedItem = OperatorOption.Of(ComparisonOperator.Greater);
        Dispatcher.UIThread.RunJobs();
        editor.SelectGroupComparison(2);
        Dispatcher.UIThread.RunJobs();
        editor.SelectGroupComparison(1);
        Dispatcher.UIThread.RunJobs();

        editor.History.UndoCount.Should().Be(1);
        editor.Puzzle.FindGroupComparison(1)!.Operator.Should().Be(ComparisonOperator.Greater);
        editor.Puzzle.FindGroupComparison(2)!.Operator.Should().Be(ComparisonOperator.Less);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Comparison_list_fits_at_minimum_window_size() => fixture.Session.Dispatch(() =>
    {
        var puzzle = ClassicSudoku.Create(Givens);
        for (var i = 1; i <= 6; i++)
        {
            puzzle.GroupComparisons.Add(new GroupComparison(i, i, i + 9, (ComparisonOperator)(i % 6)));
        }

        var (window, viewModel) = OpenWindow(puzzle);
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        viewModel.Editor.SelectGroupComparison(3);
        Dispatcher.UIThread.RunJobs();
        Capture(window, "mindestgroesse-vergleiche.png");

        var groups = window.FindControl<ListBox>("GroupList")!;
        var comparisons = window.FindControl<ListBox>("GroupComparisonList")!;
        comparisons.IsEffectivelyVisible.Should().BeTrue();
        output.WriteLine($"Gruppenliste {groups.Bounds.Height:0.#} px, Vergleichsliste {comparisons.Bounds.Height:0.#} px");
        groups.Bounds.Height.Should().BeGreaterThan(60, "die Gruppenliste zeigt mindestens einen Eintrag ganz");
        var bottom = comparisons.TranslatePoint(new Point(0, comparisons.Bounds.Height), window)!.Value;
        bottom.Y.Should().BeLessThanOrEqualTo(window.Bounds.Height, "die Vergleichsliste wird nicht abgeschnitten");
        foreach (var text in new[] { "Rückgängig", "Wiederholen", "Einpassen" })
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, text));
            var corner = button.TransformToVisual(window)!.Value.Transform(new Point(button.Bounds.Width, button.Bounds.Height));
            corner.X.Should().BeLessThanOrEqualTo(window.Bounds.Width, text);
        }

        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Puzzles_without_comparisons_hide_the_comparison_list() => fixture.Session.Dispatch(() =>
    {
        var (window, _) = OpenWindow(ClassicSudoku.Create());
        window.FindControl<DockPanel>("GroupComparisonSection")!.IsVisible.Should().BeFalse();
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Dangling_and_empty_comparison_groups_render_without_errors() => fixture.Session.Dispatch(() =>
    {
        var puzzle = GroupComparisonEditorTests.Row();
        puzzle.Groups.Add(new Group(4, [99], "AllDifferent")); // nur ein fehlender Knoten
        puzzle.GroupComparisons.Add(new GroupComparison(1, 1, 9, ComparisonOperator.Less));
        puzzle.GroupComparisons.Add(new GroupComparison(2, 4, 2, ComparisonOperator.Less));
        puzzle.GroupComparisons.Add(new GroupComparison(2, 2, 3, (ComparisonOperator)42));
        var (window, viewModel) = OpenWindow(puzzle);
        foreach (var comparison in new[] { 1, 2 })
        {
            viewModel.Editor.SelectGroupComparison(comparison);
            Capture(window, $"vergleich-haengend-{comparison}.png").PixelSize.Width.Should().BeGreaterThan(0);
            viewModel.Editor.Properties.Should().BeOfType<GroupComparisonPropertiesViewModel>();
        }

        viewModel.Editor.GroupComparisons.Select(c => c.Title).Should().Equal("Links (1) < Gruppe 9 (fehlt)", "Gruppe 4 < Rechts (2)", "Rechts (2) ? Gruppe 3");
        window.Close();
    }, CancellationToken.None);

    private static bool IsAccentBlue(byte r, byte g, byte b) => b > 170 && r < 90 && g < 140;

    private static bool IsRuleGreen(byte r, byte g, byte b) => r < 90 && g > 120 && b < 120;

    /// <summary>Grautöne der Verbindung (#4b5563, geglättet heller), nicht das neutrale Schwarz der Schrift.</summary>
    private static int ComparisonGrayPixels(WriteableBitmap frame, PixelRect area) =>
        CountPixels(frame, area, (r, g, b) => r is > 40 and < 170 && b > r + 10 && b < 200 && g > r);

    /// <summary>Bereich um das Etikett in Fensterpixeln (etwas größer als das Etikett, mit einem Stück Linie).</summary>
    private static PixelRect BadgeArea(Window window, Control canvas, EditorViewModel editor, GroupComparison comparison)
    {
        var (from, to) = editor.GroupComparisonConnector(comparison)!.Value;
        var center = editor.Viewport.ToScreen(GroupComparisonGeometry.BadgeCenter(from, to));
        var point = canvas.TranslatePoint(new Point(center.X, center.Y), window)!.Value;
        var half = (int)Math.Ceiling(GroupComparisonGeometry.BadgeHalfSize(editor.Viewport.Scale)) + 3;
        return new PixelRect((int)point.X - half, (int)point.Y - half, 2 * half, 2 * half);
    }
}
