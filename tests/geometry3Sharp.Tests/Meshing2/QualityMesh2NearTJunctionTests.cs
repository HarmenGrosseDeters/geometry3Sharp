using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #664: two segments at a small angle whose ends miss each other by a rounding (a T-junction rotated into doubles, an end 4e-17 beside
    /// the other segment). O11 makes that end a vertex of the other's chain, so the chains meet there, but the small-angle rule asked for the
    /// exact input segments to meet: no apex exemption, and a figure-eight cascade of subsegment splits along the whole pair ran to the
    /// Steiner limit (400 000 vertices). The mesher's PairOf now also counts an end within its tolerance of the other segment, and the
    /// oracle's small-input-angle excuse takes such an end in the tube as the apex (NearMeetAngleExcused).
    /// </summary>
    public class QualityMesh2NearTJunctionTests
    {
        /// <summary> the near-contact fan of #542: the unit square, A along y = 0.5, four B_i that start at 0.25 + 0.1 i and end at x = 0.85,
        /// scale (i + 1) 2^-k above A; with a lift the B_i start that far above A instead of on it </summary>
        static Mesh2Input Fan(int k, double scale, double lift, int steinerLimit)
        {
            var x = new List<double> { 0, 1, 1, 0 };
            var y = new List<double> { 0, 0, 1, 1 };
            var input = new Mesh2Input { MinAngleDeg = 30, ConstrainedQuality = true, SteinerLimit = steinerLimit };
            int Add(double px, double py) { x.Add(px); y.Add(py); return x.Count - 1; }
            input.Segments.Add(new SegmentInput(Add(0.125, 0.5), Add(0.875, 0.5)));
            for (int i = 0; i < 4; ++i) input.Segments.Add(new SegmentInput(Add(0.25 + 0.1 * i, 0.5 + lift), Add(0.85, 0.5 + (i + 1) * Math.Pow(2, -k) * scale)));
            input.X = x.ToArray(); input.Y = y.ToArray();
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            return input;
        }

        static (Mesh2Output Output, OracleReport Report) Run(Mesh2Input input)
        {
            var output = new QualityMesh2Candidate().Run(input);
            Assert.True(output.Status == Mesh2Status.Success, output.Status + " " + output.FailureKind);
            return (output, Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 }));
        }

        /// <summary> the starts one ulp (2^-53) above A, on the side where B_i no longer meets A: within the tolerance, so the chains meet.
        /// On master this runs to the limit of 20 000 (a cascade); now it stops far below it, oracle-valid, with the new excuse used </summary>
        [Theory]
        [InlineData(26)]
        [InlineData(28)]
        public void LiftedTJunctions_Terminate_AndAreOracleValid(int k)
        {
            var r = Run(Fan(k, 1.13, Math.Pow(2, -53), 20000));
            Assert.False(r.Output.SteinerLimitReached, "the Steiner limit was reached: " + r.Output.VertexCount + " vertices");
            Assert.True(r.Output.VertexCount < 8000, r.Output.VertexCount + " vertices");
            Assert.True(r.Report.Ok, r.Report.ToString());
        }

        /// <summary> the rotated fan of the issue (Similarity "rotated": in 2D a rotation with a squash of cos 17 degrees along one axis) </summary>
        [Fact]
        public void RotatedFan_Terminates_AndIsOracleValid()
        {
            var s = Similarity.ByName("rotated");
            var input = Fan(26, 1.13, 0, 20000);
            for (int i = 0; i < input.X.Length; ++i) { var q = s.Apply(new Vector3d(input.X[i], input.Y[i], 0)); input.X[i] = q.x; input.Y[i] = q.y; }
            var r = Run(input);
            Assert.False(r.Output.SteinerLimitReached, "the Steiner limit was reached: " + r.Output.VertexCount + " vertices");
            Assert.True(r.Report.Ok, r.Report.ToString());
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary> under every similarity the lifted fan (k = 26) terminates and the oracle accepts it; the 2^k scales give the same mesh bit
        /// for bit </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void LiftedFan_UnderEverySimilarity(string name)
        {
            var s = Similarity.ByName(name);
            var plain = Fan(26, 1.13, Math.Pow(2, -53), 20000);
            var moved = Fan(26, 1.13, Math.Pow(2, -53), 20000);
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

        /// <summary> one mesher, two computes, and a fresh mesher: the same mesh </summary>
        [Fact]
        public void LiftedFan_Reuse_SameInstance()
        {
            var c = ConstrainedDelaunayMesh2Candidate.Convert(Fan(28, 1.13, Math.Pow(2, -53), 20000));
            var mesher = new QualityMesh2 { MinAngle = 30, Conforming = false, SteinerLimit = 20000 };
            var a = mesher.Compute(c); var b = mesher.Compute(c);
            var fresh = new QualityMesh2 { MinAngle = 30, Conforming = false, SteinerLimit = 20000 }.Compute(c);
            Assert.Equal(a.Mesh.Vertices, b.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, b.Mesh.Triangles);
            Assert.Equal(a.Mesh.Vertices, fresh.Mesh.Vertices);
            Assert.Equal(a.Mesh.Triangles, fresh.Mesh.Triangles);
        }
    }
}
