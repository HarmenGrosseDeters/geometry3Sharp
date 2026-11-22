using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// IntrTriangle3Triangle3 decides every pair of non-degenerate triangles exactly (closed triangles, touching counts):
    /// Test(), the static Intersects() and Find() agree with ExactTriangleIntersection on engineered contacts (shared
    /// vertex or edge, a corner on an edge or in the face, coplanar and parallel copies, 1-ulp and 1e-9 offsets) under
    /// every Similarity, with no absolute epsilon (the old separating axes and the 1e-8 plane thickness decided such
    /// pairs wrongly, about 2 % of them). Find() reports a contact lying on both triangles; the result is the same,
    /// bit for bit, at 2^k scales; it is cached; degenerate triangles touch only if they really do.
    /// </summary>
    public class IntrTriangle3Triangle3ExactDecisionTests
    {
        static double Perturb(Random r, double v)
        {
            if (v == 0)     // (not subnormal: a 2^k scale would lose it)
                return (r.Next(2) == 0 ? 1 : -1) * Math.Pow(2, -r.Next(30, 53));
            switch (r.Next(1, 6)) {
                case 1: return Math.BitIncrement(v);
                case 2: return Math.BitDecrement(v);
                case 3: return v + (r.NextDouble() - 0.5) * 1e-9;
                case 4: return v + (r.NextDouble() - 0.5) * 1e-12;
                default: return v + (r.NextDouble() - 0.5) * 1e-6;
            }
        }

        static Vector3d[] Corners(Random r, Vector3d[] other)
        {
            int L(int g) => r.Next(-g, g + 1);
            var t = new Vector3d[3];
            for (int i = 0; i < 3; i++) t[i] = new Vector3d(L(2), L(2), L(2));
            if (other != null) {
                switch (r.Next(6)) {
                    case 0: t[0] = other[r.Next(3)]; break;                                        // shared vertex
                    case 1: t[0] = other[0]; t[1] = other[1]; break;                               // shared edge
                    case 2: t[0] = other[0] + (r.Next(0, 5) / 4.0) * (other[1] - other[0]); break; // corner on an edge
                    case 3: t[0] = other[0] + 0.25 * (other[1] - other[0]) + 0.25 * (other[2] - other[0]); break; // corner in the face
                    case 4: for (int i = 0; i < 3; i++) t[i] = other[i] + new Vector3d(0, 0, r.Next(0, 2)); break; // coplanar or parallel copy
                }
            }
            for (int i = 0; i < 3; i++)
                if (r.Next(3) == 0) {
                    int ax = r.Next(3);
                    t[i][ax] = Perturb(r, t[i][ax]);
                }
            return t;
        }

        /// <summary>engineered pairs (non-degenerate), in original coordinates</summary>
        static List<(Vector3d[] a, Vector3d[] b)> Pairs(int count, int seed)
        {
            var r = new Random(seed);
            var list = new List<(Vector3d[], Vector3d[])>();
            while (list.Count < count) {
                var a = Corners(r, null);
                var b = Corners(r, a);
                var ta = new Triangle3d(a[0], a[1], a[2]);
                var tb = new Triangle3d(b[0], b[1], b[2]);
                if (ExactTriangleIntersection.IsDegenerate(ref ta) || ExactTriangleIntersection.IsDegenerate(ref tb))
                    continue;
                list.Add((a, b));
            }
            return list;
        }

        static Triangle3d Map(Vector3d[] t, Similarity s) => new Triangle3d(s.Apply(t[0]), s.Apply(t[1]), s.Apply(t[2]));

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Decisions_AgreeWithExact_OnEngineeredContacts(string name)
        {
            Similarity s = Similarity.ByName(name);
            int hits = 0, touches = 0;
            foreach (var (a, b) in Pairs(600, 17)) {
                Triangle3d ta = Map(a, s), tb = Map(b, s);
                if (ExactTriangleIntersection.IsDegenerate(ref ta) || ExactTriangleIntersection.IsDegenerate(ref tb))
                    continue;       // (the image of a perturbation below the resolution of the frame)
                bool truth = ExactTriangleIntersection.Intersects(ref ta, ref tb);
                bool coplanar = ExactPredicates.Orient3d(ta.V0, ta.V1, ta.V2, tb.V0) == 0 && ExactPredicates.Orient3d(ta.V0, ta.V1, ta.V2, tb.V1) == 0
                    && ExactPredicates.Orient3d(ta.V0, ta.V1, ta.V2, tb.V2) == 0;
                string pair = name + ": " + string.Join(" ", a.Concat(b).Select(v => v.ToString()));
                Assert.True(truth == IntrTriangle3Triangle3.Intersects(ref ta, ref tb), "Intersects " + pair);
                Assert.True(truth == new IntrTriangle3Triangle3(ta, tb).Test(), "Test " + pair);
                var f = new IntrTriangle3Triangle3(ta, tb) { ReportCoplanarIntersection = true };
                Assert.True(truth == f.Find(), "Find " + pair);
                // by default Find() leaves out exactly coplanar pairs
                Assert.True((truth && coplanar == false) == new IntrTriangle3Triangle3(ta, tb).Find(), "Find default " + pair);
                if (truth) hits++;
                if (truth && f.Type == IntersectionType.Point) touches++;
            }
            Assert.InRange(hits, 100, 590);     // the generator makes both outcomes common
        }

        [Fact]
        public void Find_ContactLiesOnBothTriangles()
        {
            int checkedPairs = 0;
            foreach (var (a, b) in Pairs(1500, 23)) {
                Triangle3d ta = new Triangle3d(a[0], a[1], a[2]), tb = new Triangle3d(b[0], b[1], b[2]);
                var f = new IntrTriangle3Triangle3(ta, tb) { ReportCoplanarIntersection = true };
                if (f.Find() == false)
                    continue;
                IEnumerable<Vector3d> pts = f.Type == IntersectionType.Polygon ? f.PolygonPoints
                    : f.Type == IntersectionType.Segment ? new[] { f.Points[0], f.Points[1] }
                    : f.Type == IntersectionType.Point ? new[] { f.Points[0] } : null;
                Assert.NotNull(pts);
                Assert.Equal(f.Type == IntersectionType.Segment ? 2 : f.Type == IntersectionType.Point ? 1 : f.PolygonPoints.Length, f.Quantity);
                foreach (Vector3d p0 in pts) {
                    Vector3d p = p0;
                    string where = f.Type + " " + string.Join(" ", a.Concat(b).Select(v => v.ToString("R")));
                    // (the distance from the closest point: the squared distance of DistanceSqr has a noise of 1e-8)
                    DistPoint3Triangle3.DistanceSqr(ref p, ref ta, out Vector3d qa, out _);
                    DistPoint3Triangle3.DistanceSqr(ref p, ref tb, out Vector3d qb, out _);
                    Assert.True(p.Distance(qa) < 1e-12, "on triangle 0 " + where);
                    Assert.True(p.Distance(qb) < 1e-12, "on triangle 1 " + where);
                }
                checkedPairs++;
            }
            Assert.True(checkedPairs > 300);
        }

        [Fact]
        public void Find_GenericCrossing_IsASegmentOfPositiveLength()
        {
            // two triangles crossing like a plus sign: exactly the segment x in [-1, 1], y = 0, z = 0 ... clipped by both
            var a = new Triangle3d(new Vector3d(-2, -1, 0), new Vector3d(2, -1, 0), new Vector3d(0, 3, 0));
            var b = new Triangle3d(new Vector3d(-1, 0, -1), new Vector3d(1, 0, -1), new Vector3d(0, 0, 2));
            var f = new IntrTriangle3Triangle3(a, b);
            Assert.True(f.Find());
            Assert.Equal(IntersectionType.Segment, f.Type);
            Assert.Equal(2, f.Quantity);
            double len = f.Points[0].Distance(f.Points[1]);
            Assert.InRange(len, 1.2, 2.0);
            Assert.All(new[] { f.Points[0], f.Points[1] }, p => Assert.True(Math.Abs(p.y) < 1e-14 && Math.Abs(p.z) < 1e-14));
        }

        [Fact]
        public void Find_TouchingAtOneCorner_IsAPoint_AndIsDecidedExactly()
        {
            var a = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            var up = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(0, 0, 1), new Vector3d(-1, -1, 1));
            var f = new IntrTriangle3Triangle3(a, up);
            Assert.True(f.Find());
            Assert.Equal(IntersectionType.Point, f.Type);
            Assert.Equal(1, f.Quantity);
            Assert.Equal(Vector3d.Zero, f.Points[0]);
            // 1e-300 above touching: a miss (the old plane thickness 1e-8 called it a touch)
            var away = new Triangle3d(new Vector3d(0, 0, 1e-300), new Vector3d(0, 0, 1), new Vector3d(-1, -1, 1));
            Assert.False(new IntrTriangle3Triangle3(a, away).Test());
            Assert.False(new IntrTriangle3Triangle3(a, away).Find());
        }

        [Fact]
        public void NoAbsoluteEpsilon_ParallelTrianglesFarApartAreNotTouching()
        {
            // old: triangles within 1e-8 of the plane counted as in it; and a vertex 5e-9 above the plane touched
            foreach (double h in new[] { 5e-9, 1e-12, 1e-300 }) {
                var a = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
                var b = new Triangle3d(new Vector3d(0.2, 0.2, h), new Vector3d(0.9, 0.2, h), new Vector3d(0.2, 0.9, h));
                Assert.False(IntrTriangle3Triangle3.Intersects(ref a, ref b), "parallel at h = " + h);
                var tilted = new Triangle3d(new Vector3d(0.2, 0.2, h), new Vector3d(0.9, 0.2, 2 * h), new Vector3d(0.2, 0.9, 3 * h));
                Assert.False(new IntrTriangle3Triangle3(a, tilted).Test(), "tilted at h = " + h);
                Assert.False(new IntrTriangle3Triangle3(a, tilted).Find(), "tilted Find at h = " + h);
            }
            // a tiny pair: everything is within 1e-8 of everything
            double s = Math.Pow(2, -40);
            var ta = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(s, 0, 0), new Vector3d(0, s, 0));
            var tb = new Triangle3d(new Vector3d(0, 0, 2 * s), new Vector3d(s, 0, 2 * s), new Vector3d(0, s, 2 * s));
            Assert.False(IntrTriangle3Triangle3.Intersects(ref ta, ref tb));
            Assert.False(new IntrTriangle3Triangle3(ta, tb).Find());
        }

        [Fact]
        public void DegenerateTriangle_TouchesOnlyIfItReallyDoes()
        {
            var a = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            // a segment (collinear corners) 5e-9 above the triangle: old distance tolerance 1e-8 said touching
            var seg = new Triangle3d(new Vector3d(0.2, 0.2, 5e-9), new Vector3d(0.4, 0.2, 5e-9), new Vector3d(0.3, 0.2, 5e-9));
            Assert.False(IntrTriangle3Triangle3.Intersects(ref a, ref seg));
            Assert.False(new IntrTriangle3Triangle3(a, seg).Test());
            Assert.False(new IntrTriangle3Triangle3(a, seg).Find());
            // the same segment on the triangle (a point of it inside): touching, Type Unknown, no points
            var on = new Triangle3d(new Vector3d(0.2, 0.2, 0), new Vector3d(0.4, 0.2, 0), new Vector3d(0.3, 0.2, 0));
            Assert.True(IntrTriangle3Triangle3.Intersects(ref a, ref on));
            var f = new IntrTriangle3Triangle3(a, on);
            Assert.True(f.Find());
            Assert.Equal(IntersectionType.Unknown, f.Type);
            Assert.Equal(0, f.Quantity);
            // a point-like triangle through the face
            var pt = new Triangle3d(new Vector3d(0.25, 0.25, 0), new Vector3d(0.25, 0.25, 0), new Vector3d(0.25, 0.25, 0));
            Assert.True(new IntrTriangle3Triangle3(a, pt).Test());
        }

        [Fact]
        public void Test_ReportsTheTypeOfWhatItKnows()
        {
            var a = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            var inPlane = new Triangle3d(new Vector3d(0.1, 0.1, 0), new Vector3d(0.5, 0.1, 0), new Vector3d(0.1, 0.5, 0));
            var crossing = new Triangle3d(new Vector3d(0.2, 0.2, -1), new Vector3d(0.2, 0.2, 1), new Vector3d(0.6, 0.2, 1));
            var apart = new Triangle3d(new Vector3d(5, 5, 5), new Vector3d(6, 5, 5), new Vector3d(5, 6, 5));
            var t1 = new IntrTriangle3Triangle3(a, inPlane); Assert.True(t1.Test()); Assert.Equal(IntersectionType.Plane, t1.Type);
            var t2 = new IntrTriangle3Triangle3(a, crossing); Assert.True(t2.Test()); Assert.Equal(IntersectionType.Unknown, t2.Type);
            var t3 = new IntrTriangle3Triangle3(a, apart); Assert.False(t3.Test()); Assert.Equal(IntersectionType.Empty, t3.Type);
        }

        public static IEnumerable<object[]> Pow2Names()
        {
            foreach (Similarity s in Similarity.All())
                if (s.IsExact || s == Similarity.Identity)
                    yield return new object[] { s.Name };
        }

        [Theory]
        [MemberData(nameof(Pow2Names))]
        public void Result_IsTheSameBitForBit_AtPowerOfTwoScales(string name)
        {
            Similarity s = Similarity.ByName(name);
            int compared = 0;
            foreach (var (a, b) in Pairs(500, 31)) {
                Triangle3d ta = new Triangle3d(a[0], a[1], a[2]), tb = new Triangle3d(b[0], b[1], b[2]);
                Triangle3d sa = Map(a, s), sb = Map(b, s);
                var f0 = new IntrTriangle3Triangle3(ta, tb) { ReportCoplanarIntersection = true };
                var f1 = new IntrTriangle3Triangle3(sa, sb) { ReportCoplanarIntersection = true };
                Assert.Equal(f0.Find(), f1.Find());
                Assert.Equal(f0.Type, f1.Type);
                Assert.Equal(f0.Quantity, f1.Quantity);
                Assert.Equal(new IntrTriangle3Triangle3(ta, tb).Test(), new IntrTriangle3Triangle3(sa, sb).Test());
                Assert.Equal(IntrTriangle3Triangle3.Intersects(ref ta, ref tb), IntrTriangle3Triangle3.Intersects(ref sa, ref sb));
                if (f0.Type == IntersectionType.Segment || f0.Type == IntersectionType.Point)
                    for (int i = 0; i < f0.Quantity; i++) {
                        Vector3d expect = s.Apply(f0.Points[i]);
                        Assert.True(expect.x == f1.Points[i].x, "point x");
                        Assert.True(expect.y == f1.Points[i].y, "point y");
                        Assert.True(expect.z == f1.Points[i].z, "point z");
                    }
                compared++;
            }
            Assert.Equal(500, compared);
        }

        /// <summary>
        /// An edge of A crosses an edge of B at P (exactly: the edges are +-u and +-w through the origin), the other
        /// ends of the contact lie on the same side of P (a segment) or on opposite sides (the single point P). The
        /// side is read from well separated floating-point values; the pair has no corner in the other's plane, so the
        /// generic crossing route would take it were it not for the coplanar pair of edges.
        /// </summary>
        [Fact]
        public void EdgeCrossingEdge_IsAPointOrASegment_ByTheSideOfTheOtherEnds()
        {
            var r = new Random(5);
            int points = 0, segments = 0;
            for (int it = 0; it < 4000 && (points < 60 || segments < 60); it++) {
                Vector3d V() => new Vector3d(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1);
                Vector3d u = V(), w = V(), a3 = V(), b3 = V();
                Vector3d nA = u.Cross(a3), nB = w.Cross(b3);
                Vector3d d = nA.Cross(nB);
                if (d.Length < 0.05 * nA.Length * nB.Length) continue;
                double[] sa = Slice(new[] { -u, u, a3 }, nB, d), sb = Slice(new[] { -w, w, b3 }, nA, d);
                if (sa == null || sb == null) continue;
                // one end of each slice is P (0); the other ends must be well away from it, on a side
                double oa = Math.Abs(sa[0]) > Math.Abs(sa[1]) ? sa[0] : sa[1], zeroA = Math.Abs(sa[0]) > Math.Abs(sa[1]) ? sa[1] : sa[0];
                double ob = Math.Abs(sb[0]) > Math.Abs(sb[1]) ? sb[0] : sb[1], zeroB = Math.Abs(sb[0]) > Math.Abs(sb[1]) ? sb[1] : sb[0];
                if (Math.Abs(oa) < 0.05 || Math.Abs(ob) < 0.05 || Math.Abs(zeroA) > 1e-9 || Math.Abs(zeroB) > 1e-9) continue;
                var A = new Triangle3d(-u, u, a3); var B = new Triangle3d(-w, w, b3);
                if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B)) continue;
                bool opposite = oa * ob < 0;
                var f = new IntrTriangle3Triangle3(A, B);
                Assert.True(f.Find());
                if (opposite) {
                    Assert.Equal(IntersectionType.Point, f.Type);
                    Assert.Equal(1, f.Quantity);
                    Assert.Equal(Vector3d.Zero, f.Points[0]);
                    points++;
                } else {
                    Assert.Equal(IntersectionType.Segment, f.Type);
                    Assert.Equal(2, f.Quantity);
                    Assert.True(f.Points[0].Distance(f.Points[1]) > 0.04, "a segment of the length of the shorter slice");
                    segments++;
                }
            }
            Assert.True(points >= 20 && segments >= 20, "points " + points + ", segments " + segments);
        }

        // the interval of d . x over the triangle's part in the plane through the origin with normal n (null if it
        // has no part in it or a corner is within 0.05 (relative) of it)
        static double[] Slice(Vector3d[] tri, Vector3d n, Vector3d d)
        {
            double[] dist = tri.Select(v => n.Dot(v) / n.Length).ToArray();
            if (dist.Any(x => Math.Abs(x) < 0.05) || (dist.All(x => x > 0) || dist.All(x => x < 0))) return null;
            var s = new List<double>();
            for (int i = 0; i < 3; i++) {
                int j = (i + 1) % 3;
                if (dist[i] * dist[j] < 0) {
                    double t = dist[i] / (dist[i] - dist[j]);
                    s.Add(d.Dot(tri[i] + t * (tri[j] - tri[i])) / d.Length);
                }
            }
            return s.Count == 2 ? s.ToArray() : null;
        }

        [Fact]
        public void NearlyTouchingEdges_NeverGiveASegmentOfZeroLength()
        {
            // the edge-edge touch above, with one corner of B moved by 1 ulp: the contact is a point or a segment of
            // length ~1e-16 (exactly decided), never two equal points under Quantity 2
            var r = new Random(9);
            int hits = 0;
            for (int it = 0; it < 3000; it++) {
                Vector3d V() => new Vector3d(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1);
                Vector3d u = V(), w = V(), a3 = V(), b3 = V();
                Vector3d w2 = w; int ax = r.Next(3);
                w2[ax] = r.Next(2) == 0 ? Math.BitIncrement(w2[ax]) : Math.BitDecrement(w2[ax]);
                var A = new Triangle3d(-u, u, a3); var B = new Triangle3d(-w, w2, b3);
                if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B)) continue;
                bool truth = ExactTriangleIntersection.Intersects(ref A, ref B);
                var f = new IntrTriangle3Triangle3(A, B);
                Assert.Equal(truth, f.Find());
                Assert.Equal(truth, IntrTriangle3Triangle3.Intersects(ref A, ref B));
                if (truth == false) continue;
                hits++;
                if (f.Quantity == 2)
                    Assert.NotEqual(f.Points[0], f.Points[1]);
                else
                    Assert.Equal(1, f.Quantity);
            }
            Assert.True(hits > 100, "hits " + hits);
        }

        [Fact]
        public void CornerNearlyInThePlane_IsDecidedByTheExactSigns()
        {
            // a corner of B at the rounded image of a point of A's plane: its exact side is noise (1e-17), and so is the
            // sign of its rounded distance
            var r = new Random(13);
            int hits = 0, misses = 0;
            for (int it = 0; it < 3000; it++) {
                Vector3d V() => new Vector3d(r.NextDouble() * 4 - 2, r.NextDouble() * 4 - 2, r.NextDouble() * 4 - 2);
                var A = new Triangle3d(V(), V(), V());
                double l1 = r.NextDouble() * 1.6 - 0.3, l2 = r.NextDouble() * 1.6 - 0.3;      // (in A, or just outside it)
                if (r.Next(3) == 0) { l1 = 1.0 / 3; l2 = 1.0 / 3; }
                Vector3d n = (A.V1 - A.V0).Cross(A.V2 - A.V0);
                if (n.Length < 1) continue;     // (not a sliver: its plane is well defined, the contact within 1e-12 of it)
                Vector3d on = A.V0 + l1 * (A.V1 - A.V0) + l2 * (A.V2 - A.V0);
                var B = new Triangle3d(on, on + 0.5 * n.Normalized + V() * 0.2, on - 0.5 * n.Normalized + V() * 0.2);
                if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B)) continue;
                bool truth = ExactTriangleIntersection.Intersects(ref A, ref B);
                var f = new IntrTriangle3Triangle3(A, B) { ReportCoplanarIntersection = true };
                Assert.Equal(truth, f.Find());
                Assert.Equal(truth, new IntrTriangle3Triangle3(A, B).Test());
                if (truth) {
                    hits++;
                    foreach (Vector3d q0 in f.Type == IntersectionType.Polygon ? f.PolygonPoints : f.Type == IntersectionType.Segment ? new[] { f.Points[0], f.Points[1] } : new[] { f.Points[0] }) {
                        Vector3d q = q0;
                        DistPoint3Triangle3.DistanceSqr(ref q, ref A, out Vector3d qa, out _);
                        DistPoint3Triangle3.DistanceSqr(ref q, ref B, out Vector3d qb, out _);
                        Assert.True(q.Distance(qa) < 1e-12 && q.Distance(qb) < 1e-12, "contact on both");
                    }
                } else misses++;
            }
            Assert.True(hits > 100 && misses > 100, "hits " + hits + ", misses " + misses);
        }

        [Fact]
        public void Find_IsCached_AndResetByTheSetters()
        {
            var a = new Triangle3d(new Vector3d(-2, -1, 0), new Vector3d(2, -1, 0), new Vector3d(0, 3, 0));
            var b = new Triangle3d(new Vector3d(-1, 0, -1), new Vector3d(1, 0, -1), new Vector3d(0, 0, 2));
            var f = new IntrTriangle3Triangle3(a, b);
            Assert.True(f.Find());
            Vector3d p0 = f.Points[0], p1 = f.Points[1];
            Assert.True(f.Find());
            Assert.Equal(IntersectionResult.Intersects, f.Result);
            Assert.Equal(p0, f.Points[0]);
            Assert.Equal(p1, f.Points[1]);
            Assert.Equal(2, f.Compute().Quantity);
            // a new triangle recomputes (a miss)
            f.Triangle1 = new Triangle3d(new Vector3d(9, 9, 9), new Vector3d(10, 9, 9), new Vector3d(9, 10, 9));
            Assert.Equal(IntersectionResult.NotComputed, f.Result);
            Assert.False(f.Find());
            Assert.Equal(IntersectionResult.NoIntersection, f.Result);
            Assert.False(f.Find());
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void NonFiniteInput_NeverIntersects(double bad)
        {
            var a = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(0, 2, 0));
            var b = new Triangle3d(new Vector3d(0.5, 0.5, -1), new Vector3d(0.5, 0.5, 1), new Vector3d(bad, 1, 0));
            Assert.False(IntrTriangle3Triangle3.Intersects(ref a, ref b));
            Assert.False(IntrTriangle3Triangle3.Intersects(ref b, ref a));
            Assert.False(new IntrTriangle3Triangle3(a, b).Test());
            Assert.False(new IntrTriangle3Triangle3(b, a).Find());
        }
    }
}
