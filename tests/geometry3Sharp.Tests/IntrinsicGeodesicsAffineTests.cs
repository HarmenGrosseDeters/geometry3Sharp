using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class IntrinsicGeodesicsAffineTests
    {
        [Theory]
        [InlineData(false, 1.0, LogMapStrategy.AffineLocal)]
        [InlineData(true, 0.125, LogMapStrategy.AffineLocal)]
        [InlineData(false, 1.0, LogMapStrategy.AffineAdaptive)]
        [InlineData(true, 0.125, LogMapStrategy.AffineAdaptive)]
        public void Affine_MatchesFullHomogeneousSystem(bool refine, double time, LogMapStrategy strategy)
        {
            DMesh3 mesh = IntrinsicGeodesicsTests.Grid(2, 4, (x, y) => 0.3 * x * y);
            var method = new VectorHeatMethod(mesh) { IntrinsicRefine = refine, RefineMinAngle = 0, TimeFactor = time };
            foreach (int source in new[] { 0, 5 }) {
                Vector2d[] actual = method.ComputeLogMap(source, strategy);
                IntrinsicTriangulation t = method.Triangulation;
                int n = t.VertexCount, s = t.VertexIndex(source);
                double[,] matrix = Assemble(t, method.DiffusionTime, null);
                var rhs = new double[3 * n];
                rhs[3 * s] = 1;
                double[] reference = DenseSolve(matrix, rhs);
                if (strategy == LogMapStrategy.AffineAdaptive) matrix = Assemble(t, method.DiffusionTime, reference);
                rhs[3 * s] = 0;
                rhs[3 * s + 2] = 1;
                double[] homogeneous = DenseSolve(matrix, rhs);
                foreach (int v in mesh.VertexIndices()) {
                    int i = t.VertexIndex(v);
                    Vector2d p = new Vector2d(homogeneous[3 * i], homogeneous[3 * i + 1]) / homogeneous[3 * i + 2];
                    Vector2d r = new Vector2d(reference[3 * i], reference[3 * i + 1]);
                    r /= r.Length;
                    Vector2d expected = i == s ? Vector2d.Zero : strategy == LogMapStrategy.AffineAdaptive
                        ? p : new Vector2d(p.Dot(r), p.y * r.x - p.x * r.y);
                    Assert.True(actual[v].Distance(expected) < 2e-10, $"source {source}, vertex {v}: {actual[v]} vs {expected}");
                }
            }
        }

        static double[,] Assemble(IntrinsicTriangulation t, double time, double[] reference)
        {
            int n = t.VertexCount;
            var matrix = new double[3 * n, 3 * n];
            double[] mass = t.VertexAreas();
            for (int i = 0; i < n; ++i)
                for (int k = 0; k < 3; ++k) matrix[3 * i + k, 3 * i + k] = mass[i];
            for (int h = 0; h < t.HalfedgeCount; ++h) {
                if (t.Twin(h) >= 0 && t.Twin(h) < h) continue;
                int i = t.Tail(h), j = t.Head(h);
                Vector2d eij = t.Length(h) * new Vector2d(Math.Cos(t.Signpost(h)), Math.Sin(t.Signpost(h)));
                Vector2d eji = t.Length(h) * new Vector2d(Math.Cos(t.ReverseSignpost(h)), Math.Sin(t.ReverseSignpost(h)));
                double c = time * Math.Max(0, t.CotanWeight(h));
                AddDirected(matrix, i, j, eij, eji, c, reference);
                AddDirected(matrix, j, i, eji, eij, c, reference);
            }
            return matrix;
        }

        static void AddDirected(double[,] a, int i, int j, Vector2d e, Vector2d reverse, double c, double[] reference)
        {
            // Direct homogeneous 3x3 edge transport, independently assembled without split factors.
            double re = -e.Dot(reverse) / reverse.LengthSquared;
            double im = -(e.y * reverse.x - e.x * reverse.y) / reverse.LengthSquared;
            if (reference != null) {
                double angle = Math.Atan2(e.y, e.x) - Math.Atan2(reference[3 * i + 1], reference[3 * i]);
                e = e.Length * new Vector2d(Math.Cos(angle), Math.Sin(angle));
                re = 1;
                im = 0;
            }
            double[,] transport = { { re, -im, -e.x }, { im, re, -e.y }, { 0, 0, 1 } };
            for (int p = 0; p < 3; ++p) {
                a[3 * i + p, 3 * i + p] += c;
                for (int q = 0; q < 3; ++q) a[3 * i + p, 3 * j + q] -= c * transport[p, q];
            }
        }

        static double[] DenseSolve(double[,] matrix, double[] rhs)
        {
            var a = (double[,])matrix.Clone();
            var b = (double[])rhs.Clone();
            int n = b.Length;
            for (int k = 0; k < n; ++k) {
                int pivot = k;
                for (int i = k + 1; i < n; ++i) if (Math.Abs(a[i, k]) > Math.Abs(a[pivot, k])) pivot = i;
                Assert.True(Math.Abs(a[pivot, k]) > 0);
                for (int j = k; j < n; ++j) { double tmp = a[k, j]; a[k, j] = a[pivot, j]; a[pivot, j] = tmp; }
                double swap = b[k]; b[k] = b[pivot]; b[pivot] = swap;
                for (int i = k + 1; i < n; ++i) {
                    double f = a[i, k] / a[k, k];
                    for (int j = k; j < n; ++j) a[i, j] -= f * a[k, j];
                    b[i] -= f * b[k];
                }
            }
            for (int i = n - 1; i >= 0; --i) {
                for (int j = i + 1; j < n; ++j) b[i] -= a[i, j] * b[j];
                b[i] /= a[i, i];
            }
            return b;
        }

        [Theory]
        [InlineData(LogMapStrategy.AffineLocal)]
        [InlineData(LogMapStrategy.AffineAdaptive)]
        public void Affine_LegacyDispatchAndReuseAreIdentical(LogMapStrategy strategy)
        {
            DMesh3 mesh = IntrinsicGeodesicsTests.GoodSphere(1, 7);
            var method = new VectorHeatMethod(mesh);
            Vector2d[] legacy = method.ComputeLogMap(0);
            Vector2d[] a = method.ComputeLogMap(0, strategy);
            method.ComputeLogMap(10, strategy);
            Assert.Equal(a, method.ComputeLogMap(0, strategy));
            Assert.Equal(legacy, method.ComputeLogMap(0, LogMapStrategy.Legacy));
            Assert.Equal(a, new VectorHeatMethod(mesh).ComputeLogMap(0, strategy));
        }
    }
}
