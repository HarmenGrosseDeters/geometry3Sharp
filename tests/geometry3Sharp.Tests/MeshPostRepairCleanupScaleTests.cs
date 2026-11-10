using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshPostRepairCleanup.ReferenceDiagonal: the degenerate area relative to a given diagonal instead of that of the
    /// bounds of all triangles. MeshRobustRepair passes the diagonal of the largest connected component, so that a small
    /// part far from the rest (a unit tetrahedron 1e8 away from a unit sphere) does not make every sphere triangle
    /// degenerate and collapse the sphere. The default (NaN) is unchanged.
    /// </summary>
    public class MeshPostRepairCleanupScaleTests
    {
        static DMesh3 SphereAndFarTetrahedron(double far)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            var p = new Vector3d(far, far, far);
            int v0 = m.AppendVertex(p), v1 = m.AppendVertex(p + Vector3d.AxisX), v2 = m.AppendVertex(p + Vector3d.AxisY), v3 = m.AppendVertex(p + Vector3d.AxisZ);
            m.AppendTriangle(v0, v2, v1); m.AppendTriangle(v0, v1, v3); m.AppendTriangle(v0, v3, v2); m.AppendTriangle(v1, v2, v3);
            return m;
        }

        [Fact]
        public void FarPart_WithTheLargestComponentsDiagonal_SphereKept()
        {
            DMesh3 start = SphereAndFarTetrahedron(1e8);
            // the default: every sphere triangle is below 1e-12 diag^2 of the bounds of all triangles (1.7e8)
            DMesh3 plain = new DMesh3(start);
            var c0 = new MeshPostRepairCleanup(plain);
            c0.Apply();
            Assert.True(c0.FixedDegenerate > 0);
            Assert.True(plain.TriangleCount < start.TriangleCount);
            // relative to the sphere's diagonal nothing is degenerate: nothing changes
            DMesh3 scaled = new DMesh3(start);
            var c1 = new MeshPostRepairCleanup(scaled) { ReferenceDiagonal = MeshRepairMetrics.LargestComponentDiagonal(start) };
            c1.Apply();
            Assert.Equal(0, c1.FixedDegenerate);
            Assert.Equal("", Invariance.Difference(start, scaled));
        }

        [Fact]
        public void ReferenceDiagonal_NotFiniteOrZero_TheDefault()
        {
            DMesh3 start = SphereAndFarTetrahedron(1e8);
            DMesh3 plain = new DMesh3(start);
            new MeshPostRepairCleanup(plain).Apply();
            foreach (double d in new[] { double.NaN, 0, -1, double.PositiveInfinity }) {
                DMesh3 m = new DMesh3(start);
                new MeshPostRepairCleanup(m) { ReferenceDiagonal = d }.Apply();
                Assert.Equal("", Invariance.Difference(plain, m));
            }
        }

        [Fact]
        public void LargestComponentDiagonal_OneComponent_TheTriangleBounds()
        {
            // one component: the same diagonal as the bounds of the triangles, so the cleanups of MeshRobustRepair are
            // unchanged there; two far apart: the sphere's
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            Assert.Equal(MeshRepairMetrics.TriangleBounds(sphere).DiagonalLength, MeshRepairMetrics.LargestComponentDiagonal(sphere));
            DMesh3 two = SphereAndFarTetrahedron(1e8);
            Assert.Equal(MeshRepairMetrics.TriangleBounds(sphere).DiagonalLength, MeshRepairMetrics.LargestComponentDiagonal(two));
            Assert.Equal(0, MeshRepairMetrics.LargestComponentDiagonal(new DMesh3()));
            // (a component whose extent overflows: NaN, whatever the order)
            DMesh3 huge = SphereAndFarTetrahedron(1e8);
            int a = huge.AppendVertex(new Vector3d(-1e308, 0, 0)), b = huge.AppendVertex(new Vector3d(1e308, 0, 0)), c = huge.AppendVertex(new Vector3d(0, 1, 0));
            huge.AppendTriangle(a, b, c);
            Assert.True(double.IsNaN(MeshRepairMetrics.LargestComponentDiagonal(huge)));
        }
    }
}
