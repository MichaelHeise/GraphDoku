using Avalonia;

namespace GraphDoku.App;

internal static class Program
{
    // Avalonia ist vor OnFrameworkInitializationCompleted nicht initialisiert; hier keine Avalonia-APIs verwenden.
    [STAThread]
    public static int Main(string[] args) => args is [SelfTest.Argument, .. var rest]
        ? SelfTest.Run(rest)
        : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Wird auch vom Designer verwendet.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
