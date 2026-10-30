using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    // Independent, clean-room acceptance tests derived from QUADTREE2D_SPEC sections 7 and 8.
    public class Quadtree2dAcceptanceTests
    {
        static Vector2d XY(Vector3d p) => new Vector2d(p.x, p.y);
        static Vector3d P(double x, double y, double z = 0) => new Vector3d(x, y, z);
        static AxisAlignedBox2d Box(double x0, double y0, double x1, double y1) =>
            new AxisAlignedBox2d(x0, y0, x1, y1);
        static AxisAlignedBox2d Bounds(Vector3d p) => new AxisAlignedBox2d(XY(p));
        static AxisAlignedBox2d Bounds(Segment3d s) => new AxisAlignedBox2d(XY(s.P0), XY(s.P1));
        static AxisAlignedBox2d Bounds(Triangle3d t) => Box(
            Math.Min(t.V0.x, Math.Min(t.V1.x, t.V2.x)), Math.Min(t.V0.y, Math.Min(t.V1.y, t.V2.y)),
            Math.Max(t.V0.x, Math.Max(t.V1.x, t.V2.x)), Math.Max(t.V0.y, Math.Max(t.V1.y, t.V2.y)));

        static bool Overlaps(AxisAlignedBox2d a, AxisAlignedBox2d b) =>
            a.Min.x <= b.Max.x && b.Min.x <= a.Max.x && a.Min.y <= b.Max.y && b.Min.y <= a.Max.y;

        static double Distance(Vector2d p, Vector2d q)
        {
            double x = p.x - q.x, y = p.y - q.y;
            return Math.Sqrt(x * x + y * y);
        }

        static double BoxDistance(Vector2d p, AxisAlignedBox2d b)
        {
            double x = Math.Max(b.Min.x - p.x, Math.Max(0, p.x - b.Max.x));
            double y = Math.Max(b.Min.y - p.y, Math.Max(0, p.y - b.Max.y));
            return Math.Sqrt(x * x + y * y);
        }

        static double SegmentDistance(Vector2d p, Vector2d a, Vector2d b)
        {
            double dx = b.x - a.x, dy = b.y - a.y;
            double denominator = dx * dx + dy * dy;
            double t = denominator == 0 ? 0 : ((p.x - a.x) * dx + (p.y - a.y) * dy) / denominator;
            t = Math.Max(0, Math.Min(1, t));
            return Distance(p, new Vector2d(a.x + t * dx, a.y + t * dy));
        }

        static double Cross(Vector2d a, Vector2d b, Vector2d p) =>
            (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        static bool Inside(Vector2d p, Triangle3d t)
        {
            Vector2d a = XY(t.V0), b = XY(t.V1), c = XY(t.V2);
            if (Cross(a, b, c) == 0) return false;
            double ab = Cross(a, b, p), bc = Cross(b, c, p), ca = Cross(c, a, p);
            return (ab >= 0 && bc >= 0 && ca >= 0) || (ab <= 0 && bc <= 0 && ca <= 0);
        }

        static double TriangleDistance(Vector2d p, Triangle3d t)
        {
            if (Inside(p, t)) return 0;
            return Math.Min(SegmentDistance(p, XY(t.V0), XY(t.V1)),
                Math.Min(SegmentDistance(p, XY(t.V1), XY(t.V2)), SegmentDistance(p, XY(t.V2), XY(t.V0))));
        }

        // Independent closed-slab clipping oracle, including point segments and zero-width boxes.
        static bool Clips(Vector2d a, Vector2d b, AxisAlignedBox2d box, double margin = 0)
        {
            double lo = 0, hi = 1;
            for (int axis = 0; axis < 2; ++axis) {
                double start = axis == 0 ? a.x : a.y;
                double delta = axis == 0 ? b.x - a.x : b.y - a.y;
                double min = (axis == 0 ? box.Min.x : box.Min.y) - margin;
                double max = (axis == 0 ? box.Max.x : box.Max.y) + margin;
                if (delta == 0) {
                    if (start < min || start > max) return false;
                } else {
                    double t0 = (min - start) / delta, t1 = (max - start) / delta;
                    lo = Math.Max(lo, Math.Min(t0, t1));
                    hi = Math.Min(hi, Math.Max(t0, t1));
                    if (lo > hi) return false;
                }
            }
            return true;
        }

        static void SameIds(IEnumerable<int> expected, IEnumerable<int> actual)
        {
            int[] result = actual.ToArray();
            Assert.Equal(result.Length, result.Distinct().Count());
            Assert.Equal(expected.OrderBy(x => x).ToArray(), result.OrderBy(x => x).ToArray());
        }

        static void SameBox(AxisAlignedBox2d expected, AxisAlignedBox2d actual)
        {
            Assert.Equal(expected.Min.x, actual.Min.x);
            Assert.Equal(expected.Min.y, actual.Min.y);
            Assert.Equal(expected.Max.x, actual.Max.x);
            Assert.Equal(expected.Max.y, actual.Max.y);
        }

        static int[] Ranked(int count, Func<int, double> distance, int k, double maxDistance)
        {
            return Enumerable.Range(0, count).Select(i => new { Id = i, D = distance(i) })
                .Where(v => v.D <= maxDistance).OrderBy(v => v.D).ThenBy(v => v.Id)
                .Take(k).Select(v => v.Id).ToArray();
        }

        static void CheckBroadPhase(IReadOnlyList<AxisAlignedBox2d> boxes,
            Action<AxisAlignedBox2d, List<int>> rectangle,
            Action<Vector2d, Vector2d, List<int>> segment, int seed)
        {
            var random = new Random(seed);
            var result = new List<int> { -99 };
            for (int n = 0; n < 100; ++n) {
                double x = random.Next(-25, 25) * 0.5, y = random.Next(-25, 25) * 0.5;
                var q = Box(x, y, x + random.Next(0, 10) * 0.5, y + random.Next(0, 10) * 0.5);
                rectangle(q, result);
                SameIds(Enumerable.Range(0, boxes.Count).Where(i => Overlaps(boxes[i], q)), result);
                Vector2d a = q.Min, b = n % 3 == 0 ? a : new Vector2d(random.Next(-12, 12), random.Next(-12, 12));
                segment(a, b, result);
                SameIds(Enumerable.Range(0, boxes.Count).Where(i => Clips(a, b, boxes[i], 0.0002)), result);
                var forward = result.ToArray();
                segment(b, a, result);
                SameIds(forward, result);
            }
        }

        [Fact]
        public void D1_ClosedGridBordersIncludingOffsetsAndDegenerateQueries()
        {
            foreach (Vector2d origin in new[] { new Vector2d(0, 0), new Vector2d(-16, -8), new Vector2d(2539000, 5640000) }) {
                var boxes = new List<AxisAlignedBox2d>();
                for (int y = 0; y < 16; ++y)
                    for (int x = 0; x < 16; ++x) {
                        var b = Box(origin.x + x * 0.5, origin.y + y * 0.5,
                            origin.x + (x + 1) * 0.5, origin.y + (y + 1) * 0.5);
                        boxes.Add(b);
                        boxes.Add(b);
                    }
                var tree = new LooseQuadtree2d(boxes);
                var result = new List<int>();
                for (int line = 0; line <= 16; ++line) {
                    double coordinate = line * 0.5;
                    foreach (var q in new[] {
                        Box(origin.x + coordinate, origin.y, origin.x + coordinate, origin.y + 8),
                        Box(origin.x, origin.y + coordinate, origin.x + 8, origin.y + coordinate),
                        Box(origin.x + coordinate, origin.y + coordinate, origin.x + coordinate, origin.y + coordinate) }) {
                        tree.QueryRectangle(q, result);
                        SameIds(Enumerable.Range(0, boxes.Count).Where(i => Overlaps(boxes[i], q)), result);
                    }
                }
                tree.QueryRectangle(Box(origin.x - 1, origin.y - 1, origin.x + 9, origin.y + 9), result);
                SameIds(Enumerable.Range(0, boxes.Count), result);
                tree.QueryRectangle(Box(origin.x - 3, origin.y - 3, origin.x - 1, origin.y - 1), result);
                Assert.Empty(result);
            }
        }

        [Fact]
        public void D6_LongThinBoxesAreVisibleFromBothEnds()
        {
            var boxes = new List<AxisAlignedBox2d>();
            for (int i = 0; i < 64; ++i) {
                boxes.Add(Box(-10000, i * 0.5, 10000, i * 0.5 + 0.00001));
                boxes.Add(Box(i * 0.5, -10000, i * 0.5 + 0.00001, 10000));
            }
            var tree = new LooseQuadtree2d(boxes);
            var result = new List<int>();
            for (int i = 0; i < boxes.Count; ++i) {
                foreach (Vector2d p in new[] { boxes[i].Min, boxes[i].Max }) {
                    var query = new AxisAlignedBox2d(p);
                    tree.QueryRectangle(query, result);
                    SameIds(Enumerable.Range(0, boxes.Count).Where(j => Overlaps(boxes[j], query)), result);
                }
            }
        }

        [Fact]
        public void RandomCoreRectangleSegmentAndNearestMatchIndependentOracles()
        {
            var random = new Random(871);
            var boxes = new List<AxisAlignedBox2d>();
            for (int i = 0; i < 300; ++i) {
                double x = random.Next(-100, 100) * 0.1, y = random.Next(-100, 100) * 0.1;
                boxes.Add(Box(x, y, x + random.Next(0, 30) * 0.1, y + random.Next(0, 30) * 0.1));
            }
            boxes.AddRange(new[] { boxes[0], Box(0, 0, 0, 0), Box(-20, 0, 20, 0), Box(0, -20, 0, 20) });
            var tree = new LooseQuadtree2d(boxes, false);
            CheckBroadPhase(boxes, tree.QueryRectangle, tree.QuerySegment, 823);
            var result = new List<int>();
            for (int i = 0; i < 100; ++i) {
                var p = new Vector2d(random.Next(-150, 150) * 0.1, random.Next(-150, 150) * 0.1);
                double max = i % 2 == 0 ? double.PositiveInfinity : i * 0.01;
                int[] expected = Ranked(boxes.Count, id => BoxDistance(p, boxes[id]), 13, max);
                tree.FindNearestK(p, 13, result, max);
                Assert.Equal(expected, result.ToArray());
                Assert.Equal(expected.Length == 0 ? -1 : expected[0], tree.FindNearest(p, max));
            }
        }

        [Fact]
        public void SegmentBroadPhaseHasExactlyTheSpecifiedClosedMargin()
        {
            var boxes = new[] { Box(0, 0, 1, 1), Box(0, 1.0001, 1, 1.0001),
                Box(0, 1.0003, 1, 1.0003), Box(2, 0, 2, 0) };
            var tree = new LooseQuadtree2d(boxes);
            var result = new List<int>();
            foreach (var pair in new[] {
                new[] { new Vector2d(-1, 1), new Vector2d(3, 1) },
                new[] { new Vector2d(2, 0), new Vector2d(2, 0) },
                new[] { new Vector2d(-1, -1), new Vector2d(0, 0) },
                new[] { new Vector2d(1.0002, -1), new Vector2d(1.0002, 2) } }) {
                tree.QuerySegment(pair[0], pair[1], result);
                SameIds(Enumerable.Range(0, boxes.Length).Where(i => Clips(pair[0], pair[1], boxes[i], 0.0002)), result);
            }
        }

        [Fact]
        public void D2_D7_GrowthInvalidInsertionAndStableIdsAfterAllRemovals()
        {
            var tree = new LooseQuadtree2d();
            var result = new List<int> { 42 };
            tree.QueryRectangle(Box(-1, -1, 1, 1), result);
            Assert.Empty(result);
            Assert.Equal(-1, tree.FindNearest(new Vector2d(0, 0)));
            int first = tree.Insert(Box(0, 0, 0, 0));
            Assert.Equal(0, first);
            var large = Box(-100000, -100, 100000, 100);
            Assert.Equal(1, tree.Insert(large));
            Assert.Equal(2, tree.Insert(large));
            foreach (var bad in InvalidBoxes()) {
                int before = tree.Count;
                AxisAlignedBox2d extent = tree.Extent;
                Assert.ThrowsAny<ArgumentException>(() => tree.Insert(bad));
                Assert.Equal(before, tree.Count);
                SameBox(extent, tree.Extent);
            }
            Assert.Equal(3, tree.Insert(Box(100001, 0, 100002, 0)));
            Assert.True(tree.Remove(large));
            Assert.False(tree.ContainsId(1));
            Assert.True(tree.ContainsId(2));
            Assert.False(tree.Remove(1));
            Assert.True(tree.Remove(large));
            Assert.False(tree.Remove(large));
            Assert.False(tree.Remove(2));
            Assert.Equal(2, tree.Count);
            Assert.True(tree.Remove(0));
            Assert.True(tree.Remove(3));
            Assert.Equal(0, tree.Count);
            AxisAlignedBox2d retained = tree.Extent;
            Assert.False(tree.Remove(-1));
            Assert.False(tree.Remove(int.MaxValue));
            Assert.ThrowsAny<ArgumentException>(() => tree.GetBounds(1));
            int id = tree.Insert(Box(1, 1, 1, 1));
            Assert.Equal(4, id);
            SameBox(retained, tree.Extent);
            tree.QueryRectangle(retained, result);
            SameIds(new[] { id }, result);
            SameBox(Box(1, 1, 1, 1), tree.GetBounds(id));
        }

        static IEnumerable<AxisAlignedBox2d> InvalidBoxes()
        {
            yield return Box(1, 0, -1, 2);
            yield return Box(0, 1, 2, -1);
            yield return Box(double.NaN, 0, 1, 1);
            yield return Box(0, 0, double.PositiveInfinity, 1);
            yield return Box(0, double.NegativeInfinity, 1, 1);
        }

        [Fact]
        public void CoreRejectsInvalidArgumentsAndOwnsSnapshots()
        {
            var source = new List<AxisAlignedBox2d> { Box(0, 0, 1, 1) };
            var tree = new LooseQuadtree2d(source);
            source[0] = Box(100, 100, 101, 101);
            SameBox(Box(0, 0, 1, 1), tree.GetBounds(0));
            var result = new List<int>();
            foreach (var bad in InvalidBoxes()) {
                Assert.ThrowsAny<ArgumentException>(() => new LooseQuadtree2d(new[] { bad }));
                Assert.ThrowsAny<ArgumentException>(() => tree.QueryRectangle(bad, result));
            }
            foreach (var p in new[] { new Vector2d(double.NaN, 0), new Vector2d(0, double.PositiveInfinity) }) {
                Assert.ThrowsAny<ArgumentException>(() => tree.FindNearest(p));
                Assert.ThrowsAny<ArgumentException>(() => tree.FindNearestK(p, 2, result));
                Assert.ThrowsAny<ArgumentException>(() => tree.QuerySegment(p, new Vector2d(0, 0), result));
                Assert.ThrowsAny<ArgumentException>(() => tree.QuerySegment(new Vector2d(0, 0), p, result));
            }
            foreach (double bad in new[] { -1.0, double.NaN, double.NegativeInfinity }) {
                Assert.ThrowsAny<ArgumentException>(() => tree.FindNearest(new Vector2d(0, 0), bad));
                Assert.ThrowsAny<ArgumentException>(() => tree.FindNearestK(new Vector2d(0, 0), 1, result, bad));
            }
            Assert.ThrowsAny<ArgumentException>(() => tree.FindNearestK(new Vector2d(0, 0), -1, result));
            Assert.ThrowsAny<ArgumentException>(() => tree.QueryRectangle(Box(0, 0, 1, 1), null));
            Assert.ThrowsAny<ArgumentException>(() => tree.QuerySegment(new Vector2d(0, 0), new Vector2d(1, 1), null));
            Assert.ThrowsAny<ArgumentException>(() => tree.FindNearestK(new Vector2d(0, 0), 1, null));
            Assert.ThrowsAny<ArgumentException>(() => tree.GetBounds(-1));
            result.Add(123);
            tree.FindNearestK(new Vector2d(0, 0), 0, result);
            Assert.Empty(result);
            Assert.Equal(1, tree.Count);
        }

        struct CustomDistance : IQuadtreeDistance2d
        {
            public double[] Values;
            public int Calls;
            public double Distance(int id) { ++Calls; return Values[id]; }
        }

        [Fact]
        public void GenericNearestUsesGeometryDistancesAndStableTieBreaks()
        {
            var tree = new LooseQuadtree2d(new[] { Box(-10, -10, 10, 10), Box(-2, -2, 2, 2),
                Box(1, 0, 1, 0), Box(-1, 0, -1, 0), Box(0, 1, 0, 1) });
            var distance = new CustomDistance { Values = new[] { 9.0, 4.0, 1.0, 1.0, 1.0 } };
            var p = new Vector2d(0, 0);
            Assert.Equal(2, tree.FindNearest<CustomDistance>(p, ref distance));
            Assert.True(distance.Calls > 0);
            var result = new List<int>();
            tree.FindNearestK<CustomDistance>(p, 20, result, ref distance, 4);
            Assert.Equal(new[] { 2, 3, 4, 1 }, result.ToArray());
            Assert.Equal(-1, tree.FindNearest<CustomDistance>(p, ref distance, 0.5));
            tree.FindNearestK<CustomDistance>(p, 0, result, ref distance);
            Assert.Empty(result);
            tree.FindNearestK(p, 20, result);
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, result.ToArray());
            Assert.True(tree.Remove(2));
            Assert.Equal(3, tree.FindNearest<CustomDistance>(p, ref distance, 1));
        }

        struct Visitor : IQuadtreeVisitor2d
        {
            public LooseQuadtree2d Tree;
            public HashSet<int> Seen;
            public List<int> Nested;
            public AxisAlignedBox2d Query;
            public bool RejectRegions;
            public bool Stop;
            public bool TestRegion(AxisAlignedBox2d region) => !RejectRegions && Overlaps(region, Query);
            public bool Visit(int id)
            {
                Assert.True(Seen.Add(id));
                if (Tree != null) {
                    Tree.QueryRectangle(Query, Nested);
                    Assert.Contains(id, Nested);
                    var child = new Visitor { Seen = new HashSet<int>(), Query = Query };
                    Assert.False(Tree.Traverse(ref child));
                    SameIds(Nested, child.Seen);
                }
                return Stop;
            }
        }

        [Fact]
        public void D4_TraversalEarlyStopRegionRejectionAndNestedQueriesAreIndependent()
        {
            var boxes = Enumerable.Range(0, 200).Select(i => Box(i % 20, i / 20, i % 20 + 0.5, i / 20 + 0.5)).ToArray();
            var tree = new LooseQuadtree2d(boxes);
            var query = Box(-1, -1, 30, 30);
            var visitor = new Visitor { Tree = tree, Query = query, Seen = new HashSet<int>(), Nested = new List<int>() };
            Assert.False(tree.Traverse(ref visitor));
            SameIds(Enumerable.Range(0, boxes.Length), visitor.Seen);
            visitor = new Visitor { Query = query, Seen = new HashSet<int>(), Stop = true };
            Assert.True(tree.Traverse(ref visitor));
            Assert.Single(visitor.Seen);
            Assert.InRange(visitor.Seen.Single(), 0, boxes.Length - 1);
            visitor = new Visitor { Query = query, Seen = new HashSet<int>(), RejectRegions = true };
            Assert.False(tree.Traverse(ref visitor));
            Assert.Empty(visitor.Seen);
            var empty = new LooseQuadtree2d();
            Assert.False(empty.Traverse(ref visitor));
        }

        [Fact]
        public void ParallelAndSerialBuildsAndConcurrentQueriesAgree()
        {
            var boxes = Enumerable.Range(0, 40000).Select(i => Box(i % 200, i / 200, i % 200 + 1, i / 200 + 1)).ToArray();
            var serial = new LooseQuadtree2d(boxes, false);
            var parallel = new LooseQuadtree2d(boxes, true);
            Parallel.For(0, 96, n => {
                var query = Box(n, n * 0.5, n + 0.5, n * 0.5 + 1);
                var expected = new List<int>();
                var actual = new List<int>();
                serial.QueryRectangle(query, expected);
                for (int repeat = 0; repeat < 3; ++repeat) {
                    parallel.QueryRectangle(query, actual);
                    SameIds(expected, actual);
                    parallel.QuerySegment(query.Min, query.Max, actual);
                    serial.QuerySegment(query.Min, query.Max, expected);
                    SameIds(expected, actual);
                    parallel.FindNearestK(query.Center, 7, actual);
                    serial.FindNearestK(query.Center, 7, expected);
                    Assert.Equal(expected.ToArray(), actual.ToArray());
                    serial.QueryRectangle(query, expected);
                }
            });
        }

        [Fact]
        public void DegenerateTinyAndElongatedExtentsWork()
        {
            foreach (var boxes in new[] {
                new[] { Box(0, 0, 0, 0) },
                Enumerable.Repeat(Box(0.125, 0.25, 0.125, 0.25), 1000).ToArray(),
                Enumerable.Range(0, 1000).Select(i => Box(i * 1e-8, 0, i * 1e-8, 0)).ToArray(),
                Enumerable.Range(0, 1000).Select(i => Box(i * 20, 0, i * 20 + 1, 0.001)).ToArray() }) {
                var tree = new LooseQuadtree2d(boxes);
                var result = new List<int>();
                tree.QueryRectangle(tree.Extent, result);
                SameIds(Enumerable.Range(0, boxes.Length), result);
                tree.FindNearestK(boxes[0].Min, boxes.Length + 1, result);
                Assert.Equal(Ranked(boxes.Length, i => BoxDistance(boxes[0].Min, boxes[i]), boxes.Length, double.PositiveInfinity), result.ToArray());
                Assert.Equal(boxes.Length, tree.Count);
            }
        }

        [Fact]
        public void TriangleAdapterGridBordersAndLongThinTrianglesMatchAabbOracle()
        {
            var triangles = new List<Triangle3d>();
            for (int y = 0; y < 12; ++y)
                for (int x = 0; x < 12; ++x) {
                    triangles.Add(new Triangle3d(P(x, y), P(x + 1, y), P(x + 1, y + 1)));
                    triangles.Add(new Triangle3d(P(x, y), P(x + 1, y + 1), P(x, y + 1)));
                }
            triangles.Add(new Triangle3d(P(-10000, 4), P(10000, 4), P(10000, 4.00001)));
            triangles.Add(new Triangle3d(P(4, -10000), P(4, 10000), P(4.00001, 10000)));
            var tree = new TriangleQuadtree2d(triangles);
            var result = new List<int>();
            for (int i = 0; i < triangles.Count; ++i) {
                AxisAlignedBox2d bounds = Bounds(triangles[i]);
                foreach (Vector2d p in new[] { bounds.Min, bounds.Max }) {
                    var q = new AxisAlignedBox2d(p);
                    tree.QueryRectangle(q, result);
                    SameIds(Enumerable.Range(0, triangles.Count).Where(j => Overlaps(Bounds(triangles[j]), q)), result);
                }
            }
        }

        static bool SegmentsIntersect(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            if (!Overlaps(new AxisAlignedBox2d(a, b), new AxisAlignedBox2d(c, d))) return false;
            double c0 = Cross(a, b, c), c1 = Cross(a, b, d), c2 = Cross(c, d, a), c3 = Cross(c, d, b);
            return ((c0 <= 0 && c1 >= 0) || (c0 >= 0 && c1 <= 0)) &&
                ((c2 <= 0 && c3 >= 0) || (c2 >= 0 && c3 <= 0));
        }

        [Fact]
        public void RandomNearbyPointOnSegmentAndIntersectionSetsMatchGeometryOracles()
        {
            var random = new Random(616);
            var points = Enumerable.Range(0, 200).Select(i => P(random.Next(-20, 20), random.Next(-20, 20), i)).ToArray();
            // Axis-aligned stored segments make every endpoint exactly representable in Segment3d.
            var segments = points.Select((p, i) => new Segment3d(p, p + (i % 2 == 0 ? P(i % 7, 0) : P(0, i % 7)))).ToArray();
            var pt = new PointQuadtree2d(points);
            var st = new SegmentQuadtree2d(segments);
            var result = new List<int>();
            for (int query = 0; query < 100; ++query) {
                var p = new Vector2d(random.Next(-20, 20), random.Next(-20, 20));
                var end = new Vector2d(random.Next(-20, 20), random.Next(-20, 20));
                double tolerance = query % 6;
                pt.QueryNearby(p, tolerance, result);
                SameIds(Enumerable.Range(0, points.Length).Where(i => Distance(p, XY(points[i])) <= tolerance), result);
                foreach (bool exclude in new[] { false, true }) {
                    int[] expected = Enumerable.Range(0, segments.Length).Where(i =>
                        SegmentDistance(p, XY(segments[i].P0), XY(segments[i].P1)) <= tolerance &&
                        (!exclude || (Distance(p, XY(segments[i].P0)) > tolerance && Distance(p, XY(segments[i].P1)) > tolerance))).ToArray();
                    st.QueryPoint(p, tolerance, result, exclude);
                    SameIds(expected, result);
                    st.QueryPoint(p, tolerance, result, exclude, true);
                    Assert.Equal(expected.Length == 0 ? 0 : 1, result.Count);
                    if (result.Count > 0) Assert.Contains(result[0], expected);
                }
                st.QueryIntersections(p, end, result);
                SameIds(Enumerable.Range(0, segments.Length).Where(i => SegmentsIntersect(p, end, XY(segments[i].P0), XY(segments[i].P1))), result);
            }
        }

        [Fact]
        public void RandomHeightSetsMatchAbsoluteDistanceAndGlobalExactPriorityOracles()
        {
            var random = new Random(914);
            var triangles = new List<Triangle3d>();
            for (int i = 0; i < 100; ++i) {
                Func<Vector3d> vertex = () => {
                    double x = random.Next(-10, 10), y = random.Next(-10, 10);
                    return P(x, y, x + 2 * y + i * 100);
                };
                triangles.Add(new Triangle3d(vertex(), vertex(), vertex()));
            }
            var tree = new TriangleQuadtree2d(triangles);
            for (int i = 0; i < 60; ++i) {
                var p = new Vector2d(random.Next(-60, 60) * 0.25, random.Next(-60, 60) * 0.25);
                double tolerance = (i % 5) * 0.25;
                int[] valid = Enumerable.Range(0, triangles.Count).Where(j =>
                    Cross(XY(triangles[j].V0), XY(triangles[j].V1), XY(triangles[j].V2)) != 0).ToArray();
                int[] exact = valid.Where(j => Inside(p, triangles[j])).ToArray();
                int[] expected = exact.Length > 0 ? exact : valid.Where(j => TriangleDistance(p, triangles[j]) <= tolerance).ToArray();
                CheckHeights(tree, p, tolerance, expected, j => p.x + 2 * p.y + j * 100);
            }
        }

        sealed class Adapter<T>
        {
            public Func<int> Count;
            public Func<AxisAlignedBox2d> Extent;
            public Func<T, int> Insert;
            public Func<int, bool> RemoveId;
            public Func<T, bool> RemoveItem;
            public Func<int, bool> Contains;
            public Func<int, T> Get;
            public Action<AxisAlignedBox2d, List<int>> Rectangle;
            public Action<Vector2d, Vector2d, List<int>> Segment;
            public Func<Vector2d, double, int> Nearest;
            public Action<Vector2d, int, List<int>, double> NearestK;
        }

        static Adapter<Vector3d> Wrap(PointQuadtree2d t) => new Adapter<Vector3d> {
            Count = () => t.Count, Extent = () => t.Extent, Insert = t.Insert, RemoveId = t.Remove,
            RemoveItem = t.Remove, Contains = t.ContainsId, Get = t.GetItem,
            Rectangle = t.QueryRectangle, Segment = t.QuerySegment, Nearest = t.FindNearest, NearestK = t.FindNearestK
        };
        static Adapter<Segment3d> Wrap(SegmentQuadtree2d t) => new Adapter<Segment3d> {
            Count = () => t.Count, Extent = () => t.Extent, Insert = t.Insert, RemoveId = t.Remove,
            RemoveItem = t.Remove, Contains = t.ContainsId, Get = t.GetItem,
            Rectangle = t.QueryRectangle, Segment = t.QuerySegment, Nearest = t.FindNearest, NearestK = t.FindNearestK
        };
        static Adapter<Triangle3d> Wrap(TriangleQuadtree2d t) => new Adapter<Triangle3d> {
            Count = () => t.Count, Extent = () => t.Extent, Insert = t.Insert, RemoveId = t.Remove,
            RemoveItem = t.Remove, Contains = t.ContainsId, Get = t.GetItem,
            Rectangle = t.QueryRectangle, Segment = t.QuerySegment, Nearest = t.FindNearest, NearestK = t.FindNearestK
        };

        static void CheckAdapterLifecycle<T>(Adapter<T> t, T item, T distant, T[] invalid, Func<T, AxisAlignedBox2d> bounds)
        {
            Assert.Equal(0, t.Count());
            var result = new List<int> { 5 };
            t.Rectangle(Box(-1, -1, 1, 1), result);
            Assert.Empty(result);
            Assert.Equal(-1, t.Nearest(new Vector2d(0, 0), double.PositiveInfinity));
            Assert.Equal(0, t.Insert(item));
            Assert.Equal(1, t.Insert(item));
            foreach (T bad in invalid) {
                AxisAlignedBox2d extent = t.Extent();
                Assert.ThrowsAny<ArgumentException>(() => t.Insert(bad));
                Assert.Equal(2, t.Count());
                SameBox(extent, t.Extent());
                Assert.True(t.Contains(0));
                Assert.True(t.Contains(1));
            }
            Assert.Equal(2, t.Insert(distant));
            t.Rectangle(bounds(distant), result);
            Assert.Contains(2, result);
            Assert.True(t.RemoveItem(item));
            Assert.False(t.Contains(0));
            Assert.True(t.Contains(1));
            Assert.False(t.RemoveId(0));
            Assert.True(t.RemoveItem(item));
            Assert.False(t.RemoveItem(item));
            Assert.True(t.RemoveId(2));
            Assert.False(t.RemoveId(2));
            Assert.False(t.RemoveId(-1));
            Assert.Equal(0, t.Count());
            Assert.ThrowsAny<ArgumentException>(() => t.Get(0));
            Assert.ThrowsAny<ArgumentException>(() => t.Get(-1));
            AxisAlignedBox2d retained = t.Extent();
            Assert.Equal(3, t.Insert(item));
            SameBox(retained, t.Extent());
            t.Rectangle(retained, result);
            SameIds(new[] { 3 }, result);
            Assert.Equal(item, t.Get(3));
            t.NearestK(bounds(item).Min, 10, result, double.PositiveInfinity);
            Assert.Equal(new[] { 3 }, result.ToArray());
            t.NearestK(bounds(item).Min, 0, result, double.PositiveInfinity);
            Assert.Empty(result);
            foreach (double bad in new[] { -1.0, double.NaN, double.NegativeInfinity }) {
                Assert.ThrowsAny<ArgumentException>(() => t.Nearest(new Vector2d(0, 0), bad));
                Assert.ThrowsAny<ArgumentException>(() => t.NearestK(new Vector2d(0, 0), 1, result, bad));
            }
            Assert.ThrowsAny<ArgumentException>(() => t.NearestK(new Vector2d(0, 0), -1, result, double.PositiveInfinity));
            Assert.ThrowsAny<ArgumentException>(() => t.Rectangle(Box(0, 0, -1, 1), result));
            Assert.ThrowsAny<ArgumentException>(() => t.Rectangle(retained, null));
            Assert.ThrowsAny<ArgumentException>(() => t.Segment(new Vector2d(0, 0), new Vector2d(double.NaN, 0), result));
            Assert.ThrowsAny<ArgumentException>(() => t.Nearest(new Vector2d(double.NaN, 0), double.PositiveInfinity));
        }

        [Fact]
        public void EveryAdapterSupportsEmptyGrowthRemovalAndTransactionalInvalidInsert()
        {
            CheckAdapterLifecycle(Wrap(new PointQuadtree2d()), P(0, 0), P(10000, -10000),
                new[] { P(double.NaN, 0), P(0, double.PositiveInfinity), P(0, 0, double.NaN) }, Bounds);
            var segment = new Segment3d(P(0, 0), P(2, 0, 3));
            CheckAdapterLifecycle(Wrap(new SegmentQuadtree2d()), segment, new Segment3d(P(-1000, 0), P(1000, 0)),
                new[] { new Segment3d(P(double.NaN, 0), P(1, 1)), new Segment3d(P(0, 0), P(1, 1, double.PositiveInfinity)) }, Bounds);
            var triangle = new Triangle3d(P(0, 0), P(2, 0), P(0, 2));
            CheckAdapterLifecycle(Wrap(new TriangleQuadtree2d()), triangle, new Triangle3d(P(-1000, -1000), P(1000, 0), P(0, 1000)),
                new[] { new Triangle3d(P(0, 0), P(1, double.NaN), P(0, 1)), new Triangle3d(P(0, 0), P(1, 0), P(0, 1, double.PositiveInfinity)) }, Bounds);
        }

        static void CheckAdapterQueries<T>(T[] items, Adapter<T> tree, Func<T, AxisAlignedBox2d> bounds,
            Func<Vector2d, T, double> distance)
        {
            var boxes = items.Select(bounds).ToArray();
            CheckBroadPhase(boxes, tree.Rectangle, tree.Segment, 123);
            var random = new Random(439);
            var result = new List<int>();
            for (int i = 0; i < 50; ++i) {
                var p = new Vector2d(random.Next(-20, 20) * 0.5, random.Next(-20, 20) * 0.5);
                double max = i % 2 == 0 ? double.PositiveInfinity : 0.75;
                int[] expected = Ranked(items.Length, id => distance(p, items[id]), 9, max);
                tree.NearestK(p, 9, result, max);
                Assert.Equal(expected, result.ToArray());
                Assert.Equal(expected.Length == 0 ? -1 : expected[0], tree.Nearest(p, max));
            }
        }

        [Fact]
        public void AllAdaptersBroadPhaseAndGeometryNearestHaveBruteForceOracles()
        {
            var random = new Random(533);
            Func<Vector3d> point = () => P(random.Next(-40, 40) * 0.25, random.Next(-40, 40) * 0.25, random.Next(-100, 100));
            var points = Enumerable.Range(0, 150).Select(i => point()).ToArray();
            points[1] = points[0];
            var segments = Enumerable.Range(0, 150).Select(i => new Segment3d(point(), point())).ToArray();
            segments[1] = segments[0];
            segments[2] = new Segment3d(P(0, 0, 1), P(0, 0, 100));
            var triangles = Enumerable.Range(0, 150).Select(i => new Triangle3d(point(), point(), point())).ToArray();
            triangles[1] = triangles[0];
            triangles[2] = new Triangle3d(P(0, 0), P(0, 0), P(0, 0));
            CheckAdapterQueries(points, Wrap(new PointQuadtree2d(points)), Bounds, (p, q) => Distance(p, XY(q)));
            CheckAdapterQueries(segments, Wrap(new SegmentQuadtree2d(segments)), Bounds, (p, s) => SegmentDistance(p, XY(s.P0), XY(s.P1)));
            CheckAdapterQueries(triangles, Wrap(new TriangleQuadtree2d(triangles)), Bounds, TriangleDistance);
        }

        [Fact]
        public void AdaptersOwnValueSnapshotsRatherThanFollowingSourceEdits()
        {
            var points = new[] { P(1, 2, 3) };
            var segments = new[] { new Segment3d(P(1, 2), P(3, 4)) };
            var triangles = new[] { new Triangle3d(P(0, 0), P(2, 0), P(0, 2)) };
            Vector3d p = points[0]; Segment3d s = segments[0]; Triangle3d t = triangles[0];
            var pt = new PointQuadtree2d(points);
            var st = new SegmentQuadtree2d(segments);
            var tt = new TriangleQuadtree2d(triangles);
            points[0] = P(100, 100);
            segments[0] = new Segment3d(P(100, 100), P(200, 200));
            triangles[0] = new Triangle3d(P(100, 100), P(200, 100), P(100, 200));
            Assert.Equal(p, pt.GetItem(0));
            Assert.Equal(s, st.GetItem(0));
            Assert.Equal(t, tt.GetItem(0));
        }

        [Fact]
        public void NearbyPointsUseInclusiveEuclideanXYDistanceNotAxisOrZDistance()
        {
            var points = new[] { P(0, 0), P(0, 0, 1000), P(3, 4), P(4, 4), P(-3, -4), P(6, 0) };
            var tree = new PointQuadtree2d(points);
            var result = new List<int>();
            tree.QueryNearby(new Vector2d(0, 0), 5, result);
            SameIds(new[] { 0, 1, 2, 4 }, result);
            tree.QueryNearby(new Vector2d(0, 0), 0, result);
            SameIds(new[] { 0, 1 }, result);
            tree.QueryNearby(new Vector2d(100, 100), 0.001, result);
            Assert.Empty(result);
            tree.FindNearestK(new Vector2d(0, 0), 20, result, 5);
            Assert.Equal(new[] { 0, 1, 2, 4 }, result.ToArray());
            foreach (double bad in new[] { -0.1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.ThrowsAny<ArgumentException>(() => tree.QueryNearby(new Vector2d(0, 0), bad, result));
            Assert.ThrowsAny<ArgumentException>(() => tree.QueryNearby(new Vector2d(double.NaN, 0), 1, result));
            Assert.ThrowsAny<ArgumentException>(() => tree.QueryNearby(new Vector2d(0, 0), 1, null));
            Assert.ThrowsAny<ArgumentException>(() => new PointQuadtree2d(new[] { P(0, 0, double.NaN) }));
        }

        [Fact]
        public void D3_PointOnSegmentStopsAtARealHitAndUsesCompleteAbsoluteTolerance()
        {
            var segments = new List<Segment3d>();
            for (int i = 0; i < 200; ++i)
                segments.Add(new Segment3d(P(-10, 0.002 + i * 0.00001), P(10, 0.002 + i * 0.00001)));
            segments.Add(new Segment3d(P(-10, 0.0005), P(10, 0.0005)));
            var tree = new SegmentQuadtree2d(segments);
            var result = new List<int>();
            tree.QueryPoint(new Vector2d(0, 0), 0.001, result, false, true);
            SameIds(new[] { 200 }, result);
            tree.QueryPoint(new Vector2d(0, 0), 0.001, result);
            SameIds(new[] { 200 }, result);
            tree.QueryPoint(new Vector2d(0, -5), 6, result);
            SameIds(Enumerable.Range(0, segments.Count), result);
            tree.QueryPoint(new Vector2d(0, -5), 6, result, false, true);
            Assert.Single(result);
            Assert.InRange(result[0], 0, segments.Count - 1);
        }

        [Fact]
        public void PointOnSegmentsHandlesEndpointExclusionAndDegenerateProjections()
        {
            var segments = new[] {
                new Segment3d(P(0, 0), P(10, 0, 10)),
                new Segment3d(P(5, -5, 100), P(5, 5, 200)),
                new Segment3d(P(10, 0), P(10, 10)),
                new Segment3d(P(10, 0, 10), P(0, 0)),
                new Segment3d(P(5, 0), P(5, 0, 100)),
                new Segment3d(P(5, 0), P(5, 0)) };
            var tree = new SegmentQuadtree2d(segments);
            var result = new List<int>();
            tree.QueryPoint(new Vector2d(5, 0), 0, result);
            SameIds(new[] { 0, 1, 3, 4, 5 }, result);
            tree.QueryPoint(new Vector2d(5, 0), 0, result, true);
            SameIds(new[] { 0, 1, 3 }, result);
            tree.QueryPoint(new Vector2d(10, 0), 0.001, result, true);
            Assert.Empty(result);
            tree.QueryPoint(new Vector2d(5, 0.0004), 0.001, result, true);
            SameIds(new[] { 0, 1, 3 }, result);
            tree.QueryPoint(new Vector2d(5, 0.01), 0.001, result, true);
            SameIds(new[] { 1 }, result);
            foreach (double bad in new[] { -1.0, double.NaN, double.PositiveInfinity })
                Assert.ThrowsAny<ArgumentException>(() => tree.QueryPoint(new Vector2d(0, 0), bad, result));
            Assert.ThrowsAny<ArgumentException>(() => tree.QueryPoint(new Vector2d(double.NaN, 0), 0, result));
            Assert.ThrowsAny<ArgumentException>(() => tree.QueryPoint(new Vector2d(0, 0), 0, null));
        }

        [Fact]
        public void SegmentIntersectionsIncludeDuplicatesCollinearityTouchingAndZeroLength()
        {
            var segments = new[] {
                new Segment3d(P(0, 0), P(10, 0)), new Segment3d(P(10, 0), P(0, 0)),
                new Segment3d(P(2, 0), P(7, 0)), new Segment3d(P(10, 0), P(11, 0)),
                new Segment3d(P(5, -1), P(5, 1)), new Segment3d(P(5, 0), P(5, 0)),
                new Segment3d(P(5, 0, -10), P(5, 0, 10)), new Segment3d(P(0, 0.0001), P(10, 0.0001)),
                new Segment3d(P(11, 0), P(12, 0)), new Segment3d(P(11, 1), P(11, 1)) };
            var tree = new SegmentQuadtree2d(segments);
            var result = new List<int>();
            tree.QueryIntersections(new Vector2d(0, 0), new Vector2d(10, 0), result);
            SameIds(Enumerable.Range(0, 7), result);
            tree.QueryIntersections(new Vector2d(10, 0), new Vector2d(0, 0), result);
            SameIds(Enumerable.Range(0, 7), result);
            tree.QueryIntersections(new Vector2d(5, 0), new Vector2d(5, 0), result);
            SameIds(new[] { 0, 1, 2, 4, 5, 6 }, result);
            tree.QueryIntersections(new Vector2d(100, 100), new Vector2d(100, 100), result);
            Assert.Empty(result);
            Assert.ThrowsAny<ArgumentException>(() => tree.QueryIntersections(new Vector2d(double.NaN, 0), new Vector2d(0, 0), result));
            Assert.ThrowsAny<ArgumentException>(() => tree.QueryIntersections(new Vector2d(0, 0), new Vector2d(0, double.PositiveInfinity), result));
            Assert.ThrowsAny<ArgumentException>(() => new SegmentQuadtree2d(new[] { new Segment3d(P(double.NaN, 0), P(0, 0)) }));
        }

        static void CheckHeights(TriangleQuadtree2d tree, Vector2d point, double tolerance,
            int[] ids, Func<int, double> expectedHeight)
        {
            var result = new List<QuadtreeHeightHit2d>();
            tree.QueryHeights(point, result, tolerance);
            SameIds(ids, result.Select(h => h.Id));
            foreach (var hit in result) {
                Assert.False(double.IsNaN(hit.Height) || double.IsInfinity(hit.Height));
                Assert.InRange(Math.Abs(hit.Height - expectedHeight(hit.Id)), 0, 1e-9 * Math.Max(1, Math.Abs(expectedHeight(hit.Id))));
            }
            double height;
            Assert.Equal(ids.Length > 0, tree.TryGetHeight(point, out height, tolerance));
            if (ids.Length > 0) Assert.Contains(result, h => Math.Abs(h.Height - height) < 1e-9 * Math.Max(1, Math.Abs(height)));
            tree.QueryHeights(point, result, tolerance, true);
            Assert.Equal(ids.Length == 0 ? 0 : 1, result.Count);
            if (result.Count != 0) Assert.Contains(result[0].Id, ids);
        }

        [Fact]
        public void HeightsAreToleranceCompleteAndExtrapolateRatherThanClamp()
        {
            var triangle = new Triangle3d(P(0, 0, 0), P(100, 0, 100), P(0, 100, 200));
            var tree = new TriangleQuadtree2d(new[] { triangle });
            CheckHeights(tree, new Vector2d(-2, 10), 2, new[] { 0 }, id => 18);
            CheckHeights(tree, new Vector2d(-2, 10), 1.99, new int[0], id => 0);
            CheckHeights(tree, new Vector2d(-3, -4), 5, new[] { 0 }, id => -11);
            CheckHeights(tree, new Vector2d(-4, -4), 5, new int[0], id => 0);
            CheckHeights(tree, new Vector2d(25, 25), 0, new[] { 0 }, id => 75);
            CheckHeights(tree, new Vector2d(-0.005, 10), 0.01, new[] { 0 }, id => 19.995);
        }

        [Fact]
        public void ExactHeightsTakePriorityAcrossWholeIndexRegardlessOfInsertionOrder()
        {
            var tolerance = new Triangle3d(P(0.001, 0, 100), P(1, 0, 100), P(0.001, 1, 100));
            var exact = new Triangle3d(P(-1, -1, 7), P(1, -1, 7), P(0, 1, 7));
            var overlapping = new Triangle3d(P(-1, -1, 9), P(0, 1, 9), P(1, -1, 9));
            var degenerate = new Triangle3d(P(-1, 0, 50), P(0, 0, 50), P(1, 0, 50));
            foreach (var triangles in new[] { new[] { tolerance, exact, overlapping, degenerate }, new[] { overlapping, tolerance, degenerate, exact } }) {
                var tree = new TriangleQuadtree2d(triangles);
                int[] ids = Enumerable.Range(0, triangles.Length).Where(i => triangles[i].V0.z == 7 || triangles[i].V0.z == 9).ToArray();
                CheckHeights(tree, new Vector2d(0, 0), 0.01, ids, i => triangles[i].V0.z);
            }
            var justTolerance = new TriangleQuadtree2d(new[] { tolerance, new Triangle3d(P(0.002, 0, 200), P(2, 0, 200), P(0.002, 2, 200)) });
            CheckHeights(justTolerance, new Vector2d(0, 0), 0.01, new[] { 0, 1 }, i => i == 0 ? 100 : 200);
        }

        [Fact]
        public void HeightContainmentUsesExactOrientationNotBarycentricSlopOrRoundedDeterminant()
        {
            var normal = new Triangle3d(P(0, 0, 3), P(1, 0, 3), P(0, 1, 3));
            var tree = new TriangleQuadtree2d(new[] { normal });
            CheckHeights(tree, new Vector2d(-Math.Pow(2, -40), 0.25), 0, new int[0], id => 0);
            CheckHeights(tree, new Vector2d(0.5, 0.5), 0, new[] { 0 }, id => 3);
            CheckHeights(tree, new Vector2d(0, 0), 0, new[] { 0 }, id => 3);
            double n = Math.Pow(2, 27);
            // The exact determinant is one; the two rounded double products cancel to zero.
            Assert.Equal(1m, (decimal)n * (decimal)n - ((decimal)n - 1) * ((decimal)n + 1));
            foreach (var triangle in new[] {
                new Triangle3d(P(0, 0, 11), P(n, n - 1, 11), P(n + 1, n, 11)),
                new Triangle3d(P(0, 0, 11), P(n + 1, n, 11), P(n, n - 1, 11)) }) {
                var thin = new TriangleQuadtree2d(new[] { triangle });
                CheckHeights(thin, new Vector2d(0, 0), 0, new[] { 0 }, id => 11);
                CheckHeights(thin, new Vector2d(n, n - 1), 0, new[] { 0 }, id => 11);
                CheckHeights(thin, new Vector2d(-1, -1), 0, new int[0], id => 0);
            }
        }

        [Fact]
        public void DegenerateTrianglesNeverYieldHeightsAndInvalidHeightInputsReject()
        {
            var triangles = new[] {
                new Triangle3d(P(0, 0), P(1, 0, 10), P(2, 0, 20)),
                new Triangle3d(P(0, 0), P(0, 0, 10), P(0, 0, 20)),
                new Triangle3d(P(0, 0), P(1, 1), P(2, 2)) };
            var tree = new TriangleQuadtree2d(triangles);
            CheckHeights(tree, new Vector2d(0, 0), 100, new int[0], i => 0);
            var result = new List<QuadtreeHeightHit2d>();
            foreach (double bad in new[] { -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                Assert.ThrowsAny<ArgumentException>(() => tree.QueryHeights(new Vector2d(0, 0), result, bad));
                Assert.ThrowsAny<ArgumentException>(() => { double h; tree.TryGetHeight(new Vector2d(0, 0), out h, bad); });
            }
            Assert.ThrowsAny<ArgumentException>(() => tree.QueryHeights(new Vector2d(double.NaN, 0), result));
            Assert.ThrowsAny<ArgumentException>(() => tree.QueryHeights(new Vector2d(0, 0), null));
            Assert.ThrowsAny<ArgumentException>(() => new TriangleQuadtree2d(new[] { new Triangle3d(P(0, 0), P(1, 0), P(0, 1, double.NaN)) }));
        }

        [Fact]
        public void SimilarityProjectionUsesTransformedXYOraclesAndAffineHeightPlanes()
        {
            var original = new[] { P(-4, -4, -6), P(4, -4, 2), P(0, 4, 6), P(0, 0, 2), P(3, 2, 7) };
            var baseline = new TriangleQuadtree2d(new[] { new Triangle3d(original[0], original[1], original[2]) });
            double baselineHeight;
            Assert.True(baseline.TryGetHeight(new Vector2d(0, 0), out baselineHeight, 0));
            foreach (Similarity s in Similarity.All()) {
                Vector3d[] points = original.Select(s.Apply).ToArray();
                var pointTree = new PointQuadtree2d(points);
                Vector2d query = XY(s.Apply(P(0.5, 0.25)));
                double tolerance = s.ApplyLength(4);
                var result = new List<int>();
                pointTree.QueryNearby(query, tolerance, result);
                SameIds(Enumerable.Range(0, points.Length).Where(i => Distance(query, XY(points[i])) <= tolerance), result);
                pointTree.FindNearestK(query, points.Length, result);
                Assert.Equal(Ranked(points.Length, i => Distance(query, XY(points[i])), points.Length, double.PositiveInfinity), result.ToArray());
                var triangle = new Triangle3d(points[0], points[1], points[2]);
                var triangleTree = new TriangleQuadtree2d(new[] { triangle });
                Vector3d projected = s.Apply(P(0, 0, 2));
                double height;
                Assert.True(triangleTree.TryGetHeight(XY(projected), out height, 0));
                if (s.IsExact) Assert.Equal(projected.z, height);
                else Assert.InRange(Math.Abs(projected.z - height), 0, 1e-7 * Math.Max(1, s.Scale));
                var segment = new Segment3d(points[0], points[1]);
                var segmentTree = new SegmentQuadtree2d(new[] { segment });
                segmentTree.QueryPoint(query, tolerance, result);
                SameIds(SegmentDistance(query, XY(segment.P0), XY(segment.P1)) <= tolerance ? new[] { 0 } : new int[0], result);
                var boxes = points.Select(Bounds).ToArray();
                var core = new LooseQuadtree2d(boxes);
                var rectangle = new AxisAlignedBox2d(query, tolerance);
                core.QueryRectangle(rectangle, result);
                SameIds(Enumerable.Range(0, boxes.Length).Where(i => Overlaps(boxes[i], rectangle)), result);
            }
        }

        [Fact]
        public void AdapterTraversalAndConcurrentQueriesAreIndependent()
        {
            var points = Enumerable.Range(0, 1000).Select(i => P(i % 25, i / 25)).ToArray();
            var segments = points.Select(p => new Segment3d(p, p + P(0.5, 0))).ToArray();
            var triangles = points.Select(p => new Triangle3d(p, p + P(0.5, 0), p + P(0, 0.5))).ToArray();
            var pt = new PointQuadtree2d(points, true);
            var st = new SegmentQuadtree2d(segments, true);
            var tt = new TriangleQuadtree2d(triangles, true);
            var ps = new PointQuadtree2d(points, false);
            var ss = new SegmentQuadtree2d(segments, false);
            var ts = new TriangleQuadtree2d(triangles, false);
            var visitor = new AdapterVisitor { Point = pt, Segment = st, Triangle = tt, Seen = new HashSet<int>(), Nested = new List<int>() };
            Assert.False(pt.Traverse(ref visitor));
            SameIds(Enumerable.Range(0, points.Length), visitor.Seen);
            visitor.Seen.Clear();
            Assert.False(st.Traverse(ref visitor));
            SameIds(Enumerable.Range(0, points.Length), visitor.Seen);
            visitor.Seen.Clear();
            Assert.False(tt.Traverse(ref visitor));
            SameIds(Enumerable.Range(0, points.Length), visitor.Seen);
            Parallel.For(0, 64, i => {
                var q = XY(points[i]);
                var actual = new List<int>(); var expected = new List<int>();
                var heights = new List<QuadtreeHeightHit2d>(); var reference = new List<QuadtreeHeightHit2d>();
                for (int repeat = 0; repeat < 3; ++repeat) {
                    pt.QueryNearby(q, 1, actual); ps.QueryNearby(q, 1, expected); SameIds(expected, actual);
                    st.QueryPoint(q, 1, actual); ss.QueryPoint(q, 1, expected); SameIds(expected, actual);
                    tt.QueryHeights(q, heights); ts.QueryHeights(q, reference);
                    Assert.Equal(reference.OrderBy(h => h.Id).Select(h => h.Height).ToArray(), heights.OrderBy(h => h.Id).Select(h => h.Height).ToArray());
                    SameIds(reference.Select(h => h.Id), heights.Select(h => h.Id));
                    pt.FindNearestK(q, 5, actual); ps.FindNearestK(q, 5, expected); Assert.Equal(expected.ToArray(), actual.ToArray());
                    st.FindNearestK(q, 5, actual); ss.FindNearestK(q, 5, expected); Assert.Equal(expected.ToArray(), actual.ToArray());
                    tt.FindNearestK(q, 5, actual); ts.FindNearestK(q, 5, expected); Assert.Equal(expected.ToArray(), actual.ToArray());
                }
            });
        }

        struct AdapterVisitor : IQuadtreeVisitor2d
        {
            public PointQuadtree2d Point;
            public SegmentQuadtree2d Segment;
            public TriangleQuadtree2d Triangle;
            public HashSet<int> Seen;
            public List<int> Nested;
            public bool TestRegion(AxisAlignedBox2d region) => true;
            public bool Visit(int id)
            {
                Assert.True(Seen.Add(id));
                Point.QueryNearby(XY(Point.GetItem(id)), 0, Nested);
                Assert.Contains(id, Nested);
                Segment.QueryPoint(XY(Segment.GetItem(id).P0), 0, Nested);
                Assert.Contains(id, Nested);
                Triangle.QueryRectangle(Bounds(Triangle.GetItem(id)), Nested);
                Assert.Contains(id, Nested);
                return false;
            }
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void D5_WarmedHotQueriesAllocateNoManagedMemory()
        {
            foreach (int count in new[] { 32, 40000 }) {
                var points = Enumerable.Range(0, count).Select(i => P(i % 200, i / 200)).ToArray();
                var boxes = points.Select(Bounds).ToArray();
                var triangles = points.Select(p => new Triangle3d(p, p + P(0.5, 0), p + P(0, 0.5))).ToArray();
                var segments = points.Select(p => new Segment3d(p, p + P(0.5, 0))).ToArray();
                var core = new LooseQuadtree2d(boxes);
                var pt = new PointQuadtree2d(points);
                var st = new SegmentQuadtree2d(segments);
                var tt = new TriangleQuadtree2d(triangles);
                var ids = new List<int>(count);
                var hits = new List<QuadtreeHeightHit2d>(count);
                var q = new Vector2d(0.125, 0.125);
                var r = Box(0, 0, 1, 1);
                Action hot = () => {
                    core.QueryRectangle(r, ids); core.QuerySegment(q, r.Max, ids);
                    pt.QueryRectangle(r, ids); pt.QuerySegment(q, r.Max, ids); pt.QueryNearby(q, 0.5, ids);
                    st.QueryRectangle(r, ids); st.QuerySegment(q, r.Max, ids);
                    tt.QueryRectangle(r, ids); tt.QuerySegment(q, r.Max, ids);
                    tt.QueryHeights(q, hits); double h; tt.TryGetHeight(q, out h);
                };
                for (int warm = 0; warm < 200; ++warm) hot();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < 100; ++iteration) hot();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(0L, allocated);
            }
        }
#endif
    }
}
