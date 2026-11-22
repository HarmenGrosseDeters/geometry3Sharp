using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The oracle's speed work (#1319, docs/design/MESHING2D_FAST_ORACLE.md section 8): the exact distance comparison without rationals,
    /// the power-of-two path of the rational constructor, the area of many rings by the slab sweep, and the fast path's new indexes and
    /// filters (vertices near a source, ring edges near a vertex, the pieces' chains) against the reference path and, with
    /// Oracle.CheckFastPathForTests, against the scans they replace.
    /// </summary>
    public class Meshing2OracleSpeedTests
    {
        static Pt P(double x, double y) => Pt.FromDoubles(x, y);

        static Pt Rational(BigInteger x, BigInteger y, BigInteger w) => new Pt(x, y, w);

        [Fact]
        public void Dist2ToSegmentCompare_HasTheSignOfTheRationalDistance()
        {
            var rnd = new Random(1319);
            int cases = 0;
            for (int n = 0; n < 4000; ++n) {
                // points on a small lattice (many ties: ends, feet, collinear, equal ends) and exact rationals with odd denominators
                Pt Any()
                {
                    if (rnd.Next(3) == 0) return Rational(rnd.Next(-9, 10), rnd.Next(-9, 10), rnd.Next(1, 8));
                    return P(rnd.Next(-4, 5) * 0.5, rnd.Next(-4, 5) * 0.25);
                }
                Pt a = Any(), b = rnd.Next(10) == 0 ? a : Any(), p = Any();
                Q d = Exact.Dist2ToSegment(a, b, p);
                foreach (Q limit in new[] { Q.Zero, d, d + new Q(1, 1000), d - new Q(1, 1000), new Q(1, 4), new Q(-1, 3), new Q(9, 1) }) {
                    int expected = d.CompareTo(limit);
                    Assert.Equal(Math.Sign(expected), Math.Sign(Exact.Dist2ToSegmentCompare(a, b, p, limit)));
                    Assert.Equal(expected <= 0, Exact.Dist2ToSegmentAtMost(a, b, p, limit));
                    ++cases;
                }
            }
            Assert.True(cases > 20000);
        }

        [Fact]
        public void Q_PowerOfTwoDenominators_ReduceLikeTheGcd()
        {
            var rnd = new Random(77);
            for (int n = 0; n < 3000; ++n) {
                var num = new BigInteger(rnd.Next(-100000, 100000)) << rnd.Next(0, 90);
                if (rnd.Next(4) == 0) num = -num;
                var den = BigInteger.One << rnd.Next(0, 120);
                var q = new Q(num, den);
                var g = BigInteger.GreatestCommonDivisor(num, den);
                var wantN = g.IsZero ? num : num / g; var wantD = g.IsZero ? den : den / g;
                if (num.IsZero) { wantN = BigInteger.Zero; wantD = BigInteger.One; }
                Assert.Equal(wantN, q.N);
                Assert.Equal(wantD, q.D);
            }
            // a negative power of two numerator (its two's complement has other trailing bits than a naive count of the magnitude)
            var m = new Q(new BigInteger(-8), new BigInteger(32));
            Assert.Equal(new BigInteger(-1), m.N); Assert.Equal(new BigInteger(4), m.D);
            var z = new Q(BigInteger.Zero, new BigInteger(64));
            Assert.Equal(BigInteger.Zero, z.N); Assert.Equal(BigInteger.One, z.D);
        }

        /// <summary> the input of Hp7's parcels: side x side jittered square rings that share their borders </summary>
        static Mesh2Input Parcels(int side, double angle, bool constrained)
        {
            var rnd = new Random(7);
            var xs = new List<double>(); var ys = new List<double>();
            for (int i = 0; i <= side; ++i)
                for (int j = 0; j <= side; ++j) {
                    bool edge = i == 0 || j == 0 || i == side || j == side;
                    xs.Add(i + (edge ? 0 : (rnd.NextDouble() - 0.5) * 0.5)); ys.Add(j + (edge ? 0 : (rnd.NextDouble() - 0.5) * 0.5));
                }
            var input = new Mesh2Input { X = xs.ToArray(), Y = ys.ToArray(), MinAngleDeg = angle, ConstrainedQuality = constrained };
            for (int i = 0; i < side; ++i)
                for (int j = 0; j < side; ++j) { int a = i * (side + 1) + j; input.Rings.Add(new RingInput(new[] { a, a + side + 1, a + side + 2, a + 1 }, false)); }
            return input;
        }

        /// <summary> one ring of n points around the origin with a random radius, scaled by 2^k: a coastline (Hp7's coast) </summary>
        static Mesh2Input Coast(int n, double angle, int k = 0)
        {
            var rnd = new Random(11);
            double scale = Math.Pow(2, k);
            var input = new Mesh2Input { X = new double[n], Y = new double[n], MinAngleDeg = angle };
            for (int i = 0; i < n; ++i) { double r = 1 + 0.35 * rnd.NextDouble(), a = 2 * Math.PI * i / n; input.X[i] = r * Math.Cos(a) * scale; input.Y[i] = r * Math.Sin(a) * scale; }
            input.Rings.Add(new RingInput(Enumerable.Range(0, n).ToArray(), false));
            return input;
        }

        static Mesh2Output Mesh(Mesh2Input input)
        {
            var c = new ConstrainedMesh2Input();
            for (int i = 0; i < input.Count; ++i) c.Points.Add(new Vector2d(input.X[i], input.Y[i]));
            foreach (var r in input.Rings) c.Rings.Add(new Mesh2Ring(r.Vertices, r.Hole, r.Marker));
            var result = new QualityMesh2 { MinAngle = input.MinAngleDeg ?? 0, Conforming = !input.ConstrainedQuality, SteinerLimit = 3000000 }.Compute(c);
            var mesh = result.Mesh;
            return new Mesh2Output {
                Status = Mesh2Status.Success, VX = mesh.Vertices.Select(p => p.x).ToArray(), VY = mesh.Vertices.Select(p => p.y).ToArray(),
                InputToVertex = mesh.InputToVertex.ToArray(), Triangles = mesh.Triangles.Select(t => new[] { t.a, t.b, t.c }).ToArray(),
                Neighbours = mesh.Neighbours.Select(t => new[] { t.a, t.b, t.c }).ToArray(),
                Subsegments = mesh.Subsegments.Select(s => new SubsegmentOutput(s.A, s.B, s.Marker)).ToList(),
                TriangleLabels = mesh.TriangleLabels.ToArray(), BadTriangles = mesh.BadTriangles.ToArray(), SteinerLimitReached = mesh.SteinerLimitReached };
        }

        static string Verdict(OracleReport r) =>
            string.Join(",", r.Violations.GroupBy(v => v.Kind).OrderBy(g => g.Key).Select(g => g.Key + "x" + g.Count()))
            + " | area2 " + r.Area2 + " | minsin2 " + r.MinSin2 + " | below " + r.BelowAngle + " | zone " + r.MergeZoneExcused + " | o11 " + r.O11LocalExcused + "/" + r.O11GlobalExcused + "/" + r.O11AngleExcused;

        static OracleReport Run(Mesh2Input input, Mesh2Output output, bool reference, bool check)
        {
            bool previous = Oracle.CheckFastPathForTests;
            Oracle.CheckFastPathForTests = check;
            try { return Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3, Reference = reference }); }
            finally { Oracle.CheckFastPathForTests = previous; }
        }

        /// <summary> the fast path with its checks on (every new index and filter against its scan, throwing on a difference) gives the
        /// reference path's verdict; Parcels: shared borders and many rings (the slab area, the cuts at shared corners), Coast: a long
        /// ring of thin edges (the vertex and ring-edge indexes, the point-in-ring filter) </summary>
        [Theory]
        [InlineData("parcels4")]
        [InlineData("coast120")]
        public void FastPathWithChecks_GivesTheReferenceVerdict(string which)
        {
            var input = which == "parcels4" ? Parcels(4, 20, false) : Coast(120, 20);
            var output = Mesh(input);
            var fast = Run(input, output, false, true);
            var reference = Run(input, output, true, false);
            Assert.True(reference.Ok, reference.ToString());
            Assert.Equal(Verdict(reference), Verdict(fast));
            Assert.True(fast.Ok, fast.ToString());
        }

        /// <summary> the oracle's verdict does not depend on a power of two scale (the fast path's filters have ranges: the same input
        /// at 2^-40, 1 and 2^40 gives the same counts) </summary>
        [Theory]
        [InlineData(-40)]
        [InlineData(0)]
        [InlineData(40)]
        public void Coast_VerdictIsTheSameAtPowerOfTwoScales(int k)
        {
            var input = Coast(60, 20, k); var output = Mesh(input);
            var report = Run(input, output, false, false);
            Assert.True(report.Ok, report.ToString());
            var baseInput = Coast(60, 20); var baseReport = Run(baseInput, Mesh(baseInput), false, false);
            Assert.Equal(baseReport.Violations.Count, report.Violations.Count);
            Assert.Equal(baseReport.BelowAngle, report.BelowAngle);
            Assert.Equal(baseReport.MergeZoneExcused, report.MergeZoneExcused);
        }

        /// <summary> the area of a grid of rings that share their borders is the area of the grid's boundary square, exactly: the slab
        /// sweep's incremental set of containing rings gives, for every gap, the set the full scan gave </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(6)]
        public void Domain_AreaOfSharedBorderParcels_IsTheSquare(int side)
        {
            var input = Parcels(side, 20, false);
            Assert.Equal(new Q(side * side, 1), new Domain(input).Area);
            Assert.Equal(new Q(side * side, 1), new Domain(input, false).Area);
        }

        /// <summary> nested rings with a hole and an island: the slab sweep's parity and innermost-ring rule on an open set of rings </summary>
        [Fact]
        public void Domain_AreaOfNestedRings_FollowsTheInnermostRule()
        {
            var input = new Mesh2Input { X = new double[12], Y = new double[12] };
            double[][] squares = { new[] { 0.0, 0, 10, 10 }, new[] { 2.0, 2, 8, 8 }, new[] { 4.0, 4, 6, 6 } };
            for (int s = 0; s < 3; ++s) {
                var q = squares[s];
                double[] xs = { q[0], q[2], q[2], q[0] }, ys = { q[1], q[1], q[3], q[3] };
                for (int i = 0; i < 4; ++i) { input.X[4 * s + i] = xs[i]; input.Y[4 * s + i] = ys[i]; }
                input.Rings.Add(new RingInput(new[] { 4 * s, 4 * s + 1, 4 * s + 2, 4 * s + 3 }, s == 1));   // the middle one is a hole
            }
            // 100 - 36 + 4
            Assert.Equal(new Q(68, 1), new Domain(input).Area);
        }
    }
}
