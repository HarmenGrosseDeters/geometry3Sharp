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
        }


        // left-looking supernodal Cholesky (E. Ng, B. Peyton, Block sparse Cholesky algorithms on advanced
        // uniprocessor computers, SIAM J. Sci. Comput. 1993) on the structure from analyze(), complex arithmetic
        bool numeric()
        {
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
                if (!factorBlock(J, diagA)) return false;
                pos[J] = w;
                if (w < h) {
                    int JS = colSnode[snRows[rp + w]];
                    link[J] = head[JS]; head[JS] = J;
                }
            }
            return true;
        }

        // L_J[row of i, column of row c] -= L_K[i,:] . conj(L_K[c,:]) for the rows c in [p0, p1) of K and i >= c
        unsafe void updateBlock(int K, int J, int p0, int p1, int[] relmap)
        {
            int f = snFirst[J], w = snFirst[J + 1] - f, off = snVal[J];
            int kw = snFirst[K + 1] - snFirst[K], krp = snRowPtr[K], kh = snRowPtr[K + 1] - krp, koff = snVal[K];
            fixed (double* LR = LxRe, LI = LxIm)
            fixed (int* rows = snRows, rel = relmap) {
                int* krows = rows + krp;
                double* KR = LR + koff, KI = LI + koff;
                for (int i = p0; i < kh; ++i) {
                    double* ar = KR + i * kw, ai = KI + i * kw;
                    int dbase = off + rel[krows[i]] * w - f;
                    int cend = Math.Min(i + 1, p1);
                    for (int c = p0; c < cend; ++c) {
                        double dr, di;
                        dotc(ar, ai, KR + c * kw, KI + c * kw, kw, out dr, out di);
                        LR[dbase + krows[c]] -= dr; LI[dbase + krows[c]] -= di;
                    }
                }
            }
        }

        // dense Cholesky of the diagonal block of supernode J and the rows below it, row by row
        unsafe bool factorBlock(int J, double[] diagA)
        {
            int f = snFirst[J], w = snFirst[J + 1] - f, h = snRowPtr[J + 1] - snRowPtr[J], off = snVal[J];
            double[] inv = new double[w];
            fixed (double* LR = LxRe, LI = LxIm) {
                double* BR = LR + off, BI = LI + off;
                for (int i = 0; i < h; ++i) {
                    double* lr = BR + i * w, li = BI + i * w;
                    int m = Math.Min(i, w);
                    for (int c = 0; c < m; ++c) {
                        double dr, di;
                        dotc(lr, li, BR + c * w, BI + c * w, c, out dr, out di);
                        lr[c] = (lr[c] - dr) * inv[c]; li[c] = (li[c] - di) * inv[c];
                    }
                    if (i < w) {
                        double dr, di;
                        dotc(lr, li, lr, li, i, out dr, out di);        // |L_i,:i|^2 (dr), di = 0
                        double d = lr[i] - dr;
                        if (!(d > 1e-14 * Math.Abs(diagA[f + i])) || double.IsInfinity(d)) {
                            Error = "matrix is not positive definite (pivot " + d + " at unknown " + perm[f + i] + ")";
                            return false;
                        }
                        lr[i] = Math.Sqrt(d); li[i] = 0;
                        inv[i] = 1.0 / lr[i];
                    }
                }
            }
            return true;
        }

        // sum over k of a[k] conj(b[k]) (a, b given as real and imaginary parts), four partial sums
        static unsafe void dotc(double* ar, double* ai, double* br, double* bi, int n, out double re, out double im)
        {
            double r0 = 0, r1 = 0, i0 = 0, i1 = 0;
            int k = 0;
            for (; k + 2 <= n; k += 2) {
                r0 += ar[k] * br[k] + ai[k] * bi[k]; i0 += ai[k] * br[k] - ar[k] * bi[k];
                r1 += ar[k + 1] * br[k + 1] + ai[k + 1] * bi[k + 1]; i1 += ai[k + 1] * br[k + 1] - ar[k + 1] * bi[k + 1];
            }
            for (; k < n; ++k) { r0 += ar[k] * br[k] + ai[k] * bi[k]; i0 += ai[k] * br[k] - ar[k] * bi[k]; }
            re = r0 + r1; im = i0 + i1;
        }

        /// <summary> solve A x = b (interleaved vectors of 2 N numbers; b is not modified; x and b may be the same array) </summary>
        public void Solve(double[] b, double[] x)
        {
            if (!factored) throw new InvalidOperationException("SparseHermitianCholesky.Solve: matrix is not factorized");
            double[] yr = new double[N], yi = new double[N];
            for (int k = 0; k < N; ++k) { yr[k] = b[2 * perm[k]]; yi[k] = b[2 * perm[k] + 1]; }
            // L y = Pb
            for (int J = 0; J < snCount; ++J) {
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
                    int row = snRows[rp + i];
                    yr[row] -= sr; yi[row] -= si;
                }
            }
            // L^H z = y
            for (int J = snCount - 1; J >= 0; --J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
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
            for (int k = 0; k < N; ++k) { x[2 * perm[k]] = yr[k]; x[2 * perm[k] + 1] = yi[k]; }
        }
    }
}
