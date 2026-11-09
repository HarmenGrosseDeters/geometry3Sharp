using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshRobustRepair.NotCleanReason's degenerate test (area below 1e-12 times the squared diagonal) at extreme
    /// scales (found in the review of tools/MeshFuzzPilot). Below a diagonal of about 2^-517 the threshold underflowed to
    /// 0 and no triangle was degenerate, not even an exactly flat one; above about 1e154 the diagonal overflowed and
    /// every triangle was. Now the test runs on coordinates scaled by a power of two: 2^k scales give the same answer.
    /// </summary>
    public class NotCleanReasonScaleTests
    {
        static DMesh3 Scaled(DMesh3 m, int k)
        {
            double s = Math.Pow(2, k);
            var r = new DMesh3(m);
            foreach (int v in r.VertexIndices())
                r.SetVertex(v, s * r.GetVertex(v));
            return r;
        }

        static DMesh3 Sphere()
        {
            var g = new Sphere3Generator_NormalizedCube() { EdgeVertices = 5, Radius = 1 };
            g.Generate();
            return g.MakeDMesh();
        }

        // a closed tetrahedron with corner c on the edge ab: the face abc is exactly degenerate
        static DMesh3 FlatTetrahedron()
        {
            var m = new DMesh3();
            int a = m.AppendVertex(Vector3d.Zero), b = m.AppendVertex(new Vector3d(2, 0, 0));
            int c = m.AppendVertex(new Vector3d(1, 0, 0)), d = m.AppendVertex(new Vector3d(0, 1, 1));
            m.AppendTriangle(a, c, b);
            m.AppendTriangle(a, b, d);
            m.AppendTriangle(b, c, d);
            m.AppendTriangle(c, a, d);
            return m;
        }

        public static TheoryData<int> Exponents()
        {
            var d = new TheoryData<int>();
            foreach (int k in new[] { -900, -600, -530, -520, -400, -300, 0, 300, 400, 520, 600, 900 })
                d.Add(k);
            return d;
        }

        [Theory]
        [MemberData(nameof(Exponents))]
        public void FlatTriangle_DegenerateAtEveryScale(int k)
        {
            DMesh3 m = Scaled(FlatTetrahedron(), k);
            Assert.True(m.IsClosed());
            Assert.Equal("degenerate triangles", MeshRobustRepair.NotCleanReason(m, false));
            Assert.True((MeshRobustRepair.FindDefects(m, false) & MeshRobustRepair.Defects.DegenerateTriangles) != 0);
        }

        [Theory]
        [MemberData(nameof(Exponents))]
        public void Sphere_NoDegenerateTriangleAtEveryScale(int k)
        {
            DMesh3 m = Scaled(Sphere(), k);
            Assert.True((MeshRobustRepair.FindDefects(m, false) & MeshRobustRepair.Defects.DegenerateTriangles) == 0);
        }

        [Fact]
        public void NormalSizes_Unchanged()
        {
            // inside [2^-300, 2^300] the test is the plain one: a sliver just above / below the threshold at unit size
            var m = new DMesh3();
            int a = m.AppendVertex(Vector3d.Zero), b = m.AppendVertex(new Vector3d(1, 0, 0)), c = m.AppendVertex(new Vector3d(0.5, 4e-12, 0));
            m.AppendTriangle(a, b, c);
            double diag = m.CachedBounds.DiagonalLength, area = m.GetTriArea(0);
            Assert.Equal(area < 1e-12 * diag * diag, (MeshRobustRepair.FindDefects(m, false) & MeshRobustRepair.Defects.DegenerateTriangles) != 0);
        }
    }
}
