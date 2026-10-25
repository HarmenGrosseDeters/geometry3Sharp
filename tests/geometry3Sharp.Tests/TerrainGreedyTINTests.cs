using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TIN from a height grid by greedy insertion (Garland and Heckbert): guaranteed maximum vertical
    /// error at every grid node, a height field by construction.
    /// </summary>
    public class TerrainGreedyTINTests
    {
        static double[] Grid(int nx, int ny, Func<int, int, double> h)
        {
            var z = new double[nx * ny];
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) z[i + nx * j] = h(i, j);
            return z;
        }

        // vertical distance of every grid node from the TIN (vertical rays), and the triangles' XY area
        static double MaxNodeError(TerrainGreedyTIN g)
        {
            var tree = new DMeshAABBTree3(g.Result, true);
            double top = g.Result.CachedBounds.Max.z + 10, max = 0;
            for (int j = 0; j < g.NY; ++j)
                for (int i = 0; i < g.NX; ++i) {
                    var ray = new Ray3d(new Vector3d(g.X0 + i * g.CellSize, g.Y0 + j * g.CellSize, top), -Vector3d.AxisZ);
                    int tid = tree.FindNearestHitTriangle(ray);
                    Assert.True(tid >= 0, $"node {i},{j} not covered");
                    var intr = MeshQueries.TriangleIntersection(g.Result, tid, ray);
                    Assert.True(intr.Find());
                    max = Math.Max(max, Math.Abs(ray.PointAt(intr.RayParameter).z - g.Z[i + g.NX * j]));
                }
            return max;
        }

        [Fact]
        public void Plane_TwoTriangles()
        {
            var g = new TerrainGreedyTIN(Grid(51, 31, (i, j) => 0.3 * i - 0.2 * j + 5), 51, 31, 1000, 2000, 1) { MaxError = 1e-9 };
            Assert.True(g.Compute());
            Assert.Equal(2, g.Result.TriangleCount);
            Assert.True(g.AchievedError < 1e-9);
        }

        [Theory]
        [InlineData(0.5, false)]
        [InlineData(0.1, false)]
        [InlineData(0.02, false)]
        [InlineData(0.5, true)]
        [InlineData(0.1, true)]
        [InlineData(0.02, true)]
        public void WavyTerrain_MaxErrorAtEveryNode_HeightField(double maxError, bool removeVertices)
        {
            var rnd = new Random(3);
            int nx = 121, ny = 97;
            var z = Grid(nx, ny, (i, j) => 5 * Math.Sin(i / 9.0) * Math.Cos(j / 13.0) + 0.05 * rnd.NextDouble());
            // UTM-like origin
            var g = new TerrainGreedyTIN(z, nx, ny, 326000.5, 5641000.5, 1) { MaxError = maxError, RemoveVertices = removeVertices };
            Assert.True(g.Compute());
            g.Result.CheckValidity();
            Assert.True(g.AchievedError <= maxError);
            Assert.True(MaxNodeError(g) <= maxError + 1e-9);
            Assert.Equal(0, TerrainVolumeDifference.XYOverlaps(g.Result));
            Assert.True(g.Result.TriangleCount < 2 * (nx - 1) * (ny - 1));
            // footprint: the whole rectangle, facing up
            double area = g.Result.TriangleIndices().Sum(t => { var n = g.Result.GetTriNormal(t); Assert.True(n.z > 0); return g.Result.GetTriArea(t) * n.z; });
            Assert.Equal((nx - 1) * (ny - 1), area, 6);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Benches_BreakLinesKeptWithinTheError(bool removeVertices)
        {
            // open-pit benches at 31 degrees: 8 m faces rising 6 m, 4 m berms
            double c = Math.Cos(31 * MathUtil.Deg2Rad), s = Math.Sin(31 * MathUtil.Deg2Rad);
            int n = 151;
            var z = Grid(n, n, (i, j) => {
                double u = c * i + s * j, k = Math.Floor(u / 12), f = u - 12 * k;
                return 6 * k + (f < 8 ? 6 * f / 8 : 6);
            });
            var g = new TerrainGreedyTIN(z, n, n, 0, 0, 1) { MaxError = 0.05, RemoveVertices = removeVertices };
            Assert.True(g.Compute());
            g.Result.CheckValidity();
            Assert.True(MaxNodeError(g) <= 0.05 + 1e-9);
            Assert.Equal(0, TerrainVolumeDifference.XYOverlaps(g.Result));
            // far fewer triangles than the grid (planar faces and berms)
            Assert.True(g.Result.TriangleCount < 0.2 * 2 * (n - 1) * (n - 1), $"{g.Result.TriangleCount} triangles");
        }

        [Fact]
        public void RemoveVertices_FarFewerTrianglesOnBenches()
        {
            // the planar faces and berms need only the vertices on the break lines; greedy insertion places
            // many more on the way (3909 triangles here, 249 after the removal)
            double c = Math.Cos(31 * MathUtil.Deg2Rad), s = Math.Sin(31 * MathUtil.Deg2Rad);
            int n = 151;
            var z = Grid(n, n, (i, j) => {
                double u = c * i + s * j, k = Math.Floor(u / 12), f = u - 12 * k;
                return 6 * k + (f < 8 ? 6 * f / 8 : 6);
            });
            var plain = new TerrainGreedyTIN(z, n, n, 0, 0, 1) { MaxError = 0.05 };
            var removed = new TerrainGreedyTIN(z, n, n, 0, 0, 1) { MaxError = 0.05, RemoveVertices = true };
            Assert.True(plain.Compute() && removed.Compute());
            Assert.True(MaxNodeError(removed) <= 0.05 + 1e-9);
            Assert.True(removed.RemovedVertices > 0);
            Assert.True(removed.Result.TriangleCount < 0.2 * plain.Result.TriangleCount, $"{plain.Result.TriangleCount} -> {removed.Result.TriangleCount}");
        }

        [Fact]
        public void MaxVertices_StopsEarly_ReportsTheError()
        {
            var z = Grid(64, 64, (i, j) => Math.Sin(i / 5.0) * Math.Sin(j / 7.0));
            var g = new TerrainGreedyTIN(z, 64, 64, 0, 0, 1) { MaxError = 0, MaxVertices = 50 };
            Assert.True(g.Compute());
            Assert.Equal(50, g.Result.VertexCount);
            Assert.Equal(MaxNodeError(g), g.AchievedError, 9);
        }

        [Fact]
        public void InvalidInput_Fails()
        {
            Assert.False(new TerrainGreedyTIN(new double[4], 1, 4, 0, 0, 1).Compute());
            Assert.False(new TerrainGreedyTIN(new double[] { 0, 0, double.NaN, 0 }, 2, 2, 0, 0, 1).Compute());
            Assert.False(new TerrainGreedyTIN(new double[4], 2, 2, 0, 0, 0).Compute());
        }
    }
}
