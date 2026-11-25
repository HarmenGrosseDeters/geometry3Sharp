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
    /// Regression tests for #1400: ConstrainedDelaunayMesh2 must settle on valid inputs with heavy collinear
    /// (and near-collinear) overlap instead of tripping NodingLimit on total split steps.
    /// Compiles against master without any new library hooks so CI's New tests on base catches the regression.
    /// </summary>
    public class ConstrainedDelaunayMesh2OverlapTests
    {
        sealed class Rng
        {
            ulong s;
            public Rng(ulong seed) { s = 0x9E3779B97F4A7C15UL ^ seed; }
            public double Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s >> 11) * (1.0 / (1UL << 53)); }
            public int Next(int n) => (int)(Next() * n);
        }

        /// <summary>
        /// Two crossing dyadic lines (y = 0.5, x = 0.5), each with <paramref name="perLine"/> random overlapping dyadic
        /// intervals. With perLine = 350 (700 segments), k^2 collinear splits exceed the old 64*(m+n+made)+100000 step cap
        /// (~234k) while making only 1 crossing vertex, so this fails with NodingLimit on base.
        /// </summary>
        static Mesh2Input HeavyOverlap(int perLine, bool near)
        {
            const double ulps = 1.0 / (1L << 48);
            var rng = new Rng((ulong)perLine * 31 + (near ? 19UL : 7UL));
            var xs = new List<double>(4 * perLine);
            var ys = new List<double>(4 * perLine);
            var input = new Mesh2Input();
            double Jitter() => near ? (rng.Next() - 0.5) * 2 * ulps : 0.0;
            for (int k = 0; k < 2 * perLine; ++k) {
                double u = rng.Next(1 << 20) / (double)(1 << 20);
                double v = rng.Next(1 << 20) / (double)(1 << 20);
                if (u == v) continue;
                int a = xs.Count;
                if (k % 2 == 0) {
                    xs.Add(u); ys.Add(0.5 + Jitter());
                    xs.Add(v); ys.Add(0.5 + Jitter());
                } else {
                    xs.Add(0.5 + Jitter()); ys.Add(u);
                    xs.Add(0.5 + Jitter()); ys.Add(v);
                }
                input.Segments.Add(new SegmentInput(a, a + 1, (k % 3) + 1));
            }
            input.X = xs.ToArray();
            input.Y = ys.ToArray();
            return input;
        }

        static string[] Canonical(Mesh2Output o) =>
            o.Triangles.Select(t => string.Join(" ", t)).OrderBy(s => s, StringComparer.Ordinal)
            .Concat(o.Subsegments.Select(s => "S " + Math.Min(s.A, s.B) + " " + Math.Max(s.A, s.B) + " " + s.Marker).OrderBy(s => s, StringComparer.Ordinal)).ToArray();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HeavyCollinearOverlap_SucceedsAndPassesOracle(bool near)
        {
            var input = HeavyOverlap(perLine: 400, near: near);
            var output = new ConstrainedDelaunayMesh2Candidate().Run(input);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(output.Triangles.Length > 0);
            var report = Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, "near=" + near + ": " + report);
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All2D().Select(s => new object[] { s.Name });

        [Theory]
        [MemberData(nameof(Similarities))]
        public void HeavyCollinearOverlap_Similarity_All2D(string name)
        {
            var s = Similarity.All2D().First(x => x.Name == name);
            var input = HeavyOverlap(perLine: 100, near: false);
            var e = s.Rotation * Vector3d.AxisX;
            double norm = Math.Sqrt(e.x * e.x + e.y * e.y), cos = e.x / norm, sin = e.y / norm;
            var moved = HeavyOverlap(perLine: 100, near: false);
            for (int i = 0; i < input.Count; ++i) {
                moved.X[i] = s.Scale * (cos * input.X[i] - sin * input.Y[i]) + s.Translation.x;
                moved.Y[i] = s.Scale * (sin * input.X[i] + cos * input.Y[i]) + s.Translation.y;
            }
            var a = new ConstrainedDelaunayMesh2Candidate().Run(input);
            var b = new ConstrainedDelaunayMesh2Candidate().Run(moved);
            Assert.Equal(Mesh2Status.Success, a.Status);
            Assert.Equal(Mesh2Status.Success, b.Status);
            var report = Oracle.Check(moved, b, new OracleExpect { Milestone = Milestone.M2 });
            Assert.True(report.Ok, name + ": " + report);
            if (s.IsExact) {
                Assert.Equal(Canonical(a), Canonical(b));
                for (int i = 0; i < a.VX.Length; ++i) {
                    Assert.Equal(a.VX[i] * s.Scale, b.VX[i]);
                    Assert.Equal(a.VY[i] * s.Scale, b.VY[i]);
                }
            }
        }

        [Fact]
        public void HeavyCollinearOverlap_ReuseAndParallel_AreBitIdentical()
        {
            var mesher = new ConstrainedDelaunayMesh2();
            var input = ConstrainedDelaunayMesh2Candidate.Convert(HeavyOverlap(perLine: 350, near: false));
            var first = mesher.Compute(input);
            Assert.Equal(Triangulation2Status.Success, first.Status);
            var second = mesher.Compute(input);
            Assert.Equal(Triangulation2Status.Success, second.Status);
            Assert.Equal(first.Mesh.Vertices, second.Mesh.Vertices);
            Assert.Equal(first.Mesh.Triangles, second.Mesh.Triangles);
            Assert.Equal(first.Mesh.Subsegments.Select(s => (s.A, s.B, s.Marker)), second.Mesh.Subsegments.Select(s => (s.A, s.B, s.Marker)));

            var parallel = new ConstrainedMesh2[4];
            Parallel.For(0, parallel.Length, i => parallel[i] = new ConstrainedDelaunayMesh2().Compute(input).Mesh);
            foreach (var m in parallel) {
                Assert.Equal(first.Mesh.Vertices, m.Vertices);
                Assert.Equal(first.Mesh.Triangles, m.Triangles);
                Assert.Equal(first.Mesh.Subsegments.Select(s => (s.A, s.B, s.Marker)), m.Subsegments.Select(s => (s.A, s.B, s.Marker)));
            }
        }
    }
}
