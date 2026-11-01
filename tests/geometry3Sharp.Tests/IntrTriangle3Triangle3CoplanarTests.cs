using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// IntrTriangle3Triangle3 on coplanar and nearly parallel triangles: Test(), the static Intersects() and Find()
    /// (with ReportCoplanarIntersection) agree with the exact answer for closed triangles
    /// (ExactTriangleIntersection.Intersects), and Find() gives at least one contact point on both triangles whenever
    /// they intersect. Coplanar sliver families at a shared vertex (always touching) and at a shared edge, in the
    /// three axis planes and an oblique one; non-finite input; the AABB tree queries built on it.
    /// </summary>
    public class IntrTriangle3Triangle3CoplanarTests
    {
        // both triangles in a plane: an axis plane (k = 0, 1, 2: coordinate k is c) or the oblique plane y = 2 z
        static Vector3d Lift(int plane, double u, double v, double c)
        {
            switch (plane) {
                case 0: return new Vector3d(c, u, v);
                case 1: return new Vector3d(v, c, u);
                case 2: return new Vector3d(u, v, c);
                default: return new Vector3d(u, 2 * v, v);
            }
        }

        /// <summary>
        /// pairs (A, B) of coplanar triangles: A = (s, p, q) a sliver (q within 2^-20..2^-57 of s-p), B = (u, s, w)
        /// with rays near A's (sharing the vertex s), or, if sharedEdge, B = (p, s, u) across A's edge s-p
        /// </summary>
        static IEnumerable<(Triangle3d a, Triangle3d b)> Slivers(int count, int seed, bool sharedEdge)
        {
            var rnd = new Random(seed);
            for (int i = 0; i < count; ++i) {
                int plane = i % 4;
                double c = (i % 2 == 0) ? 0 : rnd.NextDouble() * 4 - 2;
                double su = rnd.NextDouble() * 2 - 1, sv = rnd.NextDouble() * 2 - 1;
                double angle = rnd.NextDouble() * 2 * Math.PI, len = 0.05 + rnd.NextDouble() * 0.3;
                double du = Math.Cos(angle), dv = Math.Sin(angle);
                double t = 0.2 + 0.6 * rnd.NextDouble(), h = Math.Pow(2, -(rnd.Next(2) == 0 ? rnd.Next(20, 50) : rnd.Next(50, 58))) * (rnd.Next(2) == 0 ? 1 : -1);
                Vector3d s = Lift(plane, su, sv, c), p = Lift(plane, su + len * du, sv + len * dv, c);
                Vector3d q = Lift(plane, su + t * len * du - h * dv, sv + t * len * dv + h * du, c);
                double off = (rnd.Next(3) == 0 ? Math.Pow(2, -rnd.Next(10, 50)) : rnd.NextDouble() * 0.5) * (rnd.Next(2) == 0 ? 1 : -1);
                double open = (rnd.Next(3) == 0 ? Math.Pow(2, -rnd.Next(20, 50)) : 0.05 + rnd.NextDouble()) * (rnd.Next(2) == 0 ? 1 : -1);
                double a1 = angle + off, a2 = a1 + open, l1 = 0.05 + rnd.NextDouble() * 0.3, l2 = 0.05 + rnd.NextDouble() * 0.3;
                Vector3d u = Lift(plane, su + l1 * Math.Cos(a1), sv + l1 * Math.Sin(a1), c);
                Vector3d w = Lift(plane, su + l2 * Math.Cos(a2), sv + l2 * Math.Sin(a2), c);
                var A = new Triangle3d(s, p, q);
                var B = sharedEdge ? new Triangle3d(p, s, u) : new Triangle3d(u, s, w);
                if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B))
                    continue;
                yield return (A, B);
            }
        }

        static string Format(Triangle3d t) => string.Join(";", Enumerable.Range(0, 3).Select(k =>
            string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", t[k].x, t[k].y, t[k].z)));

        // p on the closed triangle t, up to a tolerance for the rounding of the contact points: its signed distance to
        // each edge line within the plane, with exact (dyadic) cross products, and its distance to the plane. (Not
        // DistPoint3Triangle3: its closest point is off by up to 1e-5 for the slivers here.)
        static void AssertOn(Triangle3d t, Vector3d p, string what)
        {
            double size = Math.Max(1, Math.Max(t.V0.Length, Math.Max(t.V1.Length, t.V2.Length)));
            double tol = 64 * MathUtil.Epsilon * size;
            Vector3d n = (t.V1 - t.V0).Cross(t.V2 - t.V0);
            int drop = Math.Abs(n.x) >= Math.Abs(n.y) && Math.Abs(n.x) >= Math.Abs(n.z) ? 0 : Math.Abs(n.y) >= Math.Abs(n.z) ? 1 : 2;
            if (ExactPredicates.Orient2d(D2(t.V0, drop), D2(t.V1, drop), D2(t.V2, drop)) == 0)
                drop = Enumerable.Range(0, 3).First(k => ExactPredicates.Orient2d(D2(t.V0, k), D2(t.V1, k), D2(t.V2, k)) != 0);
            int sign = ExactPredicates.Orient2d(D2(t.V0, drop), D2(t.V1, drop), D2(t.V2, drop));
            for (int k = 0; k < 3; ++k) {
                Vector2d a = D2(t[k], drop), b = D2(t[(k + 1) % 3], drop), x = D2(p, drop);
                Dyadic cross = (Dyadic.Of(b.x) - Dyadic.Of(a.x)) * (Dyadic.Of(x.y) - Dyadic.Of(a.y)) - (Dyadic.Of(b.y) - Dyadic.Of(a.y)) * (Dyadic.Of(x.x) - Dyadic.Of(a.x));
                double dist = sign * cross.ToDouble() / (b - a).Length;
                Assert.True(dist >= -tol, what + $": contact point {p} {-dist:G3} outside edge {k}");
            }
            Assert.True(Math.Abs(n.Normalized.Dot(p - t.V0)) <= tol, what + $": contact point {p} off the plane");
        }

        static Vector2d D2(Vector3d v, int k) => k == 0 ? new Vector2d(v.y, v.z) : k == 1 ? new Vector2d(v.z, v.x) : new Vector2d(v.x, v.y);

        static Vector3d[] Contact(IntrTriangle3Triangle3 q) => q.Type == IntersectionType.Polygon
            ? q.PolygonPoints.Take(q.Quantity).ToArray() : Enumerable.Range(0, q.Quantity).Select(k => q.Points[k]).ToArray();

        // every query against the exact answer; returns the number of disagreements (false negatives, false
        // positives) of Test(), Intersects() and Find(), for the message
        static void CheckFamily(bool sharedEdge, int seed)
        {
            int n = 0, hits = 0, folded = 0;
            int[] fn = new int[3], fp = new int[3];
            string first = null;
            foreach (var (A0, B0) in Slivers(20000, seed, sharedEdge)) {
                Triangle3d A = A0, B = B0;
                n++;
                bool exact = ExactTriangleIntersection.Intersects(ref A, ref B);
                if (exact) hits++;
                var find = new IntrTriangle3Triangle3(A, B) { ReportCoplanarIntersection = true };
                bool[] got = { new IntrTriangle3Triangle3(A, B).Test(), IntrTriangle3Triangle3.Intersects(ref A, ref B), find.Find() };
                for (int k = 0; k < 3; ++k) {
                    if (got[k] == exact) continue;
                    if (exact) fn[k]++; else fp[k]++;
                    first = first ?? $"{(new[] { "Test", "Intersects", "Find" })[k]}: A {Format(A)} B {Format(B)}: expected {exact}";
                }
                if (got[2] && exact) {
                    Assert.True(find.Quantity > 0, "Find without contact points: A " + Format(A) + " B " + Format(B));
                    if (sharedEdge) {
                        // A = (s, p, q), B = (p, s, u): folded flat (q and u on the same side of s-p) overlap in a polygon,
                        // else they share only the edge
                        int axis = Enumerable.Range(0, 3).First(k => ExactPredicates.Orient2d(D2(A.V0, k), D2(A.V1, k), D2(A.V2, k)) != 0);
                        bool fold = ExactPredicates.Orient2d(D2(A.V0, axis), D2(A.V1, axis), D2(A.V2, axis))
                            * ExactPredicates.Orient2d(D2(A.V0, axis), D2(A.V1, axis), D2(B.V2, axis)) > 0;
                        if (fold) folded++;
                        Assert.True(fold ? find.Quantity >= 3 : find.Quantity == 2, $"fold {fold}, {find.Quantity} contact points: A {Format(A)} B {Format(B)}");
                    }
                    foreach (Vector3d p in Contact(find)) {
                        AssertOn(A, p, "A " + Format(A));
                        AssertOn(B, p, "B " + Format(B));
                    }
                }
            }
            Assert.True(first == null, $"false negatives Test/Intersects/Find {fn[0]}/{fn[1]}/{fn[2]}, false positives {fp[0]}/{fp[1]}/{fp[2]} of {n}; first: {first}");
            Assert.True(n > 18000, $"valid pairs {n}");
            Assert.Equal(n, hits);      // (closed triangles that share a vertex or an edge)
            if (sharedEdge)
                Assert.True(folded > 2000 && n - folded > 2000, $"folded {folded} of {n}");
        }

        [Fact]
        public void SliversAtSharedVertex_AgreeWithExact() => CheckFamily(false, 21);

        [Fact]
        public void SliversAtSharedEdge_AgreeWithExact() => CheckFamily(true, 22);

        [Fact]
        public void SliverFamilies_PowerOfTwoScales_SameAnswersAndScaledContacts()
        {
            foreach (Similarity sim in Similarity.All().Where(x => x.IsExact))
                foreach (bool edge in new[] { false, true })
                    foreach (var (A, B) in Slivers(500, 23, edge)) {
                        var q0 = new IntrTriangle3Triangle3(A, B) { ReportCoplanarIntersection = true };
                        Triangle3d SA = new Triangle3d(sim.Apply(A.V0), sim.Apply(A.V1), sim.Apply(A.V2));
                        Triangle3d SB = new Triangle3d(sim.Apply(B.V0), sim.Apply(B.V1), sim.Apply(B.V2));
                        var q1 = new IntrTriangle3Triangle3(SA, SB) { ReportCoplanarIntersection = true };
                        Assert.Equal(q0.Find(), q1.Find());
                        Assert.Equal(q0.Type, q1.Type);
                        Assert.Equal(q0.Quantity, q1.Quantity);
                        Vector3d[] c0 = Contact(q0), c1 = Contact(q1);
                        for (int k = 0; k < c0.Length; ++k)
                            Assert.Equal(c0[k], sim.Inverse(c1[k]));
                    }
        }

        [Fact]
        public void NearlyParallelCrossing_FindsTheSegment()
        {
            // two triangles at an angle of about 1e-10 (parallel in the sense of Test()): they cross along a segment
            var A = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(0, 4, 0));
            double e = 1e-10;
            var B = new Triangle3d(new Vector3d(-1, 1, -e), new Vector3d(5, 1, e), new Vector3d(-1, 2, -e));
            Assert.True(ExactTriangleIntersection.Intersects(ref A, ref B));
            var q = new IntrTriangle3Triangle3(A, B) { ReportCoplanarIntersection = true };
            Assert.True(q.Find());
            Assert.True(q.Quantity >= 1);
            foreach (Vector3d p in Contact(q)) { AssertOn(A, p, "A"); AssertOn(B, p, "B"); }
            Assert.True(new IntrTriangle3Triangle3(A, B).Test());
            // lifted by 2e: no contact
            var C = new Triangle3d(B.V0 + new Vector3d(0, 0, 3 * e), B.V1 + new Vector3d(0, 0, 3 * e), B.V2 + new Vector3d(0, 0, 3 * e));
            Assert.False(ExactTriangleIntersection.Intersects(ref A, ref C));
            Assert.False(new IntrTriangle3Triangle3(A, C) { ReportCoplanarIntersection = true }.Find());
            Assert.False(new IntrTriangle3Triangle3(A, C).Test());
            Assert.False(IntrTriangle3Triangle3.Intersects(ref A, ref C));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void NonFiniteInput_NoException_NoCoplanarContact(double bad)
        {
            // (non-finite input has no defined contact; the exact paths report none and do not throw. Infinite x or y
            // with z = 0 takes the floating-point path of upstream, unchanged)
            var A = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(0, 4, 0));
            foreach (int k in new[] { 0, 1, 2 }) {
                var v = new Vector3d(1, 1, 0);
                v[k] = bad;
                var B = new Triangle3d(v, new Vector3d(2, 1, 0), new Vector3d(1, 2, 0));
                foreach (bool swap in new[] { false, true }) {
                    Triangle3d x = swap ? B : A, y = swap ? A : B;
                    var q = new IntrTriangle3Triangle3(x, y) { ReportCoplanarIntersection = true };
                    bool f = q.Find(), t = new IntrTriangle3Triangle3(x, y).Test(), i = IntrTriangle3Triangle3.Intersects(ref x, ref y);
                    Assert.Equal(0, q.Quantity);
                    if (double.IsNaN(bad) || k == 2)
                        Assert.False(f || t || i);
                }
            }
        }

        static DMesh3 One(Triangle3d t)
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(t.V0), m.AppendVertex(t.V1), m.AppendVertex(t.V2));
            return m;
        }

        [Fact]
        public void AabbTree_TestIntersection_CoplanarSlivers_AgreeWithExact()
        {
            int n = 0;
            foreach (bool edge in new[] { false, true })
                foreach (var (A0, B0) in Slivers(400, 24, edge)) {
                    Triangle3d A = A0, B = B0;
                    var ta = new DMeshAABBTree3(One(A), true);
                    var tb = new DMeshAABBTree3(One(B), true);
                    Assert.Equal(ExactTriangleIntersection.Intersects(ref A, ref B), ta.TestIntersection(tb));
                    n++;
                }
            Assert.True(n > 700);
        }

        [Fact]
        public void AabbTree_FindAllIntersections_NearlyParallelCrossing()
        {
            var A = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(0, 4, 0));
            double e = 1e-10;
            var B = new Triangle3d(new Vector3d(-1, 1, -e), new Vector3d(5, 1, e), new Vector3d(-1, 2, -e));
            var ta = new DMeshAABBTree3(One(A), true);
            var tb = new DMeshAABBTree3(One(B), true);
            var r = ta.FindAllIntersections(tb);
            Assert.Equal(1, r.Points.Count + r.Segments.Count);
            foreach (var s in r.Segments) { AssertOn(A, s.point0, "A"); AssertOn(B, s.point1, "B"); }
            foreach (var p in r.Points) { AssertOn(A, p.point, "A"); AssertOn(B, p.point, "B"); }
        }
    }
}
