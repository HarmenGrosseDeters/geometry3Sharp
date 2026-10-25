using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// DSubmesh3(mesh, triangles) computed the submesh in the constructor, before the object initializer
    /// ran, so `new DSubmesh3(mesh, tris) { ComputeTriMaps = true }` and the like had no effect.
    /// See docs/Analyse-2026-10.md.
    /// </summary>
    public class DSubmesh3Tests
    {
        static DMesh3 Sphere()
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            Assert.True(mesh.HasVertexNormals && mesh.HasTriangleGroups);
            return mesh;
        }

        static int[] HalfTriangles(DMesh3 mesh)
        {
            return mesh.TriangleIndices().Where(t => mesh.GetTriCentroid(t).z > 0).ToArray();
        }

        static void AssertTriMapsCorrect(DMesh3 mesh, DSubmesh3 sub, int[] tris)
        {
            Assert.Equal(tris.Length, sub.SubMesh.TriangleCount);
            foreach (int tid in tris) {
                int sub_tid = sub.MapTriangleToSubmesh(tid);
                Assert.Equal(tid, sub.MapTriangleToBaseMesh(sub_tid));
                Index3i bt = mesh.GetTriangle(tid), st = sub.SubMesh.GetTriangle(sub_tid);
                for (int j = 0; j < 3; ++j)
                    Assert.Equal(bt[j], sub.MapVertexToBaseMesh(st[j]));
            }
        }

        [Fact]
        public void Initializer_ComputeTriMaps()
        {
            DMesh3 mesh = Sphere();
            int[] tris = HalfTriangles(mesh);
            var sub = new DSubmesh3(mesh, tris) { ComputeTriMaps = true };
            AssertTriMapsCorrect(mesh, sub, tris);
        }

        [Fact]
        public void Initializer_ComputeTriMaps_Enumerable()
        {
            DMesh3 mesh = Sphere();
            int[] tris = HalfTriangles(mesh);
            var sub = new DSubmesh3(mesh, tris.Select(t => t)) { ComputeTriMaps = true };
            AssertTriMapsCorrect(mesh, sub, tris);
        }

        [Fact]
        public void Initializer_WantComponents()
        {
            DMesh3 mesh = Sphere();
            var sub = new DSubmesh3(mesh, HalfTriangles(mesh)) { WantComponents = MeshComponents.None };
            Assert.False(sub.SubMesh.HasVertexNormals);
            Assert.False(sub.SubMesh.HasTriangleGroups);
        }

        [Fact]
        public void Initializer_OverrideGroupID()
        {
            DMesh3 mesh = Sphere();
            var sub = new DSubmesh3(mesh, HalfTriangles(mesh)) { OverrideGroupID = 42 };
            Assert.All(sub.SubMesh.TriangleIndices(), t => Assert.Equal(42, sub.SubMesh.GetTriangleGroup(t)));
        }

        [Fact]
        public void TwoStep_Unchanged()
        {
            // the existing way to pass settings: construct without triangles, then Compute()
            DMesh3 mesh = Sphere();
            int[] tris = HalfTriangles(mesh);
            var sub = new DSubmesh3(mesh) { ComputeTriMaps = true, OverrideGroupID = 3 };
            sub.Compute(tris);
            AssertTriMapsCorrect(mesh, sub, tris);
            Assert.All(sub.SubMesh.TriangleIndices(), t => Assert.Equal(3, sub.SubMesh.GetTriangleGroup(t)));
        }

        [Fact]
        public void SettingSameValue_DoesNotRecompute()
        {
            // a recompute would replace SubMesh, losing edits made to it
            DMesh3 mesh = Sphere();
            var sub = new DSubmesh3(mesh, HalfTriangles(mesh));
            DMesh3 first = sub.SubMesh;
            sub.ComputeTriMaps = false;
            sub.OverrideGroupID = -1;
            sub.WantComponents = MeshComponents.All;
            Assert.Same(first, sub.SubMesh);
        }
    }
}
