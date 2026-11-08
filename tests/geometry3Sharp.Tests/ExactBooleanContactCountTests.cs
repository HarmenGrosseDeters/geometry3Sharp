using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshBoolean.Contacts counts the positions where the result has two or more vertices: where the exact solid
    /// touches itself (kept as duplicated vertices of one exact point, manifold in memory, non-manifold when welded by
    /// position); MeshRoundingRepair.InitialFalseContacts / RemainingFalseContacts count the positions where the
    /// rounding put vertices of different exact points, which the repair separates (see ExactBooleanRoundingContactsTests).
    /// </summary>
    public class ExactBooleanContactCountTests
    {
        static DMesh3 Box(Vector3d min)
        {
            return new TrivialBox3Generator() { Box = new Box3d(min + 0.5 * Vector3d.One, 0.5 * Vector3d.One), NoSharedVertices = false }.Generate().MakeDMesh();
        }

        [Fact]
        public void SkewEdges_FalseContactSeparated_NoContactLeft()
        {
            var b = new ExactMeshBoolean(ExactBooleanRoundingContactsTests.A(), ExactBooleanRoundingContactsTests.SkewB(), Ops.Difference);
            Assert.True(b.Compute(), b.Error);
            Assert.True(b.Rounding.InitialFalseContacts > 0, "the case has no contact made by the rounding");
            Assert.Equal(0, b.Rounding.RemainingFalseContacts);
            Assert.Equal(0, b.Contacts);
            Assert.True(b.RoundingClean);
            Assert.True(b.Rounding.MaxDrift <= b.Rounding.MaxLength);
        }

        [Fact]
        public void SkewEdges_WithoutSafeRounding_ContactCounted()
        {
            // without the repair the two vertices stay at one position: a contact
            var b = new ExactMeshBoolean(ExactBooleanRoundingContactsTests.A(), ExactBooleanRoundingContactsTests.SkewB(), Ops.Difference) { SafeRounding = false };
            Assert.True(b.Compute(), b.Error);
            Assert.True(b.Contacts > 0);
            Assert.False(b.RoundingClean);
        }

        [Theory]
        [InlineData(1, 1, 1, 1)]   // touching at one corner: one position with two vertices
        [InlineData(1, 1, 0, 2)]   // touching along an edge: its two ends (no midpoint: the boxes meet only along the edge)
        [InlineData(-1, -1, -1, 1)] // the corner at the origin, where -0.0 and 0.0 are one position
        public void TouchingBoxes_Union_ContactsOfTheExactSolid(double dx, double dy, double dz, int contacts)
        {
            var b = new ExactMeshBoolean(Box(Vector3d.Zero), Box(new Vector3d(dx, dy, dz)), Ops.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.True(b.Contacts == contacts, $"contacts {b.Contacts}");
            Assert.Equal(0, b.Rounding.InitialFalseContacts);
            Assert.True(b.RoundingClean);
            Assert.True(b.Result.IsClosed());
            // in memory manifold (welded by position it would not be)
            Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result, checkSelfIntersections: false));
        }

        [Fact]
        public void Contacts_ResetWhenTheNextComputeFails()
        {
            var b = new ExactMeshBoolean(Box(Vector3d.Zero), Box(Vector3d.One), Ops.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(1, b.Contacts);
            b.MeshB = new DMesh3();
            b.MeshB.AppendVertex(new Vector3d(double.NaN, 0, 0));
            b.MeshB.AppendVertex(Vector3d.AxisX);
            b.MeshB.AppendVertex(Vector3d.AxisY);
            b.MeshB.AppendTriangle(0, 1, 2);
            Assert.False(b.Compute());
            Assert.Equal(0, b.Contacts);
        }

        // a closed tetrahedron with its apex at p, its other corners at p + d0, d1, d2 (outward oriented)
        static void AddTetrahedron(DMesh3 m, Vector3d p, Vector3d d0, Vector3d d1, Vector3d d2)
        {
            if (d0.Dot(d1.Cross(d2)) > 0) { var t = d1; d1 = d2; d2 = t; }
            int a = m.AppendVertex(p), b = m.AppendVertex(p + d0), c = m.AppendVertex(p + d1), d = m.AppendVertex(p + d2);
            m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); m.AppendTriangle(a, d, b); m.AppendTriangle(b, d, c);
        }

        // tetrahedra with their apexes at one position in separate octants; the apexes of the first two share an exact
        // point (the solid touches itself there), the others are a point each, or (twoEach) the last two share one
        static (DMesh3, Func<int, int>, int[]) Cones(int count, bool twoEach)
        {
            var m = new DMesh3();
            Vector3d p = new Vector3d(0.25, 0.5, 0.75);
            var octants = new[] { new Vector3d(1, 1, 1), new Vector3d(-1, -1, 1), new Vector3d(1, -1, -1), new Vector3d(-1, 1, -1) };
            var apex = new int[count];
            for (int i = 0; i < count; ++i) {
                Vector3d o = octants[i];
                apex[i] = m.VertexCount;
                AddTetrahedron(m, p, new Vector3d(o.x, 0.2 * o.y, 0.3 * o.z), new Vector3d(0.2 * o.x, o.y, 0.3 * o.z), new Vector3d(0.3 * o.x, 0.2 * o.y, o.z));
            }
            var ids = new Dictionary<int, int>();
            for (int i = 0; i < count; ++i)
                ids[apex[i]] = twoEach ? (i < 2 ? 1000 : 2000) : (i < 2 ? 1000 : 2000 + i);
            Func<int, int> pointId = (v) => ids.TryGetValue(v, out int id) ? id : v;
            return (m, pointId, apex);
        }

        [Fact]
        public void ContactOfOneExactPoint_KeptWhileTheOtherPointIsSeparated()
        {
            (DMesh3 m, Func<int, int> pointId, int[] apex) = Cones(3, twoEach: false);
            Vector3d p = m.GetVertex(apex[0]);
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, PointIdF = pointId };
            Assert.True(r.Apply());
            Assert.Equal(1, r.InitialFalseContacts);
            Assert.Equal(0, r.RemainingFalseContacts);
            // the two vertices of one exact point still share the position, the third left it
            Assert.Equal(p, m.GetVertex(apex[0]));
            Assert.Equal(p, m.GetVertex(apex[1]));
            Assert.NotEqual(p, m.GetVertex(apex[2]));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m, checkSelfIntersections: false));
        }

        [Fact]
        public void TwoContactsOfOneExactPointEach_NotBrokenUp_Counted()
        {
            // two points with two vertices each at one position: moving one vertex would break up a contact of the
            // solid, so none moves and the position is left, counted
            (DMesh3 m, Func<int, int> pointId, int[] apex) = Cones(4, twoEach: true);
            Vector3d p = m.GetVertex(apex[0]);
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, PointIdF = pointId };
            Assert.False(r.Apply());
            Assert.Equal(1, r.InitialFalseContacts);
            Assert.Equal(1, r.RemainingFalseContacts);
            foreach (int v in apex)
                Assert.Equal(p, m.GetVertex(v));
        }

        [Fact]
        public void SeparateBoxes_NoContacts()
        {
            var b = new ExactMeshBoolean(Box(Vector3d.Zero), Box(new Vector3d(0.5, 0.25, 0.125)), Ops.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(0, b.Contacts);
            Assert.Equal(0, b.Rounding.InitialFalseContacts);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result));
        }
    }
}
