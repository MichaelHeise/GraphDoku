using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering;
using GraphDoku.App.Editing;
using GraphDoku.App.ViewModels;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.App.Controls;

/// <summary>
/// Zeichenfläche des Editors. Zeichnet direkt per <see cref="DrawingContext"/> (Raster, Gruppen, Kanten, Knoten) und
/// reicht Zeiger- und Tastaturereignisse an das <see cref="EditorViewModel"/> (DataContext) weiter. Die gesamte
/// Bearbeitungslogik liegt im ViewModel.
/// </summary>
public sealed class GraphCanvas : Control, ICustomHitTest
{
    private const int MaxRasterCells = 60000;

    private static readonly IBrush BackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#fbfbfa"));
    private static readonly IPen RasterPen = new Pen(new ImmutableSolidColorBrush(Color.Parse("#dcdcd6")), 1);
    private static readonly IBrush NodeFill = new ImmutableSolidColorBrush(Colors.White);
    private static readonly IBrush NodeSelectedFill = new ImmutableSolidColorBrush(Color.Parse("#d6e6ff"));
    private static readonly IBrush NodeStrokeBrush = new ImmutableSolidColorBrush(Color.Parse("#3a3a3a"));
    private static readonly IBrush AccentBrush = new ImmutableSolidColorBrush(Color.Parse("#1d64d8"));
    private static readonly IBrush TextBrush = new ImmutableSolidColorBrush(Color.Parse("#111111"));
    private static readonly IBrush EdgeBrush = new ImmutableSolidColorBrush(Color.Parse("#4a4a4a"));
    private static readonly IBrush RubberFill = new ImmutableSolidColorBrush(Color.Parse("#1d64d8"), 0.12);
    private static readonly IDashStyle Dashed = new ImmutableDashStyle([4, 3], 0);

    private static readonly Dictionary<StandardCursorType, Cursor> Cursors = [];

    private EditorViewModel? _editor;

    static GraphCanvas()
    {
        FocusableProperty.OverrideDefaultValue<GraphCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<GraphCanvas>(true);
    }

