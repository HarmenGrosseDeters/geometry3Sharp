using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The raw wrap's triangle quality (Alpha Wrap 2.0): gates by the paper's empty ball Phi(f)
    /// (MeshAlphaWrap.TraverseByEmptyBall) and the quality refinement of the result facets
    /// (MeshAlphaWrap.RefineMinAngleDegrees): fewer slivers, the guarantees and the separation kept; point limit,
    /// cancellation, reuse and similarities.
    /// </summary>
    public class AlphaWrapQualityRefinementTests
    {
        internal static double MinAngle(DMesh3 m, int t)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            double A = Vector3d.AngleD((b - a).Normalized, (c - a).Normalized), B = Vector3d.AngleD((a - b).Normalized, (c - b).Normalized);
            return Math.Min(A, Math.Min(B, 180 - A - B));
        }

        // the 1 %-worst minimum angle
        internal static double P01(DMesh3 m)
        {
            var v = m.TriangleIndices().Select(t => MinAngle(m, t)).OrderBy(x => x).ToList();
            return v[(int)(0.01 * (v.Count - 1))];
        }

        internal static DMesh3 Cylinder() => new CappedCylinderGenerator() { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 1.5f, Slices = 24, NoSharedVertices = false }.Generate().MakeDMesh();

        static MeshAlphaWrap Wrap(DMesh3 m, double a, double o, bool emptyBall, double minAngle)
        {
            var w = new MeshAlphaWrap(m) { RelativeAlpha = a, RelativeOffset = o, TraverseByEmptyBall = emptyBall, RefineMinAngleDegrees = minAngle };
            Assert.True(w.Compute(), w.Error);
            AlphaWrapTests.CheckWrap(w, AlphaWrapTests.Vertices(m));
            AlphaWrapTests.AssertSeparationBound(w, $"{emptyBall} {minAngle}");
            return w;
        }

        // each lever raises the 1 %-worst angle: the gates by the empty ball refine the obtuse facets the circumradius let
        // through, the quality refinement the remaining poor facets (cylinder: 16.0 -> 18.3 -> 22.2 degrees; a random soup
        // with pinches: 7.5 -> 8.3 -> 15.2; the cylinder 21.0 with the round-based carving of lever (B), whose order differs)
        [Fact]
        public void Levers_RaiseTheWorstAngles()
        {
            foreach (var (name, m, before, after) in new[] { ("cylinder", Cylinder(), 17.0, 20.5), ("soup", AlphaWrapTests.RandomSoup(42), 8.0, 13.0) }) {
                MeshAlphaWrap old = Wrap(m, 60, 600, false, 0), ball = Wrap(m, 60, 600, true, 0), both = Wrap(m, 60, 600, true, 15);
                Assert.True(P01(old.Result) < before, $"{name}: {P01(old.Result)}");
                Assert.True(P01(ball.Result) > P01(old.Result), $"{name}: {P01(ball.Result)} vs {P01(old.Result)}");
                Assert.True(P01(both.Result) > after, $"{name}: {P01(both.Result)}");
                Assert.False(old.Counts.ContainsKey("quality_points"));
                Assert.True(both.Counts["quality_points"] > 0 && both.Counts["quality_rounds"] > 0);
                // (few skipped: the poor facets that stay)
                Assert.True(both.Counts["quality_skipped"] < both.Counts["quality_points"], $"{name}: {both.Counts["quality_skipped"]} skipped");
                // (the new points can make new pinches: the second pinch refinement runs)
                Assert.True(both.PhaseMilliseconds.ContainsKey("wrap.pinches2"));
                Assert.False(ball.PhaseMilliseconds.ContainsKey("wrap.pinches2"));
            }
        }

        // Phi(f) against a brute force: the smallest distance to a corner over many centers on the dual segment (all on
        // the normal line through the circumcenter: the same distance to every corner), segments on one side of the
        // triangle's plane, crossing it, and touching it
        [Fact]
        public void EmptyBallRadius_AsBruteForce()
        {
            var rnd = new Random(7);
            Func<Vector3d> pt = () => new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()) * 4 - 2 * Vector3d.One;
            int crossing = 0, oneSide = 0;
            for (int k = 0; k < 2000; ++k) {
                Vector3d a = pt(), b = pt(), d = pt();
                Vector3d u = b - a, v = d - a, w = u.Cross(v);
                if (w.Length < 0.1) continue;
                Vector3d fc = a + (u.LengthSquared * v.Cross(w) + v.LengthSquared * w.Cross(u)) / (2 * w.LengthSquared);
                Vector3d nrm = w.Normalized;
                double s0 = 6 * rnd.NextDouble() - 3, s1 = 6 * rnd.NextDouble() - 3;
                if (k % 10 == 0) s1 = 0;        // ends on the plane
                Vector3d from = fc + s0 * nrm, to = fc + s1 * nrm;
                double brute = double.MaxValue;
                for (int j = 0; j <= 4000; ++j) {
                    Vector3d x = from + (to - from) * (j / 4000.0);
                    brute = Math.Min(brute, x.Distance(a));
                }
                double phi = MeshAlphaWrap.EmptyBallRadius(a, b, d, from, to);
                double R = fc.Distance(a);
                Assert.True(phi >= R - 1e-12 * R, $"{k}: below the circumradius");
                Assert.True(Math.Abs(phi - brute) <= 2e-3 * Math.Max(1, Math.Abs(s1 - s0)), $"{k}: {phi} vs {brute}");
                if (s0 * s1 < 0) crossing++; else oneSide++;
            }
            Assert.True(crossing > 300 && oneSide > 300);
        }

        [Theory]
        [InlineData(210)]
        [InlineData(-220)]
        [InlineData(600)]
        [InlineData(-600)]
        public void EmptyBallRadius_ExtremeScales_ScalesBitForBit(int exp)
        {
            Func<Vector3d, Vector3d> sc = p => new Vector3d(ExactPoint3.ScaleB(p.x, exp), ExactPoint3.ScaleB(p.y, exp), ExactPoint3.ScaleB(p.z, exp));
            // Include non-power-of-two edges (4, 6) and non-dyadic normal offset (3) so SafeLength/SafeHypot bit-identity is exercised.
            foreach (var (b0, d0, fc0, tNear, tFar) in new[] {
                (new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0.5, 0.5, 0), 1.0, 2.0),
                (new Vector3d(4, 0, 0), new Vector3d(0, 6, 0), new Vector3d(2.0, 3.0, 0), 3.0, 5.0),
            }) {
                Vector3d a0 = Vector3d.Zero;
                double refCross = MeshAlphaWrap.EmptyBallRadius(a0, b0, d0, fc0 + new Vector3d(0, 0, -tNear), fc0 + new Vector3d(0, 0, tFar));
                double refSide = MeshAlphaWrap.EmptyBallRadius(a0, b0, d0, fc0 + new Vector3d(0, 0, tNear), fc0 + new Vector3d(0, 0, tFar));

                double phiCross = MeshAlphaWrap.EmptyBallRadius(sc(a0), sc(b0), sc(d0), sc(fc0 + new Vector3d(0, 0, -tNear)), sc(fc0 + new Vector3d(0, 0, tFar)));
                double phiSide = MeshAlphaWrap.EmptyBallRadius(sc(a0), sc(b0), sc(d0), sc(fc0 + new Vector3d(0, 0, tNear)), sc(fc0 + new Vector3d(0, 0, tFar)));
                Assert.Equal(ExactPoint3.ScaleB(refCross, exp), phiCross);
                Assert.Equal(ExactPoint3.ScaleB(refSide, exp), phiSide);
            }
        }

        [Fact]
        public void EmptyBallRadius_ThinSliverUnderflowingCrossSquared_ReturnsExactCircumradius()
        {
            double h = ExactPoint3.ScaleB(1.0, -540);
            Assert.Equal(0.0, h * h); // w^2 underflows to 0.0 on the old unscaled formula
            Vector3d a = Vector3d.Zero, b = new Vector3d(1, 0, 0), d = new Vector3d(0.5, h, 0);
            double yc = -ExactPoint3.ScaleB(1.0, 537); // exact circumcenter y = -0.125 * 2^540 = -2^537
            Vector3d from = new Vector3d(0.5, yc, -1.0), to = new Vector3d(0.5, yc, 1.0);
            double phi = MeshAlphaWrap.EmptyBallRadius(a, b, d, from, to);
            Assert.True(!double.IsNaN(phi) && !double.IsInfinity(phi), $"phi={phi}");
            Assert.True(Math.Abs(phi - Math.Abs(yc)) <= 1e-12 * Math.Abs(yc), $"phi={phi} vs {Math.Abs(yc)}");

            // Mixed-exponent right triangle inside [2^-100, 2^100] whose unscaled numerator flushes to 0.
            double uMix = ExactPoint3.ScaleB(1.0, -55), vMix = ExactPoint3.ScaleB(1.0, -455);
            double phiMix = MeshAlphaWrap.EmptyBallRadius(
                Vector3d.Zero, new Vector3d(uMix, 0, 0), new Vector3d(0, vMix, 0),
                new Vector3d(0.5 * uMix, 0.5 * vMix, -1), new Vector3d(0.5 * uMix, 0.5 * vMix, 1));
            Assert.True(Math.Abs(phiMix - 0.5 * uMix) <= 1e-12 * (0.5 * uMix), $"phiMix={phiMix} vs {0.5 * uMix}");

            // Extreme edge-length ratio (2^100 vs 2^-974): independent-exponent rescaling avoids flushing xUnit.
            double uWide = ExactPoint3.ScaleB(1.0, 100), vWide = ExactPoint3.ScaleB(1.0, -974);
            double phiWide = MeshAlphaWrap.EmptyBallRadius(
                Vector3d.Zero, new Vector3d(uWide, 0, 0), new Vector3d(0, vWide, 0),
                new Vector3d(0.5 * uWide, 0.5 * vWide, -1), new Vector3d(0.5 * uWide, 0.5 * vWide, 1));
            Assert.True(Math.Abs(phiWide - 0.5 * uWide) <= 1e-12 * (0.5 * uWide), $"phiWide={phiWide} vs {0.5 * uWide}");
        }

        [Fact]
        public void EmptyBallRadius_SubnormalAndHugeHypot_DoesNotUnderflowOrOverflow()
        {
            // Subnormal R^2 and t^2: R = sqrt(1.499) * 2^-537, t = R -> exact hypot = sqrt(2) * R.
            double r = Math.Sqrt(1.499) * ExactPoint3.ScaleB(1.0, -537);
            Assert.True(r * r > 0 && r * r < ExactPoint3.ScaleB(1.0, -1022));
            Vector3d a = new Vector3d(-r, 0, 0), b = new Vector3d(r, 0, 0), d = new Vector3d(0, r, 0);
            double phiSub = MeshAlphaWrap.EmptyBallRadius(a, b, d, new Vector3d(0, 0, r), new Vector3d(0, 0, 2 * r));
            double expectedSub = Math.Sqrt(2.0) * r;
            Assert.True(Math.Abs(phiSub - expectedSub) <= 1e-12 * expectedSub, $"{phiSub} vs {expectedSub}");

            // Overflowing R^2 + t^2 at 2^600.
            double big = ExactPoint3.ScaleB(1.0, 600);
            Assert.True(double.IsPositiveInfinity(big * big));
            double phiBig = MeshAlphaWrap.EmptyBallRadius(
                new Vector3d(-big, 0, 0), new Vector3d(big, 0, 0), new Vector3d(0, big, 0),
                new Vector3d(0, 0, big), new Vector3d(0, 0, 2 * big));
            double expectedBig = Math.Sqrt(2.0) * big;
            Assert.True(Math.Abs(phiBig - expectedBig) <= 1e-12 * expectedBig, $"{phiBig} vs {expectedBig}");
        }

        [Fact]
        public void EmptyBallRadius_OverflowingVertexDifferences_DoesNotReturnNaN()
        {
            double s = 1e308;
            Vector3d a = new Vector3d(-s, 0, 0), b = new Vector3d(s, 0, 0), d = new Vector3d(0, s, 0);
            // Circumcenter of (-s,0,0), (s,0,0), (0,s,0) is (0,0,0), circumradius = s.
            double phi = MeshAlphaWrap.EmptyBallRadius(a, b, d, new Vector3d(0, 0, -0.5 * s), new Vector3d(0, 0, 0.5 * s));
            Assert.True(!double.IsNaN(phi) && !double.IsInfinity(phi), $"phi={phi}");
            Assert.True(Math.Abs(phi - s) <= 1e-12 * s, $"phi={phi} vs {s}");

            // Overflowing fc = a + x with a zero normal component must not produce Inf * 0 = NaN in SafePlaneSignedOffset.
            Vector3d oa = new Vector3d(1.5e308, 0, 0);
            Vector3d ob = new Vector3d(1.500075e308, 0.015e308, 0);
            Vector3d od = new Vector3d(1.500075e308, -0.015e308, 0);
            double phiFc = MeshAlphaWrap.EmptyBallRadius(oa, ob, od,
                new Vector3d(-1.5e308, 0, 0.8e308), new Vector3d(-1.5e308, 0, 1.0e308));
            double expectedR = 1.5000375e308;
            double expectedFc = Math.Sqrt((expectedR / 1e308) * (expectedR / 1e308) + 0.8 * 0.8) * 1e308;
            Assert.True(Math.Abs(phiFc - expectedFc) <= 1e-9 * expectedFc, $"phiFc={phiFc} vs {expectedFc}");
        }

        // the quality points count against the point limit (PointLimitReached, with its guarantees); cancelled in the
        // quality refinement: Cancelled
        [Fact]
        public void QualityRefinement_PointLimitAndCancel()
        {
            DMesh3 m = Cylinder();
            var plain = new MeshAlphaWrap(m) { RelativeAlpha = 60, RelativeOffset = 600, RefineMinAngleDegrees = 0, RefineNonManifold = false };
            Assert.True(plain.Compute(), plain.Error);
            long needed = plain.Counts["steiner_points"];
            var limited = new MeshAlphaWrap(m) { RelativeAlpha = 60, RelativeOffset = 600, RefineNonManifold = false, MaxSteinerPoints = (int)needed + 1 };
            Assert.False(limited.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.PointLimitReached, limited.Status);
            Assert.Equal(needed + 1, limited.Counts["steiner_points"]);
            Assert.Equal(1, limited.Counts["quality_points"]);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(limited.Result));

            var cancelled = new MeshAlphaWrap(m) { RelativeAlpha = 60, RelativeOffset = 600 };
            cancelled.CancelF = () => cancelled.PhaseMilliseconds.ContainsKey("wrap.pinches");
            Assert.False(cancelled.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.Cancelled, cancelled.Status);
            Assert.Equal(0, cancelled.Counts["quality_points"]);       // (cancelled before its first point)
        }

        // reused instance: the same result; the second pinch refinement adds to the first one's counts
        [Fact]
        public void QualityRefinement_Reuse()
        {
            var w = Wrap(AlphaWrapTests.RandomSoup(42), 60, 600, true, 15);
            var first = AlphaWrapTests.Vertices(w.Result);
            var counts = new Dictionary<string, long>(w.Counts);
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(first, AlphaWrapTests.Vertices(w.Result));
            Assert.Equal(counts, w.Counts);
        }

        // under every similarity: clean, the separation kept, the worst angles raised; the 2^k scales give the same wrap bit
        // for bit
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void QualityRefinement_UnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Cylinder();
            var w0 = new MeshAlphaWrap(input) { RelativeAlpha = 60, RelativeOffset = 600 };
            Assert.True(w0.Compute(), w0.Error);
            var w = new MeshAlphaWrap(Invariance.Transformed(input, s)) { RelativeAlpha = 60, RelativeOffset = 600 };
            if (s.IsRotated) {
                w.Alpha = s.ApplyLength(w0.UsedAlpha);
                w.Offset = s.ApplyLength(w0.UsedOffset);
            }
            Assert.True(w.Compute(), w.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            AlphaWrapTests.AssertSeparationBound(w, s.Name);
            // (a rotation changes the axis-aligned box the wrap starts from and with it the whole refinement: the worst angles
            // spread over the similarities, 19.4 to 22.3 degrees here, and 16.0 to 23.3 without the two levers; above the
            // 16.0 of the levers off without a transformation)
            Assert.True(P01(w.Result) > 18.5, $"{s.Name}: p01 {P01(w.Result)} ({P01(w0.Result)} untransformed)");
            if (s.IsExact)
                Assert.Equal("", Invariance.GeometricDifference(w0.Result, Invariance.MappedBack(w.Result, s)));
        }
    }
}
