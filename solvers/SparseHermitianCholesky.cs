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

        // fill-reducing ordering of the block graph, permuted pattern of A and the structure of L
        void analyze()
        {
            int B = 1, nb = N;
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
            LxRe = new double[snVal[snCount]]; LxIm = new double[snVal[snCount]];
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
                    apSrc[cnt[j]] = (pr < pc) ? ~q : q;
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
