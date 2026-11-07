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

    private static (long, long, long, long) Key(WorldPoint a, WorldPoint b)
    {
        var p = (Round(a.X), Round(a.Y));
        var q = (Round(b.X), Round(b.Y));
        return p.CompareTo(q) <= 0 ? (p.Item1, p.Item2, q.Item1, q.Item2) : (q.Item1, q.Item2, p.Item1, p.Item2);
    }

    private static long Round(double value) => (long)Math.Round(value * 10000);
}
