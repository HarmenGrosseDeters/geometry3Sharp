using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using g3;
using Xunit;
using static geometry3Sharp.Tests.MeshFormatTestUtil;

namespace geometry3Sharp.Tests
{
    public class PLYFormatTests
    {
        static List<DMesh3> ReadBytes(byte[] bytes, out IOReadResult result)
        {
            DMesh3Builder builder = new DMesh3Builder();
            result = StandardMeshReader.ReadFile(new MemoryStream(bytes), "ply", ReadOptions.Defaults, builder);
            return builder.Meshes;
        }

        static List<DMesh3> ReadText(string text, out IOReadResult result)
        {
            return ReadBytes(Encoding.ASCII.GetBytes(text), out result);
        }


        [Fact]
        public void Ascii_RoundTrip_IsExact()
        {
            DMesh3 mesh = AttributeSphere(new Vector3d(1.0 / 3.0, -2.5, 1e-7));
            DMesh3 read = WriteRead(mesh, "ply", AllAttributes(false));
            AssertSameTopology(mesh, read);
            Assert.Equal(0.0, MaxPositionError(mesh, read));
            Assert.True(read.HasVertexNormals && read.HasVertexColors && read.HasVertexUVs);
            Assert.Equal(0.0, MaxAttributeError(mesh, read, (m, v) => m.GetVertexNormal(v)));
            Assert.Equal(0.0, MaxUVError(mesh, read));
            // colors are stored as bytes
            Assert.True(MaxAttributeError(mesh, read, (m, v) => m.GetVertexColor(v)) <= 0.5 / 255.0 * Math.Sqrt(3) + 1e-6);
        }

        [Fact]
        public void Binary_RoundTrip_Double()
        {
            DMesh3 mesh = AttributeSphere(new Vector3d(500000.123456789, 5700000.987654321, 100));
            DMesh3 read = WriteRead(mesh, "ply", AllAttributes(true));
            AssertSameTopology(mesh, read);
            Assert.Equal(0.0, MaxPositionError(mesh, read));
            Assert.Equal(0.0, MaxAttributeError(mesh, read, (m, v) => m.GetVertexNormal(v)));
            Assert.Equal(0.0, MaxUVError(mesh, read));
        }

        [Fact]
        public void Binary_RoundTrip_Float()
        {
            DMesh3 mesh = AttributeSphere(new Vector3d(1, 2, 3));
            WriteOptions options = AllAttributes(true);
            options.RealPrecisionDigits = 7;
            DMesh3 read = WriteRead(mesh, "ply", options);
            AssertSameTopology(mesh, read);
            double err = MaxPositionError(mesh, read);
            Assert.True(err > 0 && err < 1e-6, "float error " + err);
        }

        [Fact]
        public void Write_OnlyRequestedAttributes()
        {
            DMesh3 mesh = AttributeSphere(Vector3d.Zero);
            WriteOptions options = WriteOptions.Defaults;
            DMesh3 read = WriteRead(mesh, "ply", options);
            Assert.False(read.HasVertexNormals);
            Assert.False(read.HasVertexColors);
            Assert.False(read.HasVertexUVs);
        }

        [Fact]
        public void Write_NonCompactMesh()
        {
            DMesh3 mesh = AttributeSphere(Vector3d.Zero);
            // remove some triangles and their vertices, leaving gaps in the index space
            foreach (int tid in mesh.TriangleIndices().Where(t => t % 5 == 0).ToArray())
                mesh.RemoveTriangle(tid, true, false);
            Assert.False(mesh.IsCompact);
            foreach (bool binary in new[] { false, true }) {
                DMesh3 read = WriteRead(mesh, "ply", AllAttributes(binary));
                AssertSameTopology(mesh, read);
                Assert.Equal(0.0, MaxPositionError(mesh, read));
            }
        }

