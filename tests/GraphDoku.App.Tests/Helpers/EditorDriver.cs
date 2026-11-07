using GraphDoku.App.Editing;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;

namespace GraphDoku.App.Tests.Helpers;

/// <summary>Bedient den Editor wie die Zeichenfläche: Zeigerereignisse in Bildschirmkoordinaten, Tastatureingaben.</summary>
internal static class EditorDriver
{
    public static EditorViewModel CreateEditor(Puzzle? puzzle = null)
    {
        var editor = new EditorViewModel();
        editor.SetViewSize(1000, 800);
        if (puzzle is not null)
        {
            editor.LoadPuzzle(puzzle);
        }

        return editor;
    }

    public static ScreenPoint ScreenOf(this EditorViewModel editor, GridPoint point) =>
        editor.Viewport.ToScreen(GridGeometry.ToWorld(editor.Puzzle.Grid, point));

    public static ScreenPoint ScreenOf(this EditorViewModel editor, double worldX, double worldY) =>
        editor.Viewport.ToScreen(new WorldPoint(worldX, worldY));

    public static void Click(this EditorViewModel editor, GridPoint point, EditorModifiers modifiers = EditorModifiers.None) =>
        editor.Click(editor.ScreenOf(point), modifiers);

    public static void Click(this EditorViewModel editor, ScreenPoint point, EditorModifiers modifiers = EditorModifiers.None)
    {
        editor.PointerPressed(point, PointerButton.Left, modifiers);
        editor.PointerReleased(point, PointerButton.Left, modifiers);
    }

    /// <summary>Zieht über die angegebenen Punkte (mit Zwischenschritten, wie eine echte Maus).</summary>
    public static void Drag(this EditorViewModel editor, IReadOnlyList<ScreenPoint> path, EditorModifiers modifiers = EditorModifiers.None, PointerButton button = PointerButton.Left)
    {
        editor.PointerPressed(path[0], button, modifiers);
        for (var i = 1; i < path.Count; i++)
        {
            var from = path[i - 1];
            var to = path[i];
            for (var step = 1; step <= 4; step++)
            {
                var t = step / 4.0;
                editor.PointerMoved(new ScreenPoint(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t)), modifiers);
            }
        }

        editor.PointerReleased(path[^1], button, modifiers);
    }

    public static void DragGrid(this EditorViewModel editor, params GridPoint[] points) =>
        editor.Drag([.. points.Select(editor.ScreenOf)]);

    /// <summary>Auswahlrahmen über die Weltkoordinaten-Ecken ziehen (Werkzeug „Auswahl“).</summary>
    public static void RubberBand(this EditorViewModel editor, double x1, double y1, double x2, double y2, EditorModifiers modifiers = EditorModifiers.None) =>
        editor.Drag([editor.ScreenOf(x1, y1), editor.ScreenOf(x2, y2)], modifiers);

    public static void Type(this EditorViewModel editor, string characters)
    {
        foreach (var c in characters)
        {
            editor.EnterValueCharacter(c);
        }
    }

    public static Puzzle Snapshot(this EditorViewModel editor) => editor.Puzzle.Clone();

    public static void ShouldEqual(this Puzzle actual, Puzzle expected) =>
        PuzzleComparer.Differences(actual, expected).Should().BeEmpty();
}
