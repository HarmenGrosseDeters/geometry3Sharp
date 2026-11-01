using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Xunit;

namespace geometry3Sharp.Tests.Spatial
{
    public class QuadtreeQueryOptimizationTests
    {
        struct Regions : IQuadtreeVisitor2d
        {
            public List<AxisAlignedBox2d> Bounds;
            public bool TestRegion(AxisAlignedBox2d region) { Bounds.Add(region); return true; }
            public bool Visit(int id) { return false; }
        }

        static bool Overlap(AxisAlignedBox2d a, AxisAlignedBox2d b)
        {
            return a.Min.x <= b.Max.x && a.Max.x >= b.Min.x && a.Min.y <= b.Max.y && a.Max.y >= b.Min.y;
        }

        static List<AxisAlignedBox2d> VisitRegions(LooseQuadtree2d tree)
        {
            var visitor = new Regions { Bounds = new List<AxisAlignedBox2d>() };
            tree.Traverse(ref visitor);
            return visitor.Bounds;
        }

        // Regions are the bounds of what a part of the tree holds. The extent keeps removed items as well, so it can be larger.
        static void AssertRootHoldsTheItems(LooseQuadtree2d tree)
        {
            AxisAlignedBox2d root = VisitRegions(tree)[0], extent = tree.Extent;
            Assert.True(extent.Min.x <= root.Min.x && extent.Min.y <= root.Min.y && extent.Max.x >= root.Max.x && extent.Max.y >= root.Max.y);
            var all = new List<int>();
            tree.QueryRectangle(root, all);
            Assert.Equal(tree.Count, all.Count);
        }

        [Fact]
        public void RegionsHoldTheirContentAfterSplitRemovalAndExtentRebuild()
        {
            var tree = new LooseQuadtree2d(new[] { new AxisAlignedBox2d(0, 0, 8, 8) }, false);
            AssertRootHoldsTheItems(tree);
            for (int i = 0; i < 80; ++i)
                tree.Insert(new AxisAlignedBox2d(i % 8, i / 8, i % 8 + 0.5, i / 8 + 0.5));
            Assert.True(VisitRegions(tree).Count > 1);
            AssertRootHoldsTheItems(tree);
            Assert.True(tree.Remove(0));
            var before = VisitRegions(tree);
            tree.Insert(new AxisAlignedBox2d(new Vector2d(1, 1)));
            Assert.Empty(before.Except(VisitRegions(tree))); // an insert inside may split a full cell, but moves none
            tree.Insert(new AxisAlignedBox2d(new Vector2d(-1000, 2000)));
            AssertRootHoldsTheItems(tree);
            tree.Insert(new AxisAlignedBox2d(new Vector2d(-double.MaxValue, -double.MaxValue)));
            tree.Insert(new AxisAlignedBox2d(new Vector2d(double.MaxValue, double.MaxValue)));
            AssertRootHoldsTheItems(tree);
            var result = new List<int>();
            foreach (var p in new[] { new Vector2d(-1000, 2000), new Vector2d(-double.MaxValue, -double.MaxValue), new Vector2d(double.MaxValue, double.MaxValue) }) {
                tree.QueryRectangle(new AxisAlignedBox2d(p), result);
                Assert.NotEmpty(result);
                Assert.All(result, id => Assert.True(Overlap(tree.GetBounds(id), new AxisAlignedBox2d(p))));
            }
        }

        struct RootItems : IQuadtreeVisitor2d
        {
            public List<int> Ids;
            public int Seen;
            public bool TestRegion(AxisAlignedBox2d region) { return Seen++ == 0; }
            public bool Visit(int id) { Ids.Add(id); return false; }
        }

        [Fact]
        public void AnItemTooLargeForAChild_StaysAbove()
        {
            // Pushed down by its center, it would stretch the bounds of a whole branch to cover everything.
            var boxes = Enumerable.Range(0, 400).Select(i => new AxisAlignedBox2d(new Vector2d(i % 20 * 50 + 3, i / 20 * 50 + 3))).ToList();
            boxes.Add(new AxisAlignedBox2d(0, 0, 1000, 1000));
            var visitor = new RootItems { Ids = new List<int>() };
            new LooseQuadtree2d(boxes, false).Traverse(ref visitor);
            Assert.Equal(new[] { 400 }, visitor.Ids);
        }

