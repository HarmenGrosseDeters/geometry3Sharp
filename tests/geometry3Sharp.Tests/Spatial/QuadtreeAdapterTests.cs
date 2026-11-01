using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Xunit;

namespace geometry3Sharp.Tests.Spatial
{
    public class QuadtreeAdapterTests
    {
        static Vector2d XY(Vector3d p) { return new Vector2d(p.x, p.y); }
        static Segment3d Segment(double ax, double ay, double bx, double by)
        {
            // Preserve the specified endpoints without a normalization round-trip.
            return new Segment3d(new Vector3d((ax + bx) / 2, (ay + by) / 2, 0),
                new Vector3d((bx - ax) / 2, (by - ay) / 2, 0), 1);
        }
        static Triangle3d Triangle(double x = 0, double y = 0, double z = 1)
        {
            return new Triangle3d(new Vector3d(x, y, z), new Vector3d(x + 4, y, z + 8), new Vector3d(x, y + 4, z + 12));
        }
        static void Set(IEnumerable<int> expected, IEnumerable<int> actual)
        {
            Assert.Equal(expected.OrderBy(x => x), actual.OrderBy(x => x));
        }

        [Fact]
        public void SnapshotsGrowthRemovalAndEmptyQueries()
        {
            var points = new[] { new Vector3d(1, 2, 3), new Vector3d(1, 2, 3) };
            var p = new PointQuadtree2d(points);
            points[0] = new Vector3d(99, 99, 99);
            Assert.Equal(new Vector3d(1, 2, 3), p.GetItem(0));
            Assert.True(p.Remove(new Vector3d(1, 2, 3)));
            Assert.False(p.ContainsId(0));
            Assert.True(p.ContainsId(1));
            Assert.False(p.Remove(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.GetItem(0));
            Assert.True(p.Remove(1));
            Assert.Equal(2, p.Insert(new Vector3d(-500, 800, 1)));
            Assert.Equal(1, p.Count);
            var ids = new List<int> { 123 };
            p.QueryRectangle(new AxisAlignedBox2d(-500, 800, -500, 800), ids);
            Assert.Equal(new[] { 2 }, ids);
            p.QuerySegment(new Vector2d(-500, 800), new Vector2d(-500, 800), ids);
            Assert.Equal(new[] { 2 }, ids);
            var empty = new PointQuadtree2d();
            empty.QueryNearby(Vector2d.Zero, 1, ids);
            Assert.Empty(ids);
            Assert.Equal(-1, empty.FindNearest(Vector2d.Zero));
            Assert.Equal(0, empty.Insert(Vector3d.Zero));

            var segment = Segment(0, 0, 1, 0);
            var segments = new[] { segment, segment };
            var s = new SegmentQuadtree2d(segments);
            segments[0] = Segment(20, 20, 30, 30);
            Assert.Equal(segment.P0, s.GetItem(0).P0);
            Assert.True(s.Remove(segment));
            Assert.True(s.ContainsId(1));
            Assert.False(s.ContainsId(0));
            Assert.True(s.Remove(1));
            Assert.False(s.Remove(1));
            Assert.Equal(2, s.Insert(Segment(200, -200, 300, -300)));
            Assert.Equal(1, s.Count);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.GetItem(0));
            var se = new SegmentQuadtree2d();
            se.QueryIntersections(Vector2d.Zero, Vector2d.Zero, ids);
            Assert.Empty(ids);
            Assert.Equal(0, se.Insert(segment));

            var triangle = Triangle();
            var triangles = new[] { triangle, triangle };
            var t = new TriangleQuadtree2d(triangles);
            triangles[0] = Triangle(100, 100);
            Assert.Equal(triangle.V0, t.GetItem(0).V0);
            Assert.True(t.Remove(triangle));
            Assert.True(t.ContainsId(1));
            Assert.False(t.ContainsId(0));
            Assert.True(t.Remove(1));
            Assert.False(t.Remove(1));
            Assert.Equal(2, t.Insert(Triangle(-100, -100)));
            Assert.Equal(1, t.Count);
            Assert.Throws<ArgumentOutOfRangeException>(() => t.GetItem(0));
            var te = new TriangleQuadtree2d();
            Assert.False(te.TryGetHeight(Vector2d.Zero, out double height));
            Assert.Equal(0, height);
            Assert.Equal(0, te.Insert(triangle));
        }

