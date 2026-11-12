using System;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #710 (fuzz campaign #624): two cubes near 3.7e-223, one corner of B moved to about 1.8e-284. The grid coordinates
    /// reach about 1e77, where the terms of the InCircle filter (up to 12 M^4) overflow while the bound's M^4 is still
    /// finite; the filter took the sign of an infinite sum, the Delaunay triangulation of a split triangle broke, and the
    /// constraint insertion threw. (The class name keeps the mutation shards' test filter, FullyQualifiedName~ExactMeshBooleanTests.)
    /// </summary>
    public class ExactMeshBooleanTestsIssue710
    {
        // The result is closed, manifold and consistently oriented. B itself is not clean (a corner pulled far outside the
        // cube: inside-out parts and self-intersections), and the rounding of the exact result to doubles leaves slivers
        // and crossings that SafeRounding cannot remove within its bound: RoundingClean is false, as documented (Compute
        // still succeeds; StrictRounding would fail instead). The check scales the result by 2^740 (exact), so that
        // its areas do not underflow
        static void AssertClosedManifold(ExactMeshBoolean boolean, string what)
        {
            DMesh3 m = new DMesh3(boolean.Result);
            foreach (int v in m.VertexIndices().ToArray())
                m.SetVertex(v, m.GetVertex(v) * Math.Pow(2, 740));
            Assert.True(m.TriangleCount > 0 && m.IsClosed(), what + ": not closed");
            string reasons = MeshRobustRepair.NotCleanReasons(m);
            foreach (string r in reasons.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                Assert.True(r == "degenerate triangles" || r == "self-intersections", what + ": " + reasons);
            // (the rounding's defects are reported, not hidden)
            Assert.Equal(reasons == "", boolean.RoundingClean);
        }

        static readonly int[][] Tris = { new[] { 0, 2, 1 }, new[] { 0, 3, 2 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 }, new[] { 0, 1, 5 }, new[] { 0, 5, 4 },
            new[] { 1, 2, 6 }, new[] { 1, 6, 5 }, new[] { 2, 3, 7 }, new[] { 2, 7, 6 }, new[] { 3, 0, 4 }, new[] { 3, 4, 7 } };

        static DMesh3 Mesh(Vector3d[] v)
        {
            var m = new DMesh3();
            foreach (Vector3d p in v) m.AppendVertex(p);
            foreach (int[] t in Tris) m.AppendTriangle(t[0], t[1], t[2]);
            return m;
        }

        // the reproducer of the issue (MeshFuzzPilot dump, round-trip coordinates)
        internal static (DMesh3, DMesh3) Cubes()
        {
            double a0 = 3.6741945026526215E-223, a1 = 4.538710856217944E-223;
            DMesh3 a = Mesh(new[] { new Vector3d(a0, a0, a0), new Vector3d(a1, a0, a0), new Vector3d(a1, a1, a0), new Vector3d(a0, a1, a0),
                new Vector3d(a0, a0, a1), new Vector3d(a1, a0, a1), new Vector3d(a1, a1, a1), new Vector3d(a0, a1, a1) });
            DMesh3 b = Mesh(new[] { new Vector3d(a0, a0, a0), new Vector3d(a1, a0, a0),
                new Vector3d(-1.7669274406332015E-284, 1.766927440712025E-284, -1.767003118109299E-284),
                new Vector3d(3.944355863141785E-223, 4.8088722167071076E-223, a0), new Vector3d(a0, a0, a1), new Vector3d(a1, a0, a1),
                new Vector3d(a1, 4.700807672511442E-223, 4.700807672511442E-223), new Vector3d(3.944355863141785E-223, 4.8088722167071076E-223, 4.8088722167071076E-223) });
            return (a, b);
        }

        [Theory]
        [InlineData(Ops.Difference)]
        [InlineData(Ops.Union)]
        [InlineData(Ops.Intersection)]
        public void TinyCubes_NoThrow_ClosedManifoldResult(Ops op)
        {
            (DMesh3 a, DMesh3 b) = Cubes();
            var boolean = new ExactMeshBoolean(a, b, op);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, boolean.Status);
            Assert.True(boolean.Result.VertexIndices().All(v => boolean.Result.GetVertex(v).IsFinite));
            AssertClosedManifold(boolean, op.ToString());
            // the same as without the floating-point filters, which never decided here (the exact stages only)
            var exact = new ExactMeshBoolean(a, b, op) { DisableFilters = true };
            Assert.True(exact.Compute(), exact.Error);
            Assert.Equal("", Invariance.Difference(exact.Result, boolean.Result));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void TinyCubes_UnderSimilarity(string name)
        {
            // every similarity: a status, never an exception. The powers of two scale the grid exactly: the same result,
            // mapped back, bit for bit, closed and manifold. (The other similarities round the tiny cubes onto a far
            // coarser grid, utm onto one point: other, degenerate inputs; there only that Compute returns a status, with
            // a valid mesh on success)
            Similarity sim = Similarity.ByName(name);
            (DMesh3 a, DMesh3 b) = Cubes();
            DMesh3 ta = Invariance.Transformed(a, sim), tb = Invariance.Transformed(b, sim);
            var boolean = new ExactMeshBoolean(ta, tb, Ops.Difference);
            bool ok = boolean.Compute();
            if (ok == false) {
                Assert.NotEqual(ExactMeshBoolean.BooleanStatus.Success, boolean.Status);
                return;
            }
            ResultChecks.AssertValid(boolean.Result, name);
            if (sim.IsExact) {
                AssertClosedManifold(boolean, name);
                var plain = new ExactMeshBoolean(a, b, Ops.Difference);
                Assert.True(plain.Compute(), plain.Error);
                Assert.Equal("", Invariance.Difference(plain.Result, Invariance.MappedBack(boolean.Result, sim)));
            }
        }
    }
}
