using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g3;
using Xunit;

namespace geometry3Sharp.Tests.Spatial
{
    public class LooseQuadtree2dTests
    {
        static AxisAlignedBox2d Box(double x, double y, double width = 0, double height = 0)
        {
            return new AxisAlignedBox2d(x, y, x + width, y + height);
        }

        static bool Overlap(AxisAlignedBox2d a, AxisAlignedBox2d b)
        {
            return !(a.Max.x < b.Min.x || b.Max.x < a.Min.x || a.Max.y < b.Min.y || b.Max.y < a.Min.y);
        }

        static int[] RectangleOracle(IReadOnlyList<AxisAlignedBox2d> boxes, AxisAlignedBox2d query)
        {
            return Enumerable.Range(0, boxes.Count).Where(i => Overlap(boxes[i], query)).ToArray();
        }

        static double Distance(Vector2d point, AxisAlignedBox2d box)
        {
            double x = point.x < box.Min.x ? box.Min.x - point.x : point.x > box.Max.x ? point.x - box.Max.x : 0;
            double y = point.y < box.Min.y ? box.Min.y - point.y : point.y > box.Max.y ? point.y - box.Max.y : 0;
            return Math.Sqrt(x * x + y * y);
        }

        static bool SegmentOracle(Vector2d a, Vector2d b, AxisAlignedBox2d box)
        {
            box.Expand(0.0002);
            if (Inside(a, box) || Inside(b, box)) return true;
            Vector2d c = new Vector2d(box.Min.x, box.Max.y), d = new Vector2d(box.Max.x, box.Min.y);
            return Crosses(a, b, box.Min, c) || Crosses(a, b, c, box.Max)
                || Crosses(a, b, box.Max, d) || Crosses(a, b, d, box.Min);
        }

        static bool Inside(Vector2d p, AxisAlignedBox2d b)
        {
            return p.x >= b.Min.x && p.x <= b.Max.x && p.y >= b.Min.y && p.y <= b.Max.y;
        }

        static bool Crosses(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            if (!Overlap(new AxisAlignedBox2d(a, b), new AxisAlignedBox2d(c, d))) return false;
            double x = Cross(b - a, c - a), y = Cross(b - a, d - a);
            double u = Cross(d - c, a - c), v = Cross(d - c, b - c);
            return ((x <= 0 && y >= 0) || (x >= 0 && y <= 0)) && ((u <= 0 && v >= 0) || (u >= 0 && v <= 0));
        }

        static double Cross(Vector2d a, Vector2d b) { return a.x * b.y - a.y * b.x; }

        static AxisAlignedBox2d[] Grid(int side, double offset = 0)
        {
            return Enumerable.Range(0, side * side).Select(i => Box(offset + i % side, offset + i / side, 0.5, 0.5)).ToArray();
        }

        static void SetEqual(IEnumerable<int> expected, List<int> actual)
        {
            Assert.Equal(expected.OrderBy(i => i).ToArray(), actual.OrderBy(i => i).ToArray());
        }

