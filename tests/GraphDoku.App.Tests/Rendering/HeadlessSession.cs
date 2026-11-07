using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;

namespace GraphDoku.App.Tests.Rendering;

/// <summary>
/// Avalonia ohne Fenstersystem, aber mit echtem Skia-Rendering (<c>UseHeadlessDrawing = false</c>), damit
/// <c>CaptureRenderedFrame</c> echte Pixel liefert. Läuft auch auf Linux ohne X-Server.
/// </summary>
/// <remarks>
/// Alle Fenster rendern Text in Graustufen (<see cref="TextRenderingMode.Antialias"/>): Skia glättet Text auf Linux
/// sonst mit Subpixel-Farbsäumen (LCD), die die Pixelprüfungen verfälschen – ein schwarzer Given hätte blaue Ränder wie
/// ein Lösungswert, kleine Käfigsummen zu wenige dunkle Pixel. macOS rendert ohnehin in Graustufen (Nr. 164). Gesetzt
/// beim Anwenden der Fenstervorlage, also im ersten Layout-Durchgang von <c>Show()</c> und vor dem ersten Zeichnen: Ein
/// Inhalt, der vorher schon gezeichnet war, behielte die alte Glättung bis zum nächsten Neuzeichnen.
/// </remarks>
public static class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont()
        .AfterSetup(_ => TemplatedControl.TemplateAppliedEvent.AddClassHandler<TopLevel>(
            (topLevel, _) => TextOptions.SetTextRenderingMode(topLevel, TextRenderingMode.Antialias)));
}

/// <summary>Eine Headless-Sitzung (eigener Dispatcher-Thread) für alle Rendering-Tests.</summary>
public sealed class HeadlessSessionFixture : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition(Name)]
public sealed class HeadlessTests : ICollectionFixture<HeadlessSessionFixture>
{
    public const string Name = "Headless-Rendering";
}
