using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The hash grids (docs/ROBUSTNESS_SWEEP.md R4): a query whose cell range reaches int.MaxValue (cell indices beyond
    /// 2^31 saturate there on .NET 9 and later: UTM coordinates with small cells, an infinite query point or radius) looped
    /// forever; a huge range took as long as its cell count. Each query must finish and agree with a brute-force search.
    /// </summary>
    public class HashGridRangeTests
    {
        static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

        static List<Vector3d> Points(Vector3d offset, double size, int n = 200, int seed = 7)
        {
            var r = new Random(seed);
            return Enumerable.Range(0, n).Select(i => offset + size * new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble())).ToList();
        }

        static KeyValuePair<int, double> BruteForce(List<Vector3d> p, Vector3d q, double radius)
        {
            int best = -1; double d = double.MaxValue;
            for (int i = 0; i < p.Count; ++i) {
                double di = p[i].Distance(q);
                if (di < radius && di < d) { best = i; d = di; }
            }
            return new KeyValuePair<int, double>(best, d);
        }

        public static IEnumerable<object[]> Cases()
        {
            // (offset, size of the cloud, cell size, radius)
            yield return new object[] { 0.0, 1.0, 0.1, 0.2 };
            yield return new object[] { 5712043.5, 1e-3, 3e-4, 3e-4 };      // UTM, millimetre cloud: indices beyond 2^31
            yield return new object[] { 5712043.5, 1.0, 1e-6, 0.05 };       // UTM, tiny cells
            yield return new object[] { 0.0, 1.0, 0.1, double.PositiveInfinity };
            yield return new object[] { 0.0, 1.0, 1e-12, 0.3 };             // a huge range of mostly empty cells
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void PointHashGrid3d_FindNearestInRadius_FinishesAndMatchesBruteForce(double offset, double size, double cell, double radius)
        {
            List<Vector3d> p = Points(new Vector3d(offset, -offset / 3, offset / 7), size);
            var grid = new PointHashGrid3d<int>(cell, -1);
            for (int i = 0; i < p.Count; ++i) grid.InsertPoint(i, p[i]);
            var r = new Random(3);
            for (int k = 0; k < 20; ++k) {
                Vector3d q = p[r.Next(p.Count)] + 0.3 * size * new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5);
                KeyValuePair<int, double> found = default;
                Assert.True(Timed.Finishes(() => found = grid.FindNearestInRadius(q, radius, i => p[i].Distance(q)), Budget), "query did not finish");
                KeyValuePair<int, double> expected = BruteForce(p, q, radius);
                Assert.Equal(expected.Value, found.Value);
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void PointHashGrid2d_And_SegmentHashGrid_FinishAndMatchBruteForce(double offset, double size, double cell, double radius)
        {
            List<Vector2d> p = Points(new Vector3d(offset, -offset / 3, 0), size).Select(v => v.xy).ToList();
            var grid = new PointHashGrid2d<int>(cell, -1);
            var segs = new SegmentHashGrid2d<int>(cell, -1);
            for (int i = 0; i < p.Count; ++i) { grid.InsertPoint(i, p[i]); segs.InsertSegment(i, p[i], 0); }
            var r = new Random(5);
            for (int k = 0; k < 20; ++k) {
                Vector2d q = p[r.Next(p.Count)] + 0.3 * size * new Vector2d(r.NextDouble() - 0.5, r.NextDouble() - 0.5);
                double expected = p.Select(v => v.Distance(q)).Where(d => d < radius).DefaultIfEmpty(double.MaxValue).Min();
                KeyValuePair<int, double> a = default, b = default, c = default;
                Assert.True(Timed.Finishes(() => {
                    a = grid.FindNearestInRadius(q, radius, i => p[i].Distance(q));
                    b = segs.FindNearestInRadius(q, radius, i => p[i].Distance(q));
                    c = segs.FindNearestInSquaredRadius(q, radius * radius, i => p[i].DistanceSquared(q));
                }, Budget), "query did not finish");
                Assert.Equal(expected, a.Value);
                Assert.Equal(expected, b.Value);
                Assert.Equal(expected == double.MaxValue ? double.MaxValue : expected * expected, c.Value, 12);
            }
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void PointHashGrid3d_NonFiniteQuery_FinishesWithoutResult(double bad)
        {
            List<Vector3d> p = Points(Vector3d.Zero, 1);
            var grid = new PointHashGrid3d<int>(0.1, -1);
            for (int i = 0; i < p.Count; ++i) grid.InsertPoint(i, p[i]);
            var q = new Vector3d(bad, 0.5, 0.5);
            KeyValuePair<int, double> found = default;
            Assert.True(Timed.Finishes(() => found = grid.FindNearestInRadius(q, 0.2, i => p[i].Distance(q)), Budget));
            Assert.Equal(-1, found.Key);
        }
    }
}
