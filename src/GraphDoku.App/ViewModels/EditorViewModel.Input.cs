using System.Globalization;
using GraphDoku.App.Editing;
using GraphDoku.App.Editing.Commands;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.App.ViewModels;

/// <summary>Zeiger- und Tastatureingaben der Zeichenfläche (Bildschirmkoordinaten in Pixeln).</summary>
public sealed partial class EditorViewModel
{
    /// <summary>Ab dieser Mausbewegung (Pixel) wird aus einem Klick ein Ziehen.</summary>
    public const double DragThreshold = 4;

    /// <summary>Fangradius für Kanten in Pixeln.</summary>
    public const double EdgeHitTolerance = 6;

    private Gesture _gesture;
    private ScreenPoint _pressPoint;
    private ScreenPoint _lastPoint;
    private EditorModifiers _pressModifiers;
    private int? _pressNodeId;
    private bool _pressedSelectedNode;
    private object? _gestureKey;
    private Dictionary<int, GridPoint> _moveOrigins = [];
    private GridPoint _moveAnchorOrigin;
    private (double X, double Y) _grabOffset;
    private (int Dx, int Dy) _appliedDelta;
    private GridPoint? _lastPlacedPoint;
    private bool _spaceHeld;
    private bool _refitAfterGesture;

    // Laufende Eingabe eines (mehrstelligen) Givens.
    private string _entryBuffer = string.Empty;
    private object? _entryKey;
    private IEditCommand? _entryMarker;
    private bool _entryActive;

    private enum Gesture
    {
        None,
        Panning,
        PendingMove,
        MovingNodes,
        PendingRubberBand,
        RubberBand,
        PlacingNodes,
        DrawingEdge,
    }

    /// <summary>Aktueller Auswahlrahmen in Bildschirmkoordinaten (null = keiner).</summary>
    public Bounds? RubberBand { get; private set; }

    /// <summary>Vorschau beim Kantenziehen: Startknoten und aktuelle Zeigerposition (Welt).</summary>
    public (int FromNodeId, WorldPoint To)? EdgePreview { get; private set; }

    /// <summary>true, solange eine Ziehgeste läuft.</summary>
    public bool IsGestureActive => _gesture != Gesture.None;

    /// <summary>true, wenn die Leertaste gehalten wird (Ziehen verschiebt dann die Ansicht).</summary>
    public bool IsPanning => _gesture == Gesture.Panning;

    /// <summary>
    /// Neue Größe der Zeichenfläche. War die Ansicht eingepasst (seither nicht gezoomt oder verschoben), wird neu
    /// eingepasst – z. B. wenn die Lösen-Leiste eine Meldung bekommt und die Zeichenfläche dadurch niedriger wird.
    /// Während einer Ziehgeste bleibt die Ansicht stehen (sonst landen Knoten nicht unter dem Mauszeiger); das
    /// Einpassen folgt dann am Ende der Geste, sofern die Ansicht weiterhin eingepasst ist.
    /// </summary>
    public void SetViewSize(double width, double height)
    {
        var wasFitted = IsViewFitted;
        var changed = width != Viewport.Width || height != Viewport.Height;
        Viewport.SetSize(width, height);
        if (!wasFitted || !changed || !Viewport.HasSize)
        {
            return;
        }

        if (_gesture != Gesture.None)
        {
            _refitAfterGesture = true;
        }
        else
        {
            FitView();
        }
    }

    /// <summary>Holt ein während der Geste zurückgestelltes Einpassen nach (siehe <see cref="SetViewSize"/>).</summary>
    private void RefitIfDeferred()
    {
        if (_refitAfterGesture)
        {
            _refitAfterGesture = false;
            if (IsViewFitted)
            {
                FitView();
            }
        }
    }

    public void SetSpaceHeld(bool held) => _spaceHeld = held;

    public void PointerWheel(ScreenPoint position, double delta) => Viewport.ZoomAt(position, Math.Pow(1.15, delta));

    public void PointerPressed(ScreenPoint position, PointerButton button, EditorModifiers modifiers = EditorModifiers.None)
    {
        if (_gesture != Gesture.None)
        {
            return;
        }

        _pressPoint = position;
        _lastPoint = position;
        _pressModifiers = modifiers;
        if (button != PointerButton.Left || Tool == EditorTool.Pan || _spaceHeld)
        {
            _gesture = Gesture.Panning;
            return;
        }

        var world = Viewport.ToWorld(position);
        var node = HitNode(world);
        switch (Tool)
        {
            case EditorTool.Select:
                PressSelect(node, world, modifiers);
                break;
            case EditorTool.PlaceNode:
                if (node is not null)
                {
                    SelectNodes([node.Id]);
                    break;
                }

                _gesture = Gesture.PlacingNodes;
                _gestureKey = new object();
                _lastPlacedPoint = null;
                PlaceNodeAt(GridGeometry.ToNearestGridPoint(Puzzle.Grid, world));
                break;
            case EditorTool.DrawEdge:
                if (node is not null)
                {
                    _gesture = Gesture.DrawingEdge;
                    EdgePreview = (node.Id, world);
                    RaiseVisualChanged();
                }

                break;
        }
    }

