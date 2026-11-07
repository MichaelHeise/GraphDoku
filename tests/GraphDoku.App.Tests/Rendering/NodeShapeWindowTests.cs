using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GraphDoku.App.Editing;
using GraphDoku.App.Tests.Helpers;
using GraphDoku.App.ViewModels;
using GraphDoku.App.Views;
using GraphDoku.Core.Model;
using GraphDoku.Core.Templates;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App.Tests.Rendering;

/// <summary>Paket 7 im echten (headless) Hauptfenster: Menü Ansicht → „Knoten als Kreise“ / „Knoten als Zellen (…)“.</summary>
[Collection(HeadlessTests.Name)]
public sealed class NodeShapeWindowTests(HeadlessSessionFixture fixture)
{
    [Fact]
    public Task View_menu_switches_between_circles_and_cells() => fixture.Session.Dispatch(() =>
    {
        var viewModel = new MainWindowViewModel(new FakeFileDialogService(), new FakeDialogService(), new OrToolsSolver());
        viewModel.Editor.LoadPuzzle(ClassicSudoku.Create());
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var top = window.GetVisualDescendants().OfType<MenuItem>().Single(m => Equals(m.Header, "_Ansicht") && m.Parent is Menu);
        var circles = top.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Knoten als Kreise"));
        var cells = top.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Knoten als Zellen (Quadrate)"));
        circles.IsChecked.Should().BeTrue();
        cells.IsChecked.Should().BeFalse();

        void Choose(MenuItem item)
        {
            top.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            item.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
        }

        // Der Menüeintrag muss die Einstellung im ViewModel ändern, nicht nur sein eigenes Häkchen (Nr. 114).
        Choose(cells);
        viewModel.Editor.NodeShape.Should().Be(NodeShape.Cell);
        cells.IsChecked.Should().BeTrue();
        circles.IsChecked.Should().BeFalse();
        viewModel.Editor.IsDirty.Should().BeFalse();

        Choose(circles);
        viewModel.Editor.NodeShape.Should().Be(NodeShape.Circle);
        circles.IsChecked.Should().BeTrue();
        cells.IsChecked.Should().BeFalse();

        // Umgekehrt folgt das Menü dem ViewModel, und der Text nennt die Zellform des geladenen Gitters.
        viewModel.Editor.LoadPuzzle(new Puzzle("Waben", GridType.Hex, new ValueDomain(1, 7)));
        viewModel.Editor.NodeShape = NodeShape.Cell;
        Dispatcher.UIThread.RunJobs();
        cells.Header.Should().Be("Knoten als Zellen (Sechsecke)");
        cells.IsChecked.Should().BeTrue();
        circles.IsChecked.Should().BeFalse();
        window.Close();
    }, CancellationToken.None);
}
