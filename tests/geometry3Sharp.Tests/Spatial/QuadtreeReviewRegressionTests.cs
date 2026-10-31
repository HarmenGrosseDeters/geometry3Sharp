using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests.Spatial
{
    /// <summary>
    /// Cases from the independent review. The cost tests count visited items instead of measuring time, so they are exact:
    /// results were right in all of these cases, only every query looked at every item.
    /// </summary>
    public class QuadtreeReviewRegressionTests
    {
        struct CountingVisitor : IQuadtreeVisitor2d
        {
            public AxisAlignedBox2d Query;
            public int Visited;
            public bool TestRegion(AxisAlignedBox2d region)
            {
                return region.Min.x <= Query.Max.x && region.Max.x >= Query.Min.x && region.Min.y <= Query.Max.y && region.Max.y >= Query.Min.y;
            }
            public bool Visit(int id) { ++Visited; return false; }
        }

        static int Visited(LooseQuadtree2d tree, AxisAlignedBox2d query)
        {
            var visitor = new CountingVisitor { Query = query };
            tree.Traverse(ref visitor);
            return visitor.Visited;
        }

        static AxisAlignedBox2d[] Row(int n, bool vertical = false)
        {
            return Enumerable.Range(0, n).Select(i => vertical ? new AxisAlignedBox2d(0, i, 1, i + 1) : new AxisAlignedBox2d(i, 0, i + 1, 1)).ToArray();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ThinStripDescendsIntoTheTree(bool vertical)
        {
            // One row of a TIN: an extent of 100000 x 1 and items as high as the extent.
            var tree = new LooseQuadtree2d(Row(100000, vertical), false);
            AxisAlignedBox2d query = vertical ? new AxisAlignedBox2d(0.5, 500.2, 0.5, 500.3) : new AxisAlignedBox2d(500.2, 0.5, 500.3, 0.5);
            int visited = Visited(tree, query);
            Assert.True(visited < 64, $"visited {visited}");
            var found = new List<int>();
            tree.QueryRectangle(query, found);
            Assert.Equal(new[] { 500 }, found);
        }

        struct OccupiedRegions : IQuadtreeVisitor2d
        {
            public List<AxisAlignedBox2d> Regions;
            AxisAlignedBox2d current;
            bool counted;
            public bool TestRegion(AxisAlignedBox2d region) { current = region; counted = false; return true; }
            public bool Visit(int id)
            {
                if (!counted) Regions.Add(current);
                counted = true;
                return false;
            }
        }

        [Fact]
        public void ThinStrip_HasNoTwoOccupiedCellsOverTheSameRegion()
        {
            // An axis that is not cut must not divide the items between two children that cover the same region.
            var visitor = new OccupiedRegions { Regions = new List<AxisAlignedBox2d>() };
            // Centres on both sides of the middle of the uncut axis, so that the items would divide.
            AxisAlignedBox2d[] row = Enumerable.Range(0, 100000).Select(i => new AxisAlignedBox2d(i, i % 2 == 0 ? 0 : 0.5, i + 1, i % 2 == 0 ? 0.5 : 1)).ToArray();
            new LooseQuadtree2d(row, false).Traverse(ref visitor);
            Assert.True(visitor.Regions.Count > 100);
            Assert.Equal(visitor.Regions.Count, visitor.Regions.Distinct().Count());
        }

        [Fact]
        public void ThinStripAfterSingleInserts_DescendsToo()
        {
            var tree = new LooseQuadtree2d();
            foreach (AxisAlignedBox2d box in Row(20000)) tree.Insert(box);
            int visited = Visited(tree, new AxisAlignedBox2d(500.2, 0.5, 500.3, 0.5));
            Assert.True(visited < 64, $"visited {visited}");
        }

        [Fact]
        public void ElongatedExtentKeepsSquareCells_NotOnlyStrips()
        {
            // 20 km x 5 km with cells of 10: the elongated extent of the specification.
            var boxes = new List<AxisAlignedBox2d>();
            for (int j = 0; j < 500; ++j)
                for (int i = 0; i < 2000; ++i)
                    boxes.Add(new AxisAlignedBox2d(i * 10.0, j * 10.0, i * 10.0 + 10, j * 10.0 + 10));
            var tree = new LooseQuadtree2d(boxes, false);
            int visited = Visited(tree, new AxisAlignedBox2d(7003, 2503, 7004, 2504));
            Assert.True(visited < 300, $"visited {visited}");
        }

        [Fact]
        public void FarOutlierKeepsTheTreeDeep()
        {
            var random = new Random(3);
            var boxes = new List<AxisAlignedBox2d>();
            for (int i = 0; i < 100000; ++i) {
                double x = 2539000 + random.NextDouble() * 20000, y = 5640000 + random.NextDouble() * 20000;
                boxes.Add(new AxisAlignedBox2d(x, y, x + 7, y + 7));
            }
            var query = new AxisAlignedBox2d(2549000, 5650000, 2549020, 5650020);
            int without = Visited(new LooseQuadtree2d(boxes, false), query);
            boxes.Add(new AxisAlignedBox2d(new Vector2d(1e15, 1e15)));
            int with = Visited(new LooseQuadtree2d(boxes, false), query);
            Assert.True(with < 8 * without + 64, $"visited {with} with the outlier, {without} without");
        }

        [Fact]
        public void RemovingCoincidentItemsById_DoesNotWalkTheChain()
        {
            // 200000 items at one position share one chain. Removing from its far end used to walk all of it each time.
            var boxes = Enumerable.Repeat(new AxisAlignedBox2d(new Vector2d(1, 1)), 200000).ToArray();
            var tree = new LooseQuadtree2d(boxes, false);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < boxes.Length; ++i) Assert.True(tree.Remove(i));
            Assert.True(watch.Elapsed.TotalSeconds < 5, $"took {watch.Elapsed.TotalSeconds} s");
            Assert.Equal(0, tree.Count);
        }

        [Fact]
        public void RemovedItems_LeaveTheChainWhole_InEveryPosition()
        {
            // First, middle and last of one chain, each followed by a query for what remains.
            var boxes = Enumerable.Repeat(new AxisAlignedBox2d(new Vector2d(1, 1)), 7).ToArray();
            foreach (int[] order in new[] { new[] { 0, 3, 6 }, new[] { 6, 3, 0 }, new[] { 3, 0, 6 }, new[] { 1, 2, 3, 4, 5, 6, 0 } }) {
                var tree = new LooseQuadtree2d(boxes, false);
                var live = new HashSet<int>(Enumerable.Range(0, boxes.Length));
                var found = new List<int>();
                foreach (int id in order) {
                    Assert.True(tree.Remove(id));
                    live.Remove(id);
                    tree.QueryRectangle(boxes[0], found);
                    Assert.Equal(live.OrderBy(i => i), found.OrderBy(i => i));
                }
                int added = tree.Insert(boxes[0]);
                tree.QueryRectangle(boxes[0], found);
                Assert.Contains(added, found);
                Assert.Equal(live.Count + 1, found.Count);
            }
        }

        // ---- segments built from their endpoints

        static (Vector3d[] vertices, Index2i[] edges) JitteredGrid(int cells)
        {
            var random = new Random(1);
            var vertices = new Vector3d[(cells + 1) * (cells + 1)];
            for (int j = 0; j <= cells; ++j)
                for (int i = 0; i <= cells; ++i)
                    vertices[j * (cells + 1) + i] = new Vector3d(
                        2539000 + i * 7.07 + (i > 0 && i < cells ? random.NextDouble() * 2 - 1 : 0),
                        5640000 + j * 7.07 + (j > 0 && j < cells ? random.NextDouble() * 2 - 1 : 0),
                        random.NextDouble() * 100);
            var edges = new List<Index2i>();
            for (int j = 0; j < cells; ++j)
                for (int i = 0; i < cells; ++i) {
                    int a = j * (cells + 1) + i;
                    edges.Add(new Index2i(a, a + 1));
                    edges.Add(new Index2i(a, a + cells + 1));
                    edges.Add(new Index2i(a, a + cells + 2));
                }
            return (vertices, edges.ToArray());
        }

        static Vector2d XY(Vector3d p) { return new Vector2d(p.x, p.y); }

        [Fact]
        public void EndpointSegments_AreHitExactlyAtTheirOwnVertices()
        {
            // Through Segment3d, 43% of these edges missed the point query at their own start vertex.
            (Vector3d[] vertices, Index2i[] edges) = JitteredGrid(30);
            var tree = new SegmentQuadtree2d(vertices, edges);
            var found = new List<int>();
            for (int i = 0; i < edges.Length; ++i) {
                Vector3d a = vertices[edges[i].a], b = vertices[edges[i].b];
                tree.QueryPoint(XY(a), 0, found);
                Assert.Contains(i, found);
                tree.QueryPoint(XY(b), 0, found);
                Assert.Contains(i, found);
                tree.QueryIntersections(XY(a), new Vector2d(a.x - 1, a.y - 3), found);
                Assert.Contains(i, found);
                tree.QueryRectangle(new AxisAlignedBox2d(XY(a)), found);
                Assert.Contains(i, found);
                tree.GetEndpoints(i, out Vector3d resultA, out Vector3d resultB);
                Assert.Equal(a, resultA);
                Assert.Equal(b, resultB);
            }
        }

        [Fact]
        public void EndpointSegments_KeepTheirBoundsExact()
        {
            var tree = new SegmentQuadtree2d();
            int id = tree.Insert(new Vector3d(0, 0, 10), new Vector3d(1, 3, 20));
            Assert.Equal(new AxisAlignedBox2d(0, 0, 1, 3), tree.GetBounds(id));
            var found = new List<int>();
            tree.QueryRectangle(new AxisAlignedBox2d(-1, -1, 1, -1e-16), found);
            Assert.Empty(found);
            tree.QueryIntersections(new Vector2d(-1, 0), new Vector2d(0, 0), found);
            Assert.Equal(new[] { id }, found);
        }

        [Fact]
        public void EndpointSegments_InsertRemoveAndLargeCoordinates()
        {
            var tree = new SegmentQuadtree2d();
            Vector3d a = new Vector3d(0.1, 0.2, 7), b = new Vector3d(0.7, 0.3, 8);
            int first = tree.Insert(a, b), second = tree.Insert(a, b);
            Assert.False(tree.Remove(b, a)); // the other direction is another item
            Assert.False(tree.Remove(a, new Vector3d(b.x, b.y, 9))); // so is another z
            Assert.True(tree.Remove(a, b));
            Assert.False(tree.ContainsId(first));
            Assert.True(tree.ContainsId(second));

            // The length of a Segment3d overflows here, its endpoints do not.
            int far = tree.Insert(new Vector3d(-1e200, 0, 0), new Vector3d(1e200, 1e200, 0));
            var found = new List<int>();
            tree.QueryPoint(new Vector2d(1e200, 1e200), 0, found);
            Assert.Equal(new[] { far }, found);

            Assert.Throws<ArgumentException>(() => tree.Insert(new Vector3d(double.NaN, 0, 0), b));
            Assert.Throws<ArgumentException>(() => tree.Insert(a, new Vector3d(0, 0, double.PositiveInfinity)));
            Assert.Equal(2, tree.Count);
            Assert.Throws<ArgumentNullException>(() => new SegmentQuadtree2d(null, new Index2i[0]));
            Assert.Throws<ArgumentNullException>(() => new SegmentQuadtree2d(new Vector3d[0], null));
        }

        [Fact]
        public void SegmentsFromASegment3d_StayValidInput()
        {
            var segment = new Segment3d(new Vector3d(1, 1, 0), new Vector3d(3, 2, 0));
            var tree = new SegmentQuadtree2d(new[] { segment });
            Assert.True((tree.GetItem(0).P0 - segment.P0).Length < 1e-14);
            tree.GetEndpoints(0, out Vector3d a, out Vector3d b);
            Assert.Equal(segment.P0, a);
            Assert.Equal(segment.P1, b);
            Assert.True(tree.Remove(segment));
        }

        // ---- inclusive tolerances

        [Theory]
        [InlineData(187.0)]
        [InlineData(273.0)]
        [InlineData(275.0)]
        [InlineData(279.0)]
        public void ToleranceIsInclusive_WhereTheRoundedDistanceIsAbove(double x)
        {
            var segments = new SegmentQuadtree2d();
            int id = segments.Insert(new Vector3d(0, 3, 0), new Vector3d(1000, 3, 0));
            var found = new List<int>();
            segments.QueryPoint(new Vector2d(x, 0), 3, found);
            Assert.Equal(new[] { id }, found);
            segments.QueryPoint(new Vector2d(x, 0), Below(3), found);
            Assert.Empty(found);

            var triangles = new TriangleQuadtree2d(new[] { new Triangle3d(new Vector3d(0, 3, 1), new Vector3d(1000, 3, 1), new Vector3d(500, 100, 2)) });
            Assert.True(triangles.TryGetHeight(new Vector2d(x, 0), out double height, 3));
            Assert.Equal(1.0 - 3.0 / 97.0, height, 12);
            Assert.False(triangles.TryGetHeight(new Vector2d(x, 0), out height, Below(3)));
        }

        [Fact]
        public void ToleranceBetweenPoints_IsExactWhereTheRootRounds()
        {
            // The rounded root is on one side of the true distance, and that side decides inclusion.
            var points = new PointQuadtree2d(new[] { new Vector3d(0, 0, 0) });
            var found = new List<int>();
            double x = 0.3, y = 0.4;
            double rounded = Math.Sqrt(x * x + y * y);
            points.QueryNearby(new Vector2d(x, y), rounded, found);
            bool within = Compare(x, y, rounded) <= 0;
            Assert.Equal(within, found.Count == 1);
            points.QueryNearby(new Vector2d(x, y), within ? Below(rounded) : Above(rounded), found);
            Assert.Equal(!within, found.Count == 1);
        }

        static double Below(double value) { return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value) - 1); }
        static double Above(double value) { return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value) + 1); }

        static int Compare(double x, double y, double tolerance)
        {
            System.Numerics.BigInteger Scaled(double v)
            {
                long bits = BitConverter.DoubleToInt64Bits(v);
                int exponent = (int)((bits >> 52) & 2047);
                long fraction = bits & 0xfffffffffffffL;
                return new System.Numerics.BigInteger(fraction | 0x10000000000000L) << (exponent - 1);
            }
            System.Numerics.BigInteger a = Scaled(x), b = Scaled(y), t = Scaled(tolerance);
            return (a * a + b * b).CompareTo(t * t);
        }

        [Fact]
        public void ToleranceIsExact_OnASlantedSegment_WhereTheRoundedDistanceIsFarOff()
        {
            // A 3-4-5 slope, and a point at exactly 0.15625 from it. The rounded distance is above by 1.7e-15.
            Vector2d p = new Vector2d(43.96875, 33.171875);
            var segments = new SegmentQuadtree2d();
            int id = segments.Insert(new Vector3d(0, 0, 0), new Vector3d(80, 60, 0));
            var found = new List<int>();
            segments.QueryPoint(p, 0.15625, found);
            Assert.Equal(new[] { id }, found);
            segments.QueryPoint(p, Below(0.15625), found);
            Assert.Empty(found);

            var triangles = new TriangleQuadtree2d(new[] { new Triangle3d(new Vector3d(0, 0, 1), new Vector3d(80, 60, 1), new Vector3d(80, 0, 1)) });
            Assert.True(triangles.TryGetHeight(p, out double height, 0.15625));
            Assert.False(triangles.TryGetHeight(p, out height, Below(0.15625)));

            // Every exact tie along this segment, at 1/64 steps: the perpendicular (-3, 4)/5 times the tolerance.
            for (int i = 1; i < 4096; i += 7) {
                var point = new Vector2d(i * 0.015625 - 0.09375, i * 0.75 * 0.015625 + 0.125);
                segments.QueryPoint(point, 0.15625, found);
                Assert.Equal(new[] { id }, found);
                segments.QueryPoint(point, Below(0.15625), found);
                Assert.Empty(found);
            }
        }

        [Fact]
        public void Tolerance_RefusesWhereTheRoundedDistanceIsBelow()
        {
            var segments = new SegmentQuadtree2d();
            segments.Insert(new Vector3d(2318.570703410707, -9542.214930142743, 0), new Vector3d(-9156.81739018991, 7420.300384311129, 0));
            var found = new List<int>();
            // The true distance is 0.00999999999988, and it rounds to 0.00999999999714.
            segments.QueryPoint(new Vector2d(-8883.963460177823, 7016.959449428462), 0.009999999999, found);
            Assert.Empty(found);
        }

