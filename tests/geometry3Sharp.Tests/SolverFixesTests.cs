using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SingularValueDecomposition and SymmetricEigenSolver bugs from the class-by-class review
    /// (docs/Analyse-2026-10.md) and upstream issue #168: U*S*V^T did not reproduce A for diagonal,
    /// already-reduced and rank-deficient matrices; the eigensolver had the same Householder bug,
    /// GetEigenvector() ignored the sort flag and IsRotation() computed the wrong parity.
    /// </summary>
    public class SolverFixesTests
    {
        // --- test matrices (row-major, M rows x N cols, M >= N) ---

        static double[] Identity(int m, int n)
        {
            var a = new double[m * n];
            for (int i = 0; i < n; ++i) a[i * n + i] = 1;
            return a;
        }

        static double[] Diagonal(int m, int n, params double[] d)
        {
            var a = new double[m * n];
            for (int i = 0; i < n; ++i) a[i * n + i] = d[i % d.Length];
            return a;
        }

        static double[] Random(Random rand, int m, int n)
        {
            return Enumerable.Range(0, m * n).Select(i => rand.NextDouble() * 2 - 1).ToArray();
        }

        /// <summary> product of random m x k and k x n matrices, ie rank at most k </summary>
        static double[] LowRank(Random rand, int m, int n, int k)
        {
            double[] x = Random(rand, m, k), y = Random(rand, k, n);
            var a = new double[m * n];
            for (int r = 0; r < m; ++r)
                for (int c = 0; c < n; ++c)
                    for (int i = 0; i < k; ++i)
                        a[r * n + c] += x[r * k + i] * y[i * n + c];
            return a;
        }

        static double[] Permutation(Random rand, int n)
        {
            int[] p = Enumerable.Range(0, n).OrderBy(i => rand.Next()).ToArray();
            var a = new double[n * n];
            for (int i = 0; i < n; ++i) a[i * n + p[i]] = (rand.Next(2) == 0) ? 1 : -1;
            return a;
        }

        static IEnumerable<(string, int, int, double[])> TestMatrices()
        {
            var rand = new Random(168);
            var sizes = new[] { (2, 2), (3, 2), (3, 3), (4, 3), (4, 4), (5, 5), (6, 4), (8, 8) };
            foreach (var (m, n) in sizes) {
                yield return ("identity", m, n, Identity(m, n));
                yield return ("zero", m, n, new double[m * n]);
                yield return ("diagonal", m, n, Diagonal(m, n, 2, 5, 1, -3));
                yield return ("diagonal with zeros", m, n, Diagonal(m, n, 0, 4, 0, -1));
                yield return ("rank 1", m, n, LowRank(rand, m, n, 1));
                if (n > 2)
                    yield return ("rank n-1", m, n, LowRank(rand, m, n, n - 1));
                if (m == n)
                    yield return ("signed permutation", m, n, Permutation(rand, n));
                for (int k = 0; k < 20; ++k)
                    yield return ("random", m, n, Random(rand, m, n));
                // a zero column / zero row in an otherwise dense matrix
                double[] a = Random(rand, m, n);
                for (int r = 0; r < m; ++r) a[r * n + 0] = 0;
                yield return ("zero first column", m, n, a);
                double[] b = Random(rand, m, n);
                for (int c = 0; c < n; ++c) b[1 * n + c] = 0;
                yield return ("zero second row", m, n, b);
            }

            // upstream issue #168: rotation about X by 30 degrees
            double cs = Math.Cos(Math.PI / 6), sn = Math.Sin(Math.PI / 6);
            yield return ("rotation x 30", 3, 3, new double[] { 1, 0, 0, 0, cs, -sn, 0, sn, cs });
            yield return ("rank 1 integer", 3, 3, new double[] { 1, 2, 3, 2, 4, 6, 3, 6, 9 });
            yield return ("one nonzero row", 3, 3, new double[] { 1, 2, 3, 0, 0, 0, 0, 0, 0 });
        }

        public static IEnumerable<object[]> SortTypes() { return new[] { new object[] { -1 }, new object[] { 0 }, new object[] { 1 } }; }

        static double MaxAbsDiff(double[] a, double[] b) { return a.Zip(b, (x, y) => Math.Abs(x - y)).Max(); }

        /// <summary> A^T * A for row-major n x n A, compared with identity </summary>
        static double OrthogonalityError(double[] q, int n)
        {
            double err = 0;
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    double dot = 0;
                    for (int k = 0; k < n; ++k) dot += q[k * n + i] * q[k * n + j];
                    err = Math.Max(err, Math.Abs(dot - (i == j ? 1 : 0)));
                }
            return err;
        }

        [Theory]
        [MemberData(nameof(SortTypes))]
        public void SVD_ReconstructsMatrix(int sortType)
        {
            var failures = new List<string>();
            foreach (var (name, m, n, a) in TestMatrices()) {
                var svd = new SingularValueDecomposition(m, n, 1000);
                uint iters = svd.Solve(a, sortType);
                double[] U = new double[m * m], V = new double[n * n], S = new double[n];
                svd.GetU(U);
                svd.GetV(V);
                svd.GetSingularValues(S);

                // A' = U * S * V^T, with S the m x n "diagonal" matrix
                var rec = new double[m * n];
                for (int r = 0; r < m; ++r)
                    for (int c = 0; c < n; ++c)
                        for (int k = 0; k < n; ++k)
                            rec[r * n + c] += U[r * m + k] * S[k] * V[c * n + k];

                double scale = Math.Max(1, a.Max(x => Math.Abs(x)));
                double recErr = MaxAbsDiff(a, rec) / scale;
                double uErr = OrthogonalityError(U, m), vErr = OrthogonalityError(V, n);
                bool sorted = true;
                for (int i = 0; i + 1 < n; ++i) {
                    if (sortType < 0 && S[i] < S[i + 1]) sorted = false;
                    if (sortType > 0 && S[i] > S[i + 1]) sorted = false;
                }
                if (iters == 0xFFFFFFFF || recErr > 1e-10 || uErr > 1e-10 || vErr > 1e-10 || S.Any(s => s < 0) || !sorted)
                    failures.Add($"{name} {m}x{n}: iters={iters} |A-USV^T|={recErr:E2} |U^TU-I|={uErr:E2} |V^TV-I|={vErr:E2} S=[{string.Join(",", S.Select(s => s.ToString("G4")))}]");
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        // --- SymmetricEigenSolver ---

        static double[] Symmetric(double[] a, int n)
        {
            var s = new double[n * n];
            for (int r = 0; r < n; ++r)
                for (int c = 0; c < n; ++c)
                    s[r * n + c] = 0.5 * (a[r * n + c] + a[c * n + r]);
            return s;
        }

        static IEnumerable<(string, int, double[])> SymmetricTestMatrices()
        {
            var rand = new Random(403);
            foreach (int n in new[] { 2, 3, 4, 5, 8 }) {
                yield return ("identity", n, Identity(n, n));
                yield return ("zero", n, new double[n * n]);
                yield return ("diagonal", n, Diagonal(n, n, 2, -5, 1, 3));
                yield return ("diagonal, repeated", n, Diagonal(n, n, 2, 2, -1));
                yield return ("rank 1", n, Symmetric(LowRank(rand, n, n, 1), n));
                for (int k = 0; k < 20; ++k)
                    yield return ("random", n, Symmetric(Random(rand, n, n), n));
                // first row/column already reduced: Householder of a zero vector
                double[] a = Symmetric(Random(rand, n, n), n);
                for (int i = 1; i < n; ++i) { a[i] = 0; a[i * n] = 0; }
                yield return ("decoupled first row", n, a);
            }
            yield return ("block", 3, new double[] { 1, 0, 0, 0, 2, 1, 0, 1, 2 });
            yield return ("block 4", 4, new double[] { 1, 0, 0, 0, 0, 2, 1, 0, 0, 1, 2, 0, 0, 0, 0, 3 });
        }

        static double Determinant(double[] m, int n)
        {
            double[] a = (double[])m.Clone();
            double det = 1;
            for (int c = 0; c < n; ++c) {
                int p = c;
                for (int r = c + 1; r < n; ++r)
                    if (Math.Abs(a[r * n + c]) > Math.Abs(a[p * n + c])) p = r;
                if (a[p * n + c] == 0) return 0;
                if (p != c) {
                    for (int k = 0; k < n; ++k) { double t = a[c * n + k]; a[c * n + k] = a[p * n + k]; a[p * n + k] = t; }
                    det = -det;
                }
                det *= a[c * n + c];
                for (int r = c + 1; r < n; ++r) {
                    double f = a[r * n + c] / a[c * n + c];
                    for (int k = c; k < n; ++k) a[r * n + k] -= f * a[c * n + k];
                }
            }
            return det;
        }

        [Fact]
        public void SymmetricEigenSolver_Decomposition()
        {
            // one solver per size, reused for all matrices and sort orders, to catch stale state
            var solvers = new Dictionary<int, SymmetricEigenSolver>();
            var sortTypes = new[] { SymmetricEigenSolver.SortType.Decreasing, SymmetricEigenSolver.SortType.NoSorting, SymmetricEigenSolver.SortType.Increasing };
            var failures = new List<string>();
            foreach (var (name, n, a) in SymmetricTestMatrices()) {
                if (solvers.ContainsKey(n) == false)
                    solvers[n] = new SymmetricEigenSolver(n, 1000);
                var solver = solvers[n];
                foreach (var sort in sortTypes) {
                    int iters = solver.Solve(a, sort);
                    // IsRotation() before GetEigenvectors() is computed lazily from the permutation
                    bool isRotationBefore = solver.IsRotation();
                    double[] D = solver.GetEigenvalues();
                    double[] Q = solver.GetEigenvectors();

                    // A*Q = Q*D, column by column
                    double err = 0;
                    for (int r = 0; r < n; ++r)
                        for (int c = 0; c < n; ++c) {
                            double aq = 0;
                            for (int k = 0; k < n; ++k) aq += a[r * n + k] * Q[k * n + c];
                            err = Math.Max(err, Math.Abs(aq - Q[r * n + c] * D[c]));
                        }
                    double scale = Math.Max(1, a.Max(x => Math.Abs(x)));
                    double qErr = OrthogonalityError(Q, n);

                    double colErr = 0;
                    for (int c = 0; c < n; ++c) {
                        double[] col = solver.GetEigenvector(c);
                        for (int r = 0; r < n; ++r) colErr = Math.Max(colErr, Math.Abs(col[r] - Q[r * n + c]));
                        if (solver.GetEigenvalue(c) != D[c]) colErr = double.MaxValue;
                    }

                    bool sorted = true;
                    for (int i = 0; i + 1 < n; ++i) {
                        if (sort == SymmetricEigenSolver.SortType.Decreasing && D[i] < D[i + 1]) sorted = false;
                        if (sort == SymmetricEigenSolver.SortType.Increasing && D[i] > D[i + 1]) sorted = false;
                    }
                    bool isRotation = Determinant(Q, n) > 0;

                    if (iters == SymmetricEigenSolver.NO_CONVERGENCE || err / scale > 1e-10 || qErr > 1e-10 || colErr > 1e-10 || !sorted
                            || isRotationBefore != isRotation || solver.IsRotation() != isRotation)
                        failures.Add($"{name} n={n} {sort}: |AQ-QD|={err / scale:E2} |Q^TQ-I|={qErr:E2} |GetEigenvector-Q|={colErr:E2} sorted={sorted} det(Q)>0={isRotation} IsRotation={isRotationBefore}/{solver.IsRotation()}");
                }
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void SVD_RotationX30_SingularValuesAreOne()
        {
            // upstream issue #168
            double cs = Math.Cos(Math.PI / 6), sn = Math.Sin(Math.PI / 6);
            var svd = new SingularValueDecomposition(3, 3, 100);
            svd.Solve(new double[] { 1, 0, 0, 0, cs, -sn, 0, sn, cs });
            double[] S = new double[3];
            svd.GetSingularValues(S);
            Assert.All(S, s => Assert.Equal(1.0, s, 12));
        }
    }
}