    public bool HitTest(Point point) => true;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_editor is not null)
        {
            _editor.VisualChanged -= OnVisualChanged;
            _editor.PropertyChanged -= OnEditorPropertyChanged;
        }

        _editor = DataContext as EditorViewModel;
        if (_editor is not null)
        {
            _editor.VisualChanged += OnVisualChanged;
            _editor.PropertyChanged += OnEditorPropertyChanged;
            _editor.SetViewSize(Bounds.Width, Bounds.Height);
        }

        UpdateCursor();
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _editor?.SetViewSize(e.NewSize.Width, e.NewSize.Height);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_editor is null)
        {
            return;
        }

        Focus();
        var point = e.GetCurrentPoint(this);
        var button = point.Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.MiddleButtonPressed => PointerButton.Middle,
            PointerUpdateKind.RightButtonPressed => PointerButton.Right,
            _ => PointerButton.Left,
        };
        e.Pointer.Capture(this);
        _editor.PointerPressed(ToScreen(point.Position), button, ToModifiers(e.KeyModifiers));
        UpdateCursor();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _editor?.PointerMoved(ToScreen(e.GetPosition(this)), ToModifiers(e.KeyModifiers));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_editor is null)
        {
            return;
        }

        var button = e.InitialPressMouseButton switch
        {
            MouseButton.Middle => PointerButton.Middle,
            MouseButton.Right => PointerButton.Right,
            _ => PointerButton.Left,
        };
        _editor.PointerReleased(ToScreen(e.GetPosition(this)), button, ToModifiers(e.KeyModifiers));
        e.Pointer.Capture(null);
        UpdateCursor();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_editor?.IsGestureActive == true)
        {
            _editor.CancelGesture();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _editor?.PointerWheel(ToScreen(e.GetPosition(this)), e.Delta.Y);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_editor is null || e.Handled || e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return;
        }

        e.Handled = true;
        switch (e.Key)
        {
            case Key.Space:
                _editor.SetSpaceHeld(true);
                UpdateCursor();
                break;
            case Key.Delete:
                _editor.DeleteSelection();
                break;
            case Key.Back:
                _editor.ClearGivens();
                break;
            case Key.Escape:
                _editor.Escape();
                break;
            case >= Key.D0 and <= Key.D9:
                _editor.EnterValueCharacter((char)('0' + (e.Key - Key.D0)));
                break;
            case >= Key.NumPad0 and <= Key.NumPad9:
                _editor.EnterValueCharacter((char)('0' + (e.Key - Key.NumPad0)));
                break;
            case >= Key.A and <= Key.F:
                e.Handled = _editor.EnterValueCharacter((char)('A' + (e.Key - Key.A)));
                break;
            case Key.OemMinus or Key.Subtract:
                _editor.EnterValueCharacter('-');
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space && _editor is not null)
        {
            _editor.SetSpaceHeld(false);
            UpdateCursor();
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(BackgroundBrush, bounds);
        if (_editor is null)
        {
            return;
        }

        var puzzle = _editor.Puzzle;
        var viewport = _editor.Viewport;
        var nodes = new Dictionary<int, Node>();
        foreach (var node in puzzle.Nodes)
        {
            nodes.TryAdd(node.Id, node);
        }

        var radius = EditorGeometry.NodeRadius(puzzle.Grid) * viewport.Scale;
        DrawRaster(context, puzzle.Grid, viewport);
        DrawGroups(context, puzzle, viewport, nodes, radius);
        DrawEdges(context, puzzle, viewport, nodes, radius);
        DrawEdgePreview(context, puzzle, viewport, nodes);
        DrawNodes(context, puzzle, viewport, radius);
        DrawOrderBadges(context, puzzle, viewport, nodes, radius);
        if (_editor.RubberBand is { } band)
        {
            context.DrawRectangle(RubberFill, new Pen(AccentBrush, 1), new Rect(band.Left, band.Top, band.Width, band.Height));
        }
    }

    private static void DrawRaster(DrawingContext context, GridType grid, Viewport viewport)
    {
        if (viewport.Scale < 8)
        {
            return;
        }

        var cells = VisibleCells(grid, viewport.VisibleWorld).Take(MaxRasterCells + 1).ToList();
        if (cells.Count > MaxRasterCells)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            foreach (var cell in cells)
            {
                AddPolygon(g, viewport, GridGeometry.GetCellVertices(grid, cell.X, cell.Y));
            }
        }

        context.DrawGeometry(null, RasterPen, geometry);
    }

    private void DrawGroups(DrawingContext context, Puzzle puzzle, Viewport viewport, Dictionary<int, Node> nodes, double radius)
    {
        foreach (var group in puzzle.Groups)
        {
            var selected = _editor!.SelectedGroupId == group.Id;
            if (!selected && !_editor.IsGroupVisible(group.Id))
            {
                continue;
            }

            var color = Color.Parse(GroupPalette.EffectiveColor(group.Color, group.Id));
            var members = group.Nodes.Where(nodes.ContainsKey).Select(id => nodes[id]).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            // Fläche nur für die gewählte Gruppe und Gruppen mit eigener Farbe; sonst würden sich überlappende Gruppen
            // (Zeilen, Spalten, Blöcke) zu einem unruhigen Flickenteppich mischen. Der Umriss zeigt jede sichtbare Gruppe.
            var hasOwnColor = GroupPalette.IsValidHex(group.Color);
            var fill = selected ? new ImmutableSolidColorBrush(color, 0.30) : hasOwnColor ? new ImmutableSolidColorBrush(color, 0.12) : null;
            var cellGeometry = new StreamGeometry();
            using (var g = cellGeometry.Open())
            {
                foreach (var node in members.DistinctBy(n => (n.X, n.Y)))
                {
                    AddPolygon(g, viewport, GridGeometry.GetCellVertices(puzzle.Grid, node.X, node.Y));
                }
            }

            if (fill is not null)
            {
                context.DrawGeometry(fill, null, cellGeometry);
            }

            var outline = new StreamGeometry();
            using (var g = outline.Open())
            {
                foreach (var (a, b) in GroupOutline.BoundarySegments(puzzle.Grid, members.Select(n => new GridPoint(n.X, n.Y))))
                {
                    g.BeginFigure(ToPoint(viewport.ToScreen(a)), false);
                    g.LineTo(ToPoint(viewport.ToScreen(b)));
                    g.EndFigure(false);
                }
            }

            context.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(color, 0.9), selected ? 3.5 : 2, lineCap: PenLineCap.Round), outline);

            var ordered = _editor.Registry.TryGet(group.Rule, out var rule) && rule.Kind.IsOrdered();
            if (ordered && members.Count > 0)
            {
                DrawThermometer(context, puzzle.Grid, viewport, members, radius, color);
            }
        }
    }

    private static void DrawThermometer(DrawingContext context, GridType grid, Viewport viewport, List<Node> members, double radius, Color color)
    {
        var brush = new ImmutableSolidColorBrush(color, 0.55);
        var points = members.Select(n => ToPoint(viewport.ToScreen(EditorGeometry.Center(grid, n)))).ToList();
        if (points.Count > 1)
        {
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(points[0], false);
                foreach (var point in points.Skip(1))
                {
                    g.LineTo(point);
                }

                g.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(brush, Math.Max(3, radius * 0.7), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
        }

        // Kolben am Anfang: zeigt die Richtung (Werte steigen vom Kolben weg).
        context.DrawEllipse(brush, null, points[0], radius * 1.18, radius * 1.18);
    }

    private void DrawEdges(DrawingContext context, Puzzle puzzle, Viewport viewport, Dictionary<int, Node> nodes, double radius)
    {
        foreach (var edge in puzzle.Edges)
        {
            if (!nodes.TryGetValue(edge.From, out var fromNode) || !nodes.TryGetValue(edge.To, out var toNode))
            {
                continue;
            }

            var a = ToPoint(viewport.ToScreen(EditorGeometry.Center(puzzle.Grid, fromNode)));
            var b = ToPoint(viewport.ToScreen(EditorGeometry.Center(puzzle.Grid, toNode)));
            var vector = b - a;
            var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
            if (length < 1e-6)
            {
                continue;
            }

            var unit = vector / length;
            var known = _editor!.Registry.Contains(edge.Rule);
            var selected = _editor.SelectedEdgeId == edge.Id;
            var brush = selected ? AccentBrush : EdgeBrush;
            var pen = new Pen(brush, selected ? 3.2 : 1.8, known ? null : Dashed, PenLineCap.Round);
            if (length > 2 * radius)
            {
                context.DrawLine(pen, a + (unit * radius), b - (unit * radius));
            }

            DrawEdgeMark(context, edge, a + (vector / 2), unit, radius, brush);
        }
    }

    private static void DrawEdgeMark(DrawingContext context, Edge edge, Point middle, Vector unit, double nodeRadius, IBrush lineBrush)
    {
        // Markengröße relativ zum Knotenradius, damit sie auch im engeren Dreiecksgitter zwischen die Kreise passt.
        var dot = Math.Clamp(nodeRadius * 0.32, 3.5, 14);
        switch (edge.Rule)
        {
            case "Difference":
            {
                var k = SafeInt(edge.Params, "k");
                var atLeast = SafeString(edge.Params, "mode") == "atLeast";
                var label = k == 1 && !atLeast ? null : (atLeast ? "≥" : string.Empty) + (k?.ToString(CultureInfo.InvariantCulture) ?? "?");
                DrawDot(context, middle, dot, Brushes.White, lineBrush, label, TextBrush);
                break;
            }

            case "Ratio":
            {
                var factor = SafeInt(edge.Params, "factor") ?? 2;
                DrawDot(context, middle, dot, Brushes.Black, lineBrush, factor == 2 ? null : factor.ToString(CultureInfo.InvariantCulture), Brushes.White);
                break;
            }

            case "LessThan":
            {
                // Pfeil von „from“ (kleiner) nach „to“ (größer).
                var size = Math.Clamp(nodeRadius * 0.36, 5, 16);
                var normal = new Vector(-unit.Y, unit.X);
                var tip = middle + (unit * size);
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(tip, true);
                    g.LineTo(middle - (unit * size) + (normal * size * 0.75));
                    g.LineTo(middle - (unit * size * 0.45));
                    g.LineTo(middle - (unit * size) - (normal * size * 0.75));
                    g.EndFigure(true);
                }

                context.DrawGeometry(lineBrush, null, geometry);
                break;
            }
        }
    }

    private static void DrawDot(DrawingContext context, Point center, double radius, IBrush fill, IBrush stroke, string? label, IBrush textBrush)
    {
        FormattedText? text = null;
        if (label is not null)
        {
            text = CreateText(label, Math.Max(9, radius * 1.3), textBrush, FontWeight.SemiBold);
            radius = Math.Max(radius, (Math.Max(text.Width, text.Height) / 2) + 3);
        }

        context.DrawEllipse(fill, new Pen(stroke, 1.5), center, radius, radius);
        if (text is not null)
        {
            context.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
        }
    }

    private void DrawEdgePreview(DrawingContext context, Puzzle puzzle, Viewport viewport, Dictionary<int, Node> nodes)
    {
        if (_editor!.EdgePreview is { } preview && nodes.TryGetValue(preview.FromNodeId, out var from))
        {
            context.DrawLine(
                new Pen(AccentBrush, 2, Dashed, PenLineCap.Round),
                ToPoint(viewport.ToScreen(EditorGeometry.Center(puzzle.Grid, from))),
                ToPoint(viewport.ToScreen(preview.To)));
        }
    }

    private void DrawNodes(DrawingContext context, Puzzle puzzle, Viewport viewport, double radius)
    {
        var normalPen = new Pen(NodeStrokeBrush, 1.5);
        var selectedPen = new Pen(AccentBrush, 3);
        var visible = viewport.VisibleWorld;
        var margin = EditorGeometry.NodeRadius(puzzle.Grid);
        foreach (var node in puzzle.Nodes)
        {
            var world = EditorGeometry.Center(puzzle.Grid, node);
            if (world.X < visible.Left - margin || world.X > visible.Right + margin || world.Y < visible.Top - margin || world.Y > visible.Bottom + margin)
            {
                continue;
            }

            var center = ToPoint(viewport.ToScreen(world));
            var selected = _editor!.IsNodeSelected(node.Id);
            context.DrawEllipse(selected ? NodeSelectedFill : NodeFill, selected ? selectedPen : normalPen, center, radius, radius);
            if (node.Given is int given && radius >= 5)
            {
                var text = CreateText(ValueText.Format(given, puzzle.GetEffectiveDomain(node)), radius * 1.1, TextBrush, FontWeight.SemiBold);
                context.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
            }
        }
    }

    private void DrawOrderBadges(DrawingContext context, Puzzle puzzle, Viewport viewport, Dictionary<int, Node> nodes, double radius)
    {
        IReadOnlyList<int>? order = _editor!.SelectedGroupId is int groupId
            ? puzzle.FindGroup(groupId)?.Nodes.ToList()
            : _editor.ShowSelectionOrder && _editor.SelectedNodeIds.Count > 1 ? _editor.SelectedNodeIds : null;
        if (order is null || radius < 9)
        {
            return;
        }

        var badge = Math.Max(6, radius * 0.36);
        for (var i = 0; i < order.Count; i++)
        {
            if (!nodes.TryGetValue(order[i], out var node))
            {
                continue;
            }

            var center = ToPoint(viewport.ToScreen(EditorGeometry.Center(puzzle.Grid, node)));
            var position = new Point(center.X + (radius * 0.72), center.Y - (radius * 0.72));
            var text = CreateText((i + 1).ToString(CultureInfo.InvariantCulture), badge * 1.15, Brushes.White, FontWeight.Bold);
            var width = Math.Max(badge * 2, text.Width + 4);
            context.DrawRectangle(AccentBrush, null, new RoundedRect(new Rect(position.X - (width / 2), position.Y - badge, width, badge * 2), badge));
            context.DrawText(text, new Point(position.X - (text.Width / 2), position.Y - (text.Height / 2)));
        }
    }

    private static IEnumerable<GridPoint> VisibleCells(GridType grid, Bounds world)
    {
        switch (grid)
        {
            case GridType.Square:
                for (var y = (int)Math.Floor(world.Top) - 1; y <= (int)Math.Ceiling(world.Bottom) + 1; y++)
                {
                    for (var x = (int)Math.Floor(world.Left) - 1; x <= (int)Math.Ceiling(world.Right) + 1; x++)
                    {
                        yield return new GridPoint(x, y);
                    }
                }

                break;
            case GridType.Hex:
                var rowHeight = Math.Sqrt(3) / 2;
                for (var r = (int)Math.Floor(world.Top / rowHeight) - 1; r <= (int)Math.Ceiling(world.Bottom / rowHeight) + 1; r++)
                {
                    for (var q = (int)Math.Floor(world.Left - (r / 2.0)) - 1; q <= (int)Math.Ceiling(world.Right - (r / 2.0)) + 1; q++)
                    {
                        yield return new GridPoint(q, r);
                    }
                }

                break;
            case GridType.Triangle:
                var height = GridGeometry.TriangleRowHeight;
                for (var y = (int)Math.Floor(world.Top / height) - 1; y <= (int)Math.Ceiling(world.Bottom / height) + 1; y++)
                {
                    for (var x = (int)Math.Floor(world.Left * 2) - 2; x <= (int)Math.Ceiling(world.Right * 2) + 2; x++)
                    {
                        yield return new GridPoint(x, y);
                    }
                }

                break;
        }
    }

    private static void AddPolygon(StreamGeometryContext g, Viewport viewport, IReadOnlyList<WorldPoint> vertices)
    {
        g.BeginFigure(ToPoint(viewport.ToScreen(vertices[0])), true);
        for (var i = 1; i < vertices.Count; i++)
        {
            g.LineTo(ToPoint(viewport.ToScreen(vertices[i])));
        }

        g.EndFigure(true);
    }

    private static FormattedText CreateText(string text, double size, IBrush brush, FontWeight weight) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), Math.Max(1, size), brush);

    private static int? SafeInt(RuleParams parameters, string name)
    {
        try
        {
            return parameters.GetIntOrNull(name);
        }
        catch (RuleParamsException)
        {
            return null;
        }
    }

    private static string? SafeString(RuleParams parameters, string name)
    {
        try
        {
            return parameters.GetString(name, null);
        }
        catch (RuleParamsException)
        {
            return null;
        }
    }

    private static Point ToPoint(ScreenPoint point) => new(point.X, point.Y);

    private static ScreenPoint ToScreen(Point point) => new(point.X, point.Y);

    private static EditorModifiers ToModifiers(KeyModifiers modifiers)
    {
        var result = EditorModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result |= EditorModifiers.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta))
        {
            result |= EditorModifiers.Command;
        }

        return result;
    }

    private void OnVisualChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.Tool))
        {
            UpdateCursor();
        }
    }

    private void UpdateCursor()
    {
        var type = _editor is null ? StandardCursorType.Arrow
            : _editor.IsPanning || _editor.Tool == EditorTool.Pan ? StandardCursorType.SizeAll
            : _editor.Tool is EditorTool.PlaceNode or EditorTool.DrawEdge ? StandardCursorType.Cross
            : StandardCursorType.Arrow;
        if (!Cursors.TryGetValue(type, out var cursor))
        {
            cursor = new Cursor(type);
            Cursors[type] = cursor;
        }

        Cursor = cursor;
    }
}