#if NET10_0_OR_GREATER
        [Fact]
        public void ZeroToleranceOnAPoint_DoesNotAllocate()
        {
            var points = new PointQuadtree2d(new[] { new Vector3d(1, 1, 0) });
            var found = new List<int>(4);
            var query = new Vector2d(1, 1);
            for (int i = 0; i < 2000; ++i) points.QueryNearby(query, 0, found);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; ++i) points.QueryNearby(query, 0, found);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(new[] { 0 }, found);
        }
#endif

        // ---- a Segment3d from GetItem, and endpoints it cannot hold

        [Fact]
        public void RemovingWhatGetItemReturned_RemovesTheItem_ForEveryKindOfBuild()
        {
            var random = new Random(4);
            for (int i = 0; i < 400; ++i) {
                Vector3d a = new Vector3d(random.NextDouble() * 1000, random.NextDouble() * 1000, random.NextDouble());
                Vector3d b = new Vector3d(random.NextDouble() * 1000, random.NextDouble() * 1000, random.NextDouble());
                var fromEndpoints = new SegmentQuadtree2d();
                int id = fromEndpoints.Insert(a, b);
                Assert.True(fromEndpoints.Remove(fromEndpoints.GetItem(id)), $"case {i}");
                Assert.False(fromEndpoints.ContainsId(id));

                var fromSegment = new SegmentQuadtree2d();
                id = fromSegment.Insert(new Segment3d(a, b));
                Assert.True(fromSegment.Remove(fromSegment.GetItem(id)), $"case {i}");

                var fromInput = new SegmentQuadtree2d();
                id = fromInput.Insert(new Segment3d(a, b));
                Assert.True(fromInput.Remove(new Segment3d(a, b)), $"case {i}");
            }
            // Nearly vertical in z, with an XY box a few ulps wide: the rounded endpoints of GetItem can lie beside that box.
            for (int i = 0; i < 2000; ++i) {
                double x = 0.1 + random.NextDouble(), y = 0.2 + random.NextDouble();
                double ulp = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1) - x;
                var tall = new SegmentQuadtree2d();
                int id = tall.Insert(new Vector3d(x, y, 0), new Vector3d(x + ulp * random.Next(0, 3), y + ulp * random.Next(0, 3), 1000 * random.NextDouble() + 1));
                Assert.True(tall.Remove(tall.GetItem(id)), $"tall case {i}");
            }
            // Among several equal ones the first, and nothing near it.
            var tree = new SegmentQuadtree2d();
            Vector3d p = new Vector3d(0.1, 0.2, 0.3), q = new Vector3d(0.7, 1.1, -0.4);
            int other = tree.Insert(p, new Vector3d(0.7, 1.1000001, -0.4));
            int one = tree.Insert(p, q), two = tree.Insert(p, q);
            Assert.True(tree.Remove(tree.GetItem(two)));
            Assert.False(tree.ContainsId(one));
            Assert.True(tree.ContainsId(two));
            Assert.True(tree.ContainsId(other));
        }

        [Theory]
        [InlineData(1e200, 3e200)]
        [InlineData(-1e308, 1e308)]
        [InlineData(0.0, 1e-17)]
        public void GetItem_Refuses_WhereASegmentCannotHoldTheEndpoints(double from, double to)
        {
            var tree = new SegmentQuadtree2d();
            int id = tree.Insert(new Vector3d(from, 0, 0), new Vector3d(to, 0, 0));
            Assert.Throws<ArithmeticException>(() => tree.GetItem(id));
            tree.GetEndpoints(id, out Vector3d a, out Vector3d b);
            Assert.Equal(from, a.x);
            Assert.Equal(to, b.x);
        }

        [Fact]
        public void GetItem_StillGivesAPoint_ForEqualEndpoints()
        {
            var tree = new SegmentQuadtree2d();
            int id = tree.Insert(new Vector3d(1, 2, 3), new Vector3d(1, 2, 3));
            Assert.Equal(new Vector3d(1, 2, 3), tree.GetItem(id).P0);
        }

        // ---- cost of equal items, and of cells one ulp wide

        struct RegionCounter : IQuadtreeVisitor2d
        {
            public int Regions;
            public bool TestRegion(AxisAlignedBox2d region) { ++Regions; return true; }
            public bool Visit(int id) { return false; }
        }

        [Fact]
        public void SmallClustersOfEqualItems_BuildNoChainOfNodes()
        {
            // 500 clusters of 17 equal points: cutting cannot separate them, and went down to the depth limit in each.
            var boxes = new List<AxisAlignedBox2d>();
            for (int c = 0; c < 500; ++c)
                for (int i = 0; i < 17; ++i)
                    boxes.Add(new AxisAlignedBox2d(new Vector2d(c * 7.0, c * 13.0 % 1000)));
            foreach (var tree in new[] { new LooseQuadtree2d(boxes, false), InsertedOneByOne(boxes) }) {
                var visitor = new RegionCounter();
                tree.Traverse(ref visitor);
                Assert.True(visitor.Regions < 2 * boxes.Count, $"{visitor.Regions} regions for {boxes.Count} items");
                var found = new List<int>();
                tree.QueryRectangle(boxes[17 * 200], found);
                Assert.Equal(Enumerable.Range(17 * 200, 17), found.OrderBy(i => i));
            }
        }

        static LooseQuadtree2d InsertedOneByOne(IEnumerable<AxisAlignedBox2d> boxes)
        {
            var tree = new LooseQuadtree2d();
            foreach (AxisAlignedBox2d box in boxes) tree.Insert(box);
            return tree;
        }

        [Fact]
        public void ManyEqualItemsInsertedOneByOne_StayLinear_AndCanStillSplitLater()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            AxisAlignedBox2d same = new AxisAlignedBox2d(new Vector2d(5, 5));
            var tree = new LooseQuadtree2d(new[] { new AxisAlignedBox2d(0, 0, 10, 10) }, false);
            for (int i = 0; i < 300000; ++i) tree.Insert(same);
            Assert.True(watch.Elapsed.TotalSeconds < 5, $"took {watch.Elapsed.TotalSeconds} s");

            // Different items arrive in the same cell: it splits at the next attempt, and every query stays right.
            var found = new List<int>();
            for (int i = 0; i < 300000; ++i) tree.Insert(new AxisAlignedBox2d(new Vector2d(5 + (i % 1000) * 1e-3, 5)));
            tree.QueryRectangle(same, found);
            Assert.Equal(300000 + 300 + 1, found.Count); // the big one, the equal ones, and those of i % 1000 == 0
            int visited = Visited(tree, new AxisAlignedBox2d(5.5, 5, 5.5005, 5));
            Assert.True(visited < 400000, $"visited {visited}");
        }

        [Fact]
        public void ACellWhoseLongAxisIsOneUlpWide_IsCutAcrossTheShortOne()
        {
            double next = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1.0) + 1);
            AxisAlignedBox2d[] boxes = Enumerable.Range(0, 5000).Select(i => new AxisAlignedBox2d(new Vector2d(i % 2 == 0 ? 1 : next, i * 1e-19))).ToArray();
            var tree = new LooseQuadtree2d(boxes, false);
            AxisAlignedBox2d query = new AxisAlignedBox2d(boxes[2000].Min, boxes[2000].Max);
            int visited = Visited(tree, query);
            Assert.True(visited < 200, $"visited {visited}");
            var found = new List<int>();
            tree.QueryRectangle(query, found);
            Assert.Equal(new[] { 2000 }, found);
        }

        // ---- heights next to a spike

        [Fact]
        public void HeightOnAnEdgeOppositeASpike_IsNotSwampedByIt()
        {
            // On the edge from V0 to V2, so the spike at V1 has weight 0, which rounds to about 1e-16.
            var tree = new TriangleQuadtree2d(new[] { new Triangle3d(new Vector3d(0, 0, 1), new Vector3d(21, 0, 1e20), new Vector3d(11, 6, 1)) });
            Assert.True(tree.TryGetHeight(new Vector2d(8.25, 4.5), out double height, 0));
            Assert.Equal(1, height);

            // The other way round: the spike at V2, on the edge from V0 to V1.
            tree = new TriangleQuadtree2d(new[] { new Triangle3d(new Vector3d(0, 0, 1), new Vector3d(11, 6, 1), new Vector3d(21, 0, 1e20)) });
            Assert.True(tree.TryGetHeight(new Vector2d(8.25, 4.5), out height, 0));
            Assert.Equal(1, height);
        }

        // ---- heights on the diagonal of a grid TIN

        [Theory]
        [InlineData(0.5, 0.5, 3.0)]
        [InlineData(0.25, 0.25, 2.0)]
        [InlineData(0.75, 0.75, 4.0)]
        public void HeightOnADiagonal_IsRight_AtEveryPosition(double x, double y, double expected)
        {
            foreach (double offset in new[] { 0.0, 2539000.0, -700.0 }) {
                var tree = new TriangleQuadtree2d(new[] { new Triangle3d(new Vector3d(offset, offset, 1), new Vector3d(offset + 1, offset, 2), new Vector3d(offset + 1, offset + 1, 5)) });
                Assert.True(tree.TryGetHeight(new Vector2d(offset + x, offset + y), out double height, 0));
                Assert.Equal(expected, height, 9);
            }
        }

