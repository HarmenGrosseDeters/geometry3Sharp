using System;
using g3;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The dense kernels of SparseCholesky (vector path, four products at once) give the same bits as the scalar
    /// reference, so the factor and the solutions of every caller are the same on every machine and framework.
    /// </summary>
    public unsafe class SparseCholeskyKernelTests
    {
        readonly ITestOutputHelper output;
        public SparseCholeskyKernelTests(ITestOutputHelper output) { this.output = output; }

        static ulong Next(ref ulong s) { s = s * 6364136223846793005UL + 1442695040888963407UL; return s >> 11; }
        // values with a full 53-bit mantissa and mixed magnitudes: a fused multiply-add or a different order of the sums changes bits
        static double Val(ref ulong s) { double m = (Next(ref s) & ((1UL << 52) - 1)) / (double)(1UL << 52) + 0.5; int e = (int)(Next(ref s) % 9) - 4; return (Next(ref s) % 2 == 0 ? -1 : 1) * m * Math.Pow(2, e); }

        // symmetric positive definite matrices from rational entries only (+, -, *, / and sqrt are correctly rounded, so the hashes below hold on every platform)
        internal static SparseCholesky Grid2(int nx, int ny, int bs, ulong seed)
        {
            ulong s = seed; int n = nx * ny * bs; var c = new SparseCholesky(n, bs);
            Func<int, int, int> id = (x, y) => (y * nx + x) * bs;
            for (int y = 0; y < ny; ++y)
                for (int x = 0; x < nx; ++x) {
                    int a = id(x, y);
                    for (int u = 0; u < bs; ++u) {
                        c.Add(a + u, a + u, 8.0 + (Next(ref s) % 64) / 64.0);
                        for (int v = 0; v < u; ++v) c.Add(a + u, a + v, -((Next(ref s) % 32) / 64.0));
                    }
                    int[][] nb = { new[] { x + 1, y }, new[] { x, y + 1 }, new[] { x + 1, y + 1 } };
                    foreach (var q in nb) {
                        if (q[0] >= nx || q[1] >= ny) continue;
                        int b = id(q[0], q[1]);
                        for (int u = 0; u < bs; ++u)
                            for (int v = 0; v < bs; ++v) c.Add(b + v, a + u, -((Next(ref s) % 128) / 128.0 + 0.25) / bs);
                    }
                }
            return c;
        }
        internal static SparseCholesky Grid3(int k, ulong seed)
        {
            ulong s = seed; int n = k * k * k; var c = new SparseCholesky(n);
            Func<int, int, int, int> id = (x, y, z) => (z * k + y) * k + x;
            for (int z = 0; z < k; ++z) for (int y = 0; y < k; ++y) for (int x = 0; x < k; ++x) {
                int a = id(x, y, z); c.Add(a, a, 9.0 + (Next(ref s) % 16) / 16.0);
                if (x + 1 < k) c.Add(id(x + 1, y, z), a, -(double)(1 + Next(ref s) % 8) / 8.0);
                if (y + 1 < k) c.Add(id(x, y + 1, z), a, -(double)(1 + Next(ref s) % 8) / 8.0);
                if (z + 1 < k) c.Add(id(x, y, z + 1), a, -(double)(1 + Next(ref s) % 8) / 8.0);
            }
            return c;
        }
        internal static SparseCholesky RandomSparse(int n, int perRow, ulong seed)
        {
            ulong s = seed; var c = new SparseCholesky(n);
            for (int i = 0; i < n; ++i) {
                c.Add(i, i, perRow * 2 + 1 + (Next(ref s) % 16) / 16.0);
                for (int t = 0; t < perRow; ++t) { int j = (int)(Next(ref s) % (ulong)n); if (j != i) c.Add(i, j, -(double)(1 + Next(ref s) % 8) / 8.0); }
            }
            return c;
        }

        static ulong Hash(double[] v)
        {
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < v.Length; ++i) { h ^= (ulong)BitConverter.DoubleToInt64Bits(v[i]); h *= 1099511628211UL; }
            return h;
        }

        static double[] Rhs(int n, ulong seed) { ulong s = seed; var b = new double[n]; for (int i = 0; i < n; ++i) b[i] = Val(ref s); return b; }

        // factor hash and solution hash of the matrix
        static void Run(SparseCholesky c, bool forceScalar, out ulong hf, out ulong hx)
        {
            c.ForceScalarKernels = forceScalar;
            Assert.True(c.Factorize(), c.Error);
            hf = Hash(c.FactorValues);
            var x = new double[c.N]; c.Solve(Rhs(c.N, 99), x);
            hx = Hash(x);
        }

        [Fact]
        public void Kernels_MatchScalarReference_Bitwise()
        {
            ulong s = 12345;
            var a = new double[200]; var b = new double[4 * 70 + 8]; var r = new double[4]; var r0 = new double[4];
            for (int n = 0; n <= 67; ++n)
                for (int off = 0; off < 3; ++off) {
                    for (int i = 0; i < a.Length; ++i) a[i] = Val(ref s);
                    for (int i = 0; i < b.Length; ++i) b[i] = Val(ref s);
                    int stride = n + 1 + (n % 3);
                    fixed (double* pa = a, pb = b, pr = r) {
                        double* A = pa + off, Bp = pb + off;
                        ulong refBits = (ulong)BitConverter.DoubleToInt64Bits(SparseCholeskyKernels.DotScalar(A, Bp, n));
                        Assert.Equal(refBits, (ulong)BitConverter.DoubleToInt64Bits(SparseCholeskyKernels.Dot(A, Bp, n, true)));
                        Assert.Equal(refBits, (ulong)BitConverter.DoubleToInt64Bits(SparseCholeskyKernels.Dot(A, Bp, n, false)));
                        for (int vec = 0; vec < 2; ++vec) {
                            SparseCholeskyKernels.Dot4Cols(A, Bp, stride, n, pr, vec == 1);
                            for (int j = 0; j < 4; ++j)
                                Assert.Equal((ulong)BitConverter.DoubleToInt64Bits(SparseCholeskyKernels.DotScalar(A, Bp + j * stride, n)), (ulong)BitConverter.DoubleToInt64Bits(r[j]));
                            SparseCholeskyKernels.Dot4Rows(Bp, stride, A, n, pr, vec == 1);
                            for (int j = 0; j < 4; ++j)
                                Assert.Equal((ulong)BitConverter.DoubleToInt64Bits(SparseCholeskyKernels.DotScalar(Bp + j * stride, A, n)), (ulong)BitConverter.DoubleToInt64Bits(r[j]));
                        }
                    }
                }
        }

        [Fact]
        public void Kernels_AreNotFusedMultiplyAdd()
        {
            // lane 0 holds c, then adds x * x: x * x = 1 + 2^-26 + 2^-54, rounded to 1 + 2^-26 it cancels c exactly (0), fused it leaves 2^-54
            double x = 1 + Math.Pow(2, -27), c = -(1 + Math.Pow(2, -26));
            var a = new double[] { 1, 0, 0, 0, x, 0, 0, 0 }; var b = new double[] { c, 0, 0, 0, x, 0, 0, 0 };
            fixed (double* pa = a, pb = b) {
                Assert.Equal(0.0, SparseCholeskyKernels.DotScalar(pa, pb, 8));
                Assert.Equal(0.0, SparseCholeskyKernels.Dot(pa, pb, 8, true));
                var r = stackalloc double[4];
                SparseCholeskyKernels.Dot4Cols(pa, pb, 0, 8, r, true);
                for (int j = 0; j < 4; ++j) Assert.Equal(0.0, r[j]);
                SparseCholeskyKernels.Dot4Rows(pa, 0, pb, 8, r, true);
                for (int j = 0; j < 4; ++j) Assert.Equal(0.0, r[j]);
            }
        }

        [Fact]
        public void Factor_AndSolve_VectorEqualsScalar_OnProceduralMatrices()
        {
            Func<SparseCholesky>[] make = {
                () => Grid2(40, 40, 1, 1), () => Grid2(24, 24, 2, 2), () => Grid2(17, 13, 3, 3), () => Grid3(12, 4), () => RandomSparse(900, 4, 5),
            };
            foreach (var mk in make) {
                ulong hf0, hx0, hf1, hx1;
                Run(mk(), true, out hf0, out hx0);
                Run(mk(), false, out hf1, out hx1);
                Assert.Equal(hf0, hf1); Assert.Equal(hx0, hx1);
            }
        }

        [Fact]
        public void IndefiniteMatrix_GivesTheSameError_OnBothPaths()
        {
            // a grid matrix whose pivot fails late (inside a block of four rows): the message names the pivot value and the unknown
            Func<SparseCholesky> mk = () => {
                var c = Grid2(20, 20, 2, 11);
                c.Add(300, 300, -50.0);
                return c;
            };
            var a = mk(); a.ForceScalarKernels = true; Assert.False(a.Factorize());
            var b = mk(); b.ForceScalarKernels = false; Assert.False(b.Factorize());
            Assert.NotNull(a.Error); Assert.Equal(a.Error, b.Error);
        }

        [Fact]
        public void Factor_AndSolve_ReuseAndRepeat_GiveSameBits()
        {
            var c = Grid2(20, 20, 2, 7);
            ulong hf, hx, hf2, hx2;
            Run(c, false, out hf, out hx);
            Run(c, false, out hf2, out hx2);        // factorize again on the same instance (pattern reuse path)
            Assert.Equal(hf, hf2); Assert.Equal(hx, hx2);
        }

        // golden values computed with the scalar code before the vector kernels (master at da564d55): hash of the values of L and of a solution
        [Fact]
        public void Factor_AndSolve_MatchGoldenBits_OfTheScalarCode()
        {
            var cases = new (Func<SparseCholesky> mk, ulong hf, ulong hx)[] {
                (() => Grid2(40, 40, 1, 1), 1690050378515251541UL, 6722980510847618087UL),
                (() => Grid2(24, 24, 2, 2), 7902512746324670906UL, 11495085783730029202UL),
                (() => Grid2(17, 13, 3, 3), 9265867683754729350UL, 11002076589593804164UL),
                (() => Grid3(12, 4), 17312804471613627943UL, 11918645186113209138UL),
                (() => RandomSparse(900, 4, 5), 10387091831533664555UL, 306837166766447501UL),
            };
            foreach (var cs in cases) {
                foreach (bool scalar in new[] { true, false }) {
                    ulong hf, hx; Run(cs.mk(), scalar, out hf, out hx);
                    Assert.Equal(cs.hf, hf); Assert.Equal(cs.hx, hx);
                }
            }
        }
    }
}
