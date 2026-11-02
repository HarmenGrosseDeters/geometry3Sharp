using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// DenseSimplex (the small LP solver of the separation prototype, MeshRoundingRepair.Separation.cs): textbook
    /// programs with known optima, unboundedness, a cycling example that needs Bland's rule, and invalid input.
    /// </summary>
    public class DenseSimplexTests
    {
        [Fact]
        public void TextbookProgram_Optimum()
        {
            // Chvátal, Linear Programming, chapter 2: max 5 x1 + 4 x2 + 3 x3, optimum 13 at (2, 0, 1)
            var A = new double[,] { { 2, 3, 1 }, { 4, 1, 2 }, { 3, 4, 2 } };
            var lp = new DenseSimplex();
            Assert.Equal(DenseSimplex.Status.Optimal, lp.Solve(A, new double[] { 5, 11, 8 }, new double[] { 5, 4, 3 }));
            Assert.Equal(13, lp.Objective, 12);
            Assert.Equal(2, lp.X[0], 12);
            Assert.Equal(0, lp.X[1], 12);
            Assert.Equal(1, lp.X[2], 12);
        }

        [Fact]
        public void ZeroRightHandSide_ZeroIsOptimalWhereNothingCanGrow()
        {
            // max x1 + x2 with x1 - x2 <= 0 and x1 + x2 <= 0: only 0
            var lp = new DenseSimplex();
            Assert.Equal(DenseSimplex.Status.Optimal, lp.Solve(new double[,] { { 1, -1 }, { 1, 1 } }, new double[] { 0, 0 }, new double[] { 1, 1 }));
            Assert.Equal(0, lp.Objective);
        }

        [Fact]
        public void Unbounded()
        {
            // max x1 with x1 - x2 <= 1: x1 = x2 + 1 grows without bound
            var lp = new DenseSimplex();
            Assert.Equal(DenseSimplex.Status.Unbounded, lp.Solve(new double[,] { { 1, -1 } }, new double[] { 1 }, new double[] { 1, 0 }));
        }

        [Fact]
        public void CyclingExample_TerminatesWithBland()
        {
            // Chvátal's example of cycling under the largest-coefficient rule (chapter 3): max 10 x1 - 57 x2 - 9 x3 - 24 x4
            // with 0.5 x1 - 5.5 x2 - 2.5 x3 + 9 x4 <= 0, 0.5 x1 - 1.5 x2 - 0.5 x3 + x4 <= 0, x1 <= 1; optimum 1 at
            // (1, 0, 1, 0)
            var A = new double[,] { { 0.5, -5.5, -2.5, 9 }, { 0.5, -1.5, -0.5, 1 }, { 1, 0, 0, 0 } };
            var lp = new DenseSimplex();
            Assert.Equal(DenseSimplex.Status.Optimal, lp.Solve(A, new double[] { 0, 0, 1 }, new double[] { 10, -57, -9, -24 }));
            Assert.Equal(1, lp.Objective, 12);
            Assert.True(lp.Pivots < 200, $"{lp.Pivots} pivots");
        }

        [Fact]
        public void InvalidInput()
        {
            var lp = new DenseSimplex();
            Assert.Equal(DenseSimplex.Status.Invalid, lp.Solve(new double[,] { { 1 } }, new double[] { -1 }, new double[] { 1 }));
            Assert.Equal(DenseSimplex.Status.Invalid, lp.Solve(new double[,] { { double.NaN } }, new double[] { 1 }, new double[] { 1 }));
            Assert.Equal(DenseSimplex.Status.Invalid, lp.Solve(new double[,] { { 1 } }, new double[] { 1, 2 }, new double[] { 1 }));
            Assert.Equal(DenseSimplex.Status.Invalid, lp.Solve(new double[,] { { 1 } }, new double[] { 1 }, new double[] { double.PositiveInfinity }));
        }

        [Fact]
        public void IterationLimit()
        {
            var lp = new DenseSimplex() { MaxPivots = 1 };
            var A = new double[,] { { 2, 3, 1 }, { 4, 1, 2 }, { 3, 4, 2 } };
            Assert.Equal(DenseSimplex.Status.IterationLimit, lp.Solve(A, new double[] { 5, 11, 8 }, new double[] { 5, 4, 3 }));
        }

        [Fact]
        public void Random_FeasibleAndNotWorseThanVertices()
        {
            // random programs with box rows: the solution is feasible (within rounding), and at least as good as every
            // corner of the box that is feasible
            var rnd = new Random(7);
            for (int round = 0; round < 200; round++) {
                int n = 2 + rnd.Next(3), m = 1 + rnd.Next(4);
                var A = new double[m + n, n];
                var b = new double[m + n];
                var c = new double[n];
                for (int i = 0; i < m; i++) {
                    for (int j = 0; j < n; j++) A[i, j] = rnd.NextDouble() * 4 - 2;
                    b[i] = rnd.NextDouble() * 3;
                }
                for (int j = 0; j < n; j++) { A[m + j, j] = 1; b[m + j] = 1; c[j] = rnd.NextDouble() * 2 - 1; }
                var lp = new DenseSimplex();
                Assert.Equal(DenseSimplex.Status.Optimal, lp.Solve(A, b, c));
                for (int i = 0; i < m + n; i++) {
                    double ax = 0;
                    for (int j = 0; j < n; j++) ax += A[i, j] * lp.X[j];
                    Assert.True(ax <= b[i] + 1e-9, $"round {round} row {i}: {ax} > {b[i]}");
                }
                for (int corner = 0; corner < (1 << n); corner++) {
                    var x = new double[n];
                    double obj = 0;
                    for (int j = 0; j < n; j++) { x[j] = (corner >> j) & 1; obj += c[j] * x[j]; }
                    bool feasible = true;
                    for (int i = 0; i < m && feasible; i++) {
                        double ax = 0;
                        for (int j = 0; j < n; j++) ax += A[i, j] * x[j];
                        feasible = ax <= b[i];
                    }
                    if (feasible) Assert.True(lp.Objective >= obj - 1e-9, $"round {round}: {lp.Objective} < {obj}");
                }
            }
        }
    }
}
