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
        // with pinches: 7.5 -> 8.3 -> 15.2)
        [Fact]
        public void Levers_RaiseTheWorstAngles()
        {
            foreach (var (name, m, before, after) in new[] { ("cylinder", Cylinder(), 17.0, 21.0), ("soup", AlphaWrapTests.RandomSoup(42), 8.0, 13.0) }) {
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
