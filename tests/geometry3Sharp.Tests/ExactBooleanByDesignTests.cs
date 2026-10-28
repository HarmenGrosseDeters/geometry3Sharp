using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The two results of ExactMeshBoolean that are by design and documented as such (docs/OPERATIONS.md, "By design"):
    /// open inputs give open results (generalized winding number), and solids that touch along an edge give a result
    /// that is non-manifold there when welded by position, as in Manifold.
    /// </summary>
    public class ExactBooleanByDesignTests
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

        // vertices of the mesh at a position that another vertex has too
        static int DuplicatedPositions(DMesh3 m)
        {
            var count = new Dictionary<Vector3d, int>();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                count[p] = count.TryGetValue(p, out int c) ? c + 1 : 1;
            }
            return count.Values.Where(c => c > 1).Sum();
        }

        [Fact]
        public void OpenInput_GivesOpenResult()
        {
            // a box without its top face (open) and a closed bar through its side wall y = 0 and out of its open top
            DMesh3 open = Box(0, 0, 0, 1, 1, 1);
            foreach (int t in open.TriangleIndices().ToArray()) {
                Index3i tv = open.GetTriangle(t);
                if (open.GetVertex(tv.a).z == 1 && open.GetVertex(tv.b).z == 1 && open.GetVertex(tv.c).z == 1)
                    open.RemoveTriangle(t);
            }
            Assert.False(open.IsClosed());
            DMesh3 bar = Box(0.25, -0.5, 0.5, 0.75, 0.5, 1.5);
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                var boolean = new ExactMeshBoolean(open, bar, op);
                Assert.True(boolean.Compute(), boolean.Error);
                Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, boolean.Status);
                Assert.False(boolean.ExactWindingA);
                Assert.True(boolean.ExactWindingB);
                Assert.True(boolean.IntersectingPairs > 0);
                DMesh3 r = boolean.Result;
                ResultChecks.AssertValid(r);
                Assert.True(r.TriangleCount > 0, op + ": empty");
                if (op != Ops.Intersection) {
                    // the open box's solid (generalized winding number above 1/2) is not bounded at its missing face:
                    // union and difference keep the open box's floor and walls, which end at its open boundary
                    Assert.Contains(r.EdgeIndices(), e => r.IsBoundaryEdge(e));
                    Assert.False(ExactMeshBoolean.IsWindingClosed(r), op + ": closed");
                    // the floor is kept whole (the bar does not reach it)
                    Assert.Equal(2, r.TriangleIndices().Count(t => {
                        Index3i tv = r.GetTriangle(t);
                        return r.GetVertex(tv.a).z == 0 && r.GetVertex(tv.b).z == 0 && r.GetVertex(tv.c).z == 0;
                    }));
                }
            }
        }

        [Fact]
        public void OpenInput_RefusedWhenNotAllowed()
        {
            DMesh3 open = Box(0, 0, 0, 1, 1, 1);
            open.RemoveTriangle(open.TriangleIndices().First());
            var boolean = new ExactMeshBoolean(open, Box(0.5, 0.5, 0.5, 2, 2, 2), Ops.Union) { AllowOpenMeshes = false };
            Assert.False(boolean.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.OpenInput, boolean.Status);
        }

        [Fact]
        public void SolidsTouchingAlongAnEdge_UnionNonManifoldAtTheEdge()
        {
            // unit boxes touching only along the edge x = 1, y = 1
            DMesh3 a = Box(0, 0, 0, 1, 1, 1), b = Box(1, 1, 0, 2, 2, 1);
            var boolean = new ExactMeshBoolean(a, b, Ops.Union);
            Assert.True(boolean.Compute(), boolean.Error);
            DMesh3 r = boolean.Result;
            ResultChecks.AssertValid(r);
            // closed and manifold as a DMesh3: the four triangles at the edge are split into two manifold pairs, with
            // copies of the edge's vertices
            Assert.True(r.IsClosed());
            Assert.Equal(1, boolean.NonManifoldEdges);
            Assert.Equal(0, boolean.NonManifoldTriangles);
            Assert.Equal(2.0, ResultChecks.Volume(r), 12);
            Assert.True(ExactMeshBoolean.IsWindingClosed(r));
            // welded by position, the two solids share the edge: its vertices are duplicated
            Assert.Equal(4, DuplicatedPositions(r));
            // two components, one per box
            var components = new MeshConnectedComponents(r);
            components.FindConnectedT();
            Assert.Equal(2, components.Count);
            // MeshRobustRepair.SeparateTouchingVertices moves the copies apart: no shared positions, still closed
            DMesh3 separated = new DMesh3(r);
            Assert.True(gs.MeshRobustRepair.SeparateTouchingVertices(separated, 1e-4) > 0);
            Assert.Equal(0, DuplicatedPositions(separated));
            Assert.True(separated.IsClosed());
            Assert.Equal(2.0, ResultChecks.Volume(separated), 3);
        }

        [Fact]
        public void SolidsTouchingAlongAnEdge_IntersectionEmpty_DifferenceIsA()
        {
            DMesh3 a = Box(0, 0, 0, 1, 1, 1), b = Box(1, 1, 0, 2, 2, 1);
            var inter = new ExactMeshBoolean(a, b, Ops.Intersection);
            Assert.True(inter.Compute(), inter.Error);
            Assert.Equal(0, inter.Result.TriangleCount);
            var diff = new ExactMeshBoolean(a, b, Ops.Difference);
            Assert.True(diff.Compute(), diff.Error);
            Assert.True(diff.Result.IsClosed());
            Assert.Equal(1.0, ResultChecks.Volume(diff.Result), 12);
            Assert.Equal(0, DuplicatedPositions(diff.Result));
        }
    }
}