    public void PointerMoved(ScreenPoint position, EditorModifiers modifiers = EditorModifiers.None)
    {
        var world = Viewport.ToWorld(position);
        switch (_gesture)
        {
            case Gesture.Panning:
                Viewport.PanBy(position.X - _lastPoint.X, position.Y - _lastPoint.Y);
                break;
            case Gesture.PendingMove when position.DistanceTo(_pressPoint) > DragThreshold:
                BeginMove();
                MoveTo(world);
                break;
            case Gesture.MovingNodes:
                MoveTo(world);
                break;
            case Gesture.PendingRubberBand when position.DistanceTo(_pressPoint) > DragThreshold:
                _gesture = Gesture.RubberBand;
                UpdateRubberBand(position);
                break;
            case Gesture.RubberBand:
                UpdateRubberBand(position);
                break;
            case Gesture.PlacingNodes:
                PlaceNodesAlong(Viewport.ToWorld(_lastPoint), world);
                break;
            case Gesture.DrawingEdge when EdgePreview is { } preview:
                EdgePreview = (preview.FromNodeId, world);
                RaiseVisualChanged();
                break;
        }

        _lastPoint = position;
    }

    public void PointerReleased(ScreenPoint position, PointerButton button = PointerButton.Left, EditorModifiers modifiers = EditorModifiers.None)
    {
        var gesture = _gesture;
        _gesture = Gesture.None;
        _gestureKey = null;
        switch (gesture)
        {
            case Gesture.PendingMove:
                // Klick auf einen bereits gewählten Knoten einer Mehrfachauswahl: nur diesen wählen.
                if (_pressedSelectedNode && _pressNodeId is int id && _selectedNodes.Count > 1)
                {
                    SelectNodes([id]);
                }

                break;
            case Gesture.MovingNodes:
                if (_appliedDelta != (0, 0))
                {
                    StatusMessage = $"{_moveOrigins.Count} Knoten verschoben.";
                }

                break;
            case Gesture.PendingRubberBand:
                if (!_pressModifiers.HasFlag(EditorModifiers.Shift))
                {
                    ClearSelection();
                }

                break;
            case Gesture.RubberBand:
                FinishRubberBand(position);
                break;
            case Gesture.DrawingEdge:
                FinishEdge(Viewport.ToWorld(position));
                break;
        }

        _pressNodeId = null;
        RefitIfDeferred();
    }

    /// <summary>Bricht eine laufende Geste ab (Auswahlrahmen, Kantenvorschau). Bereits ausgeführte Schritte bleiben im Verlauf.</summary>
    public void CancelGesture()
    {
        _gesture = Gesture.None;
        _gestureKey = null;
        _pressNodeId = null;
        if (RubberBand is not null || EdgePreview is not null)
        {
            RubberBand = null;
            EdgePreview = null;
            RaiseVisualChanged();
        }

        RefitIfDeferred();
    }

    /// <summary>Escape: laufende Geste abbrechen, sonst Auswahl aufheben.</summary>
    public void Escape()
    {
        if (_gesture != Gesture.None)
        {
            CancelGesture();
        }
        else
        {
            ClearSelection();
        }
    }