#if NET10_0_OR_GREATER
        [Fact]
        public void HeightOnADiagonal_DoesNotAllocate()
        {
            var tree = new TriangleQuadtree2d(new[] { new Triangle3d(new Vector3d(0, 0, 1), new Vector3d(1, 0, 2), new Vector3d(1, 1, 5)) });
            var point = new Vector2d(0.5, 0.5);
            double height = 0;
            for (int i = 0; i < 2000; ++i) tree.TryGetHeight(point, out height, 0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; ++i) tree.TryGetHeight(point, out height, 0);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(3, height);
        }
#endif

        // ---- nearest: ties and the tolerance height

        [Fact]
        public void NearestTie_GoesToTheLowerId_EvenWhereTheItemDistanceRoundsBelowItsBox()
        {
            // At x = 49: (1/49)*49 rounds to just under 1, the box distance is 1.
            var tree = new SegmentQuadtree2d(new[] {
                new Segment3d(new Vector3d(0, 500, 0), new Vector3d(100, 500, 0)),
                new Segment3d(new Vector3d(0, -500, 0), new Vector3d(100, -500, 0)) });
            int lower = tree.Insert(new Vector3d(0, -1, 0), new Vector3d(100, -1, 0));
            tree.Insert(new Vector3d(0, 1, 0), new Vector3d(100, 1, 0));
            Assert.Equal(lower, tree.FindNearest(new Vector2d(49, 0)));
            var found = new List<int>();
            tree.FindNearestK(new Vector2d(49, 0), 1, found);
            Assert.Equal(new[] { lower }, found);
        }

        [Fact]
        public void ToleranceHeight_ComesFromTheNearestTriangle_InAnyOrder()
        {
            Triangle3d far = new Triangle3d(new Vector3d(0, 0.009, 10), new Vector3d(10, 0.009, 10), new Vector3d(5, 5, 10));
            Triangle3d near = new Triangle3d(new Vector3d(0, -0.001, 20), new Vector3d(10, -0.001, 20), new Vector3d(5, -5, 20));
            var point = new Vector2d(5, 0.002);
            foreach (Triangle3d[] order in new[] { new[] { far, near }, new[] { near, far } }) {
                var tree = new TriangleQuadtree2d(order);
                Assert.True(tree.TryGetHeight(point, out double height));
                Assert.Equal(20, height);
                var hits = new List<QuadtreeHeightHit2d>();
                tree.QueryHeights(point, hits, stopAfterFirst: true);
                Assert.Equal(Array.IndexOf(order, near), Assert.Single(hits).Id);
                Assert.Equal(20, hits[0].Height);
                tree.QueryHeights(point, hits);
                Assert.Equal(2, hits.Count);
            }

            // Equally far: the lower ID.
            var tie = new TriangleQuadtree2d(new[] {
                new Triangle3d(new Vector3d(0, 1, 1), new Vector3d(2, 1, 1), new Vector3d(1, 2, 1)),
                new Triangle3d(new Vector3d(0, -1, 2), new Vector3d(2, -1, 2), new Vector3d(1, -2, 2)) });
            Assert.True(tie.TryGetHeight(new Vector2d(1, 0), out double tied, 1.5));
            Assert.Equal(1, tied);

            // The same tie where the higher ID is visited first: inserted items go to the head of their cell's list.
            var inserted = new TriangleQuadtree2d(new[] { new Triangle3d(new Vector3d(-9, -9, 0), new Vector3d(9, -9, 0), new Vector3d(0, -8.5, 0)), new Triangle3d(new Vector3d(-9, 9, 0), new Vector3d(9, 9, 0), new Vector3d(0, 8.5, 0)) });
            inserted.Insert(new Triangle3d(new Vector3d(0, 1, 1), new Vector3d(2, 1, 1), new Vector3d(1, 2, 1)));
            inserted.Insert(new Triangle3d(new Vector3d(0, -1, 2), new Vector3d(2, -1, 2), new Vector3d(1, -2, 2)));
            Assert.True(inserted.TryGetHeight(new Vector2d(1, 0), out double insertedTie, 1.5));
            Assert.Equal(1, insertedTie);

            // A degenerate triangle nearer than the real one gives no height.
            var flat = new TriangleQuadtree2d(new[] {
                new Triangle3d(new Vector3d(0, 0.001, 5), new Vector3d(1, 0.001, 5), new Vector3d(2, 0.001, 5)),
                near });
            Assert.True(flat.TryGetHeight(point, out double notFlat));
            Assert.Equal(20, notFlat);
        }
    }
}
