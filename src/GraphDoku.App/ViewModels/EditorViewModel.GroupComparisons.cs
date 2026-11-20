using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GraphDoku.App.Editing;
using GraphDoku.App.Editing.Commands;
using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;
using GraphDoku.Core.Rules;

namespace GraphDoku.App.ViewModels;

/// <summary>
/// Gruppenvergleiche im Editor: Auswahl (schließt Knoten, Kante und Gruppe aus), Erstellen aus dem Gruppen-Panel,
/// Liste „Gruppenvergleiche“ und die Verbindung auf der Zeichenfläche (Sichtbarkeit, Lage, Treffertest).
/// </summary>
public sealed partial class EditorViewModel
{
    /// <summary>Ausgewählter Gruppenvergleich (schließt Knoten-, Kanten- und Gruppenauswahl aus).</summary>
    [ObservableProperty]
    public partial int? SelectedGroupComparisonId { get; private set; }

    /// <summary>Freiraum um ein Operator-Etikett in Pixeln, den andere Etiketten nicht überdecken (zwischen zweien also doppelt).</summary>
    private const double BadgeGap = 2;

    /// <summary>Liste „Gruppenvergleiche“ unter der Gruppenliste (in Modellreihenfolge, Einträge bleiben erhalten).</summary>
    public ObservableCollection<GroupComparisonItemViewModel> GroupComparisons { get; } = [];

    /// <summary>In der Liste markierter Vergleich (gleichbedeutend mit <see cref="SelectedGroupComparisonId"/>).</summary>
    [ObservableProperty]
    public partial GroupComparisonItemViewModel? SelectedGroupComparisonItem { get; set; }

    /// <summary>true, sobald das Rätsel Gruppenvergleiche hat (erst dann erscheint die Liste).</summary>
    [ObservableProperty]
    public partial bool HasGroupComparisons { get; private set; }

    public void SelectGroupComparison(int id)
    {
        ClearSelectionCore();
        SelectedGroupComparisonId = id;
        OnSelectionChanged();
    }

    /// <summary>
    /// Erstellt den Vergleich „Summe von Gruppe <paramref name="leftGroupId"/> OP Summe von Gruppe
    /// <paramref name="rightGroupId"/>“ als Undo-Schritt und wählt ihn aus. <c>null</c> (mit Statusmeldung), wenn das
    /// nicht geht.
    /// </summary>
    public GroupComparison? CreateGroupComparison(int leftGroupId, int rightGroupId, ComparisonOperator op)
    {
        if (leftGroupId == rightGroupId)
        {
            StatusMessage = "Eine Gruppe lässt sich nicht mit sich selbst vergleichen.";
            return null;
        }

        if (Puzzle.FindGroup(leftGroupId) is null || Puzzle.FindGroup(rightGroupId) is null)
        {
            StatusMessage = $"Gruppe {(Puzzle.FindGroup(leftGroupId) is null ? leftGroupId : rightGroupId)} gibt es nicht.";
            return null;
        }

        if (!Enum.IsDefined(op))
        {
            StatusMessage = "Unbekannter Vergleichsoperator.";
            return null;
        }

        // Wie die Prüfung: Heben sich alle Knoten auf (dieselben Knoten in beiden Gruppen), bleibt nichts zu vergleichen.
        if (GroupComparisonLogic.Resolve(Puzzle, new GroupComparison(0, leftGroupId, rightGroupId, op)) is { Terms.Count: 0 })
        {
            StatusMessage = $"Gruppe {leftGroupId} und Gruppe {rightGroupId} haben dieselben Knoten; ihre Summen sind immer gleich und lassen sich nicht vergleichen.";
            return null;
        }

        var ids = Puzzle.GroupComparisons.Select(c => c.Id);
        if (NextFreeId(ids) is not int id)
        {
            StatusMessage = NoFreeIdMessage("Gruppenvergleichs", ids);
            return null;
        }

        var comparison = new GroupComparison(id, leftGroupId, rightGroupId, op);
        Execute(new AddGroupComparisonCommand(comparison));
        SelectGroupComparison(id);
        StatusMessage = $"Gruppenvergleich {id} erstellt: {GroupComparisonTexts.Meaning(Puzzle, comparison)}.";
        return Puzzle.FindGroupComparison(id);
    }

