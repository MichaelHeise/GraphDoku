using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering;
using GraphDoku.App.Editing;
using GraphDoku.App.Explaining;
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
    private static readonly IBrush GroupBoundaryBrush = new ImmutableSolidColorBrush(Color.Parse("#2f3640"), 0.85);
    private static readonly IBrush HaloBrush = new ImmutableSolidColorBrush(Color.Parse("#1d64d8"), 0.28);
    private static readonly IDashStyle Dashed = new ImmutableDashStyle([4, 3], 0);
    private static readonly IPen NotEqualPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#8a8a8a")), 1.3, lineCap: PenLineCap.Round);
    private static readonly IBrush SumLabelBackground = new ImmutableSolidColorBrush(Colors.White, 0.85);
    private static readonly IBrush SumTextBrush = new ImmutableSolidColorBrush(Color.Parse("#1f2937"));

    /// <summary>Lösungswerte: blau und normal statt schwarz und halbfett wie Givens (Paket 4).</summary>
    public static readonly Color SolutionColor = Color.Parse("#1d4ed8");

    /// <summary>Knoten, in denen sich zwei Lösungen eines mehrdeutigen Rätsels unterscheiden.</summary>
    public static readonly Color SolutionDifferenceColor = Color.Parse("#ea580c");

    private static readonly IBrush SolutionBrush = new ImmutableSolidColorBrush(SolutionColor);
    private static readonly IBrush SolutionDifferenceBrush = new ImmutableSolidColorBrush(SolutionDifferenceColor);
    private static readonly IBrush SolutionDifferenceFill = new ImmutableSolidColorBrush(Color.Parse("#ffedd5"));

    /// <summary>Erklär-Modus: Knoten des aktuellen Schritts (gelb), bei einer Verzweigung orange.</summary>
    public static readonly Color ExplanationCurrentColor = Color.Parse("#ca8a04");

    /// <summary>Erklär-Modus: Knoten, Kante bzw. Gruppe der verantwortlichen Regel (grün).</summary>
    public static readonly Color ExplanationRuleColor = Color.Parse("#16a34a");

    /// <summary>Erklär-Modus: durch eine Verzweigung (Raten) gesetzte Werte und der Verzweigungsknoten.</summary>
    public static readonly Color ExplanationBranchColor = Color.Parse("#ea580c");

    /// <summary>Erklär-Modus: vom aktuellen Schritt gestrichene Kandidaten (rot, durchgestrichen).</summary>
    public static readonly Color ExplanationRemovedColor = Color.Parse("#dc2626");

    private static readonly IBrush ExplanationCurrentFill = new ImmutableSolidColorBrush(Color.Parse("#fef08a"));
    private static readonly IBrush ExplanationBranchFill = new ImmutableSolidColorBrush(Color.Parse("#fed7aa"));
    private static readonly IBrush ExplanationRuleFill = new ImmutableSolidColorBrush(Color.Parse("#bbf7d0"));
    private static readonly IBrush ExplanationRuleBrush = new ImmutableSolidColorBrush(ExplanationRuleColor);
    private static readonly IBrush ExplanationBranchBrush = new ImmutableSolidColorBrush(ExplanationBranchColor);
    private static readonly IBrush GuessedValueBrush = new ImmutableSolidColorBrush(Color.Parse("#c2410c"));
    private static readonly IBrush RemovedCandidateBrush = new ImmutableSolidColorBrush(ExplanationRemovedColor);
    private static readonly IBrush CandidateBrush = new ImmutableSolidColorBrush(Color.Parse("#475569"));
    private static readonly IPen ExplanationCurrentPen = new ImmutablePen(new ImmutableSolidColorBrush(ExplanationCurrentColor), 3);
    private static readonly IPen ExplanationBranchPen = new ImmutablePen(new ImmutableSolidColorBrush(ExplanationBranchColor), 3);
    private static readonly IPen ExplanationRulePen = new ImmutablePen(new ImmutableSolidColorBrush(ExplanationRuleColor), 2.5);
    private static readonly IPen EmptyNodePen = new ImmutablePen(new ImmutableSolidColorBrush(ExplanationRemovedColor), 2.5);
    private static readonly IPen NormalNodePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#3a3a3a")), 1.5);

    private static readonly Dictionary<StandardCursorType, Cursor> Cursors = [];

    /// <summary>Zwischenspeicher für die vielen kleinen Kandidatentexte (sonst bis zu 16 neue Texte pro Knoten und Bild).</summary>
    private readonly Dictionary<(string Text, double Size, IBrush Brush), FormattedText> _candidateTexts = [];

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

        switch (e.Key)
        {
            case Key.Space:
                _editor.SetSpaceHeld(true);
                UpdateCursor();
                e.Handled = true;
                break;
            case Key.Escape:
                _editor.Escape();
                e.Handled = true;
                break;
            default:
                e.Handled = HandleEditKey(_editor, e.Key);
                break;
        }
    }

    /// <summary>
    /// Bearbeitungstasten der Zeichenfläche: Entf löscht die Auswahl, ⌫ die Givens, Ziffern/A–F/„-“ tragen Givens ein.
    /// Das Hauptfenster ruft dies auch auf, wenn ein Element des Panels oder der Gruppenliste den Fokus hat (aber kein
    /// Textfeld und keine Auswahlliste). true, wenn die Taste verarbeitet wurde.
    /// </summary>
    public static bool HandleEditKey(EditorViewModel editor, Key key)
    {
        ArgumentNullException.ThrowIfNull(editor);
        switch (key)
        {
            case Key.Delete:
                editor.DeleteSelection();
                return true;
            case Key.Back:
                editor.ClearGivens();
                return true;
            case >= Key.D0 and <= Key.D9:
                return editor.EnterValueCharacter((char)('0' + (key - Key.D0)));
            case >= Key.NumPad0 and <= Key.NumPad9:
                return editor.EnterValueCharacter((char)('0' + (key - Key.NumPad0)));
            case >= Key.A and <= Key.F:
                return editor.EnterValueCharacter((char)('A' + (key - Key.A)));
            case Key.OemMinus or Key.Subtract:
                return editor.EnterValueCharacter('-');
            default:
                return false;
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
        DrawEdges(context, puzzle, viewport, nodes, radius, marks: false);
        DrawEdgePreview(context, puzzle, viewport, nodes);
        DrawNodes(context, puzzle, viewport, radius);

        // Marken (Kropki-Punkte, Pfeile) über den Knoten: Sie liegen auf dem Spalt zwischen zwei Kreisen und würden sonst
        // halb verdeckt.
        DrawEdges(context, puzzle, viewport, nodes, radius, marks: true);
        DrawSumTargets(context, puzzle, viewport, nodes, radius);
        DrawOrderBadges(context, puzzle, viewport, nodes, radius);
        DrawBranchBadge(context, puzzle, viewport, nodes, radius);
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
        var editor = _editor!;
        var shown = new List<(Group Group, List<Node> Members, Color Color, bool Selected)>();
        foreach (var group in puzzle.Groups)
        {
            var selected = editor.SelectedGroupId == group.Id;
            if (!selected && !editor.IsGroupVisible(group.Id))
            {
                continue;
            }

            var members = group.Nodes.Where(nodes.ContainsKey).Select(id => nodes[id]).ToList();
            if (members.Count > 0)
            {
                shown.Add((group, members, Color.Parse(GroupPalette.EffectiveColor(group.Color, group.Id)), selected));
            }
        }

        // Flächen nur für Gruppen mit eigener Farbe (die gewählte Gruppe kommt zuletzt obenauf); sonst würden sich
        // überlappende Gruppen (Zeilen, Spalten, Blöcke) zu einem unruhigen Flickenteppich mischen.
        foreach (var (group, members, color, selected) in shown)
        {
            if (!selected && GroupPalette.IsValidHex(group.Color))
            {
                context.DrawGeometry(new ImmutableSolidColorBrush(color, 0.12), null, CellGeometry(puzzle.Grid, viewport, members));
            }
        }

        // Umrisse aller sichtbaren Gruppen zusammengefasst (Nr. 61): Ein Stück, das nur auf dem Umriss einer Gruppe
        // liegt, in deren Farbe; geteilte Stücke neutral und umso kräftiger, je mehr Gruppen dort ihre Grenze haben
        // (beim klassischen Sudoku treten so die Blockgrenzen hervor). Farbige Linien übereinander wären nicht lesbar.
        var shared = GroupOutline.SharedBoundaries(puzzle.Grid, [.. shown.Select(s => s.Members.Select(n => new GridPoint(n.X, n.Y)))]);
        foreach (var bucket in shared.Where(x => x.Count > 1).GroupBy(x => Math.Min(x.Count, 6)))
        {
            context.DrawGeometry(null, new Pen(GroupBoundaryBrush, SharedBoundaryWidth(bucket.Key), lineCap: PenLineCap.Round), SegmentGeometry(viewport, bucket));
        }

        foreach (var bucket in shared.Where(x => x.Count == 1).GroupBy(x => x.Owner))
        {
            context.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(shown[bucket.Key].Color, 0.9), 2, lineCap: PenLineCap.Round), SegmentGeometry(viewport, bucket));
        }

        // Gruppen mit eigener Farbe zusätzlich mit farbigem Umriss, damit sie auch auf geteilten Grenzen erkennbar sind.
        foreach (var (group, members, color, selected) in shown)
        {
            if (!selected && GroupPalette.IsValidHex(group.Color))
            {
                context.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(color, 0.9), 2, lineCap: PenLineCap.Round), OutlineGeometry(puzzle.Grid, viewport, members));
            }
        }

        foreach (var (group, members, color, selected) in shown)
        {
            if (!selected && editor.Registry.TryGet(group.Rule, out var rule) && rule.Kind.IsOrdered())
            {
                DrawThermometer(context, puzzle.Grid, viewport, members, radius, color);
            }
        }

        // Erklär-Modus: Gruppe der verantwortlichen Regel grün hervorheben (auch wenn ihre Hervorhebung ausgeblendet ist).
        if (editor.Explanation?.Constraint is { Kind: ConstraintKind.Group, Id: var ruleGroupId })
        {
            foreach (var group in puzzle.Groups.Where(g => g.Id == ruleGroupId))
            {
                var members = group.Nodes.Where(nodes.ContainsKey).Select(id => nodes[id]).ToList();
                if (members.Count > 0)
                {
                    context.DrawGeometry(new ImmutableSolidColorBrush(ExplanationRuleColor, 0.12), null, CellGeometry(puzzle.Grid, viewport, members));
                    context.DrawGeometry(null, new Pen(ExplanationRuleBrush, 3.5, lineCap: PenLineCap.Round), OutlineGeometry(puzzle.Grid, viewport, members));
                }
            }
        }

        foreach (var (group, members, color, selected) in shown)
        {
            if (!selected)
            {
                continue;
            }

            context.DrawGeometry(new ImmutableSolidColorBrush(color, 0.30), null, CellGeometry(puzzle.Grid, viewport, members));
            context.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(color, 0.95), 3.5, lineCap: PenLineCap.Round), OutlineGeometry(puzzle.Grid, viewport, members));
            if (editor.Registry.TryGet(group.Rule, out var rule) && rule.Kind.IsOrdered())
            {
                DrawThermometer(context, puzzle.Grid, viewport, members, radius, color);
            }
        }
    }

    /// <summary>Linienstärke einer von <paramref name="count"/> Gruppen geteilten Grenze (2 → 1,85 px, 4 → 3,55 px).</summary>
    public static double SharedBoundaryWidth(int count) => Math.Min(4.5, 1 + (0.85 * (count - 1)));

    private static StreamGeometry CellGeometry(GridType grid, Viewport viewport, List<Node> members)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        foreach (var node in members.DistinctBy(n => (n.X, n.Y)))
        {
            AddPolygon(g, viewport, GridGeometry.GetCellVertices(grid, node.X, node.Y));
        }

        return geometry;
    }

    private static StreamGeometry OutlineGeometry(GridType grid, Viewport viewport, List<Node> members) =>
        LineGeometry(viewport, GroupOutline.BoundarySegments(grid, members.Select(n => new GridPoint(n.X, n.Y))));

    private static StreamGeometry SegmentGeometry(Viewport viewport, IEnumerable<SharedSegment> segments) =>
        LineGeometry(viewport, segments.Select(x => (x.A, x.B)));

    private static StreamGeometry LineGeometry(Viewport viewport, IEnumerable<(WorldPoint A, WorldPoint B)> segments)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        foreach (var (a, b) in segments)
        {
            g.BeginFigure(ToPoint(viewport.ToScreen(a)), false);
            g.LineTo(ToPoint(viewport.ToScreen(b)));
            g.EndFigure(false);
        }

        return geometry;
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

            context.DrawGeometry(null, new Pen(brush, Math.Max(4, radius * 0.95), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
        }

        // Kolben am Anfang: zeigt die Richtung (Werte steigen vom Kolben weg). Deutlich größer als der Knotenkreis, damit
        // er neben dem weißen Kreis als Fläche zu sehen ist.
        context.DrawEllipse(brush, null, points[0], radius * 1.3, radius * 1.3);
    }

    /// <summary>Kanten: erst nur die Linien (unter den Knoten), dann mit <paramref name="marks"/> nur die Marken (darüber).</summary>
    private void DrawEdges(DrawingContext context, Puzzle puzzle, Viewport viewport, Dictionary<int, Node> nodes, double radius, bool marks)
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
            var ruleOfStep = _editor.Explanation?.Constraint is { Kind: ConstraintKind.Edge, Id: var ruleEdgeId } && ruleEdgeId == edge.Id;
            var brush = ruleOfStep ? ExplanationRuleBrush : selected ? AccentBrush : EdgeBrush;
            if (marks)
            {
                if (edge.Rule is "Difference" or "Ratio" or "LessThan")
                {
                    // Nicht auf einen Knoten zwischen den Enden (Kante zum übernächsten Knoten): Die Marke liegt über den
                    // Knoten und verdeckte dort dessen Wert.
                    var (position, gap) = EditorGeometry.EdgeMarkPosition(
                        EditorGeometry.Center(puzzle.Grid, fromNode),
                        EditorGeometry.Center(puzzle.Grid, toNode),
                        EditorGeometry.NodeRadius(puzzle.Grid),
                        NodesBetween(puzzle.Grid, nodes, fromNode, toNode));
                    DrawEdgeMark(context, edge, ToPoint(viewport.ToScreen(position)), unit, radius, gap * viewport.Scale, brush, selected);
                }

                continue;
            }

            // „Ungleich“ hat keine Marke und kommt oft zu Dutzenden vor (Dreiecks-Sudoku: 168): dünner und heller, damit
            // Knoten und Werte lesbar bleiben.
            var plain = edge.Rule == "NotEqual" && !selected && !ruleOfStep;
            var pen = plain
                ? NotEqualPen
                : new Pen(brush, selected || ruleOfStep ? 3.2 : 1.8, known ? null : Dashed, PenLineCap.Round);
            if (length > 2 * radius)
            {
                context.DrawLine(pen, a + (unit * radius), b - (unit * radius));
            }
        }
    }

    /// <summary>Mittelpunkte der Knoten, deren Kreis die Strecke zwischen den beiden Knoten berühren kann (ohne die Enden).</summary>
    private static IEnumerable<WorldPoint> NodesBetween(GridType grid, Dictionary<int, Node> nodes, Node from, Node to)
    {
        var a = EditorGeometry.Center(grid, from);
        var b = EditorGeometry.Center(grid, to);
        var r = EditorGeometry.NodeRadius(grid);
        var (left, right) = (Math.Min(a.X, b.X) - r, Math.Max(a.X, b.X) + r);
        var (top, bottom) = (Math.Min(a.Y, b.Y) - r, Math.Max(a.Y, b.Y) + r);
        foreach (var node in nodes.Values)
        {
            var c = EditorGeometry.Center(grid, node);
            if (c.X >= left && c.X <= right && c.Y >= top && c.Y <= bottom && c.DistanceTo(a) > 1e-9 && c.DistanceTo(b) > 1e-9)
            {
                yield return c;
            }
        }
    }

    private static void DrawEdgeMark(DrawingContext context, Edge edge, Point middle, Vector unit, double nodeRadius, double gap, IBrush lineBrush, bool selected)
    {
        // Markengröße relativ zum Knotenradius, damit sie auch im engeren Dreiecksgitter zwischen die Kreise passt.
        var dot = Math.Clamp(nodeRadius * 0.32, 3.5, 14);
        if (selected && edge.Rule is "Difference" or "Ratio" or "LessThan")
        {
            // Hof um die Marke: Bei eng liegenden Knoten füllt die Marke den Spalt fast ganz aus, die blaue Linie allein
            // wäre dann kaum zu sehen.
            var halo = Math.Max(dot * 1.9, dot + 6);
            context.DrawEllipse(HaloBrush, null, middle, halo, halo);
        }

        var ring = selected ? 3 : 1.5;
        switch (edge.Rule)
        {
            case "Difference":
            {
                var k = SafeInt(edge.Params, "k");
                var atLeast = SafeString(edge.Params, "mode") == "atLeast";
                var label = k == 1 && !atLeast ? null : (atLeast ? "≥" : string.Empty) + (k?.ToString(CultureInfo.InvariantCulture) ?? "?");
                DrawDot(context, middle, dot, selected ? NodeSelectedFill : Brushes.White, lineBrush, ring, label, TextBrush);
                break;
            }

            case "Ratio":
            {
                var factor = SafeInt(edge.Params, "factor") ?? 2;
                DrawDot(context, middle, dot, Brushes.Black, lineBrush, ring, factor == 2 ? null : factor.ToString(CultureInfo.InvariantCulture), Brushes.White);
                break;
            }

            case "LessThan":
            {
                // Pfeil von „from“ (kleiner) nach „to“ (größer); höchstens so lang wie der Spalt zwischen den Kreisen
                // (darüber hinaus läge er auf den Werten), aber nie kleiner als 5 px.
                var size = Math.Max(5, Math.Min(Math.Clamp(nodeRadius * 0.36, 5, 16), gap * 0.42));
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

                // Weißer Rand, damit der Pfeil auch über einer Kreislinie klar zu sehen ist.
                context.DrawGeometry(lineBrush, new Pen(Brushes.White, 1, lineJoin: PenLineJoin.Round), geometry);
                break;
            }
        }
    }

    private static void DrawDot(DrawingContext context, Point center, double radius, IBrush fill, IBrush stroke, double strokeWidth, string? label, IBrush textBrush)
    {
        FormattedText? text = null;
        if (label is not null)
        {
            text = CreateText(label, Math.Max(9, radius * 1.3), textBrush, FontWeight.SemiBold);
            radius = Math.Max(radius, (Math.Max(text.Width, text.Height) / 2) + 3);
        }

        context.DrawEllipse(fill, new Pen(stroke, strokeWidth), center, radius, radius);
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
        var differencePen = new Pen(SolutionDifferenceBrush, 2.5);
        var visible = viewport.VisibleWorld;
        var margin = EditorGeometry.NodeRadius(puzzle.Grid);
        var edgeEnds = _editor!.SelectedEdgeId is int edgeId && puzzle.FindEdge(edgeId) is { } edge ? new[] { edge.From, edge.To } : [];
        var halo = radius + Math.Max(4, radius * 0.22);
        foreach (var node in puzzle.Nodes)
        {
            var world = EditorGeometry.Center(puzzle.Grid, node);
            if (world.X < visible.Left - margin || world.X > visible.Right + margin || world.Y < visible.Top - margin || world.Y > visible.Bottom + margin)
            {
                continue;
            }

            var center = ToPoint(viewport.ToScreen(world));
            var selected = _editor.IsNodeSelected(node.Id);
            var endOfSelectedEdge = edgeEnds.Contains(node.Id);
            if (endOfSelectedEdge)
            {
                // Endknoten der gewählten Kante hervorheben (Knoten-Ids stehen nicht auf der Zeichenfläche).
                context.DrawEllipse(HaloBrush, null, center, halo, halo);
            }

            if (_editor.Explanation is { } explanation)
            {
                DrawExplainedNode(context, puzzle, node, center, radius, explanation, selected || endOfSelectedEdge ? selectedPen : null);
                continue;
            }

            var differs = node.Given is null && _editor.IsSolutionDifference(node.Id);
            var fill = selected ? NodeSelectedFill : differs ? SolutionDifferenceFill : NodeFill;
            var pen = selected || endOfSelectedEdge ? selectedPen : differs ? differencePen : normalPen;
            context.DrawEllipse(fill, pen, center, radius, radius);
            if (radius < 5)
            {
                continue;
            }

            if (node.Given is int given)
            {
                var text = CreateText(ValueText.Format(given, puzzle, node), radius * 1.1, TextBrush, FontWeight.SemiBold);
                context.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
            }
            else if (_editor.SolutionValue(node.Id) is int solved)
            {
                // Lösungswert (nur Anzeige, nicht im Rätsel): andere Farbe und Strichstärke als ein Given.
                var text = CreateText(ValueText.Format(solved, puzzle, node), radius * 1.05, differs ? SolutionDifferenceBrush : SolutionBrush, FontWeight.Normal);
                context.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
            }
        }
    }

    /// <summary>
    /// Knoten im Erklär-Modus: Hervorhebung nach Schritt (gelb = geändert, orange = Verzweigung, grün = Regel), Givens
    /// schwarz, hergeleitete Werte blau, geratene orange; offene Knoten zeigen ihre Kandidaten als kleines Raster.
    /// </summary>
    private void DrawExplainedNode(DrawingContext context, Puzzle puzzle, Node node, Point center, double radius, ExplanationView explanation, IPen? selectionPen)
    {
        var highlight = explanation.GetHighlight(node.Id);
        var empty = explanation.EmptyNodeIds.Contains(node.Id);
        var fill = highlight switch
        {
            ExplanationHighlight.Current => ExplanationCurrentFill,
            ExplanationHighlight.CurrentBranch => ExplanationBranchFill,
            ExplanationHighlight.Rule => ExplanationRuleFill,
            _ => selectionPen is not null ? NodeSelectedFill : NodeFill,
        };
        var pen = selectionPen ?? (highlight switch
        {
            ExplanationHighlight.Current => ExplanationCurrentPen,
            ExplanationHighlight.CurrentBranch => ExplanationBranchPen,
            ExplanationHighlight.Rule => ExplanationRulePen,
            _ => NormalNodePen,
        });
        context.DrawEllipse(fill, pen, center, radius, radius);
        if (empty)
        {
            // Widerspruch: eigener roter Ring außen. Der Knoten, dem der letzte Schritt den letzten Kandidaten nimmt, ist
            // immer zugleich Knoten des aktuellen Schritts (gelb) – der rote Ring darf die Hervorhebung nicht ersetzen.
            var outer = radius + Math.Max(3, radius * 0.14);
            context.DrawEllipse(null, EmptyNodePen, center, outer, outer);
        }

        if (radius < 5)
        {
            return;
        }

        var domain = puzzle.GetEffectiveDomain(node);
        var hex = ValueText.IsHex(puzzle, node);
        if (node.Given is int given)
        {
            var text = CreateText(ValueText.Format(given, hex), radius * 1.1, TextBrush, FontWeight.SemiBold);
            context.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
        }
        else if (explanation.GetSetValue(node.Id) is int value)
        {
            var brush = explanation.IsGuessed(node.Id) ? GuessedValueBrush : SolutionBrush;
            var text = CreateText(ValueText.Format(value, hex), radius * 1.05, brush, FontWeight.Normal);
            context.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
        }
        else if (explanation.ShowCandidates)
        {
            DrawCandidates(context, center, radius, domain, hex, explanation.GetCandidates(node.Id), explanation.GetRemovedCandidates(node.Id));
        }
    }

    /// <summary>
    /// Kandidaten als kleines Raster im Kreis, jeder Wert an fester Stelle (9 Werte 3 × 3, 16 Werte 4 × 4, bei 0–15 als
    /// 0–F). Vom aktuellen Schritt gestrichene Werte erscheinen rot und durchgestrichen. Wird die Schrift zu klein, steht
    /// stattdessen nur der gestrichene Wert (falls der Schritt hier streicht) bzw. die Anzahl der Kandidaten im Knoten.
    /// </summary>
    private void DrawCandidates(DrawingContext context, Point center, double radius, ValueDomain domain, bool hex, IReadOnlyList<int> candidates, IReadOnlyList<int> removed)
    {
        var count = (long)domain.Max - domain.Min + 1;
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling(count / (double)columns);
        // Raster etwa so groß wie das einbeschriebene Quadrat (1,41 r), damit die Ziffern in den Ecken den Rand nicht berühren.
        var side = radius * (columns >= 4 ? 1.44 : 1.5);
        var cell = side / Math.Max(columns, rows);
        var size = Math.Round(cell * 2) / 2;
        if (count > 36 || size < 5)
        {
            if (removed.Count > 0)
            {
                // Zu klein für das Raster: Wenigstens die Wirkung des aktuellen Schritts (gestrichene Werte rot,
                // durchgestrichen) bleibt auf der Zeichenfläche sichtbar, statt nur im Panel zu stehen.
                DrawRemovedOnly(context, center, radius, hex, removed);
            }
            else if (radius >= 8 && candidates.Count > 0)
            {
                var text = CandidateText(FormattableString.Invariant($"({candidates.Count})"), Math.Max(4, radius * 0.6), CandidateBrush);
                context.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
            }

            return;
        }

        var left = center.X - (columns * cell / 2);
        var top = center.Y - (rows * cell / 2);
        void Draw(int value, IBrush brush, bool strike)
        {
            var index = value - domain.Min;
            var cellCenter = new Point(left + ((index % columns) + 0.5) * cell, top + ((index / columns) + 0.5) * cell);
            var text = CandidateText(ValueText.Format(value, hex), size, brush);
            context.DrawText(text, new Point(cellCenter.X - (text.Width / 2), cellCenter.Y - (text.Height / 2)));
            if (strike)
            {
                var half = Math.Max(text.Width * 0.7, cell * 0.36);
                context.DrawLine(new Pen(brush, Math.Max(1, size / 10), lineCap: PenLineCap.Round), new Point(cellCenter.X - half, cellCenter.Y + (half * 0.45)), new Point(cellCenter.X + half, cellCenter.Y - (half * 0.45)));
            }
        }

        foreach (var value in candidates)
        {
            Draw(value, CandidateBrush, strike: false);
        }

        foreach (var value in removed)
        {
            Draw(value, RemovedCandidateBrush, strike: true);
        }
    }

    /// <summary>Kleine Knoten: nur die vom aktuellen Schritt gestrichenen Werte, mittig, rot und durchgestrichen.</summary>
    private void DrawRemovedOnly(DrawingContext context, Point center, double radius, bool hex, IReadOnlyList<int> removed)
    {
        var values = removed.Select(v => ValueText.Format(v, hex)).ToList();
        var label = string.Join(values.Any(v => v.Length > 1) ? "," : string.Empty, values);
        var size = Math.Round(radius * 1.1 * 2) / 2;
        var text = CandidateText(label, size, RemovedCandidateBrush);
        if (text.Width > radius * 1.7)
        {
            size = Math.Max(4, Math.Round(size * radius * 1.7 / text.Width * 2) / 2);
            text = CandidateText(label, size, RemovedCandidateBrush);
        }

        context.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));
        var half = Math.Max(text.Width / 2 + 1, radius * 0.45);
        context.DrawLine(new Pen(RemovedCandidateBrush, Math.Max(1, size / 9), lineCap: PenLineCap.Round), new Point(center.X - half, center.Y + (half * 0.45)), new Point(center.X + half, center.Y - (half * 0.45)));
    }

    private FormattedText CandidateText(string text, double size, IBrush brush)
    {
        var key = (text, size, brush);
        if (!_candidateTexts.TryGetValue(key, out var formatted))
        {
            if (_candidateTexts.Count > 4000)
            {
                _candidateTexts.Clear();
            }

            formatted = CreateText(text, size, brush, FontWeight.Normal);
            _candidateTexts[key] = formatted;
        }

        return formatted;
    }

    /// <summary>Verzweigung: orange Marke „Raten“ (bei kleinen Knoten „?“) am geratenen Knoten.</summary>
    private void DrawBranchBadge(DrawingContext context, Puzzle puzzle, Viewport viewport, Dictionary<int, Node> nodes, double radius)
    {
        if (_editor!.Explanation is not { IsBranch: true, CurrentStep.NodeId: int nodeId } || !nodes.TryGetValue(nodeId, out var node) || radius < 5)
        {
            return;
        }

        var center = ToPoint(viewport.ToScreen(EditorGeometry.Center(puzzle.Grid, node)));
        var badge = Math.Max(8, radius * 0.46);
        var text = CreateText(radius >= 16 ? "Raten" : "?", badge * 1.1, Brushes.White, FontWeight.Bold);
        var width = Math.Max(badge * 2, text.Width + (badge * 1.1));
        var position = new Point(center.X + (radius * 0.55), center.Y - (radius * 0.95));
        var rect = new Rect(position.X - (width / 2), position.Y - badge, width, badge * 2);
        context.DrawRectangle(ExplanationBranchBrush, new Pen(Brushes.White, 1.5), new RoundedRect(rect, badge));
        context.DrawText(text, new Point(position.X - (text.Width / 2), position.Y - (text.Height / 2)));
    }

    /// <summary>
    /// Zielsumme jeder sichtbaren <c>Sum</c>-Gruppe (Killer-Käfig) klein in der Zelle, die in Leserichtung zuerst kommt
    /// (oberste Zeile, darin ganz links): auf dem Quadratgitter in der linken oberen Ecke, sonst zur oberen linken Ecke
    /// der Zelle hin. „?“, wenn die Zielsumme fehlt oder ungültig ist.
    /// </summary>
    private void DrawSumTargets(DrawingContext context, Puzzle puzzle, Viewport viewport, Dictionary<int, Node> nodes, double radius)
    {
        if (radius < 6)
        {
            return;
        }

        var editor = _editor!;
        var size = Math.Clamp(viewport.Scale * (puzzle.Grid == GridType.Triangle ? 0.13 : 0.2), 8, 18);
        foreach (var group in puzzle.Groups)
        {
            if (group.Rule != "Sum" || (!editor.IsGroupVisible(group.Id) && editor.SelectedGroupId != group.Id))
            {
                continue;
            }

            var members = group.Nodes.Where(nodes.ContainsKey).Select(id => nodes[id]).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            var anchor = EditorGeometry.RowWiseOrder(puzzle.Grid, members).First();
            var target = SafeInt(group.Params, "target");
            var text = CreateText(target?.ToString(CultureInfo.InvariantCulture) ?? "?", size, SumTextBrush, FontWeight.SemiBold);
            var center = EditorGeometry.Center(puzzle.Grid, anchor);
            var vertices = GridGeometry.GetCellVertices(puzzle.Grid, anchor.X, anchor.Y);

            // Ecke der Zelle oben links (kleinste Summe x + y), Text etwas nach innen gerückt.
            var corner = vertices.MinBy(v => v.X + v.Y);
            var screenCorner = ToPoint(viewport.ToScreen(corner));
            var screenCenter = ToPoint(viewport.ToScreen(center));
            Point topLeft;
            if (puzzle.Grid == GridType.Square)
            {
                topLeft = new Point(screenCorner.X + 2, screenCorner.Y + 1);
            }
            else if (puzzle.Grid == GridType.Hex)
            {
                // Sechseck: Der Knotenkreis füllt die Zelle fast aus (Radius 0,38 bei Innenkreis 0,5), 42 % zur Mitte lägen
                // im Kreis direkt neben dem Wert. Daher so weit zur Ecke, dass das Etikett den Kreis gerade nicht
                // berührt, höchstens bis zur Ecke.
                var toCorner = screenCorner - screenCenter;
                var cornerDistance = Math.Sqrt((toCorner.X * toCorner.X) + (toCorner.Y * toCorner.Y));
                var unit = toCorner / cornerDistance;
                var support = ((text.Width / 2) + 1.5) * Math.Abs(unit.X) + (text.Height / 2 * Math.Abs(unit.Y));
                var distance = Math.Min(cornerDistance, radius + 1.5 + support);
                var at = screenCenter + (unit * distance);
                topLeft = new Point(at.X - (text.Width / 2), at.Y - (text.Height / 2));
            }
            else
            {
                var inner = screenCorner + ((screenCenter - screenCorner) * 0.42);
                topLeft = new Point(inner.X - (text.Width / 2), inner.Y - (text.Height / 2));
            }

            var box = new Rect(topLeft.X - 1.5, topLeft.Y, text.Width + 3, text.Height);
            context.DrawRectangle(SumLabelBackground, null, new RoundedRect(box, 2));
            context.DrawText(text, topLeft);
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
