// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3.CoreMathPort;

namespace g3
{
    /// <summary>
    /// Sparse Cholesky factorization A = L L^T of a symmetric positive definite matrix, for systems that are
    /// solved many times (parameterization, Laplacians with fixed boundary values). The caller makes sure the
    /// matrix is positive definite; a singular one is not reliably rejected (see IsValid).
    ///
    /// Usage: Add() the entries, Factorize(), then Solve() any number of right-hand sides. To refactor a matrix
    /// with new values, Clear() and Add() again; if the nonzero pattern is the same, the ordering and the
    /// structure of L are reused and only the numeric factorization runs.
    ///
    /// The unknowns are ordered by approximate minimum degree (on the graph of blocks of BlockSize unknowns,
    /// eg 2 for interleaved u,v coordinates) to limit fill. The structure of L comes from the postordered
    /// elimination tree, the values from a left-looking supernodal Cholesky with dense blocks. Written from the
    /// papers and textbooks named in the code and in docs/SOURCES.md, no code taken over.
    /// </summary>
    public class SparseCholesky
    {
        /// <summary> number of unknowns </summary>
        public readonly int N;
        /// <summary> unknowns [k*BlockSize, (k+1)*BlockSize) are ordered together </summary>
        public readonly int BlockSize;
        /// <summary> reason the last Factorize() failed, or null </summary>
        public string Error;
        /// <summary> nonzeros of L (after Factorize) </summary>
        public int FactorNonZeros { get { return Lp == null ? 0 : Lp[N]; } }

        // accumulated entries, lower triangle (r >= c)
        List<int> tRow = new List<int>(), tCol = new List<int>();
        List<double> tVal = new List<double>();

        // pattern of A (lower triangle, original indices, compressed by column) from the last analysis
        int[] aColPtr, aRow;
        double[] aVal;

        // ordering: perm[k] = original index of the k-th unknown, iperm = inverse
        int[] perm, iperm;
        // structure of L by column (permuted indices); the diagonal is the first entry of each column, rows ascending
        int[] Lp, Li;
        // supernodes: columns [snFirst[s], snFirst[s+1]) share the rows snRows[snRowPtr[s] ..]; their values are a
        // dense row-major block (rows x width) at Lx[snVal[s]]
        int snCount;
        int[] snFirst, snVal, colSnode, snRowPtr, snRows;
        double[] Lx;
        // permuted entries of A by column: (permuted row, index into aVal)
        int[] apColPtr, apRow, apSrc;

        bool factored;
        // test switch: run the scalar kernels even where the vector kernels are available (the results are the same bits)
        internal bool ForceScalarKernels;
        // the values of L (dense blocks of the supernodes), for tests
        internal double[] FactorValues { get { return Lx; } }
        // ordering, structure of L, supernodes and permuted pattern of A, for tests
        internal int[][] AnalysisArrays { get { return new[] { perm, Lp, Li, snFirst, snVal, snRowPtr, snRows, colSnode, apColPtr, apRow, apSrc }; } }
        // slot in the compressed pattern of each entry of the last Factorize, to refill in the same order
        int[] slotOf, prevRow, prevCol;

        public SparseCholesky(int n, int blockSize = 1)
        {
            if (blockSize < 1 || n % blockSize != 0)
                throw new ArgumentException("SparseCholesky: n must be a multiple of blockSize");
            N = n;
            BlockSize = blockSize;
        }

        /// <summary>
        /// The symmetric n x n matrix from the entries of one triangle (an off-diagonal entry once, in either order;
        /// entries given more than once are summed, so per-element assembly is fine), factorized at once: check
        /// IsValid, then Solve().
        /// </summary>
        public SparseCholesky(int n, IEnumerable<KeyValuePair<Index2i, double>> entries) : this(n)
        {
            foreach (var kv in entries) {
                if (kv.Key.a < 0 || kv.Key.b < 0 || kv.Key.a >= n || kv.Key.b >= n)
                    throw new ArgumentException("SparseCholesky: entry out of range");
                Add(kv.Key.a, kv.Key.b, kv.Value);
            }
            Factorize();
        }

