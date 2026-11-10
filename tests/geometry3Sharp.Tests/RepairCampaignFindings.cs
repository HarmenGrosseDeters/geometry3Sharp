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
    /// F1 (fixed: the exact resolve of a result that lost a part is retried relative to the largest component, and a part still lost
    /// makes Apply() false with LostFarPart): a clean closed part is lost with Apply() true where the parts lie far apart
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
    /// F1c (open, noted): with parts far apart, the cleanups relative to the diagonal of all triangles can erode a part without
    /// losing it, with Apply() true: a tetrahedron-sized part at 2e7 from a sphere of radius 4096 comes back with 85 % of its
    /// volume, a sphere of radius 1.5e-3 next to a box at 1e5 with 70 % (the IoU of the whole stays above 0.99, the part
    /// is not judged). The loss check of F1 sees only a part with less than half of its samples left.
    ///
    /// F1b (open, noted): with Apply() false and ScaleDisparity the returned mesh can hold a part at a fraction of its
    /// volume (a sphere of radius 2 next to a tetrahedron of size 2^8 at 2^22: volume 29.7 in the input, 1.1 in the mesh).
    /// Apply() false says the repair failed, so the campaign does not judge the parts there.
    ///
    /// F2 (own bugfix PR, DMeshAABBTree3; still open on a base without it): the top-down build decided between "split at the midpoint" and "split half and half"
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

        // F1, fixed. With the exact resolve the first attempt (from the repair stage, whose cleanups collapse the box) is rejected
        // for the lost part, and the retry relative to the largest component starts from the input: the box is kept, and the
        // result is reported as not repaired (ScaleDisparity), as for the farther cases
        [Theory]
        [InlineData(1e6)]
        [InlineData(2.2e6)]
        [InlineData(5e6)]
        public void FarSphere_BoxKept_InTheGap(double far)
        {
            var r = new MeshRobustRepair(BoxAndFarSphere(far));
            bool ok = r.Apply();
            Assert.False(ok);
            Assert.StartsWith("scale disparity: degenerate triangles", r.ResultReason);
            Assert.Equal(2, Components(r.Mesh));
        }


        // the final check, reached with the early ones bypassed: the exact resolve of the repair result has collapsed the box and
        // is accepted, Apply() is false with the reason (the mesh stays in Mesh)
        [Fact]
        public void FarSphere_LostBox_FinalCheck_IsNotReportedAsRepaired()
        {
            var r = new MeshRobustRepair(BoxAndFarSphere(2.2e6)) { BypassEarlyLostPartChecks = true };
            bool ok = r.Apply();
            Assert.True(r.UsedExactResolve);
            Assert.False(ok);
            Assert.Equal(MeshRobustRepair.LostFarPart, r.ResultReason);
            Assert.Equal(1, Components(r.Mesh));
        }

        // the three scales of the far part scene over Similarity.All(): the parts stay (two components), the 2^k scales give the same
        // result bit for bit
        [Fact]
        public void FarSphere_Similarities_PartsKept_Pow2BitIdentical()
        {
            DMesh3 scene = BoxAndFarSphere(2.2e6);
            var r0 = new MeshRobustRepair(new DMesh3(scene));
            bool ok0 = r0.Apply();
            Assert.Equal(2, Components(r0.Mesh));
            foreach (Similarity sim in Similarity.All()) {
                var r = new MeshRobustRepair(Invariance.Transformed(scene, sim));
                bool ok = r.Apply();
                Assert.True(Components(r.Mesh) == 2, sim.ToString());
                Assert.True(ok || r.ResultReason.StartsWith("scale disparity"), sim.ToString());
                if (sim.IsExact) {
                    Assert.Equal(ok0, ok);
                    Assert.Equal(r0.ResultReason, r.ResultReason);
                    Assert.Equal("", Invariance.Difference(r0.Mesh, Invariance.MappedBack(r.Mesh, sim)));
                }
            }
        }

        // the far part handling (relative tolerances and cleanups) is for a few closed parts: not for a far scene with a hole, not for
        // many pieces (a cracked surface or a soup has pieces, not parts, and must be welded at its own scale as before)
        [Fact]
        public void FarPartHandling_OnlyForFewClosedParts()
        {
            var far = new MeshRobustRepair(BoxAndFarSphere(2.2e6));
            far.Apply();
            Assert.True(far.FarPartsHandled);

            DMesh3 open = BoxAndFarSphere(2.2e6);
            open.RemoveTriangle(open.TriangleIndices().First(), true, false);
            var withHole = new MeshRobustRepair(open);
            withHole.Apply();
            Assert.False(withHole.FarPartsHandled);

            DMesh3 many = BoxAndFarSphere(2.2e6);
            var edit = new MeshEditor(many);
            for (int k = 0; k < 70; ++k) {
                DMesh3 b = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(10 * k, 20, 0), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
                edit.AppendMesh(b);
            }
            var pieces = new MeshRobustRepair(many);
            pieces.Apply();
            Assert.False(pieces.FarPartsHandled);
        }
    }
}
