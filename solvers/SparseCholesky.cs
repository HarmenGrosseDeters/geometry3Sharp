// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Sparse Cholesky factorization A = L L^T of a symmetric positive definite matrix, for systems that are
    /// solved many times (parameterization, Laplacians with fixed boundary values).
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

        /// <summary> the last Factorize() succeeded (the matrix is numerically positive definite) </summary>
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
        /// Factorize the accumulated matrix. Returns false (and sets Error) if it is not positive definite.
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
                        if (add_term(ref acc, Lx[co + t], y[f + t]) == false) return false;
                    y[f + c] = acc - Math.Log(Lx[co + c]);
                }
                for (int i = w; i < h; ++i) {
                    double acc = double.NegativeInfinity;
                    int io = off + i * w;
                    for (int t = 0; t < w; ++t)
                        if (add_term(ref acc, Lx[io + t], y[f + t]) == false) return false;
                    y[snRows[rp + i]] = log_add(y[snRows[rp + i]], acc);
                }
            }
            // L^T z = y
            for (int J = snCount - 1; J >= 0; --J) {
                int f = snFirst[J], w = snFirst[J + 1] - f, rp = snRowPtr[J], h = snRowPtr[J + 1] - rp, off = snVal[J];
                for (int c = w - 1; c >= 0; --c) {
                    double acc = y[f + c];
                    for (int i = c + 1; i < h; ++i)
                        if (add_term(ref acc, Lx[off + i * w + c], y[snRows[rp + i]]) == false) return false;
                    y[f + c] = acc - Math.Log(Lx[off + c * w + c]);
                }
            }
            for (int k = 0; k < N; ++k)
                logX[perm[k]] = y[k];
            return true;
        }

        // acc = log(exp(acc) - l * exp(logY)) for an entry l <= 0 of the factor; false if l > 0 meets a non-zero y
        static bool add_term(ref double acc, double l, double logY)
        {
            if (l == 0 || double.IsNegativeInfinity(logY)) return true;
            if (l > 0) return false;
            acc = log_add(acc, Math.Log(-l) + logY);
            return true;
        }

        static double log_add(double a, double b)
        {
            if (double.IsNegativeInfinity(a)) return b;
            if (double.IsNegativeInfinity(b)) return a;
            double m = Math.Max(a, b);
            return m + Math.Log(1 + Math.Exp(-Math.Abs(a - b)));
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


        // fill-reducing ordering of the block graph, permuted pattern of A and the structure of L
        void analyze()
        {
            int B = BlockSize, nb = N / B;
            // block adjacency without duplicates
            List<int>[] adj = new List<int>[nb];
            for (int i = 0; i < nb; ++i)
                adj[i] = new List<int>();
            for (int c = 0; c < N; ++c)
                for (int q = aColPtr[c]; q < aColPtr[c + 1]; ++q) {
                    int bi = aRow[q] / B, bj = c / B;
                    if (bi != bj) { adj[bi].Add(bj); adj[bj].Add(bi); }
                }
            int[] mark = new int[nb];
            for (int i = 0; i < nb; ++i) mark[i] = -1;
            for (int i = 0; i < nb; ++i) {
                List<int> l = adj[i];
                int w = 0;
                for (int t = 0; t < l.Count; ++t) {
                    int x = l[t];
                    if (mark[x] != i) { mark[x] = i; l[w++] = x; }
                }
                l.RemoveRange(w, l.Count - w);
            }

            List<int> blockOrder = new MinimumDegree(adj).Order();

            perm = new int[N]; iperm = new int[N];
            for (int k = 0; k < nb; ++k)
                for (int s = 0; s < B; ++s) {
                    perm[k * B + s] = blockOrder[k] * B + s;
                    iperm[blockOrder[k] * B + s] = k * B + s;
                }

            symbolic();
            // postorder the elimination tree: same fill, but the columns of each subtree (and chains) are
            // consecutive, which gives large supernodes
            int[] parent = new int[N];
            for (int j = 0; j < N; ++j)
                parent[j] = Lp[j + 1] - Lp[j] > 1 ? Li[Lp[j] + 1] : -1;
            int[] childHead = new int[N], childNext = new int[N];
            for (int j = 0; j < N; ++j) childHead[j] = -1;
            for (int j = N - 1; j >= 0; --j)
                if (parent[j] >= 0) { childNext[j] = childHead[parent[j]]; childHead[parent[j]] = j; }
            int[] post = new int[N];
            int npost = 0;
            int[] stack = new int[N];
            for (int root = 0; root < N; ++root) {
                if (parent[root] >= 0) continue;
                int top = 0;
                stack[0] = root;
                while (top >= 0) {
                    int v = stack[top];
                    int c = childHead[v];
                    if (c >= 0) {
                        childHead[v] = childNext[c];
                        stack[++top] = c;
                    } else {
                        post[npost++] = v;
                        top--;
                    }
                }
            }
            int[] perm2 = new int[N];
            for (int i = 0; i < N; ++i) perm2[i] = perm[post[i]];
            perm = perm2;
            for (int i = 0; i < N; ++i) iperm[perm[i]] = i;
            symbolic();

            // relaxed supernodes (C. Ashcraft, R. Grimes, The influence of relaxed supernode partitions on the
            // multifrontal method, ACM TOMS 1989): column j joins the supernode of j-1 if j is the parent of j-1
            // and the explicit zeros stay below 10% of the dense block (or it is narrower than 4 columns).
            // Along such a chain struct(L_k) is within {k+1} + struct(L_k+1), so the rows of a supernode [f, l)
            // are f..l-1 and the rows of L_(l-1) below l-1.
            var first = new List<int>();
            colSnode = new int[N];
            long zeros = 0;
            for (int j = 0; j < N; ++j) {
                bool merge = false;
                if (j > 0 && Lp[j] - Lp[j - 1] > 1 && Li[Lp[j - 1] + 1] == j) {
                    int f = first[first.Count - 1], width = j - f;
                    long cPrev = Lp[j] - Lp[j - 1], c = Lp[j + 1] - Lp[j];
                    long add = width * (c + 1 - cPrev);
                    long size = (width + 1) * (width + c);
                    if (add == 0 || zeros + add <= 0.1 * size || width < 4) {
                        merge = true;
                        zeros += add;
                    }
                }
                if (!merge) { first.Add(j); zeros = 0; }
                colSnode[j] = first.Count - 1;
            }
            snCount = first.Count;
            first.Add(N);
            snFirst = first.ToArray();
            snRowPtr = new int[snCount + 1];
            snVal = new int[snCount + 1];
            for (int J = 0; J < snCount; ++J) {
                int f = snFirst[J], l = snFirst[J + 1], w = l - f;
                int h = w + (Lp[l] - Lp[l - 1] - 1);
                snRowPtr[J + 1] = snRowPtr[J] + h;
                snVal[J + 1] = snVal[J] + h * w;
            }
            snRows = new int[snRowPtr[snCount]];
            for (int J = 0; J < snCount; ++J) {
                int f = snFirst[J], l = snFirst[J + 1], p = snRowPtr[J];
                for (int j = f; j < l; ++j) snRows[p++] = j;
                for (int q = Lp[l - 1] + 1; q < Lp[l]; ++q) snRows[p++] = Li[q];
            }
            Lx = new double[snVal[snCount]];
        }


        // permuted pattern of A (lower triangle by column) and the structure of L for the current ordering
        void symbolic()
        {
            // entries of A in permuted lower-triangle form, by column
            int nnz = aColPtr[N];
            int[] cnt = new int[N + 1];
            for (int c = 0; c < N; ++c)
                for (int q = aColPtr[c]; q < aColPtr[c + 1]; ++q)
                    cnt[Math.Min(iperm[aRow[q]], iperm[c]) + 1]++;
            for (int j = 0; j < N; ++j)
                cnt[j + 1] += cnt[j];
            apColPtr = (int[])cnt.Clone();
            apRow = new int[nnz]; apSrc = new int[nnz];
            for (int c = 0; c < N; ++c)
                for (int q = aColPtr[c]; q < aColPtr[c + 1]; ++q) {
                    int pr = iperm[aRow[q]], pc = iperm[c];
                    int j = Math.Min(pr, pc);
                    apRow[cnt[j]] = Math.Max(pr, pc);
                    apSrc[cnt[j]] = q;
                    cnt[j]++;
                }

            // symbolic factorization: struct(L_j) = struct(A_j) + struct(L_c) of the children c of j in the
            // elimination tree, rows > j; the parent of j is the first row of struct(L_j)
            Lp = new int[N + 1];
            List<int> li = new List<int>(2 * nnz);
            int[] childHead = new int[N], childNext = new int[N], rmark = new int[N];
            List<int> rows = new List<int>();
            for (int j = 0; j < N; ++j) { childHead[j] = -1; rmark[j] = -1; }
            for (int j = 0; j < N; ++j) {
                rows.Clear();
                rmark[j] = j;
                for (int q = apColPtr[j]; q < apColPtr[j + 1]; ++q) {
                    int r = apRow[q];
                    if (rmark[r] != j) { rmark[r] = j; rows.Add(r); }
                }
                for (int c = childHead[j]; c != -1; c = childNext[c])
                    for (int q = Lp[c] + 1; q < Lp[c + 1]; ++q) {
                        int r = li[q];
                        if (r > j && rmark[r] != j) { rmark[r] = j; rows.Add(r); }
                    }
                rows.Sort();
                Lp[j] = li.Count;
                li.Add(j);
                li.AddRange(rows);
                Lp[j + 1] = li.Count;
                if (rows.Count > 0) {
                    int parent = rows[0];
                    childNext[j] = childHead[parent];
                    childHead[parent] = j;
                }
            }
            Li = li.ToArray();
        }


        /// <summary>
        /// Minimum degree ordering on the quotient graph (A. George, J. Liu, The Evolution of the Minimum Degree
        /// Ordering Algorithm, SIAM Review 1989), with the approximate external degrees of P. Amestoy, T. Davis,
        /// I. Duff, An Approximate Minimum Degree Ordering Algorithm, SIAM J. Matrix Anal. Appl. 1996.
        /// An eliminated node becomes an element (the clique of its neighbours, stored as a list) instead of
        /// adding the clique edges, so the cost stays close to the size of the input graph.
        /// </summary>
        class MinimumDegree
        {
            List<int>[] adj;
            int n;

            public MinimumDegree(List<int>[] adj)
            {
                this.adj = adj;
                n = adj.Length;
            }

            public List<int> Order()
            {
                List<int> order = new List<int>(n);
                // per variable: adjacent variables and adjacent elements; per element: its variables
                List<int>[] A = adj;
                List<int>[] E = new List<int>[n];
                List<int>[] L = new List<int>[n];
                bool[] eliminated = new bool[n], absorbed = new bool[n];
                int[] degree = new int[n], bucketHead = new int[n + 1], bnext = new int[n], bprev = new int[n];
                int[] mark = new int[n], wFlag = new int[n], wVal = new int[n];
                for (int d = 0; d <= n; ++d) bucketHead[d] = -1;
                Action<int> insert = (a) => {
                    int d = degree[a];
                    bprev[a] = -1; bnext[a] = bucketHead[d];
                    if (bucketHead[d] >= 0) bprev[bucketHead[d]] = a;
                    bucketHead[d] = a;
                };
                Action<int> remove = (a) => {
                    if (bprev[a] >= 0) bnext[bprev[a]] = bnext[a]; else bucketHead[degree[a]] = bnext[a];
                    if (bnext[a] >= 0) bprev[bnext[a]] = bprev[a];
                };
                for (int i = n - 1; i >= 0; --i) {
                    E[i] = new List<int>();
                    degree[i] = A[i].Count;
                    mark[i] = wFlag[i] = -1;
                    insert(i);
                }
                int minDeg = 0;
                for (int k = 0; k < n; ++k) {
                    while (bucketHead[minDeg] < 0) minDeg++;
                    int p = bucketHead[minDeg];
                    remove(p);
                    order.Add(p);
                    eliminated[p] = true;

                    // new element p: the variables adjacent to p directly or through its elements, which are absorbed
                    List<int> Lp = new List<int>();
                    mark[p] = k;
                    foreach (int a in A[p])
                        if (!eliminated[a] && mark[a] != k) { mark[a] = k; Lp.Add(a); }
                    foreach (int e in E[p]) {
                        if (absorbed[e]) continue;
                        foreach (int a in L[e])
                            if (!eliminated[a] && mark[a] != k) { mark[a] = k; Lp.Add(a); }
                        absorbed[e] = true;
                        L[e] = null;
                    }
                    A[p] = null; E[p] = null;
                    L[p] = Lp;

                    // |L_e \ L_p| for the elements next to L_p
                    foreach (int i in Lp)
                        foreach (int e in E[i]) {
                            if (absorbed[e]) continue;
                            if (wFlag[e] != k) { wFlag[e] = k; wVal[e] = L[e].Count; }
                            wVal[e]--;
                        }
                    int remaining = n - k - 1;
                    foreach (int i in Lp) {
                        // elements: drop absorbed ones and those inside L_p (aggressive absorption), add p
                        List<int> Ei = E[i];
                        int w = 0, ext = 0;
                        for (int t = 0; t < Ei.Count; ++t) {
                            int e = Ei[t];
                            if (absorbed[e]) continue;
                            if (wVal[e] <= 0) { absorbed[e] = true; L[e] = null; continue; }
                            Ei[w++] = e;
                            ext += wVal[e];
                        }
                        Ei.RemoveRange(w, Ei.Count - w);
                        Ei.Add(p);
                        // variables: drop eliminated ones and those in L_p (connected through p now)
                        List<int> Ai = A[i];
                        w = 0;
                        for (int t = 0; t < Ai.Count; ++t) {
                            int a = Ai[t];
                            if (eliminated[a] || mark[a] == k) continue;
                            Ai[w++] = a;
                        }
                        Ai.RemoveRange(w, Ai.Count - w);
                        int d = Math.Min(remaining, Math.Min(degree[i] + Lp.Count - 1, Ai.Count + Lp.Count - 1 + ext));
                        if (d < 0) d = 0;
                        if (d != degree[i]) {
                            remove(i);
                            degree[i] = d;
                            insert(i);
                        }
                        if (d < minDeg) minDeg = d;
                    }
                }
                return order;
            }
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
            fixed (double* L = Lx)
            fixed (int* rows = snRows, rel = relmap) {
                int* krows = rows + krp;
                double* LK = L + koff;
                for (int i = p0; i < kh; ++i) {
                    double* li = LK + i * kw;
                    double* dst = L + off + rel[krows[i]] * w - f;
                    int cend = Math.Min(i + 1, p1);
                    for (int c = p0; c < cend; ++c)
                        dst[krows[c]] -= dot(li, LK + c * kw, kw);
                }
            }
        }

        // dense Cholesky of the diagonal block of supernode J and the rows below it, row by row
        unsafe bool factorBlock(int J, double[] diagA)
        {
            int f = snFirst[J], w = snFirst[J + 1] - f, h = snRowPtr[J + 1] - snRowPtr[J], off = snVal[J];
            double[] inv = new double[w];
            fixed (double* L = Lx) {
                double* B = L + off;
                for (int i = 0; i < h; ++i) {
                    double* li = B + i * w;
                    int m = Math.Min(i, w);
                    for (int c = 0; c < m; ++c)
                        li[c] = (li[c] - dot(li, B + c * w, c)) * inv[c];
                    if (i < w) {
                        double d = li[i] - dot(li, li, i);
                        if (!(d > 1e-14 * Math.Abs(diagA[f + i])) || double.IsInfinity(d)) {
                            Error = "matrix is not positive definite (pivot " + d + " at unknown " + perm[f + i] + ")";
                            return false;
                        }
                        li[i] = Math.Sqrt(d);
                        inv[i] = 1.0 / li[i];
                    }
                }
            }
            return true;
        }

        static unsafe double dot(double* a, double* b, int n)
        {
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0;
            int k = 0;
            for (; k + 4 <= n; k += 4) {
                s0 += a[k] * b[k]; s1 += a[k + 1] * b[k + 1];
                s2 += a[k + 2] * b[k + 2]; s3 += a[k + 3] * b[k + 3];
            }
            for (; k < n; ++k) s0 += a[k] * b[k];
            return (s0 + s1) + (s2 + s3);
        }
    }
}