        [Fact]
        public void TheTreeStopsAtItsDepthLimit_BuiltAndInserted()
        {
            // A point at the origin and points approaching it: only a limit stops the cells halving down to the smallest double.
            var boxes = new List<AxisAlignedBox2d> { new AxisAlignedBox2d(new Vector2d(0, 0)) };
            for (int k = 0; k < 1000; ++k) boxes.Add(new AxisAlignedBox2d(new Vector2d(Math.Pow(2, -k), Math.Pow(2, -k))));
            var built = new LooseQuadtree2d(boxes, false);
            var inserted = new LooseQuadtree2d(boxes.GetRange(0, 2), false);
            foreach (AxisAlignedBox2d box in boxes.Skip(2)) inserted.Insert(box);
            // At most two of the four children hold something: those on the diagonal.
            foreach (var tree in new[] { built, inserted }) {
                Assert.True(VisitRegions(tree).Count <= 2 * tree.MaxDepth + 1, $"{VisitRegions(tree).Count} regions");
                var found = new List<int>();
                tree.QueryRectangle(new AxisAlignedBox2d(new Vector2d(0, 0)), found);
                Assert.Equal(new[] { 0 }, found);
            }
        }

        struct Everything : IQuadtreeVisitor2d
        {
            public List<AxisAlignedBox2d> Regions;
            public List<int> Ids;
            public bool TestRegion(AxisAlignedBox2d region) { Regions.Add(region); return true; }
            public bool Visit(int id) { Ids.Add(id); return false; }
        }

        static Everything Walk(LooseQuadtree2d tree)
        {
            var visitor = new Everything { Regions = new List<AxisAlignedBox2d>(), Ids = new List<int>() };
            tree.Traverse(ref visitor);
            return visitor;
        }

        public static IEnumerable<object[]> LargeBuilds()
        {
            const int n = 200000;
            var random = new Random(5);
            Func<double> r = random.NextDouble;
            AxisAlignedBox2d Small(double x, double y) => new AxisAlignedBox2d(x, y, x + r() * 0.01, y + r() * 0.01);
            yield return new object[] { "uniform", Enumerable.Range(0, n).Select(i => Small(r() * 100, r() * 100)).ToArray() };
            yield return new object[] { "large items among small", Enumerable.Range(0, n).Select(i => i % 50 == 0
                ? new AxisAlignedBox2d(r() * 10, r() * 10, 90 + r() * 10, 90 + r() * 10) : Small(r() * 100, r() * 100)).ToArray() };
            yield return new object[] { "one corner and a far point", Enumerable.Range(0, n).Select(i => i == 0
                ? new AxisAlignedBox2d(new Vector2d(1e6, 1e6)) : Small(r(), r())).ToArray() };
            yield return new object[] { "all equal", Enumerable.Repeat(new AxisAlignedBox2d(1, 1, 2, 2), n).ToArray() };
            yield return new object[] { "one child stays below the bucket", Enumerable.Range(0, n).Select(i => i < 5
                ? new AxisAlignedBox2d(new Vector2d(100 - i, 100 - i)) : Small(r(), r())).ToArray() };
            yield return new object[] { "a line", Enumerable.Range(0, n).Select(i => new AxisAlignedBox2d(new Vector2d(i, 0))).ToArray() };
            yield return new object[] { "a chain to the depth limit", Enumerable.Range(0, n).Select(i => {
                double v = Math.Pow(2, -(i % 1000));
                return new AxisAlignedBox2d(new Vector2d(v, v));
            }).ToArray() };
        }

        [Theory]
        [MemberData(nameof(LargeBuilds))]
        public void ParallelBuild_GivesTheTreeAndItemOrderOfASequentialOne(string name, AxisAlignedBox2d[] boxes)
        {
            Assert.NotNull(name);
            Assert.True(boxes.Length >= 2 * LooseQuadtree2d.ParallelThreshold);
            Everything serial = Walk(new LooseQuadtree2d(boxes, false));
            Everything parallel = Walk(new LooseQuadtree2d(boxes, true));
            Assert.Equal(serial.Regions, parallel.Regions);
            Assert.Equal(serial.Ids, parallel.Ids);
            Assert.Equal(Enumerable.Range(0, boxes.Length), parallel.Ids.OrderBy(id => id));
            Assert.Equal(serial.Ids, Walk(new LooseQuadtree2d(boxes, true)).Ids);
            Assert.Equal(new LooseQuadtree2d(boxes, false).Extent, new LooseQuadtree2d(boxes, true).Extent);
        }

