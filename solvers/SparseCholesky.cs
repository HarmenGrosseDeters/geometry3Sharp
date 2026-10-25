// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Direct solver for sparse symmetric positive definite systems A x = b: Cholesky factorization
    /// A = L L^T in envelope (skyline) storage after a reverse Cuthill-McKee ordering, which keeps the fill
    /// inside the envelope small for mesh matrices (Laplacians, heat operators). Factor once, solve many
    /// right-hand sides.
    ///
    /// Without pivoting, so for an M-matrix (eg mass + t * cotan Laplacian with non-negative weights, as on
    /// an intrinsic Delaunay triangulation) the substitutions only add terms of one sign and every entry
    /// of the solution is accurate relative to itself, however small (heat far from its source).
    /// </summary>
    public class SparseCholesky
    {
        int N;
        int[] perm;         // perm[k] = original index of row k
        int[] iperm;        // iperm[i] = row of original index i
        int[] first;        // first column of the envelope of row k
        int[] start;        // start of row k in values (entries first[k] .. k)
        double[] values;

        /// <summary> false after Factor() if the matrix is not (numerically) positive definite </summary>
        public bool IsValid { get; private set; }

        /// <summary> number of stored entries of the factor </summary>
        public long FactorSize { get { return values == null ? 0 : values.Length; } }


        /// <summary>
        /// factor the symmetric n x n matrix given by the entries of one triangle (entries given more than
        /// once are summed, so assembly per element is fine)
        /// </summary>
        public SparseCholesky(int n, IEnumerable<KeyValuePair<Index2i, double>> entries)
        {
            factor(n, entries);
        }

        public SparseCholesky(SymmetricSparseMatrix m)
        {
            factor(m.Rows, m.NonZeros());
        }


        void factor(int n, IEnumerable<KeyValuePair<Index2i, double>> entries)
        {
            N = n;
            // symmetric adjacency; repeated entries (in either triangle) are summed
            var rowLists = new List<KeyValuePair<int, double>>[N];
            var diag = new double[N];
            var offdiag = new Dictionary<long, double>();
            for (int i = 0; i < N; ++i) rowLists[i] = new List<KeyValuePair<int, double>>();
            foreach (var kv in entries) {
                int r = kv.Key.a, c = kv.Key.b;
                if (r < 0 || c < 0 || r >= N || c >= N)
                    throw new ArgumentException("SparseCholesky: entry out of range");
                if (r == c) { diag[r] += kv.Value; continue; }
                long key = (long)Math.Min(r, c) * N + Math.Max(r, c);
                double cur;
                offdiag[key] = offdiag.TryGetValue(key, out cur) ? cur + kv.Value : kv.Value;
            }
            foreach (var kv in offdiag) {
                int a = (int)(kv.Key / N), b = (int)(kv.Key % N);
                rowLists[a].Add(new KeyValuePair<int, double>(b, kv.Value));
                rowLists[b].Add(new KeyValuePair<int, double>(a, kv.Value));
            }

            compute_ordering(rowLists);

            // envelope of the permuted matrix
            first = new int[N];
            for (int k = 0; k < N; ++k) {
                int f = k;
                foreach (var e in rowLists[perm[k]])
                    f = Math.Min(f, iperm[e.Key]);
                first[k] = f;
            }
            start = new int[N + 1];
            for (int k = 0; k < N; ++k)
                start[k + 1] = start[k] + (k - first[k] + 1);
            values = new double[start[N]];
            for (int k = 0; k < N; ++k) {
                values[start[k] + (k - first[k])] = diag[perm[k]];
                foreach (var e in rowLists[perm[k]]) {
                    int c = iperm[e.Key];
                    if (c < k) values[start[k] + (c - first[k])] = e.Value;
                }
            }

            // row-oriented envelope Cholesky: L[k, c] = (A[k, c] - sum_m L[k, m] L[c, m]) / L[c, c]
            IsValid = true;
            for (int k = 0; k < N; ++k) {
                int fk = first[k], sk = start[k];
                for (int c = fk; c < k; ++c) {
                    int fc = first[c], sc = start[c];
                    int m0 = Math.Max(fk, fc);
                    double s = values[sk + (c - fk)];
                    for (int m = m0; m < c; ++m)
                        s -= values[sk + (m - fk)] * values[sc + (m - fc)];
                    values[sk + (c - fk)] = s / values[sc + (c - fc)];
                }
                double d = values[sk + (k - fk)];
                for (int m = fk; m < k; ++m) {
                    double v = values[sk + (m - fk)];
                    d -= v * v;
                }
                if (!(d > 0)) {
                    IsValid = false;
                    return;
                }
                values[sk + (k - fk)] = Math.Sqrt(d);
            }
        }


        // reverse Cuthill-McKee, per connected component from a pseudo-peripheral vertex
        void compute_ordering(List<KeyValuePair<int, double>>[] rows)
        {
            perm = new int[N];
            iperm = new int[N];
            bool[] visited = new bool[N];
            int count = 0;
            int[] level = new int[N];
            var queue = new List<int>();
            var byDegree = Comparer<int>.Create((a, b) => rows[a].Count.CompareTo(rows[b].Count));
            for (int seed = 0; seed < N; ++seed) {
                if (visited[seed]) continue;
                // pseudo-peripheral start: repeat BFS from the farthest vertex of minimum degree
                int startV = seed;
                int ecc = -1;
                for (int pass = 0; pass < 4; ++pass) {
                    int far, depth;
                    bfs_farthest(rows, startV, level, queue, out far, out depth);
                    if (depth <= ecc) break;
                    ecc = depth;
                    startV = far;
                }
                int begin = count;
                visited[startV] = true;
                perm[count++] = startV;
                for (int q = begin; q < count; ++q) {
                    int v = perm[q];
                    int nb0 = count;
                    foreach (var e in rows[v])
                        if (visited[e.Key] == false) {
                            visited[e.Key] = true;
                            perm[count++] = e.Key;
                        }
                    Array.Sort(perm, nb0, count - nb0, byDegree);
                }
                Array.Reverse(perm, begin, count - begin);
            }
            for (int k = 0; k < N; ++k)
                iperm[perm[k]] = k;
        }

        static void bfs_farthest(List<KeyValuePair<int, double>>[] rows, int s, int[] level, List<int> queue, out int far, out int depth)
        {
            queue.Clear();
            queue.Add(s);
            var seen = new HashSet<int>() { s };
            level[s] = 0;
            far = s; depth = 0;
            for (int q = 0; q < queue.Count; ++q) {
                int v = queue[q];
                if (level[v] > depth || (level[v] == depth && rows[v].Count < rows[far].Count)) {
                    depth = level[v]; far = v;
                }
                foreach (var e in rows[v])
                    if (seen.Add(e.Key)) {
                        level[e.Key] = level[v] + 1;
                        queue.Add(e.Key);
                    }
            }
        }


        /// <summary> solve A x = b (x and b may be the same array) </summary>
        public void Solve(double[] b, double[] x)
        {
            if (IsValid == false)
                throw new InvalidOperationException("SparseCholesky.Solve: matrix is not positive definite");
            double[] y = new double[N];
            for (int k = 0; k < N; ++k)
                y[k] = b[perm[k]];
            // L y' = y
            for (int k = 0; k < N; ++k) {
                int fk = first[k], sk = start[k];
                double s = y[k];
                for (int m = fk; m < k; ++m)
                    s -= values[sk + (m - fk)] * y[m];
                y[k] = s / values[sk + (k - fk)];
            }
            // L^T x' = y'
            for (int k = N - 1; k >= 0; --k) {
                int fk = first[k], sk = start[k];
                double xk = y[k] / values[sk + (k - fk)];
                y[k] = xk;
                for (int m = fk; m < k; ++m)
                    y[m] -= values[sk + (m - fk)] * xk;
            }
            for (int k = 0; k < N; ++k)
                x[perm[k]] = y[k];
        }
    }
}
