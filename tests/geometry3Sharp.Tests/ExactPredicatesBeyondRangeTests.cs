using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactPredicates on coordinates outside the range of the expansions ([2^-100, 2^100]), where the exact stage was a
    /// BigInteger determinant and is now the generated kernels of DoublePredicates (docs/design/
    /// EXACT_FALLBACK_MONOMIALS.md), through the library's existing entry points only: the stage allocates nothing, and
    /// an input of issue #711 no longer spends its time there. (The kernels themselves: GeneratedDoublePredicatesTests.)
    /// </summary>
    public class ExactPredicatesBeyondRangeTests
    {
        // m 2^e (Math.ScaleB is not in .NET Framework)
        static double Scale(double m, int e)
        {
            while (e > 1000) { m *= BitConverter.Int64BitsToDouble((long)(1000 + 1023) << 52); e -= 1000; }
            while (e < -1000) { m *= BitConverter.Int64BitsToDouble((long)(-1000 + 1023) << 52); e += 1000; }
            return m * BitConverter.Int64BitsToDouble((long)(e + 1023) << 52);
        }

        // the pattern of issue #711: small or random mantissas near 2^-943, 2^-106 and 2^-100
        static double[] ThreeScales(int n, Random rnd)
        {
            int[] scales = { -943, -106, -100 };
            double[] v = new double[n];
            for (int k = 0; k < n; ++k) {
                double m = (rnd.Next(2) == 0) ? rnd.Next(-3, 4) : (rnd.NextDouble() + 1) * (rnd.Next(2) * 2 - 1);
                v[k] = Scale(m, scales[rnd.Next(3)]);
            }
            return v;
        }

        // The fallback allocates nothing (the BigInteger stage took 1.4 to 4.6 KB per call). Inputs of the pattern of
        // issue #711, whose filters do not decide and whose coordinates are outside the range of the expansions.
        [Fact]
        public void ExactPredicates_BeyondTheExpansionRange_DoNotAllocate()
        {
            var rnd = new Random(9);
            var cases3 = new List<Vector3d[]>(); var cases2 = new List<Vector2d[]>();
            while (cases3.Count < 200) {
                double[] v = ThreeScales(12, rnd);
                var pts = new[] { new Vector3d(v[0], v[1], v[2]), new Vector3d(v[3], v[4], v[5]), new Vector3d(v[6], v[7], v[8]), new Vector3d(v[9], v[10], v[11]) };
                if (pts.Distinct().Count() == 4) cases3.Add(pts);
            }
            while (cases2.Count < 200) {
                double[] v = ThreeScales(8, rnd);
                var pts = new[] { new Vector2d(v[0], v[1]), new Vector2d(v[2], v[3]), new Vector2d(v[4], v[5]), new Vector2d(v[6], v[7]) };
                if (pts.Distinct().Count() == 4) cases2.Add(pts);
            }
            Func<int> run = () => {
                int s = 0;
                foreach (var p in cases3) s += ExactPredicates.Orient3dExact(p[0], p[1], p[2], p[3]);
                foreach (var p in cases2) s += ExactPredicates.Orient2dExact(p[0], p[1], p[2]) + ExactPredicates.InCircleExact(p[0], p[1], p[2], p[3]);
                return s;
            };
            int first = run();      // (compiles the path)
            long before = GC.GetAllocatedBytesForCurrentThread();
            int second = run();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(first, second);
            Assert.Equal(0, allocated);
        }

        // slow-unit-89209633... of the campaign grp-a (runs/mesh-fuzz; union of a 16-vertex and a 6-vertex shape, decoded
        // by MeshFuzzPilot dump): corners near 1e-303 and 1e-226 next to coordinates near 1e-34 and 1e-31, a span of
        // about 2^900 inside single predicates. With the BigInteger stage the operation allocated 1.9 GB (2.5 s); with the
        // kernels 43 MB (0.34 s). The bound is on the allocation of the operation, which does not depend on the machine's
        // speed.
        [Fact]
        public void Issue711_FuzzUnit_UnionWithoutBigIntegerPredicates()
        {
            double[,] av = {
                { 0.0, 0.0, 0.0 },
                { 2.407412430484045E-35, 0.0, 0.0 },
                { 1.2789378536946488E-34, 1.2789378536946488E-34, 1.0381966106462443E-34 },
                { 1.0381966106462443E-34, 1.2789378536946488E-34, 1.0381966106462443E-34 },
                { 2.1064858766735392E-35, 1.0381966106462443E-34, 1.2789378536946488E-34 },
                { 1.2789378536946488E-34, 1.0381966106462443E-34, 1.2789378536946488E-34 },
                { 1.2789378536946488E-34, 1.2789378536946488E-34, 1.2789378536946488E-34 },
                { 1.0381966106462443E-34, 2.407412430484045E-35, 2.407412430484045E-35 },
                { 1.2037062152420224E-35, 1.2037062152420224E-35, 1.2037062152420224E-35 },
                { 3.611118645726067E-35, 1.2037062152420224E-35, 1.2037062152420224E-35 },
                { 7.787758504775205E-308, 0.0, 3.098726858311469E-303 },
                { 1.2037062152420224E-35, 3.611118645726067E-35, 1.2037062152420224E-35 },
                { 1.2037062152420224E-35, 1.2037062152420224E-35, 3.611118645726067E-35 },
                { 3.611118645726067E-35, 1.2037062152420224E-35, 3.611118645726067E-35 },
                { 3.611118645726067E-35, 3.611118645726067E-35, 3.611118645726067E-35 },
                { 1.2037062152420224E-35, 3.611118645726067E-35, 3.611118645726067E-35 } };
            int[,] at = { { 0, 2, 1 }, { 0, 3, 2 }, { 4, 5, 6 }, { 4, 6, 7 }, { 0, 1, 5 }, { 0, 5, 4 }, { 1, 2, 6 }, { 1, 6, 5 }, { 2, 3, 7 }, { 2, 7, 6 }, { 3, 0, 4 }, { 3, 4, 7 }, { 8, 10, 9 }, { 8, 11, 10 }, { 12, 13, 14 }, { 12, 14, 15 }, { 8, 9, 13 }, { 8, 13, 12 }, { 9, 10, 14 }, { 9, 14, 13 }, { 10, 11, 15 }, { 10, 15, 14 }, { 11, 8, 12 }, { 11, 12, 15 } };
            double[,] bv = {
                { 4.319800679949808E-32, 9.307658309358938E-33, 2.1263470292250326E-31 },
                { 4.307462691243577E-32, 9.181269156758526E-33, 2.126076195326603E-31 },
                { 3.324251688544512E-280, 3.83046299398303E-274, -1.1087052984336745E-226 },
                { 4.3140830754274083E-32, 9.196315484449051E-33, 2.1262266586035084E-31 },
                { 4.3140830754274083E-32, 9.244463733058732E-33, 2.126708141089605E-31 },
                { 4.3140830754274083E-32, 9.244463733058732E-33, 2.1257451761174116E-31 } };
            int[,] bt = { { 0, 2, 4 }, { 2, 1, 4 }, { 1, 3, 4 }, { 3, 0, 4 }, { 2, 0, 5 }, { 1, 2, 5 }, { 3, 1, 5 }, { 0, 3, 5 } };
            DMesh3 a = Build(av, at), b = Build(bv, bt);
            new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union).Compute();      // (compiles the path)
            // (the parallel loops on this thread, so that the thread's allocation counter sees the whole operation and
            // nothing of the tests that run beside this one)
            bool serial = gParallel.SerialOnThisThread;
            gParallel.SerialOnThisThread = true;
            try {
                long before = GC.GetAllocatedBytesForCurrentThread();
                var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
                Assert.True(op.Compute());
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
                Assert.True(op.Result.TriangleCount > 0 && op.Result.IsClosed());
                foreach (Vector3d p in op.Result.Vertices())
                    Assert.True(p.IsFinite);
                Assert.True(allocated < 400L << 20, $"allocated {allocated >> 20} MB: the predicates of the rounding went to BigInteger");
            } finally {
                gParallel.SerialOnThisThread = serial;
            }
        }

        static DMesh3 Build(double[,] v, int[,] t)
        {
            var m = new DMesh3();
            for (int i = 0; i < v.GetLength(0); ++i) m.AppendVertex(new Vector3d(v[i, 0], v[i, 1], v[i, 2]));
            for (int i = 0; i < t.GetLength(0); ++i) m.AppendTriangle(t[i, 0], t[i, 1], t[i, 2]);
            return m;
        }
    }
}
