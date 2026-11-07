using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GraphDoku.App.Controls;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.App.Views;
using GraphDoku.Core.Templates;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests.Rendering;

/// <summary>Erklär-Modus im echten (headless) Hauptfenster: F7, Pfeiltasten, Pos1/Ende, Schieberegler.</summary>
[Collection(HeadlessTests.Name)]
public sealed class ExplanationWindowTests(HeadlessSessionFixture fixture)
{
    private const string Easy = "53..7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79";

    [Fact]
    public Task F7_explains_and_keys_navigate_when_canvas_has_focus() => fixture.Session.Dispatch(async () =>
    {
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        viewModel.Editor.LoadPuzzle(ClassicSudoku.Create(Easy));
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var explain = viewModel.Explain;
        window.FindControl<Border>("StepBar")!.IsVisible.Should().BeFalse();

        window.KeyPress(Key.F7, RawInputModifiers.None, PhysicalKey.F7, null);
        for (var i = 0; i < 200 && !explain.IsActive; i++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }

        explain.IsActive.Should().BeTrue(explain.StatusMessage);
        window.FindControl<Border>("StepBar")!.IsVisible.Should().BeTrue();
        window.FindControl<GraphCanvas>("Canvas")!.Focus();
        Dispatcher.UIThread.RunJobs();

        void Press(Key key, PhysicalKey physical)
        {
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            Dispatcher.UIThread.RunJobs();
        }

        Press(Key.Right, PhysicalKey.ArrowRight);
        Press(Key.Right, PhysicalKey.ArrowRight);
        Press(Key.Right, PhysicalKey.ArrowRight);
        explain.Position.Should().Be(3);
        Press(Key.Left, PhysicalKey.ArrowLeft);
        explain.Position.Should().Be(2);
        Press(Key.End, PhysicalKey.End);
        explain.Position.Should().Be(explain.StepCount);
        Press(Key.Right, PhysicalKey.ArrowRight);
        explain.Position.Should().Be(explain.StepCount, "am Ende geht es nicht weiter");
        Press(Key.Home, PhysicalKey.Home);
        explain.Position.Should().Be(0);

        // Nach einem Klick auf „Vor ›“ wirken die Pfeiltasten weiter.
        var next = window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Vor ›"));
        next.Focus();
        Dispatcher.UIThread.RunJobs();
        Press(Key.Right, PhysicalKey.ArrowRight);
        explain.Position.Should().Be(1);

        // In einem Textfeld (Zeitlimit) bleiben die Pfeiltasten beim Textfeld.
        var textBox = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Text == "30");
        textBox.Focus();
        Dispatcher.UIThread.RunJobs();
        Press(Key.Right, PhysicalKey.ArrowRight);
        explain.Position.Should().Be(1);

        // Schieberegler an das ViewModel gebunden.
        var slider = window.FindControl<Slider>("StepSlider")!;
        slider.Maximum.Should().Be(explain.StepCount);
        slider.Value = 42;
        Dispatcher.UIThread.RunJobs();
        explain.Position.Should().Be(42);
        explain.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        slider.Value.Should().Be(43);

        // Ein Given eintragen beendet den Modus, die Leiste meldet es.
        viewModel.Editor.SelectNodes([ClassicSudoku.NodeId(0, 2)]);
        window.FindControl<GraphCanvas>("Canvas")!.Focus();
        Press(Key.D4, PhysicalKey.Digit4);
        explain.IsActive.Should().BeFalse();
        explain.Outcome.Should().Be(ExplainOutcome.Outdated);
        window.FindControl<StackPanel>("ExplanationPanel")!.IsEffectivelyVisible.Should().BeFalse();
        window.Close();
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task Menu_item_switches_candidates_in_the_view_model() => fixture.Session.Dispatch(async () =>
    {
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        viewModel.Editor.LoadPuzzle(ClassicSudoku.Create(Easy));
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var explain = viewModel.Explain;
        await explain.ExplainCommand.ExecuteAsync(null);
        explain.IsActive.Should().BeTrue(explain.StatusMessage);
        explain.ShowCandidates.Should().BeTrue();

        var top = window.GetVisualDescendants().OfType<MenuItem>().Single(m => Equals(m.Header, "_Lösen") && m.Parent is Menu);
        var item = top.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Kandidaten anzeigen"));
        var checkBox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Kandidaten"));
        item.IsChecked.Should().BeTrue();

        void Choose()
        {
            top.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            item.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
        }

        // Der Menüeintrag muss die Einstellung im ViewModel ändern, nicht nur sein eigenes Häkchen.
        Choose();
        explain.ShowCandidates.Should().BeFalse();
        viewModel.Editor.Explanation!.ShowCandidates.Should().BeFalse();
        checkBox.IsChecked.Should().BeFalse("Häkchen in der Schrittleiste und im Menü stimmen überein");
        item.IsChecked.Should().BeFalse();

        Choose();
        explain.ShowCandidates.Should().BeTrue();
        checkBox.IsChecked.Should().BeTrue();

        // Umgekehrt folgt das Menü dem Häkchen der Schrittleiste.
        checkBox.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        explain.ShowCandidates.Should().BeFalse();
        item.IsChecked.Should().BeFalse();
        window.Close();
        return true;
    }, CancellationToken.None);
}
