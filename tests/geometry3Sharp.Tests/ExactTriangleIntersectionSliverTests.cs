using System;
using System.Collections.Generic;
using System.Globalization;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactTriangleIntersection.IntersectsBeyondShared on coplanar neighbours where one triangle is a sliver: the
    /// projection axis must be chosen exactly (a floating-point normal of a sliver can round to zero, and dropping
    /// the wrong axis projects the plane onto a line, where every corner looks collinear). Checked against an oracle
    /// that shares no code with the library: exact dyadic orientations (BooleanOracle's Dyadic) in the plane.
    /// </summary>
    public class ExactTriangleIntersectionSliverTests
    {
        // the repro of the remesher session: both triangles in y = 0, vertex ids (0, 1, 2) and (3, 1, 4)
        static readonly Vector3d[] ReproA = {
            new Vector3d(-0.976127129435447, 0, 0.07347307296897497), new Vector3d(-0.9283813883063483, 0, 0.22041939375248493),
            new Vector3d(-0.9522542588708977, 0, 0.14694623336072996) };
        static readonly Vector3d[] ReproB = {
            new Vector3d(-0.8773141400056257, 0, 0.11681076482628214), new Vector3d(-0.9283813883063483, 0, 0.22041939375248493),
            new Vector3d(-0.9999999999999962, 0, -8.742278000372475E-08) };
        static readonly Index3i ReproIdsA = new Index3i(0, 1, 2), ReproIdsB = new Index3i(3, 1, 4);

        [Fact]
        public void Repro_CoplanarSliverAtSharedVertex_DoesNotIntersect()
        {
            var A = new Triangle3d(ReproA[0], ReproA[1], ReproA[2]);
            var B = new Triangle3d(ReproB[0], ReproB[1], ReproB[2]);
            // the case: coplanar, A a sliver (not exactly degenerate) whose floating-point normal is 0
            Assert.False(ExactTriangleIntersection.IsDegenerate(ref A));
            Assert.Equal(Vector3d.Zero, (A.V2 - A.V1).Cross(A.V0 - A.V1));
            Assert.False(ExactTriangleIntersection.SegmentIntersectsTriangle(A.V2, A.V0, ref B));
            Assert.False(ExactTriangleIntersection.SegmentIntersectsTriangle(B.V2, B.V0, ref A));
            Assert.False(Oracle.CornersOverlap(Plane.Y, A.V1, A.V2, A.V0, B.V2, B.V0));
            Assert.False(ExactTriangleIntersection.IntersectsBeyondShared(ReproIdsA, ref A, ReproIdsB, ref B));
            Assert.False(ExactTriangleIntersection.IntersectsBeyondShared(ReproIdsB, ref B, ReproIdsA, ref A));
        }

        [Fact]
        public void Repro_MeshRepairMetrics_FindsNoSelfIntersection()
        {
            var mesh = new DMesh3();
            foreach (Vector3d v in new[] { ReproA[0], ReproA[1], ReproA[2], ReproB[0], ReproB[2] })
                mesh.AppendVertex(v);
            Assert.True(mesh.AppendTriangle(0, 1, 2) >= 0);
            Assert.True(mesh.AppendTriangle(3, 1, 4) >= 0);
            Assert.Equal(0, gs.MeshRepairMetrics.SelfIntersections(mesh));
        }

        // ---------------------------------------------------------------- oracle

        // the axis-aligned plane both triangles lie in: Y = the plane y = const, its 2D coordinates (z, x); etc.
        enum Plane { X = 0, Y = 1, Z = 2 }

        static class Oracle
        {
            static Dyadic[] Uv(Plane plane, Vector3d p)
            {
                switch (plane) {
                    case Plane.X: return new[] { Dyadic.Of(p.y), Dyadic.Of(p.z) };
                    case Plane.Y: return new[] { Dyadic.Of(p.z), Dyadic.Of(p.x) };
                    default: return new[] { Dyadic.Of(p.x), Dyadic.Of(p.y) };
                }
            }

            // sign of the cross product (a - s) x (b - s) in the plane, exact
            public static int Orient(Plane plane, Vector3d s, Vector3d a, Vector3d b)
            {
                Dyadic[] S = Uv(plane, s), A = Uv(plane, a), B = Uv(plane, b);
                return ((A[0] - S[0]) * (B[1] - S[1]) - (A[1] - S[1]) * (B[0] - S[0])).Sign;
            }

            static bool RayInClosedCone(Plane plane, Vector3d s, Vector3d p, Vector3d q, Vector3d x)
            {
                // the cone p-s-q, counter-clockwise, below 180 degrees
                return Orient(plane, s, p, x) >= 0 && Orient(plane, s, x, q) >= 0;
            }

            /// <summary>
            /// Do the closed triangles (s, p, q) and (s, u, w), not degenerate and in the plane, share a point other
            /// than s? Both are convex and have the corner s, so they do iff their closed corners at s share a ray,
            /// which holds iff a boundary ray of one lies in the closed corner of the other.
            /// </summary>
            public static bool CornersOverlap(Plane plane, Vector3d s, Vector3d p, Vector3d q, Vector3d u, Vector3d w)
            {
                if (Orient(plane, s, p, q) < 0) { var t = p; p = q; q = t; }
                if (Orient(plane, s, u, w) < 0) { var t = u; u = w; w = t; }
                return RayInClosedCone(plane, s, p, q, u) || RayInClosedCone(plane, s, p, q, w)
                    || RayInClosedCone(plane, s, u, w, p) || RayInClosedCone(plane, s, u, w, q);
            }

            /// <summary> do the closed triangles (a, b, c) and (b, a, d) in the plane overlap beyond their edge? </summary>
            public static bool FoldOverlaps(Plane plane, Vector3d a, Vector3d b, Vector3d c, Vector3d d)
            {
                return Orient(plane, a, b, c) * Orient(plane, a, b, d) > 0;
            }
        }

        // ---------------------------------------------------------------- generated slivers

        static Vector3d Lift(Plane plane, double u, double v, double c)
        {
            switch (plane) {
                case Plane.X: return new Vector3d(c, u, v);
                case Plane.Y: return new Vector3d(v, c, u);
                default: return new Vector3d(u, v, c);
            }
        }

        sealed class Case
        {
            public Plane Plane;
            public Vector3d S, P, Q, U, W;
            public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0}: s {1:R} {2:R} {3:R}, p {4:R} {5:R} {6:R}, q {7:R} {8:R} {9:R}, u {10:R} {11:R} {12:R}, w {13:R} {14:R} {15:R}",
                Plane, S.x, S.y, S.z, P.x, P.y, P.z, Q.x, Q.y, Q.z, U.x, U.y, U.z, W.x, W.y, W.z);
        }

        /// <summary>
        /// Coplanar triangle pairs at a shared vertex s, in the three axis planes: A = (s, p, q) a sliver (q within
        /// 2^-k of the segment s-p, k in 20..57, so its floating-point normal is often 0), B = (s, u, w) with rays
        /// near the rays of A (on either side, or a sliver itself), so that both outcomes occur.
        /// </summary>
        static IEnumerable<Case> Slivers(int count, int seed)
        {
            var rnd = new Random(seed);
            for (int i = 0; i < count; ++i) {
                var plane = (Plane)(i % 3);
                double c = (i % 2 == 0) ? 0 : rnd.NextDouble() * 4 - 2;
                double su = rnd.NextDouble() * 2 - 1, sv = rnd.NextDouble() * 2 - 1;
                double angle = rnd.NextDouble() * 2 * Math.PI, len = 0.05 + rnd.NextDouble() * 0.3;
                double du = Math.Cos(angle), dv = Math.Sin(angle);
                double t = 0.2 + 0.6 * rnd.NextDouble(), h = Math.Pow(2, -(rnd.Next(2) == 0 ? rnd.Next(20, 50) : rnd.Next(50, 58))) * (rnd.Next(2) == 0 ? 1 : -1);
                var s = Lift(plane, su, sv, c);
                var p = Lift(plane, su + len * du, sv + len * dv, c);
                var q = Lift(plane, su + t * len * du - h * dv, sv + t * len * dv + h * du, c);
                // B: its first ray at an angle from A's ray (tiny or larger, either side), its opening small or large
                double off = (rnd.Next(3) == 0 ? Math.Pow(2, -rnd.Next(10, 50)) : rnd.NextDouble() * 0.5) * (rnd.Next(2) == 0 ? 1 : -1);
                double open = (rnd.Next(3) == 0 ? Math.Pow(2, -rnd.Next(20, 50)) : 0.05 + rnd.NextDouble()) * (rnd.Next(2) == 0 ? 1 : -1);
                double a1 = angle + off, a2 = a1 + open, l1 = 0.05 + rnd.NextDouble() * 0.3, l2 = 0.05 + rnd.NextDouble() * 0.3;
                var u = Lift(plane, su + l1 * Math.Cos(a1), sv + l1 * Math.Sin(a1), c);
                var w = Lift(plane, su + l2 * Math.Cos(a2), sv + l2 * Math.Sin(a2), c);
                yield return new Case { Plane = plane, S = s, P = p, Q = q, U = u, W = w };
            }
        }

        static bool Valid(Case k)
        {
            // both triangles not degenerate (the callers skip degenerate triangles), A a sliver
            return Oracle.Orient(k.Plane, k.S, k.P, k.Q) != 0 && Oracle.Orient(k.Plane, k.S, k.U, k.W) != 0;
        }

        static bool Library(Case k, double scale = 1)
        {
            var A = new Triangle3d(scale * k.S, scale * k.P, scale * k.Q);
            var B = new Triangle3d(scale * k.U, scale * k.S, scale * k.W);
            return ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 1, 2), ref A, new Index3i(3, 0, 4), ref B);
        }

        [Fact]
        public void Slivers_AtSharedVertex_AgreeWithOracle()
        {
            int n = 0, overlapping = 0, zeroNormal = 0;
            foreach (Case k in Slivers(30000, 11)) {
                if (Valid(k) == false)
                    continue;
                n++;
                bool expected = Oracle.CornersOverlap(k.Plane, k.S, k.P, k.Q, k.U, k.W);
                if (expected) overlapping++;
                if ((k.P - k.S).Cross(k.Q - k.S) == Vector3d.Zero) zeroNormal++;
                Assert.True(expected == Library(k), $"{k}: expected {expected}");
            }
            // the family is what it claims: mostly valid, both outcomes, and many slivers with a zero normal
            Assert.True(n > 25000, $"valid cases {n}");
            Assert.True(overlapping > 2000 && n - overlapping > 2000, $"overlapping {overlapping} of {n}");
            Assert.True(zeroNormal > 300, $"zero floating-point normal {zeroNormal} of {n}");
        }

        [Fact]
        public void Slivers_AtSharedEdge_AgreeWithOracle()
        {
            int n = 0, folded = 0, zeroNormal = 0;
            foreach (Case k in Slivers(30000, 12)) {
                // A = (s, p, q) the sliver, B = (p, s, u) across the edge s-p: d = u on either side
                if (Oracle.Orient(k.Plane, k.S, k.P, k.Q) == 0 || Oracle.Orient(k.Plane, k.S, k.P, k.U) == 0)
                    continue;
                n++;
                bool expected = Oracle.FoldOverlaps(k.Plane, k.S, k.P, k.Q, k.U);
                if (expected) folded++;
                if ((k.P - k.S).Cross(k.Q - k.S) == Vector3d.Zero) zeroNormal++;
                var A = new Triangle3d(k.S, k.P, k.Q);
                var B = new Triangle3d(k.P, k.S, k.U);
                Assert.True(expected == ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 1, 2), ref A, new Index3i(1, 0, 3), ref B), $"{k}: expected {expected}");
            }
            Assert.True(n > 25000, $"valid cases {n}");
            Assert.True(folded > 2000 && n - folded > 2000, $"folded {folded} of {n}");
            Assert.True(zeroNormal > 300, $"zero floating-point normal {zeroNormal} of {n}");
        }

        /// <summary>
        /// Similarity.All(): the 2^k scales give the same answers; translations and other scales keep the triangles in
        /// one axis plane, and the answers match the oracle on the transformed coordinates. A rotation leaves the
        /// plane (the rounded corners are no longer coplanar), so the pair is another case: only checked for a
        /// consistent answer under swapping the triangles.
        /// </summary>
        [Fact]
        public void Slivers_SimilarityInvariance()
        {
            var cases = new List<Case>();
            foreach (Case k in Slivers(3000, 13))
                if (Valid(k)) cases.Add(k);
            foreach (Similarity sim in Similarity.All()) {
                foreach (Case k in cases) {
                    var t = new Case { Plane = k.Plane, S = sim.Apply(k.S), P = sim.Apply(k.P), Q = sim.Apply(k.Q), U = sim.Apply(k.U), W = sim.Apply(k.W) };
                    bool got = Library(t);
                    var A = new Triangle3d(t.S, t.P, t.Q);
                    var B = new Triangle3d(t.U, t.S, t.W);
                    Assert.Equal(got, ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(3, 0, 4), ref B, new Index3i(0, 1, 2), ref A));
                    if (sim.IsExact)
                        Assert.True(Library(k) == got, $"{sim}: {k}");
                    bool planar = (t.Plane == Plane.X ? new[] { t.S.x, t.P.x, t.Q.x, t.U.x, t.W.x } : t.Plane == Plane.Y
                        ? new[] { t.S.y, t.P.y, t.Q.y, t.U.y, t.W.y } : new[] { t.S.z, t.P.z, t.Q.z, t.U.z, t.W.z }) is double[] cs
                        && Array.TrueForAll(cs, x => x == cs[0]);
                    if (planar && Valid(t))
                        Assert.True(Oracle.CornersOverlap(t.Plane, t.S, t.P, t.Q, t.U, t.W) == got, $"{sim}: {t}");
                }
            }
        }
    }
}
