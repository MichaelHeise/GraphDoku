using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;

namespace GraphDoku.App.Editing;

/// <summary>
/// Lage der Verbindung eines Gruppenvergleichs auf der Zeichenfläche: eine gestrichelte Linie zwischen den
/// Schwerpunkten der Knotenmittelpunkte beider Gruppen, in der Mitte ein Etikett mit dem Operator. Gemeinsam für das
/// Zeichnen (<c>GraphCanvas</c>) und den Treffertest des Editors.
/// </summary>
public static class GroupComparisonGeometry
{
    /// <summary>Schwerpunkt der Knotenmittelpunkte (jede Position einmal); <c>null</c> ohne Knoten.</summary>
    public static WorldPoint? Anchor(GridType grid, IEnumerable<Node> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var centers = members.DistinctBy(n => (n.X, n.Y)).Select(n => EditorGeometry.Center(grid, n)).ToList();
        return centers.Count == 0 ? null : new WorldPoint(centers.Average(c => c.X), centers.Average(c => c.Y));
    }

    /// <summary>Mitte der Verbindung, dort steht das Etikett mit dem Operator.</summary>
    public static WorldPoint BadgeCenter(WorldPoint from, WorldPoint to) => new((from.X + to.X) / 2, (from.Y + to.Y) / 2);

    /// <summary>Schriftgröße des Operator-Etiketts in Pixeln (wächst mit dem Zoom, in Grenzen).</summary>
    public static double BadgeFontSize(double scale) => Math.Clamp(scale * 0.24, 10, 18);

    /// <summary>Halbe Höhe (und Mindest-Halbbreite) des Etiketts in Pixeln; das Symbol ist ein Zeichen, das Etikett fast quadratisch.</summary>
    public static double BadgeHalfSize(double scale) => BadgeFontSize(scale) * 0.8;

    /// <summary>Liegt der Bildschirmversatz (dx, dy) vom Etikettmittelpunkt auf dem Etikett, mit Toleranz in Pixeln?</summary>
    public static bool BadgeContains(double scale, double dx, double dy, double tolerance = 0)
    {
        var half = BadgeHalfSize(scale) + tolerance;
        return Math.Abs(dx) <= half && Math.Abs(dy) <= half;
    }
}