        [Fact]
        public void EmptyGrowthSnapshotsAndImmutableSettings()
        {
            var tree = new LooseQuadtree2d();
            var results = new List<int> { 123 };
            tree.QueryRectangle(Box(0, 0), results);
            Assert.Empty(results);
            tree.QuerySegment(Vector2d.Zero, Vector2d.One, results);
            Assert.Empty(results);
            Assert.Equal(-1, tree.FindNearest(Vector2d.Zero));
            tree.FindNearestK(Vector2d.Zero, 3, results);
            Assert.Empty(results);
            Assert.Equal(AxisAlignedBox2d.Empty, tree.Extent);
            Assert.Equal(2, tree.Looseness);
            Assert.Equal(16, tree.BucketCapacity);
            Assert.Equal(32, tree.MaxDepth);
            var box = Box(4, 5);
            Assert.Equal(0, tree.Insert(box));
            box.Min.x = 100;
            Assert.Equal(Box(4, 5), tree.GetBounds(0));
            Assert.Equal(1, tree.Insert(Box(-100, -200, 1, 2)));
            Assert.Equal(new AxisAlignedBox2d(-100, -200, 4, 5), tree.Extent);
            tree.QueryRectangle(tree.Extent, results);
            SetEqual(new[] { 0, 1 }, results);
            Assert.True(tree.Remove(0));
            Assert.True(tree.Remove(1));
            Assert.Equal(new AxisAlignedBox2d(-100, -200, 4, 5), tree.Extent);
            Assert.Equal(2, tree.Insert(Box(0, 0)));
            Assert.Equal(2, tree.FindNearest(Vector2d.Zero));
            var source = new[] { Box(1, 1) };
            var snapshot = new LooseQuadtree2d(source, false);
            source[0] = Box(9, 9);
            Assert.Equal(Box(1, 1), snapshot.GetBounds(0));
            Assert.False(snapshot.ParallelBuild);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2539000)]
        [InlineData(-5600000)]
        public void RectangleClosedGridLinesAndThinCrossingBoxes(double offset)
        {
            var boxes = Grid(32, offset).ToList();
            boxes.Add(Box(offset - 20, offset + 16, 100, 0));
            boxes.Add(Box(offset + 16, offset - 20, 0, 100));
            var tree = new LooseQuadtree2d(boxes);
            var results = new List<int>();
            for (int x = -1; x <= 65; ++x) {
                var query = Box(offset + x * 0.5, offset + 16, 0, 0.5);
                tree.QueryRectangle(query, results);
                SetEqual(RectangleOracle(boxes, query), results);
            }
            foreach (var query in new[] { Box(offset - 20, offset + 16), Box(offset + 80, offset + 16), Box(offset + 16, offset + 80) }) {
                tree.QueryRectangle(query, results);
                SetEqual(RectangleOracle(boxes, query), results);
            }
        }

        [Fact]
        public void RandomizedQueriesAndNearestMatchIndependentOracles()
        {
            var random = new Random(1203);
            var boxes = Enumerable.Range(0, 2500).Select(i => Box(random.NextDouble() * 200 - 100,
                random.NextDouble() * 80 - 40, i % 3 == 0 ? 0 : random.NextDouble() * 20,
                i % 7 == 0 ? 0 : random.NextDouble() * 5)).ToArray();
            var tree = new LooseQuadtree2d(boxes);
            var results = new List<int>();
            for (int iteration = 0; iteration < 100; ++iteration) {
                var a = new Vector2d(random.NextDouble() * 240 - 120, random.NextDouble() * 120 - 60);
                var b = new Vector2d(random.NextDouble() * 240 - 120, random.NextDouble() * 120 - 60);
                var query = new AxisAlignedBox2d(a, b);
                tree.QueryRectangle(query, results);
                SetEqual(RectangleOracle(boxes, query), results);
                int[] segment = Enumerable.Range(0, boxes.Length).Where(i => SegmentOracle(a, b, boxes[i])).ToArray();
                tree.QuerySegment(a, b, results);
                SetEqual(segment, results);
                tree.QuerySegment(b, a, results);
                SetEqual(segment, results);
                double limit = iteration % 2 == 0 ? 15 : double.PositiveInfinity;
                int[] nearest = Enumerable.Range(0, boxes.Length).Where(i => Distance(a, boxes[i]) <= limit)
                    .OrderBy(i => Distance(a, boxes[i])).ThenBy(i => i).ToArray();
                Assert.Equal(nearest.Length == 0 ? -1 : nearest[0], tree.FindNearest(a, limit));
                tree.FindNearestK(a, 17, results, limit);
                Assert.Equal(nearest.Take(17).ToArray(), results.ToArray());
            }
        }

        [Fact]
        public void SegmentMarginDegeneracyAndDirectionAreInclusive()
        {
            var boxes = new[] { Box(0, 0), Box(0, 0.0002), Box(0, 0.00021), Box(1, 1), Box(1, -1), Box(0, 0) };
            var tree = new LooseQuadtree2d(boxes);
            var results = new List<int>();
            foreach (var endpoints in new[] {
                new[] { new Vector2d(-2, 0), new Vector2d(2, 0) },
                new[] { new Vector2d(2, 0), new Vector2d(-2, 0) },
                new[] { Vector2d.Zero, Vector2d.Zero } }) {
                tree.QuerySegment(endpoints[0], endpoints[1], results);
                SetEqual(new[] { 0, 1, 5 }, results);
            }
            tree.QuerySegment(new Vector2d(-1, -1), new Vector2d(2, 2), results);
            SetEqual(new[] { 0, 1, 2, 3, 5 }, results);
            tree.QuerySegment(new Vector2d(0, -2), new Vector2d(0, 2), results);
            SetEqual(new[] { 0, 1, 2, 5 }, results);
            tree.QuerySegment(new Vector2d(10, 20), new Vector2d(30, 40), results);
            Assert.Empty(results);
        }

