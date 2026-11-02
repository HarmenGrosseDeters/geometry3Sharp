// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;

namespace g3
{
    /// <summary>
    /// A small dense linear program solver in floating point: maximize c·x subject to A x &lt;= b, 0 &lt;= x, with
    /// b &gt;= 0, so that x = 0 is feasible and the slack basis starts the simplex method without a first phase.
    /// Tableau simplex with Dantzig's rule (largest reduced cost) and Bland's rule (smallest index) after a run of
    /// (nearly) degenerate pivots and after 4 (m + n) pivots in all, which prevents cycling. Meant for the small programs of MeshSeparation (a few hundred
    /// variables): the result is not verified here; its callers check what they use exactly.
    /// Implemented from the textbook (V. Chvátal, Linear Programming, Freeman 1983, chapters 2 and 3; R. G. Bland,
    /// New finite pivoting rules for the simplex method, Mathematics of Operations Research 2(2), 1977).
    /// </summary>
    internal sealed class DenseSimplex
    {
        public enum Status { Optimal, Unbounded, IterationLimit, Invalid }

        /// <summary> the solution of the last Solve (length n) </summary>
        public double[] X;
        /// <summary> the objective c·x at X </summary>
        public double Objective;
        /// <summary> pivots of the last Solve </summary>
        public int Pivots;
        /// <summary> pivots at most </summary>
        public int MaxPivots = 20000;
        /// <summary> reduced costs and pivot elements below this count as zero (the rows are expected to be scaled to about 1) </summary>
        public double Tolerance = 1e-11;

        /// <summary> maximize c·x subject to A x &lt;= b (A is m x n, row-major), x &gt;= 0; b must be &gt;= 0 </summary>
        public Status Solve(double[,] A, double[] b, double[] c)
        {
            int m = A.GetLength(0), n = A.GetLength(1);
            X = new double[n];
            Objective = 0;
            Pivots = 0;
            if (b.Length != m || c.Length != n)
                return Status.Invalid;
            for (int i = 0; i < m; ++i)
                if (!(b[i] >= 0) || double.IsInfinity(b[i]))
                    return Status.Invalid;
            // tableau: m rows of [A | I | b], the objective row last: [-c | 0 | 0]
            int w = n + m + 1;
            var T = new double[(m + 1) * w];
            for (int i = 0; i < m; ++i) {
                for (int j = 0; j < n; ++j) {
                    double a = A[i, j];
                    if (double.IsNaN(a) || double.IsInfinity(a))
                        return Status.Invalid;
                    T[i * w + j] = a;
                }
                T[i * w + n + i] = 1;
                T[i * w + w - 1] = b[i];
            }
            for (int j = 0; j < n; ++j) {
                if (double.IsNaN(c[j]) || double.IsInfinity(c[j]))
                    return Status.Invalid;
                T[m * w + j] = -c[j];
            }
            var basis = new int[m];
            for (int i = 0; i < m; ++i)
                basis[i] = n + i;
            int degenerate = 0;
            while (true) {
                // entering column: Dantzig, or Bland after a run of degenerate pivots, and for good after many pivots
                // (rounding can make nearly degenerate pivots stall without repeating a basis exactly)
                bool bland = degenerate > 50 || Pivots > 4 * (m + n);
                int col = -1;
                double best = -Tolerance;
                for (int j = 0; j < n + m; ++j) {
                    double r = T[m * w + j];
                    if (r < best) {
                        col = j;
                        if (bland) break;
                        best = r;
                    }
                }
                if (col < 0)
                    break;
                // leaving row: the ratio test, ties by the smallest basis index (Bland)
                int row = -1;
                double ratio = double.PositiveInfinity;
                for (int i = 0; i < m; ++i) {
                    double a = T[i * w + col];
                    if (a <= Tolerance)
                        continue;
                    double q = T[i * w + w - 1] / a;
                    if (q < ratio || (q == ratio && row >= 0 && basis[i] < basis[row])) {
                        ratio = q;
                        row = i;
                    }
                }
                if (row < 0)
                    return Status.Unbounded;
                if (++Pivots > MaxPivots)
                    return Status.IterationLimit;
                degenerate = (ratio <= Tolerance) ? degenerate + 1 : 0;
                pivot(T, m, w, row, col);
                basis[row] = col;
            }
            for (int i = 0; i < m; ++i)
                if (basis[i] < n)
                    X[basis[i]] = Math.Max(0, T[i * w + w - 1]);
            for (int j = 0; j < n; ++j)
                Objective += c[j] * X[j];
            return Status.Optimal;
        }

        static void pivot(double[] T, int m, int w, int row, int col)
        {
            int r0 = row * w;
            double p = T[r0 + col];
            for (int j = 0; j < w; ++j)
                T[r0 + j] /= p;
            T[r0 + col] = 1;
            for (int i = 0; i <= m; ++i) {
                if (i == row)
                    continue;
                int i0 = i * w;
                double f = T[i0 + col];
                if (f == 0)
                    continue;
                for (int j = 0; j < w; ++j)
                    T[i0 + j] -= f * T[r0 + j];
                T[i0 + col] = 0;
                // (a right-hand side that rounding pushed below 0 is 0: the basis stays feasible)
                if (i < m && T[i0 + w - 1] < 0)
                    T[i0 + w - 1] = 0;
            }
        }
    }
}