        static long Bits(double value) { return BitConverter.DoubleToInt64Bits(value); }

        [Fact]
        public void ALargeBuild_HasTheExtentOfASequentialOne_DownToTheSignOfAZero()
        {
            // Items in different blocks, the extreme ones in the last block and at the edges of a block.
            int block = LooseQuadtree2d.ParallelThreshold, count = 3 * block + 5;
            foreach (bool negativeFirst in new[] { true, false }) {
                double first = negativeFirst ? -0.0 : 0.0, second = negativeFirst ? 0.0 : -0.0;
                var boxes = Enumerable.Range(0, count).Select(i => new AxisAlignedBox2d(1 + i % 7, 1 + i % 5, 2 + i % 7, 2 + i % 5)).ToArray();
                boxes[block - 1] = new AxisAlignedBox2d(first, first, 3, 3);
                boxes[2 * block] = new AxisAlignedBox2d(second, second, 3, 3);
                boxes[block] = new AxisAlignedBox2d(1, 1, 90, 3);
                boxes[count - 1] = new AxisAlignedBox2d(4, 4, 5, 80);
                AxisAlignedBox2d serial = new LooseQuadtree2d(boxes, false).Extent, parallel = new LooseQuadtree2d(boxes, true).Extent;
                Assert.Equal(Bits(first), Bits(parallel.Min.x));
                Assert.Equal(Bits(serial.Min.x), Bits(parallel.Min.x));
                Assert.Equal(Bits(serial.Min.y), Bits(parallel.Min.y));
                Assert.Equal(90, parallel.Max.x);
                Assert.Equal(80, parallel.Max.y);
            }
        }

        [Fact]
        public void ALargeBuild_ReportsABadBoxLikeASmallOne()
        {
            int block = LooseQuadtree2d.ParallelThreshold;
            AxisAlignedBox2d[] boxes = Enumerable.Repeat(new AxisAlignedBox2d(0, 0, 1, 1), 2 * block + 3).ToArray();
            foreach (int at in new[] { 0, block - 1, block, boxes.Length - 1 }) {
                boxes[at] = new AxisAlignedBox2d(double.NaN, 0, 1, 1);
                ArgumentException parallel = Assert.ThrowsAny<ArgumentException>(() => new LooseQuadtree2d(boxes, true));
                ArgumentException serial = Assert.ThrowsAny<ArgumentException>(() => new LooseQuadtree2d(boxes, false));
                Assert.Equal(serial.GetType(), parallel.GetType());
                Assert.Equal(serial.Message, parallel.Message);
                boxes[at] = new AxisAlignedBox2d(0, 0, 1, 1);
            }
            Assert.Equal(boxes.Length, new LooseQuadtree2d(new List<AxisAlignedBox2d>(boxes), true).Count);
        }

        [Fact]
        public void TheAdapter_CopiesAnArray_AndAnyOtherList()
        {
            Triangle3d[] triangles = TriangleGrid(4, v => v);
            var fromArray = new TriangleQuadtree2d(triangles);
            var fromList = new TriangleQuadtree2d(new List<Triangle3d>(triangles));
            Vector3d first = triangles[0].V0;
            triangles[0] = new Triangle3d(new Vector3d(9, 9, 9), new Vector3d(10, 9, 9), new Vector3d(9, 10, 9));
            Assert.Equal(first, fromArray.GetItem(0).V0);
            Assert.Equal(first, fromList.GetItem(0).V0);
            Assert.Equal(triangles[triangles.Length - 1].V2, fromArray.GetItem(triangles.Length - 1).V2);
            Assert.Equal(triangles[triangles.Length - 1].V2, fromList.GetItem(triangles.Length - 1).V2);
        }

        static Triangle3d[] TriangleGrid(int cells, Func<Vector3d, Vector3d> place)
        {
            var random = new Random(cells);
            var triangles = new List<Triangle3d>();
            for (int y = 0; y < cells; ++y)
                for (int x = 0; x < cells; ++x) {
                    Vector3d a = place(new Vector3d(x, y, random.NextDouble())), b = place(new Vector3d(x + 1, y, random.NextDouble()));
                    Vector3d c = place(new Vector3d(x, y + 1, random.NextDouble())), d = place(new Vector3d(x + 1, y + 1, random.NextDouble()));
                    triangles.Add(new Triangle3d(a, b, d));
                    triangles.Add(new Triangle3d(a, d, c));
                }
            return triangles.ToArray();
        }

