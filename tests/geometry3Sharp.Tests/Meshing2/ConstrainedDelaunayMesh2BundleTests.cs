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
    /// Regression, large-cavity, invariance, and reuse/parallel tests for constrained segment recovery (#1401):
    /// near-parallel crossing bundles and deep cavities with reflex chains and hanging constrained edges.
    /// </summary>
    public class ConstrainedDelaunayMesh2BundleTests
    {
        sealed class Rng
        {
            ulong s;
            public Rng(ulong seed) { s = 0x9E3779B97F4A7C15UL ^ seed; }
            public double Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s >> 11) * (1.0 / (1UL << 53)); }
            public int Next(int n) => (int)(Next() * n);
        }

        internal static ConstrainedMesh2Input Bundle(int n, double spacing = 1.0 / (1L << 30))
        {
            var input = new ConstrainedMesh2Input();
            var rng = new Rng((ulong)n * 31 + 6);
            var perm = Enumerable.Range(0, n).ToArray();
            for (int i = n - 1; i > 0; --i) { int j = rng.Next(i + 1); (perm[i], perm[j]) = (perm[j], perm[i]); }
            for (int i = 0; i < n; ++i) {
                int a = input.Points.Count;
                input.Points.Add(new Vector2d(0, 0.5 + i * spacing));
                input.Points.Add(new Vector2d(1, 0.5 + perm[i] * spacing));
                input.Segments.Add(new Mesh2Segment(a, a + 1, (i % 7) + 1));
            }
            return input;
        }

        /// <summary>
        /// Long diagonal crossing > 100 sliver triangles with alternating indentations (reflex chains) and hanging
        /// constrained spurs that enter the cavity from one side without crossing the diagonal.
        /// </summary>
        internal static ConstrainedMesh2Input DeepReflexCavityWithHangingSpurs(int ribs)
        {
            var input = new ConstrainedMesh2Input();
            int u = 0, w = 1;
            input.Points.Add(new Vector2d(0.0, 0.0));
            input.Points.Add(new Vector2d(ribs + 1.0, 0.0));
            var rng = new Rng(1401);
            // staggered upper/lower points forcing > 2*ribs crossed Delaunay edges and reflex chains
            var topIds = new int[ribs];
            var botIds = new int[ribs];
            for (int i = 0; i < ribs; ++i) {
                double x = i + 1.0;
                double yTop = (i % 3 == 1 ? 0.12 : 0.85) + 0.02 * rng.Next();
                double yBot = -(i % 3 == 2 ? 0.14 : 0.88) - 0.02 * rng.Next();
                topIds[i] = input.Points.Count;
                input.Points.Add(new Vector2d(x + 0.05 * (rng.Next() - 0.5), yTop));
                botIds[i] = input.Points.Count;
                input.Points.Add(new Vector2d(x + 0.15 + 0.05 * (rng.Next() - 0.5), yBot));
            }
            // add hanging constrained spurs on the upper and lower sides (near the x-axis but never crossing y = 0)
            for (int i = 2; i + 2 < ribs; i += 6) {
                int tipTop = input.Points.Count;
                input.Points.Add(new Vector2d(i + 1.5, 0.04 + 0.005 * rng.Next()));
                input.Segments.Add(new Mesh2Segment(topIds[i], tipTop, 2));
                int tipBot = input.Points.Count;
                input.Points.Add(new Vector2d(i + 1.6, -0.04 - 0.005 * rng.Next()));
                input.Segments.Add(new Mesh2Segment(botIds[i], tipBot, 3));
            }
            // insert the long horizontal segment last so it crosses all ~2*ribs Delaunay edges at once
            input.Segments.Add(new Mesh2Segment(u, w, 1));
            return input;
        }

        static Mesh2Input ToOracleInput(ConstrainedMesh2Input src)
        {
            var pts = new double[2 * src.Points.Count];
            for (int i = 0; i < src.Points.Count; ++i) { pts[2 * i] = src.Points[i].x; pts[2 * i + 1] = src.Points[i].y; }
            var mi = Generators.Literal(pts);
            foreach (var s in src.Segments) mi.Segments.Add(new SegmentInput(s.A, s.B, s.Marker, s.Locked));
            return mi;
        }

        [Fact]
        public void Bundle100_SucceedsAndPassesOracle()
        {
            var input = Bundle(100);
            var mi = ToOracleInput(input);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(output.Triangles.Length > 0);
            for (int i = 0; i < output.VX.Length; ++i) {
                Assert.False(double.IsNaN(output.VX[i]) || double.IsInfinity(output.VX[i]));
                Assert.False(double.IsNaN(output.VY[i]) || double.IsInfinity(output.VY[i]));
            }
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void DeepReflexCavityWithHangingSpurs_SucceedsAndPassesOracle()
        {
            var input = DeepReflexCavityWithHangingSpurs(120);
            var mi = ToOracleInput(input);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(output.Triangles.Length > 200);
            var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, report.ToString());
        }

        [Theory]
        [MemberData(nameof(Similarities))]
        public void BundleAndDeepCavity_Similarity_All2D(string name)
        {
            var s = Similarity.All2D().First(x => x.Name == name);
            var ex = s.Rotation * Vector3d.AxisX;
            double norm = Math.Sqrt(ex.x * ex.x + ex.y * ex.y), cos = ex.x / norm, sin = ex.y / norm;
            foreach (var raw in new[] { Bundle(40, spacing: 1e-2), DeepReflexCavityWithHangingSpurs(80) }) {
                var transformed = new ConstrainedMesh2Input();
                foreach (var p in raw.Points)
                    transformed.Points.Add(new Vector2d(
                        s.Scale * (cos * p.x - sin * p.y) + s.Translation.x,
                        s.Scale * (sin * p.x + cos * p.y) + s.Translation.y));
                foreach (var seg in raw.Segments) transformed.Segments.Add(seg);
                var mi = ToOracleInput(transformed);
                var output = new ConstrainedDelaunayMesh2Candidate().Run(mi);
                Assert.Equal(Mesh2Status.Success, output.Status);
                var report = Oracle.Check(mi, output, new OracleExpect { Milestone = Milestone.M2 });
                Assert.True(report.Ok, name + ": " + report);
                if (name == "scale 2^-10" || name == "scale 2^10") {
                    var idRes = new ConstrainedDelaunayMesh2().Compute(raw);
                    var scRes = new ConstrainedDelaunayMesh2().Compute(transformed);
                    Assert.Equal(Triangulation2Status.Success, idRes.Status);
                    Assert.Equal(Triangulation2Status.Success, scRes.Status);
                    Assert.Equal(idRes.Mesh.Vertices.Count, scRes.Mesh.Vertices.Count);
                    for (int i = 0; i < idRes.Mesh.Vertices.Count; ++i) {
                        Assert.Equal(BitConverter.DoubleToInt64Bits(s.Scale * idRes.Mesh.Vertices[i].x), BitConverter.DoubleToInt64Bits(scRes.Mesh.Vertices[i].x));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(s.Scale * idRes.Mesh.Vertices[i].y), BitConverter.DoubleToInt64Bits(scRes.Mesh.Vertices[i].y));
                    }
                    Assert.Equal(idRes.Mesh.Triangles.Count, scRes.Mesh.Triangles.Count);
                    for (int i = 0; i < idRes.Mesh.Triangles.Count; ++i) Assert.Equal(idRes.Mesh.Triangles[i], scRes.Mesh.Triangles[i]);
                    Assert.Equal(idRes.Mesh.Subsegments.Count, scRes.Mesh.Subsegments.Count);
                    for (int i = 0; i < idRes.Mesh.Subsegments.Count; ++i) {
                        Assert.Equal(idRes.Mesh.Subsegments[i].A, scRes.Mesh.Subsegments[i].A);
                        Assert.Equal(idRes.Mesh.Subsegments[i].B, scRes.Mesh.Subsegments[i].B);
                        Assert.Equal(idRes.Mesh.Subsegments[i].Marker, scRes.Mesh.Subsegments[i].Marker);
                    }
                }
            }
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All2D().Select(s => new object[] { s.Name });

        [Fact]
        public void BundleAndDeepCavity_ReuseAndParallel_AreBitIdentical()
        {
            var inputs = new[] { Bundle(50), DeepReflexCavityWithHangingSpurs(90) };
            var mesher = new ConstrainedDelaunayMesh2();
            foreach (var input in inputs) {
                var r1 = mesher.Compute(input);
                var r2 = mesher.Compute(input);
                Assert.Equal(Triangulation2Status.Success, r1.Status);
                Assert.Equal(Triangulation2Status.Success, r2.Status);
                AssertBitIdentical(r1.Mesh, r2.Mesh);

                var parallel = new ConstrainedTriangulation2Result[4];
                Parallel.For(0, 4, i => parallel[i] = new ConstrainedDelaunayMesh2().Compute(input));
                foreach (var rp in parallel) AssertBitIdentical(r1.Mesh, rp.Mesh);
            }
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
    }
}