    /// <summary>
    /// Wird die Verbindung des Vergleichs gezeichnet? Wenn beide Gruppen eingeblendet sind, der Vergleich ausgewählt
    /// ist oder er die Regel des aktuellen Erklärschritts ist (dann auch bei ausgeblendeten Gruppen).
    /// </summary>
    public bool IsGroupComparisonShown(GroupComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return SelectedGroupComparisonId == comparison.Id
            || IsExplainedGroupComparison(comparison.Id)
            || (IsGroupVisible(comparison.Left) && IsGroupVisible(comparison.Right));
    }

    /// <summary>true, wenn der Vergleich die verantwortliche Regel des aktuellen Erklärschritts ist.</summary>
    public bool IsExplainedGroupComparison(int id) =>
        Explanation?.Constraint is { Kind: ConstraintKind.GroupComparison, Id: var explained } && explained == id;

    /// <summary>
    /// Enden der Verbindung (Schwerpunkte der Knoten beider Gruppen, Welt). <c>null</c>, wenn eine Gruppe fehlt oder
    /// keinen vorhandenen Knoten hat (Datei ohne inhaltliche Prüfung, Nr. 57). Bei mehrfach vergebenen Ids gilt wie beim
    /// Lösen die erste Gruppe bzw. der erste Knoten.
    /// </summary>
    public (WorldPoint From, WorldPoint To)? GroupComparisonConnector(GroupComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        WorldPoint? AnchorOf(int groupId) => Puzzle.FindGroup(groupId) is { } group
            ? GroupComparisonGeometry.Anchor(Puzzle.Grid, group.Nodes.Select(Puzzle.FindNode).OfType<Node>())
            : null;

        return AnchorOf(comparison.Left) is { } from && AnchorOf(comparison.Right) is { } to ? (from, to) : null;
    }

    /// <summary>
    /// Alle gezeigten Gruppenvergleiche mit Verbindung und Etikett in Zeichenreihenfolge. Die Etiketten werden gemeinsam
    /// angeordnet (<see cref="GroupComparisonGeometry.LayoutBadges"/>): weg von Knotenmittelpunkten und Kantenmitten,
    /// ohne einander zu überdecken. Zeichenfläche und Treffertest nutzen genau diese Anordnung.
    /// </summary>
    public IReadOnlyList<GroupComparisonBadge> GroupComparisonBadges()
    {
        var shown = Puzzle.GroupComparisons
            .Where(IsGroupComparisonShown)
            .Select(c => (Comparison: c, Connector: GroupComparisonConnector(c)))
            .Where(x => x.Connector is not null)
            .Select(x => (x.Comparison, x.Connector!.Value.From, x.Connector!.Value.To))
            .ToList();
        if (shown.Count == 0)
        {
            return [];
        }

        // Hindernisse: Knotenmittelpunkte (Wert, Kandidaten) und Kantenmitten (Marken, bei Zellen die Randmitten); bei
        // mehrfach vergebenen Knoten-Ids nimmt eine Kante wie beim Zeichnen den ersten Knoten.
        var centers = new Dictionary<int, WorldPoint>();
        foreach (var node in Puzzle.Nodes)
        {
            centers.TryAdd(node.Id, EditorGeometry.Center(Puzzle.Grid, node));
        }

        var obstacles = Puzzle.Nodes.Select(n => EditorGeometry.Center(Puzzle.Grid, n)).Distinct()
            .Concat(Puzzle.Edges
                .Where(e => centers.ContainsKey(e.From) && centers.ContainsKey(e.To))
                .Select(e => new WorldPoint((centers[e.From].X + centers[e.To].X) / 2, (centers[e.From].Y + centers[e.To].Y) / 2)));
        var scale = Viewport.Scale;
        var half = (GroupComparisonGeometry.BadgeHalfSize(scale) + BadgeGap) / scale;
        var layout = GroupComparisonGeometry.LayoutBadges([.. shown.Select(x => (x.From, x.To))], obstacles, half);
        return [.. shown.Select((x, i) => new GroupComparisonBadge(x.Comparison, x.From, x.To, layout[i]))];
    }