        /// <summary> the matrix (each entry stored once), factorized at once: check IsValid, then Solve() </summary>
        public SparseCholesky(SymmetricSparseMatrix m) : this(m.Rows, m.NonZeros())
        {
        }

        /// <summary>
        /// The last Factorize() ran to the end: every pivot was positive, above 1e-14 times the absolute value of
        /// the diagonal entry of A it came from (false before the first Factorize() and after Clear()). False for clearly indefinite matrices, and for singular ones only where rounding leaves a pivot
        /// at or below the bound (eg the Laplacian of a small graph with integer weights, whose last pivot is 0).
        ///
        /// It is not a test for singularity: on a positive semidefinite matrix the last pivots are rounding noise,
        /// and they can come out positive and above the bound, eg on a graph Laplacian without a fixed vertex with
        /// edge weights of different magnitudes. IsValid is then true and Solve() returns
        /// values dominated by a huge, arbitrary multiple of the null vector. No
        /// bound on the pivots can fix this (a matrix with condition number near 1e16 is indistinguishable from a
        /// singular one in double precision), so make the matrix definite before factorizing it: fix (pin) at
        /// least one unknown per connected component of a Laplacian, or add a mass term.
        /// </summary>
        public bool IsValid { get { return factored; } }

        /// <summary>
        /// Add v to A[r,c] and, by symmetry, to A[c,r]: each off-diagonal entry is added once, in either order.
        /// </summary>
        public void Add(int r, int c, double v)
        {
            if (r < c) { int t = r; r = c; c = t; }
            tRow.Add(r); tCol.Add(c); tVal.Add(v);
        }

        /// <summary> remove all entries (the ordering of the previous Factorize is kept for reuse) </summary>
        public void Clear()
        {
            tRow.Clear(); tCol.Clear(); tVal.Clear();
            factored = false;
        }

        /// <summary>
        /// Factorize the accumulated matrix. Returns false (and sets Error) if a pivot is not positive; see IsValid
        /// for why a singular matrix can still be accepted.
        /// </summary>
        public bool Factorize()
        {
            Error = null;
            factored = false;
            int[] colPtr, row; double[] val;
            compress(out colPtr, out row, out val);
            if (Error != null)
                return false;
            if (aColPtr == null || !samePattern(colPtr, row)) {
                aColPtr = colPtr; aRow = row;
                analyze();
            }
            aVal = val;
            logAbsL = null;
            factored = numeric();
            return factored;
        }

        /// <summary> solve A x = b (b is not modified; x and b may be the same array) </summary>
        public void Solve(double[] b, double[] x)
        {
            if (!factored)
                throw new InvalidOperationException("SparseCholesky.Solve: matrix is not factorized");
            double[] y = new double[N];
            for (int k = 0; k < N; ++k)
                y[k] = b[perm[k]];
            // L y = Pb
            for (int J = 0; J < snCount; ++J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
                for (int c = 0; c < w; ++c) {
                    double sum = y[f + c];
                    int co = off + c * w;
                    for (int t = 0; t < c; ++t)
                        sum -= Lx[co + t] * y[f + t];
                    y[f + c] = sum / Lx[co + c];
                }
                for (int i = w; i < h; ++i) {
                    double sum = 0;
                    int io = off + i * w;
                    for (int t = 0; t < w; ++t)
                        sum += Lx[io + t] * y[f + t];
                    y[snRows[rp + i]] -= sum;
                }
            }
            // L^T z = y
            for (int J = snCount - 1; J >= 0; --J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
                for (int c = w - 1; c >= 0; --c) {
                    double sum = y[f + c];
                    for (int i = c + 1; i < h; ++i)
                        sum -= Lx[off + i * w + c] * y[snRows[rp + i]];
                    y[f + c] = sum / Lx[off + c * w + c];
                }
            }
            for (int k = 0; k < N; ++k)
                x[perm[k]] = y[k];
        }


