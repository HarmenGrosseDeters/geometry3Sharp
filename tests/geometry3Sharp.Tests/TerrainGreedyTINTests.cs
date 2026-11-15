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
        [Fact]
        public void ZeroError_TinyHeightSweepTerminates()
        {
            var random = new Random(855);
            for (int trial = 0; trial < 80; ++trial) {
                var heights = new double[20];
                for (int i = 0; i < heights.Length; ++i)
                    heights[i] = random.Next(3) == 0 ? (random.NextDouble() - 0.5) * 1e-220 : random.Next(2);
                int calls = 0;
                var tin = new TerrainGreedyTIN(heights, 5, 4, 0, 0, 1) {
                    MaxError = 0, CancelF = () => ++calls > 100
                };
                Assert.True(tin.Compute());
                Assert.InRange(tin.Result.VertexCount, 4, heights.Length);
                Assert.True(tin.Result.CheckValidity());
            }
        }

        [Fact]
        public void ZeroError_DoesNotReinsertExistingVertex()
        {
            double[] heights = { 0, 1, 1, 1, 1, 1, 1, -2.868160137710835E-223, 0, 0 };
            int calls = 0;
            var tin = new TerrainGreedyTIN(heights, 5, 2, 0.0625, 0, 0.35377429245212266) {
                MaxError = 0, CancelF = () => ++calls > 100
            };
            Assert.True(tin.Compute());
            Assert.NotNull(tin.Result);
            Assert.InRange(tin.Result.VertexCount, 4, 10);
            Assert.True(tin.Result.CheckValidity());
        }

        // #855 under Similarity.All(): a TIN of a height grid keeps z up, so a rotated frame is not applicable (the convention
        // of TerrainGridTinTests); scale and translation map the grid, and the 2^k frames give the same TIN bit for bit.
        // MaxError = 0 stays 0 in every frame, and the run ends within the grid's nodes
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void ZeroError_Similarities_EndAndScale(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) return;     // a grid cannot be rotated: not applicable
            double[] heights = { 0, 1, 1, 1, 1, 1, 1, -2.868160137710835E-223, 0, 0 };
            Func<Similarity, TerrainGreedyTIN> run = (f) => {
                int calls = 0;
                var t = new TerrainGreedyTIN(heights.Select(z => f.Scale * z + f.Translation.z).ToArray(), 5, 2,
                                             f.Scale * 0.0625 + f.Translation.x, f.Translation.y, f.Scale * 0.35377429245212266) {
                    MaxError = 0, CancelF = () => ++calls > 100
                };
                Assert.True(t.Compute());
                return t;
            };
            TerrainGreedyTIN g = run(s);
            Assert.InRange(g.Result.VertexCount, 4, 10);
            Assert.True(g.Result.CheckValidity());
            if (s.IsExact) {
                TerrainGreedyTIN r = run(Similarity.Identity);
                Assert.Equal(r.Result.VertexCount, g.Result.VertexCount);
                Assert.Equal(r.Result.TriangleCount, g.Result.TriangleCount);
                for (int v = 0; v < r.Result.MaxVertexID; ++v) {
                    Vector3d a = r.Result.GetVertex(v), b = g.Result.GetVertex(v);
                    Assert.Equal(s.Apply(a), b);
                }
                for (int t = 0; t < r.Result.MaxTriangleID; ++t) Assert.Equal(r.Result.GetTriangle(t), g.Result.GetTriangle(t));
            }
        }

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
        public void RemoveVertices_PlateauIsInvariantUnderPowerOfTwoScaling()
        {
            var random = new Random(283);
            var heights = Enumerable.Range(0, 16).Select(_ => random.Next(3) + random.Next(3) * 1e-14).ToArray();
            TerrainGreedyTIN Compute(double scale) {
                var tin = new TerrainGreedyTIN(heights.Select(z => z * scale).ToArray(), 4, 4, 0, 0, scale) {
                    MaxError = 0.5 * scale, RemoveVertices = true
                };
                Assert.True(tin.Compute());
                Assert.True(tin.Result.CheckValidity());
                return tin;
            }
            var original = Compute(1);
            Assert.True(MaxNodeError(original) <= 0.5 + 1e-9);
            foreach (double scale in new[] { 1.0 / 1024, 1024.0, 1099511627776.0 }) {
                var scaled = Compute(scale);
                Assert.Equal(original.Result.VertexCount, scaled.Result.VertexCount);
                Assert.Equal(original.Result.TriangleCount, scaled.Result.TriangleCount);
                foreach (int v in original.Result.VertexIndices()) {
                    Assert.True(scaled.Result.IsVertex(v));
                    Assert.Equal(original.Result.GetVertex(v), scaled.Result.GetVertex(v) / scale);
                }
                foreach (int t in original.Result.TriangleIndices())
                    Assert.Equal(original.Result.GetTriangle(t), scaled.Result.GetTriangle(t));
            }
        }

        // #854 under Similarity.All(): a TIN of a height grid keeps z up, so a rotated frame is not applicable (the convention
        // of TerrainGridTinTests); scale and translation map the grid. Every frame keeps the error bound at every node with a
        // valid TIN; the 2^k frames give the same TIN bit for bit (the ear choice compares the errors exactly)
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void RemoveVertices_Plateau_Similarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) return;     // a grid cannot be rotated: not applicable
            var random = new Random(283);
            var heights = Enumerable.Range(0, 16).Select(_ => random.Next(3) + random.Next(3) * 1e-14).ToArray();
            Func<Similarity, TerrainGreedyTIN> run = (f) => {
                var tin = new TerrainGreedyTIN(heights.Select(z => f.Scale * z + f.Translation.z).ToArray(), 4, 4,
                                               f.Translation.x, f.Translation.y, f.Scale) { MaxError = 0.5 * f.Scale, RemoveVertices = true };
                Assert.True(tin.Compute());
                Assert.True(tin.Result.CheckValidity());
                return tin;
            };
            TerrainGreedyTIN g = run(s);
            Assert.True(MaxNodeError(g) <= 0.5 * s.Scale * (1 + 1e-9) + 1e-9 * Math.Abs(s.Translation.z), $"{MaxNodeError(g)}");
            if (s.IsExact) {
                TerrainGreedyTIN r = run(Similarity.Identity);
                Assert.Equal(r.Result.VertexCount, g.Result.VertexCount);
                Assert.Equal(r.Result.TriangleCount, g.Result.TriangleCount);
                foreach (int v in r.Result.VertexIndices()) Assert.Equal(s.Apply(r.Result.GetVertex(v)), g.Result.GetVertex(v));
                foreach (int t in r.Result.TriangleIndices()) Assert.Equal(r.Result.GetTriangle(t), g.Result.GetTriangle(t));
            }
        }

        [Fact]
        public void RemoveVertices_SeededPlateausKeepTopologyAtPowerOfTwoScale()
        {
            for (int seed = 0; seed < 32; ++seed) {
                var random = new Random(seed);
                var heights = Enumerable.Range(0, 16).Select(_ => random.Next(3) + random.Next(3) * 1e-14).ToArray();
                TerrainGreedyTIN Compute(double scale) {
                    var tin = new TerrainGreedyTIN(heights.Select(z => z * scale).ToArray(), 4, 4, 0, 0, scale) {
                        MaxError = 0.5 * scale, RemoveVertices = true
                    };
                    Assert.True(tin.Compute());
                    return tin;
                }
                var a = Compute(1);
                var b = Compute(1099511627776.0);
                Assert.True(a.Result.CheckValidity());
                Assert.True(b.Result.CheckValidity());
                Assert.Equal(a.Result.VertexCount, b.Result.VertexCount);
                Assert.Equal(a.Result.TriangleCount, b.Result.TriangleCount);
                foreach (int v in a.Result.VertexIndices()) {
                    Assert.True(b.Result.IsVertex(v));
                    Assert.Equal(a.Result.GetVertex(v), b.Result.GetVertex(v) / 1099511627776.0);
                }
                foreach (int t in a.Result.TriangleIndices())
                    Assert.Equal(a.Result.GetTriangle(t), b.Result.GetTriangle(t));
            }
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
