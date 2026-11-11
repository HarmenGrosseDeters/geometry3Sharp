using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer.unit_direction / exponent_of in the ranges where the cross product of a triangle is rounding noise (a sliver: collinear
    /// up to rounding) at a normal scale and below 1e-150 (scaled by a power of two), and for a subnormal cross product. A sliver has no
    /// plane: its vertices get no quadric from it. The quadrics are read through a subclass (InitializeVertexQuadrics, vertQuadrics),
    /// and a reduction of a mesh with a sliver must still reach its target.
    /// </summary>
    public class ReducerCoverageTests
    {
        sealed class QuadricProbe : Reducer
        {
            public QuadricProbe(DMesh3 m) : base(m) { }
            public QuadricError[] Quadrics()
            {
                InitializeVertexQuadrics();
                return vertQuadrics;
            }
        }

        static bool IsZero(QuadricError q)
        {
            return q.Axx == 0 && q.Axy == 0 && q.Axz == 0 && q.Ayy == 0 && q.Ayz == 0 && q.Azz == 0 && q.bx == 0 && q.by == 0 && q.bz == 0 && q.c == 0;
        }

        // two triangles: a proper one (v0, v1, v2) and a sliver (v1, m, v0) glued to its edge (v0, v1), m lifted out of the line by `lift`
        static DMesh3 TriangleWithSliver(double scale, double lift, out int vm)
        {
            var mesh = new DMesh3(false, false, false, false);
            int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
            int v1 = mesh.AppendVertex(new Vector3d(scale, 0, 0));
            int v2 = mesh.AppendVertex(new Vector3d(0.5 * scale, scale, 0));
            vm = mesh.AppendVertex(new Vector3d(0.25 * scale, 0, scale * lift));
            Assert.True(mesh.AppendTriangle(v0, v1, v2) >= 0);
            Assert.True(mesh.AppendTriangle(v1, vm, v0) >= 0);
            return mesh;
        }

        // lift 1e-17: the cross product of the sliver is about 5e-18 * scale^2, below 2.2e-16 times the squared longest edge, so it is
        // noise (a zero normal, a zero area); scale 1: a length in [1e-150, 1e150] (the branch with the threshold); 2^-333: below 1e-150
        // (the branch that scales by a power of two first)
        [Theory]
        [InlineData(0)]
        [InlineData(-333)]
        public void Sliver_HasNoPlane_AndTheProperTriangleKeepsItsOwn(int shift)
        {
            double s = Math.Pow(2, shift);
            DMesh3 mesh = TriangleWithSliver(s, 1e-17, out int vm);
            QuadricError[] q = new QuadricProbe(mesh) { AreaWeightedQuadrics = false, PreserveBoundaryShape = false }.Quadrics();
            // m belongs to the sliver only
            Assert.True(IsZero(q[vm]), "the sliver's vertex has a quadric: Axx=" + q[vm].Axx + " Ayy=" + q[vm].Ayy + " Azz=" + q[vm].Azz);
            // the vertices of the proper triangle (normal (0,0,1) at any scale) carry exactly its plane: Azz = 1, the other terms 0
            foreach (int v in new[] { 0, 1, 2 }) {
                Assert.Equal(1.0, q[v].Azz);
                Assert.Equal(0.0, q[v].Axx);
                Assert.Equal(0.0, q[v].Ayy);
                Assert.Equal(0.0, q[v].Axy);
                Assert.Equal(0.0, q[v].Axz);
                Assert.Equal(0.0, q[v].Ayz);
            }
        }

        // the proper triangle is scaled by 2^k exactly: its quadric is the same bit for bit at both scales, the sliver's is zero at both
        [Fact]
        public void Sliver_QuadricsAreTheSame_AtEveryScale()
        {
            QuadricError[] a = new QuadricProbe(TriangleWithSliver(1, 1e-17, out int vm)) { AreaWeightedQuadrics = false }.Quadrics();
            QuadricError[] b = new QuadricProbe(TriangleWithSliver(Math.Pow(2, -333), 1e-17, out vm)) { AreaWeightedQuadrics = false }.Quadrics();
            for (int v = 0; v < 4; ++v) {
                Assert.Equal(a[v].Axx, b[v].Axx);
                Assert.Equal(a[v].Azz, b[v].Azz);
                Assert.Equal(a[v].Ayz, b[v].Ayz);
            }
        }

        // end to end: the reduction of a mesh with a sliver completes and stays valid and inside the grid's extent
        [Fact]
        public void ReducingAMeshWithASliver_ReachesTheTarget()
        {
            double s = 1;
            int n = 7;
            var mesh = new DMesh3(false, false, false, false);
            int[,] vid = new int[n, n];
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i)
                    vid[i, j] = mesh.AppendVertex(new Vector3d(s * i / (n - 1.0), s * j / (n - 1.0), s * 0.4 * Math.Sin(4.0 * j / (n - 1.0)) * (1 + i / (n - 1.0))));
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            int m = mesh.AppendVertex(new Vector3d(s * 0.5 / (n - 1.0), 0, s * 1e-17));
            Assert.True(mesh.AppendTriangle(vid[1, 0], m, vid[0, 0]) >= 0);
            var r = new Reducer(mesh);
            r.ReduceToTriangleCount(30);
            Assert.True(mesh.TriangleCount <= 30 && mesh.TriangleCount >= 20, "triangles: " + mesh.TriangleCount);
            Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                Assert.True(!double.IsNaN(p.x + p.y + p.z) && !double.IsInfinity(p.x + p.y + p.z));
                Assert.True(p.x >= -1e-9 * s && p.x <= 1.000000001 * s && p.y >= -1e-9 * s && p.y <= 1.000000001 * s);
            }
        }

        // exponent_of, for a subnormal number (the product with 2^64 is exact and normal)
        [Fact]
        public void ExponentOf_Subnormals_AndNormals()
        {
            Assert.Equal(-1074, Reducer.exponent_of(BitConverter.Int64BitsToDouble(1)));                       // 2^-1074, the smallest
            Assert.Equal(-1074 + 1, Reducer.exponent_of(BitConverter.Int64BitsToDouble(2)));
            Assert.Equal(-1074 + 1, Reducer.exponent_of(BitConverter.Int64BitsToDouble(3)));                   // 1.5 * 2^-1073
            Assert.Equal(-1023, Reducer.exponent_of(BitConverter.Int64BitsToDouble(1L << 51)));                // 2^-1023
            Assert.Equal(-1023, Reducer.exponent_of(BitConverter.Int64BitsToDouble((1L << 52) - 1)));          // the largest subnormal
            Assert.Equal(-1022, Reducer.exponent_of(BitConverter.Int64BitsToDouble(1L << 52)));                // the smallest normal
            Assert.Equal(0, Reducer.exponent_of(1.0));
            Assert.Equal(1023, Reducer.exponent_of(double.MaxValue));
            for (int e = -1074; e <= -1023; ++e)
                Assert.Equal(e, Reducer.exponent_of(BitConverter.Int64BitsToDouble(1L << (e + 1074))));   // (2^e as a subnormal, exactly)
        }

        // a triangle whose cross product is subnormal (legs 2^-500 and 2^-523: 2^-1023): the reduction path calls exponent_of for it; its
        // quadric is empty (the unit vector would have to be scaled by 2^-1023, which is not a normal double) and nothing is NaN
        [Fact]
        public void TriangleWithASubnormalCrossProduct_HasAFiniteQuadric()
        {
            var mesh = new DMesh3(false, false, false, false);
            mesh.AppendVertex(new Vector3d(0, 0, 0));
            mesh.AppendVertex(new Vector3d(Math.Pow(2, -500), 0, 0));
            mesh.AppendVertex(new Vector3d(0, Math.Pow(2, -523), 0));
            mesh.AppendTriangle(0, 1, 2);
            QuadricError[] q = new QuadricProbe(mesh) { PreserveBoundaryShape = false }.Quadrics();   // (the boundary quadrics are a separate path)
            for (int v = 0; v < 3; ++v) {
                Assert.False(double.IsNaN(q[v].Axx + q[v].Azz + q[v].bx + q[v].c));
                Assert.True(IsZero(q[v]), q[v].Axx + " " + q[v].Ayy + " " + q[v].Azz + " " + q[v].bx + " " + q[v].by+ " " + q[v].bz + " " + q[v].c);
            }
        }
    }
}
