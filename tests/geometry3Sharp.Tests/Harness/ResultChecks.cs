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
        /// intersecting meets only at vertex positions the two share (a contact: duplicated vertices of one exact point, which the rounding
        /// leaves apart in memory and which the pair test counts): with the ids welded by exact position, ExactTriangleIntersection.IntersectsBeyondShared is false
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
            // weld the vertex ids by exact position: two triangles then meet only where they share a position, or the pair is a crossing
            var canon = new System.Collections.Generic.Dictionary<Vector3d, int>();
            int Id(int v) { Vector3d p = mesh.GetVertex(v); if (!canon.TryGetValue(p, out int id)) canon[p] = id = canon.Count; return id; }
            foreach (Index2i pair in MeshRepairMetrics.SelfIntersectionPairs(mesh)) {
                Index3i s = mesh.GetTriangle(pair.a), t = mesh.GetTriangle(pair.b);
                Index3i cs = new Index3i(Id(s.a), Id(s.b), Id(s.c)), ct = new Index3i(Id(t.a), Id(t.b), Id(t.c));
                Triangle3d A = new Triangle3d(mesh.GetVertex(s.a), mesh.GetVertex(s.b), mesh.GetVertex(s.c)), B = new Triangle3d(mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c));
                Assert.False(ExactTriangleIntersection.IntersectsBeyondShared(cs, ref A, ct, ref B), $"{what}: triangles {pair.a} and {pair.b} meet beyond their shared vertex positions");
            }
        }

        /// <summary>
        /// every position that holds more than one vertex of a result computed with ComputeExactVertices holds vertices of ONE exact point: the
        /// contact is a property of the exact solid (it touches itself there: the rounding does not make it, and no rounding could take it away
        /// without changing the solid), not a contact the rounding made of different exact points (#1489). Returns the number of such positions
        /// </summary>
        public static int AssertContactsAreOfTheExactSolid(ExactMeshBoolean boolean, string what = "result")
        {
            Assert.True(boolean.ComputeExactVertices, what + ": needs ComputeExactVertices");
            DMesh3 mesh = boolean.Result;
            ExactPoint3[] exact = boolean.ResultExactVertices;
            int positions = 0;
            foreach (var group in mesh.VertexIndices().GroupBy(v => mesh.GetVertex(v)).Where(g => g.Count() > 1)) {
                positions++;
                int first = group.First();
                foreach (int v in group)
                    Assert.True(v < exact.Length && first < exact.Length && exact[v].Equals(exact[first]), $"{what}: vertices {first} and {v} share a position but are different exact points");
            }
            return positions;
        }

        public static double Volume(DMesh3 mesh) => MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
    }
}
