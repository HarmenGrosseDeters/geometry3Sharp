using System;
using System.IO;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// OBJReader with normal/UV indices that differ from the vertex indices (Triangle.is_complex compared
    /// each index with itself), and the BuildMeshes_ByMaterial path such files now take: it dropped UVs of
    /// meshes without material and all groups, and threw for faces without a normal index.
    /// See docs/Analyse-2026-10.md.
    /// </summary>
    public class OBJReaderFixesTests
    {
        const string Quad = "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\n";

        static DMesh3Builder Read(string obj, ReadOptions options = null, Action<OBJReader> setup = null)
        {
            var reader = new OBJReader();
            setup?.Invoke(reader);
            var builder = new DMesh3Builder();
            IOReadResult result = reader.Read(new StringReader(obj), options ?? ReadOptions.Defaults, builder);
            Assert.Equal(IOCode.Ok, result.code);
            return builder;
        }

        static Vector2f ExpectedUV(Vector3d pos)
        {
            // the UVs in the files below are the positions mirrored in x
            return new Vector2f(1 - (float)pos.x, (float)pos.y);
        }

        [Fact]
        public void UVIndicesDifferentFromVertexIndices()
        {
            // four UVs, listed in a different order than the vertices
            string obj = Quad + "vt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\n" + "f 1/2 2/1 3/4\nf 1/2 3/4 4/3\n";
            DMesh3 mesh = Read(obj).Meshes.Single();
            Assert.Equal(2, mesh.TriangleCount);
            Assert.True(mesh.HasVertexUVs);
            foreach (int vid in mesh.VertexIndices())
                Assert.Equal(ExpectedUV(mesh.GetVertex(vid)), mesh.GetVertexUV(vid));
        }

        [Fact]
        public void SharedNormalIndex()
        {
            // one normal for all vertices: was dropped because the normal count differs from the vertex count
            string obj = Quad + "vn 0 0 1\nf 1//1 2//1 3//1\nf 1//1 3//1 4//1\n";
            DMesh3 mesh = Read(obj).Meshes.Single();
            Assert.True(mesh.HasVertexNormals);
            foreach (int vid in mesh.VertexIndices())
                Assert.Equal(Vector3f.AxisZ, mesh.GetVertexNormal(vid));
        }

        [Fact]
        public void GroupsKept()
        {
            string obj = Quad + "vn 0 0 1\ng first\nf 1//1 2//1 3//1\ng second\nf 1//1 3//1 4//1\n";
            DMesh3 mesh = Read(obj).Meshes.Single();
            Assert.True(mesh.HasTriangleGroups);
            Assert.Equal(new[] { 0, 1 }, mesh.TriangleIndices().Select(t => mesh.GetTriangleGroup(t)).OrderBy(g => g).ToArray());
        }

        [Fact]
        public void Materials_GroupsAndUVsWithoutMaterial()
        {
            // two materials take the by-material path, which dropped the groups, and the UVs of the
            // triangles without material
            WithMtl((dir, opt, setup) => {
                string obj = "mtllib test.mtl\n" + Quad + "vt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\n"
                    + "g first\nf 1/2 2/1 3/4\nusemtl red\nf 1/2 3/4 4/3\ng second\nusemtl blue\nf 2/1 3/4 4/3\n";
                DMesh3Builder builder = Read(obj, opt, setup);
                Assert.Equal(3, builder.Meshes.Count);
                Assert.All(builder.Meshes, m => Assert.True(m.HasTriangleGroups));
                var groups = builder.Meshes.Select(m => m.GetTriangleGroup(m.TriangleIndices().Single())).OrderBy(g => g).ToArray();
                Assert.Equal(new[] { 0, 0, 1 }, groups);
                foreach (DMesh3 m in builder.Meshes) {
                    Assert.True(m.HasVertexUVs);
                    foreach (int vid in m.VertexIndices())
                        Assert.Equal(ExpectedUV(m.GetVertex(vid)), m.GetVertexUV(vid));
                }
            });
        }

        static void WithMtl(Action<string, ReadOptions, Action<OBJReader>> f)
        {
            string dir = Path.Combine(Path.GetTempPath(), "g3_obj_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                File.WriteAllText(Path.Combine(dir, "test.mtl"), "newmtl red\nKd 1 0 0\nnewmtl blue\nKd 0 0 1\n");
                f(dir, new ReadOptions() { ReadMaterials = true }, r => r.MTLFileSearchPaths.Add(dir));
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void FacesWithAndWithoutNormalIndex()
        {
            string obj = Quad + "vn 0 0 1\nf 1//1 2//1 3//1\nf 1 3 4\n";
            DMesh3 mesh = Read(obj).Meshes.Single();
            Assert.Equal(2, mesh.TriangleCount);
            Assert.True(mesh.HasVertexNormals);
            Index3i t0 = mesh.GetTriangle(0);
            for (int j = 0; j < 3; ++j)
                Assert.Equal(Vector3f.AxisZ, mesh.GetVertexNormal(t0[j]));
        }

        [Fact]
        public void TwoMaterials_FaceWithoutNormalIndex()
        {
            WithMtl((dir, opt, setup) => {
                string obj = "mtllib test.mtl\n" + Quad + "vn 0 0 1\nvt 0 0\n"
                    + "usemtl red\nf 1//1 2//1 3//1\nusemtl blue\nf 1 3 4\n";
                DMesh3Builder builder = Read(obj, opt, setup);
                Assert.Equal(2, builder.Meshes.Count);
                Assert.All(builder.Meshes, m => Assert.Equal(1, m.TriangleCount));
                Assert.Equal(2, builder.Materials.Count);
            });
        }

        [Fact]
        public void SimpleFilesUnchanged()
        {
            // per-vertex normals and UVs with matching indices still take the simple path: vertices are not split
            string obj = Quad + "vn 0 0 1\nvn 0 0 1\nvn 0 0 1\nvn 0 0 1\nvt 1 0\nvt 0 0\nvt 0 1\nvt 1 1\n"
                + "f 1/1/1 2/2/2 3/3/3\nf 1/1/1 3/3/3 4/4/4\n";
            var reader = new OBJReader();
            var builder = new DMesh3Builder();
            Assert.Equal(IOCode.Ok, reader.Read(new StringReader(obj), ReadOptions.Defaults, builder).code);
            Assert.False(reader.HasComplexVertices);
            DMesh3 mesh = builder.Meshes.Single();
            Assert.Equal(4, mesh.VertexCount);
            foreach (int vid in mesh.VertexIndices())
                Assert.Equal(ExpectedUV(mesh.GetVertex(vid)), mesh.GetVertexUV(vid));
        }
    }
}
