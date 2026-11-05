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
        /// <summary> the constructed vertices on the boundary of a mesh of points only (its subsegments are pieces of hull edges):
        /// exactly on an edge of the input's hull, or off every hull edge (a split point rounded next to its edge) </summary>
        static void HullSplits(Mesh2Input input, Mesh2Output output, out int exact, out int rounded)
        {
            var pts = Enumerable.Range(0, input.Count).Select(i => new Vector2d(input.X[i], input.Y[i])).OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            var hull = new List<Vector2d>();
            for (int pass = 0; pass < 2; ++pass) {
                int start = hull.Count;
                for (int j = 0; j < pts.Count; ++j) {
                    var p = pass == 0 ? pts[j] : pts[pts.Count - 1 - j];
                    while (hull.Count >= start + 2 && ExactPredicates.Orient2d(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            var inputVertices = new HashSet<int>(output.InputToVertex);
            var onBoundary = new HashSet<int>(output.Subsegments.SelectMany(s => new[] { s.A, s.B }).Where(v => !inputVertices.Contains(v)));
            exact = rounded = 0;
            foreach (int v in onBoundary) {
                var q = new Vector2d(output.VX[v], output.VY[v]);
                bool on = false;
                for (int i = 0; i < hull.Count && !on; ++i) on = ExactPredicates.Orient2d(hull[i], hull[(i + 1) % hull.Count], q) == 0;
                if (on) ++exact; else ++rounded;
            }
        }

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
        [InlineData(20.0, false, 6)]
        [InlineData(25.0, false, 8)]
        [InlineData(30.0, false, 14)]
        [InlineData(20.0, true, 5)]
        [InlineData(25.0, true, 7)]
        [InlineData(30.0, true, 14)]
        public void SubnormalPoints_KeepTheHullAndTheDomain(double angle, bool constrained, int badNow)
        {
            var input = Points(-1074, angle, constrained);
            var report = Judge(input, out var output);
            string what = angle + (constrained ? " constrained" : "");
            AssertDomainKept(report, output, what);
            // refinement still works where it can: Steiner points are added, and the triangles left below the bound stay few (today's
            // count, with a margin; a refiner that refused every tiny piece leaves more)
            Assert.True(output.VX.Length > input.Count, what + ": no Steiner point");
            int bad = report.Violations.Count(v => v.Kind == Violation.AngleBelowBound);
            Assert.True(bad <= badNow + 3, what + ": " + bad + " triangles below the bound");
        }

        [Fact]
        public void TinyPieces_AreSplitExactlyWherePossible_AndTheThresholdHolds()
        {
            // where the piece's split point is exactly on its hull edge (even integer differences) it is used: at 2^-1074 and below the
            // threshold. Above it (pieces of 2^-1026 and longer) a rounded point is used as in the normal range. The 20-bit points span
            // 2^20 steps, their refined hull pieces 2^2 to 2^20: at 2^-1044 one rounded split is still on a piece of 2^-1026, at
            // 2^-1046 none is. This pins the threshold to one exponent (measured: -1028 and below let rounded splits through at 2^-1046,
            // -1025 and above refuse the one at 2^-1044; -1027 passes, and is as safe: half a step is then at most 2^-48 of the piece).
            var tiny = Points(-1074, 30, false);
            var report = Judge(tiny, out var output);
            AssertDomainKept(report, output, "2^-1074");
            HullSplits(tiny, output, out int exact, out int rounded);
            Assert.True(exact > 0 && rounded == 0, "2^-1074: " + exact + " exact, " + rounded + " rounded");
            var below = Points(-1046, 30, false);
            report = Judge(below, out output);
            AssertDomainKept(report, output, "2^-1046");
            HullSplits(below, output, out exact, out rounded);
            Assert.True(exact > 0 && rounded == 0, "2^-1046: " + exact + " exact, " + rounded + " rounded");
            var above = Points(-1044, 30, false);
            report = Judge(above, out output);
            AssertDomainKept(report, output, "2^-1044");
            HullSplits(above, output, out exact, out rounded);
            Assert.True(rounded > 0, "2^-1044: " + exact + " exact, " + rounded + " rounded");
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
    }
}
