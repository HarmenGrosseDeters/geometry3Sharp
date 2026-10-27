using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshNormals (docs/ROBUSTNESS_SWEEP.md R1, R18, R20): unit normals at any scale (upstream normalized only if
    /// |sum of area * normal|^2 > 1e-6, so one-rings with an area below about 1e-3 kept tiny normals), the same normals
    /// in every run (the parallel sum under a lock followed the thread schedule), and no NaN normals around a bad vertex.
    /// </summary>
    public class MeshNormalsScaleTests
    {
        static DMesh3 Sphere(int n = 6) => Defects.Sphere(Vector3d.Zero, 1, n);

        static Vector3d[] Normals(DMesh3 m)
        {
            var n = new MeshNormals(m);
            n.Compute();
            return m.VertexIndices().Select(v => n.Normals[v]).ToArray();
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Normals_SameUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 m = Sphere();
            Vector3d[] a = Normals(m), b = Normals(Invariance.Transformed(m, s));
            // (rounding of the transformed coordinates, relative to the edge length of about 0.3)
            double tol = 1e-12 + 10 * s.Resolution(2) / 0.3;
            for (int i = 0; i < a.Length; ++i) {
                Vector3d back = s.Rotation.Transpose() * b[i];
                if (s.IsExact)
                    Assert.True(a[i] == b[i], $"{name}: vertex {i}: {a[i]} vs {b[i]} (not bit identical)");
                else
                    Assert.True(a[i].Distance(back) < tol, $"{name}: vertex {i}: {a[i]} vs {back}");
                Assert.True(Math.Abs(b[i].Length - 1) < 1e-12, $"{name}: vertex {i}: length {b[i].Length}");
            }
        }

        [Theory]
        [InlineData(-30)]
        [InlineData(-60)]
        [InlineData(40)]
        [InlineData(-500)]
        [InlineData(500)]
        public void Normals_ExtremeScales_UnitAndBitIdentical(int exponent)
        {
            double scale = Math.Pow(2, exponent);
            DMesh3 m = Sphere();
            DMesh3 x = new DMesh3(m);
            MeshTransforms.Scale(x, scale);
            Vector3d[] a = Normals(m), b = Normals(x);
            for (int i = 0; i < a.Length; ++i)
                Assert.True(a[i] == b[i], $"vertex {i}: {a[i]} vs {b[i]}");
            int vid = x.VertexIndices().First();
            Assert.True(MeshNormals.QuickCompute(x, vid) == MeshNormals.QuickCompute(m, vid));
            Assert.True(Math.Abs(MeshNormals.QuickCompute(x, vid).Length - 1) < 1e-12);
        }

        [Fact]
        public void Normals_RepeatedRunsAndReuse_BitIdentical()
        {
            DMesh3 m = Sphere(40);
            var reused = new MeshNormals(m);
            reused.Compute();
            Vector3d[] first = m.VertexIndices().Select(v => reused.Normals[v]).ToArray();
            for (int run = 0; run < 10; ++run) {
                Vector3d[] again = Normals(m);
                Assert.True(first.SequenceEqual(again), $"run {run} differs");
            }
            reused.Compute();
            Assert.True(first.SequenceEqual(m.VertexIndices().Select(v => reused.Normals[v])), "reused instance differs");
        }

        [Fact]
        public void Normals_EqualTheSerialSumInTriangleOrder()
        {
            // the sum is serial in triangle id order (the SpinLock sum followed the thread schedule), and QuickCompute of
            // one vertex gives the same bits
            DMesh3 m = Sphere(30);
            var sum = new Vector3d[m.MaxVertexID];
            foreach (int t in m.TriangleIndices()) {
                Index3i tri = m.GetTriangle(t);
                Vector3d a = m.GetVertex(tri.a), b = m.GetVertex(tri.b), c = m.GetVertex(tri.c);
                Vector3d n = 0.5 * (b - a).Cross(c - a);
                sum[tri.a] += n; sum[tri.b] += n; sum[tri.c] += n;
            }
            var normals = new MeshNormals(m);
            normals.Compute();
            foreach (int v in m.VertexIndices()) {
                Vector3d w = sum[v] / sum[v].MaxAbs;
                Vector3d expected = w / w.Length;
                Assert.True(normals.Normals[v] == expected, $"vertex {v}: {normals.Normals[v]} vs {expected}");
                Assert.True(MeshNormals.QuickCompute(m, v) == expected, $"QuickCompute, vertex {v}");
            }
        }

        [Fact]
        public void Normals_NonFiniteVertex_ZeroNotNaN()
        {
            DMesh3 m = Sphere();
            int bad = m.VertexIndices().Skip(10).First();
            m.SetVertex(bad, new Vector3d(double.NaN, 0, 0));
            var n = new MeshNormals(m);
            n.Compute();
            foreach (int v in m.VertexIndices())
                Assert.True(n.Normals[v].IsFinite, $"vertex {v}: {n.Normals[v]}");
            Assert.Equal(Vector3d.Zero, n.Normals[bad]);
            // a vertex away from the bad one keeps its unit normal
            int far = m.VertexIndices().First(v => m.VtxVerticesItr(v).Contains(bad) == false && v != bad);
            Assert.True(Math.Abs(n.Normals[far].Length - 1) < 1e-12);
        }

        [Fact]
        public void Normals_DegenerateOneRing_Zero()
        {
            // a vertex whose triangles have zero area keeps a zero normal
            var m = new DMesh3();
            m.AppendVertex(Vector3d.Zero); m.AppendVertex(Vector3d.AxisX); m.AppendVertex(2 * Vector3d.AxisX);
            m.AppendTriangle(0, 1, 2);
            var n = new MeshNormals(m);
            n.Compute();
            Assert.Equal(Vector3d.Zero, n.Normals[0]);
        }
    }
}
