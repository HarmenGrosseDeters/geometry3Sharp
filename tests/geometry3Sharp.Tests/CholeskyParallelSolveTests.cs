using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The solves of SparseCholesky and SparseHermitianCholesky run subtrees of the supernode tree as parallel tasks and read
    /// wide supernodes from a column-major copy (CholeskySolveSchedule). The solutions are the bits of the plain serial loops
    /// (the references below are the solves as they were before), for every schedule, thread count and task order.
    /// </summary>
    public class CholeskyParallelSolveTests
    {
        // ---- matrices: a triangulated grid of nodes with bs unknowns each, diagonally dominant

        static List<(int r, int c, double re, double im)> GridEntries(int nx, int ny, int bs, int seed, bool complex, double scale = 1)
        {
            var rnd = new Random(seed);
            int nodes = nx * ny, n = nodes * bs;
            var diag = new double[n];
            var e = new List<(int, int, double, double)>();
            Action<int, int, double, double> off = (r, c, re, im) => {
                if (r < c) { int t = r; r = c; c = t; im = -im; }
                e.Add((r, c, re * scale, im * scale)); diag[r] += Math.Abs(re) + Math.Abs(im); diag[c] += Math.Abs(re) + Math.Abs(im);
            };
            Action<int, int> link = (a, b) => {
                for (int k = 0; k < bs; ++k)
                    for (int l = 0; l < bs; ++l) {
                        double w = -(0.25 + rnd.NextDouble()) * (k == l ? 1 : 0.3);
                        double im = complex ? 0.5 * (rnd.NextDouble() - 0.5) : 0;
                        off(a * bs + k, b * bs + l, w, im);
                    }
            };
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    int v = j * nx + i;
                    for (int k = 0; k < bs; ++k)
                        for (int l = 0; l < k; ++l) off(v * bs + k, v * bs + l, -0.1 * rnd.NextDouble(), 0);
                    if (i + 1 < nx) link(v, v + 1);
                    if (j + 1 < ny) link(v, v + nx);
                    if (i + 1 < nx && j + 1 < ny && (i + j) % 2 == 0) link(v, v + nx + 1);
                }
            for (int u = 0; u < n; ++u) e.Add((u, u, (diag[u] + 0.1) * scale, 0));
            return e;
        }

        static SparseCholesky BuildReal(int nx, int ny, int bs, int seed, long minWork, int divisor, int minWidth, double scale = 1)
        {
            var c = new SparseCholesky(nx * ny * bs, bs);
            c.SolveMinWork = minWork; c.SolveTaskDivisor = divisor; c.ColumnCopyMinWidth = minWidth; c.ColumnCopyMinEntries = 0;
            foreach (var (r, cc, re, _) in GridEntries(nx, ny, bs, seed, false, scale)) c.Add(r, cc, re);
            Assert.True(c.Factorize(), c.Error);
            return c;
        }

        static SparseHermitianCholesky BuildHermitian(int nx, int ny, int seed, long minWork, int divisor, int minWidth)
        {
            var c = new SparseHermitianCholesky(nx * ny);
            c.SolveMinWork = minWork; c.SolveTaskDivisor = divisor; c.ColumnCopyMinWidth = minWidth; c.ColumnCopyMinEntries = 0;
            foreach (var (r, cc, re, im) in GridEntries(nx, ny, 1, seed, true)) c.Add(r, cc, re, im);
            Assert.True(c.Factorize(), c.Error);
            return c;
        }

        static double[] Rhs(int len, int seed)
        {
            var rnd = new Random(seed);
            var b = new double[len];
            for (int i = 0; i < len; ++i) b[i] = rnd.NextDouble() - 0.5;
            return b;
        }

        static void AssertSameBits(double[] expected, double[] actual, string what)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; ++i)
                if (BitConverter.DoubleToInt64Bits(expected[i]) != BitConverter.DoubleToInt64Bits(actual[i]))
                    Assert.Fail(what + ": entry " + i + " differs: " + expected[i].ToString("R") + " vs " + actual[i].ToString("R"));
        }

        // ---- the solves before the schedule, as the reference

        static double[] ReferenceSolve(SparseCholesky c, double[] b)
        {
            int[][] an = c.AnalysisArrays; int[] perm = an[0], snFirst = an[3], snVal = an[4], snRowPtr = an[5], snRows = an[6];
            double[] Lx = c.FactorValues; int snCount = snFirst.Length - 1, N = c.N;
            double[] y = new double[N];
            for (int k = 0; k < N; ++k) y[k] = b[perm[k]];
            for (int J = 0; J < snCount; ++J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
                for (int cc = 0; cc < w; ++cc) {
                    double sum = y[f + cc]; int co = off + cc * w;
                    for (int t = 0; t < cc; ++t) sum -= Lx[co + t] * y[f + t];
                    y[f + cc] = sum / Lx[co + cc];
                }
                for (int i = w; i < h; ++i) {
                    double sum = 0; int io = off + i * w;
                    for (int t = 0; t < w; ++t) sum += Lx[io + t] * y[f + t];
                    y[snRows[rp + i]] -= sum;
                }
            }
            for (int J = snCount - 1; J >= 0; --J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
                for (int cc = w - 1; cc >= 0; --cc) {
                    double sum = y[f + cc];
                    for (int i = cc + 1; i < h; ++i) sum -= Lx[off + i * w + cc] * y[snRows[rp + i]];
                    y[f + cc] = sum / Lx[off + cc * w + cc];
                }
            }
            var x = new double[N];
            for (int k = 0; k < N; ++k) x[perm[k]] = y[k];
            return x;
        }

        static double[] ReferenceSolve(SparseHermitianCholesky c, double[] b)
        {
            int[][] an = c.AnalysisArrays; int[] perm = an[0], snFirst = an[3], snVal = an[4], snRowPtr = an[5], snRows = an[6];
            double[] LxRe = c.FactorValues[0], LxIm = c.FactorValues[1]; int snCount = snFirst.Length - 1, N = c.N;
            double[] yr = new double[N], yi = new double[N];
            for (int k = 0; k < N; ++k) { yr[k] = b[2 * perm[k]]; yi[k] = b[2 * perm[k] + 1]; }
            for (int J = 0; J < snCount; ++J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
                for (int cc = 0; cc < w; ++cc) {
                    double sr = yr[f + cc], si = yi[f + cc]; int co = off + cc * w;
                    for (int t = 0; t < cc; ++t) {
                        double lr = LxRe[co + t], li = LxIm[co + t];
                        sr -= lr * yr[f + t] - li * yi[f + t]; si -= lr * yi[f + t] + li * yr[f + t];
                    }
                    double d = LxRe[co + cc];
                    yr[f + cc] = sr / d; yi[f + cc] = si / d;
                }
                for (int i = w; i < h; ++i) {
                    double sr = 0, si = 0; int io = off + i * w;
                    for (int t = 0; t < w; ++t) {
                        double lr = LxRe[io + t], li = LxIm[io + t];
                        sr += lr * yr[f + t] - li * yi[f + t]; si += lr * yi[f + t] + li * yr[f + t];
                    }
                    int row = snRows[rp + i];
                    yr[row] -= sr; yi[row] -= si;
                }
            }
            for (int J = snCount - 1; J >= 0; --J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
                for (int cc = w - 1; cc >= 0; --cc) {
                    double sr = yr[f + cc], si = yi[f + cc];
                    for (int i = cc + 1; i < h; ++i) {
                        double lr = LxRe[off + i * w + cc], li = LxIm[off + i * w + cc];
                        int row = snRows[rp + i];
                        sr -= lr * yr[row] + li * yi[row]; si -= lr * yi[row] - li * yr[row];
                    }
                    double d = LxRe[off + cc * w + cc];
                    yr[f + cc] = sr / d; yi[f + cc] = si / d;
                }
            }
            var x = new double[2 * N];
            for (int k = 0; k < N; ++k) { x[2 * perm[k]] = yr[k]; x[2 * perm[k] + 1] = yi[k]; }
            return x;
        }

        // the scheduled solve under several task orders (the serial loops of gParallel in another order), serial flag, and normal
        static double[] SolveWith(Action<double[], double[]> solve, int len, double[] b, int order)
        {
            var x = new double[len];
            if (order == 0) { solve(b, x); return x; }
            gParallel.SerialOnThisThread = true; gParallel.SerialOrderOnThisThread = order;
            try { solve(b, x); } finally { gParallel.SerialOnThisThread = false; gParallel.SerialOrderOnThisThread = 0; }
            return x;
        }

        // schedule parameters: (nx, ny, bs, divisor, minWidth): enough supernodes for several tasks, wide ones for the column copy
        public static IEnumerable<object[]> Shapes()
        {
            yield return new object[] { 30, 30, 1, 8, 6 };
            yield return new object[] { 24, 20, 2, 4, 8 };
            yield return new object[] { 36, 17, 1, 64, 3 };
            yield return new object[] { 14, 14, 3, 2, 12 };
            yield return new object[] { 40, 12, 2, 16, 1000 };       // no column copy
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void RealSolve_EveryScheduleGivesTheBitsOfTheSerialLoops(int nx, int ny, int bs, int divisor, int minWidth)
        {
            var c = BuildReal(nx, ny, bs, 11, 0, divisor, minWidth);
            Assert.NotNull(c.SolveScheduleForTests);
            Assert.Equal(minWidth < 1000, c.ColumnCopyInUse);
            for (int rep = 0; rep < 3; ++rep) {
                double[] b = Rhs(c.N, 100 + rep), expected = ReferenceSolve(c, b);
                foreach (int order in new[] { 0, -1, 7, 12345 })
                    AssertSameBits(expected, SolveWith(c.Solve, c.N, b, order), "order " + order);
                c.ForceSerialSolve = true;
                AssertSameBits(expected, SolveWith(c.Solve, c.N, b, 0), "serial");
                c.ForceSerialSolve = false;
            }
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void HermitianSolve_EveryScheduleGivesTheBitsOfTheSerialLoops(int nx, int ny, int bs, int divisor, int minWidth)
        {
            var c = BuildHermitian(nx, ny, 12, 0, divisor, minWidth);
            Assert.NotNull(c.SolveScheduleForTests);
            Assert.Equal(minWidth < 1000, c.ColumnCopyInUse);
            for (int rep = 0; rep < 3; ++rep) {
                double[] b = Rhs(2 * c.N, 200 + rep), expected = ReferenceSolve(c, b);
                foreach (int order in new[] { 0, -1, 7, 12345 })
                    AssertSameBits(expected, SolveWith(c.Solve, 2 * c.N, b, order), "order " + order);
                c.ForceSerialSolve = true;
                AssertSameBits(expected, SolveWith(c.Solve, 2 * c.N, b, 0), "serial");
                c.ForceSerialSolve = false;
            }
        }

        [Fact]
        public void Solve_InPlace_AndRepeated_AndConcurrent_AreTheSameBits()
        {
            var c = BuildReal(28, 28, 2, 3, 0, 8, 6);
            double[] b = Rhs(c.N, 5), expected = ReferenceSolve(c, b);
            var x = (double[])b.Clone();
            c.Solve(x, x);
            AssertSameBits(expected, x, "in place");
            var results = new double[16][];
            Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, k => { results[k] = new double[c.N]; c.Solve(b, results[k]); });
            foreach (var r in results) AssertSameBits(expected, r, "concurrent");

            var h = BuildHermitian(26, 26, 4, 0, 8, 6);
            double[] bh = Rhs(2 * h.N, 6), eh = ReferenceSolve(h, bh);
            var hr = new double[8][];
            Parallel.For(0, hr.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, k => { hr[k] = new double[2 * h.N]; h.Solve(bh, hr[k]); });
            foreach (var r in hr) AssertSameBits(eh, r, "hermitian concurrent");
        }

        // a refactorization with the same pattern keeps the analysis of SparseCholesky: the column copy and the schedule must follow the new factor
        [Fact]
        public void Refactorize_RebuildsTheColumnCopy_Real()
        {
            var c = BuildReal(26, 26, 1, 21, 0, 8, 5);
            double[] b = Rhs(c.N, 9);
            Assert.True(c.ColumnCopyInUse);
            AssertSameBits(ReferenceSolve(c, b), SolveWith(c.Solve, c.N, b, 0), "first");
            c.Clear();
            foreach (var (r, cc, re, _) in GridEntries(26, 26, 1, 21, false, 4)) c.Add(r, cc, re);       // the same pattern, other values
            Assert.True(c.Factorize(), c.Error);
            AssertSameBits(ReferenceSolve(c, b), SolveWith(c.Solve, c.N, b, 0), "second");
            c.Clear();
            foreach (var (r, cc, re, _) in GridEntries(26, 26, 1, 22, false, 1)) c.Add(r, cc, re);       // other values again (random weights differ)
            Assert.True(c.Factorize(), c.Error);
            AssertSameBits(ReferenceSolve(c, b), SolveWith(c.Solve, c.N, b, 0), "third");
        }

        [Fact]
        public void Refactorize_RebuildsTheColumnCopy_Hermitian()
        {
            var c = BuildHermitian(24, 24, 31, 0, 8, 5);
            double[] b = Rhs(2 * c.N, 10);
            Assert.True(c.ColumnCopyInUse);
            AssertSameBits(ReferenceSolve(c, b), SolveWith(c.Solve, 2 * c.N, b, 0), "first");
            c.Clear();
            foreach (var (r, cc, re, im) in GridEntries(24, 24, 1, 32, true)) c.Add(r, cc, re, im);
            Assert.True(c.Factorize(), c.Error);
            AssertSameBits(ReferenceSolve(c, b), SolveWith(c.Solve, 2 * c.N, b, 0), "second");
        }

        // a power of two scale of the matrix scales the solution exactly (the schedule must not change a single operation)
        [Fact]
        public void Solve_UnderPowerOfTwoScales_ScalesTheSolutionExactly()
        {
            double[] b = null, x1 = null;
            foreach (double s in new[] { 1.0, 4.0, 1.0 / 1024, 1 << 20 }) {
                var c = BuildReal(26, 26, 2, 41, 0, 8, 6, s);
                Assert.True(c.ColumnCopyInUse);
                b = b ?? Rhs(c.N, 3);
                var x = new double[c.N]; c.Solve(b, x);
                if (x1 == null) x1 = x;
                else for (int i = 0; i < x.Length; ++i)
                    Assert.Equal(BitConverter.DoubleToInt64Bits(x1[i] / s), BitConverter.DoubleToInt64Bits(x[i]));
            }
        }

        [Fact]
        public void SmallSystems_StayOnTheSerialPath_WithTheDefaultThreshold()
        {
            var c = new SparseCholesky(400);
            foreach (var (r, cc, re, _) in GridEntries(20, 20, 1, 1, false)) c.Add(r, cc, re);
            Assert.True(c.Factorize());
            Assert.Null(c.SolveScheduleForTests);
            Assert.False(c.ColumnCopyInUse);
            var h = new SparseHermitianCholesky(400);
            foreach (var (r, cc, re, im) in GridEntries(20, 20, 1, 1, true)) h.Add(r, cc, re, im);
            Assert.True(h.Factorize());
            Assert.Null(h.SolveScheduleForTests);
            Assert.False(h.ColumnCopyInUse);
        }

        // ---- the structure of the schedule

        [Theory]
        [InlineData(30, 30, 1, 8), InlineData(24, 20, 2, 4), InlineData(36, 17, 1, 64), InlineData(14, 14, 3, 2)]
        public void Schedule_TasksAreClosedUnderDescendants_AndBuffersDoNotOverlap(int nx, int ny, int bs, int divisor)
        {
            var c = BuildReal(nx, ny, bs, 11, 0, divisor, 1000);
            int[][] an = c.AnalysisArrays; int[] snFirst = an[3], snRowPtr = an[5], snRows = an[6], colSnode = an[7];
            var s = c.SolveScheduleForTests;
            Assert.NotNull(s);
            int snCount = snFirst.Length - 1;
            Assert.True(s.TaskFirst.Length >= 2);
            int bufEnd = 0;
            var used = new bool[s.BufSize];
            for (int t = 0; t < s.TaskFirst.Length; ++t) {
                Assert.True(s.TaskFirst[t] < s.TaskEnd[t]);
                if (t > 0) Assert.True(s.TaskFirst[t] >= s.TaskEnd[t - 1]);
                int c1 = snFirst[s.TaskEnd[t]];
                for (int J = s.TaskFirst[t]; J < s.TaskEnd[t]; ++J) {
                    Assert.Equal(t, s.TaskOf[J]);
                    int w = snFirst[J + 1] - snFirst[J], rp = snRowPtr[J], h = snRowPtr[J + 1] - rp;
                    Assert.InRange(s.Split[J], w, h);
                    // the rows inside the task are in the task, the others above it: nothing of a task writes into another task or the top below it
                    for (int i = w; i < h; ++i) {
                        int row = snRows[rp + i];
                        if (i < s.Split[J]) Assert.True(row < c1 && colSnode[row] < s.TaskEnd[t] && colSnode[row] >= s.TaskFirst[t]);
                        else { Assert.True(row >= c1); Assert.True(!used[s.BufOff[J] + i - s.Split[J]]); used[s.BufOff[J] + i - s.Split[J]] = true; bufEnd++; }
                    }
                }
            }
            Assert.Equal(s.BufSize, bufEnd);
            for (int J = 0; J < snCount; ++J)
                if (s.TaskOf[J] < 0) Assert.Equal(snRowPtr[J + 1] - snRowPtr[J], s.Split[J]);
        }

        [Fact]
        public void Schedule_IsNullBelowTheThreshold_AndForOneTask()
        {
            var c = BuildReal(30, 30, 1, 11, 0, 8, 1000);
            int[][] an = c.AnalysisArrays;
            Assert.Null(CholeskySolveSchedule.Build(an[3].Length - 1, an[3], an[5], an[6], an[7], long.MaxValue, 8));
            Assert.Null(CholeskySolveSchedule.Build(an[3].Length - 1, an[3], an[5], an[6], an[7], 0, 1));
            Assert.NotNull(CholeskySolveSchedule.Build(an[3].Length - 1, an[3], an[5], an[6], an[7], 0, 8));
        }
    }
}
