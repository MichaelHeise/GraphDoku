using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing;

/// <summary>Umriss einer Knotenmenge: alle Zellkanten, die nicht von zwei Zellen der Menge geteilt werden.</summary>
public static class GroupOutline
{
    public static IReadOnlyList<(WorldPoint A, WorldPoint B)> BoundarySegments(GridType grid, IEnumerable<GridPoint> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var counts = new Dictionary<(long, long, long, long), (WorldPoint A, WorldPoint B, int Count)>();
        foreach (var cell in cells.Distinct())
        {
            var vertices = GridGeometry.GetCellVertices(grid, cell.X, cell.Y);
            for (var i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Count];
                var key = Key(a, b);
                counts[key] = counts.TryGetValue(key, out var existing) ? existing with { Count = existing.Count + 1 } : (a, b, 1);
            }
        }

        return [.. counts.Values.Where(v => v.Count == 1).Select(v => (v.A, v.B))];
    }

    /// <summary>
    /// Umrisse mehrerer Gruppen zusammengefasst: jedes Umrissstück einmal, mit der Zahl der Gruppen, auf deren Umriss
    /// es liegt, und dem Index der Gruppe, falls es genau eine ist (sonst -1). Beim klassischen Sudoku liegen z. B.
    /// die Blockgrenzen auf den Umrissen von vier Gruppen (zwei Spalten bzw. Zeilen und zwei Blöcken), die übrigen
    /// Zellgrenzen nur auf zweien – so lassen sich Blockgrenzen betonen, ohne die Regel zu kennen.
    /// </summary>
    public static IReadOnlyList<SharedSegment> SharedBoundaries(GridType grid, IReadOnlyList<IEnumerable<GridPoint>> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var segments = new Dictionary<(long, long, long, long), SharedSegment>();
        for (var index = 0; index < groups.Count; index++)
        {
            foreach (var (a, b) in BoundarySegments(grid, groups[index]))
            {
                var key = Key(a, b);
                segments[key] = segments.TryGetValue(key, out var existing)
                    ? existing with { Count = existing.Count + 1, Owner = -1 }
                    : new SharedSegment(a, b, 1, index);
            }
        }

        return [.. segments.Values];
    }

    private static (long, long, long, long) Key(WorldPoint a, WorldPoint b)
    {
        var p = (Round(a.X), Round(a.Y));
        var q = (Round(b.X), Round(b.Y));
        return p.CompareTo(q) <= 0 ? (p.Item1, p.Item2, q.Item1, q.Item2) : (q.Item1, q.Item2, p.Item1, p.Item2);
    }

    private static long Round(double value) => (long)Math.Round(value * 10000);
}

/// <summary>Umrissstück mehrerer Gruppen: Anzahl der Gruppen, auf deren Umriss es liegt; <see cref="Owner"/> = Index der einzigen Gruppe oder -1.</summary>
public readonly record struct SharedSegment(WorldPoint A, WorldPoint B, int Count, int Owner);
