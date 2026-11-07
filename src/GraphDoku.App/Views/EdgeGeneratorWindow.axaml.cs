using Avalonia.Controls;
using Avalonia.Interactivity;
using GraphDoku.App.ViewModels;

namespace GraphDoku.App.Views;

/// <summary>Dialog „Kanten erzeugen“. Liefert true, wenn „Kanten erzeugen“ gewählt wurde; das Einfügen übernimmt der Aufrufer.</summary>
public partial class EdgeGeneratorWindow : Window
{
    public EdgeGeneratorWindow() => InitializeComponent();

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EdgeGeneratorViewModel { CanApply: true })
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