        /// <summary>
        /// Solve A x = b in the log domain, for an M-matrix A (positive diagonal, off-diagonal entries &lt;= 0)
        /// and b &gt;= 0: logB[i] = log(b[i]) (negative infinity for 0), logX[i] = log(x[i]). The Cholesky factor
        /// of an M-matrix has no positive off-diagonal entries, so both substitutions only add non-negative
        /// terms: every entry of x is as accurate as with Solve(), relative to itself, and none underflows
        /// (eg heat 1000 edge lengths from its source, below 1e-308). Returns false (logX undefined) if the
        /// factor has a positive off-diagonal entry, ie A is not an M-matrix.
        /// </summary>
        public bool SolveLog(double[] logB, double[] logX)
        {
            if (!factored)
                throw new InvalidOperationException("SparseCholesky.SolveLog: matrix is not factorized");
            if (logAbsL == null) {
                var la = new double[Lx.Length];
                for (int k = 0; k < la.Length; ++k) la[k] = CoreMath.Log(Math.Abs(Lx[k]));
                logAbsL = la;
            }
            double[] y = new double[N];
            for (int k = 0; k < N; ++k)
                y[k] = logB[perm[k]];
            // L y = Pb: y_c = (b_c + sum_t |L_ct| y_t) / L_cc
            for (int J = 0; J < snCount; ++J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
                for (int c = 0; c < w; ++c) {
                    double acc = y[f + c];
                    int co = off + c * w;
                    for (int t = 0; t < c; ++t)
                        if (add_term(ref acc, Lx[co + t], logAbsL[co + t], y[f + t]) == false) return false;
                    y[f + c] = acc - logAbsL[co + c];
                }
                for (int i = w; i < h; ++i) {
                    double acc = double.NegativeInfinity;
                    int io = off + i * w;
                    for (int t = 0; t < w; ++t)
                        if (add_term(ref acc, Lx[io + t], logAbsL[io + t], y[f + t]) == false) return false;
                    y[snRows[rp + i]] = LogAdd(y[snRows[rp + i]], acc);
                }
            }
            // L^T z = y
            for (int J = snCount - 1; J >= 0; --J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
                for (int c = w - 1; c >= 0; --c) {
                    double acc = y[f + c];
                    for (int i = c + 1; i < h; ++i)
                        if (add_term(ref acc, Lx[off + i * w + c], logAbsL[off + i * w + c], y[snRows[rp + i]]) == false) return false;
                    y[f + c] = acc - logAbsL[off + c * w + c];
                }
            }
            for (int k = 0; k < N; ++k)
                logX[perm[k]] = y[k];
            return true;
        }

        double[] logAbsL;           // log |Lx|, for SolveLog (built on first use after a factorization)

        // acc = log(exp(acc) - l * exp(logY)) for an entry l <= 0 of the factor; false if l > 0 meets a non-zero y
        static bool add_term(ref double acc, double l, double logAbsl, double logY)
        {
            if (l == 0 || double.IsNegativeInfinity(logY)) return true;
            if (l > 0) return false;
            acc = LogAdd(acc, logAbsl + logY);
            return true;
        }

        /// <summary> log(exp(a) + exp(b)) without overflow or underflow </summary>
        internal static double LogAdd(double a, double b)
        {
            if (double.IsNegativeInfinity(a)) return b;
            if (double.IsNegativeInfinity(b)) return a;
            double m = Math.Max(a, b);
            return m + CoreMath.Log(1 + CoreMath.Exp(-Math.Abs(a - b)));
        }


