using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GraphDoku.App.Services;
using GraphDoku.App.ViewModels;
using GraphDoku.App.Views;
using GraphDoku.Solver.OrTools;

namespace GraphDoku.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Hauptfenster mit echten Dialogen (auch für den Rendering-Rauchtest verwendbar).</summary>
    public static MainWindow CreateMainWindow()
    {
        var window = new MainWindow();
        window.DataContext = new MainWindowViewModel(new AvaloniaFileDialogService(window), new AvaloniaDialogService(window), new OrToolsSolver());
        return window;
    }
}
