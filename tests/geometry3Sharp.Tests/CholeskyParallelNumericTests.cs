using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The numeric factorization of SparseCholesky and SparseHermitianCholesky runs subtrees of the supernode tree as parallel tasks
    /// and the dense kernels of the large top supernodes over independent rows (CholeskyNumericPlan). The factor is the same bits as
    /// the serial left-looking loop, and so are the error and IsValid of a matrix that is not positive definite.
    /// </summary>
    public class CholeskyParallelNumericTests
    {
        static bool OneCpu { get { return Environment.ProcessorCount < 2; } }

        static SparseCholesky Real(List<(int r, int c, double re, double im)> e, int n, int bs, Action<SparseCholesky> configure, out bool ok)
        {
            var c = new SparseCholesky(n, bs);
            configure(c);
            foreach (var (r, cc, re, _) in e) c.Add(r, cc, re);
            ok = c.Factorize();
            return c;
        }

        static SparseHermitianCholesky Herm(List<(int r, int c, double re, double im)> e, int n, Action<SparseHermitianCholesky> configure, out bool ok)
        {
            var c = new SparseHermitianCholesky(n);
            configure(c);
            foreach (var (r, cc, re, im) in e) c.Add(r, cc, re, im);
            ok = c.Factorize();
            return c;
        }

        static void Serial(SparseCholesky c) { c.ForceSerialNumeric = true; }
        static void Serial(SparseHermitianCholesky c) { c.ForceSerialNumeric = true; }
        // every task and every kernel parallel, small panels
        static Action<SparseCholesky> ParallelReal(int divisor, int panel, bool scalar = false)
        {
            return c => { c.NumericMinWork = 0; c.NumericTaskDivisor = divisor; c.KernelParallelWork = 0; c.PanelWidth = panel; c.ForceScalarKernels = scalar; };
        }
        static Action<SparseHermitianCholesky> ParallelHerm(int divisor, int panel, bool scalar = false)
        {
            return c => { c.NumericMinWork = 0; c.NumericTaskDivisor = divisor; c.KernelParallelWork = 0; c.PanelWidth = panel; c.ForceScalarKernels = scalar; };
        }

        static void SameBits(double[] a, double[] b, string what)
        {
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; ++i)
                if (BitConverter.DoubleToInt64Bits(a[i]) != BitConverter.DoubleToInt64Bits(b[i]))
                    Assert.Fail(what + ": entry " + i + " differs: " + a[i].ToString("R") + " vs " + b[i].ToString("R"));
        }

        public static IEnumerable<object[]> Shapes()
        {
            yield return new object[] { 30, 30, 1, 8, 4 };
            yield return new object[] { 24, 20, 2, 4, 3 };
            yield return new object[] { 36, 17, 1, 64, 8 };
            yield return new object[] { 14, 14, 3, 6, 2 };
            yield return new object[] { 28, 28, 1, 16, 1 };
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void RealFactor_IsTheSerialFactorBitForBit(int nx, int ny, int bs, int divisor, int panel)
        {
            if (OneCpu) return;
            var e = CholeskyParallelSolveTests.GridEntries(nx, ny, bs, 7, false);
            int n = nx * ny * bs;
            bool ok;
            var serial = Real(e, n, bs, Serial, out ok); Assert.True(ok);
            foreach (bool scalar in new[] { false, true }) {
                var par = Real(e, n, bs, ParallelReal(divisor, panel, scalar), out ok);
                Assert.True(ok, par.Error);
                Assert.NotNull(par.NumericPlanForTests);
                SameBits(serial.FactorValues, par.FactorValues, "factor, scalar=" + scalar);
                for (int k = 0; k < serial.AnalysisArrays.Length; ++k) Assert.Equal(serial.AnalysisArrays[k], par.AnalysisArrays[k]);
                double[] b = new double[n]; for (int i = 0; i < n; ++i) b[i] = Math.Sin(i + 1);
                var x1 = new double[n]; var x2 = new double[n]; serial.Solve(b, x1); par.Solve(b, x2);
                SameBits(x1, x2, "solution");
            }
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void HermitianFactor_IsTheSerialFactorBitForBit(int nx, int ny, int bs, int divisor, int panel)
        {
            if (OneCpu) return;
            var e = CholeskyParallelSolveTests.GridEntries(nx, ny, 1, 8, true);
            int n = nx * ny;
            bool ok;
            var serial = Herm(e, n, Serial, out ok); Assert.True(ok);
            foreach (bool scalar in new[] { false, true }) {
                var par = Herm(e, n, ParallelHerm(divisor, panel, scalar), out ok);
                Assert.True(ok, par.Error);
                Assert.NotNull(par.NumericPlanForTests);
                SameBits(serial.FactorValues[0], par.FactorValues[0], "re, scalar=" + scalar);
                SameBits(serial.FactorValues[1], par.FactorValues[1], "im, scalar=" + scalar);
            }
        }

        // the schedule orders do not matter (parallel loops in another order), nor does the thread count
        [Fact]
        public void Factor_DoesNotDependOnTheOrderOfTheTasks()
        {
            if (OneCpu) return;
            var e = CholeskyParallelSolveTests.GridEntries(26, 26, 2, 3, false);
            bool ok;
            var serial = Real(e, 26 * 26 * 2, 2, Serial, out ok);
            foreach (int order in new[] { -1, 7, 99 }) {
                gParallel.SerialOnThisThread = true; gParallel.SerialOrderOnThisThread = order;
                try {
                    var par = Real(e, 26 * 26 * 2, 2, ParallelReal(8, 4), out ok);
                    Assert.True(ok);
                    SameBits(serial.FactorValues, par.FactorValues, "order " + order);
                } finally { gParallel.SerialOnThisThread = false; gParallel.SerialOrderOnThisThread = 0; }
            }
            for (int rep = 0; rep < 12; ++rep) {
                var par = Real(e, 26 * 26 * 2, 2, ParallelReal(8, 4), out ok);
                SameBits(serial.FactorValues, par.FactorValues, "repeat " + rep);
            }
        }

        // a matrix that is not positive definite at some unknowns: the same failure (message, IsValid) as the serial loop, which stops at the first
        public static IEnumerable<object[]> FailingUnknowns()
        {
            foreach (int u in new[] { 0, 5, 77, 301, 640, 899, 1100, 1799 }) yield return new object[] { new[] { u } };
            yield return new object[] { new[] { 1500, 40 } };
            yield return new object[] { new[] { 900, 901, 1799 } };
        }

        static List<(int r, int c, double re, double im)> Failing(int nx, int ny, int bs, int[] bad, bool complex)
        {
            var e = CholeskyParallelSolveTests.GridEntries(nx, ny, bs, 5, complex);
            for (int k = 0; k < e.Count; ++k)
                if (e[k].r == e[k].c && Array.IndexOf(bad, e[k].r) >= 0) e[k] = (e[k].r, e[k].c, -1.0, 0);
            return e;
        }

        [Theory]
        [MemberData(nameof(FailingUnknowns))]
        public void NotPositiveDefinite_GivesTheSerialError_Real(int[] bad)
        {
            if (OneCpu) return;
            var e = Failing(30, 30, 2, bad, false);
            bool ok1, ok2;
            var s = Real(e, 1800, 2, Serial, out ok1);
            var p = Real(e, 1800, 2, ParallelReal(16, 4), out ok2);
            Assert.False(ok1); Assert.False(ok2);
            Assert.Equal(s.Error, p.Error);
            Assert.False(p.IsValid);
            Assert.NotNull(p.NumericPlanForTests);
        }

        [Theory]
        [MemberData(nameof(FailingUnknowns))]
        public void NotPositiveDefinite_GivesTheSerialError_Hermitian(int[] bad)
        {
            if (OneCpu) return;
            bad = bad.Select(u => u / 2).ToArray();
            var e = Failing(30, 30, 1, bad, true);
            bool ok1, ok2;
            var s = Herm(e, 900, Serial, out ok1);
            var p = Herm(e, 900, ParallelHerm(16, 4), out ok2);
            Assert.False(ok1); Assert.False(ok2);
            Assert.Equal(s.Error, p.Error);
            Assert.False(p.IsValid);
        }

        // refactorizations: the plan of the analysis is reused, the values are new
        [Fact]
        public void Refactorize_GivesTheSerialFactor_Again()
        {
            if (OneCpu) return;
            var par = new SparseCholesky(26 * 26, 1); ParallelReal(8, 4)(par);
            var ser = new SparseCholesky(26 * 26, 1); Serial(ser);
            foreach (int seed in new[] { 1, 2, 3 }) {
                par.Clear(); ser.Clear();
                foreach (var (r, c, re, _) in CholeskyParallelSolveTests.GridEntries(26, 26, 1, seed, false, seed == 2 ? 4 : 1)) { par.Add(r, c, re); ser.Add(r, c, re); }
                Assert.True(par.Factorize()); Assert.True(ser.Factorize());
                SameBits(ser.FactorValues, par.FactorValues, "seed " + seed);
            }
            // a failing refactorization, then a good one
            par.Clear(); ser.Clear();
            foreach (var (r, c, re, _) in Failing(26, 26, 1, new[] { 400 }, false)) { par.Add(r, c, re); ser.Add(r, c, re); }
            Assert.False(par.Factorize()); Assert.False(ser.Factorize());
            Assert.Equal(ser.Error, par.Error);
            par.Clear(); ser.Clear();
            foreach (var (r, c, re, _) in CholeskyParallelSolveTests.GridEntries(26, 26, 1, 9, false)) { par.Add(r, c, re); ser.Add(r, c, re); }
            Assert.True(par.Factorize()); Assert.True(ser.Factorize());
            SameBits(ser.FactorValues, par.FactorValues, "after failure");
        }

        // a power of two scale of the matrix scales the factor exactly (sqrt of 4d is 2 sqrt(d))
        [Fact]
        public void Factor_UnderPowerOfTwoScales_ScalesExactly()
        {
            if (OneCpu) return;
            bool ok;
            var a = Real(CholeskyParallelSolveTests.GridEntries(26, 26, 2, 4, false), 26 * 26 * 2, 2, ParallelReal(8, 4), out ok);
            var b = Real(CholeskyParallelSolveTests.GridEntries(26, 26, 2, 4, false, 16), 26 * 26 * 2, 2, ParallelReal(8, 4), out ok);
            Assert.True(ok);
            double[] x = a.FactorValues, y = b.FactorValues;
            for (int i = 0; i < x.Length; ++i)
                Assert.Equal(BitConverter.DoubleToInt64Bits(x[i] * 4), BitConverter.DoubleToInt64Bits(y[i]));
        }

        [Fact]
        public void SmallSystems_UseTheSerialLoop_WithTheDefaultThreshold()
        {
            var c = new SparseCholesky(400);
            foreach (var (r, cc, re, _) in CholeskyParallelSolveTests.GridEntries(20, 20, 1, 1, false)) c.Add(r, cc, re);
            Assert.True(c.Factorize());
            Assert.Null(c.NumericPlanForTests);
        }

        // ---- the parallel kernels really ran, and the failure branches

        [Fact]
        public void ParallelKernels_RunInTheTopSupernodes_Real()
        {
            if (OneCpu) return;
            var e = CholeskyParallelSolveTests.GridEntries(36, 36, 2, 6, false);
            bool ok;
            // 4 rows left over in the last block of a panel for some supernode, vector kernels or not
            foreach (bool scalar in new[] { false, true }) {
                var p = Real(e, 36 * 36 * 2, 2, ParallelReal(8, 4, scalar), out ok);
                Assert.True(ok);
                Assert.True(p.ParallelPanelCalls > 0, "no panel factorization ran");
                Assert.True(p.ParallelUpdateCalls > 0, "no parallel update ran");
                var s = Real(e, 36 * 36 * 2, 2, Serial, out ok);
                SameBits(s.FactorValues, p.FactorValues, "scalar=" + scalar);
                Assert.Equal(0, s.ParallelPanelCalls + s.ParallelUpdateCalls);
            }
        }

        [Fact]
        public void ParallelKernels_RunInTheTopSupernodes_Hermitian()
        {
            if (OneCpu) return;
            var e = CholeskyParallelSolveTests.GridEntries(36, 36, 1, 6, true);
            bool ok;
            var p = Herm(e, 36 * 36, ParallelHerm(8, 4), out ok);
            Assert.True(ok);
            Assert.True(p.ParallelPanelCalls > 0 && p.ParallelUpdateCalls > 0);
            var s = Herm(e, 36 * 36, Serial, out ok);
            SameBits(s.FactorValues[0], p.FactorValues[0], "re"); SameBits(s.FactorValues[1], p.FactorValues[1], "im");
        }

        // two failing supernodes chosen from the layout of the plan: a task supernode below / above a top supernode. The error is the one of the
        // smaller supernode, whichever branch of numericParallel decides
        [Fact]
        public void NotPositiveDefinite_TaskAndTopSupernodes_ReportTheSmallerOne()
        {
            if (OneCpu) return;
            int nx = 30, ny = 30, n = nx * ny;
            bool ok;
            var good = Real(CholeskyParallelSolveTests.GridEntries(nx, ny, 1, 5, false), n, 1, ParallelReal(16, 4), out ok);
            var plan = good.NumericPlanForTests; Assert.NotNull(plan);
            int[][] an = good.AnalysisArrays; int[] perm = an[0], snFirst = an[3];
            int snCount = snFirst.Length - 1;
            var isTask = new bool[snCount];
            for (int t = 0; t < plan.TaskFirst.Length; ++t) for (int J = plan.TaskFirst[t]; J < plan.TaskEnd[t]; ++J) isTask[J] = true;
            int top = plan.Top.First(), lastTask = Enumerable.Range(0, snCount).Last(J => isTask[J]);
            int firstTask = Enumerable.Range(0, snCount).First(J => isTask[J]);
            int lastTop = plan.Top.Last();
            Assert.True(top < lastTask || firstTask < lastTop);
            var pairs = new List<(int a, int b)>();
            foreach (int T in plan.Top) { if (T < lastTask) pairs.Add((T, lastTask)); if (T > firstTask) pairs.Add((firstTask, T)); }
            Assert.NotEmpty(pairs);
            foreach (var (a, b) in pairs.Take(6).Concat(pairs.Skip(pairs.Count - 3))) {
                var e = Failing(nx, ny, 1, new[] { perm[snFirst[a]], perm[snFirst[b]] }, false);
                bool ok1, ok2;
                var s = Real(e, n, 1, Serial, out ok1);
                var p = Real(e, n, 1, ParallelReal(16, 4), out ok2);
                Assert.False(ok1); Assert.False(ok2);
                Assert.Equal(s.Error, p.Error);
                Assert.Contains("at unknown " + perm[snFirst[Math.Min(a, b)]] + ")", p.Error);
            }
        }

        // a changed pattern re-analyses (new plan), changed knobs rebuild the plan, between calls
        [Fact]
        public void PatternAndKnobChanges_BetweenFactorizations_KeepTheSerialBits()
        {
            if (OneCpu) return;
            int n = 26 * 26;
            var par = new SparseCholesky(n, 1); ParallelReal(8, 4)(par);
            var ser = new SparseCholesky(n, 1); Serial(ser);
            Action<List<(int r, int c, double re, double im)>> both = e => {
                par.Clear(); ser.Clear();
                foreach (var (r, c, re, _) in e) { par.Add(r, c, re); ser.Add(r, c, re); }
                Assert.True(par.Factorize()); Assert.True(ser.Factorize());
            };
            both(CholeskyParallelSolveTests.GridEntries(26, 26, 1, 1, false));
            SameBits(ser.FactorValues, par.FactorValues, "grid 26x26");
            both(CholeskyParallelSolveTests.GridEntries(13, 52, 1, 1, false));               // other pattern, same n
            SameBits(ser.FactorValues, par.FactorValues, "grid 13x52");
            var plan1 = par.NumericPlanForTests;
            par.NumericTaskDivisor = 4;
            both(CholeskyParallelSolveTests.GridEntries(13, 52, 1, 2, false));
            Assert.NotSame(plan1, par.NumericPlanForTests);
            SameBits(ser.FactorValues, par.FactorValues, "divisor 4");
            par.NumericMinWork = long.MaxValue;                                              // too little work: the serial loop
            both(CholeskyParallelSolveTests.GridEntries(13, 52, 1, 3, false));
            Assert.Null(par.NumericPlanForTests);
            SameBits(ser.FactorValues, par.FactorValues, "serial by threshold");
            par.NumericMinWork = -1;                                                         // a negative threshold is a threshold, not a missing plan
            both(CholeskyParallelSolveTests.GridEntries(26, 26, 1, 4, false));
            SameBits(ser.FactorValues, par.FactorValues, "min work -1");
        }

        [Fact]
        public void Hermitian_RefactorizeAndScale_GiveTheSerialFactor()
        {
            if (OneCpu) return;
            int n = 26 * 26;
            var par = new SparseHermitianCholesky(n); ParallelHerm(8, 4)(par);
            var ser = new SparseHermitianCholesky(n); Serial(ser);
            foreach (int seed in new[] { 1, 2, 3 }) {
                par.Clear(); ser.Clear();
                foreach (var (r, c, re, im) in CholeskyParallelSolveTests.GridEntries(26, 26, 1, seed, true, seed == 2 ? 16 : 1)) { par.Add(r, c, re, im); ser.Add(r, c, re, im); }
                Assert.True(par.Factorize()); Assert.True(ser.Factorize());
                SameBits(ser.FactorValues[0], par.FactorValues[0], "re " + seed); SameBits(ser.FactorValues[1], par.FactorValues[1], "im " + seed);
            }
            // scale 16: the factor is 4 times larger, exactly
            bool ok;
            var a = Herm(CholeskyParallelSolveTests.GridEntries(26, 26, 1, 4, true), n, ParallelHerm(8, 4), out ok);
            var b = Herm(CholeskyParallelSolveTests.GridEntries(26, 26, 1, 4, true, 16), n, ParallelHerm(8, 4), out ok);
            for (int k = 0; k < 2; ++k)
                for (int i = 0; i < a.FactorValues[k].Length; ++i)
                    Assert.Equal(BitConverter.DoubleToInt64Bits(a.FactorValues[k][i] * 4), BitConverter.DoubleToInt64Bits(b.FactorValues[k][i]));
        }

        // ---- the plan itself

        [Theory]
        [InlineData(30, 30, 1, 8), InlineData(24, 20, 2, 4), InlineData(14, 14, 3, 6)]
        public void Plan_UpdatesAreInTheOrderOfTheSerialLists_AndTasksAreClosed(int nx, int ny, int bs, int divisor)
        {
            if (OneCpu) return;
            bool ok;
            var c = Real(CholeskyParallelSolveTests.GridEntries(nx, ny, bs, 2, false), nx * ny * bs, bs, ParallelReal(divisor, 4), out ok);
            var plan = c.NumericPlanForTests;
            Assert.NotNull(plan);
            int[][] an = c.AnalysisArrays; int[] snFirst = an[3], snRowPtr = an[5], snRows = an[6], colSnode = an[7];
            int snCount = snFirst.Length - 1;
            var taskOf = Enumerable.Repeat(-1, snCount).ToArray();
            for (int t = 0; t < plan.TaskFirst.Length; ++t)
                for (int J = plan.TaskFirst[t]; J < plan.TaskEnd[t]; ++J) { Assert.Equal(-1, taskOf[J]); taskOf[J] = t; }
            Assert.Equal(Enumerable.Range(0, snCount).Where(J => taskOf[J] < 0).ToArray(), plan.Top);
            // independent replay of the serial lists
            var lists = new List<int>[snCount]; for (int J = 0; J < snCount; ++J) lists[J] = new List<int>();
            var head = new int[snCount]; var link = new int[snCount]; var pos = new int[snCount];
            for (int J = 0; J < snCount; ++J) head[J] = -1;
            for (int J = 0; J < snCount; ++J) {
                int w = snFirst[J + 1] - snFirst[J], lJ = snFirst[J + 1];
                int u = plan.UpdPtr[J];
                for (int K = head[J], next; K != -1; K = next) {
                    next = link[K];
                    int krp = snRowPtr[K], kh = snRowPtr[K + 1] - krp, p0 = pos[K], p1 = p0;
                    while (p1 < kh && snRows[krp + p1] < lJ) ++p1;
                    Assert.Equal(K, plan.UpdK[u]); Assert.Equal(p0, plan.UpdP0[u]); Assert.Equal(p1, plan.UpdP1[u]);
                    // a task supernode is only updated from its own task
                    if (taskOf[J] >= 0) Assert.Equal(taskOf[J], taskOf[K]);
                    ++u; pos[K] = p1;
                    if (p1 < kh) { int JS = colSnode[snRows[krp + p1]]; link[K] = head[JS]; head[JS] = K; }
                }
                Assert.Equal(u, plan.UpdPtr[J + 1]);
                pos[J] = w;
                if (w < snRowPtr[J + 1] - snRowPtr[J]) { int JS = colSnode[snRows[snRowPtr[J] + w]]; link[J] = head[JS]; head[JS] = J; }
            }
        }
    }
}
