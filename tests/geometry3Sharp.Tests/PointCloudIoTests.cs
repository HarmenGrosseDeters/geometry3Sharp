using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> Point cloud files: ascii XYZ (new) and PLY without faces (existing reader and writer). </summary>
    public class PointCloudIoTests
    {
        static DMesh3 Cloud(bool normals)
        {
            var rnd = new Random(4);
            DMesh3 mesh = new DMesh3(normals, false, false, false);
            // UTM-like and tiny and huge coordinates, signed zeros excluded
            double[] scales = { 1, 632117.25, 1e-9, 1e12 };
            for (int i = 0; i < 40; ++i) {
                double s = scales[i % scales.Length];
                var p = new Vector3d(s * (rnd.NextDouble() - 0.5), 5712043.5 * rnd.NextDouble() + s, s * Math.PI * rnd.NextDouble());
                if (normals) mesh.AppendVertex(new NewVertexInfo(p, new Vector3f((float)rnd.NextDouble(), (float)(rnd.NextDouble() - 0.5), 0.1f * (float)rnd.NextDouble())));
                else mesh.AppendVertex(p);
            }
            return mesh;
        }

        static DMesh3 RoundTrip(DMesh3 mesh, string extension, bool binary = false)
        {
            string dir = MeshFormatTestUtil.TempDir();
            try {
                string path = Path.Combine(dir, "cloud." + extension);
                WriteOptions options = WriteOptions.Defaults;
                options.bWriteBinary = binary;
                options.bPerVertexNormals = mesh.HasVertexNormals;
                IOWriteResult w = StandardMeshWriter.WriteMesh(path, mesh, options);
                Assert.Equal(IOCode.Ok, w.code);
                var meshes = MeshFormatTestUtil.ReadAll(path, out IOReadResult r);
                Assert.Equal(IOCode.Ok, r.code);
                Assert.Single(meshes);
                return meshes[0];
            } finally { Directory.Delete(dir, true); }
        }

        static void AssertSamePoints(DMesh3 expected, DMesh3 actual, bool normals, bool exactNormals = true)
        {
            Assert.Equal(expected.VertexCount, actual.VertexCount);
            Assert.Equal(0, actual.TriangleCount);
            Assert.Equal(normals, actual.HasVertexNormals);
            int[] a = expected.VertexIndices().ToArray(), b = actual.VertexIndices().ToArray();
            for (int i = 0; i < a.Length; ++i) {
                Assert.Equal(expected.GetVertex(a[i]), actual.GetVertex(b[i]));
                if (normals) {
                    Vector3f e = expected.GetVertexNormal(a[i]), g = actual.GetVertexNormal(b[i]);
                    if (exactNormals) Assert.Equal(e, g); else Assert.True((e - g).Length < 1e-6);
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Xyz_RoundTripIsExact(bool normals)
        {
            DMesh3 mesh = Cloud(normals);
            AssertSamePoints(mesh, RoundTrip(mesh, "xyz"), normals);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void PlyPoints_RoundTrip(bool normals, bool binary)
        {
            // a PLY file without faces is a point cloud (existing reader and writer); positions are written as double
            DMesh3 mesh = Cloud(normals);
            DMesh3 back = RoundTrip(mesh, "ply", binary);
            Assert.Equal(mesh.VertexCount, back.VertexCount);
            Assert.Equal(0, back.TriangleCount);
            Assert.Equal(normals, back.HasVertexNormals);
            int[] a = mesh.VertexIndices().ToArray(), b = back.VertexIndices().ToArray();
            for (int i = 0; i < a.Length; ++i) {
                Vector3d e = mesh.GetVertex(a[i]), g = back.GetVertex(b[i]);
                Assert.True((e - g).Length <= 1e-6 * Math.Max(1, e.Length), $"{e} vs {g}");
            }
        }

        static IOReadResult ReadText(string text, out DMesh3 mesh, ReadOptions options = null)
        {
            var builder = new DMesh3Builder();
            var result = new XYZFormatReader().ReadFile(new MemoryStream(Encoding.ASCII.GetBytes(text)), builder, options ?? ReadOptions.Defaults, null);
            mesh = builder.Meshes.Count > 0 ? builder.Meshes[0] : null;
            return result;
        }

        [Fact]
        public void Xyz_ReadsCommentsBlankLinesTabsCrLfAndExtraColumns()
        {
            string text = "# a comment\r\n\r\n// another\r\n1 2 3 0 0 1 255 0 0\r\n\t4.5\t-5e-1  6   0 1 0  7\r\n  \r\n7 8 9 1 0 0\r\n";
            Assert.Equal(IOCode.Ok, ReadText(text, out DMesh3 mesh).code);
            Assert.Equal(3, mesh.VertexCount);
            Assert.True(mesh.HasVertexNormals);
            Assert.Equal(new Vector3d(4.5, -0.5, 6), mesh.GetVertex(1));
            Assert.Equal(new Vector3f(0, 1, 0), mesh.GetVertexNormal(1));
            Assert.Equal(new Vector3f(0, 0, 1), mesh.GetVertexNormal(0));
        }

        [Fact]
        public void Xyz_PositionsOnly_ExtraColumnsBelowSixIgnored()
        {
            Assert.Equal(IOCode.Ok, ReadText("1 2 3\n4 5 6 7\n8 9 10 11 12\n", out DMesh3 mesh).code);
            Assert.False(mesh.HasVertexNormals);
            Assert.Equal(3, mesh.VertexCount);
        }

        [Theory]
        [InlineData("")]
        [InlineData("# only a comment\n\n")]
        public void Xyz_NoPoints_IsGarbageData(string text)
        {
            Assert.Equal(IOCode.GarbageDataError, ReadText(text, out _).code);
        }

        [Theory]
        [InlineData("1 2\n")]
        [InlineData("1 2 3\n4 5\n")]
        [InlineData("1 2 3 0 0 1\n4 5 6\n")]                // the first point has a normal, the second does not
        [InlineData("1 2 3 0 0 1\n4 5 6 0 1\n")]
        [InlineData("a b c\n")]
        [InlineData("1 2 3x\n")]
        [InlineData("1 2 3 0 0 z\n")]
        [InlineData("1,5 2 3\n")]                            // (a decimal comma is not a number)
        public void Xyz_Malformed_IsParsingError(string text)
        {
            Assert.Equal(IOCode.FileParsingError, ReadText(text, out _).code);
        }

        [Theory]
        [InlineData("NaN 2 3")]
        [InlineData("1 -1e400 3")]
        [InlineData("1 2 1e400")]
        public void Xyz_NonFinitePosition_RejectedUnlessAllowed(string line)
        {
            Assert.Equal(IOCode.FileParsingError, ReadText(line + "\n", out _).code);
            var allow = new ReadOptions() { AllowNonFiniteCoordinates = true };
            Assert.Equal(IOCode.Ok, ReadText(line + "\n", out DMesh3 mesh, allow).code);
            Assert.Equal(1, mesh.VertexCount);
        }

        [Fact]
        public void Xyz_IsCultureIndependent()
        {
            DMesh3 mesh = Cloud(true);
            var prev = Thread.CurrentThread.CurrentCulture;
            try {
                foreach (string culture in new[] { "de-DE", "en-US" }) {
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    AssertSamePoints(mesh, RoundTrip(mesh, "xyz"), true);
                }
            } finally { Thread.CurrentThread.CurrentCulture = prev; }
        }

        [Fact]
        public void Xyz_WriterIgnoresTrianglesAndCombinesMeshes()
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 4 }.Generate().MakeDMesh();
            DMesh3 plain = new DMesh3();
            plain.AppendVertex(new Vector3d(9, 9, 9));
            var sw = new StringWriter();
            var result = new XYZWriter().Write(sw, new List<WriteMesh> { new WriteMesh(sphere), new WriteMesh(plain) }, WriteOptions.Defaults);
            Assert.Equal(IOCode.Ok, result.code);
            string[] lines = sw.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(sphere.VertexCount + 1, lines.Length);
            Assert.All(lines, l => Assert.Equal(sphere.HasVertexNormals ? 6 : 3, l.Trim().Split(' ').Length));
            Assert.Equal(IOCode.FormatNotSupportedError, new XYZWriter().Write(new BinaryWriter(new MemoryStream()), new List<WriteMesh>(), WriteOptions.Defaults).code);
        }

        [Fact]
        public void Xyz_ReadsStraightIntoTheAlphaWrap()
        {
            // the vertices of a mesh without triangles are the input points of MeshAlphaWrap
            var rnd = new Random(2);
            DMesh3 mesh = new DMesh3();
            while (mesh.VertexCount < 600) {
                var d = new Vector3d(2 * rnd.NextDouble() - 1, 2 * rnd.NextDouble() - 1, 2 * rnd.NextDouble() - 1);
                if (d.Length > 1 || d.Length < 1e-3) continue;
                mesh.AppendVertex(d.Normalized);
            }
            DMesh3 back = RoundTrip(mesh, "xyz");
            var wrap = new MeshAlphaWrap(back);
            Assert.True(wrap.Compute(), wrap.Status.ToString());
            ResultChecks.AssertClosed(wrap.Result);
        }
    }
}
