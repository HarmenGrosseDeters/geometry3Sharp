using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The fast-winding cache of DMeshAABBTree3 is built with the two subtrees of the top three levels concurrently,
    /// and ExactMeshBoolean builds the trees of its open meshes concurrently (Lévy classification phase 2,
    /// docs/design/LEVY_CLASSIFICATION.md 4.1). No floating-point sum crosses threads, so both must give what the serial
    /// build gives, bit for bit, on every run.
    /// </summary>
    public class FastWindingParallelBuildTests
    {
        // a sphere of about 22000 triangles (deep enough for the parallel levels) with a cap removed: open
        static DMesh3 OpenSphere(Vector3d c, double capZ)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 44 }.Generate().MakeDMesh();
            foreach (int t in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(t).z > capZ)
                    m.RemoveTriangle(t, true, false);
            MeshTransforms.Translate(m, c);
            return m;
        }

        static double[] Winding(DMesh3 m, Vector3d[] points)
        {
            var tree = new DMeshAABBTree3(m, true);
            return points.Select(p => tree.FastWindingNumber(p)).ToArray();
        }

        static Vector3d[] Points()
        {
            var rnd = new Random(7);
            return Enumerable.Range(0, 300).Select(_ => new Vector3d(3 * rnd.NextDouble() - 1.5, 3 * rnd.NextDouble() - 1.5, 3 * rnd.NextDouble() - 1.5)).ToArray();
        }

        static void Serial(Action a)
        {
            gParallel.SerialOnThisThread = true;
            try {
                a();
            } finally {
                gParallel.SerialOnThisThread = false;
            }
        }

        // the plain recursion of the cache build (no parallel levels), on this thread: the reference (other tests that
        // build a cache meanwhile get the same cache, only built without the parallel levels)
        static void PlainRecursion(Action a)
        {
            int depth = DMeshAABBTree3.FastWindingParallelDepth;
            DMeshAABBTree3.FastWindingParallelDepth = 0;
            try {
                Serial(a);
            } finally {
                DMeshAABBTree3.FastWindingParallelDepth = depth;
            }
        }

        [Fact]
        public void FastWindingNumber_ParallelBuildEqualsSerial()
        {
            DMesh3 m = OpenSphere(Vector3d.Zero, 0.6);
            Assert.False(m.IsClosed());
            Assert.True(m.TriangleCount > 15000);
            Vector3d[] p = Points();
            double[] serial = null;
            PlainRecursion(() => serial = Winding(m, p));
            // the cache is used: points inside and outside the open sphere are told apart
            Assert.Contains(serial, w => w > 0.9);
            Assert.Contains(serial, w => Math.Abs(w) < 0.1);
            for (int run = 0; run < 3; ++run) {
                double[] parallel = Winding(m, p);
                for (int i = 0; i < p.Length; ++i)
                    Assert.True(BitConverter.DoubleToInt64Bits(serial[i]) == BitConverter.DoubleToInt64Bits(parallel[i]), $"run {run}, point {i}: {serial[i]:R} vs {parallel[i]:R}");
            }
        }

        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Union)]
        [InlineData(ExactMeshBoolean.Operations.Intersection)]
        [InlineData(ExactMeshBoolean.Operations.Difference)]
        public void OpenInputs_ParallelEqualsSerial_Repeated(ExactMeshBoolean.Operations op)
        {
            // both inputs open: two winding trees, built concurrently
            DMesh3 a = OpenSphere(Vector3d.Zero, 0.6), b = OpenSphere(new Vector3d(0.6, 0.3, 0.1), 0.5);
            ExactMeshBoolean serial = null;
            PlainRecursion(() => { serial = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op); serial.Compute(); });
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, serial.Status);
            // (the intersection of these two open spheres comes out empty, on master too: reported separately)
            if (op != ExactMeshBoolean.Operations.Intersection)
                Assert.True(serial.Result.TriangleCount > 0);
            for (int run = 0; run < 2; ++run) {
                var parallel = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
                parallel.Compute();
                Assert.Equal("", Invariance.Difference(serial.Result, parallel.Result));
            }
            // the same instance again (the trees of the first Compute are not reused for other meshes)
            var again = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
            again.Compute();
            again.Compute();
            Assert.Equal("", Invariance.Difference(serial.Result, again.Result));
        }
    }
}