    /// <summary>
    /// Gezeichneter Gruppenvergleich am Weltpunkt: zuerst ein Operator-Etikett (das zuletzt gezeichnete liegt oben),
    /// ohne <paramref name="badgeOnly"/> sonst die nächste Verbindungslinie im Fangradius der Kanten.
    /// </summary>
    public GroupComparison? HitGroupComparison(WorldPoint world, bool badgeOnly = false)
    {
        var scale = Viewport.Scale;
        var shown = GroupComparisonBadges();
        for (var i = shown.Count - 1; i >= 0; i--)
        {
            var badge = shown[i].Center;
            if (GroupComparisonGeometry.BadgeContains(scale, (world.X - badge.X) * scale, (world.Y - badge.Y) * scale, tolerance: 2))
            {
                return shown[i].Comparison;
            }
        }

        if (badgeOnly)
        {
            return null;
        }

        var tolerance = EdgeHitTolerance / scale;
        return shown
            .Select(x => (x.Comparison, Distance: EditorGeometry.DistanceToSegment(world, x.From, x.To)))
            .Where(x => x.Distance <= tolerance)
            .OrderBy(x => x.Distance)
            .Select(x => x.Comparison)
            .FirstOrDefault();
    }

    partial void OnSelectedGroupComparisonItemChanged(GroupComparisonItemViewModel? value)
    {
        if (_syncingGroupSelection)
        {
            return;
        }

        if (value is not null)
        {
            SelectGroupComparison(value.Id);
        }
        else if (SelectedGroupComparisonId is not null)
        {
            ClearSelection();
        }
    }

    /// <summary>Gleicht die Liste der Gruppenvergleiche mit dem Modell ab und behält dabei vorhandene Einträge.</summary>
    private void SyncGroupComparisons()
    {
        _syncingGroupSelection = true;
        try
        {
            // Mehrfach vergebene Ids sind wie bei Gruppen möglich (Nr. 57): Einträge je Id der Reihe nach zuordnen.
            var existing = GroupComparisons.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => new Queue<GroupComparisonItemViewModel>(g));
            var target = Puzzle.GroupComparisons
                .Select(c => existing.TryGetValue(c.Id, out var items) && items.Count > 0 ? items.Dequeue() : new GroupComparisonItemViewModel(c.Id))
                .ToList();
            for (var i = GroupComparisons.Count - 1; i >= 0; i--)
            {
                if (!target.Contains(GroupComparisons[i]))
                {
                    GroupComparisons.RemoveAt(i);
                }
            }

            for (var i = 0; i < target.Count; i++)
            {
                if (i >= GroupComparisons.Count || !ReferenceEquals(GroupComparisons[i], target[i]))
                {
                    var current = GroupComparisons.IndexOf(target[i]);
                    if (current >= 0)
                    {
                        GroupComparisons.Move(current, i);
                    }
                    else
                    {
                        GroupComparisons.Insert(i, target[i]);
                    }
                }

                target[i].Refresh(Puzzle, Puzzle.GroupComparisons[i]);
            }

            HasGroupComparisons = GroupComparisons.Count > 0;
            SelectedGroupComparisonItem = SelectedGroupComparisonId is int id ? GroupComparisons.FirstOrDefault(c => c.Id == id) : null;
        }
        finally
        {
            _syncingGroupSelection = false;
        }
    }
}
