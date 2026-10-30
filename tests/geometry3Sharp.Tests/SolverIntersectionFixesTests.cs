using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Regression tests for the solvers/, intersection/ and comp_geom/ findings of the class-by-class
    /// review (docs/Analyse-2026-10.md, section 2.6).
    /// </summary>
    public class SolverIntersectionFixesTests
    {
        const double Tol = 1e-10;

        static DenseMatrix Dense(int rows, int cols, params double[] values)
        {
            var m = new DenseMatrix(rows, cols);
            m.Set(values);
            return m;
        }

        static double[] DenseProduct(DenseMatrix a, DenseMatrix b)
        {
            var r = new double[a.Rows * b.Columns];
            for (int i = 0; i < a.Rows; ++i)
                for (int j = 0; j < b.Columns; ++j)
                    for (int k = 0; k < a.Columns; ++k)
                        r[i * b.Columns + j] += a[i, k] * b[k, j];
            return r;
        }

        static void AssertAllClose(double[] expected, double[] actual, double tol = Tol)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; ++i)
                Assert.True(Math.Abs(expected[i] - actual[i]) <= tol, $"index {i}: expected {expected[i]}, got {actual[i]}");
        }


        // --- DenseMatrix ---

        [Fact]
        public void DenseMatrix_Transpose_NonSquare()
        {
            DenseMatrix a = Dense(2, 3, 1, 2, 3, 4, 5, 6);
            DenseMatrix t = a.Transpose();
            Assert.Equal(3, t.Rows);
            Assert.Equal(2, t.Columns);
            AssertAllClose(new double[] { 1, 4, 2, 5, 3, 6 }, t.Buffer);
        }

        [Fact]
        public void DenseMatrix_TransposeInPlace_NonSquare()
        {
            DenseMatrix a = Dense(2, 3, 1, 2, 3, 4, 5, 6);
            a.TransposeInPlace();
            Assert.Equal(3, a.Rows);
            Assert.Equal(2, a.Columns);
            AssertAllClose(new double[] { 1, 4, 2, 5, 3, 6 }, a.Buffer);
        }

        [Fact]
        public void DenseMatrix_MultiplyVector_NonSquare()
        {
            DenseMatrix a = Dense(2, 3, 1, 2, 3, 4, 5, 6);
            DenseVector x = new DenseVector(3);
            x[0] = 1; x[1] = 1; x[2] = 1;
            DenseVector r = a.Multiply(x);
            AssertAllClose(new double[] { 6, 15 }, r.Buffer);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DenseMatrix_MultiplyMatrix_NonSquare(bool bParallel)
        {
            DenseMatrix a = Dense(2, 3, 1, 2, 3, 4, 5, 6);
            DenseMatrix b = Dense(3, 4, 1, 0, 2, -1, 3, 1, 0, 2, -2, 1, 1, 0);
            DenseMatrix r = a.Multiply(b, bParallel);
            Assert.Equal(2, r.Rows);
            Assert.Equal(4, r.Columns);
            AssertAllClose(DenseProduct(a, b), r.Buffer);
        }

        [Fact]
        public void DenseMatrix_IsPositiveDefinite_NotOnlyDiagonalDominance()
        {
            // positive definite (eigenvalues 0.1, 0.1, 2.8) but not diagonally dominant
            DenseMatrix pd = Dense(3, 3, 1, 0.9, 0.9, 0.9, 1, 0.9, 0.9, 0.9, 1);
            Assert.True(pd.IsPositiveDefinite());

            // weakly diagonally dominant but singular, ie only semi-definite
            DenseMatrix singular = Dense(2, 2, 1, 1, 1, 1);
            Assert.False(singular.IsPositiveDefinite());

            DenseMatrix zero = new DenseMatrix(2, 2);
            Assert.False(zero.IsPositiveDefinite());
        }


        // --- CholeskyDecomposition ---

        [Fact]
        public void Cholesky_Compute_ReturnsFalseForIndefiniteMatrix()
        {
            DenseMatrix indefinite = Dense(2, 2, 1, 2, 2, 1);      // eigenvalues 3, -1
            Assert.False(new CholeskyDecomposition(indefinite).Compute());
            Assert.False(new CholeskyDecomposition(indefinite).ComputeParallel());

            DenseMatrix pd = Dense(3, 3, 4, 2, 0, 2, 5, 1, 0, 1, 3);
            var chol = new CholeskyDecomposition(pd);
            Assert.True(chol.Compute());
            AssertAllClose(pd.Buffer, chol.L.Multiply(chol.L.Transpose()).Buffer);
            Assert.True(new CholeskyDecomposition(pd).ComputeParallel());
        }

        [Fact]
        public void Cholesky_ComputeParallel_OneByOne()
        {
            var chol = new CholeskyDecomposition(Dense(1, 1, 4));
            Assert.True(chol.ComputeParallel());
            Assert.Equal(2.0, chol.L[0, 0], 12);
        }


        // --- SparseSymmetricCG ---

        static Action<double[], double[]> DiagonalMultiply(double[] diag)
        {
            return (x, b) => { for (int i = 0; i < x.Length; ++i) b[i] = diag[i] * x[i]; };
        }

        [Fact]
        public void SparseSymmetricCG_ZeroRHS_GivesZeroSolution()
        {
            var cg = new SparseSymmetricCG() {
                B = new double[] { 0, 0, 0 },
                MultiplyF = DiagonalMultiply(new double[] { 1, 2, 3 }),
                UseXAsInitialGuess = false
            };
            Assert.True(cg.Solve());
            AssertAllClose(new double[] { 0, 0, 0 }, cg.X);
        }

        [Fact]
        public void SparseSymmetricCG_StopCriterionUsesLinearNorm()
        {
            // |B| = 1e6*sqrt(2). The initial residual has norm 0.05: below sqrt(1e-8*|B|) (the
            // old squared-vs-linear test) but far above the intended 1e-8*|B|.
            double[] diag = { 1, 2 };
            double[] B = { 1e6, 1e6 };
            var cg = new SparseSymmetricCG() {
                B = B,
                MultiplyF = DiagonalMultiply(diag),
                X = new double[] { 1e6 + 0.05, 0.5e6 },
                UseXAsInitialGuess = true
            };
            Assert.True(cg.Solve());
            double resid = Math.Sqrt(Enumerable.Range(0, 2).Sum(i => Math.Pow(B[i] - diag[i] * cg.X[i], 2)));
            Assert.True(resid <= 1e-8 * Math.Sqrt(2) * 1e6, $"residual {resid}");
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void SparseSymmetricCG_NonFiniteRHS_FailsWithoutRunningAllIterations(double bad)
        {
            double[] diag = { 1, 2, 3 };
            foreach (bool preconditioned in new[] { false, true }) {
                int multiplies = 0;
                var multiply = DiagonalMultiply(diag);
                var cg = new SparseSymmetricCG() {
                    B = new double[] { 1, bad, 3 },
                    MultiplyF = (x, r) => { multiplies++; multiply(x, r); },
                    PreconditionMultiplyF = (x, r) => Array.Copy(x, r, x.Length),
                    UseXAsInitialGuess = false
                };
                Assert.False(preconditioned ? cg.SolvePreconditioned() : cg.Solve());
                Assert.True(multiplies <= 2, $"preconditioned {preconditioned}: {multiplies} multiplies");
            }
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void SparseSymmetricCGMultipleRHS_OneNonFiniteRHS_FailsWithoutRunningAllIterations(double bad)
        {
            double[] diag = { 1, 2, 3 };
            foreach (bool preconditioned in new[] { false, true }) {
                int multiplies = 0;
                var multiply = DiagonalMultiplyN(diag);
                var cg = new SparseSymmetricCGMultipleRHS() {
                    B = new double[][] { new double[] { 1, 2, 3 }, new double[] { 1, bad, 3 } },
                    MultiplyF = (x, r) => { multiplies++; multiply(x, r); },
                    PreconditionMultiplyF = (x, r) => { for (int j = 0; j < x.Length; ++j) Array.Copy(x[j], r[j], x[j].Length); },
                    UseXAsInitialGuess = false
                };
                Assert.False(preconditioned ? cg.SolvePreconditioned() : cg.Solve());
                Assert.True(multiplies <= 2, $"preconditioned {preconditioned}: {multiplies} multiplies");
            }
        }

        [Fact]
        public void SparseSymmetricCG_NonFiniteMatrix_FailsWithoutRunningAllIterations()
        {
            // the right-hand sides are finite: the residual turns non-finite only inside the iteration
            double[] diag = { 1, double.NaN, 3 };
            int multiplies = 0;
            var multiply = DiagonalMultiply(diag);
            var cg = new SparseSymmetricCG() {
                B = new double[] { 1, 2, 3 },
                MultiplyF = (x, r) => { multiplies++; multiply(x, r); },
                UseXAsInitialGuess = false
            };
            Assert.False(cg.Solve());
            Assert.True(multiplies <= 2, $"{multiplies} multiplies");

            multiplies = 0;
            var multiplyN = DiagonalMultiplyN(diag);
            var cgN = new SparseSymmetricCGMultipleRHS() {
                B = new double[][] { new double[] { 1, 2, 3 } },
                MultiplyF = (x, r) => { multiplies++; multiplyN(x, r); },
                UseXAsInitialGuess = false
            };
            Assert.False(cgN.Solve());
            Assert.True(multiplies <= 2, $"{multiplies} multiplies");
        }

        [Fact]
        public void LaplacianMeshSmoother_InfiniteVertex_FailsAndKeepsTheMesh()
        {
            DMesh3 mesh = Defects.Sphere(Vector3d.Zero, 1, 6);
            int bad = mesh.VertexIndices().Skip(mesh.VertexCount / 3).First();
            mesh.SetVertex(bad, new Vector3d(double.PositiveInfinity, 0.5, 0.5));
            Vector3d[] before = mesh.VertexIndices().Select(v => mesh.GetVertex(v)).ToArray();
            var smoother = new LaplacianMeshSmoother(mesh);
            foreach (int v in mesh.VertexIndices())
                smoother.SetConstraint(v, mesh.GetVertex(v), 0.5);
            // twice: the same answer on a second solve of the instance
            Assert.False(smoother.SolveAndUpdateMesh());
            Assert.False(smoother.SolveAndUpdateMesh());
            Assert.Equal(before, mesh.VertexIndices().Select(v => mesh.GetVertex(v)).ToArray());
        }

        static Action<double[][], double[][]> DiagonalMultiplyN(double[] diag)
        {
            return (X, B) => {
                for (int j = 0; j < X.Length; ++j)
                    for (int i = 0; i < X[j].Length; ++i) B[j][i] = diag[i] * X[j][i];
            };
        }

        [Fact]
        public void SparseSymmetricCGMultipleRHS_ZeroRHS_StaysFinite()
        {
            double[] diag = { 1, 2, 3 };
            var cg = new SparseSymmetricCGMultipleRHS() {
                B = new double[][] { new double[] { 0, 0, 0 }, new double[] { 1, 2, 3 } },
                MultiplyF = DiagonalMultiplyN(diag),
                UseXAsInitialGuess = false
            };
            Assert.True(cg.Solve());
            AssertAllClose(new double[] { 0, 0, 0 }, cg.X[0]);
            AssertAllClose(new double[] { 1, 1, 1 }, cg.X[1], 1e-6);
        }

        [Fact]
        public void SparseSymmetricCGMultipleRHS_InitiallyConvergedRHS_IsNotIterated()
        {
            double[] diag = { 1, 2, 3 };
            var cg = new SparseSymmetricCGMultipleRHS() {
                B = new double[][] { new double[] { 1, 2, 3 }, new double[] { 2, 2, 2 } },
                MultiplyF = DiagonalMultiplyN(diag),
                // first initial guess is the exact solution
                X = new double[][] { new double[] { 1, 1, 1 }, new double[] { 0, 0, 0 } },
                UseXAsInitialGuess = true
            };
            Assert.True(cg.Solve());
            AssertAllClose(new double[] { 1, 1, 1 }, cg.X[0]);
            AssertAllClose(new double[] { 2, 1, 2.0 / 3.0 }, cg.X[1], 1e-6);
        }


        // --- SymmetricSparseMatrix ---

        static SymmetricSparseMatrix Sym(DenseMatrix m) { return new SymmetricSparseMatrix(m); }

        [Fact]
        public void SymmetricSparseMatrix_Multiply_NonSymmetricProductThrows()
        {
            // A*B is not symmetric, so it cannot be stored in a SymmetricSparseMatrix
            DenseMatrix a = Dense(2, 2, 1, 2, 2, 1);
            DenseMatrix b = Dense(2, 2, 3, 0, 0, 1);
            Assert.Throws<Exception>(() => Sym(a).Multiply(Sym(b)));
        }

        [Fact]
        public void SymmetricSparseMatrix_Multiply_SymmetricProduct_Guard()
        {
            // guard: products of commuting matrices (here A*A) are symmetric and still work
            DenseMatrix a = Dense(3, 3, 2, 1, 0, 1, 3, 1, 0, 1, 4);
            SymmetricSparseMatrix r = Sym(a).Multiply(Sym(a));
            double[] expected = DenseProduct(a, a);
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    Assert.Equal(expected[i * 3 + j], r[i, j], 10);
        }


        // --- PackedSparseMatrix ---

        [Fact]
        public void PackedSparseMatrix_DotRowColumn_EmptyRow()
        {
            DenseMatrix a = Dense(3, 3, 1, 0, 2, 0, 0, 0, 2, 0, 3);
            PackedSparseMatrix m = PackedSparseMatrix.FromDense(a, true);
            m.Sort();
            Assert.Equal(0.0, m.DotRowColumn(1, 0, m));
            Assert.Equal(0.0, m.DotRowColumn(0, 1, m));
            Assert.Equal(1 * 2 + 2 * 3, m.DotRowColumn(0, 2, m));
        }

        [Fact]
        public void PackedSparseMatrix_DotColumnVector_DefaultEndRow()
        {
            DenseMatrix a = Dense(3, 3, 1, 0, 2, 4, 5, 0, 7, 0, 3);
            double[] vec = { 1, 2, 3 };
            foreach (bool sorted in new[] { true, false }) {
                PackedSparseMatrix m = PackedSparseMatrix.FromDense(a, false);
                if (sorted)
                    m.Sort();
                Assert.Equal(1 * 1 + 4 * 2 + 7 * 3, m.DotColumnVector(0, vec));
                Assert.Equal(4 * 2 + 7 * 3, m.DotColumnVector(0, vec, 1));
            }
        }

        [Fact]
        public void PackedSparseMatrix_Square_NonSymmetric()
        {
            DenseMatrix a = Dense(3, 3, 1, 2, 0, 0, 1, 3, 4, 0, 1);
            PackedSparseMatrix m = PackedSparseMatrix.FromDense(a, false);
            m.Sort();
            PackedSparseMatrix sq = m.Square();
            double[] expected = DenseProduct(a, a);
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    Assert.True(Math.Abs(expected[i * 3 + j] - sq[i, j]) < Tol, $"({i},{j}): expected {expected[i * 3 + j]}, got {sq[i, j]}");
        }

        [Fact]
        public void PackedSparseMatrix_Square_Symmetric_Guard()
        {
            DenseMatrix a = Dense(3, 3, 2, 1, 0, 1, 3, 1, 0, 1, 4);
            PackedSparseMatrix m = PackedSparseMatrix.FromDense(a, true);
            m.Sort();
            PackedSparseMatrix sq = m.Square();
            double[] expected = DenseProduct(a, a);
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    Assert.Equal(expected[i * 3 + j], sq[i, j], 10);
        }

        [Fact]
        public void PackedSparseMatrix_MaxNorm_UsesAbsoluteValue()
        {
            PackedSparseMatrix m = PackedSparseMatrix.FromDense(Dense(2, 2, 1, 0, 0, -5), true);
            Assert.Equal(5.0, m.MaxNorm);
        }


        // --- intersection ---

        [Fact]
        public void IntrLine2Triangle2_DegenerateTriangleOnLine()
        {
            var tri = new Triangle2d(new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(1, 0));
            var intr = new IntrLine2Triangle2(new Line2d(new Vector2d(-1, 0), Vector2d.AxisX), tri);
            Assert.True(intr.Find());
            Assert.Equal(IntersectionType.Segment, intr.Type);
            Assert.Equal(1.0, intr.Param0, 12);
            Assert.Equal(3.0, intr.Param1, 12);

            var sintr = new IntrSegment2Triangle2(new Segment2d(new Vector2d(-1, 0), new Vector2d(5, 0)), tri);
            Assert.True(sintr.Find());
            Assert.Equal(IntersectionType.Segment, sintr.Type);
        }

        [Fact]
        public void IntrRay3Triangle3_SmallTriangleIsNotParallel()
        {
            double s = 1e-5;
            var tri = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(s, 0, 0), new Vector3d(0, s, 0));
            var ray = new Ray3d(new Vector3d(s / 4, s / 4, 1), -Vector3d.AxisZ);

            var intr = new IntrRay3Triangle3(ray, tri);
            Assert.True(intr.Find());
            Assert.Equal(1.0, intr.RayParameter, 12);

            Vector3d v0 = tri.V0, v1 = tri.V1, v2 = tri.V2;
            Assert.True(IntrRay3Triangle3.Intersects(ref ray, ref v0, ref v1, ref v2, out double rayT));
            Assert.Equal(1.0, rayT, 12);
        }

        [Fact]
        public void IntrRay3Triangle3_ParallelRay_Guard()
        {
            // guard: a ray in the plane of a large triangle is still reported as parallel
            var tri = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(100, 0, 0), new Vector3d(0, 100, 0));
            var ray = new Ray3d(new Vector3d(-1, 10, 0), Vector3d.AxisX);
            Assert.False(new IntrRay3Triangle3(ray, tri).Find());
            Vector3d v0 = tri.V0, v1 = tri.V1, v2 = tri.V2;
            Assert.False(IntrRay3Triangle3.Intersects(ref ray, ref v0, ref v1, ref v2, out double rayT));
        }

        [Fact]
        public void IntrRay3Triangle3_FailedQueryResetsType()
        {
            var tri = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            var intr = new IntrRay3Triangle3(new Ray3d(new Vector3d(0.25, 0.25, 1), -Vector3d.AxisZ), tri);
            Assert.True(intr.Find());
            Assert.Equal(IntersectionType.Point, intr.Type);

            intr.Ray = new Ray3d(new Vector3d(5, 5, 1), -Vector3d.AxisZ);
            Assert.False(intr.Find());
            Assert.Equal(IntersectionType.Empty, intr.Type);
            Assert.Equal(0, intr.Quantity);
        }

        [Fact]
        public void IntrTriangle3Triangle3_FailedQueryResetsType()
        {
            var t0 = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(0, 2, 0));
            var t1 = new Triangle3d(new Vector3d(0.5, 0.5, -1), new Vector3d(0.5, 0.5, 1), new Vector3d(1, 0.25, 0));
            var intr = new IntrTriangle3Triangle3(t0, t1);
            Assert.True(intr.Find());
            Assert.NotEqual(IntersectionType.Empty, intr.Type);
            Assert.True(intr.Quantity > 0);

            intr.Triangle1 = new Triangle3d(new Vector3d(0, 0, 5), new Vector3d(1, 0, 5), new Vector3d(0, 1, 5));
            Assert.False(intr.Find());
            Assert.Equal(IntersectionType.Empty, intr.Type);
            Assert.Equal(0, intr.Quantity);
        }

        static Triangle2d Flip(Triangle2d t) { return new Triangle2d(t.V0, t.V2, t.V1); }

        static double PolygonArea(Vector2d[] pts, int n)
        {
            double a = 0;
            for (int i = 0; i < n; ++i)
                a += pts[i].DotPerp(pts[(i + 1) % n]);
            return Math.Abs(a) / 2;
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void IntrTriangle2Triangle2_OrientationIndependent(bool cw0, bool cw1)
        {
            var big = new Triangle2d(new Vector2d(0, 0), new Vector2d(4, 0), new Vector2d(0, 4));    // CCW
            var small = new Triangle2d(new Vector2d(0.5, 0.5), new Vector2d(1.5, 0.5), new Vector2d(0.5, 1.5));
            Triangle2d t0 = cw0 ? Flip(big) : big;
            Triangle2d t1 = cw1 ? Flip(small) : small;

            var intr = new IntrTriangle2Triangle2(t0, t1);
            Assert.True(intr.Test());
            Assert.True(intr.Find());
            Assert.Equal(IntersectionType.Polygon, intr.Type);
            Assert.Equal(0.5, PolygonArea(intr.Points, intr.Quantity), 10);

            // and the other way around
            intr = new IntrTriangle2Triangle2(t1, t0);
            Assert.True(intr.Test());
            Assert.True(intr.Find());
            Assert.Equal(0.5, PolygonArea(intr.Points, intr.Quantity), 10);

            // separated triangles
            var far = new Triangle2d(new Vector2d(10, 10), new Vector2d(11, 10), new Vector2d(10, 11));
            intr = new IntrTriangle2Triangle2(t0, cw1 ? Flip(far) : far);
            Assert.False(intr.Test());
            Assert.False(intr.Find());
        }


        // --- comp_geom ---

        [Fact]
        public void Arrangement2d_SnapsOnlyWithinVertexSnapTol()
        {
            var arr = new Arrangement2d(new AxisAlignedBox2d(0, 0, 2, 2));
            arr.Insert(new Vector2d(0, 0), new Vector2d(1, 0));
            // 2e-3 away from (1,0): far outside VertexSnapTol = 1e-5, must not be merged
            arr.Insert(new Vector2d(1.002, 0), new Vector2d(1.002, 1));
            Assert.Equal(4, arr.Graph.VertexCount);

            // within the tolerance it is merged
            arr.Insert(new Vector2d(0, 1e-6), new Vector2d(0, 1));
            Assert.Equal(5, arr.Graph.VertexCount);
        }
    }
}