        [Fact]
        public void FailedInputsLeaveStateAndNextIdUnchanged()
        {
            var tree = new LooseQuadtree2d(new[] { Box(0, 0, 10, 10) });
            var invalid = new[] {
                new AxisAlignedBox2d(2, 0, 1, 1), new AxisAlignedBox2d(0, 2, 1, 1),
                new AxisAlignedBox2d(double.NaN, 0, 1, 1), new AxisAlignedBox2d(0, 0, double.PositiveInfinity, 1),
                new AxisAlignedBox2d(double.NegativeInfinity, 0, 1, 1) };
            var results = new List<int> { 91 };
            foreach (var box in invalid) {
                Assert.Throws<ArgumentException>(() => tree.Insert(box));
                Assert.Throws<ArgumentException>(() => tree.Remove(box));
                Assert.Throws<ArgumentException>(() => tree.QueryRectangle(box, results));
                Assert.Throws<ArgumentException>(() => new LooseQuadtree2d(new[] { Box(0, 0), box }));
                Assert.Equal(new[] { 91 }, results);
                Assert.Equal(1, tree.Count);
                Assert.Equal(Box(0, 0, 10, 10), tree.Extent);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => tree.GetBounds(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => tree.GetBounds(1));
            Assert.Throws<ArgumentNullException>(() => new LooseQuadtree2d(null));
            Assert.Throws<ArgumentNullException>(() => tree.QueryRectangle(Box(0, 0), null));
            Assert.Throws<ArgumentNullException>(() => tree.QuerySegment(Vector2d.Zero, Vector2d.Zero, null));
            Assert.Throws<ArgumentException>(() => tree.QuerySegment(new Vector2d(double.NaN, 0), Vector2d.Zero, results));
            Assert.Throws<ArgumentException>(() => tree.FindNearest(new Vector2d(double.PositiveInfinity, 0)));
            foreach (double maximum in new[] { -1.0, double.NaN, double.NegativeInfinity }) {
                Assert.Throws<ArgumentOutOfRangeException>(() => tree.FindNearest(Vector2d.Zero, maximum));
                Assert.Throws<ArgumentOutOfRangeException>(() => tree.FindNearestK(Vector2d.Zero, 2, results, maximum));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => tree.FindNearestK(Vector2d.Zero, -1, results));
            Assert.Equal(new[] { 91 }, results);
            Assert.Equal(1, tree.Insert(Box(100, 200)));
            Assert.Equal(2, tree.Count);
            tree.FindNearestK(Vector2d.Zero, 0, results);
            Assert.Empty(results);
        }

        [Fact]
        public void DuplicateRemovalUsesFirstLiveIdAndNeverRenumbers()
        {
            var duplicate = Box(-5, -5, 10, 10);
            var boxes = Grid(30).Concat(Enumerable.Repeat(duplicate, 40)).ToArray();
            var tree = new LooseQuadtree2d(boxes);
            Assert.True(tree.Remove(duplicate));
            Assert.False(tree.ContainsId(900));
            Assert.True(tree.ContainsId(901));
            Assert.False(tree.Remove(900));
            Assert.False(tree.Remove(-1));
            Assert.False(tree.Remove(5000));
            Assert.Throws<ArgumentOutOfRangeException>(() => tree.GetBounds(900));
            Assert.True(tree.Remove(duplicate));
            Assert.False(tree.ContainsId(901));
            var extent = tree.Extent;
            for (int i = 0; i < boxes.Length; ++i) Assert.Equal(i != 900 && i != 901, tree.Remove(i));
            Assert.Equal(0, tree.Count);
            Assert.Equal(extent, tree.Extent);
            Assert.False(tree.Remove(duplicate));
            Assert.Equal(boxes.Length, tree.Insert(duplicate));
            Assert.Equal(boxes.Length, tree.FindNearest(Vector2d.Zero));
        }

        [Fact]
        public void IncrementalSplitsGrowthAndRemovalsMatchLiveSet()
        {
            var tree = new LooseQuadtree2d(new[] { Box(-100, -100, 200, 200) });
            var live = new Dictionary<int, AxisAlignedBox2d> { { 0, tree.GetBounds(0) } };
            var random = new Random(342);
            var results = new List<int>();
            for (int i = 0; i < 1200; ++i) {
                var box = Box(random.Next(-300, 300), random.Next(-300, 300), random.Next(5), random.Next(5));
                int id = tree.Insert(box);
                Assert.Equal(i + 1, id);
                live.Add(id, box);
                if (i % 3 == 0) {
                    int remove = i / 3;
                    Assert.Equal(live.Remove(remove), tree.Remove(remove));
                }
                if (i % 50 != 0) continue;
                var query = Box(-60, -60, 180, 180);
                tree.QueryRectangle(query, results);
                SetEqual(live.Where(kv => Overlap(kv.Value, query)).Select(kv => kv.Key), results);
                Assert.Equal(live.Count, tree.Count);
            }
            tree.QueryRectangle(tree.Extent, results);
            SetEqual(live.Keys, results);
        }

        [Fact]
        public void IdenticalTinyCollinearAndElongatedExtentsWork()
        {
            foreach (var boxes in new[] {
                Enumerable.Repeat(Box(2, 3), 500).ToArray(),
                Enumerable.Repeat(Box(2, 3, 4, 5), 500).ToArray(),
                Enumerable.Range(0, 500).Select(i => Box(i * 1e-200, 0)).ToArray(),
                Enumerable.Range(0, 500).Select(i => Box(0, i * 1e-6)).ToArray(),
                Enumerable.Range(0, 500).Select(i => Box(i * 40, 0, 5, 0.00001)).ToArray() }) {
                var tree = new LooseQuadtree2d(boxes);
                var results = new List<int>();
                tree.QueryRectangle(tree.Extent, results);
                SetEqual(Enumerable.Range(0, boxes.Length), results);
                foreach (int i in new[] { 0, 117, 499 }) {
                    tree.QueryRectangle(boxes[i], results);
                    SetEqual(RectangleOracle(boxes, boxes[i]), results);
                }
            }
        }

        [Fact]
        public void ExtremeFiniteCoordinatesDoNotOverflowSpatialPruning()
        {
            var boxes = new List<AxisAlignedBox2d> { Box(-double.MaxValue, 0), Box(double.MaxValue, 0), Box(0, 0) };
            for (int i = 1; i <= 80; ++i) boxes.Add(Box(i * 1e300, i * 1e300));
            var tree = new LooseQuadtree2d(boxes);
            var results = new List<int>();
            for (int i = 0; i < boxes.Count; ++i) {
                tree.QueryRectangle(boxes[i], results);
                Assert.Equal(new[] { i }, results);
                Assert.Equal(i, tree.FindNearest(boxes[i].Min, 0));
            }
            tree.QuerySegment(new Vector2d(-double.MaxValue, 0), new Vector2d(double.MaxValue, 0), results);
            SetEqual(new[] { 0, 1, 2 }, results);
            var metric = new LooseQuadtree2d(new[] { Box(1e300, 1e300), Box(1.1e300, 0), Box(1e-300, 0), Box(2e-300, 0) });
            Assert.Equal(2, metric.FindNearest(Vector2d.Zero));
            metric.FindNearestK(Vector2d.Zero, 4, results);
            Assert.Equal(new[] { 2, 3, 1, 0 }, results);
            metric.FindNearestK(Vector2d.Zero, 4, results, 1e-300);
            Assert.Equal(new[] { 2 }, results);
        }

        struct ArrayDistance : IQuadtreeDistance2d
        {
            public double[] Values;
            public int Calls;
            public double Distance(int id) { ++Calls; return Values[id]; }
        }

        [Fact]
        public void GenericNearestUsesGeometryDistanceLimitsAndIdTies()
        {
            var tree = new LooseQuadtree2d(Enumerable.Repeat(Box(-10, -10, 20, 20), 100).ToArray());
            var distance = new ArrayDistance { Values = Enumerable.Range(0, 100).Select(i => (double)(i % 11)).ToArray() };
            var results = new List<int>();
            int[] ordered = Enumerable.Range(0, 100).OrderBy(i => distance.Values[i]).ThenBy(i => i).ToArray();
            Assert.Equal(ordered[0], tree.FindNearest(Vector2d.Zero, ref distance));
            Assert.True(distance.Calls > 0);
            tree.FindNearestK(Vector2d.Zero, 45, results, ref distance);
            Assert.Equal(ordered.Take(45), results);
            tree.FindNearestK(Vector2d.Zero, 200, results, ref distance, 3);
            Assert.Equal(ordered.Where(i => distance.Values[i] <= 3), results);
            tree.Remove(0);
            Assert.Equal(11, tree.FindNearest(Vector2d.Zero, ref distance, 0));
            distance.Values = Enumerable.Repeat(double.NaN, 100).ToArray();
            Assert.Throws<ArgumentException>(() => tree.FindNearest(Vector2d.Zero, ref distance));
        }

        [Fact]
        public void NearestTiesAndExactLimitDoNotDependOnTraversalOrder()
        {
            var boxes = Grid(40).ToList();
            boxes.Add(Box(-1, 0));
            boxes.Add(Box(0, -1));
            boxes.Add(Box(0, 1));
            boxes.Add(Box(1, 0));
            var tree = new LooseQuadtree2d(boxes);
            tree.Remove(0);
            var results = new List<int>();
            int[] order = Enumerable.Range(1, boxes.Count - 1).OrderBy(i => Distance(Vector2d.Zero, boxes[i])).ThenBy(i => i).ToArray();
            tree.FindNearestK(Vector2d.Zero, boxes.Count + 100, results);
            Assert.Equal(order, results);
            tree.FindNearestK(Vector2d.Zero, 10, results, 1);
            Assert.Equal(order.Where(i => Distance(Vector2d.Zero, boxes[i]) <= 1).Take(10), results);
            Assert.Equal(-1, tree.FindNearest(Vector2d.Zero, 0));
            Assert.Equal(order[0], tree.FindNearest(Vector2d.Zero, 1));
        }

        struct CountingVisitor : IQuadtreeVisitor2d
        {
            public AxisAlignedBox2d Query;
            public int Regions, Visits;
            public bool Stop;
            public bool TestRegion(AxisAlignedBox2d region) { ++Regions; return Overlap(region, Query); }
            public bool Visit(int id) { ++Visits; return Stop; }
        }

        struct NestedVisitor : IQuadtreeVisitor2d
        {
            public LooseQuadtree2d Tree;
            public List<int> Inner;
            public int Visits;
            public bool TestRegion(AxisAlignedBox2d region) { return true; }
            public bool Visit(int id)
            {
                ++Visits;
                Tree.QueryRectangle(Tree.GetBounds(id), Inner);
                Assert.Contains(id, Inner);
                Assert.Equal(id, Tree.FindNearest(Tree.GetBounds(id).Min, 0));
                return false;
            }
        }

        [Fact]
        public void TraversalPrunesStopsAndSupportsNestedConcurrentQueries()
        {
            var tree = new LooseQuadtree2d(Grid(100));
            var visitor = new CountingVisitor { Query = Box(10, 10) };
            Assert.False(tree.Traverse(ref visitor));
            Assert.InRange(visitor.Visits, 1, 999);
            Assert.True(visitor.Regions > 1);
            visitor = new CountingVisitor { Query = tree.Extent, Stop = true };
            Assert.True(tree.Traverse(ref visitor));
            Assert.Equal(1, visitor.Visits);
            visitor = new CountingVisitor { Query = Box(-100, -100) };
            Assert.False(tree.Traverse(ref visitor));
            Assert.Equal(0, visitor.Visits);
            var nested = new NestedVisitor { Tree = tree, Inner = new List<int>() };
            Assert.False(tree.Traverse(ref nested));
            Assert.Equal(tree.Count, nested.Visits);
            Parallel.For(0, 50, iteration => {
                var results = new List<int>();
                int id = iteration * 137;
                tree.QueryRectangle(tree.GetBounds(id), results);
                Assert.Equal(new[] { id }, results);
                tree.QuerySegment(tree.GetBounds(id).Min, tree.GetBounds(id).Min, results);
                Assert.Equal(new[] { id }, results);
                tree.FindNearestK(tree.GetBounds(id).Min, 1, results);
                Assert.Equal(new[] { id }, results);
                var local = new CountingVisitor { Query = tree.GetBounds(id), Stop = true };
                Assert.True(tree.Traverse(ref local));
                Assert.Equal(1, local.Visits);
            });
        }

        [Fact]
        public void LargeBulkBuildSerialParallelAndRepeatedResultsAgree()
        {
            var boxes = Grid(190);
            var serial = new LooseQuadtree2d(boxes, false);
            var parallel = new LooseQuadtree2d(boxes, true);
            var repeated = new LooseQuadtree2d(boxes, true);
            var first = new List<int>();
            var second = new List<int>();
            var third = new List<int>();
            for (int i = 0; i < boxes.Length; i += 127) {
                Assert.True(serial.Remove(i));
                Assert.True(parallel.Remove(i));
                Assert.True(repeated.Remove(i));
                Assert.False(parallel.Remove(i));
            }
            for (int i = 0; i < 30; ++i) {
                var query = Box(i * 6, i * 4, 13, 18);
                serial.QueryRectangle(query, first);
                parallel.QueryRectangle(query, second);
                repeated.QueryRectangle(query, third);
                SetEqual(first, second);
                SetEqual(first, third);
                serial.QuerySegment(query.Min, query.Max, first);
                parallel.QuerySegment(query.Min, query.Max, second);
                SetEqual(first, second);
                serial.FindNearestK(query.Min, 25, first);
                parallel.FindNearestK(query.Min, 25, second);
                repeated.FindNearestK(query.Min, 25, third);
                Assert.Equal(first, second);
                Assert.Equal(first, third);
            }
            Assert.Equal(boxes.Length, parallel.Insert(Box(1000, 1000)));
            parallel.QueryRectangle(parallel.Extent, first);
            Assert.Equal(parallel.Count, first.Count);
            Assert.Equal(first.Count, first.Distinct().Count());
        }

        [Fact]
        public void SimilarityAllProjectedQueriesMatchOracleAndExactScalesKeepIds()
        {
            var points = Enumerable.Range(0, 50).Select(i => new Vector3d(i * 3 - 70, (i * 19) % 31 - 15, 0)).ToArray();
            int[] baseline = null;
            foreach (Similarity similarity in Similarity.All()) {
                var boxes = points.Select(p => { var q = similarity.Apply(p); return Box(q.x, q.y); }).ToArray();
                var tree = new LooseQuadtree2d(boxes);
                var results = new List<int>();
                for (int i = 0; i < points.Length; ++i) {
                    tree.QueryRectangle(boxes[i], results);
                    Assert.Equal(new[] { i }, results);
                    tree.QuerySegment(boxes[i].Min, boxes[i].Min, results);
                    SetEqual(Enumerable.Range(0, boxes.Length).Where(j => SegmentOracle(boxes[i].Min, boxes[i].Min, boxes[j])), results);
                    Assert.Equal(i, tree.FindNearest(boxes[i].Min, 0));
                }
                var transformed = similarity.Apply(new Vector3d(7, -4, 0));
                var point = new Vector2d(transformed.x, transformed.y);
                tree.FindNearestK(point, 10, results);
                var expected = Enumerable.Range(0, boxes.Length).OrderBy(i => Distance(point, boxes[i])).ThenBy(i => i).Take(10).ToArray();
                Assert.Equal(expected, results);
                if (baseline == null) baseline = results.ToArray();
                if (similarity.IsExact) Assert.Equal(baseline, results);
                int inserted = tree.Insert(boxes[0]);
                Assert.True(tree.Remove(0));
                Assert.Equal(inserted, tree.FindNearest(boxes[0].Min, 0));
            }
        }

#if NET10_0_OR_GREATER
        static long MeasureWarmAllocationsOnDedicatedThread(Action query)
        {
            long allocated = -1;
            Exception failure = null;
            // Keep runner work off the measured thread; retain a strict zero-byte assertion.
            var worker = new Thread(() => {
                try {
                    for (int i = 0; i < 100; ++i) query();
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 100; ++i) query();
                    allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                } catch (Exception error) {
                    failure = error;
                }
            });
            worker.Start();
            worker.Join();
            if (failure != null) throw new InvalidOperationException("Allocation worker failed.", failure);
            return allocated;
        }

