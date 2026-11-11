using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TriangleBoxTree builds large trees in parallel (Morton codes per block, the sort of (code, box index) by runs
    /// sorted apart and merged, the subtrees below a fixed depth built apart and renumbered): the tree must be the
    /// same as the serial build's, node for node, whatever the schedule or the number of workers.
    /// </summary>
    [Collection("BoxTreeOrder")]
    public class BoxTreeParallelBuildTests
    {
        static List<AxisAlignedBox3d> Boxes(int n, int seed, string kind)
        {
            var rand = new Random(seed);
            var boxes = new List<AxisAlignedBox3d>();
            for (int i = 0; i < n; ++i) {
                Vector3d lo, size;
                switch (kind) {
                    case "grid":        // many equal centres and codes
                        lo = new Vector3d(rand.Next(0, 8), rand.Next(0, 8), rand.Next(0, 8));
                        size = new Vector3d(rand.Next(0, 3), rand.Next(0, 3), rand.Next(0, 3));
                        break;
                    case "same":        // all boxes equal
                        lo = new Vector3d(1, 2, 3);
                        size = new Vector3d(1, 1, 1);
                        break;
                    case "far":         // one far box squeezes the others (the rank order)
                        lo = i == 7 ? new Vector3d(1e12, 1e12, 1e12) : new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                        size = new Vector3d(1e-3, 1e-3, 1e-3);
                        break;
                    case "nonfinite":
                        lo = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                        size = new Vector3d(1e-3, 1e-3, 1e-3);
                        if (i % 997 == 0) lo.x = double.NaN;
                        if (i % 1013 == 0) size.y = double.PositiveInfinity;
                        break;
                    default:            // uniform
                        lo = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                        size = new Vector3d(1e-2, 1e-2, 1e-2) * rand.NextDouble();
                        break;
                }
                boxes.Add(new AxisAlignedBox3d(lo, lo + size));
            }
            return boxes;
        }

        static TriangleBoxTree Build(List<AxisAlignedBox3d> boxes, int parallelFrom, bool serialWorkers = false)
        {
            int saved = TriangleBoxTree.ParallelBuildFrom;
            TriangleBoxTree.ParallelBuildFrom = parallelFrom;
            gParallel.SerialOnThisThread = serialWorkers;
            try { return new TriangleBoxTree(boxes); }
            finally { TriangleBoxTree.ParallelBuildFrom = saved; gParallel.SerialOnThisThread = false; }
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (string kind in new[] { "uniform", "grid", "same", "far", "nonfinite" })
                foreach (int n in new[] { 5000, 13007 })
                    yield return new object[] { kind, n };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ParallelTree_SameAsSerialTree_NodeForNode(string kind, int n)
        {
            List<AxisAlignedBox3d> boxes = Boxes(n, 11, kind);
            TriangleBoxTree serial = Build(boxes, int.MaxValue);
            TriangleBoxTree parallel = Build(boxes, 1);
            Assert.True(serial.SameAs(parallel), kind);
            // the same on one worker (the schedule does not matter), and on repeated runs
            Assert.True(serial.SameAs(Build(boxes, 1, serialWorkers: true)), kind + " on one worker");
            for (int i = 0; i < 3; ++i)
                Assert.True(parallel.SameAs(Build(boxes, 1)), kind + " repeated");
            Assert.True(parallel.NodeCount <= parallel.NodeCapacity);
        }

        [Fact]
        public void ParallelTree_FindsExactlyTheOverlappingBoxes()
        {
            List<AxisAlignedBox3d> boxes = Boxes(9000, 5, "uniform");
            TriangleBoxTree tree = Build(boxes, 1);
            var rand = new Random(3);
            for (int q = 0; q < 40; ++q) {
                AxisAlignedBox3d query = boxes[rand.Next(boxes.Count)];
                query.Expand(0.02);
                var got = new List<int>();
                tree.Query(query, got);
                var expected = Enumerable.Range(0, boxes.Count).Where(i => !(boxes[i].Max.x < query.Min.x || boxes[i].Min.x > query.Max.x
                    || boxes[i].Max.y < query.Min.y || boxes[i].Min.y > query.Max.y || boxes[i].Max.z < query.Min.z || boxes[i].Min.z > query.Max.z)).ToList();
                Assert.Equal(expected, got.OrderBy(x => x).ToList());
            }
        }

        [Fact]
        public void ParallelTree_BelowTheThreshold_IsTheSerialBuild()
        {
            // 8 boxes, and the threshold exactly at n: the parallel path starts at n >= ParallelBuildFrom
            List<AxisAlignedBox3d> boxes = Boxes(5000, 2, "uniform");
            Assert.True(Build(boxes, 5000).SameAs(Build(boxes, int.MaxValue)));
            Assert.True(Build(boxes, 5001).SameAs(Build(boxes, int.MaxValue)));
        }
    }
}
