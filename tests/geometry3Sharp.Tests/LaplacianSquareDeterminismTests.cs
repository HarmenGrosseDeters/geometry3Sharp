using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue 1314: PackedSparseMatrix.Square appended the entries of the squared matrix to one list under a lock, in the
    /// order the blocks of its parallel loop finished. The rows of the result kept that order, Multiply adds the terms of
    /// a product in row order, and so the conjugate-gradient solves of LaplacianMeshSmoother, LaplacianMeshDeformer and
    /// LaplacianCurveDeformer gave other last bits for another schedule (another processor count, sometimes another run).
    /// (The curve deformer has no test of its own here: it uses the same Square, and its solver did not converge on the
    /// test curves tried.)
    /// The entries are now computed per row, in the order of their columns: these tests ask for the same bits under
    /// every loop order (the schedule adversary of OrderAdversaryTests), and between parallel and serial runs.
    /// </summary>
    public class LaplacianSquareDeterminismTests
    {
        static readonly int[] Orders = { -1, 1, 2, 3 };

        // f under the loop order (0: as given), serially on this thread
        static T WithOrder<T>(int order, Func<T> f)
        {
            bool serial = gParallel.SerialOnThisThread;
            gParallel.SerialOnThisThread = true;
            gParallel.SerialOrderOnThisThread = order;
            try {
                return f();
            } finally {
                gParallel.SerialOrderOnThisThread = 0;
                gParallel.SerialOnThisThread = serial;
            }
        }

        // the uniform Laplacian of a mesh (symmetric, about 7 nonzeros per row), with a weight on the diagonal
        static SymmetricSparseMatrix Laplacian(DMesh3 mesh)
        {
            var m = new SymmetricSparseMatrix();
            foreach (int v in mesh.VertexIndices()) {
                double sum = 0;
                foreach (int n in mesh.VtxVerticesItr(v)) {
                    double w = 1.0 / (1 + (v + n) % 5);     // (unequal weights: sums whose order shows)
                    if (n > v) m.Set(v, n, -w);
                    sum += w;
                }
                m.Set(v, v, sum + 0.25);
            }
            return m;
        }

        static string Difference(PackedSparseMatrix a, PackedSparseMatrix b)
        {
            if (a.Rows.Length != b.Rows.Length) return "row counts " + a.Rows.Length + " / " + b.Rows.Length;
            for (int r = 0; r < a.Rows.Length; ++r) {
                if (a.Rows[r].Length != b.Rows[r].Length) return $"row {r}: {a.Rows[r].Length} / {b.Rows[r].Length} nonzeros";
                for (int k = 0; k < a.Rows[r].Length; ++k) {
                    if (a.Rows[r][k].j != b.Rows[r][k].j) return $"row {r}, nonzero {k}: column {a.Rows[r][k].j} / {b.Rows[r][k].j}";
                    if (BitConverter.DoubleToInt64Bits(a.Rows[r][k].d) != BitConverter.DoubleToInt64Bits(b.Rows[r][k].d))
                        return $"row {r}, nonzero {k}: value {a.Rows[r][k].d:R} / {b.Rows[r][k].d:R}";
                }
            }
            return "";
        }

        [Fact]
        public void Square_SameRowsUnderEveryLoopOrder_AndInParallel()
        {
            SymmetricSparseMatrix m = Laplacian(Defects.Sphere(Vector3d.Zero, 1, 12));
            PackedSparseMatrix reference = WithOrder(0, () => m.SquarePackedParallel());
            Assert.True(reference.Rows.Length > 400, "the loop needs several blocks: " + reference.Rows.Length);
            foreach (int order in Orders)
                Assert.Equal("", Difference(reference, WithOrder(order, () => m.SquarePackedParallel())));
            for (int run = 0; run < 5; ++run)
                Assert.Equal("", Difference(reference, m.SquarePackedParallel()));
        }

        [Fact]
        public void Square_IsTheProduct_RowsInColumnOrder()
        {
            DMesh3 mesh = Defects.Sphere(Vector3d.Zero, 1, 4);
            SymmetricSparseMatrix m = Laplacian(mesh);
            int n = m.Rows;
            PackedSparseMatrix sq = m.SquarePackedParallel();
            // the dense product, by definition
            var dense = new double[n, n];
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    double s = 0;
                    for (int k = 0; k < n; ++k) s += m[i, k] * m[k, j];
                    dense[i, j] = s;
                }
            int nonzeros = 0;
            for (int r = 0; r < n; ++r) {
                var seen = new HashSet<int>();
                int last = -1;
                foreach (PackedSparseMatrix.nonzero nz in sq.Rows[r]) {
                    Assert.True(seen.Add(nz.j), $"row {r}: column {nz.j} twice");
                    Assert.True(Math.Abs(nz.d - dense[r, nz.j]) <= 1e-12 * (1 + Math.Abs(dense[r, nz.j])), $"({r}, {nz.j}): {nz.d:R} / {dense[r, nz.j]:R}");
                    // (the mirrored entries of the rows before r come in the order of those rows, then the row's own
                    // entries, columns >= r, in the order of their columns: the whole row is in column order)
                    Assert.True(nz.j > last, $"row {r}: column {nz.j} after {last}");
                    last = nz.j;
                    nonzeros++;
                }
                for (int c = 0; c < n; ++c)
                    if (!seen.Contains(c)) Assert.True(Math.Abs(dense[r, c]) <= MathUtil.ZeroTolerance, $"({r}, {c}) is missing: {dense[r, c]:R}");
            }
            Assert.True(nonzeros > 5 * n);
        }

        // ---------------------------------------------------------------- the users

        static DMesh3 Smoothed(bool multipleRHS)
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 12);
            int top = m.VertexIndices().OrderByDescending(v => m.GetVertex(v).z).ThenBy(v => v).First();
            foreach (int t in m.VtxTrianglesItr(top).ToList()) m.RemoveTriangle(t, true, false);
            var sm = new LaplacianMeshSmoother(m);
            foreach (int v in m.VertexIndices())
                sm.SetConstraint(v, m.GetVertex(v), m.IsBoundaryVertex(v) ? 100 : 0.5, m.IsBoundaryVertex(v));
            Vector3d[] result = new Vector3d[m.MaxVertexID];
            Assert.True(multipleRHS ? sm.SolveMultipleRHS(result) : sm.SolveMultipleCG(result));
            // the result is a smoothing: the hard constraints (the boundary of the hole) are met exactly, the soft ones
            // hold every other vertex near its place, and the mesh did change
            double moved = 0;
            foreach (int v in m.VertexIndices()) {
                double d = result[v].Distance(m.GetVertex(v));
                if (m.IsBoundaryVertex(v)) Assert.True(d == 0, $"boundary vertex {v} moved by {d:R}");
                Assert.True(d < 0.25, $"vertex {v} moved by {d:R}");
                moved = Math.Max(moved, d);
            }
            Assert.True(moved > 1e-4, "nothing was smoothed: " + moved.ToString("R"));
            foreach (int v in m.VertexIndices()) m.SetVertex(v, result[v]);
            Assert.True(Invariance.AllFinite(m));
            return m;
        }

        static DMesh3 Deformed()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 12);
            var deformer = new LaplacianMeshDeformer(m);
            int lowest = m.VertexIndices().OrderBy(v => m.GetVertex(v).z).ThenBy(v => v).First();
            int highest = m.VertexIndices().OrderByDescending(v => m.GetVertex(v).z).ThenBy(v => v).First();
            Vector3d fixedAt = m.GetVertex(lowest), target = m.GetVertex(highest) + new Vector3d(0.125, 0, 0.5);
            deformer.SetConstraint(lowest, fixedAt, 10, true);
            deformer.SetConstraint(highest, target, 10, true);
            Assert.True(deformer.SolveAndUpdateMesh());
            Assert.True(Invariance.AllFinite(m));
            // the result is the deformation asked for: both hard constraints are met exactly, and the surface followed
            Assert.True(m.GetVertex(lowest) == fixedAt && m.GetVertex(highest) == target, "the constraints are not met");
            Assert.True(m.VertexIndices().Count(v => m.GetVertex(v).z > 1.0625) > 5, "the surface did not follow the handle");
            return m;
        }

        static void AssertOrderIndependent(Func<DMesh3> run)
        {
            DMesh3 reference = WithOrder(0, run);
            foreach (int order in Orders) {
                string diff = Invariance.Difference(reference, WithOrder(order, run));
                Assert.True(diff == "", $"loop order {order}: {diff}");
            }
            // parallel equals serial, repeatedly
            for (int k = 0; k < 4; ++k) {
                string diff = Invariance.Difference(reference, run());
                Assert.True(diff == "", $"parallel run {k}: {diff}");
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Smoother_SameBitsUnderEveryLoopOrder_AndInParallel(bool multipleRHS)
        {
            AssertOrderIndependent(() => Smoothed(multipleRHS));
        }

        [Fact]
        public void Deformer_SameBitsUnderEveryLoopOrder_AndInParallel()
        {
            AssertOrderIndependent(Deformed);
        }
    }
}
