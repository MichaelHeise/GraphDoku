using Avalonia;
using Avalonia.Headless;

namespace GraphDoku.App.Tests.Rendering;

/// <summary>
/// Avalonia ohne Fenstersystem, aber mit echtem Skia-Rendering (<c>UseHeadlessDrawing = false</c>), damit
/// <c>CaptureRenderedFrame</c> echte Pixel liefert. Läuft auch auf Linux ohne X-Server.
/// </summary>
public static class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
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
