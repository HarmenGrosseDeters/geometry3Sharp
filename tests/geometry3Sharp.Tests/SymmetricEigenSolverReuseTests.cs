using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SymmetricEigenSolver is sized to what a solve needs (not to maxIterations) and can serve any number of solves
    /// (DualContouring / AdaptiveDualContouring keep one per worker): reuse equals fresh solvers bit for bit, a constructor
    /// and a repeated Solve allocate (almost) nothing, and the contours built with the reused solvers are bit for bit
    /// those of one fresh solver per QEF (pinned hashes recorded with the fresh solvers).
    /// </summary>
    public class SymmetricEigenSolverReuseTests
    {
        static double[] RandomSymmetric(Random rnd, int n, double scale)
        {
            var a = new double[n * n];
            for (int r = 0; r < n; ++r)
                for (int c = r; c < n; ++c)
                    a[r * n + c] = a[c * n + r] = (rnd.NextDouble() * 2 - 1) * scale;
            return a;
        }

        static long Bits(double d) { return BitConverter.DoubleToInt64Bits(d); }

        [Theory]
        [InlineData(3, 64, SymmetricEigenSolver.SortType.Decreasing)]
        [InlineData(3, 4096, SymmetricEigenSolver.SortType.Increasing)]
        [InlineData(4, 1024, SymmetricEigenSolver.SortType.Increasing)]
        [InlineData(6, 1024, SymmetricEigenSolver.SortType.NoSorting)]
        public void OneSolver_ManySolves_EqualsFreshSolvers_BitForBit(int n, int maxIter, SymmetricEigenSolver.SortType sort)
        {
            var rnd = new Random(12345 + n);
            var reused = new SymmetricEigenSolver(n, maxIter);
            for (int it = 0; it < 300; ++it) {
                // (every few solves a hard one, then easy ones: the state of a long solve must not leak into the next)
                double[] a = (it % 7 == 0) ? RandomSymmetric(rnd, n, 1e6) : RandomSymmetric(rnd, n, 1);
                if (it % 11 == 0) a = new double[n * n];   // zero matrix
                if (it % 13 == 0) for (int i = 0; i < n; ++i) a[i * n + i] = 5;   // (near) diagonal
                var fresh = new SymmetricEigenSolver(n, maxIter);
                int i1 = fresh.Solve((double[])a.Clone(), sort);
                int i2 = reused.Solve((double[])a.Clone(), sort);
                Assert.Equal(i1, i2);
                double[] e1 = fresh.GetEigenvalues(), e2 = reused.GetEigenvalues();
                for (int i = 0; i < n; ++i) Assert.Equal(Bits(e1[i]), Bits(e2[i]));
                for (int c = 0; c < n; ++c) {
                    double[] v1 = fresh.GetEigenvector(c), v2 = reused.GetEigenvector(c);
                    for (int i = 0; i < n; ++i) Assert.Equal(Bits(v1[i]), Bits(v2[i]));
                }
            }
        }

        [Fact]
        public void NoConvergenceSolve_DoesNotLeakIntoTheNextSolve()
        {
            var rnd = new Random(7);
            var reused = new SymmetricEigenSolver(5, 1);   // one iteration: converges only for easy matrices
            for (int it = 0; it < 50; ++it) {
                double[] a = RandomSymmetric(rnd, 5, 1);
                var fresh = new SymmetricEigenSolver(5, 1);
                Assert.Equal(fresh.Solve((double[])a.Clone(), SymmetricEigenSolver.SortType.Decreasing), reused.Solve((double[])a.Clone(), SymmetricEigenSolver.SortType.Decreasing));
                double[] e1 = fresh.GetEigenvalues(), e2 = reused.GetEigenvalues();
                for (int i = 0; i < 5; ++i) Assert.Equal(Bits(e1[i]), Bits(e2[i]));
            }
        }

        [Fact]
        public void Constructor_DoesNotAllocateForMaxIterations()
        {
            new SymmetricEigenSolver(3, 4096);   // (JIT)
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; ++i) new SymmetricEigenSolver(3, 4096);
            long perSolver = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
            Assert.True(perSolver < 2048, "bytes per solver: " + perSolver);   // was about 196 KB
        }

        [Fact]
        public void RepeatedSolve_AllocatesNothing()
        {
            var rnd = new Random(3);
            var solver = new SymmetricEigenSolver(3, 64);
            double[] a = RandomSymmetric(rnd, 3, 1), work = new double[9];
            for (int i = 0; i < 3; ++i) { Array.Copy(a, work, 9); solver.Solve(work, SymmetricEigenSolver.SortType.Decreasing); }   // (the list grows once)
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; ++i) { Array.Copy(a, work, 9); solver.Solve(work, SymmetricEigenSolver.SortType.Decreasing); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // ---- the contours built with per-worker solvers

        class Fn : ImplicitFunction3d
        {
            readonly Func<Vector3d, double> f;
            public Fn(Func<Vector3d, double> f) { this.f = f; }
            public double Value(ref Vector3d p) { return f(p); }
            public AxisAlignedBox3d Bounds() { return AxisAlignedBox3d.Empty; }
        }
        static double Box(Vector3d p, double he)
        {
            Vector3d q = new Vector3d(Math.Abs(p.x) - he, Math.Abs(p.y) - he, Math.Abs(p.z) - he);
            Vector3d o = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return o.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0);
        }
        static double Cyl(double a, double b, double r) { return Math.Sqrt(a * a + b * b) - r; }
        static double Csg(Vector3d p)
        {
            double f = Math.Max(Box(p, 1), p.Length - 1.35);
            return Math.Max(f, -Math.Min(Cyl(p.y, p.z, 0.55), Math.Min(Cyl(p.x, p.z, 0.55), Cyl(p.x, p.y, 0.55))));
        }

        static ulong MeshHash(DMesh3 m)
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v) { h = unchecked((h ^ (ulong)v) * 1099511628211UL); }
            foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); Mix(v); Mix(Bits(p.x)); Mix(Bits(p.y)); Mix(Bits(p.z)); }
            foreach (int t in m.TriangleIndices()) { Index3i i = m.GetTriangle(t); Mix(t); Mix(i.a); Mix(i.b); Mix(i.c); }
            return h;
        }

        const double Ext = 1.6;
        static AxisAlignedBox3d Box3() { return new AxisAlignedBox3d(new Vector3d(-Ext, -Ext, -Ext), new Vector3d(Ext, Ext, Ext)); }

        static ulong Adaptive(bool parallel)
        {
            var ad = new AdaptiveDualContouring() { Implicit = new Fn(Csg), InsideIsLess = true, CellSize = 2 * Ext / 40, Tolerance = 0.25, ParallelCompute = parallel, Bounds = Box3() };
            ad.Generate();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(ad.Mesh, true));
            return MeshHash(ad.Mesh);
        }
        static ulong Uniform(bool parallel)
        {
            var dc = new DualContouring() { Simplify = false, Implicit = new Fn(Csg), InsideIsLess = true, CellSize = 2 * Ext / 40, ParallelCompute = parallel, Bounds = Box3() };
            dc.Generate();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(dc.Mesh, true));
            return MeshHash(dc.Mesh);
        }

        // recorded with a fresh solver per QEF (before the reuse)
        const ulong AdaptiveHash = 8305701202763399720UL;
        const ulong UniformHash = 2796730562395454405UL;

        [Fact]
        public void AdaptiveDualContouring_IsBitForBitTheFreshSolverResult_SerialAndParallel()
        {
            Assert.Equal(AdaptiveHash, Adaptive(false));
            Assert.Equal(AdaptiveHash, Adaptive(true));
        }

        [Fact]
        public void DualContouring_IsBitForBitTheFreshSolverResult_SerialAndParallel()
        {
            Assert.Equal(UniformHash, Uniform(false));
            Assert.Equal(UniformHash, Uniform(true));
        }
    }
}
