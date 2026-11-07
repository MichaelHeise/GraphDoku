using System.ComponentModel;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Rules;

namespace GraphDoku.App.Tests;

public class MainWindowViewModelTests
{
    [Fact]
    public void Lists_all_registered_rules()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Title.Should().Be("GraphDoku");
        viewModel.AvailableRules.Should().HaveCount(RuleRegistry.Default.Rules.Count)
            .And.Contain("AllDifferent – Alle verschieden");
        viewModel.Puzzle.Should().BeNull();
    }

    [Fact]
    public void CreateClassicSudoku_command_creates_puzzle_and_updates_status()
    {
        var viewModel = new MainWindowViewModel();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.CreateClassicSudokuCommand.CanExecute(null).Should().BeTrue();
        viewModel.CreateClassicSudokuCommand.Execute(null);

        viewModel.Puzzle.Should().NotBeNull();
        viewModel.Puzzle!.Nodes.Should().HaveCount(81);
        viewModel.Status.Should().Be("Klassisch 9x9: 81 Knoten, 0 Kanten, 27 Gruppen.");
        changed.Should().Contain([nameof(MainWindowViewModel.Puzzle), nameof(MainWindowViewModel.Status)]);
    }
}
