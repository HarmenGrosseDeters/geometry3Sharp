using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Tests for the hot-pixel spatial index, critical-edge deduplication, X-sweep crossings, and exact 64-bit/BigInteger
    /// contact paths in SnapRounding2 (#1402): oracle validity, large-span BigInteger fallback, Similarity.All2D, and
    /// reuse/parallel bit-identical determinism.
    /// </summary>
    public class SnapRounding2IndexTests
    {
        sealed class Rng
        {
            ulong s;
            public Rng(ulong seed) { s = 0x9E3779B97F4A7C15UL ^ seed; }
            public double Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s >> 11) * (1.0 / (1UL << 53)); }
        }

        static ConstrainedMesh2Input RandomSoup(int n, ulong seed = 1402)
        {
            var input = new ConstrainedMesh2Input();
            var rng = new Rng(seed);
            for (int i = 0; i < n; ++i) {
                int a = input.Points.Count;
                input.Points.Add(new Vector2d(rng.Next(), rng.Next()));
                input.Points.Add(new Vector2d(rng.Next(), rng.Next()));
                input.Segments.Add(new Mesh2Segment(a, a + 1, (i % 7) + 1));
            }
            return input;
        }

        static ConstrainedMesh2Input TChain(int n)
        {
            var input = new ConstrainedMesh2Input();
            double ax = 0.0, ay = 0.1, bx = 1.0, by = 0.87;
            input.Points.Add(new Vector2d(ax, ay));
            input.Points.Add(new Vector2d(bx, by));
            input.Segments.Add(new Mesh2Segment(0, 1, 1));
            for (int i = 0; i < n; ++i) {
                double t = (i + 0.5) / n, px = ax + t * (bx - ax), py = ay + t * (by - ay), side = i % 2 == 0 ? 1 : -1;
                int a = input.Points.Count;
                input.Points.Add(new Vector2d(px, py));
                input.Points.Add(new Vector2d(px - side * 0.0077, py + side * 0.01));
                input.Segments.Add(new Mesh2Segment(a, a + 1, 2));
            }
            return input;
        }

        static Mesh2Input ToOracleInput(ConstrainedMesh2Input src, double snapTol)
        {
            var pts = new double[2 * src.Points.Count];
            for (int i = 0; i < src.Points.Count; ++i) { pts[2 * i] = src.Points[i].x; pts[2 * i + 1] = src.Points[i].y; }
            var mi = Generators.Literal(pts);
            mi.SnapTolerance = snapTol;
            foreach (var s in src.Segments) mi.Segments.Add(new SegmentInput(s.A, s.B, s.Marker, s.Locked));
            return mi;
        }

        [Theory]
        [InlineData(1.0 / 1024.0)]
        [InlineData(1.0 / 1048576.0)]
        public void LongSegmentSoup_Over64Segments_PassesOracleAndMultiRoundRestore(double tol)
        {
            // n = 100 > 64 exercises the X-sweep AddCrossings path, k-d tree RouteAll, deduplicated Critical, and EdgeBVH Restore
            var input = RandomSoup(100);
            var mi = ToOracleInput(input, tol);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(output.Triangles.Length > 0);
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void TChain250_Snapped_PassesOracle()
        {
            double tol = 1.0 / 1024.0;
            var input = TChain(250);
            var mi = ToOracleInput(input, tol);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(output.Triangles.Length > 0);
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void LargePixelSpan_ExercisesBigIntegerFallbackPaths_AndPassesOracle()
        {
            // Span > 2^31 pixels (> SafeSpan = 2^29) exercises the BigInteger fallback in both Meets and MeetsPixelCentreInterior
            double tol = 1.0 / (1L << 32);
            var input = new ConstrainedMesh2Input();
            input.Points.Add(new Vector2d(0.0, 0.0));
            input.Points.Add(new Vector2d(1.0, 1.0));
            input.Points.Add(new Vector2d(0.0, 1.0));
            input.Points.Add(new Vector2d(1.0, 0.0));
            // near-collinear point half a pixel off the diagonal to trigger critical-pixel detection across > 2^31 pixels
            input.Points.Add(new Vector2d(0.5, 0.5 + 0.6 * tol));
            input.Points.Add(new Vector2d(0.25, 0.25 - 0.6 * tol));
            input.Segments.Add(new Mesh2Segment(0, 1, 1));
            input.Segments.Add(new Mesh2Segment(2, 3, 2));
            input.Segments.Add(new Mesh2Segment(4, 5, 3));

            var mi = ToOracleInput(input, tol);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All2D().Select(s => new object[] { s.Name });

        [Theory]
        [MemberData(nameof(Similarities))]
        public void LongSegmentSoup_Snapped_Similarity_All2D(string name)
        {
            var s = Similarity.All2D().First(x => x.Name == name);
            var ex = s.Rotation * Vector3d.AxisX;
            double norm = Math.Sqrt(ex.x * ex.x + ex.y * ex.y), cos = ex.x / norm, sin = ex.y / norm;
            var raw = RandomSoup(70, seed: 2026);
            var transformed = new ConstrainedMesh2Input();
            foreach (var p in raw.Points)
                transformed.Points.Add(new Vector2d(
                    s.Scale * (cos * p.x - sin * p.y) + s.Translation.x,
                    s.Scale * (sin * p.x + cos * p.y) + s.Translation.y));
            foreach (var seg in raw.Segments) transformed.Segments.Add(seg);
            // keep pixel count within 2^40 even at UTM offsets (~5e6) and scale 1e-3
            double tol = Math.Max(s.Scale / 512.0, 1e-3);
            var mi = ToOracleInput(transformed, tol);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, name + ": " + report);
        }

        [Fact]
        public void LongSegmentSoup_Snapped_ReuseAndParallel_AreBitIdentical()
        {
            double tol = 1.0 / 1024.0;
            var input = RandomSoup(80);
            var mesher = new ConstrainedDelaunayMesh2 { SnapTolerance = tol };
            var r1 = mesher.Compute(input);
            var r2 = mesher.Compute(input);
            Assert.Equal(Triangulation2Status.Success, r1.Status);
            Assert.Equal(Triangulation2Status.Success, r2.Status);
            AssertBitIdentical(r1.Mesh, r2.Mesh);

            var parallel = new ConstrainedTriangulation2Result[4];
            Parallel.For(0, 4, i => parallel[i] = new ConstrainedDelaunayMesh2 { SnapTolerance = tol }.Compute(input));
            foreach (var rp in parallel) AssertBitIdentical(r1.Mesh, rp.Mesh);
        }

        static void AssertBitIdentical(ConstrainedMesh2 a, ConstrainedMesh2 b)
        {
            Assert.Equal(a.Vertices.Count, b.Vertices.Count);
            for (int i = 0; i < a.Vertices.Count; ++i) {
                Assert.Equal(BitConverter.DoubleToInt64Bits(a.Vertices[i].x), BitConverter.DoubleToInt64Bits(b.Vertices[i].x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(a.Vertices[i].y), BitConverter.DoubleToInt64Bits(b.Vertices[i].y));
            }
            Assert.Equal(a.Triangles.Count, b.Triangles.Count);
            for (int i = 0; i < a.Triangles.Count; ++i) Assert.Equal(a.Triangles[i], b.Triangles[i]);
            Assert.Equal(a.Subsegments.Count, b.Subsegments.Count);
            for (int i = 0; i < a.Subsegments.Count; ++i) {
                Assert.Equal(a.Subsegments[i].A, b.Subsegments[i].A);
                Assert.Equal(a.Subsegments[i].B, b.Subsegments[i].B);
                Assert.Equal(a.Subsegments[i].Marker, b.Subsegments[i].Marker);
            }
        }

        [Fact]
        public void HorizontalBundle_Over64Segments_TriggersYSweepAndPassesOracle()
        {
            // 100 nearly horizontal long segments spanning x in [0, 10] with narrow y-extents (> 50% wider in X than Y)
            // triggers the Y-sweep branch in SnapRounding2.AddCrossings.
            var input = new ConstrainedMesh2Input();
            var rng = new Rng(140299);
            int n = 100;
            for (int i = 0; i < n; ++i) {
                double y0 = i * 0.05;
                double y1 = y0 + (rng.Next() - 0.5) * 0.12;
                int a = input.Points.Count;
                input.Points.Add(new Vector2d(0.0, y0));
                input.Points.Add(new Vector2d(10.0, y1));
                input.Segments.Add(new Mesh2Segment(a, a + 1, 1));
            }
            // Add a few steep crossers to produce crossings across the horizontal bundle.
            for (int k = 0; k < 4; ++k) {
                double x = 2.0 + 2.0 * k;
                int a = input.Points.Count;
                input.Points.Add(new Vector2d(x, -0.1));
                input.Points.Add(new Vector2d(x + 0.3, n * 0.05 + 0.1));
                input.Segments.Add(new Mesh2Segment(a, a + 1, 2));
            }

            double tol = 1.0 / 512.0;
            var mi = ToOracleInput(input, tol);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(output.Triangles.Length > 0);
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }
    }
}
