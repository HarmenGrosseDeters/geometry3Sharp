// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Sparse Cholesky factorization A = L L^H of a complex Hermitian positive definite matrix, the complex counterpart of
    /// SparseCholesky: about half its storage and work for a matrix that SparseCholesky would hold as a real 2n x 2n system
    /// (a complex entry is two numbers, its 2 x 2 real block four). Written from the same papers as SparseCholesky (the
    /// ordering, the elimination tree and the supernodes are the analysis of that class, on the graph of the n unknowns; the
    /// numeric part is a left-looking supernodal Cholesky with the complex arithmetic of dense blocks stored as separate
    /// real and imaginary parts). No other code taken over.
    ///
    /// Usage: Add() the entries (the lower triangle: an entry (r, c) with r &gt; c once, its mirror (c, r) is the complex
    /// conjugate; the diagonal is real; entries given more than once are summed), Factorize(), then Solve() any number of
    /// right-hand sides. Vectors are interleaved: element i is (v[2 i], v[2 i + 1]) = (real, imaginary part).
    /// The caller makes sure the matrix is positive definite (see SparseCholesky.IsValid for what a pivot check can
    /// and cannot tell). Serial and deterministic.
    /// </summary>
    public class SparseHermitianCholesky
    {
        /// <summary> number of (complex) unknowns </summary>
        public readonly int N;
        /// <summary> reason the last Factorize() failed, or null </summary>
        public string Error;
        /// <summary> nonzeros of L (complex entries, after Factorize) </summary>
        public int FactorNonZeros { get { return Lp == null ? 0 : Lp[N]; } }

        List<int> tRow = new List<int>(), tCol = new List<int>();
        List<double> tRe = new List<double>(), tIm = new List<double>();

        int[] aColPtr, aRow;
        double[] aRe, aIm;
        int[] perm, iperm;
        int[] Lp, Li;
        int snCount;
        int[] snFirst, snVal, colSnode, snRowPtr, snRows;
        double[] LxRe, LxIm;
        int[] apColPtr, apRow, apSrc;
        bool factored;
        // test switch: run the scalar kernels even where the vector kernels are available (the results are the same bits)
        internal bool ForceScalarKernels;
        // values of L and the analysis arrays (ordering, structure of L, supernodes, permuted pattern of A), for tests
        internal double[][] FactorValues { get { return new[] { LxRe, LxIm }; } }
        internal int[][] AnalysisArrays { get { return new[] { perm, Lp, Li, snFirst, snVal, snRowPtr, snRows, colSnode, apColPtr, apRow, apSrc }; } }

        public SparseHermitianCholesky(int n)
        {
            if (n < 0) throw new ArgumentException("SparseHermitianCholesky: n must not be negative");
            N = n;
        }

        public bool IsValid { get { return factored; } }

        /// <summary> add re + i im to A[r,c] and its conjugate to A[c,r] (r == c: the imaginary part must be 0) </summary>
        public void Add(int r, int c, double re, double im)
        {
            if (r < c) { int t = r; r = c; c = t; im = -im; }
            tRow.Add(r); tCol.Add(c); tRe.Add(re); tIm.Add(im);
        }

        public void Clear()
        {
            tRow.Clear(); tCol.Clear(); tRe.Clear(); tIm.Clear();
            factored = false;
        }

        public bool Factorize()
        {
            Error = null;
            factored = false;
            colCopyBuilt = false; colOff = null; colRe = colIm = null;
            compress();
            if (Error != null) return false;
            analyze();
            factored = numeric();
            return factored;
        }

        // sort the entries by column, then row, and sum duplicates
        void compress()
        {
            int m = tRow.Count;
            int[] count = new int[N + 1];
            for (int e = 0; e < m; ++e) {
                int r = tRow[e], c = tCol[e];
                if (c < 0 || r >= N) { Error = "entry (" + r + "," + c + ") out of range"; return; }
                count[c + 1]++;
            }
            for (int c = 0; c < N; ++c) count[c + 1]++;      // every diagonal entry is part of the pattern
            for (int c = 0; c < N; ++c) count[c + 1] += count[c];
            int[] next = (int[])count.Clone();
            int[] rows = new int[m + N], ids = new int[m + N];
            for (int c = 0; c < N; ++c) { rows[next[c]] = c; ids[next[c]] = -1; next[c]++; }
            for (int e = 0; e < m; ++e) { int c = tCol[e]; rows[next[c]] = tRow[e]; ids[next[c]] = e; next[c]++; }
            aColPtr = new int[N + 1];
            int[] slots = new int[m];
            var outRows = new List<int>(m / 2 + N);
            for (int c = 0; c < N; ++c) {
                int p0 = count[c], len = count[c + 1] - p0;
                Array.Sort(rows, ids, p0, len);
                aColPtr[c] = outRows.Count;
                for (int q = p0; q < p0 + len; ++q) {
                    if (outRows.Count == aColPtr[c] || outRows[outRows.Count - 1] != rows[q])
                        outRows.Add(rows[q]);
                    if (ids[q] >= 0) slots[ids[q]] = outRows.Count - 1;
                }
            }
            aColPtr[N] = outRows.Count;
            aRow = outRows.ToArray();
            aRe = new double[aRow.Length]; aIm = new double[aRow.Length];
            for (int e = 0; e < m; ++e) { aRe[slots[e]] += tRe[e]; aIm[slots[e]] += tIm[e]; }
        }

        // ordering, structure of L, supernodes and permuted pattern of A: the shared analysis (CholeskyAnalysis); the permuted
        // entries below the diagonal of the original but above it after the ordering are marked as conjugates in apSrc
        void analyze()
        {
            var an = new CholeskyAnalysis(N, 1, aColPtr, aRow, true);
            perm = an.Perm; iperm = an.IPerm; Lp = an.Lp; Li = an.Li;
            snCount = an.SnCount; snFirst = an.SnFirst; snVal = an.SnVal; colSnode = an.ColSnode; snRowPtr = an.SnRowPtr; snRows = an.SnRows;
            apColPtr = an.ApColPtr; apRow = an.ApRow; apSrc = an.ApSrc;
            LxRe = new double[an.ValueCount]; LxIm = new double[an.ValueCount];
            scheduleBuilt = false; planBuilt = false; colCopyBuilt = false; colOff = null; colRe = colIm = null;
        }


        // left-looking supernodal Cholesky (E. Ng, B. Peyton, Block sparse Cholesky algorithms on advanced
        // uniprocessor computers, SIAM J. Sci. Comput. 1993) on the structure from analyze(), complex arithmetic
        bool numeric()
        {
            if (!ForceSerialNumeric) {
                CholeskyNumericPlan plan = getNumericPlan();
                if (plan != null) return numericParallel(plan);
            }
            int[] relmap = new int[N];
            int[] head = new int[snCount], link = new int[snCount], pos = new int[snCount];
            for (int J = 0; J < snCount; ++J) head[J] = -1;
            double[] diagA = new double[N];
            for (int J = 0; J < snCount; ++J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J], lJ = f + w;
                for (int i = 0; i < h; ++i) relmap[snRows[rp + i]] = i;
                Array.Clear(LxRe, off, h * w); Array.Clear(LxIm, off, h * w);
                for (int c = 0; c < w; ++c) {
                    int j = f + c;
                    for (int q = apColPtr[j]; q < apColPtr[j + 1]; ++q) {
                        int r = apRow[q];
                        int s = apSrc[q];
                        int slot = off + relmap[r] * w + c;
                        // the permuted entry is the lower-triangle entry (r, j) if the original was below the diagonal in the
                        // permuted order too, else its conjugate (see the flag in apSrc)
                        double vr = aRe[s < 0 ? ~s : s], vi = aIm[s < 0 ? ~s : s];
                        if (s < 0) vi = -vi;
                        LxRe[slot] += vr; LxIm[slot] += vi;
                        if (r == j) diagA[j] += vr;
                    }
                }
                int K = head[J];
                while (K != -1) {
                    int knext = link[K];
                    int krp = snRowPtr[K], kh = snRowPtr[K + 1] - krp;
                    int p0 = pos[K], p1 = p0;
                    while (p1 < kh && snRows[krp + p1] < lJ) p1++;
                    updateBlock(K, J, p0, p1, relmap);
                    pos[K] = p1;
                    if (p1 < kh) {
                        int JS = colSnode[snRows[krp + p1]];
                        link[K] = head[JS]; head[JS] = K;
                    }
                    K = knext;
                }
                string ferr;
                if (!factorBlock(J, diagA, out ferr)) { Error = ferr; return false; }
                pos[J] = w;
                if (w < h) {
                    int JS = colSnode[snRows[rp + w]];
                    link[J] = head[JS]; head[JS] = J;
                }
            }
            return true;
        }

        // L_J[row of i, column of row c] -= L_K[i,:] . conj(L_K[c,:]) for the rows c in [p0, p1) of K and i >= c
        void updateBlock(int K, int J, int p0, int p1, int[] relmap)
        {
            updateRows(K, J, p0, p1, relmap, p0, snRowPtr[K + 1] - snRowPtr[K]);
        }

        // the rows i in [iStart, iEnd) of the update (each writes its own row of the block of J)
        unsafe void updateRows(int K, int J, int p0, int p1, int[] relmap, int iStart, int iEnd)
        {
            int f = snFirst[J], w = snFirst[J + 1] - f, off = snVal[J];
            int kw = snFirst[K + 1] - snFirst[K], krp = snRowPtr[K], kh = snRowPtr[K + 1] - krp, koff = snVal[K];
            bool vec = !ForceScalarKernels && SparseHermitianCholeskyKernels.VectorPath;
            double* rr = stackalloc double[4], ri = stackalloc double[4];
            fixed (double* LR = LxRe, LI = LxIm)
            fixed (int* rows = snRows, rel = relmap) {
                int* krows = rows + krp;
                double* KR = LR + koff, KI = LI + koff;
                for (int i = iStart; i < iEnd; ++i) {
                    double* ar = KR + i * kw, ai = KI + i * kw;
                    int dbase = off + rel[krows[i]] * w - f;
                    int cend = Math.Min(i + 1, p1);
                    int c = p0;
                    // four rows c at once (each product keeps its own order of operations)
                    if (vec)
                        for (; c + 4 <= cend; c += 4) {
                            SparseHermitianCholeskyKernels.Dotc4Cols(ar, ai, KR + c * kw, KI + c * kw, kw, kw, rr, ri, true);
                            for (int q = 0; q < 4; ++q) { LR[dbase + krows[c + q]] -= rr[q]; LI[dbase + krows[c + q]] -= ri[q]; }
                        }
                    for (; c < cend; ++c) {
                        double dr, di;
                        SparseHermitianCholeskyKernels.Dotc(ar, ai, KR + c * kw, KI + c * kw, kw, out dr, out di, vec);
                        LR[dbase + krows[c]] -= dr; LI[dbase + krows[c]] -= di;
                    }
                }
            }
        }

        // dense Cholesky of the diagonal block of supernode J and the rows below it, row by row
        unsafe bool factorBlock(int J, double[] diagA, out string err)
        {
            err = null;
            System.Threading.Interlocked.Increment(ref ParallelPanelCalls);
            int f = snFirst[J], w = snFirst[J + 1] - f, h = snRowPtr[J + 1] - snRowPtr[J], off = snVal[J];
            double[] inv = new double[w];
            bool vec = !ForceScalarKernels && SparseHermitianCholeskyKernels.VectorPath;
            double* rr = stackalloc double[4], ri = stackalloc double[4];
            fixed (double* LR = LxRe, LI = LxIm) {
                double* BR = LR + off, BI = LI + off;
                int i = 0;
                while (i < h) {
                    // four rows at once: their entries left of the first row's index only need earlier rows, so the
                    // four products run side by side (each product keeps its own order of operations)
                    int nr = vec && i + 4 <= h ? 4 : 1;
                    int cl = 0;
                    if (nr == 4) {
                        cl = Math.Min(i, w);
                        double* L0R = BR + i * w, L0I = BI + i * w;
                        for (int c = 0; c < cl; ++c) {
                            SparseHermitianCholeskyKernels.Dotc4Rows(L0R, L0I, w, BR + c * w, BI + c * w, c, rr, ri, true);
                            for (int q = 0; q < 4; ++q) {
                                L0R[q * w + c] = (L0R[q * w + c] - rr[q]) * inv[c]; L0I[q * w + c] = (L0I[q * w + c] - ri[q]) * inv[c];
                            }
                        }
                    }
                    for (int q = 0; q < nr; ++q) {
                        int row = i + q;
                        double* lr = BR + row * w, li = BI + row * w;
                        int m = Math.Min(row, w);
                        for (int c = cl; c < m; ++c) {
                            double dr, di;
                            SparseHermitianCholeskyKernels.Dotc(lr, li, BR + c * w, BI + c * w, c, out dr, out di, vec);
                            lr[c] = (lr[c] - dr) * inv[c]; li[c] = (li[c] - di) * inv[c];
                        }
                        if (row < w) {
                            double dr, di;
                            SparseHermitianCholeskyKernels.Dotc(lr, li, lr, li, row, out dr, out di, vec);        // |L_i,:i|^2 (dr), di = 0
                            double d = lr[row] - dr;
                            if (!(d > 1e-14 * Math.Abs(diagA[f + row])) || double.IsInfinity(d)) {
                                err = "matrix is not positive definite (pivot " + d + " at unknown " + perm[f + row] + ")";
                                return false;
                            }
                            lr[row] = Math.Sqrt(d); li[row] = 0;
                            inv[row] = 1.0 / lr[row];
                        }
                    }
                    i += nr;
                }
            }
            return true;
        }

        // ---- numeric factorization in parallel (CholeskyNumericPlan): the same operations on every entry as numeric()

        // test switch: the serial numeric loop even where the plan of parallel tasks applies (the factor is the same bits)
        internal bool ForceSerialNumeric;
        // work below which the factorization stays serial, and the task size as a fraction of the work (tests lower them)
        internal long NumericMinWork = CholeskyNumericPlan.DefaultMinWork;
        internal int NumericTaskDivisor = CholeskyNumericPlan.DefaultDivisor;
        // work of an update or of a dense block from which its rows run in parallel, and the width of a panel of columns of the dense factorization
        internal long KernelParallelWork = 300000;
        internal int PanelWidth = 32;
        CholeskyNumericPlan numericPlan; bool planBuilt; long planMinWork; int planDivisor;
        // calls of the parallel kernels (tests: the parallel paths ran)
        internal int ParallelUpdateCalls, ParallelPanelCalls;

        internal CholeskyNumericPlan NumericPlanForTests { get { return getNumericPlan(); } }

        CholeskyNumericPlan getNumericPlan()
        {
            if (Environment.ProcessorCount < 2) return null;
            if (!planBuilt || planMinWork != NumericMinWork || planDivisor != NumericTaskDivisor) {
                planBuilt = true;
                numericPlan = CholeskyNumericPlan.Build(snCount, snFirst, snRowPtr, snRows, colSnode, NumericMinWork, NumericTaskDivisor);
                planMinWork = NumericMinWork; planDivisor = NumericTaskDivisor;
            }
            return numericPlan;
        }

        bool numericParallel(CholeskyNumericPlan plan)
        {
            double[] diagA = new double[N];
            int nt = plan.TaskFirst.Length;
            var failJ = new int[nt]; var failMsg = new string[nt];
            for (int t = 0; t < nt; ++t) failJ[t] = -1;
            gParallel.ForRange<int[]>(nt, () => new int[N], (t, relmap) => {
                for (int J = plan.TaskFirst[t]; J < plan.TaskEnd[t]; ++J) {
                    string err;
                    if (!supernodeNumeric(plan, J, relmap, diagA, false, out err)) { failJ[t] = J; failMsg[t] = err; break; }
                }
            });
            // the serial loop stops at the first failing supernode: report that one
            int minFail = -1; string minMsg = null;
            for (int t = 0; t < nt; ++t)
                if (failJ[t] >= 0 && (minFail < 0 || failJ[t] < minFail)) { minFail = failJ[t]; minMsg = failMsg[t]; }
            int[] topMap = new int[N];
            foreach (int T in plan.Top) {
                if (minFail >= 0 && minFail < T) break;
                string err;
                if (!supernodeNumeric(plan, T, topMap, diagA, true, out err)) { Error = err; return false; }
            }
            if (minFail >= 0) { Error = minMsg; return false; }
            return true;
        }

        // supernode J: the entries of A, the updates in the order of the plan, the dense factorization
        bool supernodeNumeric(CholeskyNumericPlan plan, int J, int[] relmap, double[] diagA, bool par, out string err)
        {
            int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
            for (int i = 0; i < h; ++i) relmap[snRows[rp + i]] = i;
            Array.Clear(LxRe, off, h * w); Array.Clear(LxIm, off, h * w);
            for (int c = 0; c < w; ++c) {
                int j = f + c;
                for (int q = apColPtr[j]; q < apColPtr[j + 1]; ++q) {
                    int r = apRow[q];
                    int s = apSrc[q];
                    int slot = off + relmap[r] * w + c;
                    double vr = aRe[s < 0 ? ~s : s], vi = aIm[s < 0 ? ~s : s];
                    if (s < 0) vi = -vi;
                    LxRe[slot] += vr; LxIm[slot] += vi;
                    if (r == j) diagA[j] += vr;
                }
            }
            for (int u = plan.UpdPtr[J]; u < plan.UpdPtr[J + 1]; ++u) {
                int K = plan.UpdK[u], p0 = plan.UpdP0[u], p1 = plan.UpdP1[u];
                int kh = snRowPtr[K + 1] - snRowPtr[K], kw = snFirst[K + 1] - snFirst[K];
                if (par && (long)(kh - p0) * (p1 - p0) * kw >= KernelParallelWork && kh - p0 >= 8) {
                    System.Threading.Interlocked.Increment(ref ParallelUpdateCalls);
                    int rows = kh - p0, chunk = Math.Max(4, rows / 48), nch = (rows + chunk - 1) / chunk;
                    gParallel.ForRange(nch, ch => {
                        int a = p0 + ch * chunk;
                        updateRows(K, J, p0, p1, relmap, a, Math.Min(a + chunk, kh));
                    });
                } else
                    updateRows(K, J, p0, p1, relmap, p0, kh);
            }
            if (par && w >= 2 * PanelWidth && (long)w * w * h / 2 >= KernelParallelWork)
                return factorBlockParallel(J, diagA, out err);
            return factorBlock(J, diagA, out err);
        }

        // dense Cholesky of the block of J in panels of PanelWidth columns: the small triangle of a panel one row after the other, then
        // the rows below the panel (independent of each other) in parallel. Entry (i, c) is (L[i,c] - dot(L[i,:c], conj L[c,:c])) / L[c,c]
        // with the same dot product as in factorBlock, so the bits are the same.
        unsafe bool factorBlockParallel(int J, double[] diagA, out string err)
        {
            err = null;
            System.Threading.Interlocked.Increment(ref ParallelPanelCalls);
            int f = snFirst[J], w = snFirst[J + 1] - f, h = snRowPtr[J + 1] - snRowPtr[J], off = snVal[J];
            double[] inv = new double[w];
            bool vec = !ForceScalarKernels && SparseHermitianCholeskyKernels.VectorPath;
            int nb = Math.Max(1, PanelWidth);
            for (int c0 = 0; c0 < w; c0 += nb) {
                int c1 = Math.Min(c0 + nb, w);
                fixed (double* LR = LxRe, LI = LxIm) {
                    double* BR = LR + off, BI = LI + off;
                    for (int row = c0; row < c1; ++row) {
                        double* lr = BR + row * w, li = BI + row * w;
                        for (int c = c0; c < row; ++c) {
                            double dr, di;
                            SparseHermitianCholeskyKernels.Dotc(lr, li, BR + c * w, BI + c * w, c, out dr, out di, vec);
                            lr[c] = (lr[c] - dr) * inv[c]; li[c] = (li[c] - di) * inv[c];
                        }
                        double pr, pi;
                        SparseHermitianCholeskyKernels.Dotc(lr, li, lr, li, row, out pr, out pi, vec);
                        double d = lr[row] - pr;
                        if (!(d > 1e-14 * Math.Abs(diagA[f + row])) || double.IsInfinity(d)) {
                            err = "matrix is not positive definite (pivot " + d + " at unknown " + perm[f + row] + ")";
                            return false;
                        }
                        lr[row] = Math.Sqrt(d); li[row] = 0;
                        inv[row] = 1.0 / lr[row];
                    }
                }
                int blocks = (h - c1 + 3) / 4;
                if (blocks <= 0) continue;
                int cc0 = c0, cc1 = c1;
                gParallel.ForRange(blocks, b => {
                    int i0 = c1 + 4 * b, nr = Math.Min(4, h - i0);
                    unsafe {
                        double* rr = stackalloc double[4], ri = stackalloc double[4];
                        fixed (double* LR = LxRe, LI = LxIm) {
                            double* BR = LR + off, BI = LI + off;
                            if (vec && nr == 4) {
                                double* L0R = BR + i0 * w, L0I = BI + i0 * w;
                                for (int c = cc0; c < cc1; ++c) {
                                    SparseHermitianCholeskyKernels.Dotc4Rows(L0R, L0I, w, BR + c * w, BI + c * w, c, rr, ri, true);
                                    for (int q = 0; q < 4; ++q) {
                                        L0R[q * w + c] = (L0R[q * w + c] - rr[q]) * inv[c]; L0I[q * w + c] = (L0I[q * w + c] - ri[q]) * inv[c];
                                    }
                                }
                            } else
                                for (int q = 0; q < nr; ++q) {
                                    double* lr = BR + (i0 + q) * w, li = BI + (i0 + q) * w;
                                    for (int c = cc0; c < cc1; ++c) {
                                        double dr, di;
                                        SparseHermitianCholeskyKernels.Dotc(lr, li, BR + c * w, BI + c * w, c, out dr, out di, vec);
                                        lr[c] = (lr[c] - dr) * inv[c]; li[c] = (li[c] - di) * inv[c];
                                    }
                                }
                        }
                    }
                });
            }
            return true;
        }

        /// <summary> solve A x = b (interleaved vectors of 2 N numbers; b is not modified; x and b may be the same array) </summary>
        public void Solve(double[] b, double[] x)
        {
            if (!factored) throw new InvalidOperationException("SparseHermitianCholesky.Solve: matrix is not factorized");
            double[] yr = new double[N], yi = new double[N];
            for (int k = 0; k < N; ++k) { yr[k] = b[2 * perm[k]]; yi[k] = b[2 * perm[k] + 1]; }
            ensureColumnCopy();
            CholeskySolveSchedule sch = ForceSerialSolve ? null : getSolveSchedule();
            if (sch == null) {
                // L y = Pb
                for (int J = 0; J < snCount; ++J) forwardStep(J, yr, yi, null, null, 0, int.MaxValue);
                // L^H z = y
                for (int J = snCount - 1; J >= 0; --J) backwardStep(J, yr, yi);
            } else {
                // the same operations in the same order for every entry, see CholeskySolveSchedule
                double[] br = new double[sch.BufSize], bi = new double[sch.BufSize];
                gParallel.ForRange(sch.TaskFirst.Length, t => {
                    for (int J = sch.TaskFirst[t]; J < sch.TaskEnd[t]; ++J)
                        forwardStep(J, yr, yi, br, bi, sch.BufOff[J], sch.Split[J]);
                });
                for (int J = 0; J < snCount; ++J) {
                    if (sch.TaskOf[J] < 0) {
                        forwardStep(J, yr, yi, null, null, 0, int.MaxValue);
                    } else {
                        int rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, bo = sch.BufOff[J] - sch.Split[J];
                        for (int i = sch.Split[J]; i < h; ++i) {
                            int row = snRows[rp + i];
                            yr[row] -= br[bo + i]; yi[row] -= bi[bo + i];
                        }
                    }
                }
                for (int J = snCount - 1; J >= 0; --J)
                    if (sch.TaskOf[J] < 0) backwardStep(J, yr, yi);
                gParallel.ForRange(sch.TaskFirst.Length, t => {
                    for (int J = sch.TaskEnd[t] - 1; J >= sch.TaskFirst[t]; --J)
                        backwardStep(J, yr, yi);
                });
            }
            for (int k = 0; k < N; ++k) { x[2 * perm[k]] = yr[k]; x[2 * perm[k] + 1] = yi[k]; }
        }

        // test switch: the serial solve even where the schedule of parallel tasks applies (the results are the same bits)
        internal bool ForceSerialSolve;
        // work below which Solve stays serial, and the task size as a fraction of the work (tests lower them)
        internal long SolveMinWork = CholeskySolveSchedule.DefaultMinWork;
        internal int SolveTaskDivisor = CholeskySolveSchedule.DefaultDivisor;
        CholeskySolveSchedule solveSchedule; bool scheduleBuilt; long scheduleMinWork; int scheduleDivisor; readonly object scheduleLock = new object();

        internal CholeskySolveSchedule SolveScheduleForTests { get { return getSolveSchedule(); } }

        CholeskySolveSchedule getSolveSchedule()
        {
            lock (scheduleLock) {
                if (!scheduleBuilt || scheduleMinWork != SolveMinWork || scheduleDivisor != SolveTaskDivisor) {
                    scheduleBuilt = true;
                    solveSchedule = Environment.ProcessorCount < 2 ? null :
                        CholeskySolveSchedule.Build(snCount, snFirst, snRowPtr, snRows, colSnode, SolveMinWork, SolveTaskDivisor);
                    scheduleMinWork = SolveMinWork; scheduleDivisor = SolveTaskDivisor;
                }
                return solveSchedule;
            }
        }

        // forward substitution of supernode J: the updates of the rows [w, split) go into y, those of [split, h) into the buffers
        // at bufOff + (i - split) (split = int.MaxValue: all into y)
        void forwardStep(int J, double[] yr, double[] yi, double[] br, double[] bi, int bufOff, int split)
        {
            int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
            for (int c = 0; c < w; ++c) {
                double sr = yr[f + c], si = yi[f + c];
                int co = off + c * w;
                for (int t = 0; t < c; ++t) {
                    double lr = LxRe[co + t], li = LxIm[co + t];
                    sr -= lr * yr[f + t] - li * yi[f + t]; si -= lr * yi[f + t] + li * yr[f + t];
                }
                double d = LxRe[co + c];
                yr[f + c] = sr / d; yi[f + c] = si / d;
            }
            for (int i = w; i < h; ++i) {
                double sr = 0, si = 0;
                int io = off + i * w;
                for (int t = 0; t < w; ++t) {
                    double lr = LxRe[io + t], li = LxIm[io + t];
                    sr += lr * yr[f + t] - li * yi[f + t]; si += lr * yi[f + t] + li * yr[f + t];
                }
                if (i < split) { int row = snRows[rp + i]; yr[row] -= sr; yi[row] -= si; }
                else { br[bufOff + i - split] = sr; bi[bufOff + i - split] = si; }
            }
        }

        // backward substitution of supernode J. The sum of a column runs over the rows below it in the row order (one chain,
        // so its bits are fixed). The entries of a column are a stride of w apart in the row-major block, a cache miss each
        // in a wide block, so wide blocks are read from a column-major copy (the same values, the same operations)
        [System.Runtime.CompilerServices.MethodImpl(JitHints.Optimize)]
        void backwardStep(int J, double[] yr, double[] yi)
        {
            int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
            if (colOff != null && colOff[J] >= 0) {
                int co = colOff[J];
                for (int c = w - 1; c >= 0; --c) {
                    double sr = yr[f + c], si = yi[f + c];
                    int q = co + c * h - c * (c - 1) / 2 - c;         // column c, row i at q + i (i >= c)
                    for (int i = c + 1; i < h; ++i) {
                        double lr = colRe[q + i], li = colIm[q + i];
                        int row = snRows[rp + i];
                        sr -= lr * yr[row] + li * yi[row]; si -= lr * yi[row] - li * yr[row];
                    }
                    double d = colRe[q + c];
                    yr[f + c] = sr / d; yi[f + c] = si / d;
                }
                return;
            }
            for (int c = w - 1; c >= 0; --c) {
                double sr = yr[f + c], si = yi[f + c];
                for (int i = c + 1; i < h; ++i) {
                    double lr = LxRe[off + i * w + c], li = LxIm[off + i * w + c];
                    int row = snRows[rp + i];
                    sr -= lr * yr[row] + li * yi[row]; si -= lr * yi[row] - li * yr[row];
                }
                double d = LxRe[off + c * w + c];
                yr[f + c] = sr / d; yi[f + c] = si / d;
            }
        }

        // column-major copy of the wide supernodes (columns c = 0..w-1, rows c..h-1 of each), built on the first Solve after a
        // factorization (in parallel; it only moves values)
        internal int ColumnCopyMinWidth = 64;
        // read when the copy is built (the first Solve after Factorize): the copy is only made for at least MinEntries and at most
        // MaxEntries entries (256 MB for the real class, 512 MB for the Hermitian one), so Solve never needs more than that extra memory
        internal long ColumnCopyMinEntries = 200000;
        internal long ColumnCopyMaxEntries = 32000000;
        int[] colOff; double[] colRe, colIm; bool colCopyBuilt;

        // the column-major copy is in use (tests)
        internal bool ColumnCopyInUse { get { ensureColumnCopy(); return colOff != null; } }

        void ensureColumnCopy()
        {
            lock (scheduleLock) {
                if (colCopyBuilt) return;
                colCopyBuilt = true;
                var offs = new int[snCount]; long total = 0;
                for (int J = 0; J < snCount; ++J) {
                    int w = snFirst[J + 1] - snFirst[J], h = snRowPtr[J + 1] - snRowPtr[J];
                    offs[J] = -1;
                    if (w >= ColumnCopyMinWidth) { offs[J] = (int)Math.Min(total, int.MaxValue); total += (long)w * h - (long)w * (w - 1) / 2; }
                }
                if (total == 0 || total < ColumnCopyMinEntries || total > ColumnCopyMaxEntries) { colOff = null; return; }
                var cr = new double[total]; var ci = new double[total];
                var chunks = new List<int>();            // (supernode, first column of a block of 16 columns)
                for (int J = 0; J < snCount; ++J) {
                    if (offs[J] < 0) continue;
                    int w = snFirst[J + 1] - snFirst[J];
                    for (int c0 = 0; c0 < w; c0 += 16) { chunks.Add(J); chunks.Add(c0); }
                }
                gParallel.ForRange(chunks.Count / 2, k => {
                    int J = chunks[2 * k], c0 = chunks[2 * k + 1];
                    int w = snFirst[J + 1] - snFirst[J], h = snRowPtr[J + 1] - snRowPtr[J], off = snVal[J], co = offs[J];
                    int c1 = Math.Min(c0 + 16, w);
                    for (int i = c0; i < h; ++i) {
                        int src = off + i * w;
                        for (int c = c0; c < c1 && c <= i; ++c) {
                            int q = co + c * h - c * (c - 1) / 2 - c;
                            cr[q + i] = LxRe[src + c]; ci[q + i] = LxIm[src + c];
                        }
                    }
                });
                colOff = offs; colRe = cr; colIm = ci;
            }
        }
    }
}
