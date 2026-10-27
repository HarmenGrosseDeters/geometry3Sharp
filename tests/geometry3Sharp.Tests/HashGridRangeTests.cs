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

        [Fact]
        public void PointHashGrid3d_ScanAndLoop_BreakTiesTheSameWay()
        {
            // points on a lattice, queries equidistant from several of them: a grid with few occupied cells scans them, one
            // with many far dummy cells loops over the range; both must return the same point
            var p = new List<Vector3d>();
            for (int i = 0; i < 4; ++i) for (int j = 0; j < 4; ++j) for (int k = 0; k < 4; ++k)
                p.Add(new Vector3d(i, j, k) * 0.25);
            var few = new PointHashGrid3d<int>(0.1, -1);
            var many = new PointHashGrid3d<int>(0.1, -1);
            for (int i = 0; i < p.Count; ++i) { few.InsertPoint(i, p[i]); many.InsertPoint(i, p[i]); }
            for (int d = 0; d < 2000; ++d) many.InsertPoint(-2, new Vector3d(100 + d, 0, 0));
            foreach (Vector3d q in new[] { new Vector3d(0.125, 0.125, 0.125), new Vector3d(0.375, 0.25, 0.125), new Vector3d(0.25, 0.25, 0.25) }) {
                var a = few.FindNearestInRadius(q, 0.5, i => i < 0 ? double.MaxValue : p[i].Distance(q));
                var b = many.FindNearestInRadius(q, 0.5, i => i < 0 ? double.MaxValue : p[i].Distance(q));
                Assert.Equal(b.Key, a.Key);
                Assert.Equal(b.Value, a.Value);
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void PointHashGrid3d_SameNearestUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            List<Vector3d> p = Points(Vector3d.Zero, 1);
            List<Vector3d> q = p.Select(v => s.Apply(v)).ToList();
            var g0 = new PointHashGrid3d<int>(0.1, -1);
            var g1 = new PointHashGrid3d<int>(s.ApplyLength(0.1), -1);
            for (int i = 0; i < p.Count; ++i) { g0.InsertPoint(i, p[i]); g1.InsertPoint(i, q[i]); }
            var r = new Random(11);
            for (int k = 0; k < 30; ++k) {
                Vector3d x = new Vector3d(r.NextDouble(), r.NextDouble(), r.NextDouble()), y = s.Apply(x);
                var a = g0.FindNearestInRadius(x, 0.2, i => p[i].Distance(x));
                var b = g1.FindNearestInRadius(y, s.ApplyLength(0.2), i => q[i].Distance(y));
                Assert.Equal(a.Key, b.Key);
                if (a.Key < 0) continue;
                if (s.IsExact)
                    Assert.Equal(a.Value * s.Scale, b.Value);
                else
                    Assert.True(Math.Abs(a.Value - b.Value / s.Scale) < 1e-6, $"{name}: {a.Value} vs {b.Value / s.Scale}");
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