        [Fact]
        public void InvalidInputsDoNotConsumeIdsOrClearValidOutputs()
        {
            var p = new PointQuadtree2d();
            var s = new SegmentQuadtree2d();
            var t = new TriangleQuadtree2d();
            Assert.Throws<ArgumentException>(() => p.Insert(new Vector3d(0, 0, double.NaN)));
            Assert.Throws<ArgumentException>(() => s.Insert(new Segment3d(Vector3d.Zero, Vector3d.AxisX, double.PositiveInfinity)));
            Assert.Throws<ArgumentException>(() => s.Insert(new Segment3d(Vector3d.Zero, Vector3d.AxisX, -1)));
            Assert.Throws<ArgumentException>(() => t.Insert(new Triangle3d(Vector3d.Zero, Vector3d.AxisX, new Vector3d(0, 1, double.NaN))));
            Assert.Equal(0, p.Count); Assert.Equal(0, s.Count); Assert.Equal(0, t.Count);
            Assert.Equal(0, p.Insert(Vector3d.Zero));
            Assert.Equal(0, s.Insert(Segment(0, 0, 1, 0)));
            Assert.Equal(0, t.Insert(Triangle()));
            var ids = new List<int> { 123 };
            foreach (double bad in new[] { -1.0, double.NaN, double.PositiveInfinity }) {
                Assert.Throws<ArgumentOutOfRangeException>(() => p.QueryNearby(Vector2d.Zero, bad, ids));
                Assert.Throws<ArgumentOutOfRangeException>(() => s.QueryPoint(Vector2d.Zero, bad, ids));
                Assert.Throws<ArgumentOutOfRangeException>(() => t.TryGetHeight(Vector2d.Zero, out _, bad));
            }
            Assert.Equal(new[] { 123 }, ids);
            Assert.Throws<ArgumentException>(() => s.QueryIntersections(new Vector2d(double.NaN, 0), Vector2d.Zero, ids));
            Assert.Throws<ArgumentNullException>(() => p.QueryNearby(Vector2d.Zero, 1, null));
            Assert.Throws<ArgumentNullException>(() => s.QueryIntersections(Vector2d.Zero, Vector2d.Zero, null));
            Assert.Throws<ArgumentNullException>(() => t.QueryHeights(Vector2d.Zero, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.FindNearestK(Vector2d.Zero, -1, ids));
            p.FindNearestK(Vector2d.Zero, 0, ids);
            Assert.Empty(ids);
            Assert.Throws<ArgumentOutOfRangeException>(() => t.FindNearest(Vector2d.Zero, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.FindNearest(Vector2d.Zero, -1));
        }

        [Fact]
        public void NearbyIsInclusiveEuclideanAndComplete()
        {
            var p = new PointQuadtree2d(new[] {
                new Vector3d(0, 0, 999), new Vector3d(3, 4, -999), new Vector3d(4, 4, 0),
                new Vector3d(0, 0, 1), new Vector3d(1000, 0, 0) });
            var ids = new List<int>();
            p.QueryNearby(Vector2d.Zero, 5, ids);
            Set(new[] { 0, 1, 3 }, ids);
            p.QueryNearby(Vector2d.Zero, 0, ids);
            Set(new[] { 0, 3 }, ids);
            p.QueryNearby(Vector2d.Zero, 1000, ids);
            Set(Enumerable.Range(0, 5), ids);
            p.FindNearestK(Vector2d.Zero, 10, ids);
            Assert.Equal(new[] { 0, 3, 1, 2, 4 }, ids);
            Assert.Equal(-1, p.FindNearest(new Vector2d(10000, 0), 1));
            Assert.Equal(0, p.FindNearest(new Vector2d(3, 0), 3));
        }

        [Fact]
        public void IntersectionsIncludeEndpointsCollinearDuplicatesAndZeroLength()
        {
            var s = new SegmentQuadtree2d(new[] { Segment(0, 0, 10, 0), Segment(10, 0, 0, 0),
                Segment(2, 0, 4, 0), Segment(5, -5, 5, 5), Segment(10, 0, 10, 2),
                Segment(3, 0, 3, 0), Segment(11, 0, 12, 0), Segment(5, 1, 5, 1) });
            var ids = new List<int>();
            s.QueryIntersections(Vector2d.Zero, new Vector2d(10, 0), ids);
            Set(new[] { 0, 1, 2, 3, 4, 5 }, ids);
            s.QueryIntersections(new Vector2d(3, 0), new Vector2d(3, 0), ids);
            Set(new[] { 0, 1, 2, 5 }, ids);
            s.QueryIntersections(new Vector2d(100, 100), new Vector2d(100, 100), ids);
            Assert.Empty(ids);
        }

        [Fact]
        public void SegmentPointExcludesEndpointsAndStopsOnlyOnHit()
        {
            var s = new SegmentQuadtree2d(new[] {
                Segment(-10, .02, 10, .02), Segment(-10, 0, 10, 0), Segment(0, -10, 0, 10), Segment(0, 0, 0, 0) });
            var ids = new List<int>();
            s.QueryPoint(Vector2d.Zero, .01, ids, true, true);
            Assert.Single(ids); Assert.Contains(ids[0], new[] { 1, 2 });
            s.QueryPoint(Vector2d.Zero, .01, ids, true);
            Set(new[] { 1, 2 }, ids);
            s.QueryPoint(Vector2d.Zero, 0, ids);
            Set(new[] { 1, 2, 3 }, ids);
            s.QueryPoint(new Vector2d(9.999, 0), .01, ids, true);
            Assert.Empty(ids);
            s.QueryPoint(new Vector2d(0, 100), 100, ids);
            Set(new[] { 0, 1, 2, 3 }, ids);
        }

        [Fact]
        public void HeightUsesExactPrecedence_AndBesideATriangleItsBoundary()
        {
            var near = Triangle(0, .25, 100);
            var exact = Triangle();
            var degenerate = new Triangle3d(Vector3d.Zero, Vector3d.AxisX, new Vector3d(2, 0, 0));
            var t = new TriangleQuadtree2d(new[] { near, exact, Triangle(0, 0, 10), degenerate });
            var hits = new List<QuadtreeHeightHit2d>();
            t.QueryHeights(new Vector2d(1, 0), hits, 1);
            Set(new[] { 1, 2 }, hits.Select(h => h.Id));
            Assert.Equal(new[] { 3.0, 12.0 }, hits.OrderBy(h => h.Id).Select(h => h.Height));
            t.QueryHeights(new Vector2d(1, 0), hits, 1, true);
            Assert.Single(hits); Assert.Contains(hits[0].Id, new[] { 1, 2 });
            Assert.True(t.TryGetHeight(new Vector2d(1, 0), out double z, 1));
            Assert.Contains(z, new[] { 3.0, 12.0 });
            var single = new TriangleQuadtree2d(new[] { exact });
            Assert.True(single.TryGetHeight(new Vector2d(1, -2), out z, 2));
            Assert.Equal(3, z, 12); // at (1, 0)
            Assert.False(single.TryGetHeight(new Vector2d(1, -2), out z, 1.999));
            Assert.True(single.TryGetHeight(new Vector2d(2, 2), out z, 0));
            Assert.Equal(11, z, 12);
            Assert.False(single.TryGetHeight(new Vector2d(2, 2 + 1e-12), out z, 0));
            var flat = new TriangleQuadtree2d(new[] { degenerate });
            flat.QueryHeights(Vector2d.Zero, hits, 100);
            Assert.Empty(hits);
            Assert.Equal(0, flat.FindNearest(Vector2d.Zero));
        }

        [Fact]
        public void NearestUsesActualGeometryNotBoundsAndStableTies()
        {
            var s = new SegmentQuadtree2d(new[] { Segment(0, 0, 10, 10), Segment(0, 9, 2, 9), Segment(0, 9, 2, 9) });
            var ids = new List<int>();
            Vector2d p = new Vector2d(0, 10);
            Assert.Equal(1, s.FindNearest(p));
            s.FindNearestK(p, 3, ids);
            Assert.Equal(new[] { 1, 2, 0 }, ids);
            s.FindNearestK(p, 3, ids, 1);
            Assert.Equal(new[] { 1, 2 }, ids);
            var t = new TriangleQuadtree2d(new[] { Triangle(), Triangle(3, 3), Triangle(3, 3) });
            Assert.Equal(1, t.FindNearest(new Vector2d(4, 4)));
            t.FindNearestK(new Vector2d(4, 4), 3, ids);
            Assert.Equal(new[] { 1, 2, 0 }, ids);
            t.FindNearestK(new Vector2d(4, 4), 3, ids, 0);
            Assert.Equal(new[] { 1, 2 }, ids);
        }

        [Fact]
        public void NumericScalingPreservesDistancesAndRepresentableHeights()
        {
            foreach (double scale in new[] { 1e-200, 1e200 }) {
                var p = new PointQuadtree2d(new[] { new Vector3d(3 * scale, 4 * scale, 0) });
                var ids = new List<int>();
                p.QueryNearby(Vector2d.Zero, 5.001 * scale, ids);
                Assert.Single(ids);
                p.QueryNearby(Vector2d.Zero, 4.999 * scale, ids);
                Assert.Empty(ids);
                var s = new SegmentQuadtree2d(new[] { new Segment3d(Vector3d.Zero, Vector3d.AxisX, scale) });
                s.QueryPoint(new Vector2d(0, scale), 1.001 * scale, ids);
                Assert.Single(ids);
                s.QueryPoint(new Vector2d(0, scale), .999 * scale, ids);
                Assert.Empty(ids);
                var triangle = new Triangle3d(Vector3d.Zero, new Vector3d(scale, 0, 4), new Vector3d(0, scale, 8));
                var t = new TriangleQuadtree2d(new[] { triangle });
                Assert.True(t.TryGetHeight(new Vector2d(.25 * scale, .25 * scale), out double z, 0));
                Assert.Equal(3, z, 12);
            }
            var thin = new Triangle3d(Vector3d.Zero, new Vector3d(1e300, 0, 1), new Vector3d(0, 1e-300, 1));
            Assert.True(new TriangleQuadtree2d(new[] { thin }).TryGetHeight(new Vector2d(2.5e299, 2.5e-301), out double height, 0));
            Assert.Equal(.5, height, 12);
            var cancellation = new Triangle3d(new Vector3d(0, 0, 1e308), new Vector3d(4, 0, -1e308), new Vector3d(0, 4, 1e308));
            Assert.True(new TriangleQuadtree2d(new[] { cancellation }).TryGetHeight(new Vector2d(2, 1), out height, 0));
            Assert.Equal(0, height);
            Assert.True(new TriangleQuadtree2d(new[] { cancellation }).TryGetHeight(new Vector2d(1, 1), out height, 0));
            Assert.Equal(5e307, height);
            var overflow = new Triangle3d(Vector3d.Zero, new Vector3d(1, 0, 1e308), new Vector3d(0, 1, 0));
            var overflowTree = new TriangleQuadtree2d(new[] { overflow });
            // The plane would overflow beside it. The boundary stays in the range of the triangle.
            Assert.True(overflowTree.TryGetHeight(new Vector2d(4, 0), out height, 3));
            Assert.Equal(1e308, height);
            var hits = new List<QuadtreeHeightHit2d>();
            overflowTree.QueryHeights(new Vector2d(4, 0), hits, 3);
            Assert.Equal(1e308, Assert.Single(hits).Height);
            Assert.True(overflowTree.TryGetHeight(Vector2d.Zero, out height, 0));
            Assert.Equal(0, height);
        }

        [Fact]
        public void SimilaritiesUseXYGeometryAndPreservePowerOfTwoResultsExactly()
        {
            foreach (Similarity sim in Similarity.All()) {
                // Full similarities followed by XY projection retain incidence, not Euclidean lengths
                // under out-of-plane rotations. Distance tests use the projected measured displacement.
                Vector3d a = sim.Apply(new Vector3d(0, 0, 1));
                Vector3d b = sim.Apply(new Vector3d(4, 0, 9));
                Vector3d c = sim.Apply(new Vector3d(0, 4, 13));
                Vector3d query = .5 * a + .25 * b + .25 * c;
                var triangles = new TriangleQuadtree2d(new[] { new Triangle3d(a, b, c) });
                Assert.True(triangles.TryGetHeight(XY(query), out double height, 0), sim.Name);
                if (sim.IsExact) Assert.Equal(query.z, height);
                else Assert.InRange(Math.Abs(height - query.z), 0, 1e-7 * Math.Max(1, Math.Abs(query.z)));
                var points = new PointQuadtree2d(new[] { a, b, c });
                var ids = new List<int>();
                points.QueryNearby(XY(query), 2 * sim.Scale, ids);
                var expected = new[] { a, b, c }.Select((p, i) => new { p, i })
                    .Where(v => XY(v.p).Distance(XY(query)) <= 2 * sim.Scale).Select(v => v.i);
                Set(expected, ids);
                var segment = new Segment3d((a + b) / 2, (b - a) / 2, 1);
                var segments = new SegmentQuadtree2d(new[] { segment });
                Vector2d midpoint = XY(segment.Center);
                segments.QueryPoint(midpoint, 1e-6 * sim.Scale, ids, true);
                Assert.Equal(new[] { 0 }, ids);
                Assert.Equal(0, triangles.FindNearest(XY(query), 0));
            }
        }

        [Fact]
        public void SerialParallelRepeatedAndNestedQueriesAgreeWithOracles()
        {
            var random = new Random(4567);
            var data = Enumerable.Range(0, 2000).Select(i => new Vector3d(random.NextDouble() * 100, random.NextDouble() * 100, i)).ToArray();
            var serial = new PointQuadtree2d(data, false);
            var parallel = new PointQuadtree2d(data, true);
            var segments = data.Take(1000).Select(p => Segment(p.x, p.y, p.x + 5, p.y + 2)).ToArray();
            var ss = new SegmentQuadtree2d(segments, false);
            var sp = new SegmentQuadtree2d(segments, true);
            var triangles = data.Take(1000).Select(p => Triangle(p.x, p.y)).ToArray();
            var ts = new TriangleQuadtree2d(triangles, false);
            var tp = new TriangleQuadtree2d(triangles, true);
            Parallel.For(0, 32, i => {
                Vector2d query = new Vector2d(i * 3, i * 2);
                var a = new List<int>(); var b = new List<int>();
                for (int repeat = 0; repeat < 3; ++repeat) {
                    serial.QueryNearby(query, 10, a); parallel.QueryNearby(query, 10, b);
                    Set(a, b);
                    Set(data.Select((p, id) => new { p, id }).Where(v => XY(v.p).Distance(query) <= 10).Select(v => v.id), b);
                    parallel.FindNearestK(query, 7, b);
                    Assert.Equal(data.Select((p, id) => new { id, d = XY(p).Distance(query) }).OrderBy(v => v.d).ThenBy(v => v.id).Take(7).Select(v => v.id), b);
                    ss.QueryPoint(query, 3, a); sp.QueryPoint(query, 3, b); Set(a, b);
                    Set(segments.Select((s, id) => new { id, d = ReferenceSegmentDistance(query, XY(s.P0), XY(s.P1)) }).Where(v => v.d <= 3).Select(v => v.id), b);
                    sp.FindNearestK(query, 5, b);
                    Assert.Equal(segments.Select((s, id) => new { id, d = ReferenceSegmentDistance(query, XY(s.P0), XY(s.P1)) }).OrderBy(v => v.d).ThenBy(v => v.id).Take(5).Select(v => v.id), b);
                    var ha = new List<QuadtreeHeightHit2d>(); var hb = new List<QuadtreeHeightHit2d>();
                    ts.QueryHeights(query, ha, 1); tp.QueryHeights(query, hb, 1);
                    Assert.Equal(ha.OrderBy(h => h.Id).Select(h => h.Height), hb.OrderBy(h => h.Id).Select(h => h.Height));
                    Set(ha.Select(h => h.Id), hb.Select(h => h.Id));
                }
            });
            var nested = new NestedVisitor { Points = parallel, Segments = sp, Triangles = tp, Ids = new List<int>(), Heights = new List<QuadtreeHeightHit2d>() };
            Assert.False(parallel.Traverse(ref nested));
            Assert.Equal(data.Length, nested.Visited);
        }
        static double ReferenceSegmentDistance(Vector2d p, Vector2d a, Vector2d b)
        {
            Vector2d edge = b - a;
            double u = Math.Max(0, Math.Min(1, (p - a).Dot(edge) / edge.LengthSquared));
            return p.Distance(a + u * edge);
        }
        struct NestedVisitor : IQuadtreeVisitor2d
        {
            internal PointQuadtree2d Points;
            internal SegmentQuadtree2d Segments;
            internal TriangleQuadtree2d Triangles;
            internal List<int> Ids;
            internal List<QuadtreeHeightHit2d> Heights;
            internal int Visited;
            public bool TestRegion(AxisAlignedBox2d region) { return true; }
            public bool Visit(int id)
            {
                Vector2d p = XY(Points.GetItem(id));
                Points.QueryNearby(p, 0, Ids);
                Assert.Contains(id, Ids);
                Segments.QueryPoint(p, .1, Ids);
                Triangles.QueryHeights(p, Heights);
                ++Visited;
                return false;
            }
        }

#if NET10_0_OR_GREATER
        [Fact]
        public void ReusedHotQueryBuffersAllocateNothingAfterWarmup()
        {
            var points = new PointQuadtree2d(new[] { new Vector3d(1, 1, 3) });
            var triangles = new TriangleQuadtree2d(new[] { Triangle() });
            var ids = new List<int>(16);
            var hits = new List<QuadtreeHeightHit2d>(16);
            Vector2d p = new Vector2d(1, 1);
            var rectangle = new AxisAlignedBox2d(0, 0, 2, 2);
            for (int i = 0; i < 100; ++i) {
                points.QueryNearby(p, 1, ids); points.QueryRectangle(rectangle, ids);
                points.QuerySegment(p, p, ids); triangles.QueryHeights(p, hits); triangles.TryGetHeight(p, out _);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; ++i) {
                points.QueryNearby(p, 1, ids); points.QueryRectangle(rectangle, ids);
                points.QuerySegment(p, p, ids); triangles.QueryHeights(p, hits); triangles.TryGetHeight(p, out _);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Single(ids); Assert.Single(hits); Assert.Equal(6, hits[0].Height);
        }
#endif
    }
}
