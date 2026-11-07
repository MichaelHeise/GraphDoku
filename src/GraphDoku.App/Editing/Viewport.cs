using GraphDoku.Core.Geometry;

namespace GraphDoku.App.Editing;

/// <summary>Punkt in Bildschirmkoordinaten der Zeichenfläche (Pixel, y nach unten).</summary>
public readonly record struct ScreenPoint(double X, double Y)
{
    public double DistanceTo(ScreenPoint other) => Math.Sqrt(((X - other.X) * (X - other.X)) + ((Y - other.Y) * (Y - other.Y)));
}

/// <summary>Achsenparalleles Rechteck (in Bildschirm- oder Weltkoordinaten), aus zwei beliebigen Ecken gebildet.</summary>
public readonly record struct Bounds(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public static Bounds FromCorners(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

/// <summary>
/// Abbildung Welt ↔ Bildschirm der Zeichenfläche: <c>screen = world · Scale + Offset</c>. Zoom um einen Bildschirmpunkt
/// (der Weltpunkt unter dem Mauszeiger bleibt stehen) und Verschieben.
/// </summary>
public sealed class Viewport
{
    public const double MinScale = 4;
    public const double MaxScale = 600;
    public const double DefaultScale = 48;

    public event EventHandler? Changed;

    /// <summary>Pixel pro Welteinheit.</summary>
    public double Scale { get; private set; } = DefaultScale;

    public double OffsetX { get; private set; } = 40;

    public double OffsetY { get; private set; } = 40;

    public double Width { get; private set; }

    public double Height { get; private set; }

    public bool HasSize => Width > 0 && Height > 0;

    public ScreenPoint ToScreen(WorldPoint world) => new((world.X * Scale) + OffsetX, (world.Y * Scale) + OffsetY);

    public WorldPoint ToWorld(ScreenPoint screen) => new((screen.X - OffsetX) / Scale, (screen.Y - OffsetY) / Scale);

    /// <summary>Sichtbarer Ausschnitt in Weltkoordinaten.</summary>
    public Bounds VisibleWorld
    {
        get
        {
            var topLeft = ToWorld(new ScreenPoint(0, 0));
            var bottomRight = ToWorld(new ScreenPoint(Width, Height));
            return new Bounds(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        }
    }

    public void SetSize(double width, double height)
    {
        if (width == Width && height == Height)
        {
            return;
        }

        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        OnChanged();
    }

    /// <summary>Zoomt um <paramref name="factor"/>; der Weltpunkt unter <paramref name="anchor"/> bleibt an seiner Stelle.</summary>
    public void ZoomAt(ScreenPoint anchor, double factor)
    {
        if (factor <= 0 || double.IsNaN(factor))
        {
            return;
        }

        var world = ToWorld(anchor);
        Scale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        OffsetX = anchor.X - (world.X * Scale);
        OffsetY = anchor.Y - (world.Y * Scale);
        OnChanged();
    }

    public void PanBy(double dx, double dy)
    {
        OffsetX += dx;
        OffsetY += dy;
        OnChanged();
    }

    /// <summary>Passt den Weltbereich <paramref name="world"/> mit Rand <paramref name="marginPixels"/> zentriert ein.</summary>
    public void FitTo(Bounds world, double marginPixels = 32)
    {
        if (!HasSize)
        {
            return;
        }

        var availableWidth = Math.Max(1, Width - (2 * marginPixels));
        var availableHeight = Math.Max(1, Height - (2 * marginPixels));
        var worldWidth = Math.Max(world.Width, 1e-9);
        var worldHeight = Math.Max(world.Height, 1e-9);
        Scale = Math.Clamp(Math.Min(availableWidth / worldWidth, availableHeight / worldHeight), MinScale, MaxScale);
        OffsetX = (Width / 2) - (((world.Left + world.Right) / 2) * Scale);
        OffsetY = (Height / 2) - (((world.Top + world.Bottom) / 2) * Scale);
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