        static void AssertSameAnswers(TriangleQuadtree2d expected, TriangleQuadtree2d actual, AxisAlignedBox2d area, string name)
        {
            var random = new Random(9);
            var a = new List<QuadtreeHeightHit2d>();
            var b = new List<QuadtreeHeightHit2d>();
            Assert.Equal(expected.Extent, actual.Extent);
            for (int q = 0; q < 200; ++q) {
                var p = new Vector2d(area.Min.x + random.NextDouble() * area.Width, area.Min.y + random.NextDouble() * area.Height);
                expected.QueryHeights(p, a);
                actual.QueryHeights(p, b);
                Assert.True(a.Select(h => (h.Id, h.Height)).SequenceEqual(b.Select(h => (h.Id, h.Height))), name);
                Assert.Equal(expected.FindNearest(p), actual.FindNearest(p));
            }
        }

        [Fact]
        public void Over_AnswersLikeACopy_UnderEverySimilarity()
        {
            foreach (Similarity sim in Similarity.All()) {
                Triangle3d[] triangles = TriangleGrid(8, sim.Apply);
                var copy = new TriangleQuadtree2d(triangles);
                AssertSameAnswers(copy, TriangleQuadtree2d.Over(triangles), copy.Extent, sim.Name);
            }
        }

        [Fact]
        public void Over_AnswersAlike_BuiltInParallelOrInSequence()
        {
            Triangle3d[] triangles = TriangleGrid(200, v => v);
            Assert.True(triangles.Length >= LooseQuadtree2d.ParallelThreshold);
            TriangleQuadtree2d serial = TriangleQuadtree2d.Over(triangles, false);
            AssertSameAnswers(serial, TriangleQuadtree2d.Over(triangles, true), serial.Extent, "over");
            AssertSameAnswers(serial, new TriangleQuadtree2d(triangles, true), serial.Extent, "copy");
        }