        // sort the entries by column, then row, and sum duplicates. If the entries come in the same order as
        // last time (refilling a matrix), their slots are reused.
        void compress(out int[] colPtr, out int[] row, out double[] val)
        {
            int m = tRow.Count;
            if (slotOf != null && slotOf.Length == m) {
                bool same = true;
                for (int e = 0; e < m && same; ++e)
                    same = tRow[e] == prevRow[e] && tCol[e] == prevCol[e];
                if (same) {
                    colPtr = aColPtr; row = aRow;
                    val = new double[aRow.Length];
                    for (int e = 0; e < m; ++e)
                        val[slotOf[e]] += tVal[e];
                    return;
                }
            }
            int[] count = new int[N + 1];
            for (int e = 0; e < m; ++e) {
                int r = tRow[e], c = tCol[e];
                if (c < 0 || r >= N) {
                    Error = "entry (" + r + "," + c + ") out of range";
                    colPtr = row = null; val = null;
                    return;
                }
                count[c + 1]++;
            }
            // every diagonal entry is part of the pattern, even if it was never added
            for (int c = 0; c < N; ++c)
                count[c + 1]++;
            for (int c = 0; c < N; ++c)
                count[c + 1] += count[c];
            int[] next = (int[])count.Clone();
            int[] rows = new int[m + N], ids = new int[m + N];
            for (int c = 0; c < N; ++c) {
                rows[next[c]] = c; ids[next[c]] = -1; next[c]++;
            }
            for (int e = 0; e < m; ++e) {
                int c = tCol[e];
                rows[next[c]] = tRow[e]; ids[next[c]] = e; next[c]++;
            }
            colPtr = new int[N + 1];
            int[] slots = new int[m];
            var outRows = new List<int>(m / 2 + N);
            for (int c = 0; c < N; ++c) {
                int p0 = count[c], len = count[c + 1] - p0;
                Array.Sort(rows, ids, p0, len);
                colPtr[c] = outRows.Count;
                for (int q = p0; q < p0 + len; ++q) {
                    if (outRows.Count == colPtr[c] || outRows[outRows.Count - 1] != rows[q])
                        outRows.Add(rows[q]);
                    if (ids[q] >= 0) slots[ids[q]] = outRows.Count - 1;
                }
            }
            colPtr[N] = outRows.Count;
            row = outRows.ToArray();
            val = new double[row.Length];
            for (int e = 0; e < m; ++e)
                val[slots[e]] += tVal[e];
            slotOf = slots;
            prevRow = tRow.ToArray(); prevCol = tCol.ToArray();
        }

        bool samePattern(int[] colPtr, int[] row)
        {
            if (row.Length != aRow.Length)
                return false;
            for (int i = 0; i <= N; ++i)
                if (colPtr[i] != aColPtr[i]) return false;
            for (int i = 0; i < row.Length; ++i)
                if (row[i] != aRow[i]) return false;
            return true;
        }


        // ordering, structure of L, supernodes and permuted pattern of A: the shared analysis (CholeskyAnalysis)
        void analyze()
        {
            var an = new CholeskyAnalysis(N, BlockSize, aColPtr, aRow, false);
            perm = an.Perm; iperm = an.IPerm; Lp = an.Lp; Li = an.Li;
            snCount = an.SnCount; snFirst = an.SnFirst; snVal = an.SnVal; colSnode = an.ColSnode; snRowPtr = an.SnRowPtr; snRows = an.SnRows;
            apColPtr = an.ApColPtr; apRow = an.ApRow; apSrc = an.ApSrc;
            Lx = new double[an.ValueCount];
        }


        // left-looking supernodal Cholesky (E. Ng, B. Peyton, Block sparse Cholesky algorithms on advanced
        // uniprocessor computers, SIAM J. Sci. Comput. 1993) on the structure from analyze()
        bool numeric()
        {
            int[] relmap = new int[N];
            int[] head = new int[snCount], link = new int[snCount], pos = new int[snCount];
            for (int J = 0; J < snCount; ++J)
                head[J] = -1;
            double[] diagA = new double[N];
            for (int J = 0; J < snCount; ++J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J], lJ = f + w;
                for (int i = 0; i < h; ++i)
                    relmap[snRows[rp + i]] = i;
                Array.Clear(Lx, off, h * w);
                for (int c = 0; c < w; ++c) {
                    int j = f + c;
                    for (int q = apColPtr[j]; q < apColPtr[j + 1]; ++q) {
                        int r = apRow[q];
                        double v = aVal[apSrc[q]];
                        Lx[off + relmap[r] * w + c] += v;
                        if (r == j) diagA[j] += v;
                    }
                }
                // updates from the supernodes K with rows in the columns of J
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
                if (!factorBlock(J, diagA))
                    return false;
                pos[J] = w;
                if (w < h) {
                    int JS = colSnode[snRows[rp + w]];
                    link[J] = head[JS]; head[JS] = J;
                }
            }
            return true;
        }

