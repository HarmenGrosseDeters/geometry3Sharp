using System;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Tests written from the surviving and uncovered mutants of the mutation workflow (tests/mutation, shards
    /// rounding and boolean-2, run of 28 October 2026): each covers a path that no test of its shard reached or
    /// checked.
    /// </summary>
    public class ExactBooleanSurvivorTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var box = new Box3d(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3d((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2));
            DMesh3 mesh = new TrivialBox3Generator() { Box = box }.Generate().MakeDMesh();
            foreach (int vid in mesh.VertexIndices().ToArray()) {
                Vector3d v = mesh.GetVertex(vid);
                mesh.SetVertex(vid, new Vector3d(v.x < box.Center.x ? x0 : x1, v.y < box.Center.y ? y0 : y1, v.z < box.Center.z ? z0 : z1));
            }
            return mesh;
        }

        // two caps on both sides of (a, b), apexes at 1/4 and 3/4 (as MeshRoundingRepairStrategyTests.TwoCaps): one zip
        static DMesh3 TwoCaps()
        {
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(4, 0, 0));
            int c = m.AppendVertex(new Vector3d(1, 0, 0)), d = m.AppendVertex(new Vector3d(3, 0, 0));
            int x = m.AppendVertex(new Vector3d(2, 1, 0)), y = m.AppendVertex(new Vector3d(2, -1, 0));
            m.AppendTriangle(a, b, c);
            m.AppendTriangle(b, a, d);
            m.AppendTriangle(c, b, x);
            m.AppendTriangle(d, a, y);
            return m;
        }

        [Fact]
        public void RoundingRepair_EmptyMesh_CleanWithoutCounts()
        {
            // after a run with defects and edits, Apply on an empty mesh resets every count
            var r = new MeshRoundingRepair(TwoCaps());
            Assert.True(r.Apply());
            Assert.Equal(2, r.InitialDegenerate);
            Assert.Equal(1, r.Zips);
            r.Mesh = new DMesh3();
            Assert.True(r.Apply());
            Assert.True(r.IsClean);
            Assert.Equal(0, r.InitialIntersections + r.InitialDegenerate + r.RemainingIntersections + r.RemainingDegenerate);
            Assert.Equal(0, r.Collapses + r.Flips + r.Moves + r.Zips + r.RemovedComponents + r.RemovedTriangles);
        }

        [Fact]
        public void RoundingRepair_ReusedOnAnotherMesh_EditsOfTheFirstForgotten()
        {
            // the first mesh is repaired by a zip, which edits vertices 2 and 3; the second is a thin closed slab
            // (thinner than MaxDisplacement times its diagonal) without defects, whose vertices 2 and 3 are not edited:
            // it must be kept (a thin component is removed only if it has a defect or an edited vertex in this run)
            var r = new MeshRoundingRepair(TwoCaps());
            Assert.True(r.Apply());
            Assert.Equal(1, r.Zips);
            DMesh3 slab = Box(0, 0, 0, 1, 1, 1e-7);
            Assert.True(slab.IsVertex(2) && slab.IsVertex(3));
            r.Mesh = slab;
            Assert.True(r.Apply());
            Assert.Equal(0, r.Zips);
            Assert.Equal(0, r.RemovedComponents);
            Assert.Equal(12, slab.TriangleCount);
            // a fresh instance does the same
            DMesh3 slab2 = Box(0, 0, 0, 1, 1, 1e-7);
            Assert.True(new MeshRoundingRepair(slab2).Apply());
            Assert.Equal(12, slab2.TriangleCount);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExactVertices_SplitEdge_VertexAtTheExactMidpoint(bool fractional)
        {
            // boxes A and B touching only along a vertical edge, between two slabs that join them above and below:
            // the surface is connected around both ends of the edge, so its four triangles cannot be separated by
            // duplicating the ends, and ExactMeshBoolean splits the edge at its midpoint, once per manifold pair. With
            // ComputeExactVertices the split vertices get the exact midpoint. In the fractional case the two ends of the
            // edge have different denominators (exact points are reduced: (1, 1.5, 0.5) has W = 2, (1, 1.5, 2.25) W = 4),
            // so that the rational sum of two points with different W is tested
            double s = 0, z0 = fractional ? 0.5 : 0, z1 = fractional ? 2.25 : 3;
            double x = 1 + s, y = fractional ? 1.5 : 1.25;
            var meshes = new[] {
                Box(s, s, z0, x, y, z1), Box(x, y, z0, 2.5 + s, 2 + s, z1),
                Box(s, s, z1, 2.5 + s, 2 + s, z1 + 1), Box(s, s, z0 - 0.75, 2.5 + s, 2 + s, z0) };
            var boolean = new ExactMeshBoolean(meshes, Ops.Union) { ComputeExactVertices = true };
            Assert.True(boolean.Compute(), boolean.Error);
            DMesh3 r = boolean.Result;
            Assert.True(boolean.SplitNonManifoldEdges >= 1, boolean.SplitNonManifoldEdges + " split edges");
            Assert.True(r.IsClosed());
            Assert.NotNull(boolean.ResultExactVertices);
            Vector3d mid = new Vector3d(x, y, 0.5 * (z0 + z1));
            int splits = 0;
            foreach (int vid in r.VertexIndices()) {
                // every exact coordinate is dyadic here, so ToVector3d is exact
                Vector3d e = boolean.ResultExactVertices[vid].ToVector3d();
                Assert.Equal(r.GetVertex(vid), e);
                if (e == mid)
                    splits++;
            }
            // one copy of the midpoint per manifold pair
            Assert.Equal(2, splits);
            // the two slabs (2.5 x 2, heights 1 and 0.75) and A and B between them
            double vol = 2.5 * 2 * 1.75 + ((x - s) * (y - s) + (2.5 - (x - s)) * (2 - (y - s))) * (z1 - z0);
            Assert.Equal(vol, ResultChecks.Volume(r), 9);
        }
    }
}
