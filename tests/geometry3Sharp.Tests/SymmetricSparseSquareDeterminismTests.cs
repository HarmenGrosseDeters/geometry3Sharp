using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Siblings of issue 1314: SymmetricSparseMatrix.Square and Multiply inserted the entries of the product into one
    /// dictionary under a lock, in the order the parallel loop scheduled its rows. The values are order independent (each
    /// entry is computed alone), but the dictionary enumerates in insertion order, and Multiply(double[], double[]) adds
    /// the terms of every product in that order: the result vector had other last bits for another schedule.
    /// These tests ask for the same bits under every loop order (the schedule adversary of OrderAdversaryTests) and between
    /// parallel and serial runs. PackedSparseMatrix.square_nonsymmetric is serial; its column order comes from a
    /// HashSet of ints (enumerated in insertion order, which is a function of the input), and the test pins that it does
    /// not depend on anything but the input.
    /// </summary>
    public class SymmetricSparseSquareDeterminismTests
    {
        static readonly int[] Orders = { -1, 1, 2, 3 };

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

        // weighted Laplacian of a sphere mesh (symmetric, about 7 nonzeros per row; unequal weights, sums whose order shows)
        static SymmetricSparseMatrix Laplacian()
        {
            DMesh3 mesh = Defects.Sphere(Vector3d.Zero, 1, 12);
            var m = new SymmetricSparseMatrix();
            foreach (int v in mesh.VertexIndices()) {
                double sum = 0;
                foreach (int n in mesh.VtxVerticesItr(v)) {
                    double w = 1.0 / (1 + (v + n) % 5) + 1e-3 * ((v * 7 + n * 13) % 11);
                    if (n > v) m.Set(v, n, -w);
                    sum += w;
                }
                m.Set(v, v, sum + 0.25);
            }
            return m;
        }

        static double[] Vector(int n)
        {
            var x = new double[n];
            for (int i = 0; i < n; ++i) x[i] = Math.Sin(i * 0.7) + 1.0 / (1 + i % 7);
            return x;
        }

        // the matrix-vector product with the result of Square (or Multiply)
        static double[] Apply(SymmetricSparseMatrix r, int n)
        {
            var result = new double[n];
            r.Multiply(Vector(n), result);
            return result;
        }

        static void AssertSameBits(double[] a, double[] b, string what)
        {
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; ++i)
                Assert.True(BitConverter.DoubleToInt64Bits(a[i]) == BitConverter.DoubleToInt64Bits(b[i]), $"{what}: entry {i}: {a[i]:R} / {b[i]:R}");
        }

        [Fact]
        public void Square_SameProductBitsUnderEveryLoopOrder_AndInParallel()
        {
            SymmetricSparseMatrix m = Laplacian();
            int n = m.Rows;
            Assert.True(n > 400);
            double[] reference = WithOrder(0, () => Apply(m.Square(true), n));
            foreach (int order in Orders)
                AssertSameBits(reference, WithOrder(order, () => Apply(m.Square(true), n)), "loop order " + order);
            for (int run = 0; run < 5; ++run)
                AssertSameBits(reference, Apply(m.Square(true), n), "parallel run " + run);
            // the serial variant is the reference too
            AssertSameBits(reference, Apply(m.Square(false), n), "serial");
            Assert.True(reference.Any(v => v != 0) && reference.All(v => !double.IsNaN(v)));
        }

        [Fact]
        public void Square_IsTheProduct()
        {
            SymmetricSparseMatrix m = Laplacian();
            int n = m.Rows;
            SymmetricSparseMatrix sq = m.Square(true);
            double[] x = Vector(n), mx = new double[n], mmx = new double[n], viaSquare = new double[n];
            m.Multiply(x, mx);
            m.Multiply(mx, mmx);
            sq.Multiply(x, viaSquare);
            for (int i = 0; i < n; ++i)
                Assert.True(Math.Abs(mmx[i] - viaSquare[i]) <= 1e-9 * (1 + Math.Abs(mmx[i])), $"{i}: {mmx[i]:R} / {viaSquare[i]:R}");
        }

        [Fact]
        public void Multiply_SameProductBitsUnderEveryLoopOrder_AndInParallel()
        {
            SymmetricSparseMatrix m = Laplacian();
            int n = m.Rows;
            Func<SymmetricSparseMatrix> run = () => { var r = new SymmetricSparseMatrix(); m.Multiply(m, ref r, true); return r; };
            double[] reference = WithOrder(0, () => Apply(run(), n));
            foreach (int order in Orders)
                AssertSameBits(reference, WithOrder(order, () => Apply(run(), n)), "loop order " + order);
            for (int k = 0; k < 5; ++k)
                AssertSameBits(reference, Apply(run(), n), "parallel run " + k);
            Assert.True(reference.Any(v => v != 0));
            // the serial variant is the reference too
            var serial = new SymmetricSparseMatrix();
            m.Multiply(m, ref serial, false);
            AssertSameBits(reference, Apply(serial, n), "serial");
        }

        [Fact]
        public void Multiply_NonCommutingMatrices_Throw()
        {
            var a = new SymmetricSparseMatrix(); var b = new SymmetricSparseMatrix();
            for (int i = 0; i < 6; ++i) { a.Set(i, i, 1 + i); b.Set(i, i, 2); }
            a.Set(0, 1, 1); a.Set(1, 2, 1); b.Set(0, 2, 1); b.Set(2, 3, 1);
            foreach (bool parallel in new[] { true, false }) {
                var r = new SymmetricSparseMatrix();
                Exception e = Record.Exception(() => a.Multiply(b, ref r, parallel));
                Assert.NotNull(e);
            }
        }

        // a non-symmetric matrix, as the packed Square takes it
        static PackedSparseMatrix NonSymmetric(int seed)
        {
            int n = 300;
            var rnd = new Random(seed);
            var entries = new DVector<matrix_entry>();
            for (int r = 0; r < n; ++r) {
                var cols = new HashSet<int>();      // (distinct columns in a row)
                while (cols.Count < 6) cols.Add(rnd.Next(n));
                foreach (int c in cols.OrderBy(c => c))
                    entries.Add(new matrix_entry() { r = r, c = c, value = rnd.NextDouble() - 0.5 });
            }
            var p = new PackedSparseMatrix(entries, n, n, false);
            p.Sort();
            return p;
        }

        [Fact]
        public void SquareNonSymmetric_IsTheProduct_AndRepeatable()
        {
            PackedSparseMatrix p = NonSymmetric(5);
            int n = p.Rows.Length;
            PackedSparseMatrix a = p.Square(), b = p.Square();
            double[] x = Vector(n), px = new double[n], ppx = new double[n], ax = new double[n], bx = new double[n];
            p.Multiply(x, px);
            p.Multiply(px, ppx);
            a.Multiply(x, ax);
            b.Multiply(x, bx);
            AssertSameBits(ax, bx, "repeat");
            for (int i = 0; i < n; ++i)
                Assert.True(Math.Abs(ppx[i] - ax[i]) <= 1e-9 * (1 + Math.Abs(ppx[i])), $"{i}: {ppx[i]:R} / {ax[i]:R}");
            // and the rows of the result list their columns without repeats
            foreach (var row in a.Rows)
                Assert.Equal(row.Length, row.Select(nz => nz.j).Distinct().Count());
        }
    }
}
