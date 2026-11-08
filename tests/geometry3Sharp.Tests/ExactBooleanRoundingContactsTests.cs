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
    /// Contacts the rounding makes (ROADMAP finding M3, Exact Boolean 2.0 finding B2): an edge of B passes skew within
    /// about 1e-16 of an edge of the unit box A, so the two points where B's faces cross A's edge are different exact
    /// points that round to one double. Before the fix SafeRounding took the two vertices there as touching (shared
    /// positions), RoundingClean was true, and MeshRobustRepair.NotCleanReason found a self-intersection at that
    /// position: the two checks disagreed. Now the rounding separates such vertices, so both agree that the result is
    /// clean. B is a parallelepiped found by a random search over such constructions (a random edge direction through a
    /// point of A's edge x = y = 1, moved off it by a few 1e-16); its corners are given exactly, in the vertex order of
    /// TrivialBox3Generator.
    /// </summary>
    public class ExactBooleanRoundingContactsTests
    {
        static DMesh3 UnitBox()
        {
            return new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)), NoSharedVertices = false }.Generate().MakeDMesh();
        }

        static readonly double[] SkewCorners = {
            1.3368910852120819, 0.9913822349044736, -0.15334808308221481,
            0.6631089147879182, 1.0086177650955264, 0.8394865070271027,
            0.2395799982108615, 0.5253992712615134, 1.1171970470500778,
            0.9133621686350252, 0.5081637410704607, 0.12436245694076031,
            1.8191225146809884, 0.8596690991124354, 0.3366565069903712,
            1.1453403442568248, 0.8769046293034881, 1.3294910970996887,
            0.721811427679768, 0.3936861354694752, 1.6072016371226638,
            1.3955935981039318, 0.37645060527842245, 0.6143670470133463 };

        // the parallelepiped B: the unit box's topology with the corners above
        internal static DMesh3 SkewB()
        {
            DMesh3 b = UnitBox();
            int k = 0;
            foreach (int v in b.VertexIndices().ToList()) {
                b.SetVertex(v, new Vector3d(SkewCorners[3 * k], SkewCorners[3 * k + 1], SkewCorners[3 * k + 2]));
                k++;
            }
            return b;
        }

        internal static DMesh3 A() => UnitBox();

        public static IEnumerable<object[]> Ops3() => new[] { Ops.Union, Ops.Intersection, Ops.Difference }.Select(o => new object[] { o });

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op, bool serial = false)
        {
            gParallel.SerialOnThisThread = serial;
            try {
                var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
                Assert.True(boolean.Compute(), boolean.Error);
                return boolean;
            } finally {
                gParallel.SerialOnThisThread = false;
            }
        }

        static int SharedPositions(DMesh3 m)
        {
            return m.VertexIndices().GroupBy(v => m.GetVertex(v)).Count(g => g.Count() > 1);
        }

        static void AssertClean(ExactMeshBoolean b, string what)
        {
            // (the exact solid of these inputs touches itself nowhere: no two vertices may share a position, and the
            // check in memory includes the self-intersections)
            Assert.True(b.RoundingClean, $"{what}: {b.Rounding.RemainingIntersections} intersecting pairs, {b.Rounding.RemainingDegenerate} degenerate left");
            Assert.Equal(0, SharedPositions(b.Result));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result));
            AssertAgrees(b, what);
        }

        // RoundingClean and NotCleanReason agree: where the rounding is clean, the result is clean in memory. With the
        // self-intersection check there unless the result has vertices at one position (where the exact solid touches
        // itself: vertices of one exact point, which MeshRepairMetrics counts as intersecting and the rounding as shared;
        // ExactMeshBoolean.Contacts counts them, see ExactBooleanContactCountTests)
        static void AssertAgrees(ExactMeshBoolean b, string what)
        {
            Assert.True(b.Result.TriangleCount > 0 && b.Result.IsClosed(), what);
            Assert.True(b.Result.Vertices().All(v => v.IsFinite), what);
            if (b.RoundingClean == false)
                return;
            int contacts = SharedPositions(b.Result);
            string reason = MeshRobustRepair.NotCleanReason(b.Result, checkSelfIntersections: contacts == 0);
            Assert.True(reason == "", $"{what}: {reason}, positions with several vertices {contacts}");
        }

        [Fact]
        public void SkewEdges_Difference_RoundingCleanAgreesWithNotCleanReason()
        {
            // the case of the search: before the fix RoundingClean true and NotCleanReason "self-intersections"
            AssertClean(Run(A(), SkewB(), Ops.Difference), "difference");
        }

        [Theory]
        [MemberData(nameof(Ops3))]
        public void SkewEdges_AllOperations_Clean(Ops op)
        {
            AssertClean(Run(A(), SkewB(), op), op.ToString());
        }

        [Theory]
        [MemberData(nameof(Ops3))]
        public void SkewEdges_Similarities_Clean_Pow2BitIdentical(Ops op)
        {
            // 2^k scales: clean and the same result bit for bit. The other similarities round the inputs: the near
            // coincidence can become an exact contact of the solid (utm difference) or go, and on scale 1e3 union the
            // repair leaves two degenerate triangles (as on master: a SafeRounding limit, Exact Boolean 2.0 finding
            // D1); there RoundingClean and NotCleanReason must agree
            ExactMeshBoolean b0 = Run(A(), SkewB(), op);
            foreach (Similarity sim in Similarity.All()) {
                ExactMeshBoolean bs = Run(Invariance.Transformed(A(), sim), Invariance.Transformed(SkewB(), sim), op);
                if (sim.IsExact) {
                    AssertClean(bs, $"{op} {sim}");
                    Assert.True(Invariance.Difference(b0.Result, Invariance.MappedBack(bs.Result, sim)) == "", sim.ToString());
                } else
                    AssertAgrees(bs, $"{op} {sim}");
            }
        }

        static string Print(DMesh3 m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices())
                sb.Append(t).Append(':').Append(m.GetTriangle(t)).Append(';');
            return sb.ToString();
        }

        [Theory]
        [MemberData(nameof(Ops3))]
        public void SkewEdges_SerialEqualsParallel_ComputeTwice(Ops op)
        {
            string serial = Print(Run(A(), SkewB(), op, serial: true).Result);
            for (int run = 0; run < 3; ++run)
                Assert.True(serial == Print(Run(A(), SkewB(), op).Result), $"parallel run {run} differs from the serial run");
            var boolean = new ExactMeshBoolean(A(), SkewB(), op);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal(serial, Print(boolean.Result));
        }
    }
}
