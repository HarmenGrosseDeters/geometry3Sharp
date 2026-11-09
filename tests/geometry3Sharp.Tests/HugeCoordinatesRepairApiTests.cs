using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The new API of the huge-coordinates fix (MeshRobustRepair.ScaleNotFinite, MeshRepairMetrics.normalizing_scale),
    /// apart from HugeCoordinatesRepairTests so that those compile against the library before the fix
    /// </summary>
    public class HugeCoordinatesRepairApiTests
    {
        static DMesh3 SphereWithFarSliver(Vector3d far)
        {
            var g = new Sphere3Generator_NormalizedCube() { EdgeVertices = 5, Radius = 1 };
            g.Generate();
            DMesh3 m = g.MakeDMesh();
            int a = m.AppendVertex(far), b = m.AppendVertex(far + new Vector3d(1e290, 0, 0));
            int c = m.AppendVertex(new Vector3d(-far.x, 0.999999 * far.y, -far.z));
            m.AppendTriangle(a, b, c);
            return m;
        }

        static DMesh3 UnitSphere(double scale)
        {
            var g = new Sphere3Generator_NormalizedCube() { EdgeVertices = 5, Radius = 1 };
            g.Generate();
            DMesh3 m = g.MakeDMesh();
            foreach (int v in m.VertexIndices())
                m.SetVertex(v, scale * m.GetVertex(v));
            return m;
        }

        [Theory]
        [InlineData(6.186542301424944E+307, 4.2432535287513695E+306, -1.2976644950812663E+308)]
        [InlineData(1e308, 1e307, -1.3e308)]
        [InlineData(-4.3229265976709274E+307, -1.6217469705340982E+307, -7.131133845344071E+307)]
        public void RobustRepair_DiagonalOverflows_FailsWithTheReason_NoThrow(double x, double y, double z)
        {
            // before: an ArgumentException from the exact predicates; the diagonal (about 2e308) is not finite, so every
            // threshold relative to it would be infinite (a run without the throw collapsed the sphere to 4 triangles)
            DMesh3 m = SphereWithFarSliver(new Vector3d(x, y, z));
            int before = m.TriangleCount;
            var r = new MeshRobustRepair(m);
            Assert.False(r.Apply());
            Assert.Equal(MeshRobustRepair.ScaleNotFinite, r.ResultReason);
            Assert.Same(m, r.Mesh);
            Assert.Equal(before, m.TriangleCount);
        }


        [Fact]
        public void VolumeCheck_NormalSizes_NotScaled()
        {
            // inside [2^-300, 2^300] the computation is unchanged (no scale)
            Assert.Equal(1.0, MeshRepairMetrics.normalizing_scale(UnitSphere(1), null));
            Assert.Equal(1.0, MeshRepairMetrics.normalizing_scale(UnitSphere(Math.Pow(2, 298)), UnitSphere(1)));
            Assert.Equal(1.0, MeshRepairMetrics.normalizing_scale(UnitSphere(Math.Pow(2, -298)), null));
            // (the sphere's extent is 2 times the radius: 2^401 is scaled into [2^299, 2^300), 2^-399 into [2^-300, 2^-299))
            Assert.Equal(Math.Pow(2, -102), MeshRepairMetrics.normalizing_scale(UnitSphere(Math.Pow(2, 400)), null));
            Assert.Equal(Math.Pow(2, 99), MeshRepairMetrics.normalizing_scale(UnitSphere(Math.Pow(2, -400)), null));
        }
    }
}
