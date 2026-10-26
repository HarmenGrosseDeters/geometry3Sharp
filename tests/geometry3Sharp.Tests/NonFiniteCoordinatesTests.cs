using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Readers reject vertex positions with NaN or infinite coordinates (FileParsingError) unless
    /// ReadOptions.AllowNonFiniteCoordinates is set. Found by the reader fuzzing: STL, OFF, binary PLY and glB reported
    /// Ok for such files, and every later operation then works on NaN.
    /// </summary>
    public class NonFiniteCoordinatesTests
    {
        const double Marker = 12345.5;      // exact in float and double, easy to find in the written file

        /// <summary>A file of the format whose marker coordinate is replaced by NaN (or infinity).</summary>
        static byte[] FileWithNonFinite(string extension, bool binary, bool infinity)
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 3 }.Generate().MakeDMesh();
            int vid = mesh.VertexIndices().First();
            Vector3d v = mesh.GetVertex(vid);
            mesh.SetVertex(vid, new Vector3d(Marker, v.y, v.z));
            byte[] data;
            string dir = MeshFormatTestUtil.TempDir();
            try {
                string path = Path.Combine(dir, "mesh." + extension);
                WriteOptions options = WriteOptions.Defaults;
                options.bWriteBinary = binary;
                Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, mesh, options).code);
                data = File.ReadAllBytes(path);
            } finally {
                Directory.Delete(dir, true);
            }
            int replaced = 0;
            // binary: the marker as float or double
            replaced += ReplaceAll(ref data, BitConverter.GetBytes((float)Marker), BitConverter.GetBytes(infinity ? float.PositiveInfinity : float.NaN));
            replaced += ReplaceAll(ref data, BitConverter.GetBytes(Marker), BitConverter.GetBytes(infinity ? double.PositiveInfinity : double.NaN));
            // glB: positions are written relative to the bounds center (the node translation), as floats
            if (replaced == 0) {
                double cx = mesh.CachedBounds.Center.x;
                replaced += ReplaceAll(ref data, BitConverter.GetBytes((float)(Marker - cx)), BitConverter.GetBytes(infinity ? float.PositiveInfinity : float.NaN));
            }
            // text: the whole number token of the marker, however it is formatted
            if (replaced == 0) {
                string text = Encoding.ASCII.GetString(data);
                var token = new System.Text.RegularExpressions.Regex(@"(?<![\d.])(12345\.50*|1\.234550*[eE]\+0*4)(?![\d.])");
                replaced = token.Matches(text).Count;
                data = Encoding.ASCII.GetBytes(token.Replace(text, infinity ? "1e400" : "NaN"));
            }
            Assert.True(replaced > 0, extension + ": marker not found in the written file");
            return data;
        }

        static int ReplaceAll(ref byte[] data, byte[] find, byte[] with)
        {
            int count = 0;
            for (int i = 0; i + find.Length <= data.Length; ++i) {
                bool match = true;
                for (int k = 0; k < find.Length && match; ++k)
                    match = data[i + k] == find[k];
                if (match) {
                    Array.Copy(with, 0, data, i, with.Length);
                    count++;
                }
            }
            return count;
        }

        public static IEnumerable<object[]> Formats()
        {
            foreach (bool infinity in new[] { false, true }) {
                yield return new object[] { "obj", false, infinity };
                yield return new object[] { "off", false, infinity };
                yield return new object[] { "stl", false, infinity };
                yield return new object[] { "stl", true, infinity };
                yield return new object[] { "ply", false, infinity };
                yield return new object[] { "ply", true, infinity };
                yield return new object[] { "glb", true, infinity };
            }
        }

        static IOReadResult Read(byte[] data, string extension, bool allow, out DMesh3Builder builder)
        {
            builder = new DMesh3Builder();
            var options = new ReadOptions() { AllowNonFiniteCoordinates = allow };
            return StandardMeshReader.ReadFile(new MemoryStream(data, false), extension, options, builder);
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void NonFiniteCoordinate_IsAParseError_UnlessAllowed(string extension, bool binary, bool infinity)
        {
            byte[] data = FileWithNonFinite(extension, binary, infinity);
            DMesh3Builder builder;
            IOReadResult r = Read(data, extension, false, out builder);
            Assert.Equal(IOCode.FileParsingError, r.code);
            Assert.Contains("non-finite", r.message);
            // also with the defaults
            builder = new DMesh3Builder();
            Assert.Equal(IOCode.FileParsingError, StandardMeshReader.ReadFile(new MemoryStream(data, false), extension, ReadOptions.Defaults, builder).code);

            // the opt-out keeps the vertex (the behavior of 1.0.324)
            r = Read(data, extension, true, out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Contains(builder.Meshes.SelectMany(m => m.Vertices()), v => v.IsFinite == false);
        }

        [Fact]
        public void ThreeMF_NonFiniteCoordinate_IsAParseError_UnlessAllowed()
        {
            // the 3MF writer zips the model: patch the XML in a package of our own
            string model = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\"><resources><object id=\"1\" type=\"model\"><mesh><vertices>"
                + "<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"NaN\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/>"
                + "</vertices><triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles></mesh></object></resources><build><item objectid=\"1\"/></build></model>";
            var stream = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true)) {
                using (var w = new StreamWriter(zip.CreateEntry("3D/3dmodel.model").Open()))
                    w.Write(model);
                using (var w = new StreamWriter(zip.CreateEntry("_rels/.rels").Open()))
                    w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Target=\"/3D/3dmodel.model\" Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>");
            }
            byte[] data = stream.ToArray();
            DMesh3Builder builder;
            IOReadResult r = Read(data, "3mf", false, out builder);
            Assert.Equal(IOCode.FileParsingError, r.code);
            Assert.Contains("non-finite", r.message);
            r = Read(data, "3mf", true, out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Contains(builder.Meshes.Single().Vertices(), v => double.IsNaN(v.x));
        }

        [Fact]
        public void FiniteFiles_ReadAsBefore()
        {
            // a marker that is not replaced: every format reads with the defaults
            foreach (object[] f in Formats().Where(f => (bool)f[2] == false)) {
                string extension = (string)f[0];
                bool binary = (bool)f[1];
                DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 3 }.Generate().MakeDMesh();
                DMesh3 read = MeshFormatTestUtil.WriteRead(mesh, extension, new WriteOptions() { bWriteBinary = binary });
                Assert.Equal(mesh.TriangleCount, read.TriangleCount);
            }
        }

        static IOReadResult ReadText(MeshFormatReader reader, string text, bool allow, out DMesh3Builder builder)
        {
            builder = new DMesh3Builder();
            return reader.ReadFile(new MemoryStream(Encoding.ASCII.GetBytes(text), false), builder,
                new ReadOptions() { AllowNonFiniteCoordinates = allow }, null);
        }

        [Theory]
        [InlineData("NaN")]
        [InlineData("1e400")]
        [InlineData("-1e400")]
        [InlineData("Infinity")]
        public void OBJ_VertexWithColors_IsCheckedToo(string token)
        {
            // the 7-token vertex line (position and color)
            string obj = "v 0 0 0 1 0 0\nv " + token + " 0 0 0 1 0\nv 0 1 0 0 0 1\nf 1 2 3\n";
            DMesh3Builder builder;
            Assert.Equal(IOCode.FileParsingError, ReadText(new OBJFormatReader(), obj, false, out builder).code);
            IOReadResult r = ReadText(new OBJFormatReader(), obj, true, out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.Equal(3, builder.Meshes.Single().VertexCount);      // (not skipped: the indices stay as written)
        }

        [Fact]
        public void PLY_TextReaderEntryPoint_HonorsTheOptions()
        {
            string ply = "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\n"
                + "element face 1\nproperty list uchar int vertex_indices\nend_header\n0 0 0\n1e400 0 0\n0 1 0\n3 0 1 2\n";
            var builder = new DMesh3Builder();
            IOReadResult r = new PLYReader().Read(new StringReader(ply), new ReadOptions(), builder);
            Assert.Equal(IOCode.FileParsingError, r.code);
            builder = new DMesh3Builder();
            r = new PLYReader().Read(new StringReader(ply), new ReadOptions() { AllowNonFiniteCoordinates = true }, builder);
            Assert.True(r.code == IOCode.Ok, r.message);
            Assert.True(double.IsPositiveInfinity(builder.Meshes.Single().GetVertex(1).x));
        }

        [Fact]
        public void ParseCoordinate_BeyondTheDoubleRange_IsInfinityOnEveryFramework()
        {
            // (.NET Framework throws OverflowException, .NET Core returns infinity: the readers see the same value)
            Assert.Equal(double.PositiveInfinity, MeshIOUtil.ParseDouble("1e400"));
            Assert.Equal(double.NegativeInfinity, MeshIOUtil.ParseDouble(" -1e400"));
            Assert.Equal(float.PositiveInfinity, MeshIOUtil.ParseFloat("1e40"));
            Assert.Equal(float.NegativeInfinity, MeshIOUtil.ParseFloat("-1e40"));
            Assert.Equal(1.5, MeshIOUtil.ParseDouble("1.5"));
            Assert.True(double.IsNaN(MeshIOUtil.ParseDouble("NaN")));
            Assert.Throws<FormatException>(() => MeshIOUtil.ParseDouble("1,5x"));
        }

        static byte[] ThreeMF(string vertices, string components = null)
        {
            string obj1 = "<object id=\"1\" type=\"model\"><mesh><vertices>" + vertices
                + "</vertices><triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles></mesh></object>";
            string obj2 = (components != null) ? "<object id=\"2\" type=\"model\"><components>" + components + "</components></object>" : "";
            string model = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\"><resources>"
                + obj1 + obj2 + "</resources><build><item objectid=\"" + (components != null ? "2" : "1") + "\"/></build></model>";
            var stream = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true)) {
                using (var w = new StreamWriter(zip.CreateEntry("3D/3dmodel.model").Open()))
                    w.Write(model);
                using (var w = new StreamWriter(zip.CreateEntry("_rels/.rels").Open()))
                    w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Target=\"/3D/3dmodel.model\" Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>");
            }
            return stream.ToArray();
        }

        [Theory]
        [InlineData("1e400")]
        [InlineData("-Infinity")]
        public void ThreeMF_InfiniteCoordinate_IsAParseError(string token)
        {
            byte[] data = ThreeMF("<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"" + token + "\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/>");
            DMesh3Builder builder;
            Assert.Equal(IOCode.FileParsingError, Read(data, "3mf", false, out builder).code);
        }

        [Fact]
        public void ThreeMF_FinitePositionMadeInfiniteByAComponentTransform_IsAParseError()
        {
            // the check is on the final position: a finite vertex scaled by 1e300 twice
            byte[] data = ThreeMF("<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1e10\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/>",
                "<component objectid=\"1\" transform=\"1e300 0 0 0 1 0 0 0 1 0 0 0\"/>");
            DMesh3Builder builder;
            IOReadResult r = Read(data, "3mf", false, out builder);
            Assert.Equal(IOCode.FileParsingError, r.code);
            Assert.Contains("non-finite", r.message);
            r = Read(data, "3mf", true, out builder);
            Assert.True(r.code == IOCode.Ok, r.message);
        }
    }
}