    /// <summary>
    /// Zeichen für ein Given in die ausgewählten Knoten. Dezimal: Ziffern (und „-“ am Anfang); aufeinanderfolgende
    /// Ziffern bilden eine mehrstellige Zahl, solange sie im Wertebereich liegt (z. B. „1“, „6“ → 16), sonst beginnt
    /// eine neue Zahl. Hexadoku (<see cref="ValueText.IsHex"/>): jedes Zeichen 0–9/A–F ist ein vollständiger Wert (außerhalb
    /// eines eigenen Knotenbereichs abgelehnt). Liefert false, wenn das
    /// Zeichen nicht zuständig ist (keine Knoten gewählt oder kein Wertezeichen).
    /// </summary>
    public bool EnterValueCharacter(char character)
    {
        var nodes = _selectedNodes.Select(Puzzle.FindNode).OfType<Node>().ToList();
        if (nodes.Count == 0)
        {
            return false;
        }

        var domains = nodes.Select(Puzzle.GetEffectiveDomain).Distinct().ToList();
        if (nodes.All(n => ValueText.IsHex(Puzzle, n)))
        {
            if (ValueText.ParseHexDigit(character) is not int hexValue)
            {
                return false;
            }

            EndValueEntry();
            if (!domains.All(d => d.Contains(hexValue)))
            {
                // Eigener Bereich im Hexadoku (z. B. 8–15): wie bei der Dezimaleingabe ablehnen und melden.
                StatusMessage = $"Der Wert {ValueText.Format(hexValue, hex: true)} liegt außerhalb des Wertebereichs {string.Join(", ", domains)}.";
                return true;
            }

            ApplyGiven(nodes, hexValue, new object());
            return true;
        }

        if (character is not ((>= '0' and <= '9') or '-'))
        {
            return false;
        }

        var continuing = _entryActive && ReferenceEquals(_history.LastCommand, _entryMarker);
        if (continuing && TryEnter(nodes, domains, _entryBuffer + character, _entryKey!))
        {
            return true;
        }

        EndValueEntry();
        if (TryEnter(nodes, domains, character.ToString(), new object()))
        {
            return true;
        }

        StatusMessage = character == '-'
            ? "Der Wertebereich enthält keine negativen Werte."
            : $"Der Wert {character} liegt außerhalb des Wertebereichs {string.Join(", ", domains)}.";
        return true;
    }

    /// <summary>Beendet eine laufende mehrstellige Eingabe (nächste Ziffer beginnt eine neue Zahl).</summary>
    public void EndValueEntry()
    {
        _entryActive = false;
        _entryBuffer = string.Empty;
        _entryKey = null;
        _entryMarker = null;
    }

