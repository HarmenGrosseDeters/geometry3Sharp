using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Off the grid, a bad triangle whose split point encroaches on an O11-unsplittable subsegment (every split point of it within the
    /// tolerance of another input constraint, `NearOtherSource`) is not given up: the encroachment of such a piece no longer stops the
    /// point, as on the grid. Found by the near-contact fan of #542 (scale 1.13, k = 26 and 28): at a T-junction where segment B_i starts on
    /// A at about 1e-7 radian, a triangle below A had its off-centre inside the diametral circle of a piece of A that B_i makes
    /// unsplittable; the mesher gave it up, and the oracle (whose O11 exemptions cover a circumcentre behind such a piece, owner decision
    /// 2026-11-02) rightly did not excuse it.
    /// </summary>
    public class QualityMesh2UnsplittableEncroachmentTests
    {
        /// <summary> the fan of Meshing2OracleCrossingRoundingTests: the unit square, A along y = 0.5, four B_i from A at 0.25 + 0.1 i to x = 0.85,
        /// scale (i + 1) 2^-k above A </summary>
        internal static Mesh2Input Fan(int k, double scale, int steinerLimit = 400000)
        {
            var x = new List<double> { 0, 1, 1, 0 };
            var y = new List<double> { 0, 0, 1, 1 };
            var input = new Mesh2Input { MinAngleDeg = 30, ConstrainedQuality = true, SteinerLimit = steinerLimit };
            int Add(double px, double py) { x.Add(px); y.Add(py); return x.Count - 1; }
            input.Segments.Add(new SegmentInput(Add(0.125, 0.5), Add(0.875, 0.5)));
            for (int i = 0; i < 4; ++i) input.Segments.Add(new SegmentInput(Add(0.25 + 0.1 * i, 0.5), Add(0.85, 0.5 + (i + 1) * Math.Pow(2, -k) * scale)));
            input.X = x.ToArray(); input.Y = y.ToArray();
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            return input;
        }

        static (Mesh2Output Output, OracleReport Report, long[] Counts) Run(Mesh2Input input)
        {
            var counts = new long[17];
            QualityMesh2.Refiner.CountsForBenchmarks = counts;
            try {
                var output = new QualityMesh2Candidate().Run(input);
                Assert.True(output.Status == Mesh2Status.Success, output.Status + " " + output.FailureKind);
                return (output, Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 }), counts);
            }
            finally { QualityMesh2.Refiner.CountsForBenchmarks = null; }
        }

        /// <summary> the two fan cases of #542's open note: the oracle accepts them, and the unsplittable pieces were met (slot 15: a subsegment
        /// left unsplit next to another source), so the case still runs through the changed path </summary>
        [Theory]
        [InlineData(26)]
        [InlineData(28)]
        public void FanTJunction_EncroachedUnsplittablePiece_DoesNotLeaveABadTriangle(int k)
        {
            var r = Run(Fan(k, 1.13));
            Assert.True(r.Report.Ok, r.Report.ToString());
            Assert.True(r.Counts[15] > 0, "no subsegment was left unsplit next to another source");
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All2D().Select(s => new object[] { s.Name });

        /// <summary> under every similarity the k = 26 fan terminates below its Steiner limit and the oracle accepts it (the angle bound
        /// included), and the 2^k scales give the same mesh bit for bit. Under a rotation the T-junction ends are rounded off A; that ran to the
        /// Steiner limit until #664 (the limit of 20 000 keeps a regression quick) </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void FanTJunction_UnderEverySimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var plain = Fan(26, 1.13);
            var moved = Fan(26, 1.13, 20000);
            for (int i = 0; i < plain.X.Length; ++i) { var q = s.Apply(new Vector3d(plain.X[i], plain.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            var b = Run(moved);
            Assert.False(b.Output.SteinerLimitReached, name + ": the Steiner limit was reached");
            Assert.True(b.Report.Ok, name + ": " + b.Report);
            if (s.IsExact) {
                var a = Run(plain);
                Assert.Equal(a.Output.VX.Select(v => v * s.Scale), b.Output.VX);
                Assert.Equal(a.Output.VY.Select(v => v * s.Scale), b.Output.VY);
                Assert.Equal(a.Output.Triangles.SelectMany(t => t), b.Output.Triangles.SelectMany(t => t));
            }
        }

        /// <summary> one mesher, two computes on the k = 28 fan: the same mesh, and the same as a fresh mesher (determinism of repeated runs) </summary>
        [Fact]
        public void FanTJunction_Reuse_SameInstance()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(Fan(28, 1.13));
            var mesher = new QualityMesh2 { MinAngle = 30, Conforming = false, SteinerLimit = 400000 };
            var a = mesher.Compute(c); var b = mesher.Compute(c);
            var fresh = new QualityMesh2 { MinAngle = 30, Conforming = false, SteinerLimit = 400000 }.Compute(c);
            Assert.Equal(a.Mesh.Vertices, b.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, b.Mesh.Triangles);
            Assert.Equal(a.Mesh.Vertices, fresh.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, fresh.Mesh.Triangles);
        }
    }
}
