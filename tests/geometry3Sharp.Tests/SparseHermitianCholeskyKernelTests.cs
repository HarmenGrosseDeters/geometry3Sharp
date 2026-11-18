using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The dense kernels of SparseHermitianCholesky (vector path, four products at once) give the same bits as the scalar
    /// reference, so the factor and the solutions of every caller (CrossField) are the same on every machine and framework.
    /// </summary>
    public unsafe class SparseHermitianCholeskyKernelTests
    {
        static ulong Next(ref ulong s) { s = s * 6364136223846793005UL + 1442695040888963407UL; return s >> 11; }
        // values with a full 53-bit mantissa and mixed magnitudes: a fused multiply-add or a different order of the sums changes bits
        static double Val(ref ulong s) { double m = (Next(ref s) & ((1UL << 52) - 1)) / (double)(1UL << 52) + 0.5; int e = (int)(Next(ref s) % 9) - 4; return (Next(ref s) % 2 == 0 ? -1 : 1) * m * Math.Pow(2, e); }
        static long Bits(double v) { return BitConverter.DoubleToInt64Bits(v); }

        [Fact]
        public void Kernels_MatchScalarReference_Bitwise()
        {
            ulong s = 4242;
            int rows = 4, maxN = 67;
            var ar = new double[200]; var ai = new double[200]; var br = new double[4 * 80 + 8]; var bi = new double[4 * 80 + 8];
            var rr = new double[4]; var ri = new double[4];
            for (int n = 0; n <= maxN; ++n)
                for (int off = 0; off < 3; ++off) {
                    for (int i = 0; i < ar.Length; ++i) { ar[i] = Val(ref s); ai[i] = Val(ref s); }
                    for (int i = 0; i < br.Length; ++i) { br[i] = Val(ref s); bi[i] = Val(ref s); }
                    int stride = n + 1 + (n % 3);
                    fixed (double* par = ar, pai = ai, pbr = br, pbi = bi, prr = rr, pri = ri) {
                        double* AR = par + off, AI = pai + off, BR = pbr + off, BI = pbi + off;
                        double r0, i0; SparseHermitianCholeskyKernels.DotcScalar(AR, AI, BR, BI, n, out r0, out i0);
                        for (int vec = 0; vec < 2; ++vec) {
                            double r1, i1; SparseHermitianCholeskyKernels.Dotc(AR, AI, BR, BI, n, out r1, out i1, vec == 1);
                            Assert.Equal(Bits(r0), Bits(r1)); Assert.Equal(Bits(i0), Bits(i1));
                            SparseHermitianCholeskyKernels.Dotc4Cols(AR, AI, BR, BI, stride, n, prr, pri, vec == 1);
                            for (int j = 0; j < rows; ++j) {
                                double er, ei; SparseHermitianCholeskyKernels.DotcScalar(AR, AI, BR + j * stride, BI + j * stride, n, out er, out ei);
                                Assert.Equal(Bits(er), Bits(rr[j])); Assert.Equal(Bits(ei), Bits(ri[j]));
                            }
                            SparseHermitianCholeskyKernels.Dotc4Rows(BR, BI, stride, AR, AI, n, prr, pri, vec == 1);
                            for (int j = 0; j < rows; ++j) {
                                double er, ei; SparseHermitianCholeskyKernels.DotcScalar(BR + j * stride, BI + j * stride, AR, AI, n, out er, out ei);
                                Assert.Equal(Bits(er), Bits(rr[j])); Assert.Equal(Bits(ei), Bits(ri[j]));
                            }
                        }
                    }
                }
        }

        [Fact]
        public void Kernels_AreNotFusedMultiplyAdd()
        {
            // the real part of the odd element: r0 holds c, then adds x * x + 0 (a fused ar*br + ai*bi would keep the 2^-54 of x * x)
            double x = 1 + Math.Pow(2, -27), c = -(1 + Math.Pow(2, -26));
            // element 0: a = 1, b = c (r0 = c); element 2: a = x, b = x (r0 += x * x): x * x rounded to 1 + 2^-26 cancels c exactly
            var ar = new double[] { 1, 0, x, 0 }; var ai = new double[4]; var br = new double[] { c, 0, x, 0 }; var bi = new double[4];
            fixed (double* par = ar, pai = ai, pbr = br, pbi = bi) {
                double re, im;
                SparseHermitianCholeskyKernels.DotcScalar(par, pai, pbr, pbi, 4, out re, out im); Assert.Equal(0.0, re);
                SparseHermitianCholeskyKernels.Dotc(par, pai, pbr, pbi, 4, out re, out im, true); Assert.Equal(0.0, re);
                var rr = stackalloc double[4]; var ri = stackalloc double[4];
                SparseHermitianCholeskyKernels.Dotc4Cols(par, pai, pbr, pbi, 0, 4, rr, ri, true);
                for (int j = 0; j < 4; ++j) Assert.Equal(0.0, rr[j]);
                SparseHermitianCholeskyKernels.Dotc4Rows(par, pai, 0, pbr, pbi, 4, rr, ri, true);
                for (int j = 0; j < 4; ++j) Assert.Equal(0.0, rr[j]);
            }
        }

        [Fact]
        public void Kernels_AreNotFusedMultiplyAdd_ImaginaryPart()
        {
            // i0 holds c (element 0: ai = 1, br = c), then adds ai * br - ar * bi = x * x - 0 (element 2): rounded x * x cancels c exactly
            double x = 1 + Math.Pow(2, -27), c = -(1 + Math.Pow(2, -26));
            var ar = new double[4]; var ai = new double[] { 1, 0, x, 0 }; var br = new double[] { c, 0, x, 0 }; var bi = new double[4];
            fixed (double* par = ar, pai = ai, pbr = br, pbi = bi) {
                double re, im;
                SparseHermitianCholeskyKernels.DotcScalar(par, pai, pbr, pbi, 4, out re, out im); Assert.Equal(0.0, im);
                SparseHermitianCholeskyKernels.Dotc(par, pai, pbr, pbi, 4, out re, out im, true); Assert.Equal(0.0, im);
                var rr = stackalloc double[4]; var ri = stackalloc double[4];
                SparseHermitianCholeskyKernels.Dotc4Cols(par, pai, pbr, pbi, 0, 4, rr, ri, true);
                for (int j = 0; j < 4; ++j) Assert.Equal(0.0, ri[j]);
                SparseHermitianCholeskyKernels.Dotc4Rows(par, pai, 0, pbr, pbi, 4, rr, ri, true);
                for (int j = 0; j < 4; ++j) Assert.Equal(0.0, ri[j]);
            }
        }

        [Fact]
        public void PivotThreshold_IsThatOfTheRowItself_InABatchOfFourRows()
        {
            // a dense 4 x 4 matrix whose ordering puts unknown 1 (diagonal 1, pivot about 1e-8 after the elimination against the
            // diagonal 1e20 of unknown 0) as the last row of one batch: the pivot test uses the diagonal entry of A of that row
            // (1e-14 * 1, passes), not the one of the first row of the batch (1e-14 * 1e20, would fail)
            for (int scalar = 0; scalar < 2; ++scalar) {
                var c = new SparseHermitianCholesky(4); c.ForceScalarKernels = scalar == 1;
                c.Add(0, 0, 1e20, 0); c.Add(1, 1, 1, 0); c.Add(2, 2, 1, 0); c.Add(3, 3, 1, 0);
                c.Add(1, 0, 1e10 * Math.Sqrt(1 - 1e-8), 0);
                c.Add(2, 0, 1e-6, 1e-6); c.Add(3, 0, 1e-6, 0); c.Add(2, 1, 1e-6, 0); c.Add(3, 1, 0, 1e-6); c.Add(3, 2, 1e-6, 0);
                Assert.True(c.Factorize(), c.Error);
            }
        }

        static ulong HashDoubles(params double[][] arrays)
        {
            ulong h = 1469598103934665603UL;
            foreach (var a in arrays) foreach (double v in a) { h ^= (ulong)Bits(v); h *= 1099511628211UL; }
            return h;
        }

        [Fact]
        public void Factor_AndSolve_VectorEqualsScalar_OnComplexMatrices()
        {
            Func<SparseHermitianCholesky>[] make = {
                () => CholeskyAnalysisTests.ComplexGrid(40, 40, 6), () => CholeskyAnalysisTests.ComplexGrid(33, 21, 7),
                () => CholeskyAnalysisTests.ComplexGrid(15, 60, 12), () => CholeskyAnalysisTests.ComplexRandom(700, 4, 8), () => CholeskyAnalysisTests.ComplexRandom(300, 9, 3),
            };
            foreach (var mk in make) {
                ulong[] hf = new ulong[2], hx = new ulong[2];
                for (int scalar = 0; scalar < 2; ++scalar) {
                    var c = mk(); c.ForceScalarKernels = scalar == 1;
                    Assert.True(c.Factorize(), c.Error);
                    var b = new double[2 * c.N]; ulong s = 11; for (int i = 0; i < b.Length; ++i) b[i] = Val(ref s);
                    var x = new double[2 * c.N]; c.Solve(b, x);
                    hf[scalar] = HashDoubles(c.FactorValues); hx[scalar] = HashDoubles(x);
                }
                Assert.Equal(hf[0], hf[1]); Assert.Equal(hx[0], hx[1]);
            }
        }

        [Fact]
        public void IndefiniteMatrices_GiveTheSameError_OnBothPaths()
        {
            // the failing row lies at different places of the batches of four rows
            for (int k = 0; k < 24; ++k) {
                int unknown = 7 * k + 3;
                Func<SparseHermitianCholesky> mk = () => {
                    var c = CholeskyAnalysisTests.ComplexGrid(20, 20, 11);
                    c.Add(unknown, unknown, -50.0, 0);
                    return c;
                };
                var a = mk(); a.ForceScalarKernels = true; Assert.False(a.Factorize());
                var b = mk(); b.ForceScalarKernels = false; Assert.False(b.Factorize());
                Assert.NotNull(a.Error); Assert.Equal(a.Error, b.Error);
            }
        }

        [Fact]
        public void IndefiniteDenseMatrices_GiveTheSameError_AtEveryRowOfABatch()
        {
            // one supernode of 12 columns (batches of four rows): the negative diagonal entry of unknown u fails the pivot of the row
            // that holds u, at every position of a batch over the 12 cases
            for (int u = 0; u < 12; ++u) {
                Func<SparseHermitianCholesky> mk = () => {
                    var c = new SparseHermitianCholesky(12);
                    for (int r = 0; r < 12; ++r) {
                        c.Add(r, r, r == u ? -5.0 : 20.0, 0);
                        for (int q = 0; q < r; ++q) c.Add(r, q, 0.1 * ((r * 7 + q * 3) % 5 - 2), 0.05 * ((r + 2 * q) % 4 - 1));
                    }
                    return c;
                };
                var a = mk(); a.ForceScalarKernels = true; Assert.False(a.Factorize());
                var b = mk(); b.ForceScalarKernels = false; Assert.False(b.Factorize());
                Assert.NotNull(a.Error); Assert.Equal(a.Error, b.Error);
            }
        }
    }
}
