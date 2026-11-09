using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>TerrainGridTin: the TIN of a height grid with a guaranteed surface error and breaklines (procedural terrains only).</summary>
    public class TerrainGridTinTests
    {
        /// <summary>Hills, a cliff of 1.5 along a circle (radius 20 around node (32, 32)), small roughness; optional gaps.</summary>
        static HeightGrid Terrain(int n, bool gaps, double x0 = 100, double y0 = 200, double cell = 1)
        {
            var g = new HeightGrid(new double[n * n], n, n, x0, y0, cell);
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    double r = Math.Sqrt((i - 32.0) * (i - 32.0) + (j - 32.0) * (j - 32.0));
                    g[i, j] = 8 * Math.Sin(i * 0.1) * Math.Cos(j * 0.13) + 0.6 * Math.Sin(0.045 * i * j) + 0.05 * Math.Sin(1.3 * i + 0.7 * j) - (r < 20 ? 1.5 : 0);
                }
            if (gaps) {
                for (int j = 40; j <= 50; ++j) for (int i = 10; i <= 20; ++i) g[i, j] = double.NaN;     // a block
                g[50, 10] = double.NaN;                                                                  // a single node
                for (int j = 0; j <= 3; ++j) g[n - 1, j] = double.NaN;                                   // a notch at the rim
            }
            return g;
        }

        static TerrainBreakline Circle(HeightGrid g, double r, double spacing = 2)
        {
            int k = (int)Math.Round(2 * Math.PI * r / spacing);
            var pts = new List<Vector2d>();
            for (int q = 0; q <= k; ++q) {
                double a = 2 * Math.PI * (q % k) / k;
                pts.Add(new Vector2d(g.X0 + Math.Round((32 + r * Math.Cos(a)) * 64) / 64 * g.CellSize, g.Y0 + Math.Round((32 + r * Math.Sin(a)) * 64) / 64 * g.CellSize));   // (multiples of 1/64: exact under 2^k scaling and translation)
            }
            return new TerrainBreakline(pts);
        }

        static TerrainGridTin Run(HeightGrid g, double eps, params TerrainBreakline[] lines)
        {
            var t = new TerrainGridTin { Grid = g, Epsilon = eps, Breaklines = lines.ToList() };
            Assert.True(t.Compute(), t.Status + " " + t.Message);
            return t;
        }

        /// <summary>|TIN - S| at random points, by locating the triangle (brute force) and evaluating S.</summary>
        static double SampledError(HeightGrid g, DMesh3 tin, int samples, int seed)
        {
            var rand = new Random(seed);
            var tris = tin.TriangleIndices().Select(t => { Index3i tv = tin.GetTriangle(t); return new[] { tin.GetVertex(tv.a), tin.GetVertex(tv.b), tin.GetVertex(tv.c) }; }).ToArray();
            double worst = 0;
            for (int s = 0; s < samples; ++s) {
                double x = g.X0 + rand.NextDouble() * (g.NX - 1) * g.CellSize, y = g.Y0 + rand.NextDouble() * (g.NY - 1) * g.CellSize;
                double sz = g.Height(x, y);
                if (double.IsNaN(sz)) continue;
                foreach (Vector3d[] t in tris) {
                    double det = (t[1].x - t[0].x) * (t[2].y - t[0].y) - (t[2].x - t[0].x) * (t[1].y - t[0].y);
                    double l1 = ((x - t[0].x) * (t[2].y - t[0].y) - (t[2].x - t[0].x) * (y - t[0].y)) / det;
                    double l2 = ((t[1].x - t[0].x) * (y - t[0].y) - (x - t[0].x) * (t[1].y - t[0].y)) / det;
                    if (l1 < 0 || l2 < 0 || l1 + l2 > 1) continue;
                    worst = Math.Max(worst, Math.Abs(t[0].z + l1 * (t[1].z - t[0].z) + l2 * (t[2].z - t[0].z) - sz));
                    break;
                }
            }
            return worst;
        }

        static void AssertHeightField(DMesh3 tin)
        {
            foreach (int t in tin.TriangleIndices())
                Assert.True(tin.GetTriArea(t) > 0 && tin.GetTriNormal(t).z > 0, "triangle " + t + " is not counter-clockwise from above");
            Assert.Equal(0, TerrainVolumeDifference.XYOverlaps(tin));
        }

        // ---- the guarantee ----

        [Theory]
        [InlineData(0.05, false)]
        [InlineData(0.2, false)]
        [InlineData(0.5, true)]
        [InlineData(0.1, true)]
        public void SurfaceError_IsWithinEpsilon_AndIsTheErrorOfTheResult(double eps, bool breaklines)
        {
            HeightGrid g = Terrain(65, false);
            TerrainGridTin t = breaklines ? Run(g, eps, Circle(g, 20), Circle(g, 12)) : Run(g, eps);
            Assert.InRange(t.SurfaceError, 0.0, eps);
            Assert.True(t.NodeError <= t.SurfaceError);
            AssertHeightField(t.Result);
            // the scan of the finished mesh, by the public function (another path: world coordinates)
            TerrainGridTin.ErrorOfTin(g, t.Result, out double node, out double surf);
            Assert.True(surf <= eps && node <= surf && surf <= t.SurfaceError + 1e-9);
            // dense sampling never exceeds it
            Assert.True(SampledError(g, t.Result, 3000, 1) <= t.SurfaceError + 1e-9);
            if (!breaklines && eps <= 0.2) Assert.True(t.LockedCreases > 0);
            Assert.True(t.Result.VertexCount < g.Z.Length);       // fewer vertices than the grid has nodes
        }

        [Fact]
        public void ErrorOfTin_FindsWhatTheNodeErrorMisses()
        {
            // the node-based TIN of TerrainGreedyTIN has the node error 0.1 and a surface error above it (cliff along grid lines)
            HeightGrid g = Terrain(65, false, 0, 0, 1);
            var greedy = new TerrainGreedyTIN((double[])g.Z.Clone(), 65, 65, 0, 0, 1) { MaxError = 0.1 };
            Assert.True(greedy.Compute());
            TerrainGridTin.ErrorOfTin(g, greedy.Result, out double node, out double surf);
            Assert.True(node <= 0.1 + 1e-9);
            Assert.True(surf > 0.1 * 1.5, "surface error " + surf);
            double sampled = SampledError(g, greedy.Result, 20000, 7);
            Assert.True(sampled <= surf + 1e-9 && sampled > 0.5 * surf, $"sampled {sampled}, exact {surf}");
        }

        [Fact]
        public void LowerEpsilon_GivesMoreVertices_AndCostsOverTheLooserOne()
        {
            HeightGrid g = Terrain(65, false);
            int loose = Run(g, 0.4).Result.VertexCount, tight = Run(g, 0.05).Result.VertexCount;
            Assert.True(tight > loose);
        }

        // ---- breaklines ----

        [Fact]
        public void Breaklines_AreEdgeChains_OfTheResult()
        {
            HeightGrid g = Terrain(65, false, 10, 20, 2);
            TerrainBreakline line = Circle(g, 20, 4);
            TerrainGridTin t = Run(g, 0.2, line);
            Assert.NotEmpty(t.BreaklineEdges);
            // every breakline edge lies on the input polyline, and together they have the polyline's length
            double total = 0, input = 0;
            for (int k = 0; k + 1 < line.Points.Count; ++k) input += (line.Points[k + 1] - line.Points[k]).Length;
            foreach (Index2i e in t.BreaklineEdges) {
                Vector3d a = t.Result.GetVertex(e.a), b = t.Result.GetVertex(e.b);
                total += new Vector2d(b.x - a.x, b.y - a.y).Length;
                foreach (Vector3d p in new[] { a, b })
                    Assert.True(Enumerable.Range(0, line.Points.Count - 1).Min(k => DistanceToSegment(new Vector2d(p.x, p.y), line.Points[k], line.Points[k + 1])) < 1e-9, "a breakline vertex off the polyline");
                Assert.True(t.Result.FindEdge(e.a, e.b) != DMesh3.InvalidID);
            }
            Assert.Equal(input, total, 6);
            // the input vertices are vertices of the result
            foreach (Vector2d p in line.Points)
                Assert.Contains(t.Result.Vertices(), v => Math.Abs(v.x - p.x) < 1e-9 && Math.Abs(v.y - p.y) < 1e-9);
        }

        static double DistanceToSegment(Vector2d p, Vector2d a, Vector2d b)
        {
            Vector2d d = b - a; double t = Math.Max(0, Math.Min(1, (p - a).Dot(d) / d.LengthSquared));
            return (p - (a + t * d)).Length;
        }

        [Fact]
        public void Breaklines_HeightsComeFromTheGrid_AndCrossingBreaklinesWork()
        {
            HeightGrid g = Terrain(65, false);
            // two lines that cross each other and the cliff circle; the z of the points is ignored (they are 2D)
            var l1 = new TerrainBreakline(new[] { new Vector2d(g.X0 + 5.3, g.Y0 + 7.1), new Vector2d(g.X0 + 58.4, g.Y0 + 51.9) });
            var l2 = new TerrainBreakline(new[] { new Vector2d(g.X0 + 6.6, g.Y0 + 55.2), new Vector2d(g.X0 + 57.7, g.Y0 + 9.8) });
            TerrainGridTin t = Run(g, 0.1, l1, l2, Circle(g, 20));
            Assert.True(t.SurfaceError <= 0.1);
            AssertHeightField(t.Result);
            Assert.True(SampledError(g, t.Result, 3000, 2) <= 0.1 + 1e-9);
        }

        [Fact]
        public void Breaklines_OutsideTheData_AreDropped_AndInvalidOnesRejected()
        {
            HeightGrid g = Terrain(65, true);
            // a line through the block of no data (x 10..20, y 40..50): the segments with an end point or the middle inside are dropped
            var line = new TerrainBreakline(new[] { new Vector2d(g.X0 + 5, g.Y0 + 45), new Vector2d(g.X0 + 8, g.Y0 + 45), new Vector2d(g.X0 + 12, g.Y0 + 45), new Vector2d(g.X0 + 25, g.Y0 + 45), new Vector2d(g.X0 + 30, g.Y0 + 45) });
            TerrainGridTin t = Run(g, 0.2, line);
            Assert.Equal(2, t.DroppedBreaklineSegments);      // (8,45)-(12,45) and (12,45)-(25,45)
            Assert.NotEmpty(t.BreaklineEdges);
            var bad = new TerrainGridTin { Grid = g, Epsilon = 0.1, Breaklines = { new TerrainBreakline(new[] { new Vector2d(double.NaN, 0), new Vector2d(1, 1) }) } };
            Assert.False(bad.Compute());
            Assert.Equal(TerrainGridTinStatus.InvalidInput, bad.Status);
        }

        [Fact]
        public void Breaklines_OnTheBoundaryOfTheData_AreKept()
        {
            HeightGrid g = Terrain(65, true);
            // along the grid line x = 9, the left side of the block of no data (cells 9..20 have no data, cells 8 have): the line belongs to the cells with data
            var line = new TerrainBreakline(new[] { new Vector2d(g.X0 + 9, g.Y0 + 41.25), new Vector2d(g.X0 + 9, g.Y0 + 45.5), new Vector2d(g.X0 + 9, g.Y0 + 49.75) });
            TerrainGridTin t = Run(g, 0.2, line);
            Assert.Equal(0, t.DroppedBreaklineSegments);
            Assert.NotEmpty(t.BreaklineEdges);
        }

        // ---- gaps ----

        [Fact]
        public void Gaps_AreHoles_AndTheTinCoversExactlyTheCellsWithData()
        {
            HeightGrid g = Terrain(65, true);
            TerrainGridTin t = Run(g, 0.2, Circle(g, 20));
            AssertHeightField(t.Result);
            int cells = 0;
            for (int j = 0; j < 64; ++j) for (int i = 0; i < 64; ++i)
                if (HeightGrid.IsData(g[i, j]) && HeightGrid.IsData(g[i + 1, j]) && HeightGrid.IsData(g[i, j + 1]) && HeightGrid.IsData(g[i + 1, j + 1])) ++cells;
            double area = t.Result.TriangleIndices().Sum(tr => { Index3i tv = t.Result.GetTriangle(tr); Vector3d a = t.Result.GetVertex(tv.a), b = t.Result.GetVertex(tv.b), c = t.Result.GetVertex(tv.c); return 0.5 * ((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)); });
            foreach (int tr in t.Result.TriangleIndices()) {
                Vector3d c = t.Result.GetTriCentroid(tr);
                Assert.False(double.IsNaN(g.Height(c.x, c.y)), "a triangle over a cell without data at " + c.x + "," + c.y);
            }
            Assert.Equal(cells, area, 6);
            Assert.True(SampledError(g, t.Result, 3000, 3) <= t.SurfaceError + 1e-9);
        }

        [Fact]
        public void Gaps_AllNaN_AndSingleCell()
        {
            var g = new HeightGrid(Enumerable.Repeat(double.NaN, 9).ToArray(), 3, 3, 0, 0, 1);
            var t = new TerrainGridTin { Grid = g };
            Assert.False(t.Compute());
            Assert.Equal(TerrainGridTinStatus.NoData, t.Status);
            var one = new HeightGrid(new double[] { 1, 2, 3, 5 }, 2, 2, 0, 0, 1);
            TerrainGridTin r = Run(one, 0.01);
            Assert.Equal(2, r.Result.TriangleCount);
            Assert.Equal(0, r.SurfaceError, 6);
        }

        // ---- invariance, reuse, determinism ----

        static (HeightGrid g, TerrainBreakline line) Mapped(Similarity s)
        {
            HeightGrid g0 = Terrain(49, false, 12.5, -7.25, 0.5);
            var z = g0.Z.Select(v => s.Scale * v + s.Translation.z).ToArray();
            var g = new HeightGrid(z, g0.NX, g0.NY, s.Scale * g0.X0 + s.Translation.x, s.Scale * g0.Y0 + s.Translation.y, s.Scale * g0.CellSize);
            TerrainBreakline c0 = Circle(g0, 12);
            var line = new TerrainBreakline(c0.Points.Select(p => new Vector2d(s.Scale * p.x + s.Translation.x, s.Scale * p.y + s.Translation.y)));
            return (g, line);
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Invariance_ScaleAndTranslation(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) return;     // a grid cannot be rotated: not applicable
            var (g, line) = Mapped(s);
            TerrainGridTin t = Run(g, 0.2 * s.Scale, line);
            Assert.True(t.SurfaceError <= 0.2 * s.Scale);
            TerrainGridTin.ErrorOfTin(g, t.Result, out _, out double surf);
            Assert.True(surf <= 0.2 * s.Scale);
            AssertHeightField(t.Result);
            if (s.IsExact || s.Translation.x == 0 && s.Translation.y == 0 && Math.Abs(Math.Log(s.Scale, 2) - Math.Round(Math.Log(s.Scale, 2))) < 1e-12) {
                // scaling by 2^k (and translation of the heights only) is exact: the same TIN, vertex for vertex, bit for bit
                var (g1, line1) = Mapped(Similarity.Identity);
                TerrainGridTin t1 = Run(g1, 0.2, line1);
                Assert.Equal(t1.Result.VertexCount, t.Result.VertexCount);
                Assert.Equal(t1.Result.TriangleCount, t.Result.TriangleCount);
                for (int v = 0; v < t1.Result.MaxVertexID; ++v) {
                    Vector3d a = t1.Result.GetVertex(v), b = t.Result.GetVertex(v);
                    Assert.Equal(s.Scale * a.x, b.x); Assert.Equal(s.Scale * a.y, b.y); Assert.Equal(s.Scale * a.z, b.z);
                }
                for (int tr = 0; tr < t1.Result.MaxTriangleID; ++tr) Assert.Equal(t1.Result.GetTriangle(tr), t.Result.GetTriangle(tr));
                Assert.Equal(t1.BreaklineEdges.Count, t.BreaklineEdges.Count);
            }
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Fact]
        public void TranslationOfTheOrigin_GivesTheSameTinInIndexSpace()
        {
            // the XY origin does not enter the computation: the meshes differ by the translation (exactly, for dyadic values)
            HeightGrid a = Terrain(49, false, 0, 0, 1), b = Terrain(49, false, 1024, -2048, 1);
            TerrainGridTin ta = Run(a, 0.2, Circle(a, 12)), tb = Run(b, 0.2, Circle(b, 12));
            Assert.Equal(ta.Result.VertexCount, tb.Result.VertexCount);
            for (int v = 0; v < ta.Result.MaxVertexID; ++v) {
                Vector3d p = ta.Result.GetVertex(v), q = tb.Result.GetVertex(v);
                Assert.Equal(p.x + 1024, q.x); Assert.Equal(p.y - 2048, q.y); Assert.Equal(p.z, q.z);
            }
        }

        [Fact]
        public void Compute_Twice_AndRepeated_GiveTheSameResult()
        {
            HeightGrid g = Terrain(65, true);
            var t = new TerrainGridTin { Grid = g, Epsilon = 0.1, Breaklines = { Circle(g, 20) } };
            Assert.True(t.Compute());
            DMesh3 first = t.Result; double err = t.SurfaceError; int rounds = t.Rounds;
            Assert.True(t.Compute());          // reuse of the instance
            AssertSameMesh(first, t.Result);
            Assert.Equal(err, t.SurfaceError); Assert.Equal(rounds, t.Rounds);
            for (int k = 0; k < 3; ++k) {      // new instances (the error scan runs in parallel)
                var u = new TerrainGridTin { Grid = g, Epsilon = 0.1, Breaklines = { Circle(g, 20) } };
                Assert.True(u.Compute());
                AssertSameMesh(first, u.Result);
            }
        }

        static void AssertSameMesh(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a.VertexCount, b.VertexCount);
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            for (int v = 0; v < a.MaxVertexID; ++v) Assert.Equal(a.GetVertex(v), b.GetVertex(v));
            for (int t = 0; t < a.MaxTriangleID; ++t) Assert.Equal(a.GetTriangle(t), b.GetTriangle(t));
        }

        // ---- found by the independent review ----

        static double CoveredArea(DMesh3 tin) => tin.TriangleIndices().Sum(tr => { Index3i tv = tin.GetTriangle(tr); Vector3d a = tin.GetVertex(tv.a), b = tin.GetVertex(tv.b), c = tin.GetVertex(tv.c); return 0.5 * ((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)); });

        static int CellsWithData(HeightGrid g)
        {
            int cells = 0;
            for (int j = 0; j < g.NY - 1; ++j) for (int i = 0; i < g.NX - 1; ++i)
                if (HeightGrid.IsData(g[i, j]) && HeightGrid.IsData(g[i + 1, j]) && HeightGrid.IsData(g[i, j + 1]) && HeightGrid.IsData(g[i + 1, j + 1])) ++cells;
            return cells;
        }

        static void AssertCoversExactlyTheData(HeightGrid g, DMesh3 tin)
        {
            foreach (int tr in tin.TriangleIndices()) {
                Vector3d c = tin.GetTriCentroid(tr);
                Assert.False(double.IsNaN(g.Height(c.x, c.y)), "a triangle over a cell without data at " + c.x + "," + c.y);
            }
            Assert.Equal(CellsWithData(g) * g.CellSize * g.CellSize, CoveredArea(tin), 6);
        }

        [Fact]
        public void Gaps_ThatTouchTheRimAndAreCutByTheHull_AreNotMeshed()
        {
            // two no-data nodes at the rim: the component of cells without data is cut into several faces by the hull
            var g = new HeightGrid(Enumerable.Range(0, 16).Select(k => (double)(k % 4 + k / 4)).ToArray(), 4, 4, 0, 0, 1);
            g[1, 3] = double.NaN; g[0, 2] = double.NaN;
            TerrainGridTin t = Run(g, 0.3);
            AssertCoversExactlyTheData(g, t.Result);
        }

        [Fact]
        public void Gaps_Random_CoverExactlyTheCellsWithData()
        {
            var rand = new Random(5);
            for (int round = 0; round < 40; ++round) {
                int n = 8 + rand.Next(8);
                HeightGrid g = Terrain(n, false, 0, 0, 1);
                int style = round % 4;
                for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i)
                    if ((style == 0 && rand.NextDouble() < 0.12) || (style == 1 && (i + j) % 5 == 0 && rand.NextDouble() < 0.5) || (style == 2 && (i + j) % 2 == 0 && rand.NextDouble() < 0.2) || (style == 3 && (i >= n - 2 || j == 0) && rand.NextDouble() < 0.5))
                        g[i, j] = double.NaN;
                if (CellsWithData(g) == 0) continue;
                var t = new TerrainGridTin { Grid = g, Epsilon = 0.4 };
                if (round % 3 == 0)
                    t.Breaklines.Add(new TerrainBreakline(new[] { new Vector2d(1.3, 1.1), new Vector2d(n - 2.2, n - 2.7), new Vector2d(n - 1.5, 1.4) }));
                Assert.True(t.Compute(), $"round {round}: {t.Status} {t.Message}");
                AssertCoversExactlyTheData(g, t.Result);
                Assert.True(t.SurfaceError <= 0.4);
                Assert.True(SampledError(g, t.Result, 500, round) <= t.SurfaceError + 1e-9);
            }
        }

        [Fact]
        public void Breaklines_ThroughAHole_AreClippedToTheData()
        {
            var g = new HeightGrid(Enumerable.Range(0, 100).Select(k => (double)(k % 10 * 0.3 + k / 10 * 0.2)).ToArray(), 10, 10, 0, 0, 1);
            g[4, 4] = double.NaN;
            // the end points and the middle are in cells with data, the segment crosses the cells around the no-data node (3..5)
            var line = new TerrainBreakline(new[] { new Vector2d(1, 4.5), new Vector2d(9, 4.5) });
            TerrainGridTin t = Run(g, 0.05, line);
            Assert.True(t.DroppedBreaklineSegments >= 1);
            AssertCoversExactlyTheData(g, t.Result);
        }

        [Theory]
        [InlineData(300, false)]
        [InlineData(400, true)]
        public void LongSteps_ConvergeInFewRounds(int n, bool diagonal)
        {
            var z = new double[n * n];
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) z[i + n * j] = (diagonal ? i - j > 3 : i > n / 3) ? 5 : 0;
            var g = new HeightGrid(z, n, n, 0, 0, 1);
            TerrainGridTin t = Run(g, 0.1);
            Assert.True(t.Rounds <= 40, "rounds " + t.Rounds);
            Assert.True(t.SurfaceError <= 0.1);
            Assert.True(t.Result.VertexCount < 4000);
        }

        /// <summary>A random small terrain with crossing breaklines: a crease request meets a breakline segment that is split in the same round.</summary>
        static TerrainGridTin ConflictCase(int seed)
        {
            var r = new Random(seed);
            int n = 6 + r.Next(10);
            var z = new double[n * n];
            int style = seed % 3;
            for (int k = 0; k < z.Length; ++k) z[k] = style == 0 ? r.NextDouble() * 4 : style == 1 ? (r.Next(3) == 0 ? 3 : 0) : Math.Round(r.NextDouble()) * 2;
            var g = new HeightGrid(z, n, n, 0, 0, 1);
            var t = new TerrainGridTin { Grid = g, Epsilon = 0.05 + r.NextDouble() * 0.3 };
            int nl = 1 + r.Next(3);
            for (int l = 0; l < nl; ++l) {
                int np = 2 + r.Next(3);
                t.Breaklines.Add(new TerrainBreakline(Enumerable.Range(0, np).Select(_ => new Vector2d(0.2 + r.NextDouble() * (n - 1.4), 0.2 + r.NextDouble() * (n - 1.4)))));
            }
            return t;
        }

        [Theory]
        [InlineData(1, 10, 76, 125)]
        [InlineData(3, 9, 80, 130)]
        [InlineData(4, 11, 223, 401)]
        public void CreaseConflicts_AreRetriedInTheNextRound_AndTheGuaranteeHolds(int seed, int rounds, int vertices, int triangles)
        {
            TerrainGridTin t = ConflictCase(seed);
            Assert.True(t.Compute(), t.Status + " " + t.Message);
            Assert.True(t.CreaseConflicts > 0, "the case no longer reaches the guard (a crease request next to a segment split in the same round)");
            // golden counts: deferring the crease to the next round (the guard) changes the course of the rounds; without it the case ends in fewer rounds with other counts
            Assert.Equal((rounds, vertices, triangles), (t.Rounds, t.Result.VertexCount, t.Result.TriangleCount));
            HeightGrid g = t.Grid;
            Assert.True(t.SurfaceError <= t.Epsilon);
            TerrainGridTin.ErrorOfTin(g, t.Result, out _, out double surf);
            Assert.True(surf <= t.Epsilon);
            AssertHeightField(t.Result);
            AssertCoversExactlyTheData(g, t.Result);
            Assert.True(SampledError(g, t.Result, 2000, seed) <= surf + 1e-9);
            // every breakline segment is still an edge chain
            foreach (TerrainBreakline line in t.Breaklines)
                for (int k = 0; k + 1 < line.Points.Count; ++k)
                    Assert.Contains(t.BreaklineEdges, e => {
                        Vector3d a = t.Result.GetVertex(e.a), b = t.Result.GetVertex(e.b);
                        return DistanceToSegment(new Vector2d(a.x, a.y), line.Points[k], line.Points[k + 1]) < 1e-9 && DistanceToSegment(new Vector2d(b.x, b.y), line.Points[k], line.Points[k + 1]) < 1e-9;
                    });
        }

        // ---- breaklines with heights from the input (survey lines, D4 FromInput) ----

        /// <summary>A smooth terrain without cliffs, so that a survey line with vertices every half cell stays within a few 1e-3 of the grid surface.</summary>
        static HeightGrid Smooth(int n, double x0 = 100, double y0 = 200, double cell = 1)
        {
            var g = new HeightGrid(new double[n * n], n, n, x0, y0, cell);
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) g[i, j] = 3 * Math.Sin(i * 0.1) * Math.Cos(j * 0.13) + 0.2 * Math.Sin(0.3 * i + 0.2 * j);
            return g;
        }

        /// <summary>A survey line from (a) to (b) in world coordinates (a multiple of 1/64 of a cell, so that it is exact under 2^k scaling), a vertex every half cell, z = grid surface + offset.</summary>
        static TerrainBreakline Survey(HeightGrid g, Vector2d a, Vector2d b, double offset, TerrainBreaklineHeights heights = TerrainBreaklineHeights.FromInput)
        {
            int k = (int)Math.Ceiling((b - a).Length / (0.5 * g.CellSize));
            var pts = new List<Vector3d>();
            for (int q = 0; q <= k; ++q) {
                double x = g.X0 + Math.Round((a.x - g.X0 + (b.x - a.x) * q / k) / g.CellSize * 64) / 64 * g.CellSize, y = g.Y0 + Math.Round((a.y - g.Y0 + (b.y - a.y) * q / k) / g.CellSize * 64) / 64 * g.CellSize;
                pts.Add(new Vector3d(x, y, g.Height(x, y) + offset));
            }
            return new TerrainBreakline(pts, heights);
        }

        /// <summary>The exact maximum of |survey line - S| over the line: S is piecewise linear along it with breaks at the grid lines and diagonals, the line is linear between its vertices.</summary>
        static double SurveyDeviation(HeightGrid g, TerrainBreakline line)
        {
            double worst = 0;
            for (int k = 0; k + 1 < line.Points.Count; ++k) {
                Vector2d p = line.Points[k], q = line.Points[k + 1];
                var ts = new List<double> { 0, 1 };
                double[] f0 = { (p.x - g.X0) / g.CellSize, (p.y - g.Y0) / g.CellSize, (p.x - g.X0) / g.CellSize - (p.y - g.Y0) / g.CellSize };
                double[] f1 = { (q.x - g.X0) / g.CellSize, (q.y - g.Y0) / g.CellSize, (q.x - g.X0) / g.CellSize - (q.y - g.Y0) / g.CellSize };
                for (int c = 0; c < 3; ++c) {
                    double lo = Math.Min(f0[c], f1[c]), hi = Math.Max(f0[c], f1[c]);
                    if (f1[c] != f0[c]) for (double v = Math.Floor(lo) + 1; v < hi; ++v) ts.Add((v - f0[c]) / (f1[c] - f0[c]));
                }
                foreach (double t in ts) {
                    double x = p.x + t * (q.x - p.x), y = p.y + t * (q.y - p.y);
                    double sz = g.Height(x, y);
                    if (double.IsNaN(sz)) continue;
                    worst = Math.Max(worst, Math.Abs(line.Z[k] + t * (line.Z[k + 1] - line.Z[k]) - sz));
                }
            }
            return worst;
        }

        static readonly Vector2d SurveyFrom = new Vector2d(110.3, 212.4), SurveyTo = new Vector2d(150.7, 243.9);

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.03)]
        [InlineData(-0.07)]
        public void FromInput_TheTinFollowsTheSurveyLine_AndTheGuaranteeHolds(double offset)
        {
            HeightGrid g = Smooth(65);
            TerrainBreakline line = Survey(g, SurveyFrom, SurveyTo, offset);
            TerrainGridTin t = Run(g, 0.1, line);
            Assert.InRange(t.SurfaceError, 0.0, 0.1);
            double dev = SurveyDeviation(g, line);
            Assert.True(t.BreaklineError >= dev && t.BreaklineError <= dev + 1e-9, $"BreaklineError {t.BreaklineError:R} exact {dev:R}");
            Assert.True(t.SurfaceError >= t.BreaklineError);
            AssertHeightField(t.Result);
            // every breakline vertex of the result has the survey height, and the edges between them are straight in 3D
            foreach (Index2i e in t.BreaklineEdges) {
                Vector3d a = t.Result.GetVertex(e.a), b = t.Result.GetVertex(e.b);
                int k = Enumerable.Range(0, line.Points.Count - 1).First(q => DistanceToSegment(new Vector2d(a.x, a.y), line.Points[q], line.Points[q + 1]) < 1e-9 && DistanceToSegment(new Vector2d(b.x, b.y), line.Points[q], line.Points[q + 1]) < 1e-9);
                foreach (Vector3d p in new[] { a, b }) {
                    double s = (new Vector2d(p.x, p.y) - line.Points[k]).Dot(line.Points[k + 1] - line.Points[k]) / (line.Points[k + 1] - line.Points[k]).LengthSquared;
                    Assert.Equal(line.Z[k] + s * (line.Z[k + 1] - line.Z[k]), p.z, 9);
                }
            }
            foreach (Vector3d p in line.Points.Select((q, i) => new Vector3d(q.x, q.y, line.Z[i])))
                Assert.Contains(t.Result.Vertices(), v => Math.Abs(v.x - p.x) < 1e-9 && Math.Abs(v.y - p.y) < 1e-9 && v.z == p.z);
            TerrainGridTin.ErrorOfTin(g, t.Result, out double node, out double surf);
            Assert.True(surf <= t.SurfaceError && node <= surf);
            Assert.True(SampledError(g, t.Result, 3000, 11) <= t.SurfaceError + 1e-9);
        }

        [Fact]
        public void FromInput_ALineFurtherThanEpsilonFromTheGrid_IsReported_NotHidden()
        {
            HeightGrid g = Smooth(65);
            TerrainBreakline line = Survey(g, SurveyFrom, SurveyTo, 0.3);       // 3 x Epsilon above the surface
            var t = new TerrainGridTin { Grid = g, Epsilon = 0.1, Breaklines = { line } };
            Assert.False(t.Compute());
            Assert.Equal(TerrainGridTinStatus.BreaklineHeightDeviates, t.Status);
            Assert.NotNull(t.Result);
            Assert.InRange(t.BreaklineError, 0.29, 0.31);
            Assert.True(t.SurfaceError >= t.BreaklineError && t.SurfaceError <= t.BreaklineError + 1e-9, "the surface error is the deviation of the line, nowhere else is it above Epsilon");
            AssertHeightField(t.Result);
            // the same line as FromGrid has the guarantee, and the line is moved to the surface
            TerrainGridTin grid = Run(g, 0.1, Survey(g, SurveyFrom, SurveyTo, 0.3, TerrainBreaklineHeights.FromGrid));
            Assert.Equal(0, grid.BreaklineError);
            Assert.True(grid.SurfaceError <= 0.1);
        }

        [Fact]
        public void FromInput_TheZOfAFromGridBreaklineIsIgnored_AndMixedLinesWork()
        {
            HeightGrid g = Smooth(65);
            TerrainBreakline plain = Survey(g, new Vector2d(112, 240), new Vector2d(150, 215), 5, TerrainBreaklineHeights.FromGrid);      // z is 5 too high and ignored
            Assert.Null(plain.Z);
            var a = new TerrainBreakline(plain.Points);
            TerrainGridTin t1 = Run(g, 0.1, a), t2 = Run(g, 0.1, plain);
            AssertSameMesh(t1.Result, t2.Result);
            TerrainGridTin mixed = Run(g, 0.1, plain, Survey(g, SurveyFrom, SurveyTo, 0.02));
            Assert.True(mixed.SurfaceError <= 0.1);
            Assert.InRange(mixed.BreaklineError, 0.0, 0.1);
            AssertHeightField(mixed.Result);
            Assert.True(SampledError(g, mixed.Result, 3000, 12) <= mixed.SurfaceError + 1e-9);
        }

        /// <summary>The survey height of a line at a point on it (null if the point is not on the line).</summary>
        static double? SurveyZAt(TerrainBreakline line, double x, double y)
        {
            for (int k = 0; k + 1 < line.Points.Count; ++k) {
                Vector2d p = line.Points[k], q = line.Points[k + 1];
                if (DistanceToSegment(new Vector2d(x, y), p, q) > 1e-7) continue;
                double s = (new Vector2d(x, y) - p).Dot(q - p) / (q - p).LengthSquared;
                return line.Z[k] + s * (line.Z[k + 1] - line.Z[k]);
            }
            return null;
        }

        [Fact]
        public void FromInput_CrossingLinesWithDifferentHeights_TheLowestIndexWinsAtTheCrossing()
        {
            HeightGrid g = Smooth(65);
            // two lines crossing near world (130, 225): the first 0.02 above the surface, the second 0.06 above
            TerrainBreakline l1 = Survey(g, new Vector2d(115, 210), new Vector2d(145, 240), 0.02), l2 = Survey(g, new Vector2d(115, 240), new Vector2d(145, 210), 0.06);
            foreach (var (first, second) in new[] { (l1, l2), (l2, l1) }) {
                TerrainGridTin t = Run(g, 0.1, first, second);
                Assert.True(t.SurfaceError <= 0.1);
                // the vertex both lines share: not a vertex of either input
                Vector3d at = t.Result.Vertices().Single(v => SurveyZAt(first, v.x, v.y) != null && SurveyZAt(second, v.x, v.y) != null);
                Assert.Equal(SurveyZAt(first, at.x, at.y).Value, at.z, 9);
                Assert.True(Math.Abs(SurveyZAt(first, at.x, at.y).Value - SurveyZAt(second, at.x, at.y).Value) > 0.03);
                // the second line bends to the first at the crossing, which its deviation shows
                Assert.True(t.BreaklineError >= 0.05 && t.BreaklineError <= 0.1);
            }
        }

        [Fact]
        public void FromInput_LinesSharingAVertexWithDifferentHeights_TheFirstGivenWins()
        {
            HeightGrid g = Smooth(65);
            var mid = new Vector2d(130.5, 225.5);
            TerrainBreakline Join(Vector2d a, Vector2d b, double off)
            {
                TerrainBreakline h1 = Survey(g, a, mid, off), h2 = Survey(g, mid, b, off);
                return new TerrainBreakline(h1.Points.Select((p, i) => new Vector3d(p.x, p.y, h1.Z[i])).Concat(h2.Points.Skip(1).Select((p, i) => new Vector3d(p.x, p.y, h2.Z[i + 1]))), TerrainBreaklineHeights.FromInput);
            }
            TerrainBreakline l1 = Join(new Vector2d(115, 210), new Vector2d(145, 240), 0.02), l2 = Join(new Vector2d(115, 240), new Vector2d(145, 210), 0.06);
            foreach (var (first, second, z) in new[] { (l1, l2, g.Height(mid.x, mid.y) + 0.02), (l2, l1, g.Height(mid.x, mid.y) + 0.06) }) {
                TerrainGridTin t = Run(g, 0.1, first, second);
                Assert.True(t.SurfaceError <= 0.1);
                Assert.All(t.Result.Vertices().Where(v => v.x == mid.x && v.y == mid.y), v => Assert.Equal(z, v.z, 12));
                Assert.Single(t.Result.Vertices().Where(v => v.x == mid.x && v.y == mid.y));
            }
        }

        [Fact]
        public void FromInput_WithGapsAndNonFiniteHeights()
        {
            HeightGrid g = Smooth(65);
            for (int j = 20; j <= 30; ++j) for (int i = 20; i <= 30; ++i) g[i, j] = double.NaN;
            TerrainGridTin t = Run(g, 0.1, Survey(g, new Vector2d(105, 205), new Vector2d(118, 245), 0.01), Survey(g, new Vector2d(135, 205), new Vector2d(150, 245), 0.01));
            AssertCoversExactlyTheData(g, t.Result);
            Assert.True(t.SurfaceError <= 0.1);
            var bad = new TerrainGridTin { Grid = g, Breaklines = { new TerrainBreakline(new[] { new Vector3d(105, 205, 1), new Vector3d(118, 245, double.NaN) }, TerrainBreaklineHeights.FromInput) } };
            Assert.False(bad.Compute());
            Assert.Equal(TerrainGridTinStatus.InvalidInput, bad.Status);
            // a non-finite z of a FromGrid line is ignored
            Assert.True(new TerrainGridTin { Grid = g, Breaklines = { new TerrainBreakline(new[] { new Vector3d(105, 205, double.NaN), new Vector3d(118, 245, 0) }) } }.Compute());
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void FromInput_Invariance_ScaleAndTranslation(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) return;
            HeightGrid g0 = Smooth(49, 12.5, -7.25, 0.5);
            TerrainBreakline l0 = Survey(g0, new Vector2d(15, -4), new Vector2d(33, 14), 0.02);
            HeightGrid Map(HeightGrid g) => new HeightGrid(g.Z.Select(v => s.Scale * v + s.Translation.z).ToArray(), g.NX, g.NY, s.Scale * g.X0 + s.Translation.x, s.Scale * g.Y0 + s.Translation.y, s.Scale * g.CellSize);
            var l = new TerrainBreakline(l0.Points.Select((p, i) => new Vector3d(s.Scale * p.x + s.Translation.x, s.Scale * p.y + s.Translation.y, s.Scale * l0.Z[i] + s.Translation.z)), TerrainBreaklineHeights.FromInput);
            TerrainGridTin t = Run(Map(g0), 0.1 * s.Scale, l);
            Assert.True(t.SurfaceError <= 0.1 * s.Scale);
            AssertHeightField(t.Result);
            if (s.IsExact) {
                TerrainGridTin t1 = Run(g0, 0.1, l0);
                Assert.Equal(t1.Result.VertexCount, t.Result.VertexCount);
                for (int v = 0; v < t1.Result.MaxVertexID; ++v) {
                    Vector3d a = t1.Result.GetVertex(v), b = t.Result.GetVertex(v);
                    Assert.Equal(s.Scale * a.x, b.x); Assert.Equal(s.Scale * a.y, b.y); Assert.Equal(s.Scale * a.z, b.z);
                }
                for (int tr = 0; tr < t1.Result.MaxTriangleID; ++tr) Assert.Equal(t1.Result.GetTriangle(tr), t.Result.GetTriangle(tr));
                Assert.InRange(Math.Abs(t.BreaklineError - s.Scale * t1.BreaklineError), 0, 1e-9 * s.Scale);      // (the rounding margin is not scale invariant)
            }
        }

        [Fact]
        public void FromInput_ComputeTwice_AndRepeated_GiveTheSameResult()
        {
            HeightGrid g = Smooth(65);
            TerrainBreakline Line() => Survey(g, SurveyFrom, SurveyTo, 0.03);
            var t = new TerrainGridTin { Grid = g, Epsilon = 0.1, Breaklines = { Line(), Survey(g, new Vector2d(115, 240), new Vector2d(145, 210), 0.01) } };
            Assert.True(t.Compute());
            DMesh3 first = t.Result; double err = t.BreaklineError;
            Assert.True(t.Compute());
            AssertSameMesh(first, t.Result);
            Assert.Equal(err, t.BreaklineError);
            for (int k = 0; k < 3; ++k) {
                var u = new TerrainGridTin { Grid = g, Epsilon = 0.1, Breaklines = { Line(), Survey(g, new Vector2d(115, 240), new Vector2d(145, 210), 0.01) } };
                Assert.True(u.Compute());
                AssertSameMesh(first, u.Result);
            }
        }

        // ---- world-coordinate rounding (UTM-sized coordinates) ----

        /// <summary>A steep, rough terrain (neighbouring nodes differ by up to 40) at the given origin.</summary>
        static HeightGrid SteepTerrain(int n, double x0, double y0, int seed)
        {
            var r = new Random(seed);
            var z = new double[n * n];
            for (int k = 0; k < z.Length; ++k) z[k] = Math.Round(r.NextDouble() * 40 * (k % 3 == 0 ? 1 : 0.2), 3);
            return new HeightGrid(z, n, n, x0, y0, 1);
        }

        [Fact]
        public void WorldRounding_IsPartOfTheMargin_SoAnEpsilonBelowItIsRejected()
        {
            // rounding x = X0 + i to a double moves a vertex by about 1e-10 at UTM; with neighbouring heights 40 apart that is a height error of about 1e-8
            double eps = 5e-9;
            var utm = new TerrainGridTin { Grid = SteepTerrain(17, 632117.25, 5712043.5, 1), Epsilon = eps };
            Assert.False(utm.Compute());
            Assert.Equal(TerrainGridTinStatus.InvalidInput, utm.Status);
            Assert.Contains("rounding margin", utm.Message);
            // the same terrain near the origin has none of it
            Assert.True(new TerrainGridTin { Grid = SteepTerrain(17, 0, 0, 1), Epsilon = eps }.Compute());
            // and a flat terrain at UTM is not affected by the origin
            Assert.True(new TerrainGridTin { Grid = new HeightGrid(new double[17 * 17], 17, 17, 632117.25, 5712043.5, 1), Epsilon = eps }.Compute());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void WorldRounding_ResultInWorldCoordinates_IsWithinTheReportedError(int seed)
        {
            HeightGrid g = SteepTerrain(33, 632117.25, 5712043.5, seed);
            var rand = new Random(seed + 100);
            var t = new TerrainGridTin { Grid = g, Epsilon = 3e-7 };
            for (int l = 0; l < 3; ++l)
                t.Breaklines.Add(new TerrainBreakline(Enumerable.Range(0, 4).Select(_ => new Vector2d(g.X0 + 1 + rand.NextDouble() * 30, g.Y0 + 1 + rand.NextDouble() * 30))));
            Assert.True(t.Compute(), t.Status + " " + t.Message);
            Assert.True(t.SurfaceError <= t.Epsilon);
            // the scan of the vertices as they are written (world coordinates)
            TerrainGridTin.ErrorOfTin(g, t.Result, out _, out double world);
            Assert.True(world <= t.SurfaceError, $"world {world:R} reported {t.SurfaceError:R}");
        }

        [Fact]
        public void Epsilon_BelowTheRoundingMargin_IsRejected()
        {
            var g = new HeightGrid(new double[50 * 50], 50, 50, 0, 0, 1);
            var t = new TerrainGridTin { Grid = g, Epsilon = 1e-15 };
            Assert.False(t.Compute());
            Assert.Equal(TerrainGridTinStatus.InvalidInput, t.Status);
            Assert.Contains("rounding margin", t.Message);
            Assert.True(new TerrainGridTin { Grid = g, Epsilon = 1e-9 }.Compute());
        }

        [Fact]
        public void ErrorOfTin_RejectsMeshesThatItCannotJudge()
        {
            HeightGrid g = Terrain(9, false, 0, 0, 1);
            DMesh3 Tri(Vector3d a, Vector3d b, Vector3d c) { var m = new DMesh3(); m.AppendVertex(a); m.AppendVertex(b); m.AppendVertex(c); m.AppendTriangle(0, 1, 2); return m; }
            Assert.Throws<ArgumentException>(() => TerrainGridTin.ErrorOfTin(g, Tri(new Vector3d(-3, -3, 0), new Vector3d(9, -3, 0), new Vector3d(2, 9, 0)), out _, out _));
            Assert.Throws<ArgumentException>(() => TerrainGridTin.ErrorOfTin(g, Tri(new Vector3d(0, 0, double.NaN), new Vector3d(4, 0, 0), new Vector3d(0, 4, 0)), out _, out _));
            Assert.Throws<ArgumentException>(() => TerrainGridTin.ErrorOfTin(g, Tri(new Vector3d(0, 0, 0), new Vector3d(1e17, 0, 0), new Vector3d(0, 4, 0)), out _, out _));
            Assert.Throws<ArgumentException>(() => TerrainGridTin.ErrorOfTin(g, Tri(new Vector3d(0, 0, 0), new Vector3d(double.PositiveInfinity, 0, 0), new Vector3d(0, 4, 0)), out _, out _));
            Assert.Throws<ArgumentNullException>(() => TerrainGridTin.ErrorOfTin(null, new DMesh3(), out _, out _));
            // a triangle on the border of the grid is fine (conversion rounding of border vertices is tolerated)
            TerrainGridTin.ErrorOfTin(g, Tri(new Vector3d(0, 0, g[0, 0]), new Vector3d(8, 0, g[8, 0]), new Vector3d(0, 8, g[0, 8])), out double node, out double surf);
            Assert.True(surf >= node);
        }

        // ---- an independent exact oracle: clip every TIN triangle against every half-cell of the grid, compare at the polygon vertices ----

        static double OracleSurfaceError(HeightGrid g, DMesh3 tin)
        {
            double worst = 0;
            foreach (int tr in tin.TriangleIndices()) {
                Index3i tv = tin.GetTriangle(tr);
                Vector3d[] p = { tin.GetVertex(tv.a), tin.GetVertex(tv.b), tin.GetVertex(tv.c) };
                double det = (p[1].x - p[0].x) * (p[2].y - p[0].y) - (p[2].x - p[0].x) * (p[1].y - p[0].y);
                double Tz(double x, double y)
                {
                    double l1 = ((x - p[0].x) * (p[2].y - p[0].y) - (p[2].x - p[0].x) * (y - p[0].y)) / det;
                    double l2 = ((p[1].x - p[0].x) * (y - p[0].y) - (x - p[0].x) * (p[1].y - p[0].y)) / det;
                    return p[0].z + l1 * (p[1].z - p[0].z) + l2 * (p[2].z - p[0].z);
                }
                int i0 = Math.Max(0, (int)Math.Floor(Math.Min(p[0].x, Math.Min(p[1].x, p[2].x))) - 1), i1 = Math.Min(g.NX - 2, (int)Math.Ceiling(Math.Max(p[0].x, Math.Max(p[1].x, p[2].x))));
                int j0 = Math.Max(0, (int)Math.Floor(Math.Min(p[0].y, Math.Min(p[1].y, p[2].y))) - 1), j1 = Math.Min(g.NY - 2, (int)Math.Ceiling(Math.Max(p[0].y, Math.Max(p[1].y, p[2].y))));
                for (int j = j0; j <= j1; ++j)
                    for (int i = i0; i <= i1; ++i)
                        for (int half = 0; half < 2; ++half) {
                            // the half-cell triangle (counter-clockwise) and the plane of the grid surface on it
                            Vector3d[] h = half == 0
                                ? new[] { new Vector3d(i, j, g[i, j]), new Vector3d(i + 1, j, g[i + 1, j]), new Vector3d(i + 1, j + 1, g[i + 1, j + 1]) }
                                : new[] { new Vector3d(i, j, g[i, j]), new Vector3d(i + 1, j + 1, g[i + 1, j + 1]), new Vector3d(i, j + 1, g[i, j + 1]) };
                            if (h.Any(v => double.IsNaN(v.z))) continue;
                            double hdet = (h[1].x - h[0].x) * (h[2].y - h[0].y) - (h[2].x - h[0].x) * (h[1].y - h[0].y);
                            double Sz(double x, double y)
                            {
                                double l1 = ((x - h[0].x) * (h[2].y - h[0].y) - (h[2].x - h[0].x) * (y - h[0].y)) / hdet;
                                double l2 = ((h[1].x - h[0].x) * (y - h[0].y) - (x - h[0].x) * (h[1].y - h[0].y)) / hdet;
                                return h[0].z + l1 * (h[1].z - h[0].z) + l2 * (h[2].z - h[0].z);
                            }
                            // clip the TIN triangle by the three half planes of the half-cell
                            var poly = new List<Vector2d> { new Vector2d(p[0].x, p[0].y), new Vector2d(p[1].x, p[1].y), new Vector2d(p[2].x, p[2].y) };
                            if (det < 0) poly.Reverse();
                            for (int e = 0; e < 3 && poly.Count > 0; ++e) {
                                Vector2d a = new Vector2d(h[e].x, h[e].y), b = new Vector2d(h[(e + 1) % 3].x, h[(e + 1) % 3].y);
                                var next = new List<Vector2d>();
                                for (int k = 0; k < poly.Count; ++k) {
                                    Vector2d u = poly[k], v = poly[(k + 1) % poly.Count];
                                    double du = (b.x - a.x) * (u.y - a.y) - (b.y - a.y) * (u.x - a.x), dv = (b.x - a.x) * (v.y - a.y) - (b.y - a.y) * (v.x - a.x);
                                    if (du >= 0) next.Add(u);
                                    if ((du > 0 && dv < 0) || (du < 0 && dv > 0)) next.Add(u + (du / (du - dv)) * (v - u));
                                }
                                poly = next;
                            }
                            foreach (Vector2d v in poly) worst = Math.Max(worst, Math.Abs(Tz(v.x, v.y) - Sz(v.x, v.y)));
                        }
            }
            return worst;
        }

        [Fact]
        public void ErrorOfTin_EqualsTheClippingOracle()
        {
            HeightGrid g = Terrain(33, false, 0, 0, 1);
            var greedy = new TerrainGreedyTIN((double[])g.Z.Clone(), 33, 33, 0, 0, 1) { MaxError = 0.3 };
            Assert.True(greedy.Compute());
            TerrainGridTin.ErrorOfTin(g, greedy.Result, out _, out double surf);
            Assert.Equal(OracleSurfaceError(g, greedy.Result), surf, 9);
            Assert.True(surf > 0.45);                     // the oracle case is one where the node error (0.3) is not the surface error
            TerrainGridTin t = Run(g, 0.2, Circle(g, 12, 3));
            TerrainGridTin.ErrorOfTin(g, t.Result, out _, out double surf2);
            double oracle = OracleSurfaceError(g, t.Result);
            Assert.Equal(oracle, surf2, 9);
            Assert.True(oracle <= 0.2 && t.SurfaceError >= oracle - 1e-12);
            // random triangles with arbitrary (non-grid) vertices
            var rand = new Random(9);
            for (int k = 0; k < 20; ++k) {
                var m = new DMesh3();
                for (int c = 0; c < 3; ++c) m.AppendVertex(new Vector3d(1 + rand.NextDouble() * 30, 1 + rand.NextDouble() * 30, rand.NextDouble() * 4 - 2));
                m.AppendTriangle(0, 1, 2);
                TerrainGridTin.ErrorOfTin(g, m, out _, out double s3);
                Assert.Equal(OracleSurfaceError(g, m), s3, 9);
            }
        }

        [Fact]
        public void Compute_InParallelThreads_GivesTheSameResult()
        {
            HeightGrid g = Terrain(65, true);
            var results = new DMesh3[4];
            System.Threading.Tasks.Parallel.For(0, 4, k => {
                var t = new TerrainGridTin { Grid = g, Epsilon = 0.15, Breaklines = { Circle(g, 12) } };
                Assert.True(t.Compute());
                results[k] = t.Result;
            });
            for (int k = 1; k < 4; ++k) AssertSameMesh(results[0], results[k]);
        }

        // ---- statuses ----

        [Fact]
        public void Statuses()
        {
            HeightGrid g = Terrain(17, false);
            Assert.Equal(TerrainGridTinStatus.InvalidInput, Status(new TerrainGridTin { Grid = null }));
            Assert.Equal(TerrainGridTinStatus.InvalidInput, Status(new TerrainGridTin { Grid = g, Epsilon = 0 }));
            Assert.Equal(TerrainGridTinStatus.InvalidInput, Status(new TerrainGridTin { Grid = g, Epsilon = double.NaN }));
            Assert.Equal(TerrainGridTinStatus.InvalidInput, Status(new TerrainGridTin { Grid = g, Epsilon = -1 }));
            Assert.Equal(TerrainGridTinStatus.InvalidInput, Status(new TerrainGridTin { Grid = g, MaxRounds = 0 }));
            Assert.Equal(TerrainGridTinStatus.InvalidInput, Status(new TerrainGridTin { Grid = new HeightGrid(new double[] { 1, 2 }, 2, 1, 0, 0, 1) }));
            Assert.Equal(TerrainGridTinStatus.Cancelled, Status(new TerrainGridTin { Grid = g, CancelF = () => true }));
            // too few rounds: the last TIN is returned with the error it has
            var few = new TerrainGridTin { Grid = Terrain(33, false), Epsilon = 0.01, MaxRounds = 1 };
            Assert.False(few.Compute());
            Assert.Equal(TerrainGridTinStatus.NotConverged, few.Status);
            Assert.NotNull(few.Result);
            Assert.True(few.SurfaceError > 0.01);
            AssertHeightField(few.Result);
            TerrainGridTin.ErrorOfTin(few.Grid, few.Result, out _, out double surf);
            Assert.True(surf <= few.SurfaceError + 1e-9);
        }

        static TerrainGridTinStatus Status(TerrainGridTin t) { Assert.False(t.Compute()); return t.Status; }
    }
}
