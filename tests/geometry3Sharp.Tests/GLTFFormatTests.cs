using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using g3;
using Xunit;
using static geometry3Sharp.Tests.MeshFormatTestUtil;

namespace geometry3Sharp.Tests
{
    public class GLTFFormatTests
    {
        static List<DMesh3> ReadJson(string json, out IOReadResult result, List<string> warnings = null)
        {
            GLTFReader reader = new GLTFReader();
            if (warnings != null)
                reader.warningEvent += (msg, data) => warnings.Add(msg);
            DMesh3Builder builder = new DMesh3Builder();
            result = reader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), ReadOptions.Defaults, builder);
            return builder.Meshes;
        }

        static string DataUri(byte[] bytes)
        {
            return "data:application/octet-stream;base64," + Convert.ToBase64String(bytes);
        }

        static byte[] Floats(params float[] values)
        {
            byte[] b = new byte[values.Length * 4];
            Buffer.BlockCopy(values, 0, b, 0, b.Length);
            return b;
        }

        // unit right triangle in the xy plane: (0,0,0), (1,0,0), (0,1,0)
        static readonly float[] TriPositions = { 0, 0, 0, 1, 0, 0, 0, 1, 0 };

        static string SingleTriangle(string nodes, string extra = "")
        {
            return "{\"asset\":{\"version\":\"2.0\"}," + extra +
                "\"buffers\":[{\"byteLength\":36,\"uri\":\"" + DataUri(Floats(TriPositions)) + "\"}]," +
                "\"bufferViews\":[{\"buffer\":0,\"byteLength\":36}]," +
                "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}]," +
                "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0}}]}]," +
                nodes + "}";
        }


        [Fact]
        public void GLB_RoundTrip_WithAttributes()
        {
            DMesh3 mesh = AttributeSphere(new Vector3d(1, 2, 3));
            DMesh3 read = WriteRead(mesh, "glb", AllAttributes(true));
            AssertSameTopology(mesh, read);
            Assert.True(MaxPositionError(mesh, read) < 1e-6);
            Assert.True(read.HasVertexNormals && read.HasVertexColors && read.HasVertexUVs);
            Assert.True(MaxAttributeError(mesh, read, (m, v) => m.GetVertexNormal(v)) < 1e-6);
            Assert.True(MaxAttributeError(mesh, read, (m, v) => m.GetVertexColor(v)) < 1e-6);
            Assert.Equal(0.0, MaxUVError(mesh, read));
            Assert.True(read.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void GLTF_Embedded_RoundTrip()
        {
            DMesh3 mesh = AttributeSphere(new Vector3d(-4, 0.5, 9));
            DMesh3 read = WriteRead(mesh, "gltf", AllAttributes(false));
            AssertSameTopology(mesh, read);
            Assert.True(MaxPositionError(mesh, read) < 1e-5);
            Assert.True(read.HasVertexNormals);
        }

        [Fact]
        public void LargeCoordinates_KeepPrecision_WithCenteredNodes()
        {
            // UTM-like coordinates, where float32 has a resolution of 0.5
            DMesh3 mesh = AttributeSphere(new Vector3d(500000.123456, 5700000.654321, 312.25), 10.0);
            DMesh3 read = WriteRead(mesh, "glb", WriteOptions.Defaults);
            double err = MaxPositionError(mesh, read);
            Assert.True(err < 1e-5, "centered error " + err);

            // without centering, the float positions lose the precision
            MemoryStream ms = new MemoryStream();
            new GLTFWriter() { CenterNodes = false }.WriteGLB(ms, new List<WriteMesh>() { new WriteMesh(mesh) }, WriteOptions.Defaults);
            ms.Position = 0;
            DMesh3Builder builder = new DMesh3Builder();
            Assert.Equal(IOCode.Ok, new GLTFReader().Read(ms, ReadOptions.Defaults, builder).code);
            Assert.True(MaxPositionError(mesh, builder.Meshes[0]) > 1e-2);
        }

        [Fact]
        public void GLB_Layout_IsValid()
        {
            DMesh3 mesh = AttributeSphere(Vector3d.Zero);
            mesh.RemoveTriangle(mesh.TriangleIndices().First(), true, false);   // odd sizes
            MemoryStream ms = new MemoryStream();
            new GLTFWriter().WriteGLB(ms, new List<WriteMesh>() { new WriteMesh(mesh, "name \"quoted\"") }, AllAttributes(true));
            byte[] b = ms.ToArray();
            Assert.Equal(0x46546C67u, BitConverter.ToUInt32(b, 0));
            Assert.Equal(2u, BitConverter.ToUInt32(b, 4));
            Assert.Equal((uint)b.Length, BitConverter.ToUInt32(b, 8));
            uint jsonLength = BitConverter.ToUInt32(b, 12);
            Assert.Equal(0u, jsonLength % 4);
            Assert.Equal(0x4E4F534Au, BitConverter.ToUInt32(b, 16));
            int binHeader = 20 + (int)jsonLength;
            uint binLength = BitConverter.ToUInt32(b, binHeader);
            Assert.Equal(0u, binLength % 4);
            Assert.Equal(0x004E4942u, BitConverter.ToUInt32(b, binHeader + 4));
            Assert.Equal(b.Length, binHeader + 8 + (int)binLength);

            // JSON chunk parses, required POSITION min/max are present
            var root = (Dictionary<string, object>)MiniJson.Parse(Encoding.UTF8.GetString(b, 20, (int)jsonLength));
            var acc = (Dictionary<string, object>)((List<object>)root["accessors"])[0];
            Assert.Equal(3, ((List<object>)acc["min"]).Count);
            Assert.Equal("name \"quoted\"", ((Dictionary<string, object>)((List<object>)root["meshes"])[0])["name"]);
        }

        [Fact]
        public void Write_MultipleMeshes_EmptyMesh_AndPointSet()
        {
            DMesh3 a = AttributeSphere(Vector3d.Zero);
            DMesh3 empty = new DMesh3();
            DMesh3 points = new DMesh3();
            points.AppendVertex(new Vector3d(1, 2, 3));
            points.AppendVertex(new Vector3d(4, 5, 6));
            MemoryStream ms = new MemoryStream();
            List<WriteMesh> meshes = new List<WriteMesh>() { new WriteMesh(a, "a"), new WriteMesh(empty, "empty"), new WriteMesh(points, "points") };
            Assert.Equal(IOCode.Ok, new GLTFWriter().WriteGLB(ms, meshes, WriteOptions.Defaults).code);
            ms.Position = 0;
            List<string> warnings = new List<string>();
            GLTFReader reader = new GLTFReader();
            reader.warningEvent += (msg, data) => warnings.Add(msg);
            DMesh3Builder builder = new DMesh3Builder();
            Assert.Equal(IOCode.Ok, reader.Read(ms, ReadOptions.Defaults, builder).code);
            // the empty mesh has no glTF mesh; the point set is written as point primitive, which the reader skips
            Assert.Equal(2, builder.Meshes.Count);
            Assert.Equal(a.TriangleCount, builder.Meshes[0].TriangleCount);
            Assert.Equal(0, builder.Meshes[1].VertexCount);
            Assert.Contains(warnings, w => w.Contains("point/line"));
        }

        [Fact]
        public void Read_NodeHierarchy_TRS_And_Matrix()
        {
            // parent: column-major matrix, rotate 90 deg about x then translate (10,0,0); child: rotate 90 deg about z, non-uniform scale (2,3,1), translate (0,0,5)
            double s = Math.Sqrt(0.5);
            string nodes =
                "\"scene\":0,\"scenes\":[{\"nodes\":[0]}]," +
                "\"nodes\":[" +
                "{\"matrix\":[1,0,0,0, 0,0,1,0, 0,-1,0,0, 10,0,0,1],\"children\":[1]}," +
                "{\"mesh\":0,\"rotation\":[0,0," + s.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    s.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "],\"scale\":[2,3,1],\"translation\":[0,0,5]}]";
            IOReadResult r;
            List<DMesh3> meshes = ReadJson(SingleTriangle(nodes), out r);
            Assert.True(r.code == IOCode.Ok, r.message);
            DMesh3 m = Assert.Single(meshes);
            // (1,0,0) -> scale (2,0,0) -> rotate (0,2,0) -> translate (0,2,5) -> parent (x,-z,y) + (10,0,0) = (10,-5,2)
            Assert.True(m.GetVertex(1).Distance(new Vector3d(10, -5, 2)) < 1e-6, m.GetVertex(1).ToString());
            // (0,1,0) -> scale (0,3,0) -> rotate (-3,0,0) -> (-3,0,5) -> parent (7,-5,0); scale is applied before rotation
            Assert.True(m.GetVertex(2).Distance(new Vector3d(7, -5, 0)) < 1e-6, m.GetVertex(2).ToString());

            // with ApplyNodeTransforms = false the raw positions are returned
            GLTFReader reader = new GLTFReader() { ApplyNodeTransforms = false };
            DMesh3Builder builder = new DMesh3Builder();
            reader.Read(new MemoryStream(Encoding.UTF8.GetBytes(SingleTriangle(nodes))), ReadOptions.Defaults, builder);
            Assert.Equal(new Vector3d(1, 0, 0), builder.Meshes[0].GetVertex(1));
        }

        [Fact]
        public void Read_NormalsUseInverseTransposeOfNodeTransform()
        {
            // sloped triangle with its face normal as vertex normals; non-uniform scale changes the normal direction
            Vector3d a = Vector3d.Zero, b = new Vector3d(1, 0, 1), c = new Vector3d(0, 1, 0);
            Vector3f n = (Vector3f)MathUtil.Normal(a, b, c);
            byte[] buffer = Floats(0, 0, 0, 1, 0, 1, 0, 1, 0).Concat(Floats(n.x, n.y, n.z, n.x, n.y, n.z, n.x, n.y, n.z)).ToArray();
            string json = "{\"asset\":{\"version\":\"2.0\"}," +
                "\"buffers\":[{\"byteLength\":72,\"uri\":\"" + DataUri(buffer) + "\"}]," +
                "\"bufferViews\":[{\"buffer\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":36}]," +
                "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}," +
                "{\"bufferView\":1,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}]," +
                "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1}}]}]," +
                "\"nodes\":[{\"mesh\":0,\"scale\":[1,1,4]}]}";
            IOReadResult r;
            DMesh3 m = ReadJson(json, out r)[0];
            Assert.True(r.code == IOCode.Ok, r.message);
            Vector3d expected = m.GetTriNormal(0);
            Assert.True(expected.Dot(n) < 0.99);     // the scale really changes the normal
            for (int vid = 0; vid < 3; ++vid)
                Assert.True(((Vector3d)m.GetVertexNormal(vid)).Distance(expected) < 1e-6, m.GetVertexNormal(vid).ToString());
        }

        [Fact]
        public void Read_MeshInstancedByTwoNodes()
        {
            string nodes = "\"nodes\":[{\"mesh\":0},{\"mesh\":0,\"translation\":[0,0,3]}]";
            IOReadResult r;
            List<DMesh3> meshes = ReadJson(SingleTriangle(nodes), out r);
            Assert.Equal(IOCode.Ok, r.code);
            Assert.Equal(2, meshes.Count);
            Assert.Equal(new Vector3d(0, 1, 3), meshes[1].GetVertex(2));
        }

        [Fact]
        public void Read_NegativeScale_FlipsWinding()
        {
            // closed tetrahedron, mirrored by the node
            float[] pos = { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            ushort[] idx = { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
            byte[] ib = new byte[idx.Length * 2];
            Buffer.BlockCopy(idx, 0, ib, 0, ib.Length);
            byte[] buffer = Floats(pos).Concat(ib).ToArray();
            Func<string, string> json = (node) => "{\"asset\":{\"version\":\"2.0\"}," +
                "\"buffers\":[{\"byteLength\":" + buffer.Length + ",\"uri\":\"" + DataUri(buffer) + "\"}]," +
                "\"bufferViews\":[{\"buffer\":0,\"byteLength\":48},{\"buffer\":0,\"byteOffset\":48,\"byteLength\":24}]," +
                "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":4,\"type\":\"VEC3\"}," +
                "{\"bufferView\":1,\"componentType\":5123,\"count\":12,\"type\":\"SCALAR\"}]," +
                "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1}]}]," +
                "\"nodes\":[" + node + "]}";
            IOReadResult r;
            DMesh3 plain = ReadJson(json("{\"mesh\":0}"), out r)[0];
            Assert.Equal(IOCode.Ok, r.code);
            DMesh3 mirrored = ReadJson(json("{\"mesh\":0,\"scale\":[-1,1,1]}"), out r)[0];
            Assert.Equal(IOCode.Ok, r.code);
            Assert.True(Volume(plain) > 0);
            Assert.Equal(Volume(plain), Volume(mirrored), 12);
        }

        [Fact]
        public void Read_InterleavedNormalizedAndStrips_WithTwoPrimitives()
        {
            // primitive 0: interleaved position (float3) + color (ubyte4 normalized), stride 16, triangle strip of 4 vertices
            // primitive 1: positions only, triangle fan with ubyte indices
            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);
            float[][] quad = { new float[] { 0, 0, 0 }, new float[] { 1, 0, 0 }, new float[] { 0, 1, 0 }, new float[] { 1, 1, 0 } };
            foreach (float[] p in quad) {
                bw.Write(p[0]); bw.Write(p[1]); bw.Write(p[2]);
                bw.Write((byte)255); bw.Write((byte)0); bw.Write((byte)51); bw.Write((byte)255);
            }
            int fanOffset = (int)ms.Length;   // 64
            float[] fan = { 0, 0, 2, 1, 0, 2, 1, 1, 2, 0, 1, 2 };
            foreach (float f in fan)
                bw.Write(f);
            int idxOffset = (int)ms.Length;   // 112
            foreach (byte i in new byte[] { 0, 1, 2, 3 })
                bw.Write(i);
            bw.Flush();
            byte[] buffer = ms.ToArray();
            string json = "{\"asset\":{\"version\":\"2.0\"}," +
                "\"buffers\":[{\"byteLength\":" + buffer.Length + ",\"uri\":\"" + DataUri(buffer) + "\"}]," +
                "\"bufferViews\":[{\"buffer\":0,\"byteLength\":64,\"byteStride\":16}," +
                "{\"buffer\":0,\"byteOffset\":" + fanOffset + ",\"byteLength\":48}," +
                "{\"buffer\":0,\"byteOffset\":" + idxOffset + ",\"byteLength\":4}]," +
                "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":4,\"type\":\"VEC3\"}," +
                "{\"bufferView\":0,\"byteOffset\":12,\"componentType\":5121,\"normalized\":true,\"count\":4,\"type\":\"VEC4\"}," +
                "{\"bufferView\":1,\"componentType\":5126,\"count\":4,\"type\":\"VEC3\"}," +
                "{\"bufferView\":2,\"componentType\":5121,\"count\":4,\"type\":\"SCALAR\"}]," +
                "\"meshes\":[{\"name\":\"two\",\"primitives\":[" +
                "{\"attributes\":{\"POSITION\":0,\"COLOR_0\":1},\"mode\":5}," +
                "{\"attributes\":{\"POSITION\":2},\"indices\":3,\"mode\":6}]}]}";
            IOReadResult r;
            List<DMesh3> meshes = ReadJson(json, out r);
            Assert.True(r.code == IOCode.Ok, r.message);
            DMesh3 m = Assert.Single(meshes);
            Assert.Equal(8, m.VertexCount);
            Assert.Equal(4, m.TriangleCount);
            Assert.True(m.HasTriangleGroups);
            Assert.Equal(new[] { 0, 0, 1, 1 }, m.TriangleIndices().Select(t => m.GetTriangleGroup(t)).ToArray());
            Assert.Equal(new Vector3f(1, 0, 0.2f), m.GetVertexColor(0));
            Assert.Equal(Vector3f.One, m.GetVertexColor(5));     // primitive without colors gets white
            // all triangles face +z
            foreach (int tid in m.TriangleIndices())
                Assert.True(m.GetTriNormal(tid).z > 0.99);
            Assert.Equal(2.0, MeshMeasurements.AreaT(m, m.TriangleIndices()), 6);
        }

        [Fact]
        public void Read_SparseAccessor()
        {
            // base positions all zero (no bufferView), sparse values set vertices 1 and 2
            uint[] sidx = { 1, 2 };
            byte[] ib = new byte[8];
            Buffer.BlockCopy(sidx, 0, ib, 0, 8);
            byte[] buffer = ib.Concat(Floats(1, 0, 0, 0, 1, 0)).ToArray();
            string json = "{\"asset\":{\"version\":\"2.0\"}," +
                "\"buffers\":[{\"byteLength\":" + buffer.Length + ",\"uri\":\"" + DataUri(buffer) + "\"}]," +
                "\"bufferViews\":[{\"buffer\":0,\"byteLength\":8},{\"buffer\":0,\"byteOffset\":8,\"byteLength\":24}]," +
                "\"accessors\":[{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"sparse\":{\"count\":2," +
                "\"indices\":{\"bufferView\":0,\"componentType\":5125},\"values\":{\"bufferView\":1}}}]," +
                "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0}}]}]}";
            IOReadResult r;
            DMesh3 m = ReadJson(json, out r)[0];
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(new Vector3d(0, 1, 0), m.GetVertex(2));
            Assert.Equal(1, m.TriangleCount);
        }

        [Fact]
        public void Read_ExternalBuffer_FromFile()
        {
            string dir = TempDir();
            try {
                File.WriteAllBytes(Path.Combine(dir, "tri data.bin"), Floats(TriPositions));
                string json = SingleTriangle("\"nodes\":[{\"mesh\":0}]").Replace(DataUri(Floats(TriPositions)), "tri%20data.bin");
                string path = Path.Combine(dir, "tri.gltf");
                File.WriteAllText(path, json);
                IOReadResult r;
                List<DMesh3> meshes = ReadAll(path, out r);
                Assert.True(r.code == IOCode.Ok, r.message);
                Assert.Equal(new Vector3d(1, 0, 0), meshes[0].GetVertex(1));

                // from a stream, the external file cannot be resolved
                DMesh3Builder builder = new DMesh3Builder();
                r = StandardMeshReader.ReadFile(new MemoryStream(Encoding.UTF8.GetBytes(json)), "gltf", ReadOptions.Defaults, builder);
                Assert.Equal(IOCode.FileAccessError, r.code);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Read_ExternalBuffer_AbsolutePathsAndSchemesAreNotRead()
        {
            string dir = TempDir();
            string other = TempDir();
            try {
                string target = Path.Combine(other, "tri.bin");
                File.WriteAllBytes(target, Floats(TriPositions));
                foreach (string uri in new[] { target, new Uri(target).AbsoluteUri, "http://example.com/tri.bin" }) {
                    string json = SingleTriangle("\"nodes\":[{\"mesh\":0}]").Replace(DataUri(Floats(TriPositions)), uri.Replace("\\", "/"));
                    string path = Path.Combine(dir, "tri.gltf");
                    File.WriteAllText(path, json);
                    IOReadResult r;
                    ReadAll(path, out r);
                    Assert.True(r.code == IOCode.FileAccessError, uri + ": " + r.code);
                }
            } finally {
                Directory.Delete(dir, true);
                Directory.Delete(other, true);
            }
        }

        [Fact]
        public void Read_ExternalBuffer_PathsLeavingTheDirectoryAreNotRead()
        {
            // root/model/tri.gltf may read root/model/sub/tri.bin, but not root/other/tri.bin via ../
            string root = TempDir();
            try {
                string modelDir = Path.Combine(root, "model"), sub = Path.Combine(modelDir, "sub"), other = Path.Combine(root, "other");
                Directory.CreateDirectory(sub);
                Directory.CreateDirectory(other);
                File.WriteAllBytes(Path.Combine(sub, "tri.bin"), Floats(TriPositions));
                File.WriteAllBytes(Path.Combine(other, "tri.bin"), Floats(TriPositions));
                string path = Path.Combine(modelDir, "tri.gltf");
                foreach (string uri in new[] { "../other/tri.bin", "sub/../../other/tri.bin", "..%2Fother%2Ftri.bin", "./../other/tri.bin" }) {
                    File.WriteAllText(path, SingleTriangle("\"nodes\":[{\"mesh\":0}]").Replace(DataUri(Floats(TriPositions)), uri));
                    IOReadResult r;
                    ReadAll(path, out r);
                    Assert.True(r.code == IOCode.FileAccessError, uri + ": " + r.code + " " + r.message);
                }
                foreach (string uri in new[] { "sub/tri.bin", "./sub/tri.bin", "sub/../sub/tri.bin" }) {
                    File.WriteAllText(path, SingleTriangle("\"nodes\":[{\"mesh\":0}]").Replace(DataUri(Floats(TriPositions)), uri));
                    IOReadResult r;
                    List<DMesh3> meshes = ReadAll(path, out r);
                    Assert.True(r.code == IOCode.Ok, uri + ": " + r.message);
                    Assert.Equal(new Vector3d(1, 0, 0), meshes[0].GetVertex(1));
                }
            } finally {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Read_DracoRequired_NotSupported_OtherExtensionsIgnored()
        {
            IOReadResult r;
            ReadJson(SingleTriangle("\"nodes\":[{\"mesh\":0}]", "\"extensionsRequired\":[\"KHR_draco_mesh_compression\"],"), out r);
            Assert.Equal(IOCode.FormatNotSupportedError, r.code);
            List<DMesh3> meshes = ReadJson(SingleTriangle("\"nodes\":[{\"mesh\":0}]", "\"extensionsRequired\":[\"KHR_materials_unlit\"],"), out r);
            Assert.Equal(IOCode.Ok, r.code);
            Assert.Single(meshes);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[1,2,3]")]
        [InlineData("{\"asset\":{\"version\":\"1.0\"}}")]
        [InlineData("{\"asset\":{\"version\":\"2.0\"},\"meshes\":[{\"primitives\":[{\"attributes\":{}}]}]}")]
        [InlineData("{\"asset\":{\"version\":\"2.0\"},\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":5}}]}]}")]
        [InlineData("{\"asset\":{\"version\":\"2.0\"},\"nodes\":[{\"mesh\":3}]}")]
        [InlineData("{\"asset\":{\"version\":\"2.0\"},\"nodes\":[{\"children\":[0]}],\"scenes\":[{\"nodes\":[0]}]}")]
        public void Read_InvalidFiles_ReturnError(string json)
        {
            IOReadResult r;
            ReadJson(json, out r);
            Assert.NotEqual(IOCode.Ok, r.code);
        }

        [Fact]
        public void Read_SharedChildNodes_ReturnError_InsteadOfExponentialExpansion()
        {
            // 40 nodes, each with two references to the next one: < 1 KB of JSON, 2^40 paths to the mesh node.
            // The node hierarchy must be a tree, so the second reference is an error.
            const int depth = 40;
            string chain = string.Join(",", Enumerable.Range(0, depth).Select(i => "{\"children\":[" + (i + 1) + "," + (i + 1) + "]}"));
            string json = SingleTriangle("\"scenes\":[{\"nodes\":[0]}],\"nodes\":[" + chain + ",{\"mesh\":0}]");
            IOReadResult r = default(IOReadResult);
            System.Threading.Tasks.Task task = System.Threading.Tasks.Task.Run(() => ReadJson(json, out r));
            Assert.True(task.Wait(TimeSpan.FromSeconds(30)), "reading did not finish");
            Assert.Equal(IOCode.FileParsingError, r.code);
            Assert.Contains("more than once", r.message);
        }

        [Theory]
        [InlineData("\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"children\":[1,1]},{\"mesh\":0}]")]       // same child twice
        [InlineData("\"scenes\":[{\"nodes\":[0,1]}],\"nodes\":[{\"children\":[1]},{\"mesh\":0}]")]       // scene root that is also a child
        [InlineData("\"scenes\":[{\"nodes\":[0,2]}],\"nodes\":[{\"children\":[1]},{\"mesh\":0},{\"children\":[1]}]")]   // two parents
        public void Read_NodeWithTwoParents_ReturnsError(string nodes)
        {
            IOReadResult r;
            List<DMesh3> meshes = ReadJson(SingleTriangle(nodes), out r);
            Assert.Equal(IOCode.FileParsingError, r.code);
        }

        [Fact]
        public void Read_SiblingSubtrees_StillRead()
        {
            // two separate subtrees that instance the same mesh are valid (no node is shared)
            string nodes = "\"scenes\":[{\"nodes\":[0,2]}],\"nodes\":[{\"children\":[1]},{\"mesh\":0},{\"children\":[3],\"translation\":[0,0,3]},{\"mesh\":0}]";
            IOReadResult r;
            List<DMesh3> meshes = ReadJson(SingleTriangle(nodes), out r);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(2, meshes.Count);
            Assert.Equal(new Vector3d(0, 1, 3), meshes[1].GetVertex(2));
        }

        [Fact]
        public void Read_AccessorPastBufferEnd_ReturnsError()
        {
            string json = SingleTriangle("\"nodes\":[{\"mesh\":0}]").Replace("\"count\":3", "\"count\":4");
            IOReadResult r;
            ReadJson(json, out r);
            Assert.Equal(IOCode.FileParsingError, r.code);
        }

        [Fact]
        public void Read_TruncatedGLB_ReturnsError()
        {
            MemoryStream ms = new MemoryStream();
            new GLTFWriter().WriteGLB(ms, new List<WriteMesh>() { new WriteMesh(AttributeSphere(Vector3d.Zero)) }, WriteOptions.Defaults);
            byte[] b = ms.ToArray();
            DMesh3Builder builder = new DMesh3Builder();
            IOReadResult r = new GLTFReader().Read(new MemoryStream(b.Take(b.Length / 2).ToArray()), ReadOptions.Defaults, builder);
            Assert.Equal(IOCode.FileParsingError, r.code);
        }
    }


    public class MiniJsonTests
    {
        [Fact]
        public void Parse_Values()
        {
            var obj = (Dictionary<string, object>)MiniJson.Parse(
                " {\"a\": [1, -2.5e3, true, false, null], \"b\": {\"c\": \"x\\\"y\\\\z\\n\\u00e9\\/\"}, \"e\": {}, \"f\": []} ");
            var a = (List<object>)obj["a"];
            Assert.Equal(new object[] { 1.0, -2500.0, true, false, null }, a.ToArray());
            Assert.Equal("x\"y\\z\né/", ((Dictionary<string, object>)obj["b"])["c"]);
            Assert.Empty((Dictionary<string, object>)obj["e"]);
            Assert.Empty((List<object>)obj["f"]);
        }

        [Fact]
        public void Quote_RoundTrips()
        {
            string s = "tab\t \"q\" back\\slash \u0001 newline\n ümlaut";
            Assert.Equal(s, MiniJson.Parse(MiniJson.Quote(s)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("{\"a\" 1}")]
        [InlineData("[1,]")]
        [InlineData("\"unterminated")]
        [InlineData("tru")]
        [InlineData("1 2")]
        [InlineData("\"bad \\x escape\"")]
        public void Parse_Invalid_Throws(string json)
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse(json));
        }

        [Fact]
        public void Parse_DeepNesting_Throws()
        {
            string json = new string('[', 1000) + new string(']', 1000);
            Assert.Throws<FormatException>(() => MiniJson.Parse(json));
        }
    }
}