        [Fact]
        public void Write_MultipleMeshes_AreCombined()
        {
            DMesh3 a = AttributeSphere(Vector3d.Zero), b = AttributeSphere(new Vector3d(5, 0, 0));
            string dir = TempDir();
            try {
                string path = Path.Combine(dir, "two.ply");
                Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMeshes(path, new List<DMesh3>() { a, b }, WriteOptions.Defaults).code);
                IOReadResult r;
                List<DMesh3> meshes = ReadAll(path, out r);
                Assert.Equal(IOCode.Ok, r.code);
                Assert.Single(meshes);
                Assert.Equal(a.TriangleCount + b.TriangleCount, meshes[0].TriangleCount);
                MeshConnectedComponents comps = new MeshConnectedComponents(meshes[0]);
                comps.FindConnectedT();
                Assert.Equal(2, comps.Count);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void PointCloud_RoundTrip()
        {
            DMesh3 points = new DMesh3();
            for (int i = 0; i < 10; ++i)
                points.AppendVertex(new Vector3d(i, i * i, -i));
            foreach (bool binary in new[] { false, true }) {
                WriteOptions options = WriteOptions.Defaults;
                options.bWriteBinary = binary;
                DMesh3 read = WriteRead(points, "ply", options);
                Assert.Equal(10, read.VertexCount);
                Assert.Equal(0, read.TriangleCount);
                Assert.Equal(new Vector3d(9, 81, -9), read.GetVertex(9));
            }
        }

        [Fact]
        public void Ascii_Write_UsesInvariantCulture()
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                DMesh3 mesh = AttributeSphere(new Vector3d(0.25, 0.5, 0.75));
                StringWriter sw = new StringWriter();
                new PLYWriter().Write(sw, new List<WriteMesh>() { new WriteMesh(mesh) }, WriteOptions.Defaults);
                Assert.DoesNotContain(",", sw.ToString());
                IOReadResult r;
                List<DMesh3> meshes = ReadText(sw.ToString(), out r);
                Assert.Equal(IOCode.Ok, r.code);
                Assert.Equal(0.0, MaxPositionError(mesh, meshes[0]));
            } finally {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [Fact]
        public void Ascii_Read_PolygonsCommentsAndByteColors()
        {
            string ply =
                "ply\r\n" +
                "format ascii 1.0\r\n" +
                "comment a quad and a pentagon\r\n" +
                "obj_info generated by hand\r\n" +
                "element vertex 7\r\n" +
                "property float x\nproperty float y\nproperty float z\n" +
                "property uchar red\nproperty uchar green\nproperty uchar blue\n" +
                "element face 2\n" +
                "property list uchar int vertex_index\n" +
                "end_header\n" +
                "0 0 0 255 0 0\n1 0 0 0 255 0\n1 1 0 0 0 255\n0 1 0 255 255 255\n" +
                "2 0 0 0 0 0\n3 0 0 0 0 0\n2.5 1 0 0 0 0\n" +
                "4 0 1 2 3\n" +
                "5 1 4 5 6 2\n";
            IOReadResult r;
            List<DMesh3> meshes = ReadText(ply, out r);
            Assert.True(r.code == IOCode.Ok, r.message);
            DMesh3 m = meshes[0];
            Assert.Equal(7, m.VertexCount);
            Assert.Equal(2 + 3, m.TriangleCount);
            Assert.True(m.HasVertexColors);
            Assert.Equal(new Vector3f(1, 0, 0), m.GetVertexColor(0));
            Assert.Equal(new Vector3f(1, 1, 1), m.GetVertexColor(3));
            Assert.Equal(1.0 + 1.75, MeshMeasurements.AreaT(m, m.TriangleIndices()), 6);
        }

        [Fact]
        public void BinaryBigEndian_Read_SkipsOtherElementsAndProperties()
        {
            MemoryStream ms = new MemoryStream();
            byte[] header = Encoding.ASCII.GetBytes(
                "ply\n" +
                "format binary_big_endian 1.0\n" +
                "element vertex 4\n" +
                "property double x\nproperty double y\nproperty double z\n" +
                "property short quality\n" +
                "element edge 1\n" +
                "property int vertex1\nproperty int vertex2\n" +
                "element face 2\n" +
                "property uchar flags\n" +
                "property list uchar uint vertex_indices\n" +
                "property list uchar float texcoord\n" +
                "end_header\n");
            ms.Write(header, 0, header.Length);
            Action<byte[]> writeBE = (b) => { Array.Reverse(b); ms.Write(b, 0, b.Length); };
            double[][] verts = { new double[] { 0, 0, 0 }, new double[] { 2, 0, 0 }, new double[] { 2, 3, 0 }, new double[] { 0, 3, 0 } };
            foreach (double[] v in verts) {
                foreach (double d in v)
                    writeBE(BitConverter.GetBytes(d));
                writeBE(BitConverter.GetBytes((short)7));
            }
            writeBE(BitConverter.GetBytes(0));
            writeBE(BitConverter.GetBytes(1));
            uint[][] faces = { new uint[] { 0, 1, 2 }, new uint[] { 0, 2, 3 } };
            foreach (uint[] f in faces) {
                ms.WriteByte(0xAB);
                ms.WriteByte(3);
                foreach (uint i in f)
                    writeBE(BitConverter.GetBytes(i));
                ms.WriteByte(2);
                writeBE(BitConverter.GetBytes(0.5f));
                writeBE(BitConverter.GetBytes(0.25f));
            }

            IOReadResult r;
            List<DMesh3> meshes = ReadBytes(ms.ToArray(), out r);
            Assert.True(r.code == IOCode.Ok, r.message);
            DMesh3 m = meshes[0];
            Assert.Equal(4, m.VertexCount);
            Assert.Equal(2, m.TriangleCount);
            Assert.Equal(new Vector3d(2, 3, 0), m.GetVertex(2));
            Assert.Equal(6.0, MeshMeasurements.AreaT(m, m.TriangleIndices()), 12);
        }

        [Fact]
        public void Read_SkipsInvalidFaces()
        {
            string ply = "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\n" +
                "element face 3\nproperty list uchar int vertex_indices\nend_header\n" +
                "0 0 0\n1 0 0\n0 1 0\n" +
                "3 0 1 2\n" +   // ok
                "2 0 1\n" +     // too few vertices
                "3 0 1 9\n";    // index out of range
            List<string> warnings = new List<string>();
            PLYReader reader = new PLYReader();
            reader.warningEvent += (msg, data) => warnings.Add(msg);
            DMesh3Builder builder = new DMesh3Builder();
            IOReadResult r = reader.Read(new BinaryReader(new MemoryStream(Encoding.ASCII.GetBytes(ply))), ReadOptions.Defaults, builder);
            Assert.Equal(IOCode.Ok, r.code);
            Assert.Equal(1, builder.Meshes[0].TriangleCount);
            Assert.Contains(warnings, w => w.Contains("skipped 2"));
        }

        [Fact]
        public void TextReader_ReadsAscii_RejectsBinary()
        {
            string ply = "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\n" +
                "element face 1\nproperty list uchar int vertex_indices\nend_header\n0 0 0\n1 0 0\n0 1 0\n3 0 1 2\n";
            DMesh3Builder builder = new DMesh3Builder();
            Assert.Equal(IOCode.Ok, new PLYReader().Read(new StringReader(ply), ReadOptions.Defaults, builder).code);
            Assert.Equal(1, builder.Meshes[0].TriangleCount);

            string bin = ply.Replace("format ascii", "format binary_little_endian");
            Assert.Equal(IOCode.FormatNotSupportedError, new PLYReader().Read(new StringReader(bin), ReadOptions.Defaults, new DMesh3Builder()).code);
        }

        [Theory]
        [InlineData("solid foo\nendsolid\n")]                                       // not PLY
        [InlineData("ply\nelement vertex 1\nproperty float x\nend_header\n0\n")]   // no format line
        [InlineData("ply\nformat ascii 1.0\nelement vertex 1\nproperty float x\nend_header\n0\n")]  // no y, z
        [InlineData("ply\nformat ascii 1.0\nelement vertex 2\nproperty float x\nproperty float y\nproperty float z\nend_header\n0 0 0\n1 1\n")]  // truncated
        [InlineData("ply\nformat ascii 1.0\nelement vertex 1\nproperty int128 x\nend_header\n0\n")]  // unknown type
        [InlineData("ply\nformat ascii 1.0\nelement vertex 1\nproperty float x\n")]  // no end_header
        public void Read_InvalidFiles_ReturnError(string text)
        {
            IOReadResult r;
            ReadText(text, out r);
            Assert.NotEqual(IOCode.Ok, r.code);
        }

        [Fact]
        public void Read_TruncatedBinary_ReturnsError()
        {
            DMesh3 mesh = AttributeSphere(Vector3d.Zero);
            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);
            new PLYWriter().Write(bw, new List<WriteMesh>() { new WriteMesh(mesh) }, WriteOptions.Defaults);
            bw.Flush();
            byte[] bytes = ms.ToArray();
            IOReadResult r;
            ReadBytes(bytes.Take(bytes.Length - 10).ToArray(), out r);
            Assert.Equal(IOCode.FileParsingError, r.code);
        }
    }
}
