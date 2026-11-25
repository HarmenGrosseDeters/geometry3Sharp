using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactTriangleIntersection.IntersectsBeyondShared for neighbours of which one or both triangles are degenerate
    /// (collinear corners, or two equal corners), #1551. The oracle is independent of the method: the part of two
    /// such triangles beyond their shared vertex (or edge) is convex and contains the shared part, so it is not empty
    /// iff a point a small step beyond the shared part, in a direction of the degenerate triangle, lies in both. The
    /// step is a power of two, so the point is exact, and membership is decided by the exact predicates. On these
    /// integer grids (coordinates in [-3, 3], multiples up to 3) the step 2^-20 cannot miss an overlap: where the part
    /// beyond the shared vertex or edge is not empty it reaches from it at least a fraction of the segment that is
    /// bounded by the inverse of a determinant of grid coordinates (far above 2^-20).
    /// </summary>
    public class ExactTriangleIntersectionDegenerateNeighbourTests
    {
        const double Step = 1.0 / (1 << 20);

        static Vector2d Drop(Vector3d v, int k) => k == 0 ? new Vector2d(v.y, v.z) : k == 1 ? new Vector2d(v.z, v.x) : new Vector2d(v.x, v.y);

        static bool Collinear(Vector3d a, Vector3d b, Vector3d c)
        {
            for (int k = 0; k < 3; ++k)
                if (ExactPredicates.Orient2d(Drop(a, k), Drop(b, k), Drop(c, k)) != 0) return false;
            return true;
        }

        static bool OnSegment(Vector3d a, Vector3d b, Vector3d x)
        {
            if (Collinear(a, b, x) == false) return false;
            for (int k = 0; k < 3; ++k)
                if (x[k] < Math.Min(a[k], b[k]) || x[k] > Math.Max(a[k], b[k])) return false;
            return true;
        }

        // x in the closed triangle T (degenerate: on one of its edges), exact
        static bool InClosed(ref Triangle3d T, Vector3d x)
        {
            if (Collinear(T.V0, T.V1, T.V2))
                return OnSegment(T.V0, T.V1, x) || OnSegment(T.V1, T.V2, x) || OnSegment(T.V2, T.V0, x);
            if (ExactPredicates.Orient3d(T.V0, T.V1, T.V2, x) != 0) return false;
            for (int k = 0; k < 3; ++k) {
                Vector2d a = Drop(T.V0, k), b = Drop(T.V1, k), c = Drop(T.V2, k), p = Drop(x, k);
                int o = ExactPredicates.Orient2d(a, b, c);
                if (o == 0) continue;
                return ExactPredicates.Orient2d(a, b, p) * o >= 0 && ExactPredicates.Orient2d(b, c, p) * o >= 0 && ExactPredicates.Orient2d(c, a, p) * o >= 0;
            }
            return false;
        }

        // the oracle for a pair with one shared vertex s, A degenerate: A is the union of the segments s-p and s-q
        // (its corners on one line through s); its points beyond s near s are s + Step (x - s), x in {p, q}
        static bool OracleOneShared(Vector3d s, Vector3d p, Vector3d q, ref Triangle3d A, ref Triangle3d B)
        {
            foreach (Vector3d x in new[] { p, q }) {
                if (x == s) continue;
                Vector3d y = s + Step * (x - s);
                if (InClosed(ref A, y) && InClosed(ref B, y)) return true;
            }
            return false;
        }

        // with a shared edge a-b and A degenerate (on the line a-b): its points beyond the edge near it are
        // b + Step (b - a) and a + Step (a - b)
        static bool OracleSharedEdge(Vector3d a, Vector3d b, ref Triangle3d A, ref Triangle3d B)
        {
            if (a == b) return false;   // (not generated)
            foreach (Vector3d y in new[] { b + Step * (b - a), a + Step * (a - b) })
                if (InClosed(ref A, y) && InClosed(ref B, y)) return true;
            return false;
        }

        [Fact]
        public void ReviewCase_SegmentThroughSharedVertex_DoesNotIntersectBeyondIt()
        {
            // #1551: A is a segment from s = (-2, 1, 1) to (1, 0, 0), off B's plane except at s ((3, -1, -1) . n = 21)
            var A = new Triangle3d(new Vector3d(-2, 1, 1), new Vector3d(1, 0, 0), new Vector3d(1, 0, 0));
            var B = new Triangle3d(new Vector3d(-2, 1, 1), new Vector3d(3, 3, -2), new Vector3d(1, -3, 3));
            Assert.False(ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 1, 2), ref A, new Index3i(0, 3, 4), ref B));
            Assert.False(ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 3, 4), ref B, new Index3i(0, 1, 2), ref A));
        }

        [Fact]
        public void SegmentInThePlane_IntoTheCorner_Intersects_OtherwiseNot()
        {
            Vector3d s = Vector3d.Zero;
            var B = new Triangle3d(s, new Vector3d(4, 0, 0), new Vector3d(0, 4, 0));
            Index3i ia = new Index3i(0, 1, 2), ib = new Index3i(0, 3, 4);
            Func<Vector3d, Vector3d, bool> f = (p, q) => { var A = new Triangle3d(s, p, q); return ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B); };
            Assert.True(f(new Vector3d(1, 1, 0), new Vector3d(2, 2, 0)));       // into B's corner, in its plane
            Assert.True(f(new Vector3d(2, 0, 0), new Vector3d(2, 0, 0)));       // along B's edge
            Assert.False(f(new Vector3d(-1, -1, 0), new Vector3d(-2, -2, 0)));  // in the plane, away from the corner
            Assert.False(f(new Vector3d(1, 1, 1), new Vector3d(2, 2, 2)));      // off the plane
            Assert.False(f(s, s));                                              // a point: only s
            // s between p and q: the hull passes through s, one half into the corner
            Assert.True(f(new Vector3d(-1, -1, 0), new Vector3d(1, 1, 0)));
            Assert.False(f(new Vector3d(-1, 1, 0), new Vector3d(1, -1, 0)));    // through s, both halves outside the corner
        }

        [Fact]
        public void TwoSegmentsFromSharedVertex_IntersectIffOnOneRay()
        {
            Vector3d s = new Vector3d(1, 2, 3);
            Index3i ia = new Index3i(0, 1, 2), ib = new Index3i(0, 3, 4);
            Func<Vector3d, Vector3d, Vector3d, Vector3d, bool> f = (p, q, u, w) => {
                var A = new Triangle3d(s, s + p, s + q); var B = new Triangle3d(s, s + u, s + w);
                return ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B);
            };
            Vector3d d = new Vector3d(1, -2, 3);
            Assert.True(f(d, 2 * d, 3 * d, 3 * d));
            Assert.False(f(d, 2 * d, -d, -3 * d));
            Assert.False(f(d, 2 * d, new Vector3d(1, 0, 0), new Vector3d(2, 0, 0)));
            Assert.True(f(-d, d, -2 * d, -2 * d));
        }

        [Fact]
        public void SharedEdge_DegenerateOnOneLine_IntersectsIffBothExtendTheSameEnd()
        {
            Vector3d a = new Vector3d(0, 0, 0), b = new Vector3d(1, 1, 1);
            Index3i ia = new Index3i(0, 1, 2), ib = new Index3i(1, 0, 3);
            Func<double, double, bool> f = (tc, td) => {
                var A = new Triangle3d(a, b, tc * b); var B = new Triangle3d(b, a, td * b);
                return ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B);
            };
            Assert.True(f(2, 3));
            Assert.True(f(-1, -2));
            Assert.False(f(2, -1));
            Assert.False(f(0.5, 2));
            // one degenerate, one not: they meet only in the edge
            var A2 = new Triangle3d(a, b, 2 * b); var B2 = new Triangle3d(b, a, new Vector3d(0, 1, 0));
            Assert.False(ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A2, ib, ref B2));
        }

        [Fact]
        public void AllIdsShared_False_InEveryCornerOrder()
        {
            // one (degenerate) triangle twice: nothing beyond what is shared, whatever the order of its corners (review of
            // #1554: the edge branch gave false or true by the order)
            Vector3d a = new Vector3d(0, 0, 0), b = new Vector3d(1, 0, 0), c = new Vector3d(2, 0, 0);
            Index3i ids = new Index3i(0, 1, 2);
            foreach (var T0 in new[] { new Triangle3d(a, b, c), new Triangle3d(a, c, b), new Triangle3d(b, a, c), new Triangle3d(c, b, a) }) {
                var T = T0; var U = T0;
                Assert.False(ExactTriangleIntersection.IntersectsBeyondShared(ids, ref T, ids, ref U));
            }
        }

        [Fact]
        public void SharedEdgeAtOnePosition_AsOneSharedVertex()
        {
            // the two shared ids at one position: the "edge" is a point, the pair is two segments from it (review of #1554)
            Vector3d o = Vector3d.Zero;
            Index3i ia = new Index3i(0, 1, 2), ib = new Index3i(1, 0, 3);
            var A = new Triangle3d(o, o, new Vector3d(1, 1, 0));
            var B = new Triangle3d(o, o, new Vector3d(2, 2, 0));
            Assert.True(ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B));
            var B2 = new Triangle3d(o, o, new Vector3d(-1, -1, 0));
            Assert.False(ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B2));
        }

        [Fact]
        public void PointLikeAgainstSegment_OnlyTheSharedVertex()
        {
            // A all at s: nothing beyond s, also against a degenerate B through s (review of #1554: same_ray_3d's x = s guard)
            Vector3d s = new Vector3d(1, 2, 3);
            var A = new Triangle3d(s, s, s);
            var B = new Triangle3d(s, s, new Vector3d(2, 2, 3));
            Assert.False(ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 1, 2), ref A, new Index3i(0, 3, 4), ref B));
            Assert.False(ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 3, 4), ref B, new Index3i(0, 1, 2), ref A));
        }

        // degenerate A (collinear on an integer grid, or two equal corners) sharing a vertex at a varying corner with a
        // random or a degenerate B; either order
        [Fact]
        public void RandomDegenerateNeighbours_AgreeWithTheOracle()
        {
            var rand = new Random(1551);
            Func<Vector3d> point = () => new Vector3d(rand.Next(-3, 4), rand.Next(-3, 4), rand.Next(-3, 4));
            int hits = 0, n = 0, inPlane = 0;
            while (n < 30000) {
                Vector3d s = point(), x = point();
                Vector3d dir = x - s;
                // A: s and two points on a line through s (a multiple of dir, or s itself)
                Vector3d p = s + rand.Next(-2, 3) * dir, q = s + rand.Next(-2, 3) * dir;
                // B: random, in a plane through s and dir half of the time, or degenerate a quarter of the time
                Vector3d u, w;
                int kind = rand.Next(4);
                if (kind == 0) { u = s + rand.Next(-2, 3) * dir; w = s + rand.Next(-2, 3) * dir; }
                else if (kind == 1) { Vector3d e = point(); u = s + rand.Next(-2, 3) * dir + e; w = s + rand.Next(-2, 3) * dir - rand.Next(0, 3) * e; }
                else { u = point(); w = point(); }
                var A = new Triangle3d(s, p, q);
                var B = new Triangle3d(s, u, w);
                if (Collinear(s, p, q) == false) continue;
                bool expected = OracleOneShared(s, p, q, ref A, ref B) || (Collinear(s, u, w) && OracleOneShared(s, u, w, ref B, ref A));
                // the shared vertex at a varying corner of each, and either order
                int ca = rand.Next(3), cb = rand.Next(3);
                var At = Rotate(A, ca); var Bt = Rotate(B, cb);
                Index3i iat = Rotate(new Index3i(0, 1, 2), ca), ibt = Rotate(new Index3i(0, 3, 4), cb);
                bool got = rand.Next(2) == 0 ? ExactTriangleIntersection.IntersectsBeyondShared(iat, ref At, ibt, ref Bt)
                    : ExactTriangleIntersection.IntersectsBeyondShared(ibt, ref Bt, iat, ref At);
                Assert.True(expected == got, $"A {A.V0} {A.V1} {A.V2}, B {B.V0} {B.V1} {B.V2}: expected {expected}");
                n++;
                if (expected) hits++;
                if (kind == 1) inPlane++;
            }
            Assert.True(hits > 1000 && n - hits > 1000, $"{hits} of {n} intersecting");
        }

        [Fact]
        public void RandomDegenerateSharedEdges_AgreeWithTheOracle()
        {
            var rand = new Random(1552);
            Func<Vector3d> point = () => new Vector3d(rand.Next(-3, 4), rand.Next(-3, 4), rand.Next(-3, 4));
            int hits = 0, n = 0;
            while (n < 20000) {
                Vector3d a = point(), b = point();
                if (a == b) continue;
                Vector3d e = b - a;
                Vector3d c = a + rand.Next(-2, 4) * e, d = rand.Next(2) == 0 ? a + rand.Next(-2, 4) * e : point();
                var A = new Triangle3d(a, b, c); var B = new Triangle3d(b, a, d);
                bool expected = OracleSharedEdge(a, b, ref A, ref B);
                bool got = rand.Next(2) == 0 ? ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 1, 2), ref A, new Index3i(1, 0, 3), ref B)
                    : ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(1, 0, 3), ref B, new Index3i(0, 1, 2), ref A);
                Assert.True(expected == got, $"A {A.V0} {A.V1} {A.V2}, B {B.V0} {B.V1} {B.V2}: expected {expected}");
                n++;
                if (expected) hits++;
            }
            Assert.True(hits > 1000 && n - hits > 1000, $"{hits} of {n} intersecting");
        }

        [Fact]
        public void DegenerateNeighbours_EveryFrame_SameAnswer()
        {
            // the cases above under Similarity.All(): rotations and translations change the coordinates, but the
            // answers of these configurations are kept wherever the transformed corners stay collinear (exact scalings
            // always); the exact scalings give the same answer bit for bit
            var rand = new Random(1553);
            Func<Vector3d> point = () => new Vector3d(rand.Next(-3, 4), rand.Next(-3, 4), rand.Next(-3, 4));
            var cases = new List<(Triangle3d, Triangle3d, bool)>();
            while (cases.Count < 3000) {
                Vector3d s = point(), dir = point() - s, e = point();
                Vector3d p = s + rand.Next(-2, 3) * dir, q = s + rand.Next(-2, 3) * dir;
                Vector3d u = s + rand.Next(-2, 3) * dir + e, w = s + rand.Next(-2, 3) * dir - rand.Next(0, 3) * e;
                if (u == s || w == s) continue;
                var A = new Triangle3d(s, p, q); var B = new Triangle3d(s, u, w);
                cases.Add((A, B, ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 1, 2), ref A, new Index3i(0, 3, 4), ref B)));
            }
            // (the exact scalings, and the UTM translation, which is exact on these small integer coordinates)
            foreach (Similarity sim in Similarity.All()) {
                if (sim.IsExact == false && sim != Similarity.Utm) continue;
                foreach (var (A0, B0, expected) in cases) {
                    var A = new Triangle3d(sim.Apply(A0.V0), sim.Apply(A0.V1), sim.Apply(A0.V2));
                    var B = new Triangle3d(sim.Apply(B0.V0), sim.Apply(B0.V1), sim.Apply(B0.V2));
                    Assert.Equal(expected, ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 1, 2), ref A, new Index3i(0, 3, 4), ref B));
                }
            }
            foreach (int e2 in new[] { -500, 500 }) {
                double f = Math.Pow(2, e2);
                foreach (var (A0, B0, expected) in cases) {
                    var A = new Triangle3d(f * A0.V0, f * A0.V1, f * A0.V2); var B = new Triangle3d(f * B0.V0, f * B0.V1, f * B0.V2);
                    Assert.Equal(expected, ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 1, 2), ref A, new Index3i(0, 3, 4), ref B));
                }
            }
        }

        static Triangle3d Rotate(Triangle3d t, int k) => k == 0 ? t : k == 1 ? new Triangle3d(t.V1, t.V2, t.V0) : new Triangle3d(t.V2, t.V0, t.V1);
        static Index3i Rotate(Index3i t, int k) => k == 0 ? t : k == 1 ? new Index3i(t.b, t.c, t.a) : new Index3i(t.c, t.a, t.b);
    }
}
