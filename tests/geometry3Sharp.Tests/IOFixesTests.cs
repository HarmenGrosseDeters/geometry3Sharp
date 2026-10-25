using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// I/O bugs from the class-by-class review (docs/Analyse-2026-10.md):
    /// locale-dependent parsing/formatting, unflushed .mtl file, leaked .mtl handle,
    /// truncated zlib decompression and worker-thread exceptions in ParallelStream.
    /// </summary>
    public class IOFixesTests
    {
        /// <summary>
        /// Runs f with a locale that uses ',' as decimal separator (and '.' as group separator).
        /// </summary>
        static T WithGermanCulture<T>(Func<T> f)
        {
            var current = Thread.CurrentThread.CurrentCulture;
            try {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                return f();
            } finally {
                Thread.CurrentThread.CurrentCulture = current;
            }
        }

        static DMesh3 Triangle()
        {
            var mesh = new DMesh3();
            mesh.AppendTriangle(mesh.AppendVertex(new Vector3d(1.5, 2.25, 3)), mesh.AppendVertex(new Vector3d(0.5, 0, 0)), mesh.AppendVertex(new Vector3d(0, 0.5, 0)));
            return mesh;
        }

        static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "g3_io_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // --- readers parse with InvariantCulture ---

        [Fact]
        public void OBJReader_ParsesDecimalPointUnderGermanLocale()
        {
            string obj = "v 1.5 2.25 3\nv 0.5 0 0\nv 0 0.5 0\nvn 0 0 1\nvt 0.25 0.75\nf 1 2 3\n";
            var builder = new DMesh3Builder();
            IOReadResult result = WithGermanCulture(() => new OBJReader().Read(new StringReader(obj), ReadOptions.Defaults, builder));
            Assert.Equal(IOCode.Ok, result.code);
            Assert.True(builder.Meshes[0].GetVertex(0).EpsilonEqual(new Vector3d(1.5, 2.25, 3), 1e-12));
        }

        [Fact]
        public void OBJReader_Materials_ParsesDecimalPointAndClosesFile()
        {
            string dir = TempDir();
            try {
                string mtl = Path.Combine(dir, "test.mtl");
                File.WriteAllText(mtl, "newmtl red\nKd 0.5 0.25 0\nd 0.75\n");
                string obj = "mtllib test.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl red\nf 1 2 3\n";
                var reader = new OBJReader();
                reader.MTLFileSearchPaths.Add(dir);
                var builder = new DMesh3Builder();
                IOReadResult result = WithGermanCulture(() => reader.Read(new StringReader(obj), new ReadOptions() { ReadMaterials = true }, builder));
                Assert.Equal(IOCode.Ok, result.code);
                var material = Assert.IsType<OBJMaterial>(Assert.Single(builder.Materials));
                Assert.Equal(new Vector3f(0.5f, 0.25f, 0), material.Kd);
                Assert.Equal(0.75f, material.d);

                // fails while the reader still holds the .mtl file open
                using (var exclusive = new FileStream(mtl, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void STLReader_Ascii_ParsesDecimalPointUnderGermanLocale()
        {
            string stl = "solid t\nfacet normal 0 0 1\nouter loop\nvertex 1.5 2.25 3\nvertex 0.5 0 0\nvertex 0 0.5 0\nendloop\nendfacet\nendsolid t\n";
            var builder = new DMesh3Builder();
            IOReadResult result = WithGermanCulture(() => new STLReader().Read(new StringReader(stl), ReadOptions.Defaults, builder));
            Assert.Equal(IOCode.Ok, result.code);
            DMesh3 mesh = builder.Meshes[0];
            Assert.Contains(mesh.Vertices(), v => v.EpsilonEqual(new Vector3d(1.5, 2.25, 3), 1e-6));
        }

        [Fact]
        public void OFFReader_ParsesDecimalPointWithoutCultureSwitch()
        {
            // OFFReader is internal, so go through StandardMeshReader with its culture switch turned off
            string dir = TempDir();
            try {
                string path = Path.Combine(dir, "t.off");
                File.WriteAllText(path, "OFF\n3 1 0\n1.5 2.25 3\n0.5 0 0\n0 0.5 0\n3 0 1 2\n");
                var builder = new DMesh3Builder();
                var reader = new StandardMeshReader() { MeshBuilder = builder, ReadInvariantCulture = false };
                IOReadResult result = WithGermanCulture(() => reader.Read(path, ReadOptions.Defaults));
                Assert.Equal(IOCode.Ok, result.code);
                Assert.True(builder.Meshes[0].GetVertex(0).EpsilonEqual(new Vector3d(1.5, 2.25, 3), 1e-12));
            } finally {
                Directory.Delete(dir, true);
            }
        }

        // --- writers format with InvariantCulture ---

        public static IEnumerable<object[]> TextWriters()
        {
            yield return new object[] { new OBJWriter(), "v 1.5 2.25 3" };
            yield return new object[] { new OFFWriter(), "1.5" };
            yield return new object[] { new STLWriter(), "vertex 1.5" };
        }

        [Theory]
        [MemberData(nameof(TextWriters))]
        public void TextWriters_UseDecimalPointUnderGermanLocale(IMeshWriter meshWriter, string expected)
        {
            // a StringWriter created under de-DE formats with de-DE, whatever the thread culture is later
            string text = WithGermanCulture(() => {
                var sw = new StringWriter();
                IOWriteResult result = meshWriter.Write(sw, new List<WriteMesh> { new WriteMesh(Triangle()) }, WriteOptions.Defaults);
                Assert.Equal(IOCode.Ok, result.code);
                return sw.ToString();
            });
            Assert.Contains(expected, text);
            Assert.DoesNotContain("1,5", text);
        }

        // --- OBJWriter .mtl file ---

        [Fact]
        public void OBJWriter_WritesMaterialFile()
        {
            var streams = new Dictionary<string, MemoryStream>();
            var writer = new StandardMeshWriter();
            writer.OpenStreamF = (path) => { var ms = new MemoryStream(); streams[path] = ms; return ms; };
            writer.CloseStreamF = (stream) => { };

            var material = new OBJMaterial() { name = "red", Kd = new Vector3f(1, 0, 0) };
            var mesh = new WriteMesh(Triangle()) { Materials = new List<GenericMaterial> { material }, TriToMaterialMap = new ConstantIndexMap(0) };
            WriteOptions options = WriteOptions.Defaults;
            options.bWriteMaterials = true;
            options.MaterialFilePath = "x.mtl";

            IOWriteResult result = writer.Write("x.obj", new List<WriteMesh> { mesh }, options);
            Assert.Equal(IOCode.Ok, result.code);
            string mtl = System.Text.Encoding.UTF8.GetString(streams["x.mtl"].ToArray());
            Assert.Contains("newmtl red", mtl);
            Assert.Contains("Kd 1 0 0", mtl);
        }

        // --- BufferUtil zlib ---

        [Fact]
        public void BufferUtil_ZLibRoundTrip_LargeBuffer()
        {
            // DeflateStream.Read returns partial blocks for large buffers
            var rand = new Random(5);
            byte[] data = new byte[4 * 1024 * 1024];
            for (int i = 0; i < data.Length; ++i)
                data[i] = (byte)((i % 251) ^ (rand.Next(16)));
            byte[] restored = BufferUtil.DecompressZLib(BufferUtil.CompressZLib(data, true));
            Assert.Equal(data, restored);
        }

        // --- ParallelStream ---

        [Fact]
        public void ParallelStream_ConsumerException_IsRethrownByRun()
        {
            // used to be unhandled on the consumer thread, which terminates the process
            var stream = new ParallelStream<int, int>() {
                ProducerF = (i) => i,
                ConsumerF = (i) => { if (i == 50) throw new InvalidOperationException("consumer failed"); }
            };
            Exception e = Assert.ThrowsAny<Exception>(() => stream.Run(Enumerable.Range(0, 100)));
            Assert.IsType<InvalidOperationException>(e.InnerException);
        }

        [Fact]
        public void ParallelStream_ProducerException_IsRethrownByRun()
        {
            // used to leave the consumer spinning forever
            var stream = new ParallelStream<int, int>() {
                ProducerF = (i) => { if (i == 50) throw new InvalidOperationException("producer failed"); return i; },
                ConsumerF = (i) => { }
            };
            Exception e = Assert.ThrowsAny<Exception>(() => stream.Run(Enumerable.Range(0, 100)));
            Assert.IsType<InvalidOperationException>(e.InnerException);
        }

        [Fact]
        public void STLWriter_Binary_StreamErrorIsReported()
        {
            var writer = new BinaryWriter(new MemoryStream(new byte[100]));      // not expandable
            Assert.ThrowsAny<Exception>(() => new STLWriter().Write(writer, new List<WriteMesh> { new WriteMesh(Triangle()) }, WriteOptions.Defaults));
        }
    }
}
