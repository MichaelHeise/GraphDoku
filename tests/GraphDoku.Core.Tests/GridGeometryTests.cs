using GraphDoku.Core.Geometry;
using GraphDoku.Core.Model;

namespace GraphDoku.Core.Tests;

public class GridGeometryTests
{
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData(GridType.Square)]
    [InlineData(GridType.Hex)]
    [InlineData(GridType.Triangle)]
    public void Neighbors_at_int_limits_drop_targets_outside_the_int_range(GridType grid)
    {
        foreach (var (x, y) in new[] { (int.MaxValue, 0), (int.MinValue, 0), (0, int.MaxValue), (0, int.MinValue), (int.MaxValue, int.MinValue) })
        {
            var neighbors = GridGeometry.GetOrthogonalNeighbors(grid, x, y)
                .Concat(GridGeometry.GetDiagonalNeighbors(grid, x, y))
                .Concat(GridGeometry.SupportsKnightMove(grid) ? GridGeometry.GetKnightMoves(grid, x, y) : []);
            foreach (var n in neighbors)
            {
                Math.Abs((long)n.X - x).Should().BeLessThanOrEqualTo(2, $"Nachbar {n} von ({x}, {y}) darf nicht überlaufen");
                Math.Abs((long)n.Y - y).Should().BeLessThanOrEqualTo(2, $"Nachbar {n} von ({x}, {y}) darf nicht überlaufen");
            }
        }

        GridGeometry.GetOrthogonalNeighbors(grid, 0, 0).Should().HaveCount(grid switch { GridType.Square => 4, GridType.Hex => 6, _ => 3 });
        GridGeometry.GetOrthogonalNeighbors(grid, int.MaxValue, 0).Should().HaveCountLessThan(GridGeometry.GetOrthogonalNeighbors(grid, 0, 0).Count);
    }

    [Fact]
    public void Square_world_coordinates_equal_grid_coordinates()
    {
        GridGeometry.ToWorld(GridType.Square, 3, -2).Should().Be(new WorldPoint(3, -2));
    }

    [Theory]
    [InlineData(GridType.Square)]
    [InlineData(GridType.Hex)]
    public void Orthogonal_neighbors_have_distance_one(GridType grid)
    {
        var origin = new GridPoint(2, 3);
        foreach (var neighbor in GridGeometry.GetOrthogonalNeighbors(grid, origin.X, origin.Y))
        {
            GridGeometry.Distance(grid, origin, neighbor).Should().BeApproximately(1, Tolerance);
        }
    }

    [Fact]
    public void Triangle_orientation_follows_parity()
    {
        GridGeometry.IsUpTriangle(0, 0).Should().BeTrue();
        GridGeometry.IsUpTriangle(1, 0).Should().BeFalse();
        GridGeometry.IsUpTriangle(0, 1).Should().BeFalse();
        GridGeometry.IsUpTriangle(-1, 0).Should().BeFalse();
        GridGeometry.IsUpTriangle(-1, -1).Should().BeTrue();
    }

    [Fact]
    public void Triangle_edge_neighbors_have_centroid_distance_of_inradius_twice()
    {
        var expected = 1 / Math.Sqrt(3);
        foreach (var origin in new[] { new GridPoint(0, 0), new GridPoint(1, 0), new GridPoint(-3, 2) })
        {
            foreach (var neighbor in GridGeometry.GetOrthogonalNeighbors(GridType.Triangle, origin.X, origin.Y))
            {
                GridGeometry.Distance(GridType.Triangle, origin, neighbor).Should().BeApproximately(expected, Tolerance);
            }
        }
    }

    [Theory]
    [InlineData(GridType.Square, 4, 8)]
    [InlineData(GridType.Triangle, 3, 12)]
    [InlineData(GridType.Hex, 6, 6)]
    public void Neighbor_counts(GridType grid, int orthogonal, int diagonal)
    {
        GridGeometry.GetOrthogonalNeighbors(grid, 0, 0).Should().HaveCount(orthogonal).And.OnlyHaveUniqueItems();
        GridGeometry.GetDiagonalNeighbors(grid, 0, 0).Should().HaveCount(diagonal).And.OnlyHaveUniqueItems();
        GridGeometry.GetOrthogonalNeighbors(grid, 1, 0).Should().HaveCount(orthogonal);
        GridGeometry.GetDiagonalNeighbors(grid, 1, 0).Should().HaveCount(diagonal);
    }

    [Theory]
    [InlineData(GridType.Square)]
    [InlineData(GridType.Triangle)]
    [InlineData(GridType.Hex)]
    public void Neighbor_offsets_match_shared_edges_and_vertices_of_cell_polygons(GridType grid)
    {
        foreach (var origin in new[] { new GridPoint(0, 0), new GridPoint(1, 0), new GridPoint(2, 3), new GridPoint(-1, 2) })
        {
            var originVertices = GridGeometry.GetCellVertices(grid, origin.X, origin.Y);
            var sharingEdge = new HashSet<GridPoint>();
            var sharingVertex = new HashSet<GridPoint>();
            for (var dx = -4; dx <= 4; dx++)
            {
                for (var dy = -3; dy <= 3; dy++)
                {
                    var other = new GridPoint(origin.X + dx, origin.Y + dy);
                    if (other == origin)
                    {
                        continue;
                    }

                    var shared = GridGeometry.GetCellVertices(grid, other.X, other.Y)
                        .Count(v => originVertices.Any(o => o.DistanceTo(v) < 1e-6));
                    if (shared >= 2)
                    {
                        sharingEdge.Add(other);
                    }

                    if (shared >= 1)
                    {
                        sharingVertex.Add(other);
                    }
                }
            }

            GridGeometry.GetOrthogonalNeighbors(grid, origin.X, origin.Y).Should().BeEquivalentTo(sharingEdge, $"{grid} {origin}");
            GridGeometry.GetDiagonalNeighbors(grid, origin.X, origin.Y).Should().BeEquivalentTo(sharingVertex, $"{grid} {origin}");
        }
    }

    [Theory]
    [InlineData(GridType.Square, 4)]
    [InlineData(GridType.Triangle, 3)]
    [InlineData(GridType.Hex, 6)]
    public void Cell_vertices_have_expected_count_and_unit_edges(GridType grid, int count)
    {
        var vertices = GridGeometry.GetCellVertices(grid, 1, 2);
        vertices.Should().HaveCount(count);
        var expectedSide = grid == GridType.Hex ? 1 / Math.Sqrt(3) : 1;
        for (var i = 0; i < count; i++)
        {
            vertices[i].DistanceTo(vertices[(i + 1) % count]).Should().BeApproximately(expectedSide, Tolerance);
        }
    }

    [Fact]
    public void Knight_moves_on_square_grid()
    {
        GridGeometry.SupportsKnightMove(GridType.Square).Should().BeTrue();
        var moves = GridGeometry.GetKnightMoves(GridType.Square, 0, 0);
        moves.Should().HaveCount(8).And.Contain(new GridPoint(1, 2)).And.Contain(new GridPoint(-2, -1));
        moves.Should().OnlyContain(m => GridGeometry.IsAtDistance(GridType.Square, new GridPoint(0, 0), m, Math.Sqrt(5), GridGeometry.DefaultTolerance));
    }

    [Theory]
    [InlineData(GridType.Triangle)]
    [InlineData(GridType.Hex)]
    public void Knight_moves_are_rejected_on_other_grids(GridType grid)
    {
        GridGeometry.SupportsKnightMove(grid).Should().BeFalse();
        var act = () => GridGeometry.GetKnightMoves(grid, 0, 0);
        act.Should().Throw<NotSupportedException>().WithMessage("Der Springerzug ist nur für das Quadratgitter definiert*");
    }

    [Fact]
    public void Free_distance_uses_tolerance()
    {
        var a = new GridPoint(0, 0);
        var b = new GridPoint(1, 1);
        GridGeometry.IsAtDistance(GridType.Square, a, b, 1.414).Should().BeFalse();
        GridGeometry.IsAtDistance(GridType.Square, a, b, 1.414, tolerance: 0.001).Should().BeTrue();
        GridGeometry.IsAtDistance(GridType.Hex, a, new GridPoint(1, 1), Math.Sqrt(3)).Should().BeTrue();
    }

    [Theory]
    [InlineData(GridType.Square)]
    [InlineData(GridType.Triangle)]
    [InlineData(GridType.Hex)]
    public void Nearest_grid_point_inverts_world_coordinates(GridType grid)
    {
        for (var x = -3; x <= 3; x++)
        {
            for (var y = -3; y <= 3; y++)
            {
                var world = GridGeometry.ToWorld(grid, x, y);
                var shifted = new WorldPoint(world.X + 0.05, world.Y - 0.05);
                GridGeometry.ToNearestGridPoint(grid, shifted).Should().Be(new GridPoint(x, y));
            }
        }
    }
}
