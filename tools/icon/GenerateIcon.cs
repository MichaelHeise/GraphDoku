#:package SkiaSharp@3.119.4
#:property ManagePackageVersionsCentrally=false
#:property PublishAot=false

// Erzeugt das App-Icon von GraphDoku: sieben Knoten im Sechseck (wie ein Ausschnitt des Hexagonalgitters), verbunden
// durch Kanten, ein Knoten hervorgehoben. Ausgabe: PNG-Dateien in allen Größen, die macOS (.icns), Windows (.ico) und
// Linux brauchen. Die Container-Formate baut tools/icon/build-icons.sh daraus.
//
// Aufruf (aus dem Repo-Wurzelverzeichnis): dotnet run tools/icon/GenerateIcon.cs -- <Ausgabeordner>

using SkiaSharp;

var output = args.Length > 0 ? args[0] : "icon-out";
Directory.CreateDirectory(output);

int[] sizes = [16, 24, 32, 48, 64, 128, 256, 512, 1024];
foreach (var size in sizes)
{
    using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bitmap))
    {
        Draw(canvas, size);
    }

    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var file = File.Create(Path.Combine(output, $"icon-{size}.png"));
    data.SaveTo(file);
}

Console.WriteLine($"{sizes.Length} PNG-Dateien in {Path.GetFullPath(output)}");

static void Draw(SKCanvas canvas, int size)
{
    canvas.Clear(SKColors.Transparent);
    float s = size;

    // Hintergrund: abgerundetes Quadrat mit leichtem Verlauf (macOS-Raster: etwa 10 % Rand).
    var margin = s * 0.08f;
    var rect = new SKRect(margin, margin, s - margin, s - margin);
    var corner = s * 0.19f;
    using (var background = new SKPaint { IsAntialias = true })
    {
        background.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, rect.Top), new SKPoint(0, rect.Bottom),
            [new SKColor(0x2B, 0x4C, 0x7E), new SKColor(0x16, 0x2A, 0x4A)],
            SKShaderTileMode.Clamp);
        canvas.DrawRoundRect(rect, corner, corner, background);
    }

    // Sieben Knoten: Mitte und sechs Nachbarn auf einem Sechseck.
    var center = new SKPoint(s / 2, s / 2);
    var ring = s * 0.25f;
    var points = new SKPoint[7];
    points[0] = center;
    for (var i = 0; i < 6; i++)
    {
        var angle = (Math.PI / 3 * i) - (Math.PI / 2);
        points[i + 1] = new SKPoint(center.X + (ring * (float)Math.Cos(angle)), center.Y + (ring * (float)Math.Sin(angle)));
    }

    // Kanten: Speichen von der Mitte und der äußere Ring.
    var small = size <= 32;
    using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(0x9F, 0xC2, 0xEE), StrokeWidth = Math.Max(1f, s * (small ? 0.045f : 0.028f)), StrokeCap = SKStrokeCap.Round })
    {
        for (var i = 1; i <= 6; i++)
        {
            canvas.DrawLine(points[0], points[i], edge);
            canvas.DrawLine(points[i], points[(i % 6) + 1], edge);
        }
    }

    var radius = s * (small ? 0.085f : 0.075f);
    using var node = new SKPaint { IsAntialias = true, Color = SKColors.White };
    using var accent = new SKPaint { IsAntialias = true, Color = new SKColor(0xF2, 0xA1, 0x3B) };
    for (var i = 0; i < points.Length; i++)
    {
        canvas.DrawCircle(points[i], i == 0 ? radius * 1.35f : radius, i == 0 ? accent : node);
    }
}
