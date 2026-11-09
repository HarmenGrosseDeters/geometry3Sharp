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

        // tetrahedra with their apexes at one position p in separate octants; the apexes of the first two share an exact
        // point (the solid touches itself there), the others are a point each, or (twoEach) the last two share one, or
        // (singleFirst) the first is a point of its own and the next two share one
        static (DMesh3, Func<int, int>, int[]) Cones(int count, bool twoEach, bool singleFirst = false, Vector3d? at = null)
        {
            var m = new DMesh3();
            Vector3d p = at ?? new Vector3d(0.25, 0.5, 0.75);
            var octants = new[] { new Vector3d(1, 1, 1), new Vector3d(-1, -1, 1), new Vector3d(1, -1, -1), new Vector3d(-1, 1, -1) };
            var apex = new int[count];
            for (int i = 0; i < count; ++i) {
                Vector3d o = octants[i];
                apex[i] = m.VertexCount;
                AddTetrahedron(m, p, new Vector3d(o.x, 0.2 * o.y, 0.3 * o.z), new Vector3d(0.2 * o.x, o.y, 0.3 * o.z), new Vector3d(0.3 * o.x, 0.2 * o.y, o.z));
            }
            var ids = new Dictionary<int, int>();
            for (int i = 0; i < count; ++i)
                ids[apex[i]] = singleFirst ? (i == 0 ? 2000 : i < 3 ? 1000 : 2000 + i)
                    : twoEach ? (i < 2 ? 1000 : 2000) : (i < 2 ? 1000 : 2000 + i);
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
        public void ApplyAgain_OnAnEmptiedMesh_NoStaleContacts()
        {
            // reuse: an Apply that leaves a contact, then the mesh is emptied and Apply runs again on the same instance.
            // The early return for an empty mesh resets every count, so Apply and IsClean agree (before, the contact
            // counts kept the first Apply's values: Apply true, IsClean false)
            (DMesh3 m, Func<int, int> pointId, int[] apex) = Cones(4, twoEach: true);
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, PointIdF = pointId };
            Assert.False(r.Apply());
            Assert.Equal(1, r.RemainingFalseContacts);
            r.Mesh = new DMesh3();
            Assert.True(r.Apply());
            Assert.True(r.IsClean);
            Assert.Equal(0, r.InitialFalseContacts);
            Assert.Equal(0, r.RemainingFalseContacts);
        }

        [Fact]
        public void SinglePointOfTheSmallestVertex_Moved_TheContactOfTheOtherKept()
        {
            // the vertex of smallest id is the single vertex of its point: the point with two vertices stays (not the
            // point of the first vertex), the single one moves
            (DMesh3 m, Func<int, int> pointId, int[] apex) = Cones(3, twoEach: false, singleFirst: true);
            Vector3d p = m.GetVertex(apex[0]);
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, PointIdF = pointId };
            Assert.True(r.Apply());
            Assert.Equal(1, r.InitialFalseContacts);
            Assert.Equal(0, r.RemainingFalseContacts);
            Assert.NotEqual(p, m.GetVertex(apex[0]));
            Assert.Equal(p, m.GetVertex(apex[1]));
            Assert.Equal(p, m.GetVertex(apex[2]));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void ContactAtTheOrigin_SeparatedByANormalStep(string name)
        {
            // at the origin a position has no unit in the last place: the first step is the unit of maxLength (not a
            // subnormal one); the 2^k scales give the same result, scaled
            Similarity sim = Similarity.ByName(name);
            (DMesh3 m0, Func<int, int> pointId, int[] apex) = Cones(3, twoEach: false, at: Vector3d.Zero);
            DMesh3 reference = new DMesh3(m0);
            Assert.True(new MeshRoundingRepair(reference) { SharedPositionsTouch = true, PointIdF = pointId }.Apply());
            DMesh3 m = Invariance.Transformed(m0, sim);
            Vector3d p = m.GetVertex(apex[2]);
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, PointIdF = pointId };
            Assert.True(r.Apply(), name);
            Assert.Equal(1, r.InitialFalseContacts);
            Assert.Equal(0, r.RemainingFalseContacts);
            Assert.Equal(m.GetVertex(apex[0]), m.GetVertex(apex[1]));
            Vector3d d = m.GetVertex(apex[2]) - p;
            double moved = Math.Max(Math.Abs(d.x), Math.Max(Math.Abs(d.y), Math.Abs(d.z)));
            Assert.True(moved >= 2.2250738585072014E-308 && moved <= r.MaxLength, $"{name}: moved by {moved}");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m, checkSelfIntersections: false));
            if (sim.IsExact)
                Assert.True(Invariance.Difference(reference, Invariance.MappedBack(m, sim)) == "", name);
        }

        [Fact]
        public void TouchingTheReflectedBox_AtNegativeZero_OneContact()
        {
            // the second box is the first reflected through the origin: its corner there is (-0.0, -0.0, -0.0), the
            // first box's (0.0, 0.0, 0.0), one exact point (the arrangement merges them): one contact, of the solid.
            // (count_contacts needs no -0.0 normalization: Vector3d's equality compares with ==, and double's hash is
            // the same for +0 and -0, on .NET Framework as on .NET)
            DMesh3 a = Box(Vector3d.Zero), b = new DMesh3(a);
            foreach (int v in b.VertexIndices().ToList())
                b.SetVertex(v, -b.GetVertex(v));
            b.ReverseOrientation();
            Assert.Contains(b.VertexIndices(), v => BitConverter.DoubleToInt64Bits(b.GetVertex(v).x) == long.MinValue);
            var boolean = new ExactMeshBoolean(a, b, Ops.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal(1, boolean.Contacts);
            Assert.Equal(0, boolean.Rounding.InitialFalseContacts);
            Assert.True(boolean.RoundingClean);
            Assert.Equal(new Vector3d(0, 0, 0), new Vector3d(-0.0, -0.0, -0.0));
            Assert.Equal(new Vector3d(0, 0, 0).GetHashCode(), new Vector3d(-0.0, -0.0, -0.0).GetHashCode());
        }

        [Theory]
        [InlineData(1, 1, 1, 1)]
        [InlineData(1, 1, 0, 2)]
        public void TouchingBoxes_Contacts_Similarities(double dx, double dy, double dz, int contacts)
        {
            // the shared corners are input points: every similarity maps them to one double each, so the contacts stay
            foreach (Similarity sim in Similarity.All()) {
                var b = new ExactMeshBoolean(Invariance.Transformed(Box(Vector3d.Zero), sim), Invariance.Transformed(Box(new Vector3d(dx, dy, dz)), sim), Ops.Union);
                Assert.True(b.Compute(), b.Error);
                Assert.True(b.Contacts == contacts, $"{sim}: contacts {b.Contacts}");
                Assert.True(b.Rounding.InitialFalseContacts == 0, sim.ToString());
                Assert.True(b.RoundingClean, sim.ToString());
                Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result, checkSelfIntersections: false));
            }
        }

        [Fact]
        public void EdgeSplitAtItsMidpoint_CopiesShareTheExactPoint_NotSeparated()
        {
            // a solid touching itself along an edge: the edge is split at its midpoint, a vertex per pair of pieces;
            // the copies have one exact point id (the edge's), so they are a contact of the solid, not one the rounding
            // made, and stay at one position
            DMesh3[] parts = ExactMeshBooleanTests.PinchedSolid();
            var b = new ExactMeshBoolean(ExactMeshBooleanTests.Merge(parts), new DMesh3(), Ops.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(1, b.SplitNonManifoldEdges);
            Assert.Equal(0, b.Rounding.InitialFalseContacts);
            Assert.True(b.Result.VertexIndices().Any(v => b.Result.GetVertex(v) == new Vector3d(1, 1, 0.5)));
            Assert.Equal(1, b.Contacts);
            Assert.True(b.RoundingClean);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result, checkSelfIntersections: false));
        }

        [Fact]
        public void OverlappingBoxes_NoContacts()
        {
            var b = new ExactMeshBoolean(Box(Vector3d.Zero), Box(new Vector3d(0.5, 0.25, 0.125)), Ops.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(0, b.Contacts);
            Assert.Equal(0, b.Rounding.InitialFalseContacts);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result));
        }
    }
}
