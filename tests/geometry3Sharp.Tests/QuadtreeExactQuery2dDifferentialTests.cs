using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QA 2.0 gap list (thin tests): seeded properties of the exact distance and clipping helpers of the 2D quadtrees
    /// (QuadtreeExactQuery2d, rational squared distances). The oracles are independent of the implementation: segment /
    /// box intersection by exact orientation tests (endpoint inside, or a proper or touching crossing with a box edge),
    /// zero distance by exact collinear and range tests, and order relations (the distance to a segment is at most the
    /// distance to its endpoints and to any grid point on it; symmetric in the endpoints; a box distance is the least over
    /// its four edges, or 0 inside). Inputs: coordinates that are multiples of 1/8 in a small window (touching, collinear,
    /// zero-length, corner cases) and random doubles, at binary scales 2^-300 .. 2^300 and translated.
    /// </summary>
    public class QuadtreeExactQuery2dDifferentialTests
    {
        static BigInteger I(double v) => QuadtreeGeometry2d.Integer(v);

        static int Orient(Vector2d a, Vector2d b, Vector2d c) =>
            ((I(b.x) - I(a.x)) * (I(c.y) - I(a.y)) - (I(b.y) - I(a.y)) * (I(c.x) - I(a.x))).Sign;

        static bool InBox(Vector2d p, AxisAlignedBox2d box) => p.x >= box.Min.x && p.x <= box.Max.x && p.y >= box.Min.y && p.y <= box.Max.y;

        static bool OnSegment(Vector2d p, Vector2d a, Vector2d b) =>
            Orient(a, b, p) == 0 && p.x >= Math.Min(a.x, b.x) && p.x <= Math.Max(a.x, b.x) && p.y >= Math.Min(a.y, b.y) && p.y <= Math.Max(a.y, b.y);

        static bool SegmentsTouch(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            int o1 = Orient(a, b, c), o2 = Orient(a, b, d), o3 = Orient(c, d, a), o4 = Orient(c, d, b);
            if (o1 * o2 < 0 && o3 * o4 < 0) return true;
            return OnSegment(c, a, b) || OnSegment(d, a, b) || OnSegment(a, c, d) || OnSegment(b, c, d);
        }

        static bool OracleIntersects(Vector2d a, Vector2d b, AxisAlignedBox2d box)
        {
            if (InBox(a, box) || InBox(b, box)) return true;
            var c = new[] { box.Min, new Vector2d(box.Max.x, box.Min.y), box.Max, new Vector2d(box.Min.x, box.Max.y) };
            for (int k = 0; k < 4; ++k)
                if (SegmentsTouch(a, b, c[k], c[(k + 1) % 4])) return true;
            return false;
        }

        static Vector2d Grid(Random rng, int range = 12) => new Vector2d(rng.Next(range) / 8.0, rng.Next(range) / 8.0);
        static Vector2d Place(Vector2d p, int e, double shift) => new Vector2d(Math.ScaleB(p.x, e) + shift, Math.ScaleB(p.y, e) + shift);
        static AxisAlignedBox2d Box(Vector2d a, Vector2d b) =>
            new AxisAlignedBox2d(new Vector2d(Math.Min(a.x, b.x), Math.Min(a.y, b.y)), new Vector2d(Math.Max(a.x, b.x), Math.Max(a.y, b.y)));

        static readonly int[] Exponents = { -300, -30, 0, 40, 300 };

        [Fact]
        public void SegmentIntersects_EqualsTheExactOracle_OnGridAndAtEveryScale()
        {
            var rng = new Random(2026111601);
            int hits = 0, misses = 0;
            foreach (int e in Exponents)
                foreach (double shift in new[] { 0.0, Math.ScaleB(1.0, e + 4) })
                    for (int i = 0; i < 400; ++i) {
                        Vector2d a = Place(Grid(rng), e, shift), b = i % 7 == 0 ? a : Place(Grid(rng), e, shift);
                        AxisAlignedBox2d box = Box(Place(Grid(rng), e, shift), Place(Grid(rng), e, shift));
                        bool expected = OracleIntersects(a, b, box);
                        Assert.True(expected == QuadtreeExactQuery2d.SegmentIntersects(a, b, box), $"2^{e} shift {shift} case {i}: oracle {expected}");
                        Assert.True(expected == QuadtreeExactQuery2d.SegmentIntersects(b, a, box), $"2^{e} case {i}: not symmetric in the endpoints");
                        if (expected) ++hits; else ++misses;
                    }
            Assert.True(hits > 1000 && misses > 1000, $"vacuous: {hits} hits, {misses} misses");
        }

        [Fact]
        public void SegmentIntersects_RandomDoubles_EqualsTheExactOracle()
        {
            var rng = new Random(2026111602);
            for (int i = 0; i < 1500; ++i) {
                Vector2d a = new Vector2d(rng.NextDouble(), rng.NextDouble()), b = new Vector2d(rng.NextDouble(), rng.NextDouble());
                AxisAlignedBox2d box = Box(new Vector2d(rng.NextDouble(), rng.NextDouble()), new Vector2d(rng.NextDouble(), rng.NextDouble()));
                Assert.Equal(OracleIntersects(a, b, box), QuadtreeExactQuery2d.SegmentIntersects(a, b, box));
            }
        }

        [Fact]
        public void SquaredDistances_SatisfyTheirExactRelations()
        {
            var rng = new Random(2026111603);
            int zero = 0, positive = 0;
            foreach (int e in Exponents)
                for (int i = 0; i < 300; ++i) {
                    Vector2d p = Place(Grid(rng), e, 0), a = Place(Grid(rng), e, 0), b = Place(Grid(rng), e, 0);
                    if (i % 4 == 0) p = new Vector2d(a.x + (b.x - a.x) / 2, a.y + (b.y - a.y) / 2);      // exactly on the segment
                    var seg = QuadtreeExactQuery2d.SegmentSquared(p, a, b);
                    var seg2 = QuadtreeExactQuery2d.SegmentSquared(p, b, a);
                    Assert.True(seg.CompareTo(seg2) == 0, $"2^{e} case {i}: segment distance depends on the endpoint order");
                    Assert.True(seg.CompareTo(QuadtreeExactQuery2d.PointSquared(p, a)) <= 0 && seg.CompareTo(QuadtreeExactQuery2d.PointSquared(p, b)) <= 0, $"2^{e} case {i}: farther than an endpoint");
                    // zero exactly on the segment
                    Assert.Equal(OnSegment(p, a, b) || (a == b && p == a), seg.Numerator.IsZero);
                    // not farther than the exact grid points of the segment (a + (b - a) k / 4: representable)
                    for (int k = 1; k < 4; ++k) {
                        Vector2d q = new Vector2d(a.x + (b.x - a.x) * k / 4, a.y + (b.y - a.y) * k / 4);
                        if (Orient(a, b, q) != 0) continue;       // rounded off the segment (never for these grids), skip
                        Assert.True(seg.CompareTo(QuadtreeExactQuery2d.PointSquared(p, q)) <= 0, $"2^{e} case {i}: a point of the segment is nearer");
                    }
                    if (seg.Numerator.IsZero) ++zero; else ++positive;

                    // the box distance: 0 inside, else the least over the four edges
                    AxisAlignedBox2d box = Box(Place(Grid(rng), e, 0), Place(Grid(rng), e, 0));
                    var bd = QuadtreeExactQuery2d.BoxSquared(p, box);
                    if (InBox(p, box)) Assert.True(bd.Numerator.IsZero);
                    else {
                        var c = new[] { box.Min, new Vector2d(box.Max.x, box.Min.y), box.Max, new Vector2d(box.Min.x, box.Max.y) };
                        var best = QuadtreeExactQuery2d.SegmentSquared(p, c[0], c[1]);
                        for (int k = 1; k < 4; ++k) {
                            var s = QuadtreeExactQuery2d.SegmentSquared(p, c[k], c[(k + 1) % 4]);
                            if (s.CompareTo(best) < 0) best = s;
                        }
                        Assert.True(bd.CompareTo(best) == 0, $"2^{e} case {i}: box distance is not the least edge distance");
                    }
                }
            Assert.True(zero > 300 && positive > 800, $"vacuous: {zero} zero, {positive} positive");
        }

        [Fact]
        public void TriangleSquared_IsZeroExactlyInsideAndInvariantUnderRelabelling()
        {
            var rng = new Random(2026111604);
            int inside = 0, outside = 0;
            foreach (int e in Exponents)
                for (int i = 0; i < 300; ++i) {
                    Vector2d a = Place(Grid(rng), e, 0), b = Place(Grid(rng), e, 0), c = Place(Grid(rng), e, 0), p = Place(Grid(rng), e, 0);
                    Func<Vector2d, Vector3d> v3 = v => new Vector3d(v.x, v.y, 0);
                    var d = QuadtreeExactQuery2d.TriangleSquared(p, new Triangle3d(v3(a), v3(b), v3(c)));
                    bool oracleInside = Orient(a, b, c) != 0
                        ? new[] { Orient(a, b, p), Orient(b, c, p), Orient(c, a, p) }.Select(Math.Sign).Distinct().Where(s => s != 0).Count() <= 1
                        : OnSegment(p, a, b) || OnSegment(p, b, c) || OnSegment(p, c, a);      // a degenerate triangle: its segments
                    Assert.Equal(oracleInside, d.Numerator.IsZero);
                    foreach (var (x, y, z2) in new[] { (b, c, a), (c, a, b), (c, b, a), (a, c, b) })
                        Assert.True(d.CompareTo(QuadtreeExactQuery2d.TriangleSquared(p, new Triangle3d(v3(x), v3(y), v3(z2)))) == 0, $"2^{e} case {i}: depends on the vertex order");
                    // never farther than a vertex or an edge
                    Assert.True(d.CompareTo(QuadtreeExactQuery2d.SegmentSquared(p, a, b)) <= 0 && d.CompareTo(QuadtreeExactQuery2d.PointSquared(p, c)) <= 0);
                    if (oracleInside) ++inside; else ++outside;
                }
            Assert.True(inside > 100 && outside > 500, $"vacuous: {inside} inside, {outside} outside");
        }

        [Fact]
        public void Distances_ScaleExactlyByPowersOfTwo()
        {
            var rng = new Random(2026111605);
            for (int i = 0; i < 200; ++i) {
                Vector2d p = new Vector2d(rng.NextDouble(), rng.NextDouble()), a = new Vector2d(rng.NextDouble(), rng.NextDouble()), b = new Vector2d(rng.NextDouble(), rng.NextDouble());
                var d0 = QuadtreeExactQuery2d.SegmentSquared(p, a, b);
                foreach (int e in new[] { -200, 150 }) {
                    var d1 = QuadtreeExactQuery2d.SegmentSquared(Place(p, e, 0), Place(a, e, 0), Place(b, e, 0));
                    // squared distance scales by 2^2e, and the integer images by 2^(2e) as well: the ratio num/den is 4^e times larger
                    BigInteger lhs = d1.Numerator * d0.Denominator, rhs = d0.Numerator * d1.Denominator;
                    Assert.True(e < 0 ? lhs * BigInteger.Pow(2, -2 * e) == rhs : lhs == rhs * BigInteger.Pow(2, 2 * e), $"case {i} 2^{e}: not scaled by 2^{2 * e}");
                }
            }
        }
    }
}
