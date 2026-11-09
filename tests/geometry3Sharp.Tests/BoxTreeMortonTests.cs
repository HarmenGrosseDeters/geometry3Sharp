using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TriangleBoxTree orders the boxes along a Morton curve of their centres and splits every range in the middle. The
    /// order only decides which boxes share a node: a query must find exactly the boxes whose closed box overlaps it,
    /// and the self join exactly the overlapping pairs, for any input, including touching and empty boxes, equal
    /// centres, coordinates far apart in magnitude and coordinates that are not finite (the quantization of the curve
    /// must not depend on them). The Boolean results stay the same (ExactBoolean*Tests, the cohort hashes).
    /// </summary>
    public class BoxTreeMortonTests
    {
        static bool closed_overlap(AxisAlignedBox3d a, AxisAlignedBox3d b)
        {
            return !(a.Max.x < b.Min.x || a.Min.x > b.Max.x || a.Max.y < b.Min.y || a.Min.y > b.Max.y || a.Max.z < b.Min.z || a.Min.z > b.Max.z);
        }

        static List<AxisAlignedBox3d> Boxes(int n, int seed, string kind)
        {
            var rand = new Random(seed);
            var boxes = new List<AxisAlignedBox3d>();
            for (int i = 0; i < n; ++i) {
                Vector3d lo, size;
                switch (kind) {
                    case "grid":        // integer boxes: many touch, many share a centre, some are points
                        lo = new Vector3d(rand.Next(0, 12), rand.Next(0, 12), rand.Next(0, 12));
                        size = new Vector3d(rand.Next(0, 3), rand.Next(0, 3), rand.Next(0, 3));
                        break;
                    case "slivers":     // long thin boxes across the scene, and tiny ones
                        lo = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                        size = (i % 3 == 0) ? new Vector3d(rand.NextDouble(), 1e-9, 1e-9) : new Vector3d(1e-7, 1e-7, 1e-7);
                        break;
                    default:            // magnitudes from 1e-300 to 1e300
                        double m = Math.Pow(10, rand.Next(-300, 301));
                        lo = m * new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5);
                        size = m * new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                        break;
                }
                boxes.Add(new AxisAlignedBox3d(lo, lo + size));
            }
            return boxes;
        }

        static void AssertExact(List<AxisAlignedBox3d> boxes, int seed)
        {
            var tree = new TriangleBoxTree(boxes);
            var rand = new Random(seed);
            // queries: the boxes themselves (touching included) and random boxes
            var queries = new List<AxisAlignedBox3d>(boxes.Take(200));
            for (int q = 0; q < 50 && boxes.Count > 0; ++q) {
                AxisAlignedBox3d a = boxes[rand.Next(boxes.Count)], b = boxes[rand.Next(boxes.Count)];
                var box = new AxisAlignedBox3d(a.Min, a.Min);
                box.Contain(b.Max);
                queries.Add(box);
            }
            foreach (AxisAlignedBox3d query in queries) {
                var got = new List<int>();
                tree.Query(query, got);
                var expected = Enumerable.Range(0, boxes.Count).Where(i => closed_overlap(boxes[i], query)).ToList();
                Assert.Equal(expected, got.OrderBy(x => x).ToList());
            }
            var pairs = new List<(int, int)>();
            foreach (Index2i t in tree.SelfPairTasks(8)) {
                var list = new List<Index2i>();
                tree.SelfPairs(t, list);
                pairs.AddRange(list.Select(p => (Math.Min(p.a, p.b), Math.Max(p.a, p.b))));
            }
            var expectedPairs = new List<(int, int)>();
            for (int i = 0; i < boxes.Count; ++i)
                for (int j = i + 1; j < boxes.Count; ++j)
                    if (closed_overlap(boxes[i], boxes[j]))
                        expectedPairs.Add((i, j));
            Assert.Equal(expectedPairs, pairs.OrderBy(x => x).ToList());
        }

        [Theory]
        [InlineData("grid", 0)]
        [InlineData("grid", 9)]
        [InlineData("grid", 64)]
        [InlineData("grid", 1500)]
        [InlineData("slivers", 1500)]
        [InlineData("magnitudes", 1500)]
        [InlineData("grid", 6000)]
        [InlineData("slivers", 6000)]
        [InlineData("magnitudes", 6000)]
        public void QueriesAndPairs_AsAScan(string kind, int n)
        {
            for (int seed = 1; seed <= 3; ++seed)
                AssertExact(Boxes(n, seed, kind), seed);
        }

        // groups of 16 boxes, each a point cluster of 8 and 8 points at x offsets 2^-1 .. 2^-8 from it: along the Morton
        // curve every highest-bit split of a group peels one point off, unless each side must hold some (the min-side rule)
        static List<AxisAlignedBox3d> Peeling(int groups)
        {
            var boxes = new List<AxisAlignedBox3d>();
            int side = (int)Math.Ceiling(Math.Pow(groups, 1.0 / 3));
            for (int g = 0; g < groups; ++g) {
                var o = new Vector3d(g % side, (g / side) % side, g / (side * side));
                for (int k = 0; k < 8; ++k)
                    boxes.Add(new AxisAlignedBox3d(o, o));
                for (int j = 1; j <= 8; ++j) {
                    Vector3d p = o + new Vector3d(Math.Pow(2, -j), 0, 0);
                    boxes.Add(new AxisAlignedBox3d(p, p));
                }
            }
            return boxes;
        }

        [Theory]
        [InlineData(400)]
        [InlineData(12500)]
        public void SkewedInput_NodesAndDepthWithinTheBounds(int groups)
        {
            // the nodes fit the capacity (at most n / 2 - 1) and the depth stays below log(n / 4) / log(8 / 7) + 1, the
            // bound the query stack (256) is sized for; and the queries are a scan's
            List<AxisAlignedBox3d> boxes = Peeling(groups);
            int n = boxes.Count;
            var tree = new TriangleBoxTree(boxes);
            Assert.True(tree.NodeCount <= n / 2 - 1 && tree.NodeCount <= tree.NodeCapacity, $"{tree.NodeCount} nodes for {n} boxes");
            double bound = Math.Log(n / 4.0) / Math.Log(8.0 / 7) + 1;
            Assert.True(tree.Depth() <= bound, $"depth {tree.Depth()}, bound {bound:F1}");
            var rand = new Random(3);
            for (int q = 0; q < 50; ++q) {
                AxisAlignedBox3d query = boxes[rand.Next(n)];
                query.Expand(0.3);
                var got = new List<int>();
                tree.Query(query, got);
                var expected = Enumerable.Range(0, n).Where(i => closed_overlap(boxes[i], query)).ToList();
                Assert.Equal(expected, got.OrderBy(x => x).ToList());
            }
        }

        [Fact]
        public void NotFiniteCoordinates_NoEffectOnTheOthers()
        {
            // boxes with infinite coordinates: their centres are not finite (or NaN); the order still is a permutation and
            // every query of finite boxes finds what a scan finds
            List<AxisAlignedBox3d> boxes = Boxes(300, 5, "grid");
            boxes.Add(new AxisAlignedBox3d(new Vector3d(double.NegativeInfinity, 0, 0), new Vector3d(double.PositiveInfinity, 1, 1)));
            boxes.Add(new AxisAlignedBox3d(new Vector3d(0, 0, 0), new Vector3d(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity)));
            boxes.Add(new AxisAlignedBox3d(new Vector3d(-1e308, -1e308, -1e308), new Vector3d(1e308, 1e308, 1e308)));
            var tree = new TriangleBoxTree(boxes);
            for (int i = 0; i < 300; ++i) {
                var got = new List<int>();
                tree.Query(boxes[i], got);
                var expected = Enumerable.Range(0, boxes.Count).Where(k => closed_overlap(boxes[k], boxes[i])).ToList();
                Assert.Equal(expected, got.OrderBy(x => x).ToList());
            }
        }

        [Fact]
        public void SameInput_SameOrderOfResults()
        {
            // deterministic: two trees of one input answer a query in the same order
            List<AxisAlignedBox3d> boxes = Boxes(2000, 7, "grid");
            var t1 = new TriangleBoxTree(boxes);
            var t2 = new TriangleBoxTree(new List<AxisAlignedBox3d>(boxes));
            foreach (AxisAlignedBox3d q in boxes.Take(100)) {
                var a = new List<int>();
                var b = new List<int>();
                t1.Query(q, a);
                t2.Query(q, b);
                Assert.Equal(a, b);
            }
        }
    }
}
