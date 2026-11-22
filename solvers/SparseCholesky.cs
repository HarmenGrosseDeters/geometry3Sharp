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
    /// The unknowns are ordered by approximate minimum degree by default (on the graph of blocks of BlockSize unknowns,
    /// eg 2 for interleaved u,v coordinates) to limit fill; set <see cref="Ordering"/> to
    /// <see cref="CholeskyOrdering.NestedDissection"/> for opt-in patch-guided nested dissection. The structure of L
    /// comes from the postordered elimination tree, the values from a left-looking supernodal Cholesky with dense blocks.
    /// Written from the papers and textbooks named in the code and in docs/SOURCES.md, no code taken over.
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
        /// <summary> fill-reducing ordering strategy applied on the next symbolic analysis in Factorize() (default: MinimumDegree) </summary>
        public CholeskyOrdering Ordering { get; set; } = CholeskyOrdering.MinimumDegree;

        // accumulated entries, lower triangle (r >= c)
        List<int> tRow = new List<int>(), tCol = new List<int>();
        List<double> tVal = new List<double>();

        // pattern of A (lower triangle, original indices, compressed by column) from the last analysis
        int[] aColPtr, aRow;
        double[] aVal;
        CholeskyOrdering analyzedOrdering;

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
            colCopyBuilt = false; colOff = null; colVal = null;
            int[] colPtr, row; double[] val;
            compress(out colPtr, out row, out val);
            if (Error != null)
                return false;
            if (aColPtr == null || analyzedOrdering != Ordering || !samePattern(colPtr, row)) {
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
            ensureColumnCopy();
            CholeskySolveSchedule sch = ForceSerialSolve ? null : getSolveSchedule();
            if (sch == null) {
                // L y = Pb
                for (int J = 0; J < snCount; ++J)
                    forwardStep(J, y, null, 0, int.MaxValue);
                // L^T z = y
                for (int J = snCount - 1; J >= 0; --J)
                    backwardStep(J, y);
            } else {
                // the same operations in the same order for every entry, see CholeskySolveSchedule
                double[] buf = new double[sch.BufSize];
                gParallel.ForRange(sch.TaskFirst.Length, t => {
                    for (int J = sch.TaskFirst[t]; J < sch.TaskEnd[t]; ++J)
                        forwardStep(J, y, buf, sch.BufOff[J], sch.Split[J]);
                });
                for (int J = 0; J < snCount; ++J) {
                    if (sch.TaskOf[J] < 0) {
                        forwardStep(J, y, null, 0, int.MaxValue);
                    } else {
                        int rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, bo = sch.BufOff[J] - sch.Split[J];
                        for (int i = sch.Split[J]; i < h; ++i)
                            y[snRows[rp + i]] -= buf[bo + i];
                    }
                }
                for (int J = snCount - 1; J >= 0; --J)
                    if (sch.TaskOf[J] < 0) backwardStep(J, y);
                gParallel.ForRange(sch.TaskFirst.Length, t => {
                    for (int J = sch.TaskEnd[t] - 1; J >= sch.TaskFirst[t]; --J)
                        backwardStep(J, y);
                });
            }
            for (int k = 0; k < N; ++k)
                x[perm[k]] = y[k];
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

        // forward substitution of supernode J: the updates of the rows [w, split) go into y, those of [split, h) into buf at
        // bufOff + (i - split) (split = int.MaxValue: all into y)
        [System.Runtime.CompilerServices.MethodImpl(JitHints.Optimize)]
        void forwardStep(int J, double[] y, double[] buf, int bufOff, int split)
        {
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
                if (i < split) y[snRows[rp + i]] -= sum;
                else buf[bufOff + i - split] = sum;
            }
        }

        // backward substitution of supernode J. The sum of a column runs over the rows below it in the row order (one chain,
        // so its bits are fixed). The entries of a column are a stride of w apart in the row-major block, a cache miss each
        // in a wide block, so wide blocks are read from a column-major copy (the same values, the same operations)
        [System.Runtime.CompilerServices.MethodImpl(JitHints.Optimize)]
        void backwardStep(int J, double[] y)
        {
            int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
            if (colOff != null && colOff[J] >= 0) {
                int co = colOff[J];
                for (int c = w - 1; c >= 0; --c) {
                    double sum = y[f + c];
                    int q = co + c * h - c * (c - 1) / 2 - c;         // column c, row i at q + i (i >= c)
                    for (int i = c + 1; i < h; ++i)
                        sum -= colVal[q + i] * y[snRows[rp + i]];
                    y[f + c] = sum / colVal[q + c];
                }
                return;
            }
            for (int c = w - 1; c >= 0; --c) {
                double sum = y[f + c];
                for (int i = c + 1; i < h; ++i)
                    sum -= Lx[off + i * w + c] * y[snRows[rp + i]];
                y[f + c] = sum / Lx[off + c * w + c];
            }
        }

        // column-major copy of the wide supernodes (columns c = 0..w-1, rows c..h-1 of each), built on the first Solve after a
        // factorization (in parallel; it only moves values)
        internal int ColumnCopyMinWidth = 64;
        // read when the copy is built (the first Solve after Factorize): the copy is only made for at least MinEntries and at most
        // MaxEntries entries (256 MB for the real class, 512 MB for the Hermitian one), so Solve never needs more than that extra memory
        internal long ColumnCopyMinEntries = 200000;
        internal long ColumnCopyMaxEntries = 32000000;
        int[] colOff; double[] colVal; bool colCopyBuilt;

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
                var cv = new double[total];
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
                        for (int c = c0; c < c1 && c <= i; ++c)
                            cv[co + c * h - c * (c - 1) / 2 - c + i] = Lx[src + c];
                    }
                });
                colOff = offs; colVal = cv;
            }
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
            analyzedOrdering = Ordering;
            var an = new CholeskyAnalysis(N, BlockSize, aColPtr, aRow, false, analyzedOrdering);
            perm = an.Perm; iperm = an.IPerm; Lp = an.Lp; Li = an.Li;
            snCount = an.SnCount; snFirst = an.SnFirst; snVal = an.SnVal; colSnode = an.ColSnode; snRowPtr = an.SnRowPtr; snRows = an.SnRows;
            apColPtr = an.ApColPtr; apRow = an.ApRow; apSrc = an.ApSrc;
            Lx = new double[an.ValueCount];
            scheduleBuilt = false; planBuilt = false;
        }


        // left-looking supernodal Cholesky (E. Ng, B. Peyton, Block sparse Cholesky algorithms on advanced
        // uniprocessor computers, SIAM J. Sci. Comput. 1993) on the structure from analyze()
        bool numeric()
        {
            if (!ForceSerialNumeric) {
                CholeskyNumericPlan plan = getNumericPlan();
                if (plan != null) return numericParallel(plan);
            }
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

        // L_J[row of i, column of row c] -= L_K[i,:] . L_K[c,:] for the rows c in [p0, p1) of K and i >= c
        // (row by row, so that the writes go to one row of the block of J at a time)
        void updateBlock(int K, int J, int p0, int p1, int[] relmap)
        {
            updateRows(K, J, p0, p1, relmap, p0, snRowPtr[K + 1] - snRowPtr[K]);
        }

        // the rows i in [iStart, iEnd) of the update (each writes its own row of the block of J)
        unsafe void updateRows(int K, int J, int p0, int p1, int[] relmap, int iStart, int iEnd)
        {
            int f = snFirst[J], w = snFirst[J + 1] - f, off = snVal[J];
            int kw = snFirst[K + 1] - snFirst[K], krp = snRowPtr[K], kh = snRowPtr[K + 1] - krp, koff = snVal[K];
            bool vec = !ForceScalarKernels && SparseCholeskyKernels.VectorPath;
            double* r4 = stackalloc double[4];
            fixed (double* L = Lx)
            fixed (int* rows = snRows, rel = relmap) {
                int* krows = rows + krp;
                double* LK = L + koff;
                for (int i = iStart; i < iEnd; ++i) {
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
        unsafe bool factorBlock(int J, double[] diagA, out string err)
        {
            err = null;
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
                                err = "matrix is not positive definite (pivot " + d + " at unknown " + perm[f + row] + ")";
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
        // the rows below the panel (independent of each other) in parallel. Entry (i, c) is (L[i,c] - dot(L[i,:c], L[c,:c])) / L[c,c]
        // with the same dot product as in factorBlock, so the bits are the same.
        unsafe bool factorBlockParallel(int J, double[] diagA, out string err)
        {
            err = null;
            System.Threading.Interlocked.Increment(ref ParallelPanelCalls);
            int f = snFirst[J], w = snFirst[J + 1] - f, h = snRowPtr[J + 1] - snRowPtr[J], off = snVal[J];
            double[] inv = new double[w];
            bool vec = !ForceScalarKernels && SparseCholeskyKernels.VectorPath;
            int nb = Math.Max(1, PanelWidth);
            for (int c0 = 0; c0 < w; c0 += nb) {
                int c1 = Math.Min(c0 + nb, w);
                fixed (double* L = Lx) {
                    double* B = L + off;
                    for (int row = c0; row < c1; ++row) {
                        double* li = B + row * w;
                        for (int c = c0; c < row; ++c)
                            li[c] = (li[c] - SparseCholeskyKernels.Dot(li, B + c * w, c, vec)) * inv[c];
                        double d = li[row] - SparseCholeskyKernels.Dot(li, li, row, vec);
                        if (!(d > 1e-14 * Math.Abs(diagA[f + row])) || double.IsInfinity(d)) {
                            err = "matrix is not positive definite (pivot " + d + " at unknown " + perm[f + row] + ")";
                            return false;
                        }
                        li[row] = Math.Sqrt(d);
                        inv[row] = 1.0 / li[row];
                    }
                }
                int blocks = (h - c1 + 3) / 4;
                if (blocks <= 0) continue;
                int cc0 = c0, cc1 = c1;
                gParallel.ForRange(blocks, b => {
                    int i0 = c1 + 4 * b, nr = Math.Min(4, h - i0);
                    unsafe {
                        double* r4 = stackalloc double[4];
                        fixed (double* L = Lx) {
                            double* B = L + off;
                            if (vec && nr == 4) {
                                double* L0 = B + i0 * w;
                                for (int c = cc0; c < cc1; ++c) {
                                    SparseCholeskyKernels.Dot4Rows(L0, w, B + c * w, c, r4, true);
                                    for (int q = 0; q < 4; ++q)
                                        L0[q * w + c] = (L0[q * w + c] - r4[q]) * inv[c];
                                }
                            } else
                                for (int q = 0; q < nr; ++q) {
                                    double* li = B + (i0 + q) * w;
                                    for (int c = cc0; c < cc1; ++c)
                                        li[c] = (li[c] - SparseCholeskyKernels.Dot(li, B + c * w, c, vec)) * inv[c];
                                }
                        }
                    }
                });
            }
            return true;
        }
    }
}
