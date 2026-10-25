using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class MeshEditorBowtieTests
    {
        [Fact]
        public void DoubleCone_IsClosedWithOneBowtie()
        {
            DMesh3 mesh = MeshTestUtil.DoubleConeBowtie(8, 0);
            Assert.Equal(0, MeshTestUtil.BoundaryEdgeCount(mesh));
            Assert.Single(MeshIterators.BowtieVertices(mesh));
        }

        // the old DisconnectBowtie grouped one-ring triangles in a single order-dependent pass and
        // could split a fan into several pieces, which cut cracks (boundary edges) into the mesh
        [Fact]
        public void DisconnectAllBowties_NeverOpensCracks()
        {
            for (int seed = 0; seed < 200; ++seed) {
                DMesh3 mesh = MeshTestUtil.DoubleConeBowtie(8, seed);
                int nRemaining = new MeshEditor(mesh).DisconnectAllBowties();

                Assert.Equal(0, nRemaining);
                Assert.True(MeshTestUtil.BoundaryEdgeCount(mesh) == 0, $"seed {seed}: boundary edges after disconnect");
                Assert.Equal(2 * 8 + 2 + 2, mesh.VertexCount);   // apex was duplicated exactly once
                var comps = new MeshConnectedComponents(mesh);
                comps.FindConnectedT();
                Assert.Equal(2, comps.Count);
                Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
            }
        }

        /// <summary> nFans open fans of nTris triangles each around vertex 0, in separate sectors; triangles in random order </summary>
        static DMesh3 OpenFans(int nFans, int nTris, int seed)
        {
            var mesh = new DMesh3(MeshComponents.VertexNormals | MeshComponents.VertexColors | MeshComponents.VertexUVs | MeshComponents.FaceGroups);
            mesh.AppendVertex(new NewVertexInfo(Vector3d.Zero, Vector3f.AxisZ, new Vector3f(0.25f, 0.5f, 0.75f), new Vector2f(0.5f, 0.5f)));
            var tris = new List<Index3i>();
            double sector = 2 * Math.PI / nFans;
            for (int f = 0; f < nFans; ++f) {
                int first = mesh.VertexCount;
                for (int i = 0; i <= nTris; ++i) {
                    double a = f * sector + 0.8 * sector * i / nTris;
                    mesh.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), 0));
                }
                for (int i = 0; i < nTris; ++i)
                    tris.Add(new Index3i(0, first + i, first + i + 1));
            }
            var rand = new Random(seed);
            foreach (Index3i t in tris.OrderBy(t => rand.Next()))
                mesh.AppendTriangle(t, 7);
            return mesh;
        }

        [Fact]
        public void DisconnectBowtie_ThreeOpenFans()
        {
            for (int seed = 0; seed < 100; ++seed) {
                DMesh3 mesh = OpenFans(3, 4, seed);
                int boundary = MeshTestUtil.BoundaryEdgeCount(mesh);
                new MeshEditor(mesh).DisconnectBowtie(0);

                Assert.Empty(MeshIterators.BowtieVertices(mesh));
                Assert.True(MeshTestUtil.BoundaryEdgeCount(mesh) == boundary, $"seed {seed}: boundary edges {boundary} -> {MeshTestUtil.BoundaryEdgeCount(mesh)}");
                Assert.Equal(1 + 3 * 5 + 2, mesh.VertexCount);     // center duplicated once per extra fan
                var comps = new MeshConnectedComponents(mesh);
                comps.FindConnectedT();
                Assert.Equal(3, comps.Count);
                Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
            }
        }

        [Fact]
        public void DisconnectBowtie_CopiesKeepAttributes()
        {
            DMesh3 mesh = OpenFans(2, 3, 1);
            new MeshEditor(mesh).DisconnectBowtie(0);
            int copy = mesh.MaxVertexID - 1;
            Assert.Equal(Vector3d.Zero, mesh.GetVertex(copy));
            Assert.Equal(Vector3f.AxisZ, mesh.GetVertexNormal(copy));
            Assert.Equal(new Vector3f(0.25f, 0.5f, 0.75f), mesh.GetVertexColor(copy));
            Assert.Equal(new Vector2f(0.5f, 0.5f), mesh.GetVertexUV(copy));
            Assert.All(mesh.TriangleIndices(), t => Assert.Equal(7, mesh.GetTriangleGroup(t)));
        }

        [Fact]
        public void DisconnectBowtie_SingleFanUnchanged()
        {
            DMesh3 mesh = OpenFans(1, 5, 2);
            int nv = mesh.VertexCount;
            new MeshEditor(mesh).DisconnectBowtie(0);
            Assert.Equal(nv, mesh.VertexCount);
        }
    }
}
