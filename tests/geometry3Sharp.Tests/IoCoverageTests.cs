using System;
using System.Collections.Generic;
using System.IO;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> Paths of the XYZ reader and writer and of StandardMeshWriter that no other test reaches. </summary>
    public class IoCoverageTests
    {
        static DMesh3 Triangle(double offset)
        {
            var m = new DMesh3(false, false, false, false);
            m.AppendVertex(new Vector3d(offset, 0, 0));
            m.AppendVertex(new Vector3d(offset + 1, 0.5, 0));
            m.AppendVertex(new Vector3d(offset, 2, -3.25));
            m.AppendTriangle(0, 1, 2);
            return m;
        }

        // XYZReader.Read(BinaryReader): XYZ is an ascii format, the binary overload says so and builds nothing
        [Fact]
        public void XyzReader_BinaryRead_IsNotSupported_AndAppendsNothing()
        {
            var builder = new DMesh3Builder();
            IOReadResult r = new XYZReader().Read(new BinaryReader(new MemoryStream(new byte[] { 1, 2, 3, 4 })), ReadOptions.Defaults, builder);
            Assert.Equal(IOCode.FormatNotSupportedError, r.code);
            Assert.Equal("binary read not supported for XYZ format", r.message);
            Assert.Empty(builder.Meshes);
        }

        // XYZWriter: WriteOptions.ProgressFunc is called once per mesh, with (index, count), before the mesh's points
        [Fact]
        public void XyzWriter_CallsProgressFunc_OncePerMesh_BeforeItsPoints()
        {
            var calls = new List<string>();
            var sw = new StringWriter();
            sw.NewLine = "\n";
            WriteOptions options = WriteOptions.Defaults;
            options.ProgressFunc = (i, n) => calls.Add(i + "/" + n + "@" + sw.GetStringBuilder().Length);
            var meshes = new List<WriteMesh> { new WriteMesh(Triangle(0)), new WriteMesh(Triangle(10)) };
            IOWriteResult r = new XYZWriter().Write(sw, meshes, options);
            Assert.Equal(IOCode.Ok, r.code);
            string first = "0 0 0\n1 0.5 0\n0 2 -3.25\n";
            Assert.Equal(first + "10 0 0\n11 0.5 0\n10 2 -3.25\n", sw.ToString());
            // progress of mesh 0 before any text, of mesh 1 after exactly the three points of mesh 0
            Assert.Equal(new[] { "0/2@0", "1/2@" + first.Length }, calls.ToArray());
        }

        // StandardMeshWriter.Write_XYZ: OpenStreamF returning null is FileAccessError (and nothing is written or closed)
        [Fact]
        public void StandardMeshWriter_Xyz_OpenStreamReturnsNull_IsFileAccessError()
        {
            int opened = 0, closed = 0;
            var writer = new StandardMeshWriter();
            writer.OpenStreamF = name => { opened++; return null; };
            writer.CloseStreamF = s => closed++;
            IOWriteResult r = writer.Write("points.xyz", new List<WriteMesh> { new WriteMesh(Triangle(0)) }, WriteOptions.Defaults);
            Assert.Equal(IOCode.FileAccessError, r.code);
            Assert.Equal("Could not open file points.xyz for writing", r.message);
            Assert.Equal(1, opened);
            Assert.Equal(0, closed);
        }

        // the same call with a stream: the XYZ writer is reached through StandardMeshWriter and its bytes are the points
        [Fact]
        public void StandardMeshWriter_Xyz_WritesThePointsToTheOpenedStream()
        {
            var ms = new MemoryStream();
            int closed = 0;
            var writer = new StandardMeshWriter();
            writer.OpenStreamF = name => ms;
            writer.CloseStreamF = s => closed++;
            IOWriteResult r = writer.Write("points.xyz", new List<WriteMesh> { new WriteMesh(Triangle(0)) }, WriteOptions.Defaults);
            Assert.Equal(IOCode.Ok, r.code);
            Assert.Equal(1, closed);
            string text = System.Text.Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n");
            Assert.Equal("0 0 0\n1 0.5 0\n0 2 -3.25\n", text);
        }

        // MeshIOUtil.FormatDoubleByDigits / FormatFloatByDigits at the top of the range: G15 and G16 of the maximum
        // are above it ("1.79769313486232E+308" is OverflowException on .NET Framework, infinity on .NET Core:
        // not equal either way), so G17 is the result
        [Fact]
        public void FormatByDigits_OfTheMaximum_FallsThroughToSeventeenDigits()
        {
            string d = MeshIOUtil.FormatDoubleByDigits(double.MaxValue);
            Assert.Equal("1.7976931348623157E+308", d);
            Assert.Equal(double.MaxValue, double.Parse(d, System.Globalization.CultureInfo.InvariantCulture));
            string dn = MeshIOUtil.FormatDoubleByDigits(-double.MaxValue);
            Assert.Equal("-1.7976931348623157E+308", dn);

            string f = MeshIOUtil.FormatFloatByDigits(float.MaxValue);
            Assert.Equal(float.MaxValue, float.Parse(f, System.Globalization.CultureInfo.InvariantCulture));
            // (G8 "3.4028235E+38" reads back on .NET Core, is out of range on .NET Framework: G9 "3.40282347E+38" there)
            Assert.True(f == "3.4028235E+38" || f == "3.40282347E+38", f);
            Assert.Equal("-" + f, MeshIOUtil.FormatFloatByDigits(-float.MaxValue));
        }
    }
}
