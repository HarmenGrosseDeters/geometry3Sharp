using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The correctly rounded decimal parser (ExactDecimal, docs/design/DETERMINISM.md C2). The expected values are known by
    /// construction (a double's exact decimal expansion parses back to it; the exact halfway string between two neighbours
    /// rounds to the even one, a digit more rounds up, a digit less rounds down), so the tests do not depend on the
    /// platform parser, which is wrong on .NET Framework. On frameworks with a correct parser it is also a differential
    /// oracle.
    /// </summary>
    public class ExactDecimalTests
    {
        ulong state = 424242;
        ulong Next()
        {
            ulong z = (state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static long Bits(double d) { return BitConverter.DoubleToInt64Bits(d); }
        static int Bits(float f) { return BitConverter.ToInt32(BitConverter.GetBytes(f), 0); }

        // exact decimal string of m x 2^k: "digits" + "e" + exponent (no rounding, any digit count)
        static string ExactString(BigInteger m, int k)
        {
            if (k >= 0) return (m << k).ToString(CultureInfo.InvariantCulture);
            return (m * BigInteger.Pow(5, -k)).ToString(CultureInfo.InvariantCulture) + "e" + (k).ToString(CultureInfo.InvariantCulture);
        }

        static void Decompose(double x, out BigInteger m, out int k)
        {
            long bits = Bits(x);
            int e = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) { m = frac; k = -1074; } else { m = frac | (1L << 52); k = e - 1075; }
        }

        IEnumerable<double> RandomDoubles(int count)
        {
            for (int i = 0; i < count; ++i) {
                if (i % 3 == 0) {
                    double x = BitConverter.Int64BitsToDouble((long)(Next() & 0x7FFFFFFFFFFFFFFFUL));
                    if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                    yield return x;
                } else if (i % 3 == 1)
                    yield return ((Next() >> 11) * (1.0 / 9007199254740992.0)) * Math.Pow(10, (int)(Next() % 40) - 20);
                else
                    yield return BitConverter.Int64BitsToDouble((long)(Next() & 0x000FFFFFFFFFFFFFUL));      // subnormals
            }
        }

        [Fact]
        public void ExactExpansion_ParsesBackToTheSameDouble()
        {
            foreach (double x in RandomDoubles(3000)) {
                BigInteger m; int k;
                Decompose(x, out m, out k);
                string s = ExactString(m, k);
                double v;
                Assert.True(ExactDecimal.TryParseDouble(s, out v), s);
                Assert.True(Bits(x) == Bits(v), $"{x:R}: \"{s.Substring(0, Math.Min(40, s.Length))}...\" gave {v:R}");
            }
        }

        [Fact]
        public void SeventeenDigits_ParseBackToTheSameDouble()
        {
            foreach (double x in RandomDoubles(20000)) {
                string s = x.ToString("E16", CultureInfo.InvariantCulture);
                double v;
                Assert.True(ExactDecimal.TryParseDouble(s, out v), s);
                Assert.True(Bits(x) == Bits(v), $"{s} gave {v:R}, expected {x:R}");
            }
        }

        // the exact halfway string between x and its upper neighbour: (2m + 1) x 2^(k-1)
        [Fact]
        public void HalfwayStrings_RoundToEven_AndOneDigitMoreOrLessDecides()
        {
            int tested = 0;
            foreach (double x in RandomDoubles(2000).Concat(new[] { 1.0, 2.0, 9007199254740992.0, double.Epsilon, 2.2250738585072009E-308, 2.2250738585072014E-308, 4503599627370496.0, 0.1, 1e23 })) {
                if (x == double.MaxValue) continue;
                BigInteger m; int k;
                Decompose(x, out m, out k);
                double up = BitConverter.Int64BitsToDouble(Bits(x) + 1);
                BigInteger mid = 2 * m + 1;
                string half = ExactString(mid, k - 1);
                // one more digit: the same string with a "1" appended after a long run of zeros (a number just above halfway)
                string above, below;
                if (half.Contains("e")) {
                    int at = half.IndexOf('e');
                    above = half.Substring(0, at) + new string('0', 40) + "1" + "e" + (int.Parse(half.Substring(at + 1), CultureInfo.InvariantCulture) - 41).ToString(CultureInfo.InvariantCulture);
                    // just below: halfway minus one unit in the last (very far) place
                    BigInteger digits = BigInteger.Parse(half.Substring(0, at), CultureInfo.InvariantCulture);
                    below = (digits * BigInteger.Pow(10, 41) - 1).ToString(CultureInfo.InvariantCulture) + "e" + (int.Parse(half.Substring(at + 1), CultureInfo.InvariantCulture) - 41).ToString(CultureInfo.InvariantCulture);
                } else {
                    above = half + "." + new string('0', 40) + "1";
                    below = (BigInteger.Parse(half, CultureInfo.InvariantCulture) - 1).ToString(CultureInfo.InvariantCulture) + "." + new string('9', 41);
                }
                double even = (Bits(x) & 1) == 0 ? x : up;
                double vh, va, vb;
                Assert.True(ExactDecimal.TryParseDouble(half, out vh));
                Assert.True(ExactDecimal.TryParseDouble(above, out va));
                Assert.True(ExactDecimal.TryParseDouble(below, out vb));
                Assert.True(Bits(vh) == Bits(even), $"halfway above {x:R}: expected {even:R}, got {vh:R}");
                Assert.True(Bits(va) == Bits(up), $"above halfway after {x:R}: expected {up:R}, got {va:R}");
                Assert.True(Bits(vb) == Bits(x), $"below halfway after {x:R}: expected {x:R}, got {vb:R}");
                ++tested;
            }
            Assert.True(tested > 2000);
        }

        [Fact]
        public void OverflowAndUnderflowBoundaries()
        {
            // MaxValue = (2^53 - 1) x 2^971; the halfway point to 2^1024 is (2^54 - 1) x 2^970: rounds to even = infinity
            string half = ExactString((BigInteger.One << 54) - 1, 970);
            double v;
            Assert.True(ExactDecimal.TryParseDouble(half, out v));
            Assert.True(double.IsPositiveInfinity(v), "the halfway point above MaxValue rounds to infinity (ties to even)");
            Assert.True(ExactDecimal.TryParseDouble("-" + half, out v));
            Assert.True(double.IsNegativeInfinity(v));
            Assert.True(ExactDecimal.TryParseDouble(ExactString((BigInteger.One << 54) - 2, 970), out v));
            Assert.Equal(double.MaxValue, v);
            Assert.True(ExactDecimal.TryParseDouble("1e400", out v)); Assert.True(double.IsPositiveInfinity(v));
            Assert.True(ExactDecimal.TryParseDouble("-1e400", out v)); Assert.True(double.IsNegativeInfinity(v));
            Assert.True(ExactDecimal.TryParseDouble("1e-400", out v)); Assert.Equal(0L, Bits(v));
            Assert.True(ExactDecimal.TryParseDouble("-1e-400", out v)); Assert.Equal(Bits(-0.0), Bits(v));
            // half of double.Epsilon, 2^-1075, is the tie between 0 and Epsilon: to even = 0; a digit more = Epsilon
            string tie = ExactString(BigInteger.One, -1075);
            Assert.True(ExactDecimal.TryParseDouble(tie, out v)); Assert.Equal(0L, Bits(v));
            string tieDigits = tie.Substring(0, tie.IndexOf('e'));
            Assert.True(ExactDecimal.TryParseDouble(tieDigits + new string('0', 40) + "1e" + (-1075 - 41).ToString(CultureInfo.InvariantCulture), out v)); Assert.Equal(double.Epsilon, v);
            Assert.True(ExactDecimal.TryParseDouble("4.9406564584124654e-324", out v)); Assert.Equal(double.Epsilon, v);
            Assert.True(ExactDecimal.TryParseDouble("1.7976931348623157e308", out v)); Assert.Equal(double.MaxValue, v);
            Assert.True(ExactDecimal.TryParseDouble("1.7976931348623159e308", out v)); Assert.True(double.IsPositiveInfinity(v));
        }

        [Fact]
        public void ShortNumbers_AreExactAndKeepTheSignOfZero()
        {
            double v;
            foreach (var c in new (string, double)[] { ("0", 0.0), ("1", 1.0), (".5", 0.5), ("5.", 5.0), ("+1.5e3", 1500.0), ("  2.25  ", 2.25), ("1E2", 100.0),
                ("0.1", 0.1), ("123456789012345", 123456789012345.0), ("1e22", 1e22), ("1e23", 1e23), ("9007199254740993", 9007199254740992.0),
                ("0.000001", 1e-6), ("100e-2", 1.0), ("3.14159265358979323846", 3.141592653589793), ("007", 7.0), ("1e+5", 1e5) }) {
                Assert.True(ExactDecimal.TryParseDouble(c.Item1, out v), c.Item1);
                Assert.True(Bits(c.Item2) == Bits(v), $"\"{c.Item1}\": {v:R} vs {c.Item2:R}");
            }
            Assert.True(ExactDecimal.TryParseDouble("-0", out v)); Assert.Equal(Bits(-0.0), Bits(v));
            Assert.True(ExactDecimal.TryParseDouble("-0.000", out v)); Assert.Equal(Bits(-0.0), Bits(v));
            Assert.True(ExactDecimal.TryParseDouble("0e100", out v)); Assert.Equal(0L, Bits(v));
            Assert.True(ExactDecimal.TryParseDouble("-0e-5", out v)); Assert.Equal(Bits(-0.0), Bits(v));
        }

        [Fact]
        public void LongStrings_BeyondTheKeptDigits_StillRoundCorrectly()
        {
            // the halfway string between 1 and its upper neighbour followed by 900 zeros and a 1: just above, so it rounds up
            double one = 1.0, up = BitConverter.Int64BitsToDouble(Bits(one) + 1);
            string half = ExactString((BigInteger.One << 53) + 1, -53);        // 1 + 2^-53, the tie
            int at = half.IndexOf('e');
            string above = half.Substring(0, at) + new string('0', 900) + "1" + "e" + (int.Parse(half.Substring(at + 1), CultureInfo.InvariantCulture) - 901).ToString(CultureInfo.InvariantCulture);
            string exact = half.Substring(0, at) + new string('0', 900) + "e" + (int.Parse(half.Substring(at + 1), CultureInfo.InvariantCulture) - 900).ToString(CultureInfo.InvariantCulture);
            double v;
            Assert.True(ExactDecimal.TryParseDouble(above, out v)); Assert.Equal(Bits(up), Bits(v));
            Assert.True(ExactDecimal.TryParseDouble(exact, out v)); Assert.Equal(Bits(one), Bits(v));   // the tie itself: to even
            Assert.True(ExactDecimal.TryParseDouble("0." + new string('0', 5000) + "1", out v)); Assert.Equal(0L, Bits(v));
            Assert.True(ExactDecimal.TryParseDouble("1" + new string('0', 5000), out v)); Assert.True(double.IsPositiveInfinity(v));
        }

        [Fact]
        public void NotPlainDecimals_AreLeftToThePlatform()
        {
            double v;
            foreach (string s in new[] { "", " ", "abc", "1e", "e5", "--1", "+-1", "1.2.3", "0x10", "NaN", "Infinity", "-Infinity", "1,5", "1e+", ".", "+", "-.e1", "1 2", "1e5.5", "∞" })
                Assert.False(ExactDecimal.TryParseDouble(s, out v), $"\"{s}\"");
            Assert.False(ExactDecimal.TryParseDouble(null, out v));
            // only the white space NumberStyles.Float allows is trimmed, not every Unicode space (the platform would reject these)
            foreach (string s in new[] { "\u00A01.5", "1.5\u2028", "\u30001" })
                Assert.False(ExactDecimal.TryParseDouble(s, out v), s);
            Assert.True(ExactDecimal.TryParseDouble("\t\n\v\f\r 1.5 \t", out v)); Assert.Equal(1.5, v);
        }

        [Fact]
        public void Float_ExactExpansion_RoundTrips_AndHalfwayRoundsToEven()
        {
            state = 31337;
            for (int i = 0; i < 6000; ++i) {
                uint bits = (uint)Next() & 0x7FFFFFFFu;
                float x = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
                if (float.IsNaN(x) || float.IsInfinity(x)) continue;
                int e = (int)((bits >> 23) & 0xFF);
                long frac = bits & 0x7FFFFF;
                BigInteger m; int k;
                if (e == 0) { m = frac; k = -149; } else { m = frac | (1L << 23); k = e - 150; }
                float v;
                Assert.True(ExactDecimal.TryParseFloat(ExactString(m, k), out v));
                Assert.True(Bits(x) == Bits(v), $"{x:R}: exact expansion gave {v:R}");
                Assert.True(ExactDecimal.TryParseFloat(x.ToString("E8", CultureInfo.InvariantCulture), out v));
                Assert.True(Bits(x) == Bits(v), $"{x.ToString("E8", CultureInfo.InvariantCulture)} gave {v:R}, expected {x:R}");
                if (bits >= 0x7F7FFFFF) continue;
                float up = BitConverter.ToSingle(BitConverter.GetBytes(bits + 1), 0);
                string half = ExactString(2 * m + 1, k - 1);
                float even = (bits & 1) == 0 ? x : up;
                Assert.True(ExactDecimal.TryParseFloat(half, out v));
                Assert.True(Bits(v) == Bits(even), $"float halfway after {x:R}: expected {even:R}, got {v:R}");
            }
            float f;
            Assert.True(ExactDecimal.TryParseFloat("16777217", out f)); Assert.Equal(16777216f, f);
            Assert.True(ExactDecimal.TryParseFloat("16777217.000000000001", out f)); Assert.Equal(16777218f, f);
            Assert.True(ExactDecimal.TryParseFloat("-0", out f)); Assert.Equal(Bits(-0f), Bits(f));
            Assert.True(ExactDecimal.TryParseFloat("1e39", out f)); Assert.True(float.IsPositiveInfinity(f));
            Assert.True(ExactDecimal.TryParseFloat("3.4028235e38", out f)); Assert.Equal(float.MaxValue, f);
            Assert.True(ExactDecimal.TryParseFloat("1e-50", out f)); Assert.Equal(0, Bits(f));
        }

        // a float is rounded once from the decimal, not through double (which can round a halfway case the wrong way)
        [Fact]
        public void Float_IsNotRoundedTwice()
        {
            // 16777217 + 2^-30 is above the float tie (16777217) but within a double: 1.0000000596046448 x 2^24
            float f;
            Assert.True(ExactDecimal.TryParseFloat("16777217.000000000931322574615478515625", out f));
            Assert.Equal(16777218f, f);
        }

        [Fact]
        public void RandomStrings_NeverThrow_AndAgreeWithAPlatformParserThatIsExact()
        {
            state = 99;
            string alphabet = "0123456789.eE+- ";
            bool platformExact = PlatformParserIsExact();
            for (int i = 0; i < 30000; ++i) {
                int len = 1 + (int)(Next() % 28);
                var sb = new StringBuilder();
                for (int j = 0; j < len; ++j) sb.Append(alphabet[(int)(Next() % (ulong)alphabet.Length)]);
                string s = sb.ToString();
                double v; float f;
                bool ok = ExactDecimal.TryParseDouble(s, out v);
                bool okf = ExactDecimal.TryParseFloat(s, out f);
                Assert.Equal(ok, okf);
                if (ok && platformExact) {
                    double p;
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out p))
                        Assert.True(Bits(p) == Bits(v), $"\"{s}\": platform {p:R}, exact {v:R}");
                    float pf;
                    if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out pf))
                        Assert.True(Bits(pf) == Bits(f), $"\"{s}\" as float: platform {pf:R}, exact {f:R}");
                }
            }
            // long digit strings against the platform parser where it is exact
            if (platformExact) {
                state = 7;
                for (int i = 0; i < 20000; ++i) {
                    var sb = new StringBuilder();
                    sb.Append((char)('1' + (int)(Next() % 9))).Append('.');
                    int digits = 15 + (int)(Next() % 12);
                    for (int j = 0; j < digits; ++j) sb.Append((char)('0' + (int)(Next() % 10)));
                    sb.Append('e').Append((int)(Next() % 700) - 350);
                    string s = sb.ToString();
                    double v, p = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
                    Assert.True(ExactDecimal.TryParseDouble(s, out v));
                    Assert.True(Bits(p) == Bits(v), $"\"{s}\": platform {p:R}, exact {v:R}");
                }
            }
        }

        static bool PlatformParserIsExact()
        {
            // the same probe as MeshIOUtil's, here independent of it
            return Bits(double.Parse("5.6348333975312060066e-8", NumberStyles.Float, CultureInfo.InvariantCulture)) == 0x3e6e4074bfc9cfd2L
                && Bits(double.Parse("-0", NumberStyles.Float, CultureInfo.InvariantCulture)) == Bits(-0.0);
        }

        [Fact]
        public void MeshIOUtil_ParsesCorrectlyRoundedOnEveryFramework()
        {
            Assert.Equal(0x3e6e4074bfc9cfd2L, Bits(MeshIOUtil.ParseDouble("5.6348333975312060066e-8")));
            Assert.Equal(Bits(-0.0), Bits(MeshIOUtil.ParseDouble("-0")));
            Assert.Equal(9007199254740992.0, MeshIOUtil.ParseDouble("9007199254740993"));
            Assert.Equal(9007199254740994.0, MeshIOUtil.ParseDouble("9007199254740993.00000000000000000001"));
            Assert.True(double.IsPositiveInfinity(MeshIOUtil.ParseDouble("1e400")));
            Assert.True(double.IsNegativeInfinity(MeshIOUtil.ParseDouble(" -1e400 ")));
            Assert.Equal(16777218f, MeshIOUtil.ParseFloat("16777217.00000000000000000001"));
            Assert.Equal(Bits(-0f), Bits(MeshIOUtil.ParseFloat("-0")));
            Assert.Throws<FormatException>(() => MeshIOUtil.ParseDouble("abc"));
            Assert.Throws<FormatException>(() => MeshIOUtil.ParseFloat("1e"));
            double v;
            Assert.True(MeshIOUtil.TryParseDouble("1e999", out v)); Assert.True(double.IsPositiveInfinity(v));
            Assert.True(MeshIOUtil.TryParseDouble("0.1", out v)); Assert.Equal(0.1, v);
            Assert.False(MeshIOUtil.TryParseDouble("x", out v));
        }

        // the XYZ reader through the exact parser (forced on every framework): a value beyond the range is infinity, never an exception
        [Fact]
        public void XyzReader_UsesTheExactParser_AndReadsOutOfRangeValuesAsInfinity()
        {
            MeshIOUtil.ForceExactDecimal = true;
            try {
                var builder = new DMesh3Builder();
                var allow = new ReadOptions() { AllowNonFiniteCoordinates = true };
                string text = "5.6348333975312060066e-8 -1e400 9007199254740993 1e40 0 0\n";
                var result = new XYZFormatReader().ReadFile(new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes(text)), builder, allow, null);
                Assert.Equal(IOCode.Ok, result.code);
                Vector3d p = builder.Meshes[0].GetVertex(0);
                Assert.Equal(0x3e6e4074bfc9cfd2L, Bits(p.x));
                Assert.True(double.IsNegativeInfinity(p.y));
                Assert.Equal(9007199254740992.0, p.z);
                Assert.True(float.IsPositiveInfinity(builder.Meshes[0].GetVertexNormal(0).x));
                var rejected = new XYZFormatReader().ReadFile(new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes("1 1e400 3\n")), new DMesh3Builder(), ReadOptions.Defaults, null);
                Assert.Equal(IOCode.FileParsingError, rejected.code);
            } finally {
                MeshIOUtil.ForceExactDecimal = false;
            }
        }

        // the readers use the exact parser when the platform's is wrong; here it is forced on every framework
        [Fact]
        public void Readers_UseTheExactParser_ForCoordinatesBeyondThePlatformsPrecision()
        {
            string obj = "v 5.6348333975312060066e-8 -0 9007199254740993\nv 1e400 0.1 3.14159265358979323846\nv 1 2 3\nf 1 2 3\n";
            MeshIOUtil.ForceExactDecimal = true;
            try {
                var builder = new DMesh3Builder();
                Assert.Equal(IOCode.Ok, new OBJReader().Read(new System.IO.StringReader(obj), new ReadOptions() { AllowNonFiniteCoordinates = true }, builder).code);
                DMesh3 m = builder.Meshes[0];
                Vector3d a = m.GetVertex(0), b = m.GetVertex(1);
                Assert.Equal(0x3e6e4074bfc9cfd2L, Bits(a.x));
                Assert.Equal(Bits(-0.0), Bits(a.y));
                Assert.Equal(9007199254740992.0, a.z);
                Assert.True(double.IsPositiveInfinity(b.x));
                Assert.Equal(3.141592653589793, b.z);
                // PLY ascii, OFF and the 3MF-style numbers go through the same helper
                var ply = new DMesh3Builder();
                string plyText = "ply\nformat ascii 1.0\nelement vertex 3\nproperty double x\nproperty double y\nproperty double z\nelement face 1\nproperty list uchar int vertex_indices\nend_header\n"
                    + "5.6348333975312060066e-8 -0 9007199254740993\n1 2 3\n4 5 6\n3 0 1 2\n";
                Assert.Equal(IOCode.Ok, new PLYReader().Read(new System.IO.StringReader(plyText), ReadOptions.Defaults, ply).code);
                Assert.Equal(0x3e6e4074bfc9cfd2L, Bits(ply.Meshes[0].GetVertex(0).x));
                Assert.Equal(9007199254740992.0, ply.Meshes[0].GetVertex(0).z);
                var off = new DMesh3Builder();
                Assert.Equal(IOCode.Ok, new OFFReader().Read(new System.IO.StringReader("OFF\n3 1 0\n5.6348333975312060066e-8 0 0\n1 0 0\n0 1 0\n3 0 1 2\n"), ReadOptions.Defaults, off).code);
                Assert.Equal(0x3e6e4074bfc9cfd2L, Bits(off.Meshes[0].GetVertex(0).x));
            } finally {
                MeshIOUtil.ForceExactDecimal = false;
            }
        }
    }
}
