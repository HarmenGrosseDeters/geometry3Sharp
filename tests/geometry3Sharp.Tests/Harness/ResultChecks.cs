using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Checks of a result itself, for tests of a success path (docs/QA.md 2.3): a status or a count says what the
    /// operation claims, these check whether it holds.
    /// </summary>
    public static class ResultChecks
    {
        /// <summary> a valid DMesh3 (consistent data structure and orientation, no bowties) with finite coordinates </summary>
        public static void AssertValid(DMesh3 mesh, string what = "result")
        {
            Assert.True(mesh != null, what + ": null");
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly), what + ": invalid DMesh3 (or bowtie vertices)");
            Assert.True(mesh.Vertices().All(v => v.IsFinite), what + ": non-finite coordinates");
        }

        /// <summary> valid and closed </summary>
        public static void AssertClosed(DMesh3 mesh, string what = "result")
        {
            AssertValid(mesh, what);
            Assert.True(mesh.IsClosed(), what + ": not closed");
        }

        /// <summary> clean in the sense of MeshRobustRepair.NotCleanReason, and valid </summary>
        public static void AssertClean(DMesh3 mesh, string what = "result", bool checkSelfIntersections = true)
        {
            AssertValid(mesh, what);
            // (all reasons, not only the first: a failure message that names every defect)
            string reason = MeshRobustRepair.NotCleanReasons(mesh, checkSelfIntersections);
            Assert.True(reason == "", what + ": " + reason);
        }

        /// <summary>
        /// Clean in the sense of what the library promises for a result whose exact solid touches itself (#1410): valid, and
        /// NotCleanReason without the self-intersection check is empty; every pair of triangles that MeshRepairMetrics reports as
        /// intersecting has a vertex position in common (a contact: duplicated vertices of one exact point, which the rounding
        /// leaves apart in memory and which the pair test counts), so no two triangles cross
        /// </summary>
        public static void AssertCleanExceptContacts(DMesh3 mesh, string what = "result")
        {
            AssertValid(mesh, what);
            // (without two vertices at one position the result is clean in the full sense)
            if (mesh.VertexIndices().GroupBy(v => mesh.GetVertex(v)).All(g => g.Count() == 1)) {
                AssertClean(mesh, what);
                return;
            }
            string reason = MeshRobustRepair.NotCleanReasons(mesh, false);
            Assert.True(reason == "", what + ": " + reason);
            foreach (Index2i pair in MeshRepairMetrics.SelfIntersectionPairs(mesh)) {
                Index3i s = mesh.GetTriangle(pair.a), t = mesh.GetTriangle(pair.b);
                Vector3d[] ps = { mesh.GetVertex(s.a), mesh.GetVertex(s.b), mesh.GetVertex(s.c) }, pt = { mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c) };
                Assert.True(ps.Any(p => pt.Contains(p)), $"{what}: triangles {pair.a} and {pair.b} cross without a common vertex position");
            }
        }

        public static double Volume(DMesh3 mesh) => MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
    }
}
