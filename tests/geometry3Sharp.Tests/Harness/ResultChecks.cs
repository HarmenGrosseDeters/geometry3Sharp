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

        public static double Volume(DMesh3 mesh) => MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
    }
}
