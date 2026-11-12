using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using g3;

namespace g3cli.Tests
{
    /// <summary>
    /// TrianglePolyFormat.ImproperSegments: the segments that meet another other than at one shared end point. Segments with a shared end
    /// point are decided by the directions around it (d log d at a vertex of degree d, not d^2 exact tests); the rest by the quadtree.
    /// The reference here is the pairwise test, on integer coordinates.
    /// </summary>
    public class TrianglePolyImproperSegmentsTests
    {
        static long Cross(long ax, long ay, long bx, long by, long cx, long cy) => (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        static int Sign(long v) => v < 0 ? -1 : v > 0 ? 1 : 0;

        static bool OnSeg(long ax, long ay, long bx, long by, long px, long py) =>
            Math.Min(ax, bx) <= px && px <= Math.Max(ax, bx) && Math.Min(ay, by) <= py && py <= Math.Max(ay, by);

        // do the closed segments ab and cd have a point in common
        static bool Meet(long[] a, long[] b, long[] c, long[] d)
        {
            int o1 = Sign(Cross(a[0], a[1], b[0], b[1], c[0], c[1])), o2 = Sign(Cross(a[0], a[1], b[0], b[1], d[0], d[1]));
            int o3 = Sign(Cross(c[0], c[1], d[0], d[1], a[0], a[1])), o4 = Sign(Cross(c[0], c[1], d[0], d[1], b[0], b[1]));
            if (o1 * o2 < 0 && o3 * o4 < 0) return true;
            return (o1 == 0 && OnSeg(a[0], a[1], b[0], b[1], c[0], c[1])) || (o2 == 0 && OnSeg(a[0], a[1], b[0], b[1], d[0], d[1]))
                || (o3 == 0 && OnSeg(c[0], c[1], d[0], d[1], a[0], a[1])) || (o4 == 0 && OnSeg(c[0], c[1], d[0], d[1], b[0], b[1]));
        }

        // the pairwise reference
        static bool[] Reference(long[][] pts, List<Index2i> edges)
        {
            var bad = new bool[edges.Count];
            for (int i = 0; i < edges.Count; ++i)
                for (int j = i + 1; j < edges.Count; ++j) {
                    Index2i e = edges[i], f = edges[j];
                    int shared = (e.a == f.a || e.a == f.b ? 1 : 0) + (e.b == f.a || e.b == f.b ? 1 : 0);
                    bool improper;
                    if (shared == 0) improper = Meet(pts[e.a], pts[e.b], pts[f.a], pts[f.b]);
                    else {
                        int p = e.a == f.a || e.a == f.b ? e.a : e.b;
                        int q = e.a == p ? e.b : e.a, r = f.a == p ? f.b : f.a;
                        long ux = pts[q][0] - pts[p][0], uy = pts[q][1] - pts[p][1], vx = pts[r][0] - pts[p][0], vy = pts[r][1] - pts[p][1];
                        improper = ux * vy - uy * vx == 0 && ux * vx + uy * vy > 0;     // the same way along one line
                    }
                    if (improper) bad[i] = bad[j] = true;
                }
            return bad;
        }

        static List<Vector2d> ToVectors(long[][] pts, double scale = 1, double dx = 0, double dy = 0) =>
            pts.Select(p => new Vector2d(scale * p[0] + dx, scale * p[1] + dy)).ToList();

        [Fact]
        public void RandomSmallGraphs_EqualThePairwiseReference()
        {
            var rnd = new Random(3);
            int withBad = 0, withoutBad = 0;
            for (int c = 0; c < 600; ++c) {
                int grid = 3 + rnd.Next(4);
                var pts = new List<long[]>();
                int n = 4 + rnd.Next(8);
                for (int i = 0; i < n; ++i) {
                    var p = new[] { (long)rnd.Next(grid), (long)rnd.Next(grid) };
                    if (!pts.Any(q => q[0] == p[0] && q[1] == p[1])) pts.Add(p);
                }
                var edges = new List<Index2i>();
                var seen = new HashSet<(int, int)>();
                int m = 2 + rnd.Next(10);
                for (int i = 0; i < m && pts.Count > 1; ++i) {
                    int a = rnd.Next(pts.Count), b = rnd.Next(pts.Count);
                    if (a == b || !seen.Add((Math.Min(a, b), Math.Max(a, b)))) continue;
                    edges.Add(new Index2i(a, b));
                }
                var expected = Reference(pts.ToArray(), edges);
                Assert.Equal(expected, TrianglePolyFormat.ImproperSegments(ToVectors(pts.ToArray()), edges));
                if (expected.Any(b => b)) ++withBad; else ++withoutBad;
            }
            Assert.True(withBad > 100 && withoutBad > 50, $"the cases are mixed: {withBad} with, {withoutBad} without");
        }

        [Theory]
        [InlineData(1.0 / 1024, 0.0, 0.0)]
        [InlineData(1024.0, 0.0, 0.0)]
        [InlineData(1.0, 1024.0, -4096.0)]
        [InlineData(1.0 / 64, 1.0 / 2, 3.0)]
        public void Invariance_ExactScalesAndTranslations(double scale, double dx, double dy)
        {
            // powers of two and translations by small multiples of them are exact on these small integers: the same improper segments as
            // the reference on the integers
            var rnd = new Random(8);
            int withBad = 0;
            for (int c = 0; c < 80; ++c) {
                var pts = Enumerable.Range(0, 7).Select(i => new[] { (long)rnd.Next(4), (long)rnd.Next(4) }).GroupBy(p => (p[0], p[1])).Select(g => g.First()).ToArray();
                var edges = new List<Index2i>();
                var seen = new HashSet<(int, int)>();
                for (int i = 0; i < 9; ++i) {
                    int a = rnd.Next(pts.Length), b = rnd.Next(pts.Length);
                    if (a != b && seen.Add((Math.Min(a, b), Math.Max(a, b)))) edges.Add(new Index2i(a, b));
                }
                var expected = Reference(pts, edges);
                Assert.Equal(expected, TrianglePolyFormat.ImproperSegments(ToVectors(pts, scale, dx, dy), edges));
                if (expected.Any(x => x)) ++withBad;
            }
            Assert.True(withBad > 10);
        }

        [Theory]
        [InlineData(1e150)]
        [InlineData(1e-150)]
        [InlineData(1e-300)]
        public void ExtremeScales_GiveTheResultOfTheIntegers(double scale)
        {
            // collinear overlaps and crossings at coordinates near the ends of the double range (the half-plane test compares, the orientations are exact)
            var pts = new long[][] { new long[] { 0, 0 }, new long[] { 2, 0 }, new long[] { 5, 0 }, new long[] { 1, 3 }, new long[] { 1, -3 }, new long[] { 0, 4 } };
            var edges = new List<Index2i> { new Index2i(0, 1), new Index2i(0, 2), new Index2i(3, 4), new Index2i(0, 5) };
            var bad = TrianglePolyFormat.ImproperSegments(ToVectors(pts, scale), edges);
            Assert.Equal(Reference(pts, edges), bad);
            Assert.Equal(new[] { true, true, true, false }, bad);     // 0-1 and 0-2 overlap; 3-4 crosses both; 0-5 only touches at 0
        }

        [Fact]
        public void Hub_SpokesThatRunTheSameWay_AreImproper_OppositeOnesAreNot()
        {
            // hub 0 at the origin; 1 at (1,0), 2 at (3,0) on the same ray (overlap: both bad); 3 at (-2,0) opposite (not bad); 4 at (0,5)
            var pts = new long[][] { new long[] { 0, 0 }, new long[] { 1, 0 }, new long[] { 3, 0 }, new long[] { -2, 0 }, new long[] { 0, 5 } };
            var edges = new List<Index2i> { new Index2i(0, 1), new Index2i(0, 2), new Index2i(0, 3), new Index2i(0, 4) };
            var bad = TrianglePolyFormat.ImproperSegments(ToVectors(pts), edges);
            Assert.Equal(new[] { true, true, false, false }, bad);
            Assert.Equal(Reference(pts, edges), bad);
            // the same with the shared point as the other end of the segments, and the segments listed in another order
            var reversed = edges.Select(e => new Index2i(e.b, e.a)).Reverse().ToList();
            Assert.Equal(new[] { false, false, true, true }, TrianglePolyFormat.ImproperSegments(ToVectors(pts), reversed));
        }

        [Fact]
        public void Hub_ThreeOnOneRay_AllAreImproper()
        {
            var pts = new long[][] { new long[] { 0, 0 }, new long[] { 1, 1 }, new long[] { 2, 2 }, new long[] { 5, 5 }, new long[] { 1, -1 } };
            var edges = new List<Index2i> { new Index2i(0, 1), new Index2i(0, 2), new Index2i(0, 3), new Index2i(0, 4) };
            Assert.Equal(new[] { true, true, true, false }, TrianglePolyFormat.ImproperSegments(ToVectors(pts), edges));
        }

        [Fact]
        public void ABigWheel_HasNoImproperSegment_AnExtendedSpokeMakesItsNeighboursImproper()
        {
            // 4000 spokes to a rim on an integer polygon: no spoke overlaps another, the rim segments meet only at shared end points
            int n = 4000;
            var pts = new List<long[]> { new long[] { 0, 0 } };
            var edges = new List<Index2i>();
            for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * i / n;
                pts.Add(new[] { (long)Math.Round(1e6 * Math.Cos(a)), (long)Math.Round(1e6 * Math.Sin(a)) });
                edges.Add(new Index2i(0, i + 1));
                edges.Add(new Index2i(i + 1, (i + 1) % n + 1));
            }
            Assert.DoesNotContain(true, TrianglePolyFormat.ImproperSegments(ToVectors(pts.ToArray()), edges));
            // a spoke from the hub through rim point 1 to twice its distance overlaps spoke 0 and passes through the rim point, where the
            // two rim segments of that point touch its inside: those four are improper, nothing else
            pts.Add(new[] { pts[1][0] * 2, pts[1][1] * 2 });
            edges.Add(new Index2i(0, pts.Count - 1));
            var bad = TrianglePolyFormat.ImproperSegments(ToVectors(pts.ToArray()), edges);
            Assert.Equal(new[] { 0, 1, 2 * n - 1, edges.Count - 1 }, Enumerable.Range(0, edges.Count).Where(i => bad[i]).ToArray());
        }
    }
}
