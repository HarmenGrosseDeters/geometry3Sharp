using System;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> Tests of the check helper ResultChecks.AssertCleanExceptContacts itself (#1410): it must accept a contact and reject a crossing. </summary>
    public class ResultChecksTests
    {
        // a closed, outward oriented tetrahedron of its own vertices, appended to the mesh
        static void AddTetra(DMesh3 m, Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            if ((b - a).Cross(c - a).Dot(d - a) > 0) { Vector3d t = b; b = c; c = t; }
            int ia = m.AppendVertex(a), ib = m.AppendVertex(b), ic = m.AppendVertex(c), id = m.AppendVertex(d);
            m.AppendTriangle(ia, ib, ic); m.AppendTriangle(ia, id, ib); m.AppendTriangle(ib, id, ic); m.AppendTriangle(ic, id, ia);
        }

        [Fact]
        public void AssertCleanExceptContacts_AcceptsTwoSolidsTouchingAtADuplicatedPosition()
        {
            var m = new DMesh3();
            AddTetra(m, new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1));
            AddTetra(m, new Vector3d(0, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, -1));      // the origin again, with its own vertex id
            ResultChecks.AssertCleanExceptContacts(m, "touching");
        }

        [Fact]
        public void AssertCleanExceptContacts_RejectsATriangleThatCrossesBesideTheSharedPosition()
        {
            // T1 = (0,0,0),(1,0,0),(0,1,0) of the first solid; a triangle of the second has the origin as a corner of its own (another
            // vertex id) and the corners (0.2,0.2,1), (0.3,0.1,-1): its plane cuts T1 in a segment from the shared position, so the
            // two triangles share one position and cross as well
            var m = new DMesh3();
            AddTetra(m, new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, -1));
            AddTetra(m, new Vector3d(0, 0, 0), new Vector3d(0.2, 0.2, 1), new Vector3d(0.3, 0.1, -1), new Vector3d(0.6, 0.6, 0));
            var e = Assert.ThrowsAny<Exception>(() => ResultChecks.AssertCleanExceptContacts(m, "crossing"));
            Assert.Contains("beyond their shared vertex positions", e.Message);
        }

        [Fact]
        public void AssertCleanExceptContacts_RequiresAMeshWithoutRepeatedPositionsToBeFullyClean()
        {
            var m = new DMesh3();
            AddTetra(m, new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1));
            AddTetra(m, new Vector3d(0.1, 0.1, 0.1), new Vector3d(2, 0.1, 0.1), new Vector3d(0.1, 2, 0.1), new Vector3d(0.1, 0.1, 2));
            Assert.ThrowsAny<Exception>(() => ResultChecks.AssertCleanExceptContacts(m, "overlapping"));
        }
    }
}
