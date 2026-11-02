using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TriangleTriangleFilter: where it decides, it gives the answer of the exact
    /// ExactTriangleIntersection.IntersectsBeyondShared; on pairs in one axis plane it always decides.
    /// </summary>
    public class TriangleTriangleFilterTests
    {
        struct Pair
        {
            public Index3i Ia, Ib;
            public Triangle3d A, B;
        }

        // pairs of triangles with small integer coordinates in the plane coordinate[axis] = level: without shared
        // vertices, with one and with two (ids as in a mesh). Integer grids give many touching configurations (a
        // vertex on an edge, collinear edges, coincident vertices)
        // (integer = false: random coordinates in [0, 6), no touching configurations)
        static List<Pair> PlanarPairs(int count, int axis, double level, int seed, bool integer = true)
        {
            var rand = new Random(seed);
            Func<Vector3d> point = () => {
                double u = integer ? rand.Next(0, 7) : 6 * rand.NextDouble(), v = integer ? rand.Next(0, 7) : 6 * rand.NextDouble();
                return axis == 0 ? new Vector3d(level, u, v) : axis == 1 ? new Vector3d(v, level, u) : new Vector3d(u, v, level);
            };
            var pairs = new List<Pair>();
            while (pairs.Count < count) {
                int kind = pairs.Count % 3;
                Vector3d a0 = point(), a1 = point(), a2 = point(), b1 = point(), b2 = point(), b0 = point();
                var p = new Pair { Ia = new Index3i(0, 1, 2), A = new Triangle3d(a0, a1, a2) };
                if (kind == 0) {
                    p.Ib = new Index3i(3, 4, 5); p.B = new Triangle3d(b0, b1, b2);
                } else if (kind == 1) {
                    // vertex 1 shared, at a varying corner of B
                    int c = rand.Next(3);
                    p.Ib = c == 0 ? new Index3i(1, 4, 5) : c == 1 ? new Index3i(4, 1, 5) : new Index3i(4, 5, 1);
                    p.B = c == 0 ? new Triangle3d(a1, b1, b2) : c == 1 ? new Triangle3d(b1, a1, b2) : new Triangle3d(b1, b2, a1);
                } else {
                    // edge 1-2 shared, either direction
                    bool flip = rand.Next(2) == 0;
                    p.Ib = flip ? new Index3i(2, 1, 5) : new Index3i(1, 2, 5);
                    p.B = flip ? new Triangle3d(a2, a1, b2) : new Triangle3d(a1, a2, b2);
                }
                if (ExactTriangleIntersection.IsDegenerate(ref p.A) || ExactTriangleIntersection.IsDegenerate(ref p.B))
                    continue;
                pairs.Add(p);
            }
            return pairs;
        }

        static Pair Transformed(Pair p, Func<Vector3d, Vector3d> f)
        {
            return new Pair { Ia = p.Ia, Ib = p.Ib, A = new Triangle3d(f(p.A.V0), f(p.A.V1), f(p.A.V2)), B = new Triangle3d(f(p.B.V0), f(p.B.V1), f(p.B.V2)) };
        }

        static bool Exact(Pair p) => ExactTriangleIntersection.IntersectsBeyondShared(p.Ia, ref p.A, p.Ib, ref p.B);
        static int Filter(Pair p) => TriangleTriangleFilter.BeyondShared(p.Ia, ref p.A, p.Ib, ref p.B);

        // the decided pairs agree with the exact test; returns (separate, intersecting, undecided)
        static (int, int, int) CheckAgainstExact(IEnumerable<Pair> pairs, string what)
        {
            int sep = 0, hit = 0, und = 0;
            foreach (Pair pair in pairs) {
                Pair p = pair;
                int f = Filter(p);
                bool e = Exact(p);
                if (f == TriangleTriangleFilter.Undecided) { und++; continue; }
                Assert.True((f == TriangleTriangleFilter.Intersecting) == e,
                    $"{what}: filter {f}, exact {e}, ids {p.Ia} {p.Ib}, A {p.A.V0} {p.A.V1} {p.A.V2}, B {p.B.V0} {p.B.V1} {p.B.V2}");
                // the convenience method gives the exact answer
                Assert.Equal(e, TriangleTriangleFilter.IntersectsBeyondShared(p.Ia, ref p.A, p.Ib, ref p.B));
                if (e) hit++; else sep++;
            }
            return (sep, hit, und);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void AxisPlane_AlwaysDecided_AsTheExactTest(int axis)
        {
            List<Pair> pairs = PlanarPairs(30000, axis, 3.25, 11 + axis);
            var (sep, hit, und) = CheckAgainstExact(pairs, $"axis {axis}");
            Assert.Equal(0, und);
            // both outcomes in every sharing class
            for (int kind = 0; kind < 3; ++kind) {
                var k = pairs.Where((p, i) => i % 3 == kind).ToList();
                Assert.Contains(k, p => Exact(p));
                Assert.Contains(k, p => Exact(p) == false);
            }
            Assert.True(sep > 3000 && hit > 3000, $"{sep} separate, {hit} intersecting");
        }

        // pairs with A in the axis plane coordinate[axis] = level (small integer coordinates in it) and B with its
        // corners on the plane or on one side of it (offsets 0, 0, 1, 2 along the axis: it touches the plane in a
        // vertex or an edge, or not at all), without shared vertices, with one and with two; crossing = true gives
        // each B corners on both sides. The touching pairs are what CAD-like parts give (a cap in an axis plane and a
        // wall that meets it at the rim): Orient3d is exactly 0 there, the exact test's slow stage
        static List<Pair> AxisPlaneTouchingPairs(int count, int axis, double level, int seed, bool crossing = false)
        {
            var rand = new Random(seed);
            Func<double, Vector3d> point = (d) => {
                double u = rand.Next(0, 7), v = rand.Next(0, 7), w = level + d;
                return axis == 0 ? new Vector3d(w, u, v) : axis == 1 ? new Vector3d(v, w, u) : new Vector3d(u, v, w);
            };
            var offsets = new double[] { 0, 0, 1, 2 };
            var pairs = new List<Pair>();
            while (pairs.Count < count) {
                int kind = pairs.Count % 3;
                double sign = rand.Next(2) == 0 ? -1 : 1;
                Func<double> off = () => sign * offsets[rand.Next(offsets.Length)];
                Vector3d a0 = point(0), a1 = point(0), a2 = point(0);
                Vector3d b0 = point(off()), b1 = point(off()), b2 = point(crossing ? -sign * (1 + rand.Next(2)) : off());
                if (crossing) b1 = point(sign * (1 + rand.Next(2)));
                var p = new Pair { Ia = new Index3i(0, 1, 2), A = new Triangle3d(a0, a1, a2) };
                if (kind == 0) {
                    p.Ib = new Index3i(3, 4, 5); p.B = new Triangle3d(b0, b1, b2);
                } else if (kind == 1) {
                    int c = rand.Next(3);
                    p.Ib = c == 0 ? new Index3i(1, 4, 5) : c == 1 ? new Index3i(4, 1, 5) : new Index3i(4, 5, 1);
                    p.B = c == 0 ? new Triangle3d(a1, b1, b2) : c == 1 ? new Triangle3d(b1, a1, b2) : new Triangle3d(b1, b2, a1);
                } else {
                    bool flip = rand.Next(2) == 0;
                    p.Ib = flip ? new Index3i(2, 1, 5) : new Index3i(1, 2, 5);
                    p.B = flip ? new Triangle3d(a2, a1, b2) : new Triangle3d(a1, a2, b2);
                }
                if (ExactTriangleIntersection.IsDegenerate(ref p.A) || ExactTriangleIntersection.IsDegenerate(ref p.B))
                    continue;
                // (B in the plane too: the planar case of AxisPlane_AlwaysDecided_AsTheExactTest)
                if (p.B.V0[axis] == level && p.B.V1[axis] == level && p.B.V2[axis] == level)
                    continue;
                if (rand.Next(2) == 0) { var t = p.A; p.A = p.B; p.B = t; var ti = p.Ia; p.Ia = p.Ib; p.Ib = ti; }
                pairs.Add(p);
            }
            return pairs;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void AxisPlaneAndTouching_AlwaysDecided_AsTheExactTest(int axis)
        {
            // one triangle in an axis plane, the other on one side of it, touching it or not: decided both ways,
            // either argument order
            List<Pair> pairs = AxisPlaneTouchingPairs(30000, axis, 2.5, 41 + axis);
            var (sep, hit, und) = CheckAgainstExact(pairs, $"axis {axis}");
            Assert.Equal(0, und);
            for (int kind = 0; kind < 3; ++kind) {
                var k = pairs.Where((p, i) => i % 3 == kind).ToList();
                Assert.Contains(k, p => Exact(p) == false);
                // (a shared edge with the far corner off the plane never intersects beyond it)
                if (kind < 2)
                    Assert.Contains(k, p => Exact(p));
            }
            Assert.True(sep > 3000 && hit > 3000, $"{sep} separate, {hit} intersecting");
            foreach (Pair p in pairs) {
                Pair q = new Pair { Ia = p.Ib, Ib = p.Ia, A = p.B, B = p.A };
                Assert.Equal(Filter(p), Filter(q));
            }
        }

        [Fact]
        public void AxisPlaneAndDegenerate_NeverWronglyDecided()
        {
            // A in the plane z = 0, B degenerate (collinear corners, or two at one point) touching the plane: without
            // shared vertices the exact test takes B as the closed segment, and the filter agrees where it decides;
            // with a shared vertex the filter leaves a degenerate B to the exact test (whose callers skip it)
            var rand = new Random(53);
            Func<double, Vector3d> point = (z) => new Vector3d(rand.Next(0, 7), rand.Next(0, 7), z);
            int decided = 0, shared = 0;
            for (int n = 0; n < 20000; ++n) {
                Vector3d a0 = point(0), a1 = point(0), a2 = point(0);
                var p = new Pair { Ia = new Index3i(0, 1, 2), A = new Triangle3d(a0, a1, a2) };
                if (ExactTriangleIntersection.IsDegenerate(ref p.A))
                    continue;
                if (n % 2 == 0) {
                    // a segment from the plane up, or lying on it with a third corner on it
                    Vector3d b0 = point(0), b1 = rand.Next(2) == 0 ? point(0) : point(rand.Next(1, 3));
                    p.Ib = new Index3i(3, 4, 5); p.B = new Triangle3d(b0, b1, b0 + rand.Next(-1, 3) * (b1 - b0));
                    if (p.B.V0.z == 0 && p.B.V1.z == 0 && p.B.V2.z == 0) continue;
                    int f = Filter(p);
                    if (f != TriangleTriangleFilter.Undecided) {
                        decided++;
                        Assert.True((f == TriangleTriangleFilter.Intersecting) == Exact(p), $"filter {f}: A {p.A.V0} {p.A.V1} {p.A.V2}, B {p.B.V0} {p.B.V1} {p.B.V2}");
                    }
                } else {
                    // vertex 1 shared, a second corner of B at the same point (another id), the third above the plane
                    p.Ib = new Index3i(1, 4, 5); p.B = new Triangle3d(a1, a1, point(rand.Next(1, 3)));
                    Assert.Equal(TriangleTriangleFilter.Undecided, Filter(p));
                    shared++;
                }
            }
            Assert.True(decided > 3000 && shared > 5000, $"{decided} decided, {shared} shared");
        }

        // the projection along axis k that TriangleTriangleFilter uses: the other two coordinates in cyclic order
        static Vector2d DropCyclic(Vector3d v, int k) => k == 0 ? new Vector2d(v.y, v.z) : k == 1 ? new Vector2d(v.z, v.x) : new Vector2d(v.x, v.y);

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void AxisPlaneFactorization_SameSignAsOrient3d(int k)
        {
            // the identity the axis-plane case rests on: for p0, p1, p2 with p0[k] = p1[k] = p2[k] = h,
            // Orient3d(p0, p1, p2, x) = (h - x[k]) * Orient2d of the projections along k in cyclic order (the column k of
            // the determinant is h - x[k] in every row; a cyclic order of the columns is an even permutation), so its
            // sign is sign(h - x[k]) * Orient2d. Checked against the exact Orient3d on random and adversarial inputs:
            // x on the plane and one ulp off it, degenerate projections (collinear, coincident corners), large and
            // tiny magnitudes, far from the origin, every order of the corners
            var rand = new Random(61 + k);
            double[] levels = { 0, 2.5, -3, 1e-300, -4.9e-324, 1e300, 632117.25, 5712043.5, 0.1 };
            double[] scales = { 1, 1e-200, 1e200, 1e-3, 1e5 };
            int n = 0, zero = 0, degenerate = 0;
            Func<double, Vector3d> at = (hk) => {
                double u = rand.Next(-4, 5) + (rand.Next(3) == 0 ? rand.NextDouble() : 0), v = rand.Next(-4, 5) + (rand.Next(3) == 0 ? rand.NextDouble() : 0);
                return k == 0 ? new Vector3d(hk, u, v) : k == 1 ? new Vector3d(v, hk, u) : new Vector3d(u, v, hk);
            };
            foreach (double h in levels)
                foreach (double sc in scales)
                    for (int t = 0; t < 400; ++t) {
                        Func<Vector3d, Vector3d> scale = (q) => {
                            var r = q * sc; r[k] = q[k]; return r;
                        };
                        Vector3d p0 = scale(at(h)), p1 = scale(at(h)), p2 = scale(at(h));
                        if (t % 5 == 0) p2 = p0 + rand.Next(-2, 3) * (p1 - p0);       // collinear or coincident
                        p2[k] = h;
                        double xk = (t % 4) switch {
                            0 => h, 1 => Math.BitIncrement(h), 2 => Math.BitDecrement(h), _ => h + sc * rand.Next(-3, 4)
                        };
                        Vector3d x = scale(at(xk)); x[k] = xk;
                        int o2 = ExactPredicates.Orient2d(DropCyclic(p0, k), DropCyclic(p1, k), DropCyclic(p2, k));
                        int expected = Math.Sign(h.CompareTo(xk)) * o2;
                        Vector3d[] P = { p0, p1, p2 };
                        foreach (var perm in new[] { new[] { 0, 1, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 1, 0, 2 }, new[] { 0, 2, 1 }, new[] { 2, 1, 0 } }) {
                            bool even = perm[0] == 0 && perm[1] == 1 || perm[0] == 1 && perm[1] == 2 || perm[0] == 2 && perm[1] == 0;
                            int o3 = ExactPredicates.Orient3d(P[perm[0]], P[perm[1]], P[perm[2]], x);
                            Assert.True((even ? expected : -expected) == o3, $"k {k}: {P[perm[0]]} {P[perm[1]]} {P[perm[2]]} {x}: Orient3d {o3}, expected {(even ? expected : -expected)}");
                        }
                        n++;
                        if (expected == 0) zero++;
                        if (o2 == 0) degenerate++;
                    }
            Assert.True(zero > 0.2 * n && degenerate > 0.1 * n && n - zero > 0.4 * n, $"{n} cases: {zero} zero, {degenerate} degenerate");
        }

        [Fact]
        public void AxisPlaneAndCrossing_DecidedPairsAgree()
        {
            // the other triangle with corners on both sides of the plane: left to the exact test, except where the
            // projection decides
            List<Pair> pairs = AxisPlaneTouchingPairs(30000, 1, -1.5, 43, crossing: true);
            var (sep, hit, und) = CheckAgainstExact(pairs, "crossing");
            Assert.True(und > 1000, $"{sep} separate, {hit} intersecting, {und} undecided");
        }

        [Fact]
        public void AxisPlaneAndTouching_EveryFrame_DecidedPairsAgree_ExactScalesSameAnswer()
        {
            List<Pair> pairs = AxisPlaneTouchingPairs(9000, 2, 1.5, 47);
            int[] reference = pairs.Select(Filter).ToArray();
            foreach (Similarity s in Similarity.All()) {
                var moved = pairs.Select(p => Transformed(p, s.Apply)).ToList();
                var (sep, hit, und) = CheckAgainstExact(moved, s.Name);
                Assert.True(sep > 1000, $"{s.Name}: {sep} separate, {und} undecided");
                if (s.IsExact)
                    Assert.Equal(reference, moved.Select(Filter).ToArray());
            }
        }

        [Fact]
        public void AxisPlane_SwappedArguments_SameAnswer()
        {
            foreach (Pair p in PlanarPairs(6000, 2, 0, 5)) {
                Pair q = new Pair { Ia = p.Ib, Ib = p.Ia, A = p.B, B = p.A };
                Assert.Equal(Filter(p), Filter(q));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void NearlyCoplanar_DecidedPairsAgree_MostSeparatePairsDecided(bool integer)
        {
            // the planar pairs with the z of every vertex moved by a tiny amount: no longer in an axis plane, so the
            // exact test needs its slow stage; the filter decides by the projection
            var rand = new Random(3);
            var pairs = PlanarPairs(30000, 2, 0, 7, integer).Select(p => {
                // the same id gets the same offset (shared vertices stay shared)
                var q = p;
                var offsets = new double[6];
                for (int i = 0; i < 6; ++i) offsets[i] = 1e-9 * (rand.NextDouble() - 0.5);
                Func<Vector3d, int, Vector3d> lift = (v, id) => new Vector3d(v.x, v.y, offsets[id]);
                q.A = new Triangle3d(lift(p.A.V0, p.Ia.a), lift(p.A.V1, p.Ia.b), lift(p.A.V2, p.Ia.c));
                q.B = new Triangle3d(lift(p.B.V0, p.Ib.a), lift(p.B.V1, p.Ib.b), lift(p.B.V2, p.Ib.c));
                return q;
            }).ToList();
            var (sep, hit, und) = CheckAgainstExact(pairs, "nearly coplanar");
            int exactSeparate = pairs.Count(p => Exact(p) == false);
            // (the filter never claims an intersection outside an axis plane)
            Assert.Equal(0, hit);
            // (many of these random pairs overlap in the projection, one just above the other, or touch there: only
            // the exact test separates them. Mesh neighbours do not overlap: TiltedPlate_NeighbourPairs_AlmostAllDecided)
            Assert.True(sep > 0.3 * exactSeparate, $"{sep} of {exactSeparate} separate pairs decided, {und} undecided");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void TiltedPlate_NeighbourPairs_AlmostAllDecided(int tilt)
        {
            // the pairs with touching or overlapping boxes, the candidates of the self-intersection checks
            DMesh3 mesh = TiltedJitterPlate(20, 4, tilt);
            var pairs = new List<Pair>();
            int[] ids = mesh.TriangleIndices().ToArray();
            foreach (int i in ids)
                foreach (int j in ids) {
                    if (j <= i) continue;
                    Index3i ia = mesh.GetTriangle(i), ib = mesh.GetTriangle(j);
                    var p = new Pair { Ia = ia, Ib = ib, A = new Triangle3d(mesh.GetVertex(ia.a), mesh.GetVertex(ia.b), mesh.GetVertex(ia.c)),
                        B = new Triangle3d(mesh.GetVertex(ib.a), mesh.GetVertex(ib.b), mesh.GetVertex(ib.c)) };
                    AxisAlignedBox3d ba = new AxisAlignedBox3d(p.A.V0), bb = new AxisAlignedBox3d(p.B.V0);
                    ba.Contain(p.A.V1); ba.Contain(p.A.V2); bb.Contain(p.B.V1); bb.Contain(p.B.V2);
                    if (ba.Min.x <= bb.Max.x && bb.Min.x <= ba.Max.x && ba.Min.y <= bb.Max.y && bb.Min.y <= ba.Max.y && ba.Min.z <= bb.Max.z && bb.Min.z <= ba.Max.z)
                        pairs.Add(p);
                }
            var (sep, hit, und) = CheckAgainstExact(pairs, "tilted plate");
            Assert.Equal(0, hit);
            Assert.True(pairs.Count > 4000 && und < 0.01 * pairs.Count, $"{pairs.Count} pairs: {sep} separate, {und} undecided");
        }

        [Fact]
        public void ExtrudedPart_CapPairs_AllDecided()
        {
            // a CAD-like part: an extruded star with caps as fans around their centres and walls in no axis plane. The
            // candidates of the self-intersection checks (touching or overlapping boxes) include cap and wall triangles
            // that meet at the rim without a shared vertex: each corner of the wall on the cap's plane or above it
            DMesh3 mesh = new DMesh3();
            int n = 24;
            int cb = mesh.AppendVertex(new Vector3d(0, 0, 0)), ct = mesh.AppendVertex(new Vector3d(0, 0, 0.3));
            var b = new int[n]; var t = new int[n];
            for (int i = 0; i < n; ++i) {
                double r = i % 2 == 0 ? 1 : 0.6, a = 2 * Math.PI * i / n;
                b[i] = mesh.AppendVertex(new Vector3d(r * Math.Cos(a), r * Math.Sin(a), 0));
                t[i] = mesh.AppendVertex(new Vector3d(r * Math.Cos(a), r * Math.Sin(a), 0.3));
            }
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                mesh.AppendTriangle(cb, b[j], b[i]); mesh.AppendTriangle(ct, t[i], t[j]);
                mesh.AppendTriangle(b[i], b[j], t[j]); mesh.AppendTriangle(b[i], t[j], t[i]);
            }
            var pairs = new List<Pair>();
            int[] ids = mesh.TriangleIndices().ToArray();
            foreach (int i in ids)
                foreach (int j in ids) {
                    if (j <= i) continue;
                    Index3i ia = mesh.GetTriangle(i), ib = mesh.GetTriangle(j);
                    var p = new Pair { Ia = ia, Ib = ib, A = new Triangle3d(mesh.GetVertex(ia.a), mesh.GetVertex(ia.b), mesh.GetVertex(ia.c)),
                        B = new Triangle3d(mesh.GetVertex(ib.a), mesh.GetVertex(ib.b), mesh.GetVertex(ib.c)) };
                    AxisAlignedBox3d ba = new AxisAlignedBox3d(p.A.V0), bb = new AxisAlignedBox3d(p.B.V0);
                    ba.Contain(p.A.V1); ba.Contain(p.A.V2); bb.Contain(p.B.V1); bb.Contain(p.B.V2);
                    if (ba.Min.x <= bb.Max.x && bb.Min.x <= ba.Max.x && ba.Min.y <= bb.Max.y && bb.Min.y <= ba.Max.y && ba.Min.z <= bb.Max.z && bb.Min.z <= ba.Max.z)
                        pairs.Add(p);
                }
            var (sep, hit, und) = CheckAgainstExact(pairs, "extruded part");
            Assert.Equal(0, hit);
            // every pair with a cap triangle is decided (only wall pairs at the valleys of the star, which meet at an
            // angle no axis projection separates, are left to the exact test, where Orient3d is not 0)
            Func<Triangle3d, bool> cap = (T) => T.V0.z == T.V1.z && T.V0.z == T.V2.z;
            var withCap = pairs.Where(p => cap(p.A) || cap(p.B)).ToList();
            Assert.True(withCap.Count > 800, $"{withCap.Count} of {pairs.Count} pairs with a cap triangle");
            Assert.DoesNotContain(withCap, p => Filter(p) == TriangleTriangleFilter.Undecided);
            Assert.True(und < 0.1 * pairs.Count, $"{und} of {pairs.Count} undecided");
        }

        // coplanar pairs at a shared vertex and at a shared edge where A is a sliver (its third corner within 2^-20..2^-57 of
        // the segment of the other two, so its floating-point normal is often 0): the class ExactTriangleIntersection got
        // wrong before #249, in the axis planes and in the plane y = 2 z
        static List<Pair> SliverPairs(int count, int seed, out int zeroNormal)
        {
            var rnd = new Random(seed);
            var pairs = new List<Pair>();
            zeroNormal = 0;
            for (int i = 0; pairs.Count < count; ++i) {
                int plane = i % 4;
                double c = (i % 2 == 0) ? 0 : 4 * rnd.NextDouble() - 2;
                Func<double, double, Vector3d> lift = (u, v) => plane == 0 ? new Vector3d(c, u, v) : plane == 1 ? new Vector3d(v, c, u)
                    : plane == 2 ? new Vector3d(u, v, c) : new Vector3d(u, 2 * v, v);
                double su = 2 * rnd.NextDouble() - 1, sv = 2 * rnd.NextDouble() - 1, angle = 2 * Math.PI * rnd.NextDouble();
                double len = 0.05 + 0.3 * rnd.NextDouble(), t = 0.2 + 0.6 * rnd.NextDouble();
                double h = Math.Pow(2, -rnd.Next(20, 58)) * (rnd.Next(2) == 0 ? 1 : -1);
                double du = Math.Cos(angle), dv = Math.Sin(angle);
                Vector3d s = lift(su, sv), p = lift(su + len * du, sv + len * dv), q = lift(su + t * len * du - h * dv, sv + t * len * dv + h * du);
                double off = (rnd.Next(3) == 0 ? Math.Pow(2, -rnd.Next(10, 50)) : 0.5 * rnd.NextDouble()) * (rnd.Next(2) == 0 ? 1 : -1);
                double open = (rnd.Next(3) == 0 ? Math.Pow(2, -rnd.Next(20, 50)) : 0.05 + rnd.NextDouble()) * (rnd.Next(2) == 0 ? 1 : -1);
                double a1 = angle + off, a2 = a1 + open, l1 = 0.05 + 0.3 * rnd.NextDouble(), l2 = 0.05 + 0.3 * rnd.NextDouble();
                Vector3d u3 = lift(su + l1 * Math.Cos(a1), sv + l1 * Math.Sin(a1)), w3 = lift(su + l2 * Math.Cos(a2), sv + l2 * Math.Sin(a2));
                var pair = new Pair { Ia = new Index3i(0, 1, 2), A = new Triangle3d(s, p, q) };
                if (i % 2 == 0) { pair.Ib = new Index3i(3, 0, 4); pair.B = new Triangle3d(u3, s, w3); }     // shared vertex s
                else { pair.Ib = new Index3i(1, 0, 3); pair.B = new Triangle3d(p, s, u3); }                // shared edge s-p
                if (ExactTriangleIntersection.IsDegenerate(ref pair.A) || ExactTriangleIntersection.IsDegenerate(ref pair.B))
                    continue;
                if ((p - s).Cross(q - s) == Vector3d.Zero) zeroNormal++;
                pairs.Add(pair);
            }
            return pairs;
        }

        [Fact]
        public void Slivers_AgreeWithTheExactTest()
        {
            // since #249 the exact test is right on these too: the filter and it agree everywhere
            int zeroNormal;
            List<Pair> pairs = SliverPairs(40000, 31, out zeroNormal);
            var (sep, hit, und) = CheckAgainstExact(pairs, "slivers");
            Assert.True(zeroNormal > 150, $"zero floating-point normal {zeroNormal}");
            Assert.True(sep > 2000 && hit > 2000, $"{sep} separate, {hit} intersecting, {und} undecided");
        }

        [Fact]
        public void General3D_DecidedPairsAgree()
        {
            var rand = new Random(9);
            Func<Vector3d> point = () => new Vector3d(rand.Next(0, 5), rand.Next(0, 5), rand.Next(0, 5));
            var pairs = new List<Pair>();
            while (pairs.Count < 30000) {
                var p = new Pair { Ia = new Index3i(0, 1, 2), A = new Triangle3d(point(), point(), point()) };
                int kind = pairs.Count % 3;
                if (kind == 0) { p.Ib = new Index3i(3, 4, 5); p.B = new Triangle3d(point(), point(), point()); }
                else if (kind == 1) { p.Ib = new Index3i(4, 0, 5); p.B = new Triangle3d(point(), p.A.V0, point()); }
                else { p.Ib = new Index3i(2, 1, 5); p.B = new Triangle3d(p.A.V2, p.A.V1, point()); }
                if (ExactTriangleIntersection.IsDegenerate(ref p.A) || ExactTriangleIntersection.IsDegenerate(ref p.B))
                    continue;
                pairs.Add(p);
            }
            var (sep, hit, und) = CheckAgainstExact(pairs, "general");
            Assert.True(sep > 1000 && hit > 100, $"{sep} separate, {hit} intersecting, {und} undecided");
        }

        [Fact]
        public void Intersects_SameAsExactTriangleIntersection()
        {
            // the closed test of IntrTriangle3Triangle3's exact route: also for degenerate triangles, in an axis plane,
            // nearly coplanar and in general position
            var rand = new Random(23);
            int hits = 0, n = 0;
            for (int k = 0; k < 60000; ++k) {
                int family = k % 3;
                Func<Vector3d> point = () => new Vector3d(rand.Next(0, 6), rand.Next(0, 6), family == 0 ? 2.5 : family == 1 ? 1e-9 * rand.NextDouble() : rand.Next(0, 6));
                var A = new Triangle3d(point(), point(), point());
                var B = new Triangle3d(point(), point(), point());
                if (k % 7 == 0) A.V2 = A.V0 + 2 * (A.V1 - A.V0);      // collinear
                bool expected = ExactTriangleIntersection.Intersects(ref A, ref B);
                Assert.True(expected == TriangleTriangleFilter.Intersects(ref A, ref B), $"A {A.V0} {A.V1} {A.V2}, B {B.V0} {B.V1} {B.V2}");
                n++;
                if (expected) hits++;
            }
            Assert.True(hits > 5000 && n - hits > 5000, $"{hits} of {n} intersecting");
        }

        [Fact]
        public void NonFinite_Undecided()
        {
            foreach (bool inB in new[] { false, true })
                foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                    var p = PlanarPairs(1, 2, 0, 1)[0];
                    if (inB) p.B.V2 = new Vector3d(0, bad, 0); else p.A.V1 = new Vector3d(bad, 0, 0);
                    Assert.Equal(TriangleTriangleFilter.Undecided, Filter(p));
                }
        }

        [Fact]
        public void DegenerateTriangles_NeverWronglyDecided()
        {
            // the callers skip degenerate (collinear) triangles, but the filter must not rely on it: in an axis plane
            // without shared vertices the exact test handles them as segments; with shared vertices the filter leaves
            // degenerate pairs to it
            var rand = new Random(17);
            Func<Vector3d> point = () => new Vector3d(rand.Next(0, 7), rand.Next(0, 7), 0);
            int degenerate = 0, sharedUndecided = 0;
            for (int n = 0; n < 40000; ++n) {
                Vector3d a0 = point(), a1 = point(), b0 = point(), b1 = point(), b2 = point();
                // A collinear: its third point on the line through the first two (or equal to one of them)
                Vector3d a2 = a0 + rand.Next(-2, 3) * (a1 - a0);
                var p = new Pair { Ia = new Index3i(0, 1, 2), A = new Triangle3d(a0, a1, a2) };
                int kind = n % 3;
                if (kind == 0) { p.Ib = new Index3i(3, 4, 5); p.B = new Triangle3d(b0, b1, b2); }
                else if (kind == 1) { p.Ib = new Index3i(4, 1, 5); p.B = new Triangle3d(b1, a1, b2); }
                else { p.Ib = new Index3i(2, 1, 5); p.B = new Triangle3d(a2, a1, b2); }
                if (n % 2 == 1) { var t = p.A; p.A = p.B; p.B = t; var ti = p.Ia; p.Ia = p.Ib; p.Ib = ti; }
                if (ExactTriangleIntersection.IsDegenerate(ref p.A) == false && ExactTriangleIntersection.IsDegenerate(ref p.B) == false)
                    continue;
                degenerate++;
                int f = Filter(p);
                if (kind == 0) {
                    if (f != TriangleTriangleFilter.Undecided)
                        Assert.True((f == TriangleTriangleFilter.Intersecting) == Exact(p), $"filter {f}: A {p.A.V0} {p.A.V1} {p.A.V2}, B {p.B.V0} {p.B.V1} {p.B.V2}");
                    // (a collinear triangle in the plane is never claimed to intersect: no edge line of it exists)
                    if (f == TriangleTriangleFilter.Intersecting) Assert.Fail("degenerate pair decided as intersecting");
                } else {
                    // a degenerate corner or edge triangle: no certificate of the filter applies in the plane
                    Assert.Equal(TriangleTriangleFilter.Undecided, f);
                    sharedUndecided++;
                }
            }
            Assert.True(degenerate > 10000 && sharedUndecided > 5000, $"{degenerate} degenerate pairs, {sharedUndecided} shared");
        }

        [Fact]
        public void EveryFrame_DecidedPairsAgree_ExactScalesSameAnswer()
        {
            foreach (int axis in new[] { 0, 2 }) {
                List<Pair> pairs = PlanarPairs(9000, axis, 1.5, 21 + axis);
                int[] reference = pairs.Select(Filter).ToArray();
                foreach (Similarity s in Similarity.All()) {
                    var moved = pairs.Select(p => Transformed(p, s.Apply)).ToList();
                    var (sep, hit, und) = CheckAgainstExact(moved, s.Name);
                    Assert.True(sep > 1000, $"{s.Name}: {sep} separate, {und} undecided");
                    if (s.IsExact)
                        Assert.Equal(reference, moved.Select(Filter).ToArray());
                }
            }
        }

        // ---- in the Reducer ----

        // rotations of the plate (normal z) out of the axis planes: normal mostly z, mostly x, mostly y, and in the
        // plane x = 0 (a wall along the x axis: the projection must not drop y or z)
        static readonly Quaterniond[] Tilts = {
            new Quaterniond(new Vector3d(1, 2, 3).Normalized, 37), new Quaterniond(new Vector3d(0.1, 1, 0.2).Normalized, 75),
            new Quaterniond(new Vector3d(1, 0.1, 0.2).Normalized, 75), new Quaterniond(Vector3d.AxisX, 45) };

        static DMesh3 TiltedJitterPlate(int n, int seed, int tilt = 0)
        {
            var rand = new Random(seed);
            var q = Tilts[tilt];
            var mesh = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = i, y = j;
                    if (i > 0 && j > 0 && i < n && j < n) { x += 0.3 * (2 * rand.NextDouble() - 1); y += 0.3 * (2 * rand.NextDouble() - 1); }
                    mesh.AppendVertex(q * new Vector3d(x, y, 0));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = i + j * (n + 1), b = a + 1, c = a + n + 2, d = a + n + 1;
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            return mesh;
        }

        [Fact]
        public void Reducer_TiltedPlate_EveryFrame_NoIntersection()
        {
            // nearly coplanar neighbours everywhere (the filter's main case outside axis planes)
            DMesh3 source = TiltedJitterPlate(14, 2);
            DMesh3 reference = new DMesh3(source);
            new Reducer(reference) { PreventSelfIntersections = true }.ReduceToTriangleCount(40);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 mesh = Invariance.Transformed(source, s);
                new Reducer(mesh) { PreventSelfIntersections = true }.ReduceToTriangleCount(40);
                Assert.True(mesh.TriangleCount <= 60, $"{s.Name}: {mesh.TriangleCount}");
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(mesh));
                Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly), s.Name);
                Assert.True(Invariance.AllFinite(mesh), s.Name);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference, Invariance.MappedBack(mesh, s)));
            }
        }
    }
}