        [Theory]
        [InlineData("rectangle")]
        [InlineData("segment")]
        [InlineData("nearest")]
        [InlineData("nearest-k")]
        [InlineData("generic-nearest")]
        [InlineData("generic-nearest-k")]
        [InlineData("traverse")]
        public void IndividualWarmQueriesAllocateNoManagedMemory(string operation)
        {
            foreach (int side in new[] { 16, 150 }) {
                var tree = new LooseQuadtree2d(Grid(side));
                var results = new List<int>(tree.Count);
                var query = Box(10, 10, 3, 3);
                var distance = new ArrayDistance { Values = Enumerable.Repeat(1000.0, tree.Count).ToArray() };
                var visitor = new CountingVisitor { Query = query };
                long allocated = MeasureWarmAllocationsOnDedicatedThread(() => {
                    switch (operation) {
                        case "rectangle": tree.QueryRectangle(query, results); break;
                        case "segment": tree.QuerySegment(query.Min, query.Max, results); break;
                        case "nearest": tree.FindNearest(query.Min); break;
                        case "nearest-k": tree.FindNearestK(query.Min, 4, results); break;
                        case "generic-nearest": tree.FindNearest(query.Min, ref distance); break;
                        case "generic-nearest-k": tree.FindNearestK(query.Min, 4, results, ref distance); break;
                        case "traverse": tree.Traverse(ref visitor); break;
                        default: throw new ArgumentOutOfRangeException(nameof(operation));
                    }
                });
                Assert.Equal(0, allocated);
                if (operation.StartsWith("generic-", StringComparison.Ordinal)) Assert.True(distance.Calls > 0);
            }
        }

