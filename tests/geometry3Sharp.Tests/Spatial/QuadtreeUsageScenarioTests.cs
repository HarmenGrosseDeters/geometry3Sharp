using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests.Spatial
{
    /// <summary>
    /// Whole usage scenarios on the adapters: edit cycles, a TIN swept vertex by vertex, stacked surfaces, and decisions right at the
    /// tolerance. Expected results come from scans here, and at the tolerance from exact arithmetic of the test's own.
    /// </summary>
    public class QuadtreeUsageScenarioTests
    {
        const double UtmX = 2539000, UtmY = 5640000;

        static Vector2d XY(Vector3d p) { return new Vector2d(p.x, p.y); }

        static AxisAlignedBox2d Box(params Vector3d[] points)
        {
            return new AxisAlignedBox2d(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
        }

        static bool Overlap(AxisAlignedBox2d a, AxisAlignedBox2d b)
        {
            return a.Min.x <= b.Max.x && b.Min.x <= a.Max.x && a.Min.y <= b.Max.y && b.Min.y <= a.Max.y;
        }

        // ---- exact arithmetic, in units of 2^-1074

        static BigInteger Exact(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 2047);
            long fraction = bits & 0xfffffffffffffL;
            BigInteger result = exponent == 0 ? new BigInteger(fraction) : new BigInteger(fraction | 0x10000000000000L) << (exponent - 1);
            return bits < 0 ? -result : result;
        }

        static bool ExactlyWithin(Vector2d p, Vector2d a, Vector2d b, double tolerance)
        {
            BigInteger dx = Exact(b.x) - Exact(a.x), dy = Exact(b.y) - Exact(a.y);
            BigInteger qx = Exact(p.x) - Exact(a.x), qy = Exact(p.y) - Exact(a.y);
            BigInteger tol = Exact(tolerance), dot = qx * dx + qy * dy, length = dx * dx + dy * dy;
            if (dot <= 0) return qx * qx + qy * qy <= tol * tol;
            if (dot >= length) {
                BigInteger rx = Exact(p.x) - Exact(b.x), ry = Exact(p.y) - Exact(b.y);
                return rx * rx + ry * ry <= tol * tol;
            }
            BigInteger cross = qx * dy - qy * dx;
            return cross * cross <= tol * tol * length;
        }

        static double Height(Vector3d a, Vector3d b, Vector3d c, Vector2d p)
        {
            BigInteger ax = Exact(a.x), ay = Exact(a.y);
            BigInteger ux = Exact(b.x) - ax, uy = Exact(b.y) - ay, vx = Exact(c.x) - ax, vy = Exact(c.y) - ay;
            BigInteger px = Exact(p.x) - ax, py = Exact(p.y) - ay, det = ux * vy - uy * vx;
            // The integers are in units of 2^-1074, far beyond double: divide first, keeping 64 bits of the weights.
            double u = (double)(((px * vy - py * vx) << 64) / det) / Math.Pow(2, 64);
            double v = (double)(((ux * py - uy * px) << 64) / det) / Math.Pow(2, 64);
            return a.z + u * (b.z - a.z) + v * (c.z - a.z);
        }

        // ---- decisions right at the tolerance

        [Theory]
        [InlineData(10.0, 0.0, 0.0, 0.01)]
        [InlineData(100.0, 0.0, 0.0, 0.01)]
        [InlineData(1000.0, 0.0, 0.0, 0.01)]
        [InlineData(1000.0, 0.0, 0.0, 1e-5)]
        [InlineData(1000.0, UtmX, UtmY, 0.01)]
        [InlineData(100.0, UtmX, UtmY, 0.001)]
        public void AtTheTolerance_BesideALongEdge_TheAnswerIsExact(double length, double x, double y, double tolerance)
        {
            var random = new Random(9);
            var found = new List<int>();
            int inside = 0, outside = 0;
            for (int i = 0; i < 1500; ++i) {
                double angle = random.NextDouble() * Math.PI * 2;
                Vector2d a = new Vector2d(x + random.NextDouble() * 20, y + random.NextDouble() * 20);
                Vector2d b = a + new Vector2d(Math.Cos(angle), Math.Sin(angle)) * length;
                Vector2d normal = new Vector2d(-Math.Sin(angle), Math.Cos(angle));
                // Within a few parts in 1e16 to 1e9 of the tolerance, on either side.
                double off = (random.Next(2) == 0 ? 1 : -1) * Math.Pow(10, -9 - random.NextDouble() * 7);
                Vector2d p = a + (b - a) * (0.1 + 0.8 * random.NextDouble()) + normal * (tolerance * (1 + off));
                bool expected = ExactlyWithin(p, a, b, tolerance);
                if (expected) ++inside; else ++outside;

                var segments = new SegmentQuadtree2d();
                segments.Insert(new Vector3d(a.x, a.y, 0), new Vector3d(b.x, b.y, 0));
                segments.QueryPoint(p, tolerance, found);
                Assert.True(expected == (found.Count == 1), $"segment, case {i}: expected {expected}");

                // The third vertex on the far side, so that this edge decides.
                Vector2d far = (a + b) * 0.5 - normal * (length * 0.5);
                var triangle = new Triangle3d(new Vector3d(a.x, a.y, 1), new Vector3d(b.x, b.y, 1), new Vector3d(far.x, far.y, 2));
                var triangles = new TriangleQuadtree2d(new[] { triangle });
                Assert.True(expected == triangles.TryGetHeight(p, out double height, tolerance), $"triangle, case {i}: expected {expected}");
            }
            Assert.True(inside > 100 && outside > 100, $"{inside} inside, {outside} outside");
        }

        [Fact]
        public void AtTheTolerance_KnownCases()
        {
            // The rounded distance of each lands on the wrong side of the tolerance.
            var thin = new TriangleQuadtree2d(new[] { new Triangle3d(
                new Vector3d(153.4841940521655, 153.4841940521655, 7),
                new Vector3d(971.9343026503615, 971.9343026503615, 8),
                new Vector3d(971.9343026503615, 971.9851870109081, 9)) });
            Assert.False(thin.TryGetHeight(new Vector2d(552.860314051206, 552.8588998376437), out double height, 0.001));

            Vector3d a = new Vector3d(6.007678111087381, 0.2773524728963862, 0), b = new Vector3d(0.4339618220812813, -8.025278840709444, 0);
            var p = new Vector2d(2.2907494201555543, -5.277341547112895);
            var segments = new SegmentQuadtree2d();
            int id = segments.Insert(a, b);
            var found = new List<int>();
            segments.QueryPoint(p, 0.01, found);
            Assert.Equal(new[] { id }, found);
            var triangles = new TriangleQuadtree2d(new[] { new Triangle3d(a, b, new Vector3d(-5.0818113470214978, 1.6997531050995716, 0)) });
            Assert.True(triangles.TryGetHeight(p, out height, 0.01));
        }

        // ---- a thin triangle

        [Fact]
        public void NearlyCollinearTriangle_KeepsItsHeight()
        {
            // The rounded determinant of this one is off by about 1e-5 in relative terms.
            Vector3d a = new Vector3d(501120.81590620376, 5601007.926008738, 7.674257600528309);
            Vector3d b = new Vector3d(501124.7257587103, 5601011.835861244, 87.6067603880571);
            Vector3d c = new Vector3d(501128.6356112169, 5601015.745713751, 8.636908470018259);
            var p = new Vector2d(501126.6806849636, 5601013.790787498);
            var tree = new TriangleQuadtree2d(new[] { new Triangle3d(a, b, c) });
            Assert.True(tree.TryGetHeight(p, out double height, 0.01));
            Assert.Equal(48.121834, height, 5);
        }

        // ---- edit cycles

        static IEnumerable<int> EveryThird(int count) { return Enumerable.Range(0, count).Where(i => i % 3 == 0); }

        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(UtmX, UtmY)]
        public void RemoveReinsertRemove_OnEveryAdapter_MatchesAScan(double x, double y)
        {
            var random = new Random(11);
            Vector3d[] points = Enumerable.Range(0, 900).Select(i => new Vector3d(x + random.NextDouble() * 500, y + random.NextDouble() * 500, random.NextDouble())).ToArray();
            Vector3d[][] edges = Enumerable.Range(0, 900).Select(i => new[] { points[i], points[i] + new Vector3d(random.NextDouble() * 10, random.NextDouble() * 10, 1) }).ToArray();
            Vector3d[][] corners = Enumerable.Range(0, 900).Select(i => new[] { points[i], points[i] + new Vector3d(random.NextDouble() * 10, 0, 1), points[i] + new Vector3d(0, random.NextDouble() * 10 + 0.1, 2) }).ToArray();

            var pointTree = new PointQuadtree2d(points);
            var segmentTree = new SegmentQuadtree2d(new Vector3d[0], new Index2i[0]);
            foreach (Vector3d[] e in edges) segmentTree.Insert(e[0], e[1]);
            var triangleTree = new TriangleQuadtree2d(corners.Select(c => new Triangle3d(c[0], c[1], c[2])).ToArray());

            // Live items per adapter, by ID. All three see the same edits.
            var live = new Dictionary<int, int>();
            for (int i = 0; i < 900; ++i) live[i] = i;
            var check = new Action<string>(stage => {
                Assert.Equal(live.Count, pointTree.Count);
                Assert.Equal(live.Count, segmentTree.Count);
                Assert.Equal(live.Count, triangleTree.Count);
                var found = new List<int>();
                for (int q = 0; q < 40; ++q) {
                    double qx = x + random.NextDouble() * 500, qy = y + random.NextDouble() * 500;
                    var query = new AxisAlignedBox2d(qx, qy, qx + 30, qy + 30);
                    pointTree.QueryRectangle(query, found);
                    Assert.Equal(live.Where(e => Overlap(Box(points[e.Value]), query)).Select(e => e.Key).OrderBy(i => i), found.OrderBy(i => i));
                    segmentTree.QueryRectangle(query, found);
                    Assert.Equal(live.Where(e => Overlap(Box(edges[e.Value]), query)).Select(e => e.Key).OrderBy(i => i), found.OrderBy(i => i));
                    triangleTree.QueryRectangle(query, found);
                    Assert.Equal(live.Where(e => Overlap(Box(corners[e.Value]), query)).Select(e => e.Key).OrderBy(i => i), found.OrderBy(i => i));
                }
            });

            check("built");
            foreach (int i in EveryThird(900)) {
                Assert.True(pointTree.Remove(points[i]));
                Assert.True(segmentTree.Remove(edges[i][0], edges[i][1]));
                Assert.True(triangleTree.Remove(new Triangle3d(corners[i][0], corners[i][1], corners[i][2])));
                live.Remove(i);
            }
            check("removed");
            var reinserted = new List<int>();
            foreach (int i in EveryThird(900)) {
                int id = pointTree.Insert(points[i]);
                Assert.Equal(id, segmentTree.Insert(edges[i][0], edges[i][1]));
                Assert.Equal(id, triangleTree.Insert(new Triangle3d(corners[i][0], corners[i][1], corners[i][2])));
                live[id] = i;
                reinserted.Add(id);
            }
            check("reinserted");
            foreach (int i in EveryThird(900)) {
                Assert.True(pointTree.Remove(points[i]));
                Assert.True(segmentTree.Remove(edges[i][0], edges[i][1]));
                Assert.True(triangleTree.Remove(new Triangle3d(corners[i][0], corners[i][1], corners[i][2])));
                Assert.False(pointTree.Remove(points[i]));
                Assert.False(segmentTree.Remove(edges[i][0], edges[i][1]));
                Assert.False(triangleTree.Remove(new Triangle3d(corners[i][0], corners[i][1], corners[i][2])));
            }
            foreach (int id in reinserted) live.Remove(id);
            check("removed again");
        }

        // ---- a whole TIN

        static Vector3d[,] JitteredGrid(int cells, double step, int seed, Func<double, double, double> z = null)
        {
            var random = new Random(seed);
            var grid = new Vector3d[cells + 1, cells + 1];
            for (int j = 0; j <= cells; ++j)
                for (int i = 0; i <= cells; ++i) {
                    double x = UtmX + i * step + (i > 0 && i < cells ? (random.NextDouble() - 0.5) * 0.3 * step : 0);
                    double y = UtmY + j * step + (j > 0 && j < cells ? (random.NextDouble() - 0.5) * 0.3 * step : 0);
                    grid[i, j] = new Vector3d(x, y, z != null ? z(x, y) : random.NextDouble() * 100);
                }
            return grid;
        }

        static List<Triangle3d> Triangles(Vector3d[,] grid)
        {
            var result = new List<Triangle3d>();
            for (int j = 0; j + 1 < grid.GetLength(1); ++j)
                for (int i = 0; i + 1 < grid.GetLength(0); ++i) {
                    result.Add(new Triangle3d(grid[i, j], grid[i + 1, j], grid[i + 1, j + 1]));
                    result.Add(new Triangle3d(grid[i, j], grid[i + 1, j + 1], grid[i, j + 1]));
                }
            return result;
        }

        static bool ExactlyInside(Triangle3d t, Vector2d p)
        {
            int orientation = ExactPredicates.Orient2d(XY(t.V0), XY(t.V1), XY(t.V2));
            return orientation != 0 && ExactPredicates.Orient2d(XY(t.V0), XY(t.V1), p) * orientation >= 0
                && ExactPredicates.Orient2d(XY(t.V1), XY(t.V2), p) * orientation >= 0
                && ExactPredicates.Orient2d(XY(t.V2), XY(t.V0), p) * orientation >= 0;
        }

        [Fact]
        public void EveryVertexOfATin_GivesAllItsTriangles_WithTheVertexHeight()
        {
            Vector3d[,] grid = JitteredGrid(24, 7.07, 3);
            List<Triangle3d> triangles = Triangles(grid);
            var tree = new TriangleQuadtree2d(triangles);
            var hits = new List<QuadtreeHeightHit2d>();
            foreach (Vector3d vertex in grid) {
                tree.QueryHeights(XY(vertex), hits, 0);
                int[] expected = Enumerable.Range(0, triangles.Count).Where(i => ExactlyInside(triangles[i], XY(vertex))).ToArray();
                Assert.Equal(expected, hits.Select(h => h.Id).OrderBy(i => i));
                Assert.All(hits, hit => Assert.Equal(vertex.z, hit.Height));
                Assert.True(tree.TryGetHeight(XY(vertex), out double height, 0));
                Assert.Equal(vertex.z, height);
            }
        }

        [Fact]
        public void PointsOfATin_GiveExactlyTheTrianglesThatHoldThem()
        {
            Vector3d[,] grid = JitteredGrid(24, 7.07, 4);
            List<Triangle3d> triangles = Triangles(grid);
            var tree = new TriangleQuadtree2d(triangles);
            var hits = new List<QuadtreeHeightHit2d>();
            var random = new Random(8);
            for (int q = 0; q < 800; ++q) {
                Triangle3d t = triangles[random.Next(triangles.Count)];
                // On an edge up to rounding, or inside.
                double u = random.NextDouble(), v = q % 2 == 0 ? 0 : random.NextDouble() * (1 - u);
                Vector2d p = XY(t.V0) * (1 - u - v) + XY(t.V1) * u + XY(t.V2) * v;
                tree.QueryHeights(p, hits, 0);
                int[] expected = Enumerable.Range(0, triangles.Count).Where(i => ExactlyInside(triangles[i], p)).ToArray();
                Assert.Equal(expected, hits.Select(h => h.Id).OrderBy(i => i));
                foreach (QuadtreeHeightHit2d hit in hits) {
                    Triangle3d holder = triangles[hit.Id];
                    Assert.Equal(Height(holder.V0, holder.V1, holder.V2, p), hit.Height, 6);
                }
            }
            // A point on a shared edge has two exact hits, and heights that agree up to rounding.
            Triangle3d first = triangles[0];
            Vector2d onDiagonal = (XY(first.V0) + XY(first.V2)) * 0.5;
            tree.QueryHeights(onDiagonal, hits, 0);
            Assert.NotEmpty(hits);
            foreach (QuadtreeHeightHit2d hit in hits) Assert.Equal(hits[0].Height, hit.Height, 9);
        }

        [Fact]
        public void TwoStackedSurfaces_GiveTwoHeights_AndExactHitsWinOverTolerance()
        {
            Vector3d[,] lower = JitteredGrid(12, 10, 5, (x, y) => 10 + (x - UtmX) * 0.01);
            Vector3d[,] upper = JitteredGrid(12, 10, 6, (x, y) => 50 + (y - UtmY) * 0.01);
            List<Triangle3d> both = Triangles(lower).Concat(Triangles(upper)).ToList();
            var tree = new TriangleQuadtree2d(both);
            var hits = new List<QuadtreeHeightHit2d>();
            var random = new Random(13);
            for (int q = 0; q < 300; ++q) {
                var p = new Vector2d(UtmX + 10 + random.NextDouble() * 100, UtmY + 10 + random.NextDouble() * 100);
                tree.QueryHeights(p, hits, 0.01);
                int exact = both.Count(t => ExactlyInside(t, p));
                Assert.Equal(exact, hits.Count);
                Assert.True(hits.Count >= 2);
                Assert.Contains(hits, h => Math.Abs(h.Height - (10 + (p.x - UtmX) * 0.01)) < 1e-6);
                Assert.Contains(hits, h => Math.Abs(h.Height - (50 + (p.y - UtmY) * 0.01)) < 1e-6);
            }

            // Just outside one surface and inside the other: only the exact hit.
            var inner = new Triangle3d(new Vector3d(0, 0, 1), new Vector3d(10, 0, 1), new Vector3d(0, 10, 1));
            var cover = new Triangle3d(new Vector3d(-5, -5, 2), new Vector3d(15, -5, 2), new Vector3d(-5, 15, 2));
            var mixed = new TriangleQuadtree2d(new[] { inner, cover });
            mixed.QueryHeights(new Vector2d(3, -0.005), hits, 0.01);
            Assert.Equal(new[] { 1 }, hits.Select(h => h.Id));
            Assert.Equal(2, hits[0].Height);
        }

        // ---- points on few positions

        [Fact]
        public void AQuarterOfThePointsOnFewPositions_NearbyMatchesAScan()
        {
            var random = new Random(21);
            Vector2d[] heavy = Enumerable.Range(0, 5).Select(i => new Vector2d(UtmX + i * 100, UtmY + i * 50)).ToArray();
            Vector3d[] points = Enumerable.Range(0, 40000).Select(i => {
                if (i % 4 == 0) { Vector2d h = heavy[random.Next(heavy.Length)]; return new Vector3d(h.x, h.y, i); }
                return new Vector3d(UtmX + random.NextDouble() * 1000, UtmY + random.NextDouble() * 1000, i);
            }).ToArray();
            var tree = new PointQuadtree2d(points);
            var found = new List<int>();
            for (int q = 0; q < 60; ++q) {
                Vector2d p = q % 3 == 0 ? heavy[q % heavy.Length] : new Vector2d(UtmX + random.NextDouble() * 1000, UtmY + random.NextDouble() * 1000);
                double tolerance = q % 2 == 0 ? 2.5 : 0.001;
                tree.QueryNearby(p, tolerance, found);
                int[] expected = Enumerable.Range(0, points.Length).Where(i => XY(points[i]).Distance(p) <= tolerance).ToArray();
                Assert.Equal(expected, found.OrderBy(i => i));
            }
        }

        // ---- the worked example of section 2.3, as far as the library has its queries

        [Fact]
        public void WorkedEdgeExample()
        {
            Vector3d[] vertices = {
                new Vector3d(0, 0, 0), new Vector3d(10, 0, 10), new Vector3d(5, -5, 100), new Vector3d(5, 5, 200), new Vector3d(10, 10, 20),
                new Vector3d(3, -2, 50), new Vector3d(3, 0.0005, 60), new Vector3d(20, 20, 0), new Vector3d(21, 21, 0) };
            Index2i[] edges = { new Index2i(0, 1), new Index2i(2, 3), new Index2i(1, 4), new Index2i(5, 6), new Index2i(1, 0), new Index2i(7, 8) };
            const int A = 0, B = 1, C = 2, D = 3, E = 4;
            var tree = new SegmentQuadtree2d(vertices, edges);
            var found = new List<int>();

            tree.QueryPoint(new Vector2d(5, 0), 0.001, found, excludeEndpoints: true);
            Assert.Equal(new[] { A, B, E }, found.OrderBy(i => i));
            tree.QueryPoint(new Vector2d(10, 0), 0.001, found, excludeEndpoints: true);
            Assert.Empty(found);
            tree.QueryPoint(new Vector2d(100, 100), 0.001, found, excludeEndpoints: true);
            Assert.Empty(found);
            tree.QueryPoint(new Vector2d(5, 0.0004), 0.001, found, excludeEndpoints: true);
            Assert.Equal(new[] { A, B, E }, found.OrderBy(i => i));
            tree.QueryPoint(new Vector2d(5, 0.01), 0.001, found, excludeEndpoints: true);
            Assert.Equal(new[] { B }, found);

            // Section 8: touching and duplicates count, there is no tolerance. D ends 0.0005 above A, so it crosses it. F is far.
            tree.QueryIntersections(new Vector2d(0, 0), new Vector2d(10, 0), found);
            Assert.Equal(new[] { A, B, C, D, E }, found.OrderBy(i => i));
        }

        [Fact]
        public void WorkedPointExample()
        {
            var tree = new PointQuadtree2d(new[] { new Vector3d(1, 1, 0), new Vector3d(1.0005, 1, 0), new Vector3d(1, 1, 0), new Vector3d(5, 5, 0), new Vector3d(1.01, 1, 0) });
            var found = new List<int>();
            tree.QueryNearby(new Vector2d(1, 1), 0.001, found);
            Assert.Equal(new[] { 0, 1, 2 }, found.OrderBy(i => i));
            tree.QueryNearby(new Vector2d(3, 3), 0.001, found);
            Assert.Empty(found);
        }
    }
}
