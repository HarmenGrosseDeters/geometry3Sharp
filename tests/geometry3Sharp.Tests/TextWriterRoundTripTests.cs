using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The text writers write coordinates that read back bit for bit on every framework (docs/design/DETERMINISM.md, C3):
    /// .NET Framework's default and "R" formats keep 15 digits and lose bits, .NET Core's keep the shortest exact string.
    /// </summary>
    public class TextWriterRoundTripTests
    {
        static ulong state = 12345;
        // SplitMix64, the same sequence on every framework
        static ulong Next()
        {
            ulong z = (state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static IEnumerable<double> Values(int count)
        {
            state = 12345;
            yield return 0.0; yield return -0.0; yield return 0.1; yield return 1.0 / 3.0; yield return 0.1 + 0.2;
            yield return double.Epsilon; yield return double.MaxValue; yield return -double.MaxValue; yield return 1e21;
            yield return 123456789012345678.0; yield return 5e-324; yield return 2.2250738585072014E-308;
            for (int i = 0; i < count; ++i) {
                if (i % 2 == 0) {
                    double x = BitConverter.Int64BitsToDouble((long)Next());
                    if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                    yield return x;
                } else
                    yield return ((Next() >> 11) * (1.0 / 9007199254740992.0) - 0.5) * 2000.0;
            }
        }

        // (.NET Framework's double.Parse("-0") returns +0: the sign of a zero is lost when reading, which is the parser's, not the
        // writer's; the text "-0" is checked on its own. A correctly rounded parser, docs/design/DETERMINISM.md C2, restores it.)
        static bool SameValueBits(double x, double back)
        {
            if (x == 0 && back == 0) return true;
            return BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(back);
        }

        [Fact]
        public void FormatDouble_ReadsBackBitForBit()
        {
            int checkedCount = 0;
            foreach (double x in Values(20000)) {
                string s = MeshIOUtil.FormatDouble(x);
                double back = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
                Assert.True(SameValueBits(x, back), $"{x:R} -> \"{s}\" -> {back:R}");
                ++checkedCount;
            }
            Assert.True(checkedCount > 20000);
        }

        // the path .NET Framework takes, on every framework
        [Fact]
        public void FormatDoubleByDigits_ReadsBackBitForBit()
        {
            foreach (double x in Values(20000)) {
                string s = MeshIOUtil.FormatDoubleByDigits(x);
                double back = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
                Assert.True(SameValueBits(x, back), $"{x:R} -> \"{s}\" -> {back:R}");
            }
            Assert.Equal("0.1", MeshIOUtil.FormatDoubleByDigits(0.1));
            Assert.Equal("0.3333333333333333", MeshIOUtil.FormatDoubleByDigits(1.0 / 3.0));
            Assert.Equal("0.30000000000000004", MeshIOUtil.FormatDoubleByDigits(0.1 + 0.2));
        }

        [Fact]
        public void FormatFloatByDigits_ReadsBackBitForBit()
        {
            state = 99;
            for (int i = 0; i < 20000; ++i) {
                float x = i % 2 == 0 ? BitConverter.ToSingle(BitConverter.GetBytes((uint)Next()), 0) : (float)(((Next() >> 11) * (1.0 / 9007199254740992.0) - 0.5) * 2000.0);
                if (float.IsNaN(x) || float.IsInfinity(x)) continue;
                string s = MeshIOUtil.FormatFloatByDigits(x);
                float back = float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
                Assert.True((x == 0 && back == 0) || BitConverter.ToInt32(BitConverter.GetBytes(x), 0) == BitConverter.ToInt32(BitConverter.GetBytes(back), 0), $"{x:R} -> \"{s}\" -> {back:R}");
            }
        }

        [Fact]
        public void FormatFloat_ReadsBackBitForBit()
        {
            state = 99;
            for (int i = 0; i < 20000; ++i) {
                float x = i % 2 == 0 ? BitConverter.ToSingle(BitConverter.GetBytes((uint)Next()), 0) : (float)(((Next() >> 11) * (1.0 / 9007199254740992.0) - 0.5) * 2000.0);
                if (float.IsNaN(x) || float.IsInfinity(x)) continue;
                string s = MeshIOUtil.FormatFloat(x);
                float back = float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
                Assert.True((x == 0 && back == 0) || BitConverter.ToInt32(BitConverter.GetBytes(x), 0) == BitConverter.ToInt32(BitConverter.GetBytes(back), 0), $"{x:R} -> \"{s}\" -> {back:R}");
            }
        }

        // .NET Framework throws OverflowException when parsing the 15-digit rounding of double.MaxValue (it is above the maximum)
        [Fact]
        public void FormatByDigits_NearTheLimits_DoesNotThrow_AndKeepsTheSignOfZero()
        {
            foreach (double x in new[] { double.MaxValue, -double.MaxValue, BitConverter.Int64BitsToDouble(0x7FEFFFFFFFFFFFFEL), BitConverter.Int64BitsToDouble(0x7FEFFFFFFFFFFFF0L), 1.7976931348623155E+308 }) {
                string s = MeshIOUtil.FormatDoubleByDigits(x);
                Assert.Equal(BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)));
            }
            foreach (float x in new[] { float.MaxValue, -float.MaxValue, BitConverter.ToSingle(BitConverter.GetBytes(0x7F7FFFFEu), 0) }) {
                string s = MeshIOUtil.FormatFloatByDigits(x);
                Assert.Equal(BitConverter.ToInt32(BitConverter.GetBytes(x), 0), BitConverter.ToInt32(BitConverter.GetBytes(float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)), 0));
            }
            Assert.Equal("-0", MeshIOUtil.FormatDoubleByDigits(-0.0));
            Assert.Equal("0", MeshIOUtil.FormatDoubleByDigits(0.0));
            Assert.Equal("-0", MeshIOUtil.FormatFloatByDigits(-0.0f));
            Assert.Equal("-0", MeshIOUtil.FormatDouble(-0.0));
        }

        [Fact]
        public void FormatDouble_ShortCasesStayShort()
        {
            Assert.Equal("0.1", MeshIOUtil.FormatDouble(0.1));
            Assert.Equal("0", MeshIOUtil.FormatDouble(0.0));
            Assert.Equal("0.3333333333333333", MeshIOUtil.FormatDouble(1.0 / 3.0));
            Assert.Equal("0.30000000000000004", MeshIOUtil.FormatDouble(0.1 + 0.2));
            Assert.Equal("1", MeshIOUtil.FormatDouble(1.0));
            Assert.Equal("NaN", MeshIOUtil.FormatDouble(double.NaN));
        }

        [Fact]
        public void FormatDouble_UsesInvariantCulture()
        {
            CultureInfo saved = System.Threading.Thread.CurrentThread.CurrentCulture;
            try {
                System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal("0.1", MeshIOUtil.FormatDouble(0.1));
                Assert.Equal("0.3333333333333333", MeshIOUtil.FormatDouble(1.0 / 3.0));
                Assert.Equal("0.1", MeshIOUtil.FormatFloat(0.1f));
            } finally { System.Threading.Thread.CurrentThread.CurrentCulture = saved; }
        }

        static DMesh3 RandomMesh()
        {
            state = 777;
            var m = new DMesh3();
            for (int i = 0; i < 300; ++i)
                m.AppendVertex(new Vector3d(((Next() >> 11) * (1.0 / 9007199254740992.0) - 0.5) * 2000.0,
                    ((Next() >> 11) * (1.0 / 9007199254740992.0) - 0.5) * 1e-3, ((Next() >> 11) * (1.0 / 9007199254740992.0)) * 7.0));
            for (int i = 0; i + 2 < 300; i += 3)
                m.AppendTriangle(i, i + 1, i + 2);
            return m;
        }

        static void AssertSameVertices(DMesh3 expected, DMesh3 actual)
        {
            Assert.Equal(expected.VertexCount, actual.VertexCount);
            var a = expected.VertexIndices().ToArray(); var b = actual.VertexIndices().ToArray();
            for (int i = 0; i < a.Length; ++i) {
                Vector3d p = expected.GetVertex(a[i]), q = actual.GetVertex(b[i]);
                Assert.True(BitConverter.DoubleToInt64Bits(p.x) == BitConverter.DoubleToInt64Bits(q.x) && BitConverter.DoubleToInt64Bits(p.y) == BitConverter.DoubleToInt64Bits(q.y)
                    && BitConverter.DoubleToInt64Bits(p.z) == BitConverter.DoubleToInt64Bits(q.z), $"vertex {i}: {p.x:R} {p.y:R} {p.z:R} -> {q.x:R} {q.y:R} {q.z:R}");
            }
        }

        [Fact]
        public void Obj_WritesCoordinatesThatReadBackExactly()
        {
            DMesh3 mesh = RandomMesh();
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(IOCode.Ok, new OBJWriter().Write(sw, new List<WriteMesh> { new WriteMesh(mesh) }, WriteOptions.Defaults).code);
            var builder = new DMesh3Builder();
            Assert.Equal(IOCode.Ok, new OBJReader().Read(new StringReader(sw.ToString()), ReadOptions.Defaults, builder).code);
            Assert.Single(builder.Meshes);
            AssertSameVertices(mesh, builder.Meshes[0]);
        }

        [Fact]
        public void Ply_Ascii_WritesCoordinatesThatReadBackExactly()
        {
            DMesh3 mesh = RandomMesh();
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(IOCode.Ok, new PLYWriter().Write(sw, new List<WriteMesh> { new WriteMesh(mesh) }, WriteOptions.Defaults).code);
            var builder = new DMesh3Builder();
            Assert.Equal(IOCode.Ok, new PLYReader().Read(new StringReader(sw.ToString()), ReadOptions.Defaults, builder).code);
            Assert.Single(builder.Meshes);
            AssertSameVertices(mesh, builder.Meshes[0]);
        }

        [Fact]
        public void ThreeMF_WritesCoordinatesThatReadBackExactly()
        {
            DMesh3 mesh = RandomMesh();
            var ms = new MemoryStream();
            Assert.Equal(IOCode.Ok, new ThreeMFWriter().Write(ms, new List<WriteMesh> { new WriteMesh(mesh) }, WriteOptions.Defaults).code);
            var builder = new DMesh3Builder();
            ms.Position = 0;
            Assert.Equal(IOCode.Ok, new ThreeMFReader() { ConvertToMillimeters = false }.Read(ms, ReadOptions.Defaults, builder).code);
            Assert.Single(builder.Meshes);
            AssertSameVertices(mesh, builder.Meshes[0]);
        }
    }
}
