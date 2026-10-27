using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshBoolean with open inputs (generalized winding numbers) where an open boundary of an input lies on an
    /// edge that other pieces meet: there the integer jumps of the winding numbers around the edge do not add up to
    /// zero, no inside/outside choice for the regions around the edge fits all pieces, and the kept pieces can run the
    /// edge the same way. They are not joined there (ConflictEdges), so the result stays consistently oriented.
    /// </summary>
    public class ExactBooleanOpenInputTests
    {
        static DMesh3 Box(Vector3d center, Vector3d extent) =>
            new TrivialBox3Generator() { Box = new Box3d(center, extent) }.Generate().MakeDMesh();

        // the box of DefectInputTests.UnionWithBox
        static DMesh3 TestBox() => Box(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.6, 0.4, 0.7));

        // interior edges whose two triangles run the same way (by vertex ids)
        static int InconsistentEdges(DMesh3 m)
        {
            int n = 0;
            foreach (int e in m.EdgeIndices()) {
                Index2i et = m.GetEdgeT(e), ev = m.GetEdgeV(e);
                if (et.b == DMesh3.InvalidID) continue;
                bool fa = IndexUtil.find_tri_ordered_edge(ev.a, ev.b, m.GetTriangle(et.a).array) != DMesh3.InvalidID;
                bool fb = IndexUtil.find_tri_ordered_edge(ev.a, ev.b, m.GetTriangle(et.b).array) != DMesh3.InvalidID;
                if (fa == fb) ++n;
            }
            return n;
        }

        static void AssertConsistent(ExactMeshBoolean op, string what)
        {
            Assert.True(op.Status == ExactMeshBoolean.BooleanStatus.Success, what + ": " + op.Error);
            DMesh3 r = op.Result;
            Assert.True(r.TriangleCount > 0, what + ": empty");
            Assert.Equal(0, InconsistentEdges(r));
            Assert.True(r.CheckValidity(true, FailMode.ReturnOnly), what + ": invalid DMesh3");
            Assert.True(r.Vertices().All(v => v.IsFinite), what + ": non-finite coordinates");
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Intersection, ExactMeshBoolean.Operations.Difference }) {
                yield return new object[] { "minimized", op };
                yield return new object[] { "minimized, unrounded", op };
            }
        }

        static DMesh3 Input(string name) =>
            name == "minimized" ? DefectInputTests.MinimizedRandomEdits6() : DefectInputTests.MinimizedRandomEdits6Unrounded();

        [Theory]
        [MemberData(nameof(Cases))]
        public void OpenBoundaryOnEdge_ConsistentResult(string name, ExactMeshBoolean.Operations op)
        {
            foreach (bool simplify in new[] { false, true })
                foreach (bool safe in new[] { false, true }) {
                    var b = new ExactMeshBoolean(Input(name), TestBox(), op) { SimplifyFlatRegions = simplify, SafeRounding = safe };
                    b.Compute();
                    Assert.False(b.ExactWindingA);
                    AssertConsistent(b, $"{name} {op} simplify {simplify} safe rounding {safe}");
                }
        }

        [Fact]
        public void Minimized_Union_PiecesRunningTheSameWayAreNotJoined()
        {
            // the union keeps two pieces that run the edge from the origin along the open edge of the fourth
            // triangle the same way (the edge where that triangle meets the coplanar ones)
            var b = new ExactMeshBoolean(DefectInputTests.MinimizedRandomEdits6(), TestBox(), ExactMeshBoolean.Operations.Union);
            Assert.True(b.Compute(), b.Error);
            AssertConsistent(b, "union");
            Assert.True(b.ConflictEdges > 0);
            Assert.Contains(b.Result.BoundaryEdgeIndices(), e => b.Result.GetEdgeV(e) is Index2i ev
                && (b.Result.GetVertex(ev.a).Length == 0 || b.Result.GetVertex(ev.b).Length == 0));
        }

        [Fact]
        public void ClosedInputs_NoConflictEdges()
        {
            // the winding numbers of closed meshes are exact: the pieces at every edge fit together
            DMesh3 a = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 0.8, EdgeVertices = 6 }.Generate().MakeDMesh();
            MeshTransforms.Translate(s, new Vector3d(0.9, 0.3, 0.2));
            foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Intersection, ExactMeshBoolean.Operations.Difference }) {
                var b = new ExactMeshBoolean(a, s, op);
                Assert.True(b.Compute(), b.Error);
                Assert.Equal(0, b.ConflictEdges);
                ResultChecks.AssertClosed(b.Result);
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Minimized_Union_SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 a = DefectInputTests.MinimizedRandomEdits6(), b = TestBox();
            var op0 = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            Assert.True(op0.Compute(), op0.Error);
            var op = new ExactMeshBoolean(Invariance.Transformed(a, s), Invariance.Transformed(b, s), ExactMeshBoolean.Operations.Union);
            Assert.True(op.Compute(), op.Error);
            // a consistent result under every transform; the same one under the exact ones (the others round the
            // coordinates, and the case depends on exact coplanarity and coincidence: another arrangement)
            AssertConsistent(op, s.Name);
            if (s.IsExact) {
                Assert.Equal(op0.ConflictEdges, op.ConflictEdges);
                Assert.Equal("", Invariance.GeometricDifference(op0.Result, Invariance.MappedBack(op.Result, s)));
            }
        }

        [Fact]
        public void ReusedInstance_ConflictEdgesReset_SameResult()
        {
            var op = new ExactMeshBoolean(DefectInputTests.MinimizedRandomEdits6(), TestBox(), ExactMeshBoolean.Operations.Union);
            Assert.True(op.Compute(), op.Error);
            int conflicts = op.ConflictEdges;
            DMesh3 first = op.Result;
            Assert.True(conflicts > 0);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(conflicts, op.ConflictEdges);
            Assert.Equal("", Invariance.Difference(first, op.Result));
            // then closed inputs on the same instance: no conflicts, as from a fresh instance
            op.MeshA = Box(Vector3d.Zero, Vector3d.One);
            op.MeshB = Box(new Vector3d(0.5, 0.5, 0.5), new Vector3d(1, 0.3, 0.3));
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(0, op.ConflictEdges);
            var fresh = new ExactMeshBoolean(op.MeshA, op.MeshB, ExactMeshBoolean.Operations.Union);
            Assert.True(fresh.Compute(), fresh.Error);
            Assert.Equal("", Invariance.Difference(fresh.Result, op.Result));
        }

        [Theory]
        [InlineData("minimized")]
        [InlineData("minimized, unrounded")]
        public void OpenBoundaryOnEdge_RepeatedRuns_SameResult(string name)
        {
            // the arrangement runs in parallel (point ids depend on the thread schedule); the classification and the
            // pairing at the edges must not
            DMesh3 first = null;
            int conflicts = -1;
            for (int run = 0; run < 20; ++run) {
                var op = new ExactMeshBoolean(Input(name), TestBox(), ExactMeshBoolean.Operations.Union);
                Assert.True(op.Compute(), op.Error);
                if (first == null) {
                    first = op.Result;
                    conflicts = op.ConflictEdges;
                    AssertConsistent(op, name);
                } else {
                    Assert.Equal(conflicts, op.ConflictEdges);
                    Assert.Equal("", Invariance.Difference(first, op.Result));
                }
            }
        }
    }
}
