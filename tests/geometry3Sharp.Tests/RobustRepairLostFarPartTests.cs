using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A clean closed part must not be lost with Apply() true when the parts lie far apart compared to their size (the owner's rule for
    /// far parts, #425). A unit box at the origin and a sphere of radius 8 at 1e6 .. 5e6: the cleanups relative to the diagonal of all
    /// triangles collapsed the box, the result (the sphere) was clean and its IoU with the input 0.9995, and Apply() was true.
    /// Public API only (RepairCampaignFindings has the cases and the reason strings).
    /// </summary>
    public class RobustRepairLostFarPartTests
    {
        static DMesh3 BoxAndFarSphere(double far)
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 8, EdgeVertices = 4 }.Generate().MakeDMesh();
            foreach (int v in s.VertexIndices()) s.SetVertex(v, new Vector3d(-far, 0, 0) + s.GetVertex(v));
            new MeshEditor(m).AppendMesh(s);
            return m;
        }

        [Theory]
        [InlineData(1e6)]
        [InlineData(2.2e6)]
        [InlineData(5e6)]
        public void FarSphere_TheBoxIsKept_OrApplyIsFalse(double far)
        {
            var r = new MeshRobustRepair(BoxAndFarSphere(far));
            bool ok = r.Apply();
            var comps = new MeshConnectedComponents(r.Mesh);
            comps.FindConnectedT();
            Assert.True(comps.Count == 2 || ok == false, $"{comps.Count} components with Apply() true");
        }
    }
}
