using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> g3.SparseHermitianCholesky: the same answers as the real 2n x 2n SparseCholesky, residuals, failure, reuse. </summary>
    public class SparseHermitianCholeskyTests
    {
        // a Hermitian positive definite matrix on an n x m grid with random phases on the edges (and a few long edges), diagonal dominant
        static List<(int r, int c, double re, double im)> GridMatrix(int n, int m, int seed, double shift)
        {
            var rnd = new Random(seed);
            var e = new List<(int, int, double, double)>();
            var diag = new double[n * m];
            Action<int, int> edge = (a, b) => {
                double w = 0.5 + rnd.NextDouble(), ph = 2 * Math.PI * rnd.NextDouble();
                e.Add((Math.Max(a, b), Math.Min(a, b), -w * Math.Cos(ph) * (a > b ? 1 : 1), -w * Math.Sin(ph) * (a > b ? 1 : -1)));
                diag[a] += w; diag[b] += w;
            };
            for (int j = 0; j < m; ++j)
                for (int i = 0; i < n; ++i) {
                    int v = j * n + i;
                    if (i + 1 < n) edge(v, v + 1);
                    if (j + 1 < m) edge(v, v + n);
                    if (i + 1 < n && j + 1 < m && rnd.Next(3) == 0) edge(v, v + n + 1);
                }
            for (int v = 0; v < n * m; ++v) e.Add((v, v, diag[v] + shift, 0));
            return e;
        }

        static double[] Multiply(int N, List<(int r, int c, double re, double im)> e, double[] x)
        {
            var y = new double[2 * N];
            foreach (var (r, c, re, im) in e) {
                // A[r,c] = re + i im, A[c,r] = conj
                y[2 * r] += re * x[2 * c] - im * x[2 * c + 1]; y[2 * r + 1] += re * x[2 * c + 1] + im * x[2 * c];
                if (r != c) { y[2 * c] += re * x[2 * r] + im * x[2 * r + 1]; y[2 * c + 1] += re * x[2 * r + 1] - im * x[2 * r]; }
            }
            return y;
        }

        static SparseHermitianCholesky Build(int N, List<(int r, int c, double re, double im)> e)
        {
            var h = new SparseHermitianCholesky(N);
            foreach (var (r, c, re, im) in e) h.Add(r, c, re, im);
            Assert.True(h.Factorize(), h.Error);
            return h;
        }

        [Theory]
        [InlineData(1, 1), InlineData(2, 1), InlineData(3, 3), InlineData(7, 5), InlineData(24, 20)]
        public void Solve_HasASmallResidual_AndMatchesTheRealForm(int n, int m)
        {
            var e = GridMatrix(n, m, n * 31 + m, 0.05);
            int N = n * m;
            var h = Build(N, e);
            var rnd = new Random(5);
            var b = new double[2 * N];
            for (int k = 0; k < b.Length; ++k) b[k] = rnd.NextDouble() - 0.5;
            var x = new double[2 * N];
            h.Solve(b, x);
            var r = Multiply(N, e, x);
            double worst = 0, bn = 0;
            for (int k = 0; k < b.Length; ++k) { worst = Math.Max(worst, Math.Abs(r[k] - b[k])); bn = Math.Max(bn, Math.Abs(b[k])); }
            Assert.True(worst < 1e-9 * bn * 50, "residual " + worst);

            // the real 2n x 2n form: [[Re, -Im], [Im, Re]] with interleaved unknowns
            var real = new SparseCholesky(2 * N, 2);
            foreach (var (rr, c, re, im) in e) {
                if (rr == c) { real.Add(2 * rr, 2 * rr, re); real.Add(2 * rr + 1, 2 * rr + 1, re); continue; }
                real.Add(2 * rr, 2 * c, re); real.Add(2 * rr + 1, 2 * c + 1, re);
                real.Add(2 * rr, 2 * c + 1, -im); real.Add(2 * rr + 1, 2 * c, im);
            }
            Assert.True(real.Factorize());
            var xr = new double[2 * N];
            real.Solve(b, xr);
            for (int k = 0; k < x.Length; ++k) Assert.Equal(xr[k], x[k], 8);
        }

        [Fact]
        public void BiggerSystem_HasHalfTheFillOfTheRealForm()
        {
            var e = GridMatrix(60, 60, 3, 0.05);
            var h = Build(3600, e);
            var real = new SparseCholesky(7200, 2);
            foreach (var (rr, c, re, im) in e) {
                if (rr == c) { real.Add(2 * rr, 2 * rr, re); real.Add(2 * rr + 1, 2 * rr + 1, re); continue; }
                real.Add(2 * rr, 2 * c, re); real.Add(2 * rr + 1, 2 * c + 1, re);
                real.Add(2 * rr, 2 * c + 1, -im); real.Add(2 * rr + 1, 2 * c, im);
            }
            Assert.True(real.Factorize());
            Assert.True(h.FactorNonZeros > 0);
            // a complex entry is 4 numbers in the real form: the real factor holds about four times as many
            Assert.True(4.0 * h.FactorNonZeros <= 1.15 * real.FactorNonZeros, h.FactorNonZeros + " vs " + real.FactorNonZeros);
        }

        [Fact]
        public void IndefiniteMatrix_FailsWithAnError()
        {
            var h = new SparseHermitianCholesky(2);
            h.Add(0, 0, 1, 0); h.Add(1, 1, 1, 0); h.Add(1, 0, 2, 0.5);      // |offdiagonal| > 1
            Assert.False(h.Factorize());
            Assert.NotNull(h.Error);
            Assert.False(h.IsValid);
            Assert.Throws<InvalidOperationException>(() => h.Solve(new double[4], new double[4]));
        }

        [Fact]
        public void Entries_AboveTheDiagonal_AreTheConjugate_AndDuplicatesAreSummed()
        {
            var a = new SparseHermitianCholesky(2);
            a.Add(0, 0, 2, 0); a.Add(1, 1, 3, 0); a.Add(1, 0, 0.5, 0.25); a.Add(1, 0, 0.25, 0);
            var b = new SparseHermitianCholesky(2);
            b.Add(0, 0, 2, 0); b.Add(1, 1, 3, 0); b.Add(0, 1, 0.75, -0.25);       // the mirror entry (0, 1) is the conjugate of (1, 0)
            Assert.True(a.Factorize()); Assert.True(b.Factorize());
            var rhs = new[] { 1.0, 2.0, -1.0, 0.5 }; var xa = new double[4]; var xb = new double[4];
            a.Solve(rhs, xa); b.Solve(rhs, xb);
            for (int k = 0; k < 4; ++k) Assert.Equal(xa[k], xb[k], 12);
            // A x = rhs with A = [[2, 0.75 - 0.25i], [0.75 + 0.25i, 3]]
            Assert.Equal(rhs[0], 2 * xa[0] + (0.75 * xa[2] + 0.25 * xa[3]), 10);
            Assert.Equal(rhs[1], 2 * xa[1] + (0.75 * xa[3] - 0.25 * xa[2]), 10);
        }

        [Fact]
        public void Clear_AllowsANewMatrix_AndRepeatedRunsAreIdentical()
        {
            var e1 = GridMatrix(10, 10, 1, 0.1); var e2 = GridMatrix(10, 10, 2, 0.1);
            var h = new SparseHermitianCholesky(100);
            foreach (var t in e1) h.Add(t.r, t.c, t.re, t.im);
            Assert.True(h.Factorize());
            h.Clear();
            foreach (var t in e2) h.Add(t.r, t.c, t.re, t.im);
            Assert.True(h.Factorize());
            var fresh = Build(100, e2);
            var b = new double[200]; for (int k = 0; k < 200; ++k) b[k] = Math.Sin(k);
            var x1 = new double[200]; var x2 = new double[200];
            h.Solve(b, x1); fresh.Solve(b, x2);
            for (int k = 0; k < 200; ++k) Assert.Equal(BitConverter.DoubleToInt64Bits(x1[k]), BitConverter.DoubleToInt64Bits(x2[k]));
        }
    }
}
