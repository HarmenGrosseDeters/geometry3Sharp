using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// What the property campaigns of MeshRobustRepair and MeshRoundingRepair (RepairCampaignTests, QA 2.0 T3) found,
    /// pinned with minimal procedural reproductions (each failing seed is a fixed example test, QA.md 2.4).
    ///
    /// F1 (open, skipped until decided): a clean closed part is lost with Apply() true where the parts lie far apart
    /// compared to their size, but not as far as the retry of ScaleDisparity covers. A unit box at the origin and a
    /// sphere of radius 8 at distance 2.2e6: by the diagonal of all triangles (2.2e6) the box's triangles are below the
    /// degenerate area of NotCleanReason (1e-12 * diagonal^2 = 4.8), MeshAutoRepair's result is "not clean", the exact
    /// resolve runs, its cleanups collapse the box, the result (the sphere) is clean, its volume IoU with the input is
    /// 0.9995 (the box is 5e-4 of the volume) and CheckLostComponents does not judge the box either (it skips parts
    /// below 1e-3 of the diagonal of all triangles, MeshRepairMetrics.MinComponentDiagonal; the box is 8e-7 of it).
    /// Closer (up to 3e5) the box is kept, further (1e7 and more) the retry relative to the largest component runs and
    /// Apply() is false with ScaleDisparity, as documented. The retry (ScaleRetry) is tried only when the exact
    /// resolve fails; here it succeeds.
    ///
    /// F2 (fixed, DMeshAABBTree3): the top-down build decided between "split at the midpoint" and "split half and half"
    /// by an absolute spread of 1e-8 (MathUtil.ZeroTolerance), so a mesh whose centroids lie within 1e-8 along an axis
    /// (a small mesh, or a thin plate) got another tree than the same mesh scaled by 2^k, and the winding number
    /// contour of the solidify fallback (adaptive, ray queries) differed in the last bits. The test is in
    /// DMeshAABBTreeScaleTests (in the bugfix PR of F2).
    ///
    /// F3 (open, not pursued): the solidify fallback is still not bit-identical under 2^k scaling for scenes whose
    /// pieces are smaller than about 2.4e-4 (absolute) or whose thin pieces are thinner than about 1e-9: further absolute
    /// tolerances in the spatial queries (DMeshAABBTree3 box_eps 6e-6 and the ray window ZeroTolerancef 1e-8 in
    /// FindNearestHitTriangle / FindAllHitTriangles). It is a case of the scale-relative tolerances concept
    /// (docs/ROADMAP.md); the campaign stays within pieces of at least 2.4e-4 (RepairCampaignTests.RobustShift).
    /// </summary>
    public class RepairCampaignFindings
    {
        static DMesh3 BoxAndFarSphere(double far)
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
            DMesh3 s = DegenerateFamilies.Sphere(4);
            foreach (int v in s.VertexIndices()) s.SetVertex(v, new Vector3d(-far, 0, 0) + 8 * s.GetVertex(v));
            new MeshEditor(m).AppendMesh(s);
            return m;
        }

        static int Components(DMesh3 m)
        {
            var c = new MeshConnectedComponents(m);
            c.FindConnectedT();
            return c.Count;
        }

        // the cases around F1 that already hold: kept near, reported (false, ScaleDisparity) far
        [Theory]
        [InlineData(1e3)]
        [InlineData(3e5)]
        [InlineData(1e7)]
        [InlineData(1e9)]
        public void FarSphere_BoxKept(double far)
        {
            var r = new MeshRobustRepair(BoxAndFarSphere(far));
            bool ok = r.Apply();
            Assert.Equal(2, Components(r.Mesh));
            if (ok == false)
                Assert.StartsWith("scale disparity", r.ResultReason);
        }

        [Theory(Skip = "F1: the box is lost with Apply() true (see the class comment); decision pending")]
        [InlineData(1e6)]
        [InlineData(2.2e6)]
        public void FarSphere_BoxKept_InTheGap(double far)
        {
            var r = new MeshRobustRepair(BoxAndFarSphere(far));
            bool ok = r.Apply();
            Assert.True(Components(r.Mesh) == 2, $"{Components(r.Mesh)} components, Apply() {ok}, '{r.ResultReason}'");
        }
    }
}
