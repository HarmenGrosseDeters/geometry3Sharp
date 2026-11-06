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
    /// The small-angle rule of the constrained-quality mode, through the public API (MESHING2D_SMALL_ANGLES.md, owner decision
    /// 2026-11-06): a subsegment is not split for a vertex that refinement made on exactly one partner source of a small input angle,
    /// at the apex region. Without it a hull edge at a small angle to a segment drove a cascade of splits towards the apex: ext/cdt/island
    /// gave 8 879 triangles at 30 degrees, 2 971 with the rule; the wedge below about 30 times as many as with it. Intentional behaviour
    /// change: the output of QualityMesh2 with Conforming = false and MinAngle &gt; 0.
    /// </summary>
    public class QualityMesh2SmallAngleRuleTests
    {
        /// <summary> island-like: a segment from the hull corner A = (0, 0) to P = (8, 8 tan(degrees)), at that angle to the hull edge A-B
        /// (B = (10, 0); the third hull point (0, 6) makes the other angles 59 degrees and more, but 31 at B); no ring, so the domain is the
        /// hull and its edges are constraints (D13) </summary>
        internal static Mesh2Input HullWedge(double angle, double degrees = 2.6, double snap = 0)
        {
            double t = Math.Tan(degrees * Math.PI / 180);
            var input = Generators.Literal(0, 0, 10, 0, 0, 6, 8, Math.Round(8 * t, 12));
            input.Segments.Add(new SegmentInput(0, 3));
            input.MinAngleDeg = angle;
            input.ConstrainedQuality = true;
            input.SnapTolerance = snap;
            return input;
        }

        static void AssertValid(Mesh2Input input, Mesh2Output output, string what, OracleExpect expect = null)
        {
            Assert.True(output.Status == Mesh2Status.Success, what + ": " + output.Status + " " + output.FailureKind);
            Assert.False(output.SteinerLimitReached, what + ": the Steiner limit was reached (the angle check would be skipped)");
            var report = Oracle.Check(input, output, expect ?? new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, what + ": " + report);
        }

        /// <summary> the wedge at 2.6 and 1 degrees, 20 (lenses) and 30 degrees: valid, and a few dozen triangles at most (master: 59 to
        /// 430, the cascade towards the apex; with the rule 11 to 22) </summary>
        [Theory]
        [InlineData(2.6, 20)]
        [InlineData(2.6, 30)]
        [InlineData(1.0, 20)]
        [InlineData(1.0, 30)]
        public void HullWedge_HasNoCascade(double degrees, double angle)
        {
            var input = HullWedge(angle, degrees);
            var output = new QualityMesh2Candidate().Run(input);
            AssertValid(input, output, degrees + " at " + angle);
            Assert.True(output.Triangles.Length <= 30, degrees + " at " + angle + ": " + output.Triangles.Length + " triangles");
        }

        /// <summary> the wedge on a snap grid (a pixel of 2^-8), where its chains share pixels near the apex: valid (no pinch, no
        /// off-grid vertex), and still no cascade (master: 57 and 178 triangles) </summary>
        [Theory]
        [InlineData(20)]
        [InlineData(30)]
        public void HullWedge_OnTheGrid_IsValid(double angle)
        {
            var input = HullWedge(angle, 2.6, 1.0 / 256);
            var output = new QualityMesh2Candidate().Run(input);
            AssertValid(input, output, "grid at " + angle);
            Assert.True(output.Triangles.Length <= 40, "grid at " + angle + ": " + output.Triangles.Length + " triangles");
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary> the wedge under every similarity: the others pass the oracle, the 2^k scales give the same mesh bit for bit </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void HullWedge_UnderEverySimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var plain = HullWedge(30);
            var moved = HullWedge(30);
            for (int i = 0; i < plain.Count; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            var b = new QualityMesh2Candidate().Run(moved);
            AssertValid(moved, b, name);
            if (s.IsExact) {
                var a = new QualityMesh2Candidate().Run(plain);
                Assert.Equal(a.VX.Select(v => v * s.Scale), b.VX);
                Assert.Equal(a.VY.Select(v => v * s.Scale), b.VY);
                Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
            }
        }

        /// <summary> the wedge scaled by 2^-500 and 2^200: the same mesh bit for bit (the pair's angle, apex and radii are computed in a
        /// scale by a power of two; the meeting test is exact) </summary>
        [Theory]
        [InlineData(-500)]
        [InlineData(200)]
        public void HullWedge_AtExtremeScales_IsBitIdentical(int exponent)
        {
            double f = Math.Pow(2, exponent);
            var plain = HullWedge(30, 1.0);
            var moved = HullWedge(30, 1.0);
            for (int i = 0; i < plain.Count; ++i) { moved.X[i] = plain.X[i] * f; moved.Y[i] = plain.Y[i] * f; }
            var a = new QualityMesh2Candidate().Run(plain);
            var b = new QualityMesh2Candidate().Run(moved);
            Assert.Equal(Mesh2Status.Success, a.Status);
            Assert.False(b.SteinerLimitReached);
            Assert.Equal(a.VX.Select(v => v * f), b.VX);
            Assert.Equal(a.VY.Select(v => v * f), b.VY);
            Assert.Equal(a.Triangles.SelectMany(t => t), b.Triangles.SelectMany(t => t));
        }

        /// <summary> one mesher, computed twice, in parallel and serially: the same mesh </summary>
        [Fact]
        public void HullWedge_IsDeterministic()
        {
            var input = ConstrainedDelaunayMesh2Candidate.Convert(HullWedge(30, 1.0));
            var mesher = new QualityMesh2 { MinAngle = 30, Conforming = false };
            var first = mesher.Compute(input).Mesh;
            Assert.False(first.SteinerLimitReached);
            var results = new ConstrainedMesh2[8];
            Parallel.For(0, results.Length, i => results[i] = mesher.Compute(input).Mesh);
            foreach (var m in results.Concat(new[] { mesher.Compute(input).Mesh })) {
                Assert.Equal(first.Vertices, m.Vertices);
                Assert.Equal(first.Triangles, m.Triangles);
                Assert.Equal(first.BadTriangles, m.BadTriangles);
            }
        }

        public static IEnumerable<object[]> SmallAngleRows() => new[] { "F-38/a", "F-38/b", "F-38/c", "F-38/d", "F-38/e" }.Select(id => new object[] { id });

        /// <summary>
        /// The catalogue's small-angle rows in the constrained mode, with the angle check on (their rows have AngleGuaranteed = false, so
        /// the golden rows check nothing about the excuses): every triangle meets the bound or is excused by the oracle. Master passes
        /// these too; the fan (d) drops from 22 384 triangles to 3 165
        /// </summary>
        [Theory]
        [MemberData(nameof(SmallAngleRows))]
        public void CatalogueSmallAngleRows_ConstrainedMode_AngleChecked(string id)
        {
            var row = Catalogue.Rows().Single(r => r.Id == id);
            var input = row.Case.Input;
            input.ConstrainedQuality = true;
            var output = new QualityMesh2Candidate().Run(input);
            var expect = row.Case.Expect;
            expect.AngleGuaranteed = true;
            AssertValid(input, output, id, expect);
            if (id == "F-38/d") Assert.True(output.Triangles.Length <= 5000, id + ": " + output.Triangles.Length + " triangles");
        }
    }
}
