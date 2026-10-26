using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// double.Parse(s, CultureInfo.InvariantCulture) uses NumberStyles.Float | AllowThousands, so "1,5" (a
    /// decimal comma, eg from German software) parsed as 15. The text readers must reject it instead of
    /// silently scaling coordinates by 10 (or 100, 1000, ...).
    /// </summary>
    public class DecimalCommaTests
    {
        static IOReadResult Read(string text, string extension, out DMesh3Builder builder, List<string> warnings)
        {
            builder = new DMesh3Builder();
            StandardMeshReader reader = new StandardMeshReader() { MeshBuilder = builder };
            reader.warningEvent += (msg, o) => warnings.Add(msg);
            return reader.Read(new MemoryStream(System.Text.Encoding.ASCII.GetBytes(text)), extension, ReadOptions.Defaults);
        }

        static bool HasVertexWithX(DMesh3Builder builder, double x)
        {
            return builder.Meshes.Any(m => m.Vertices().Any(v => v.x == x));
        }

        [Fact]
        public void OBJ_DecimalComma_IsNotReadAsThousandsSeparator()
        {
            var warnings = new List<string>();
            IOReadResult r = Read("v 1,5 0 0\nv 2 0 0\nv 0 1 0\nvn 0,5 0 1\nvt 0,25 0\nf 1 2 3\n", "obj", out DMesh3Builder builder, warnings);
            Assert.False(HasVertexWithX(builder, 15));
            Assert.Contains(warnings, w => w.Contains("1,5"));
            Assert.Contains(warnings, w => w.Contains("0,5"));
            Assert.Contains(warnings, w => w.Contains("0,25"));
        }

        [Theory]
        [InlineData("v 0 0,5 0")]               // bad value after a valid one
        [InlineData("v 0 0 0 1 0,5 0")]         // bad vertex color
        public void OBJ_BadValueLaterInLine_SkipsWholeLine(string badLine)
        {
            // a partly added line would shift the coordinates of all following vertices
            var warnings = new List<string>();
            IOReadResult r = Read(badLine + "\nv 1 2 3\nv 4 5 6\nv 7 8 9\nf 1 2 3\n", "obj", out DMesh3Builder builder, warnings);
            Assert.Equal(IOCode.Ok, r.code);
            DMesh3 m = builder.Meshes[0];
            Assert.Equal(3, m.VertexCount);
            Assert.Equal(new Vector3d(1, 2, 3), m.GetVertex(0));
            Assert.Equal(new Vector3d(7, 8, 9), m.GetVertex(2));
        }

        [Fact]
        public void MTL_BadValue_SkipsOnlyThatLine()
        {
            string dir = MeshFormatTestUtil.TempDir();
            try {
                File.WriteAllText(Path.Combine(dir, "m.mtl"), "newmtl a\nNs 96,078431\nnewmtl b\nKd 1 0 0\n");
                File.WriteAllText(Path.Combine(dir, "m.obj"), "mtllib m.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl b\nf 1 2 3\n");
                var warnings = new List<string>();
                var builder = new DMesh3Builder();
                var reader = new StandardMeshReader() { MeshBuilder = builder };
                reader.warningEvent += (msg, o) => warnings.Add(msg);
                ReadOptions options = ReadOptions.Defaults;
                options.ReadMaterials = true;
                IOReadResult r = reader.Read(Path.Combine(dir, "m.obj"), options);
                Assert.Equal(IOCode.Ok, r.code);
                Assert.Contains(warnings, w => w.Contains("96,078431"));
                Assert.DoesNotContain(warnings, w => w.Contains("unknown material"));
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void OBJ_ValidNumberFormats_StillRead()
        {
            var warnings = new List<string>();
            IOReadResult r = Read("v 1e-3 -.5 +2\nv 2.0 0 0\nv 0 1E2 0\nf 1 2 3\n", "obj", out DMesh3Builder builder, warnings);
            Assert.Equal(IOCode.Ok, r.code);
            Assert.Empty(warnings);
            DMesh3 m = builder.Meshes[0];
            Assert.Equal(new Vector3d(0.001, -0.5, 2), m.GetVertex(0));
            Assert.Equal(new Vector3d(0, 100, 0), m.GetVertex(2));
        }

        [Fact]
        public void OFF_DecimalComma_IsAnError()
        {
            var warnings = new List<string>();
            IOReadResult r = Read("OFF\n3 1 0\n1,5 0 0\n2 0 0\n0 1 0\n3 0 1 2\n", "off", out DMesh3Builder builder, warnings);
            Assert.NotEqual(IOCode.Ok, r.code);
            Assert.False(HasVertexWithX(builder, 15));
        }

        [Fact]
        public void AsciiSTL_DecimalComma_IsAnError()
        {
            string stl = "solid t\nfacet normal 0 0 1\n outer loop\n  vertex 1,5 0 0\n  vertex 2 0 0\n  vertex 0 1 0\n endloop\nendfacet\nendsolid t\n";
            var warnings = new List<string>();
            IOReadResult r = Read(stl, "stl", out DMesh3Builder builder, warnings);
            Assert.NotEqual(IOCode.Ok, r.code);
            Assert.False(HasVertexWithX(builder, 15));
        }

        [Fact]
        public void Util_ParseFloatDouble_DecimalComma_ReturnsDefault()
        {
            Assert.Equal(-1f, Util.ParseFloat("1,5", -1f));
            Assert.Equal(-1.0, Util.ParseDouble("1,5", -1.0));
            Assert.Equal(1.5f, Util.ParseFloat("1.5", -1f));
        }
    }
}
