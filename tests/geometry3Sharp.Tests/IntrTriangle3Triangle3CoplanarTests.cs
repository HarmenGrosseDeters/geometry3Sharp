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
            // off the plane: det(b - a, c - a, p - a) / |(b - a) x (c - a)|, both exact up to the final roundings
            Func<double, Dyadic> D = Dyadic.Of;
            Dyadic[] u = { D(t.V1.x) - D(t.V0.x), D(t.V1.y) - D(t.V0.y), D(t.V1.z) - D(t.V0.z) };
            Dyadic[] w = { D(t.V2.x) - D(t.V0.x), D(t.V2.y) - D(t.V0.y), D(t.V2.z) - D(t.V0.z) };
            Dyadic[] q = { D(p.x) - D(t.V0.x), D(p.y) - D(t.V0.y), D(p.z) - D(t.V0.z) };
            Dyadic[] cr = { u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0] };
            double num = (cr[0] * q[0] + cr[1] * q[1] + cr[2] * q[2]).ToDouble();
            double den = Math.Sqrt(cr.Sum(x => x.ToDouble() * x.ToDouble()));
            Assert.True(Math.Abs(num) <= tol * den, what + $": contact point {p} off the plane");
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

        // the three queries against the exact answer on any pairs; contacts on both triangles; for exactly coplanar
        // pairs the polygon
        static void CheckAgree(IEnumerable<(Triangle3d a, Triangle3d b)> pairs, int minHits, int minMisses)
        {
            int n = 0, hits = 0;
            string first = null;
            int mismatches = 0;
            foreach (var (A0, B0) in pairs) {
                Triangle3d A = A0, B = B0;
                n++;
                bool exact = ExactTriangleIntersection.Intersects(ref A, ref B);
                if (exact) hits++;
                var find = new IntrTriangle3Triangle3(A, B) { ReportCoplanarIntersection = true };
                bool[] got = { new IntrTriangle3Triangle3(A, B).Test(), IntrTriangle3Triangle3.Intersects(ref A, ref B), find.Find() };
                if (got.Any(g => g != exact)) {
                    mismatches++;
                    first = first ?? $"Test/Intersects/Find {got[0]}/{got[1]}/{got[2]}, exact {exact}: A {Format(A)} B {Format(B)}";
                    continue;
                }
                if (exact == false)
                    continue;
                Assert.True(find.Quantity > 0, "no contact: A " + Format(A) + " B " + Format(B));
                bool coplanar = Enumerable.Range(0, 3).All(k => ExactPredicates.Orient3d(A.V0, A.V1, A.V2, B[k]) == 0);
                if (coplanar)
                    Assert.True(find.Type == IntersectionType.Polygon, $"coplanar, {find.Type}: A {Format(A)} B {Format(B)}");
                foreach (Vector3d p in Contact(find)) {
                    AssertOn(A, p, "A " + Format(A));
                    AssertOn(B, p, "B " + Format(B));
                }
            }
            Assert.True(mismatches == 0, $"{mismatches} of {n} disagree; first: {first}");
            Assert.True(hits >= minHits && n - hits >= minMisses, $"intersecting {hits} of {n}");
        }

        // exactly coplanar pairs in oblique planes with full-mantissa coordinates (x, y on a 2^-30 grid, z = x + y or
        // y = 3 x - z exactly): their rounded normals differ by some ulps (the independent review of #253)
        static IEnumerable<(Triangle3d, Triangle3d)> Oblique(int count, int seed)
        {
            var rnd = new Random(seed);
            Func<double> g = () => Math.Round((rnd.NextDouble() * 2 - 1) * (1 << 30)) / (1 << 30);
            for (int i = 0; i < count; ++i) {
                bool first = i % 2 == 0;
                Func<Vector3d> P = () => {
                    double x = g(), y = g();
                    return first ? new Vector3d(x, y, x + y) : new Vector3d(x, 3 * x - y, y);
                };
                // B near A: a corner of A plus small offsets, so that many pairs touch or overlap
                var A = new Triangle3d(P(), P(), P());
                Vector3d c = A[rnd.Next(3)];
                Func<Vector3d> Q = () => {
                    double dx = g() * 0.5, dy = g() * 0.5;
                    double x = c.x + dx, y = first ? c.y + dy : c.z + dy;
                    return first ? new Vector3d(x, y, x + y) : new Vector3d(x, 3 * x - y, y);
                };
                var B = new Triangle3d(Q(), Q(), Q());
                if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B))
                    continue;
                if (Enumerable.Range(0, 3).Any(k => ExactPredicates.Orient3d(A.V0, A.V1, A.V2, B[k]) != 0))
                    continue;       // (a sum rounded off the plane)
                yield return (A, B);
            }
        }

        [Fact]
        public void ObliqueCoplanarPairs_AgreeWithExact()
        {
            var pairs = Oblique(20000, 25).ToList();
            Assert.True(pairs.Count > 5000, $"exactly coplanar pairs {pairs.Count}");
            // the family has pairs whose rounded normals are not parallel (the case the review found)
            Assert.Contains(pairs, p => {
                Vector3d n0 = (p.Item1.V1 - p.Item1.V0).UnitCross(p.Item1.V2 - p.Item1.V1), n1 = (p.Item2.V1 - p.Item2.V0).UnitCross(p.Item2.V2 - p.Item2.V1);
                return n0.UnitCross(n1) != Vector3d.Zero;
            });
            CheckAgree(pairs, 1000, 1000);
        }

        [Fact]
        public void ReviewExamples_AgreeWithExact()
        {
            Func<double, double, double, Vector3d> V = (x, y, z) => new Vector3d(x, y, z);
            double h = 3.725290298461914E-09;
            CheckAgree(new[] {
                (new Triangle3d(V(-0.05313338153064251, 0.5226241089403629, 0.4694907274097204), V(-0.19692603684961796, -0.3682477446272969, -0.5651737814769149), V(-0.1180697726085782, 0.07606480177491903, -0.04200497083365917)),
                 new Triangle3d(V(0.6470888508483768, -0.515655780211091, 0.1314330706372857), V(0.33488110546022654, -0.06590460427105427, 0.26897650118917227), V(-0.15839528385549784, 0.6103872358798981, 0.45199195202440023))),
                (new Triangle3d(V(-0.7328200126066804, -2.9070857455953956, 0.7086257077753544), V(0.09257932659238577, 0.37706670723855495, -0.09932872746139765), V(-0.32012033741921186, -1.2650095084682107, 0.3046484962105751)),
                 new Triangle3d(V(0.3681868268176913, 0.3999224230647087, 0.7046380573883653), V(0.9344277298077941, 3.6568649569526315, -0.8535817675292492), V(-0.7209979258477688, -2.345586416311562, 0.1825926387682557))),
                (new Triangle3d(V(-1, -1, 0), V(0.5, 0.5, 0), V(0.5, 1.125, 0)),
                 new Triangle3d(V(-1.625, -1.875, -h), V(-0.375, -0.625, -h), V(2, 2.25, h))),
            }, 3, 0);
        }

        [Fact]
        public void TiltedWithinTheEpsilon_TouchingAnEdge_AgreeWithExact()
        {
            // A has an edge on y = 0; B rises from z = -h at y = -1/4 to z = h at y = 1/4, so it crosses A's plane on
            // y = 0 (h = 2^-28..2^-59: within the epsilon of A's plane, not parallel to it)
            var rnd = new Random(26);
            var pairs = new List<(Triangle3d, Triangle3d)>();
            for (int i = 0; i < 4000; ++i) {
                double h = Math.Pow(2, -rnd.Next(28, 60));
                Func<double> g = () => rnd.Next(-16, 17) / 8.0;
                double x0 = g(), x1 = g();
                if (x0 == x1) continue;
                var A = new Triangle3d(new Vector3d(x0, 0, 0), new Vector3d(x1, 0, 0), new Vector3d(g(), rnd.Next(1, 9) / 8.0 * (rnd.Next(2) == 0 ? 1 : -1), 0));
                var B = new Triangle3d(new Vector3d(g(), -0.25, -h), new Vector3d(g(), -0.25, -h), new Vector3d(g(), 0.25, h));
                if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B)) continue;
                pairs.Add((A, B));
            }
            CheckAgree(pairs, 500, 500);
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
        public void NonFiniteInput_NoContact_NoException(double bad)
        {
            // (non-finite input has no defined contact: none is reported, and nothing throws)
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
