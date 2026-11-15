using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The default of TriangleBoxTree.ParallelBuildFrom: trees from that many boxes on are built in parallel (the input
    /// trees of the arrangement and SafeRounding's tree of the result, which for typical operands of 1k-100k triangles are
    /// built serially above 262144 boxes only). The parallel tree is the serial tree node for node
    /// (BoxTreeParallelBuildTests), so the default decides speed only: these tests pin the default and its boundary, and check
    /// that ExactMeshBoolean results with trees above it are the same with the parallel and the serial build, on one worker and
    /// on many, in repeated runs, and under the 2^k scales (Similarity.All(): exact scaling gives the same result bit for bit).
    /// </summary>
    [Collection("BoxTreeOrder")]
    public class BoxTreeThresholdTests
    {
        static List<AxisAlignedBox3d> Boxes(int n, int seed)
        {
            var rand = new Random(seed);
            var boxes = new List<AxisAlignedBox3d>();
            for (int i = 0; i < n; ++i) {
                var lo = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                boxes.Add(new AxisAlignedBox3d(lo, lo + new Vector3d(1e-2, 1e-2, 1e-2) * rand.NextDouble()));
            }
            return boxes;
        }

        static TriangleBoxTree Build(List<AxisAlignedBox3d> boxes, int parallelFrom)
        {
            int saved = TriangleBoxTree.ParallelBuildFrom;
            TriangleBoxTree.ParallelBuildFrom = parallelFrom;
            try { return new TriangleBoxTree(boxes); }
            finally { TriangleBoxTree.ParallelBuildFrom = saved; }
        }

        [Fact]
        public void Default_IsAboveTheMedianSplitAndNotAboveTheTypicalOperand()
        {
            Assert.Equal(TriangleBoxTree.DefaultParallelBuildFrom, TriangleBoxTree.ParallelBuildFrom);
            // (the median split, up to 4096 boxes, is serial and cheap: the default must not reach into it)
            Assert.True(TriangleBoxTree.DefaultParallelBuildFrom > TriangleBoxTree.MedianSplitLimit);
            // (the point of the default: operands of 10k-100k triangles build their trees in parallel)
            Assert.True(TriangleBoxTree.DefaultParallelBuildFrom <= 8192);
        }

        [Fact]
        public void Boundary_BelowAtAndAboveTheDefault_ParallelFromTheDefaultOn()
        {
            int t = TriangleBoxTree.DefaultParallelBuildFrom;
            foreach (int n in new[] { t - 1, t, t + 1 }) {
                List<AxisAlignedBox3d> boxes = Boxes(n, 7);
                TriangleBoxTree.ParallelBuildsOnThisThread = 0;
                TriangleBoxTree tree = new TriangleBoxTree(boxes);       // the default threshold
                int parallel = TriangleBoxTree.ParallelBuildsOnThisThread;
                Assert.Equal(n >= t ? 1 : 0, parallel);
                Assert.True(tree.SameAs(Build(boxes, int.MaxValue)), "n = " + n);
            }
        }

        static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices)
        {
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(s, center);
            return s;
        }

        // every vertex and triangle id with its coordinates (bits), the vertices mapped back by the inverse of a power-of-two scale
        static string Fingerprint(DMesh3 m, double inverseScale = 1)
        {
            var sb = new StringBuilder();
            sb.Append(m.MaxVertexID).Append('/').Append(m.MaxTriangleID).Append('|');
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v) * inverseScale;
                sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                sb.Append(t).Append(':').Append(f.a).Append(',').Append(f.b).Append(',').Append(f.c).Append(';');
            }
            return sb.ToString();
        }

        // two overlapping spheres of more triangles than the default threshold each (so their input trees, and the rounding's tree of
        // the result, take the parallel build), scaled by s about the origin
        static string Run(ExactMeshBoolean.Operations op, double scale, int parallelFrom, bool serialWorkers = false)
        {
            DMesh3 a = Sphere(new Vector3d(0, 0, 0), 1, 40), b = Sphere(new Vector3d(0.4, 0.3, 0.2), 0.9, 40);
            Assert.True(a.TriangleCount > TriangleBoxTree.DefaultParallelBuildFrom);
            MeshTransforms.Scale(a, scale); MeshTransforms.Scale(b, scale);
            int saved = TriangleBoxTree.ParallelBuildFrom;
            TriangleBoxTree.ParallelBuildFrom = parallelFrom;
            gParallel.SerialOnThisThread = serialWorkers;
            try {
                var boolean = new ExactMeshBoolean(a, b, op);
                Assert.True(boolean.Compute(), boolean.Error);
                ResultChecks.AssertClosed(boolean.Result);
                Assert.True(boolean.RoundingClean);
                return Fingerprint(boolean.Result, 1.0 / scale);
            } finally {
                TriangleBoxTree.ParallelBuildFrom = saved;
                gParallel.SerialOnThisThread = false;
            }
        }

        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Union)]
        [InlineData(ExactMeshBoolean.Operations.Difference)]
        public void Boolean_ParallelTreesEqualSerialTrees_OnOneWorkerAndMany_AndRepeated(ExactMeshBoolean.Operations op)
        {
            int t = TriangleBoxTree.DefaultParallelBuildFrom;
            string serial = Run(op, 1, int.MaxValue);
            Assert.Equal(serial, Run(op, 1, t));
            Assert.Equal(serial, Run(op, 1, t, serialWorkers: true));
            for (int i = 0; i < 3; ++i)
                Assert.Equal(serial, Run(op, 1, t));
        }

        [Fact]
        public void Boolean_PowerOfTwoScales_GiveTheSameResultBitForBit_WithTheDefaultThreshold()
        {
            int t = TriangleBoxTree.DefaultParallelBuildFrom;
            foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Intersection }) {
                string identity = Run(op, 1, t);
                foreach (Similarity s in Similarity.All().Where(x => x.IsExact))
                    Assert.True(identity == Run(op, s.Scale, t), op + " at " + s);
            }
        }
    }
}
