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

        static AxisAlignedBox2d Loose(AxisAlignedBox2d box)
        {
            double x = box.Max.x * 0.5 - box.Min.x * 0.5;
            double y = box.Max.y * 0.5 - box.Min.y * 0.5;
            return new AxisAlignedBox2d(
                Math.Max(-double.MaxValue, box.Min.x - x), Math.Max(-double.MaxValue, box.Min.y - y),
                Math.Min(double.MaxValue, box.Max.x + x), Math.Min(double.MaxValue, box.Max.y + y));
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

        // A bulk build roots at the extent. Growing leaves room beyond it, so that sorted inserts do not rebuild each time.
        static void AssertRootCoversExtent(LooseQuadtree2d tree)
        {
            AxisAlignedBox2d root = VisitRegions(tree)[0], extent = Loose(tree.Extent);
            Assert.True(root.Min.x <= extent.Min.x && root.Min.y <= extent.Min.y && root.Max.x >= extent.Max.x && root.Max.y >= extent.Max.y);
        }

        [Fact]
        public void LooseRegionsRemainIdenticalAfterSplitRemovalAndExtentRebuild()
        {
            var tree = new LooseQuadtree2d(new[] { new AxisAlignedBox2d(0, 0, 8, 8) }, false);
            Assert.Equal(Loose(tree.Extent), VisitRegions(tree)[0]);
            for (int i = 0; i < 80; ++i)
                tree.Insert(new AxisAlignedBox2d(i % 8, i / 8, i % 8 + 0.5, i / 8 + 0.5));
            Assert.True(VisitRegions(tree).Count > 1);
            AssertRootCoversExtent(tree);
            Assert.True(tree.Remove(0));
            var before = VisitRegions(tree);
            tree.Insert(new AxisAlignedBox2d(new Vector2d(1, 1)));
            Assert.Empty(before.Except(VisitRegions(tree))); // an insert inside may split a full cell, but moves none
            tree.Insert(new AxisAlignedBox2d(new Vector2d(-1000, 2000)));
            AssertRootCoversExtent(tree);
            tree.Insert(new AxisAlignedBox2d(new Vector2d(-double.MaxValue, -double.MaxValue)));
            tree.Insert(new AxisAlignedBox2d(new Vector2d(double.MaxValue, double.MaxValue)));
            AssertRootCoversExtent(tree);
            var result = new List<int>();
            foreach (var p in new[] { new Vector2d(-1000, 2000), new Vector2d(-double.MaxValue, -double.MaxValue), new Vector2d(double.MaxValue, double.MaxValue) }) {
                tree.QueryRectangle(new AxisAlignedBox2d(p), result);
                Assert.NotEmpty(result);
                Assert.All(result, id => Assert.True(Overlap(tree.GetBounds(id), new AxisAlignedBox2d(p))));
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