        [Fact]
        public void DedicatedAllocationMeasurementDetectsRealAllocationsAndPropagatesFailures()
        {
            byte[] retained = null;
            Assert.True(MeasureWarmAllocationsOnDedicatedThread(() => retained = new byte[1024]) > 0);
            Assert.Equal(1024, retained.Length);
            var expected = new InvalidOperationException("probe");
            var actual = Assert.Throws<InvalidOperationException>(() =>
                MeasureWarmAllocationsOnDedicatedThread(() => { throw expected; }));
            Assert.Same(expected, actual.InnerException);
        }

        [Fact]
        public void WarmHotQueriesAllocateNoManagedMemory()
        {
            var tree = new LooseQuadtree2d(Grid(150));
            var results = new List<int>(tree.Count);
            var query = Box(10, 10, 3, 3);
            var distance = new ArrayDistance { Values = Enumerable.Repeat(100.0, tree.Count).ToArray() };
            var visitor = new CountingVisitor { Query = query };
            for (int i = 0; i < 100; ++i) {
                tree.QueryRectangle(query, results);
                tree.QuerySegment(query.Min, query.Max, results);
                tree.FindNearest(query.Min);
                tree.FindNearestK(query.Min, 4, results);
                tree.FindNearest(query.Min, ref distance, 1);
                tree.FindNearestK(query.Min, 4, results, ref distance, 1);
                tree.Traverse(ref visitor);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; ++i) {
                tree.QueryRectangle(query, results);
                tree.QuerySegment(query.Min, query.Max, results);
                tree.FindNearest(query.Min);
                tree.FindNearestK(query.Min, 4, results);
                tree.FindNearest(query.Min, ref distance, 1);
                tree.FindNearestK(query.Min, 4, results, ref distance, 1);
                tree.Traverse(ref visitor);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
        }
#endif
    }
}
