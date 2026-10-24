using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class MeshBoundaryLoopsTests
    {
        static DMesh3 LoadTestFile(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "test_files", name);
            DMesh3 mesh = StandardMeshReader.ReadMesh(path);
            Assert.NotNull(mesh);
            return mesh;
        }

        // open box: box without its top face
        static DMesh3 OpenBox()
        {
            DMesh3 mesh = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
            foreach (int tid in mesh.TriangleIndices().ToArray())
                if (mesh.GetTriCentroid(tid).y > 0.99)
                    mesh.RemoveTriangle(tid, true, false);
            return mesh;
        }

        // 6x6 grid with two cells removed that touch diagonally at one vertex (a bowtie on the boundary)
        static DMesh3 GridWithBowtieHoles()
        {
            DMesh3 mesh = new GriddedRectGenerator() { Width = 6, Height = 6, EdgeVertices = 7 }.Generate().MakeDMesh();
            foreach (int tid in mesh.TriangleIndices().ToArray()) {
                Vector3d c = mesh.GetTriCentroid(tid);
                bool cellA = c.x > -1 && c.x < 0 && c.z > -1 && c.z < 0;
                bool cellB = c.x > 0 && c.x < 1 && c.z > 0 && c.z < 1;
                if (cellA || cellB)
                    mesh.RemoveTriangle(tid, true, false);
            }
            return mesh;
        }

        // checks the invariants every boundary loop decomposition must have
        static void Validate(DMesh3 mesh, MeshBoundaryLoops loops, bool expectOriented)
        {
            Assert.Equal(0, loops.SpanCount);
            var boundaryEdges = new HashSet<int>(MeshIterators.BoundaryEdges(mesh));
            var seen = new HashSet<int>();
            foreach (EdgeLoop loop in loops) {
                int N = loop.Vertices.Length;
                Assert.True(N >= 3, $"loop with {N} vertices");
                Assert.Equal(N, loop.Edges.Length);
                Assert.Equal(N, loop.Vertices.Distinct().Count());     // simple loop
                for (int i = 0; i < N; ++i) {
                    int eid = loop.Edges[i];
                    Assert.Contains(eid, boundaryEdges);
                    Assert.True(seen.Add(eid), $"edge {eid} in more than one loop");
                    Index2i ev = mesh.GetEdgeV(eid);
                    int a = loop.Vertices[i], b = loop.Vertices[(i + 1) % N];
                    Assert.True((ev.a == a && ev.b == b) || (ev.a == b && ev.b == a), "edge does not connect consecutive loop vertices");
                    if (expectOriented) {
                        Index2i oriented = mesh.GetOrientedBoundaryEdgeV(eid);
                        Assert.True(oriented.a == a && oriented.b == b, "loop does not follow boundary orientation");
                    }
                }
            }
            Assert.Equal(boundaryEdges.Count, seen.Count);
        }


        [Fact]
        public void OpenBox_OneOrientedLoop()
        {
            DMesh3 mesh = OpenBox();
            var loops = new MeshBoundaryLoops(mesh);
            Assert.Equal(1, loops.Count);
            Assert.Equal(4, loops[0].VertexCount);
            Validate(mesh, loops, true);
        }

        [Fact]
        public void BowtieHoles_SeparateSimpleLoops_AndFillable()
        {
            DMesh3 mesh = GridWithBowtieHoles();
            Assert.Single(MeshIterators.BoundaryEdges(mesh).Select(e => mesh.GetEdgeV(e)).SelectMany(ev => new[] { ev.a, ev.b })
                .GroupBy(v => v).Where(g => g.Count() > 2));
            var loops = new MeshBoundaryLoops(mesh);
            Assert.Equal(3, loops.Count);       // outer border + two holes
            Validate(mesh, loops, true);

            foreach (EdgeLoop loop in loops.Where(l => l.VertexCount == 4))
                Assert.True(new SimpleHoleFiller(mesh, loop).Fill());
            Assert.Single(new MeshBoundaryLoops(mesh).Loops);   // only the outer border is left
        }

        [Fact]
        public void InconsistentOrientation_StillOneLoop()
        {
            DMesh3 mesh = OpenBox();
            // flip one triangle on the opening: its boundary edge now runs against the others
            int tid = MeshIterators.BoundaryEdges(mesh).Select(e => mesh.GetEdgeT(e).a).First();
            mesh.ReverseTriOrientation(tid);
            var loops = new MeshBoundaryLoops(mesh);
            Assert.Equal(1, loops.Count);
            Assert.Equal(4, loops[0].VertexCount);
            Validate(mesh, loops, false);
            Assert.True(loops.SawInconsistentOrientation);
            Assert.False(loops.IsConsistentlyOriented(loops[0]));
        }

        [Fact]
        public void ConsistentLoops_AreReportedAsOriented()
        {
            DMesh3 mesh = GridWithBowtieHoles();
            var loops = new MeshBoundaryLoops(mesh);
            Assert.False(loops.SawInconsistentOrientation);
            Assert.All(loops.Loops, l => Assert.True(loops.IsConsistentlyOriented(l)));
        }

        [Theory]
        [InlineData("crazy_boundary.obj")]
        [InlineData("bunny_bowties.obj")]
        [InlineData("crazyhole.obj")]
        public void UpstreamProblemMeshes_DecomposeIntoSimpleLoops(string file)
        {
            DMesh3 mesh = LoadTestFile(file);
            var loops = new MeshBoundaryLoops(mesh);
            Validate(mesh, loops, false);
        }
    }
}
