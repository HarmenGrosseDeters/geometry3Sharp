// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// The symbolic part of SparseCholesky and SparseHermitianCholesky, shared by both: the fill-reducing ordering of the
    /// graph of the unknowns (blocks of BlockSize unknowns are ordered together), the postordered elimination tree, the
    /// structure of L by column, the relaxed supernodes and the permuted lower triangle of A. It depends on the nonzero
    /// pattern only, not on the values or on real or complex arithmetic.
    ///
    /// Written from the papers named in the code: the minimum degree ordering (A. George, J. Liu, The Evolution of the Minimum
    /// Degree Ordering Algorithm, SIAM Review 1989; P. Amestoy, T. Davis, I. Duff, An Approximate Minimum Degree Ordering
    /// Algorithm, SIAM J. Matrix Anal. Appl. 1996), the elimination tree (J. Liu, The role of elimination trees in sparse
    /// factorization, SIAM J. Matrix Anal. Appl. 1990) and the relaxed supernodes (C. Ashcraft, R. Grimes, 1989). No code
    /// taken over.
    /// </summary>
    internal sealed class CholeskyAnalysis
    {
        /// <summary> perm[k] = original index of the k-th unknown, iperm = inverse </summary>
        public int[] Perm, IPerm;
        /// <summary> structure of L by column (permuted indices); the diagonal is the first entry of each column, rows ascending </summary>
        public int[] Lp, Li;
        /// <summary>
        /// supernodes: columns [SnFirst[s], SnFirst[s+1]) share the rows SnRows[SnRowPtr[s] ..]; their values are a dense
        /// row-major block (rows x width) at SnVal[s]
        /// </summary>
        public int SnCount;
        public int[] SnFirst, SnVal, ColSnode, SnRowPtr, SnRows;
        /// <summary>
        /// the entries of A by column of the permuted lower triangle: (permuted row, index into the values of A); with
        /// conjugateFlag the index is complemented (~q) where the permuted entry is the mirror of the stored one
        /// </summary>
        public int[] ApColPtr, ApRow, ApSrc;
        /// <summary> total size of the dense blocks (values of L) </summary>
        public int ValueCount { get { return SnVal[SnCount]; } }

        readonly int N, B;
        readonly int[] aColPtr, aRow;
        readonly bool conj;
        readonly CholeskyOrdering ordering;

        /// <summary>
        /// The analysis of the symmetric matrix with the lower triangle (original indices, by column, every diagonal entry
        /// present) given by aColPtr and aRow, ordered in blocks of blockSize unknowns.
        /// </summary>
        public CholeskyAnalysis(int n, int blockSize, int[] aColPtr, int[] aRow, bool conjugateFlag,
            CholeskyOrdering ordering = CholeskyOrdering.MinimumDegree)
        {
            N = n; B = blockSize; this.aColPtr = aColPtr; this.aRow = aRow; conj = conjugateFlag; this.ordering = ordering;
            order();
            permutedPattern();
            // postorder the elimination tree: same fill, but the columns of each subtree (and chains) are
            // consecutive, which gives large supernodes. The tree is found from the pattern of A alone (J. Liu 1990:
            // the parent of j is the first row below the diagonal of column j of L), so the structure of L
            // is computed once, for the final order.
            int[] parent = etree();
            int[] post = postorder(parent);
            int[] perm2 = new int[N];
            for (int i = 0; i < N; ++i) perm2[i] = Perm[post[i]];
            Perm = perm2;
            for (int i = 0; i < N; ++i) IPerm[Perm[i]] = i;
            permutedPattern();
            symbolic();
            supernodes();
        }

        // fill-reducing ordering of the block graph
        void order()
        {
            int nb = N / B;
            if (ordering == CholeskyOrdering.NestedDissection) {
                int[] ndOrder = CholeskyNestedDissection.Order(N, B, aColPtr, aRow);
                Perm = new int[N]; IPerm = new int[N];
                for (int k = 0; k < nb; ++k)
                    for (int s = 0; s < B; ++s) {
                        Perm[k * B + s] = ndOrder[k] * B + s;
                        IPerm[ndOrder[k] * B + s] = k * B + s;
                    }
                return;
            }
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

            Perm = new int[N]; IPerm = new int[N];
            for (int k = 0; k < nb; ++k)
                for (int s = 0; s < B; ++s) {
                    Perm[k * B + s] = blockOrder[k] * B + s;
                    IPerm[blockOrder[k] * B + s] = k * B + s;
                }
        }

        // the permuted lower triangle of A by column
        void permutedPattern()
        {
            int nnz = aColPtr[N];
            int[] cnt = new int[N + 1];
            for (int c = 0; c < N; ++c)
                for (int q = aColPtr[c]; q < aColPtr[c + 1]; ++q)
                    cnt[Math.Min(IPerm[aRow[q]], IPerm[c]) + 1]++;
            for (int j = 0; j < N; ++j)
                cnt[j + 1] += cnt[j];
            ApColPtr = (int[])cnt.Clone();
            ApRow = new int[nnz]; ApSrc = new int[nnz];
            for (int c = 0; c < N; ++c)
                for (int q = aColPtr[c]; q < aColPtr[c + 1]; ++q) {
                    int pr = IPerm[aRow[q]], pc = IPerm[c];
                    int j = Math.Min(pr, pc);
                    ApRow[cnt[j]] = Math.Max(pr, pc);
                    ApSrc[cnt[j]] = conj && pr < pc ? ~q : q;
                    cnt[j]++;
                }
        }

        // elimination tree of the permuted matrix (J. Liu): for each row k the columns j < k of its entries, climbing
        // from j through the ancestors found so far (path compression through `ancestor`) to the first one that is not below k
        int[] etree()
        {
            // row lists of the strictly lower triangle: row r holds the columns j < r with an entry (r, j)
            int[] rcnt = new int[N + 1];
            for (int j = 0; j < N; ++j)
                for (int q = ApColPtr[j]; q < ApColPtr[j + 1]; ++q)
                    if (ApRow[q] > j) rcnt[ApRow[q] + 1]++;
            for (int r = 0; r < N; ++r) rcnt[r + 1] += rcnt[r];
            int[] next = (int[])rcnt.Clone(), cols = new int[rcnt[N]];
            for (int j = 0; j < N; ++j)
                for (int q = ApColPtr[j]; q < ApColPtr[j + 1]; ++q)
                    if (ApRow[q] > j) cols[next[ApRow[q]]++] = j;
            int[] parent = new int[N], ancestor = new int[N];
            for (int k = 0; k < N; ++k) {
                parent[k] = -1; ancestor[k] = -1;
                for (int t = rcnt[k]; t < rcnt[k + 1]; ++t) {
                    int i = cols[t];
                    while (i != -1 && i < k) {
                        int a = ancestor[i];
                        ancestor[i] = k;
                        if (a == -1) parent[i] = k;
                        i = a;
                    }
                }
            }
            return parent;
        }

        static int[] postorder(int[] parent)
        {
            int n = parent.Length;
            int[] childHead = new int[n], childNext = new int[n];
            for (int j = 0; j < n; ++j) childHead[j] = -1;
            for (int j = n - 1; j >= 0; --j)
                if (parent[j] >= 0) { childNext[j] = childHead[parent[j]]; childHead[parent[j]] = j; }
            int[] post = new int[n];
            int npost = 0;
            int[] stack = new int[n];
            for (int root = 0; root < n; ++root) {
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
            return post;
        }

        // structure of L: struct(L_j) = struct(A_j) + struct(L_c) of the children c of j in the elimination tree, rows > j;
        // the parent of j is the first row of struct(L_j)
        void symbolic()
        {
            Lp = new int[N + 1];
            int[] li = new int[Math.Max(16, 2 * ApColPtr[N])];
            int count = 0;
            int[] childHead = new int[N], childNext = new int[N], rmark = new int[N];
            int[] rows = new int[16];
            for (int j = 0; j < N; ++j) { childHead[j] = -1; rmark[j] = -1; }
            for (int j = 0; j < N; ++j) {
                int nr = 0;
                rmark[j] = j;
                for (int q = ApColPtr[j]; q < ApColPtr[j + 1]; ++q) {
                    int r = ApRow[q];
                    if (rmark[r] != j) {
                        rmark[r] = j;
                        if (nr == rows.Length) Array.Resize(ref rows, 2 * nr);
                        rows[nr++] = r;
                    }
                }
                for (int c = childHead[j]; c != -1; c = childNext[c])
                    for (int q = Lp[c] + 1; q < Lp[c + 1]; ++q) {
                        int r = li[q];
                        if (r > j && rmark[r] != j) {
                            rmark[r] = j;
                            if (nr == rows.Length) Array.Resize(ref rows, 2 * nr);
                            rows[nr++] = r;
                        }
                    }
                Array.Sort(rows, 0, nr);
                Lp[j] = count;
                if (count + 1 + nr > li.Length) Array.Resize(ref li, Math.Max(2 * li.Length, count + 1 + nr));
                li[count++] = j;
                Array.Copy(rows, 0, li, count, nr);
                count += nr;
                Lp[j + 1] = count;
                if (nr > 0) {
                    int parent = rows[0];
                    childNext[j] = childHead[parent];
                    childHead[parent] = j;
                }
            }
            Li = new int[count];
            Array.Copy(li, Li, count);
        }

        // relaxed supernodes (C. Ashcraft, R. Grimes, The influence of relaxed supernode partitions on the
        // multifrontal method, ACM TOMS 1989): column j joins the supernode of j-1 if j is the parent of j-1
        // and the explicit zeros stay below 10% of the dense block (or it is narrower than 4 columns).
        // Along such a chain struct(L_k) is within {k+1} + struct(L_k+1), so the rows of a supernode [f, l)
        // are f..l-1 and the rows of L_(l-1) below l-1.
        void supernodes()
        {
            var first = new List<int>();
            ColSnode = new int[N];
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
                ColSnode[j] = first.Count - 1;
            }
            SnCount = first.Count;
            first.Add(N);
            SnFirst = first.ToArray();
            SnRowPtr = new int[SnCount + 1];
            SnVal = new int[SnCount + 1];
            for (int J = 0; J < SnCount; ++J) {
                int f = SnFirst[J], l = SnFirst[J + 1], w = l - f;
                int h = w + (Lp[l] - Lp[l - 1] - 1);
                SnRowPtr[J + 1] = SnRowPtr[J] + h;
                SnVal[J + 1] = SnVal[J] + h * w;
            }
            SnRows = new int[SnRowPtr[SnCount]];
            for (int J = 0; J < SnCount; ++J) {
                int f = SnFirst[J], l = SnFirst[J + 1], p = SnRowPtr[J];
                for (int j = f; j < l; ++j) SnRows[p++] = j;
                for (int q = Lp[l - 1] + 1; q < Lp[l]; ++q) SnRows[p++] = Li[q];
            }
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
    }
}
