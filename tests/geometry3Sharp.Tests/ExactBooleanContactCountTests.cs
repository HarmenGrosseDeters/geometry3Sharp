using System;
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
        [InlineData(1, 1, 0, 2)]   // touching along an edge: its two ends (the edge is split into manifold pairs)
        public void TouchingBoxes_Union_ContactsOfTheExactSolid(double dx, double dy, double dz, int atLeast)
        {
            var b = new ExactMeshBoolean(Box(Vector3d.Zero), Box(new Vector3d(dx, dy, dz)), Ops.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.True(b.Contacts >= atLeast, $"contacts {b.Contacts}");
            Assert.Equal(0, b.Rounding.InitialFalseContacts);
            Assert.True(b.RoundingClean);
            Assert.True(b.Result.IsClosed());
            // in memory manifold (welded by position it would not be)
            Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result, checkSelfIntersections: false));
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
