using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The last stages of ExactMeshArrangement.Orient and InCircle (points beyond the fixed width) run on stack limbs
    /// (ExactLimbSigns.Orient2, InCircle). Through the library's existing entry points only, so that the tests also
    /// build against the base library: with every exact point kept as BigIntegers (FixedWidthPoints off) and the
    /// filters off, every Orient and InCircle of the triangulation on a constructed point takes that stage; the result
    /// must be the default one, and the operation must allocate far less than with BigInteger determinants.
    /// </summary>
    public class ExactBooleanWidePointTests
    {
        static DMesh3 Input(string kind)
        {
            if (kind.StartsWith("sphere"))
                return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = int.Parse(kind.Substring(6)) }.Generate().MakeDMesh();
            return ExactBooleanLimbSignsTests.Shape(kind);
        }

        // wide: all exact points as BigIntegers and no filters; the parallel loops on this thread if serial (so that the
        // thread's allocation counter sees the whole operation and nothing of the tests that run beside this one)
        static long Run(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations operation, bool wide, bool serial, out ExactMeshBoolean op)
        {
            bool before = gParallel.SerialOnThisThread;
            gParallel.SerialOnThisThread = serial;
            try {
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                op = new ExactMeshBoolean(a, b, operation) { AllowOpenMeshes = true };
                if (wide) { op.FixedWidthPoints = false; op.DisableFilters = true; }
                op.Compute();
                return GC.GetAllocatedBytesForCurrentThread() - bytes;
            } finally {
                gParallel.SerialOnThisThread = before;
            }
        }

        static List<string> Triangles(DMesh3 m) { return ExactBooleanLimbSignsTests.Triangles(m); }

        // Measured (one thread, .NET 10): 7 / 11 / 28 / 17 MB with the limb stages, 20 / 32 / 73 / 36 MB with the BigInteger
        // determinants. The
        // bound is on the allocation of the operation, which does not depend on the machine's speed; it is checked
        // where the library has the limb stages (net10.0, not the netstandard2.0 build under net48).
        [Theory]
        [InlineData("sphere8", 12)]
        [InlineData("sphere12", 20)]
        [InlineData("sheets4", 45)]
        [InlineData("sheets3", 25)]
        public void WidePoints_SameResult_WithoutBigIntegerDeterminants(string kind, int megabytes)
        {
            DMesh3 a = Input(kind), b = ExactBooleanLimbSignsTests.Copy(a);
            ExactMeshBoolean normal, wide;
            Run(a, b, ExactMeshBoolean.Operations.Union, false, true, out normal);
            Run(a, b, ExactMeshBoolean.Operations.Union, true, true, out wide);        // (compiles the path)
            long bytes = Run(a, b, ExactMeshBoolean.Operations.Union, true, true, out wide);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, normal.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, wide.Status);
            Assert.True(normal.Result.TriangleCount > 300, $"{normal.Result.TriangleCount} triangles");
            // every Orient and InCircle on constructed points by the last stage: the default mesh, triangle for triangle
            Assert.Equal(Triangles(normal.Result), Triangles(wide.Result));
#if NET
            Assert.True(bytes < megabytes * 1048576L, $"{kind}: {bytes / 1048576.0:0.0} MB allocated with wide points and the filters off, bound {megabytes} MB");
#else
            Assert.True(bytes > 0 && megabytes > 0);
#endif
        }

        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Intersection)]
        [InlineData(ExactMeshBoolean.Operations.Difference)]
        public void WidePoints_SameResult_OtherOperations(ExactMeshBoolean.Operations operation)
        {
            DMesh3 a = Input("sphere6"), b = ExactBooleanLimbSignsTests.Copy(a);
            ExactMeshBoolean normal, wide;
            Run(a, b, operation, false, false, out normal);
            Run(a, b, operation, true, false, out wide);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, normal.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, wide.Status);
            Assert.Equal(Triangles(normal.Result), Triangles(wide.Result));
            ResultChecks.AssertClosed(wide.Result);
        }

        // Under every similarity the wide stages give the default result of the same transformed input, and under the
        // exact ones (the translations by integers and the 2^k scales) the result of the untransformed input.
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void WidePoints_SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 a = Input("sphere5"), b = ExactBooleanLimbSignsTests.Copy(a);
            ExactMeshBoolean wide0, normal, wide;
            Run(a, b, ExactMeshBoolean.Operations.Union, true, false, out wide0);
            DMesh3 ta = Invariance.Transformed(a, s), tb = Invariance.Transformed(b, s);
            Run(ta, tb, ExactMeshBoolean.Operations.Union, false, false, out normal);
            Run(ta, tb, ExactMeshBoolean.Operations.Union, true, false, out wide);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, wide0.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, normal.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, wide.Status);
            Assert.Equal(Triangles(normal.Result), Triangles(wide.Result));
            ResultChecks.AssertClosed(wide.Result);
            if (s.IsExact)
                Assert.Equal("", Invariance.GeometricDifference(wide0.Result, Invariance.MappedBack(wide.Result, s)));
        }

        // Compute twice on one instance, and with the parallel loops serial: the same mesh.
        [Fact]
        public void WidePoints_ReusedInstance_AndSerial_SameResult()
        {
            DMesh3 a = Input("sphere6"), b = ExactBooleanLimbSignsTests.Copy(a);
            var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { FixedWidthPoints = false, DisableFilters = true };
            Assert.True(op.Compute(), op.Error);
            List<string> first = Triangles(op.Result);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(first, Triangles(op.Result));
            for (int run = 0; run < 3; ++run) {
                ExactMeshBoolean other;
                Run(a, b, ExactMeshBoolean.Operations.Union, true, run == 0, out other);
                Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, other.Status);
                Assert.Equal(first, Triangles(other.Result));
            }
        }
    }
}
