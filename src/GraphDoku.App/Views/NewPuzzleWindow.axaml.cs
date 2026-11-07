using Avalonia.Controls;
using Avalonia.Interactivity;
using GraphDoku.App.ViewModels;

namespace GraphDoku.App.Views;

public partial class NewPuzzleWindow : Window
{
    public NewPuzzleWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewPuzzleViewModel { IsValid: true })
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