    private bool TryEnter(List<Node> nodes, List<ValueDomain> domains, string text, object key)
    {
        if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) && domains.All(d => d.Contains(value)))
        {
            ApplyGiven(nodes, value, key);
            StartEntry(text, key);
            return true;
        }

        if (IsPrefixOfDomainValue(text, domains))
        {
            StartEntry(text, key);
            StatusMessage = $"Eingabe: {text} …";
            return true;
        }

        return false;
    }

    private void StartEntry(string text, object key)
    {
        _entryActive = true;
        _entryBuffer = text;
        _entryKey = key;
        _entryMarker = _history.LastCommand;
    }

    private static bool IsPrefixOfDomainValue(string text, List<ValueDomain> domains) =>
        domains[0].Values.Any(v =>
        {
            var s = v.ToString(CultureInfo.InvariantCulture);
            return s.Length > text.Length && s.StartsWith(text, StringComparison.Ordinal) && domains.All(d => d.Contains(v));
        });

    private void ApplyGiven(List<Node> nodes, int value, object key)
    {
        var changes = nodes.Where(n => n.Given != value).ToDictionary(n => n.Id, n => (n.Given, (int?)value));
        if (changes.Count > 0)
        {
            _history.Execute(new SetGivensCommand(changes, key));
        }

        StatusMessage = nodes.Count == 1
            ? $"Given {ValueText.Format(value, Puzzle, nodes[0])} in Knoten {nodes[0].Id}."
            : $"Given {value} in {nodes.Count} Knoten.";
    }

    private void PressSelect(Node? node, WorldPoint world, EditorModifiers modifiers)
    {
        var shift = modifiers.HasFlag(EditorModifiers.Shift);
        // Zellen füllen die Fläche lückenlos (Paket 7): Kanten sind dann nur über ihre Linienstücke am Zellrand und ihre
        // Marke zu treffen und haben dort Vorrang vor der Zelle (Nr. 154).
        if (NodeShape == NodeShape.Cell && !shift && HitEdge(world) is { } cellEdge)
        {
            SelectEdge(cellEdge.Id);
            return;
        }

        if (node is not null)
        {
            if (shift)
            {
                ToggleNodeSelection(node.Id);
                return;
            }

            _pressedSelectedNode = IsNodeSelected(node.Id);
            if (!_pressedSelectedNode)
            {
                SelectNodes([node.Id]);
            }

            _pressNodeId = node.Id;
            _gesture = Gesture.PendingMove;
            return;
        }

        if (HitEdge(world) is { } edge && !shift)
        {
            SelectEdge(edge.Id);
            return;
        }

        _gesture = Gesture.PendingRubberBand;
    }

    private void BeginMove()
    {
        _gesture = Gesture.MovingNodes;
        _gestureKey = new object();
        _moveOrigins = _selectedNodes
            .Select(Puzzle.FindNode)
            .OfType<Node>()
            .ToDictionary(n => n.Id, n => new GridPoint(n.X, n.Y));
        var anchor = _pressNodeId is int id ? Puzzle.FindNode(id) : null;
        _moveAnchorOrigin = anchor is null ? default : new GridPoint(anchor.X, anchor.Y);
        _appliedDelta = (0, 0);

        // Greifpunkt relativ zum Mittelpunkt: Eingerastet wird der Mittelpunkt, nicht der Zeiger. Sonst sprünge eine am
        // Rand gepackte Zelle (Zelldarstellung, Nr. 155) schon nach wenigen Pixeln in die Nachbarzelle.
        var grabbed = Viewport.ToWorld(_pressPoint);
        var center = anchor is null ? grabbed : EditorGeometry.Center(Puzzle.Grid, anchor);
        _grabOffset = (grabbed.X - center.X, grabbed.Y - center.Y);
    }

    private void MoveTo(WorldPoint world)
    {
        var target = EditorGeometry.SnapForMove(Puzzle.Grid, new WorldPoint(world.X - _grabOffset.X, world.Y - _grabOffset.Y), _moveAnchorOrigin);
        var delta = (target.X - _moveAnchorOrigin.X, target.Y - _moveAnchorOrigin.Y);
        if (delta == _appliedDelta || _moveOrigins.Count == 0)
        {
            return;
        }

        // Nicht auf Gitterpunkte schieben, die von anderen Knoten belegt sind.
        var occupied = Puzzle.Nodes.Where(n => !_moveOrigins.ContainsKey(n.Id)).Select(n => new GridPoint(n.X, n.Y)).ToHashSet();
        var moves = _moveOrigins.ToDictionary(
            kv => kv.Key,
            kv => (From: new GridPoint(kv.Value.X + _appliedDelta.Dx, kv.Value.Y + _appliedDelta.Dy), To: new GridPoint(kv.Value.X + delta.Item1, kv.Value.Y + delta.Item2)));
        if (moves.Values.Any(m => occupied.Contains(m.To)))
        {
            return;
        }

        _appliedDelta = delta;
        _history.Execute(new MoveNodesCommand(moves, _gestureKey));
    }

    /// <summary>Setzt Knoten auf alle Gitterpunkte entlang der Mausbewegung (auch bei schneller Bewegung lückenlos).</summary>
    private void PlaceNodesAlong(WorldPoint from, WorldPoint to)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to) / 0.2));
        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var point = new WorldPoint(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));
            PlaceNodeAt(GridGeometry.ToNearestGridPoint(Puzzle.Grid, point));
        }
    }

    private void PlaceNodeAt(GridPoint point)
    {
        if (_lastPlacedPoint == point)
        {
            return;
        }

        _lastPlacedPoint = point;
        if (Puzzle.Nodes.Any(n => n.X == point.X && n.Y == point.Y))
        {
            return;
        }

        var node = new Node(Puzzle.NextNodeId(), point.X, point.Y);
        _history.Execute(new AddNodesCommand([node], _gestureKey));
        StatusMessage = $"Knoten {node.Id} an ({point.X}, {point.Y}) gesetzt.";
        SelectNodes([node.Id]);
    }

    private void UpdateRubberBand(ScreenPoint position)
    {
        RubberBand = Bounds.FromCorners(_pressPoint.X, _pressPoint.Y, position.X, position.Y);
        RaiseVisualChanged();
    }

    private void FinishRubberBand(ScreenPoint position)
    {
        var rect = Bounds.FromCorners(_pressPoint.X, _pressPoint.Y, position.X, position.Y);
        RubberBand = null;

        var inside = Puzzle.Nodes
            .Where(n =>
            {
                var screen = Viewport.ToScreen(EditorGeometry.Center(Puzzle.Grid, n));
                return rect.Contains(screen.X, screen.Y);
            })
            .ToList();
        var ordered = EditorGeometry.RowWiseOrder(Puzzle.Grid, inside).Select(n => n.Id).ToList();
        if (_pressModifiers.HasFlag(EditorModifiers.Shift))
        {
            AddToSelection(ordered);
        }
        else
        {
            SelectNodes(ordered);
        }

        StatusMessage = $"{_selectedNodes.Count} Knoten ausgewählt.";
    }

    private void FinishEdge(WorldPoint world)
    {
        var preview = EdgePreview;
        EdgePreview = null;
        RaiseVisualChanged();
        if (preview is not { } p || HitNode(world) is not { } target || target.Id == p.FromNodeId)
        {
            return;
        }

        if (NewEdgeRule is not { } rule)
        {
            StatusMessage = "Keine Kantenregel gewählt.";
            return;
        }

        var directed = rule.Kind.IsDirected();
        var duplicate = Puzzle.Edges.Any(e => e.Rule == rule.TypeName
            && ((e.From == p.FromNodeId && e.To == target.Id) || (!directed && e.From == target.Id && e.To == p.FromNodeId)));
        if (duplicate)
        {
            StatusMessage = $"Zwischen Knoten {p.FromNodeId} und {target.Id} gibt es bereits eine Kante „{rule.DisplayName}“.";
            return;
        }

        var edge = new Edge(Puzzle.NextEdgeId(), p.FromNodeId, target.Id, rule.TypeName, rule.CreateDefaultParams());
        Execute(new AddEdgeCommand(edge));
        SelectEdge(edge.Id);
        StatusMessage = $"Kante {edge.Id} ({rule.DisplayName}) von Knoten {edge.From} nach {edge.To} erstellt.";
    }

    /// <summary>
    /// Oberster Knoten, der den Weltpunkt enthält: bei Kreisen im Knotenkreis, bei Zellen (<see cref="NodeShape"/>) im
    /// Zellumriss.
    /// </summary>
    public Node? HitNode(WorldPoint world)
    {
        for (var i = Puzzle.Nodes.Count - 1; i >= 0; i--)
        {
            var node = Puzzle.Nodes[i];
            if (EditorGeometry.Contains(Puzzle.Grid, NodeShape, node, world))
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>
    /// Kante in der Nähe des Weltpunkts (Toleranz in Pixeln). Bei Kreisen zählt die ganze Strecke zwischen den
    /// Mittelpunkten; bei Zellen nur die sichtbaren Stege über den Zellrändern (<see cref="EditorGeometry.CellEdgeStubs"/>)
    /// und die Marke, damit ein Klick ins Innere einer Zelle die Zelle trifft.
    /// </summary>
    public Edge? HitEdge(WorldPoint world)
    {
        var tolerance = EdgeHitTolerance / Viewport.Scale;
        var cells = NodeShape == NodeShape.Cell;
        var clear = EditorGeometry.ClearRadius(Puzzle.Grid, NodeShape);
        var markRadius = Math.Max(tolerance, EditorGeometry.ContentRadius(Puzzle.Grid, NodeShape) * 0.32);
        Edge? best = null;
        var bestDistance = double.MaxValue;
        foreach (var edge in Puzzle.Edges)
        {
            if (Puzzle.FindNode(edge.From) is not { } a || Puzzle.FindNode(edge.To) is not { } b)
            {
                continue;
            }

            var from = EditorGeometry.Center(Puzzle.Grid, a);
            var to = EditorGeometry.Center(Puzzle.Grid, b);
            double distance;
            if (!cells)
            {
                distance = EditorGeometry.DistanceToSegment(world, from, to);
            }
            else
            {
                var others = NodesNear(from, to, clear);
                distance = EditorGeometry.CellEdgeStubs(Puzzle.Grid, from, to, others)
                    .Select(s => EditorGeometry.DistanceToSegment(world, s.From, s.To))
                    .DefaultIfEmpty(double.MaxValue)
                    .Min();
                if (EdgeHasMark(edge.Rule))
                {
                    var mark = EditorGeometry.EdgeMarkPosition(from, to, clear, others).Position;
                    distance = Math.Min(distance, Math.Max(0, mark.DistanceTo(world) - markRadius + tolerance));
                }
            }

            if (distance <= tolerance && distance < bestDistance)
            {
                best = edge;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Regeln mit Marke auf der Kante (Kropki-Punkte, Pfeil); „Ungleich“ ist nur eine Linie.</summary>
    public static bool EdgeHasMark(string rule) => rule is "Difference" or "Ratio" or "LessThan";

    /// <summary>
    /// Mittelpunkte der Knoten, deren Freihaltekreis (Radius <paramref name="radius"/>) die Strecke a–b berühren kann,
    /// ohne die Enden selbst.
    /// </summary>
    public IEnumerable<WorldPoint> NodesNear(WorldPoint a, WorldPoint b, double radius)
    {
        var (left, right) = (Math.Min(a.X, b.X) - radius, Math.Max(a.X, b.X) + radius);
        var (top, bottom) = (Math.Min(a.Y, b.Y) - radius, Math.Max(a.Y, b.Y) + radius);
        foreach (var node in Puzzle.Nodes)
        {
            var c = EditorGeometry.Center(Puzzle.Grid, node);
            if (c.X >= left && c.X <= right && c.Y >= top && c.Y <= bottom && c.DistanceTo(a) > 1e-9 && c.DistanceTo(b) > 1e-9)
            {
                yield return c;
            }
        }
    }
}