        [Fact]
        public void ALargeBuild_ReportsABadTriangleLikeASmallOne()
        {
            var good = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            var bad = new Triangle3d(new Vector3d(double.NaN, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            Triangle3d[] triangles = Enumerable.Repeat(good, 2 * LooseQuadtree2d.ParallelThreshold).ToArray();
            // At the edges of the blocks that the bounds are computed in, too.
            int block = LooseQuadtree2d.ParallelThreshold;
            foreach (int at in new[] { 0, block - 1, block, triangles.Length - 1 }) {
                triangles[at] = bad;
                Assert.Throws<ArgumentException>(() => new TriangleQuadtree2d(triangles));
                Assert.Throws<ArgumentException>(() => TriangleQuadtree2d.Over(triangles));
                Assert.Throws<ArgumentException>(() => TriangleQuadtree2d.Over(triangles, false));
                triangles[at] = good;
            }
            Assert.Equal(triangles.Length, TriangleQuadtree2d.Over(triangles).Count);

            // The last one too gets its bounds.
            triangles[triangles.Length - 1] = new Triangle3d(new Vector3d(50, 50, 2), new Vector3d(52, 50, 2), new Vector3d(50, 52, 2));
            foreach (TriangleQuadtree2d tree in new[] { new TriangleQuadtree2d(triangles), TriangleQuadtree2d.Over(triangles) }) {
                Assert.Equal(TriangleQuadtree2d.Over(triangles, false).Extent, tree.Extent);
                Assert.True(tree.TryGetHeight(new Vector2d(50.5, 50.5), out double height, 0));
                Assert.Equal(2, height);
            }
        }

        [Fact]
        public void ParallelRegionSnapshotsAndRepeatedQueriesMatchSerialAfterMutation()
        {
            var boxes = Enumerable.Range(0, 40000).Select(i =>
                new AxisAlignedBox2d(i % 200, i / 200, i % 200 + 0.75, i / 200 + 0.75)).ToArray();
            var serial = new LooseQuadtree2d(boxes, false);
            var parallel = new LooseQuadtree2d(boxes, true);
            Assert.Equal(VisitRegions(serial), VisitRegions(parallel));
            foreach (var tree in new[] { serial, parallel }) {
                tree.Remove(1);
                tree.Insert(new AxisAlignedBox2d(new Vector2d(500, 500)));
                tree.Insert(new AxisAlignedBox2d(199.5, 199.5, 200, 200));
            }
            Parallel.For(0, 64, i => {
                var query = new AxisAlignedBox2d(i * 3, i * 2, i * 3 + 5, i * 2 + 4);
                int[] expected = Enumerable.Range(0, 40002).Where(id => parallel.ContainsId(id) && Overlap(parallel.GetBounds(id), query)).ToArray();
                var actual = new List<int>();
                for (int repeat = 0; repeat < 2; ++repeat) {
                    parallel.QueryRectangle(query, actual);
                    Assert.Equal(expected, actual.OrderBy(id => id));
                    serial.QueryRectangle(query, actual);
                    Assert.Equal(expected, actual.OrderBy(id => id));
                }
            });
        }

        [Fact]
        public void ExactHeightCandidatePruningPreservesSimilarityAndReuse()
        {
            var original = new List<Triangle3d>();
            for (int y = 0; y < 12; ++y) {
                for (int x = 0; x < 12; ++x) {
                    var a = new Vector3d(x, y, 2 * x + 3 * y);
                    var b = new Vector3d(x + 1, y, 2 * x + 3 * y + 2);
                    var c = new Vector3d(x, y + 1, 2 * x + 3 * y + 3);
                    var d = new Vector3d(x + 1, y + 1, 2 * x + 3 * y + 5);
                    original.Add(new Triangle3d(a, b, c));
                    original.Add(new Triangle3d(b, d, c));
                }
            }
            original.Add(new Triangle3d(Vector3d.Zero, Vector3d.Zero, Vector3d.AxisX));
            foreach (Similarity similarity in Similarity.All()) {
                var triangles = original.Select(t => new Triangle3d(similarity.Apply(t.V0), similarity.Apply(t.V1), similarity.Apply(t.V2))).ToArray();
                var tree = new TriangleQuadtree2d(triangles);
                var hits = new List<QuadtreeHeightHit2d>();
                for (int i = 0; i < 24; ++i) {
                    var p = new Vector3d(i % 12 + 0.25, i / 12 + 0.375, 2 * (i % 12 + 0.25) + 3 * (i / 12 + 0.375));
                    Vector3d expected = similarity.Apply(p);
                    var query = new Vector2d(expected.x, expected.y);
                    for (int repeat = 0; repeat < 2; ++repeat) {
                        Assert.True(tree.TryGetHeight(query, out double actual, 0));
                        Assert.False(double.IsNaN(actual) || double.IsInfinity(actual));
                        if (similarity.IsExact)
                            Assert.Equal(BitConverter.DoubleToInt64Bits(expected.z), BitConverter.DoubleToInt64Bits(actual));
                        else
                            Assert.True(Math.Abs(actual - expected.z) <= Math.Max(1e-9, 8 * similarity.Resolution(64)), similarity.Name);
                        tree.QueryHeights(query, hits, 0);
                        Assert.Single(hits);
                        Assert.Equal(i * 2, hits[0].Id);
                        Assert.Equal(actual, hits[0].Height);
                    }
                }
                Assert.False(tree.TryGetHeight(new Vector2d(double.MaxValue, double.MaxValue), out _, 0));
            }
        }

        [Fact]
        public void ExactHeightPruningKeepsClosedVerticesAndToleranceFallback()
        {
            var triangle = new Triangle3d(new Vector3d(0, 0, 1), new Vector3d(4, 0, 9), new Vector3d(0, 4, 13));
            var tree = new TriangleQuadtree2d(new[] { triangle, new Triangle3d(Vector3d.Zero, Vector3d.Zero, Vector3d.AxisX) });
            foreach (var vertex in new[] { triangle.V0, triangle.V1, triangle.V2 }) {
                Assert.True(tree.TryGetHeight(new Vector2d(vertex.x, vertex.y), out double height, 0));
                Assert.Equal(vertex.z, height);
            }
            var hits = new List<QuadtreeHeightHit2d>();
            var near = new Vector2d(-0.01, 1);
            Assert.False(tree.TryGetHeight(near, out _, 0));
            Assert.True(tree.TryGetHeight(near, out double atBoundary, 0.02));
            tree.QueryHeights(near, hits, 0.02);
            Assert.Single(hits);
            Assert.Equal(0, hits[0].Id);
            Assert.Equal(atBoundary, hits[0].Height);
            Assert.Equal(4, atBoundary, 12); // at (0, 1)
        }
    }
}