        // L_J[row of i, column of row c] -= L_K[i,:] . L_K[c,:] for the rows c in [p0, p1) of K and i >= c
        // (row by row, so that the writes go to one row of the block of J at a time)
        unsafe void updateBlock(int K, int J, int p0, int p1, int[] relmap)
        {
            int f = snFirst[J], w = snFirst[J + 1] - f, off = snVal[J];
            int kw = snFirst[K + 1] - snFirst[K], krp = snRowPtr[K], kh = snRowPtr[K + 1] - krp, koff = snVal[K];
            bool vec = !ForceScalarKernels && SparseCholeskyKernels.VectorPath;
            double* r4 = stackalloc double[4];
            fixed (double* L = Lx)
            fixed (int* rows = snRows, rel = relmap) {
                int* krows = rows + krp;
                double* LK = L + koff;
                for (int i = p0; i < kh; ++i) {
                    double* li = LK + i * kw;
                    double* dst = L + off + rel[krows[i]] * w - f;
                    int cend = Math.Min(i + 1, p1);
                    int c = p0;
                    if (vec)
                        for (; c + 4 <= cend; c += 4) {
                            SparseCholeskyKernels.Dot4Cols(li, LK + c * kw, kw, kw, r4, true);
                            dst[krows[c]] -= r4[0]; dst[krows[c + 1]] -= r4[1];
                            dst[krows[c + 2]] -= r4[2]; dst[krows[c + 3]] -= r4[3];
                        }
                    for (; c < cend; ++c)
                        dst[krows[c]] -= SparseCholeskyKernels.Dot(li, LK + c * kw, kw, vec);
                }
            }
        }

        // dense Cholesky of the diagonal block of supernode J and the rows below it, row by row
        unsafe bool factorBlock(int J, double[] diagA)
        {
            int f = snFirst[J], w = snFirst[J + 1] - f, h = snRowPtr[J + 1] - snRowPtr[J], off = snVal[J];
            double[] inv = new double[w];
            bool vec = !ForceScalarKernels && SparseCholeskyKernels.VectorPath;
            double* r4 = stackalloc double[4];
            fixed (double* L = Lx) {
                double* B = L + off;
                int i = 0;
                while (i < h) {
                    // four rows at once: their entries left of the first row's index only need earlier rows, so the
                    // four dot products run side by side (each product keeps its own order of operations)
                    int nr = vec && i + 4 <= h ? 4 : 1;
                    int cl = 0;
                    if (nr == 4) {
                        cl = Math.Min(i, w);
                        double* L0 = B + i * w;
                        for (int c = 0; c < cl; ++c) {
                            SparseCholeskyKernels.Dot4Rows(L0, w, B + c * w, c, r4, true);
                            for (int q = 0; q < 4; ++q)
                                L0[q * w + c] = (L0[q * w + c] - r4[q]) * inv[c];
                        }
                    }
                    for (int q = 0; q < nr; ++q) {
                        int row = i + q;
                        double* li = B + row * w;
                        int m = Math.Min(row, w);
                        for (int c = cl; c < m; ++c)
                            li[c] = (li[c] - SparseCholeskyKernels.Dot(li, B + c * w, c, vec)) * inv[c];
                        if (row < w) {
                            double d = li[row] - SparseCholeskyKernels.Dot(li, li, row, vec);
                            if (!(d > 1e-14 * Math.Abs(diagA[f + row])) || double.IsInfinity(d)) {
                                Error = "matrix is not positive definite (pivot " + d + " at unknown " + perm[f + row] + ")";
                                return false;
                            }
                            li[row] = Math.Sqrt(d);
                            inv[row] = 1.0 / li[row];
                        }
                    }
                    i += nr;
                }
            }
            return true;
        }
    }
}
