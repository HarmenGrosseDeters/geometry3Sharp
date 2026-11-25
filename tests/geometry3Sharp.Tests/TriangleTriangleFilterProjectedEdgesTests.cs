using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TriangleTriangleFilter.ProjectedEdges, the cull of the Reducer's candidates without shared vertices: its signs are
    /// those of ExactPredicates.Orient2d (the filter stage on the same doubles, 0 where it does not prove a sign), its
    /// Separates never claims a pair the exact test finds intersecting, and it never culls against a degenerate
    /// projection.
    /// </summary>
    public class TriangleTriangleFilterProjectedEdgesTests
    {
        // the exact sign of (b - a) x (c - a) from the doubles' mantissas and exponents (BigInteger, independent of
        // ExactPredicates, whose filter the cull shares with Orient2d)
        static int ExactOrient(Vector2d a, Vector2d b, Vector2d c)
        {
            double[] v = { a.x, a.y, b.x, b.y, c.x, c.y };
            var m = new BigInteger[6]; var ex = new int[6];
            int emin = int.MaxValue;
            for (int i = 0; i < 6; ++i) {
                long bits = BitConverter.DoubleToInt64Bits(v[i]);
                int e = (int)((bits >> 52) & 0x7ff);
                long mant = bits & 0xfffffffffffffL;
                if (e == 0) e = 1; else mant |= 1L << 52;
                m[i] = bits < 0 ? -new BigInteger(mant) : new BigInteger(mant);
                ex[i] = e;
                if (mant != 0) emin = Math.Min(emin, e);
            }
            if (emin == int.MaxValue) return 0;
            var w = new BigInteger[6];
            for (int i = 0; i < 6; ++i) w[i] = m[i] << (ex[i] - emin);
            BigInteger det = (w[2] - w[0]) * (w[5] - w[1]) - (w[3] - w[1]) * (w[4] - w[0]);
            return det.Sign;
        }

        // the next double up, for finite x (Math.BitIncrement is missing on net48)
        static double BitIncrement(double x)
        {
            if (x == 0) return double.Epsilon;
            long b = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(x > 0 ? b + 1 : b - 1);
        }

        static Vector3d Grid(Random r) => new Vector3d(r.Next(-4, 5), r.Next(-4, 5), r.Next(-4, 5));
        static Vector3d Unit(Random r) => new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble());

        // a point near the line of the projected edge k of A (a multiple of the edge plus an ulp-scale offset), so that
        // the filter's bound matters
        static Vector3d NearEdge(Random r, ref Triangle3d A, int k, double offset)
        {
            Vector3d a = A[k], b = A[(k + 1) % 3];
            double t = 3 * r.NextDouble() - 1;
            Vector3d p = a + t * (b - a);
            return new Vector3d(p.x + offset * (r.NextDouble() - 0.5), p.y + offset * (r.NextDouble() - 0.5), p.z + offset * (r.NextDouble() - 0.5));
        }

        // every edge/point sign of the cull is 0 or Orient2d's sign; returns the number of proven signs
        static int CheckSigns(Triangle3d A, IEnumerable<Vector3d> points, string what)
        {
            var e = TriangleTriangleFilter.ProjectedEdges.Of(ref A);
            Vector2d[] P = { e.Project(A.V0), e.Project(A.V1), e.Project(A.V2) };
            Assert.Equal(ExactOrient(P[0], P[1], P[2]), e.O);
            int proven = 0;
            foreach (Vector3d p in points) {
                Vector2d c = e.Project(p);
                for (int k = 0; k < 3; ++k) {
                    int s = e.EdgeSign(k, c);
                    // bit for bit the filter stage of Orient2d(e0, e1, c): the same doubles (zeros included)
                    Vector2d e0 = P[k], e1 = P[(k + 1) % 3];
                    Assert.Equal(ExactPredicates.Orient2dFilter(e1.x - e0.x, e1.y - e0.y, c.x - e0.x, c.y - e0.y), s);
                    if (s == 0) continue;
                    proven++;
                    Assert.True(s == ExactOrient(P[k], P[(k + 1) % 3], c), $"{what}: edge {k}, A {A.V0} {A.V1} {A.V2}, p {p}");
                }
            }
            return proven;
        }

        static IEnumerable<Vector3d> Points(Random r, Triangle3d A, int n, double scale, double nearOffset)
        {
            for (int i = 0; i < n; ++i) {
                yield return scale * Unit(r);
                yield return NearEdge(r, ref A, i % 3, nearOffset);
            }
        }

        [Fact]
        public void EdgeSigns_AreOrient2dSigns_OnRandomGridAndNearCollinearPoints()
        {
            var rand = new Random(71);
            int proven = 0, total = 0;
            for (int n = 0; n < 1000; ++n) {
                Triangle3d A = n % 2 == 0 ? new Triangle3d(Unit(rand), Unit(rand), Unit(rand)) : new Triangle3d(Grid(rand), Grid(rand), Grid(rand));
                // near-collinear points: offsets of a few ulps (where a smaller bound would give wrong signs) and larger
                double off = n % 3 == 0 ? 0 : (n % 3 == 1 ? 1e-15 : 1e-9);
                proven += CheckSigns(A, Points(rand, A, 20, 4, off), "random");
                total += 3 * 40;
            }
            Assert.True(proven > total / 2, $"{proven} of {total} proven");
        }

        [Fact]
        public void EdgeSigns_AreOrient2dSigns_OnTheUlpGrid()
        {
            // the classic adversarial input of orientation tests (Kettner et al., "Classroom examples of robustness
            // problems in geometric computations", 2008): an edge from a point within a few hundred ulps of (0.5, 0.5) to
            // (12, 12), against the point (24, 24) nearly on its line. The differences from the edge start are rounded,
            // and the floating-point determinant has the wrong sign for many of the starts. No bound gives a wrong sign
            // here. (Another base point, e1 instead of e0, is a cyclic permutation of the same orientation: its signs are
            // right, but it decides other points than Orient2d's filter does, which the bit-identity assert catches)
            int proven = 0, zero = 0;
            Vector3d q = new Vector3d(12, 12, 0), r3 = new Vector3d(24, 24, 0);
            Vector2d r = new Vector2d(24, 24);
            double px = 0.5;
            for (int i = 0; i < 256; ++i, px = BitIncrement(px)) {
                double py = 0.5;
                for (int j = 0; j < 256; ++j, py = BitIncrement(py)) {
                    Vector3d p = new Vector3d(px, py, 0), x = new Vector3d(px, 30, 0);
                    int edge = (i + j) % 3;
                    // the edge (p, q) as edge `edge` of A, in the plane z = 0 (projected along z, as it is)
                    var A = edge == 0 ? new Triangle3d(p, q, x) : edge == 1 ? new Triangle3d(x, p, q) : new Triangle3d(q, x, p);
                    var e = TriangleTriangleFilter.ProjectedEdges.Of(ref A);
                    Assert.Equal(2, e.Axis);
                    int s = e.EdgeSign(edge, e.Project(r3));
                    Assert.Equal(ExactPredicates.Orient2dFilter(12 - px, 12 - py, 24 - px, 24 - py), s);
                    if (s == 0) { zero++; continue; }
                    proven++;
                    Assert.True(s == ExactOrient(new Vector2d(px, py), new Vector2d(12, 12), r), $"edge {edge}, start ({px:R}, {py:R})");
                }
            }
            Assert.True(proven > 10000 && zero > 1000, $"{proven} proven, {zero} undecided");
        }

        [Fact]
        public void EdgeSigns_FoundNearTheBound_AreOrient2dSigns()
        {
            // edges a -> b and points c whose floating-point determinant has the wrong sign within the filter's bound but
            // outside half of it (found by a search over random near-collinear inputs): a smaller bound proves them wrong
            var cases = new[] {
                (-63.75418752420423, 42.0534421103324, -237.9659100984996, 174.80890994463533, -191.86793137984537, 139.68062672362066),
                (-0.09230508129918252, 0.05469815418110144, -0.362041455454678, 0.49954935564871383, -0.7161381958225614, 1.0835282901976453),
                (-0.006343475851506682, 0.013183369147327435, -0.03886354438500178, 0.08503279200116301, 0.026634578452571042, -0.059677921601489305),
                (-0.10792013204559689, 0.13006105082112412, -0.5701211962476938, -0.5953955891753526, 0.3418623912027545, 0.8360259291326395) };
            foreach (var (ax, ay, bx, by, cx, cy) in cases) {
                Vector3d a = new Vector3d(ax, ay, 0), b = new Vector3d(bx, by, 0);
                // a third corner off the edge's line, in the plane z = 0
                Vector3d x = a + new Vector3d(-(by - ay), bx - ax, 0);
                var A = new Triangle3d(a, b, x);
                var e = TriangleTriangleFilter.ProjectedEdges.Of(ref A);
                Assert.Equal(2, e.Axis);
                Vector2d c = new Vector2d(cx, cy);
                int s = e.EdgeSign(0, c);
                Assert.True(s == 0 || s == ExactOrient(new Vector2d(ax, ay), new Vector2d(bx, by), c), $"a ({ax}, {ay}), b ({bx}, {by}), c ({cx}, {cy}): {s}");
            }
        }

        [Fact]
        public void EdgeSigns_AreOrient2dSigns_AtExtremeScalesAndUnderflow()
        {
            // 2^500 and 2^-60 (the products stay finite and above the filter's underflow limit 1e-200: the same signs as
            // at scale 1), and 2^-500, coordinates near 1e-110 and 1e-160, subnormal differences near 1e-308 (the
            // products below the limit): never a wrong sign; below the limit nothing is proven
            var rand = new Random(73);
            foreach (double f in new[] { Math.Pow(2, 500), Math.Pow(2, -60) }) {
                int proven = 0;
                for (int n = 0; n < 150; ++n) {
                    var A0 = new Triangle3d(Grid(rand), Grid(rand), Grid(rand));
                    var A = new Triangle3d(f * A0.V0, f * A0.V1, f * A0.V2);
                    proven += CheckSigns(A, Points(rand, A0, 10, 4, 1e-15).Select(p => f * p), "2^" + Math.Log(f, 2));
                }
                Assert.True(proven > 300, $"scale {f}: {proven}");
            }
            foreach (double f in new[] { Math.Pow(2, -500), 1e-110, 1e-160 }) {
                int proven = 0;
                for (int n = 0; n < 150; ++n) {
                    var A0 = new Triangle3d(Grid(rand), Grid(rand), Grid(rand));
                    var A = new Triangle3d(f * A0.V0, f * A0.V1, f * A0.V2);
                    proven += CheckSigns(A, Points(rand, A0, 10, 4, 1e-12).Select(p => f * p), "tiny " + f);
                }
                // products near 1e-301, 1e-220 and 1e-320: below the underflow limit 1e-200
                Assert.Equal(0, proven);
            }
            {
                int n0 = 0;
                for (int n = 0; n < 150; ++n) {
                    Vector3d o = new Vector3d(1e-308, 1e-308, 1e-308);
                    var A0 = new Triangle3d(Grid(rand), Grid(rand), Grid(rand));
                    var A = new Triangle3d(o + 1e-310 * A0.V0, o + 1e-310 * A0.V1, o + 1e-310 * A0.V2);
                    n0 += CheckSigns(A, Points(rand, A0, 10, 4, 1e-12).Select(p => o + 1e-310 * p), "subnormal differences");
                }
                Assert.Equal(0, n0);
            }
        }

        [Fact]
        public void Separates_NeverAPairTheExactTestFindsIntersecting()
        {
            // integer grids (touching corners, collinear edges), random pairs, nearly coplanar pairs; both orientations of A
            var rand = new Random(79);
            var q = new Quaterniond(new Vector3d(1, 2, 3).Normalized, 37);
            int separated = 0, n = 0, hits = 0;
            for (int k = 0; k < 60000; ++k) {
                int family = k % 3;
                Func<Vector3d> point = family == 0 ? (Func<Vector3d>)(() => Grid(rand))
                    : family == 1 ? (() => 4 * Unit(rand)) : (() => q * new Vector3d(4 * rand.NextDouble(), 4 * rand.NextDouble(), 1e-12 * (rand.NextDouble() - 0.5)));
                var A = new Triangle3d(point(), point(), point());
                var C = new Triangle3d(point(), point(), point());
                if (k % 2 == 1) A = new Triangle3d(A.V0, A.V2, A.V1);
                var e = TriangleTriangleFilter.ProjectedEdges.Of(ref A);
                bool exact = ExactTriangleIntersection.Intersects(ref A, ref C);
                if (exact) hits++;
                n++;
                if (e.Separates(ref C)) {
                    separated++;
                    Assert.False(exact, $"A {A.V0} {A.V1} {A.V2}, C {C.V0} {C.V1} {C.V2}");
                }
            }
            Assert.True(separated > 10000 && hits > 5000, $"{separated} separated, {hits} intersecting of {n}");
        }

        [Fact]
        public void Separates_XAndYDominantTriangles_ProjectAlongTheirAxis()
        {
            // a triangle in a plane x = 1 (y = 1) is projected along x (y), where it is not degenerate, and culls a far
            // triangle; along z it would project to a segment (O = 0) and cull nothing
            for (int k = 0; k < 2; ++k) {
                Func<double, double, double, Vector3d> at = (h, u, v) => k == 0 ? new Vector3d(h, u, v) : new Vector3d(v, h, u);
                var A = new Triangle3d(at(1, 0, 0), at(1, 1, 0), at(1, 0, 1));
                var C = new Triangle3d(at(1, 5, 5), at(1.5, 6, 5), at(0.5, 5, 6));
                var e = TriangleTriangleFilter.ProjectedEdges.Of(ref A);
                Assert.Equal(k, e.Axis);
                Assert.NotEqual(0, e.O);
                Assert.True(e.Separates(ref C), $"axis {k}");
                Assert.False(ExactTriangleIntersection.Intersects(ref A, ref C));
            }
        }

        [Fact]
        public void Separates_DegenerateOrFlatProjection_NeverCulls()
        {
            var far = new Triangle3d(new Vector3d(100, 0, 0), new Vector3d(101, 0, 0), new Vector3d(100, 1, 0));
            // collinear corners, two equal corners, one point: O = 0
            foreach (var A in new[] {
                new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1), new Vector3d(2, 2, 2)),
                new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 2, 3), new Vector3d(1, 2, 3)),
                new Triangle3d(new Vector3d(5, 5, 5), new Vector3d(5, 5, 5), new Vector3d(5, 5, 5)) }) {
                var A1 = A;
                var e = TriangleTriangleFilter.ProjectedEdges.Of(ref A1);
                Assert.Equal(0, e.O);
                Assert.False(e.Separates(ref far));
            }
            // a sliver whose floating-point normal is 0 (its corners collinear in floating point, not exactly): the
            // dominant axis is then x, the projection may be degenerate, and it culls only where O is not 0
            var rand = new Random(83);
            int zeroO = 0;
            for (int k = 0; k < 2000; ++k) {
                Vector3d a = Unit(rand), d = Unit(rand);
                Vector3d b = a + d, c = a + 2 * d;
                c = new Vector3d(BitIncrement(c.x), c.y, c.z);
                var A = new Triangle3d(a, b, c);
                var e = TriangleTriangleFilter.ProjectedEdges.Of(ref A);
                Vector2d p0 = e.Project(a), p1 = e.Project(b), p2 = e.Project(c);
                Assert.Equal(ExactOrient(p0, p1, p2), e.O);
                if (e.O == 0) { zeroO++; Assert.False(e.Separates(ref far)); }
                else if (e.Separates(ref far)) Assert.False(ExactTriangleIntersection.Intersects(ref A, ref far));
            }
            Assert.True(zeroO > 5, $"{zeroO} degenerate projections");
        }

        [Fact]
        public void Separates_EveryFrame_NeverWrong_ExactScalesSameDecisions()
        {
            var rand = new Random(89);
            var pairs = new List<(Triangle3d, Triangle3d)>();
            for (int k = 0; k < 4000; ++k)
                pairs.Add((new Triangle3d(Grid(rand), Grid(rand), Grid(rand)), new Triangle3d(Grid(rand), Grid(rand), Grid(rand))));
            Func<(Triangle3d, Triangle3d), bool> sep = p => { var A = p.Item1; var C = p.Item2; return TriangleTriangleFilter.ProjectedEdges.Of(ref A).Separates(ref C); };
            bool[] reference = pairs.Select(sep).ToArray();
            Assert.True(reference.Count(x => x) > 400, $"{reference.Count(x => x)} separated");
            foreach (Similarity s in Similarity.All()) {
                var moved = pairs.Select(p => (new Triangle3d(s.Apply(p.Item1.V0), s.Apply(p.Item1.V1), s.Apply(p.Item1.V2)),
                    new Triangle3d(s.Apply(p.Item2.V0), s.Apply(p.Item2.V1), s.Apply(p.Item2.V2)))).ToList();
                for (int i = 0; i < moved.Count; ++i) {
                    var (A, C) = moved[i];
                    bool culled = sep(moved[i]);
                    if (culled) Assert.False(ExactTriangleIntersection.Intersects(ref A, ref C), s.Name);
                    if (s.IsExact) Assert.Equal(reference[i], culled);
                }
            }
        }
    }
}
