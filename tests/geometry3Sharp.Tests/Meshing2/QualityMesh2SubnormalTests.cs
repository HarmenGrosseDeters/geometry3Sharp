using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Refinement of inputs scaled into the subnormal range. A split point of a piece shorter than 2^-1026 rounds to the subnormal step
    /// (2^-1074), which is no small fraction of the piece: on the hull it rounded outside the domain, on a constraint off its source
    /// (found with the reference comparison: m1/subnormal-32 with an angle bound; BoundaryNotConstrained, SubsegmentUnsourced,
    /// TriangleOutsideDomain, AreaMismatch). Such a piece is now split only at a point exactly on it.
    /// </summary>
    public class QualityMesh2SubnormalTests
    {
        /// <summary> the case m1/subnormal-32 (32 points with 20-bit integer coordinates) at scale 2^exponent </summary>
        static Mesh2Input Points(int exponent, double angle, bool constrained)
        {
            var input = Generators.Transform(Generators.UniformInt(32, 20, 8), exponent);
            input.MinAngleDeg = angle;
            input.ConstrainedQuality = constrained;
            return input;
        }

        static OracleReport Judge(Mesh2Input input, out Mesh2Output output)
        {
            output = new QualityMesh2Candidate().Run(input);
            return Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
        }

        /// <summary> the mesh keeps the domain and its boundary: no violation but angles below the bound (a piece that cannot be split
        /// exactly stays, and its encroaching triangles with it, as with any unsplittable piece) </summary>
        static void AssertDomainKept(OracleReport report, Mesh2Output output, string what)
        {
            Assert.True(output.Status == Mesh2Status.Success && output.Triangles.Length > 0, what + ": " + output.Status);
            var other = report.Violations.Where(v => v.Kind != Violation.AngleBelowBound).ToList();
            Assert.True(other.Count == 0, what + ": " + string.Join("; ", other));
        }

        [Theory]
        [InlineData(20.0, false)]
        [InlineData(25.0, false)]
        [InlineData(30.0, false)]
        [InlineData(20.0, true)]
        [InlineData(25.0, true)]
        [InlineData(30.0, true)]
        public void SubnormalPoints_KeepTheHullAndTheDomain(double angle, bool constrained)
        {
            var input = Points(-1074, angle, constrained);
            var report = Judge(input, out var output);
            AssertDomainKept(report, output, angle + (constrained ? " constrained" : ""));
        }

        /// <summary> the points of m1/subnormal-32 rotated (coordinates with full mantissas: split points are then rarely exactly on
        /// their segment), scaled by 2^exponent </summary>
        static Mesh2Input Rotated(int exponent, double angle)
        {
            var input = Points(0, angle, false);
            double c = Math.Cos(0.3), sn = Math.Sin(0.3), f = Math.Pow(2, exponent);
            for (int i = 0; i < input.Count; ++i) {
                double x = input.X[i], y = input.Y[i];
                input.X[i] = (c * x - sn * y) * f; input.Y[i] = (sn * x + c * y) * f;
            }
            return input;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EveryScale_KeepsTheDomain_AndNormalScalesGiveTheSameMesh(bool rotated)
        {
            // across the subnormal boundary: below it the domain is kept; where every piece is longer than 2^-1026 nothing changes,
            // and the mesh is the unit-scale mesh scaled by 2^k bit for bit
            Func<int, Mesh2Input> make = e => rotated ? Rotated(e, 30) : Points(e, 30, false);
            var unit = new QualityMesh2Candidate().Run(make(0));
            Assert.True(Oracle.Check(make(0), unit, new OracleExpect { Milestone = Milestone.M3 }).Ok);
            foreach (int e in new[] { -1074, -1070, -1064, -1058, -1052, -1046, -1040, -1034, -1028, -1022, -1016, -1010, -1000, -990, -900, -500, 200 }) {
                // (the rotated points are rounded once more where they are subnormal: another input there, judged on its own)
                var input = make(e);
                var report = Judge(input, out var output);
                AssertDomainKept(report, output, "2^" + e);
                if (e < -1022) continue;
                double s = Math.Pow(2, e);
                Assert.Equal(unit.VX.Length, output.VX.Length);
                for (int i = 0; i < unit.VX.Length; ++i)
                    Assert.True(output.VX[i] == unit.VX[i] * s && output.VY[i] == unit.VY[i] * s, "2^" + e + ", vertex " + i);
                Assert.Equal(unit.Triangles.Select(t => string.Join(",", t)), output.Triangles.Select(t => string.Join(",", t)));
                Assert.True(report.Ok, "2^" + e + ": " + report);
            }
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Similarity_LeavesTheMeshValid(string name)
        {
            // the change applies below 2^-1026 only: under every similarity of the harness the case meshes as before, valid
            var s = Similarity.ByName(name);
            var input = Points(-10, 30, false);
            for (int i = 0; i < input.Count; ++i) { var q = s.Apply(new Vector3d(input.X[i], input.Y[i], 0)); input.X[i] = q.x; input.Y[i] = q.y; }
            var report = Judge(input, out var output);
            Assert.True(output.Triangles.Length > 0 && report.Ok, name + ": " + report);
        }

        [Fact]
        public void TinyPieces_AreSplitOnlyExactly()
        {
            // a segment whose midpoint is exact (even integer coordinates) is still split; the refiner's check accepts exactly the
            // points on the piece, strictly between its ends
            double t = double.Epsilon;
            var a = new Vector2d(0, 0); var b = new Vector2d(4 * t, 2 * t);
            Assert.True(QualityMesh2.Refiner.ExactlyWithin(a, b, a, b, new Vector2d(2 * t, t)));
            Assert.False(QualityMesh2.Refiner.ExactlyWithin(a, b, a, b, new Vector2d(2 * t, 2 * t)));
            Assert.False(QualityMesh2.Refiner.ExactlyWithin(a, b, a, b, a));
            Assert.False(QualityMesh2.Refiner.ExactlyWithin(a, b, a, b, new Vector2d(6 * t, 3 * t)));
            var v = new Vector2d(0, 0); var w = new Vector2d(0, 4 * t);
            Assert.True(QualityMesh2.Refiner.ExactlyWithin(v, w, v, w, new Vector2d(0, 3 * t)));
            Assert.False(QualityMesh2.Refiner.ExactlyWithin(v, w, v, w, new Vector2d(0, -t)));
            // ends rounded next to ab (a crossing): p on ab between them counts, p on their line but off ab does not
            var u2 = new Vector2d(t, 0); var w2 = new Vector2d(3 * t, 2 * t);
            Assert.True(QualityMesh2.Refiner.ExactlyWithin(a, b, u2, w2, new Vector2d(2 * t, t)));
            Assert.False(QualityMesh2.Refiner.ExactlyWithin(a, b, u2, w2, new Vector2d(2 * t, 1 * t + t)));
            // a steep piece: compared along y, in which its ends differ most
            var sa = new Vector2d(0, 0); var sb = new Vector2d(t, 8 * t);
            Assert.False(QualityMesh2.Refiner.ExactlyWithin(sa, sb, sa, sb, new Vector2d(t, 8 * t)));
            Assert.True(QualityMesh2.Refiner.ExactlyWithin(new Vector2d(0, 0), new Vector2d(2 * t, 8 * t), new Vector2d(0, 0), new Vector2d(2 * t, 8 * t), new Vector2d(t, 4 * t)));
            // a steep piece with ends next to ab: p is between them along y, not along x (where it shares an end's x)
            Assert.True(QualityMesh2.Refiner.ExactlyWithin(new Vector2d(0, 0), new Vector2d(2 * t, 16 * t), new Vector2d(t, 2 * t), new Vector2d(2 * t, 14 * t), new Vector2d(t, 8 * t)));
        }
    }
}
