using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshIOUtil.FormatDouble / FormatFloat (ShortestDecimal, Ryu): the shortest text that reads back bit for bit under a
    /// correctly rounded parser, the same on every framework. The oracles do not use the platform: the text is parsed with
    /// ExactDecimal, and shortness is checked by trying the two neighbours of the digits with one digit fewer.
    /// </summary>
    public class ShortestDecimalTests
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static ulong state;
        // SplitMix64, the same sequence on every framework
        static ulong Next()
        {
            ulong z = (state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static double FromBits(ulong b) { return BitConverter.Int64BitsToDouble((long)b); }
        static ulong Bits(double d) { return (ulong)BitConverter.DoubleToInt64Bits(d); }
        static float FromBits(uint b) { return BitConverter.ToSingle(BitConverter.GetBytes(b), 0); }
        static uint Bits(float f) { return (uint)BitConverter.ToInt32(BitConverter.GetBytes(f), 0); }

        // "R" is the shortest string only on .NET Core 3.0 and later
        static readonly bool PlatformRIsShortest = (1.0 / 3.0).ToString("R", Inv) == "0.3333333333333333" && (1.0f / 3.0f).ToString("R", Inv) == "0.33333334";

        static IEnumerable<double> DoubleClasses()
        {
            yield return 0.0; yield return -0.0; yield return double.Epsilon; yield return -double.Epsilon;
            yield return double.MaxValue; yield return -double.MaxValue;
            yield return 2.2250738585072014E-308;                       // the smallest normal
            yield return FromBits(0x000FFFFFFFFFFFFFUL);                // the largest subnormal
            yield return FromBits(0x7FEFFFFFFFFFFFFEUL);
            yield return 1.0; yield return 0.1; yield return 0.1 + 0.2; yield return 1.0 / 3.0; yield return 5e-320;
            yield return 9007199254740992.0; yield return 9007199254740994.0; yield return 1e15; yield return 1e16; yield return 1e17;
            yield return 1e22; yield return 1e23; yield return 8.41e21; yield return 4.35; yield return 0.0001; yield return 0.00001;
            for (int e = -1074; e <= 1023; ++e) {                        // every power of two and its neighbours (the asymmetric interval)
                double p = Math.Pow(2, e);
                if (p == 0 || double.IsInfinity(p)) continue;
                yield return p;
                yield return FromBits(Bits(p) + 1);
                if (Bits(p) > 0) yield return FromBits(Bits(p) - 1);
            }
            for (int e = -323; e <= 308; ++e) {                          // powers of ten and the shortest strings around them
                double p = double.Parse("1e" + e, NumberStyles.Float, Inv);
                if (e < -300 || e > 300) {
                    yield return p;
                    continue;
                }
                yield return p;
                yield return FromBits(Bits(p) + 1);
                yield return FromBits(Bits(p) - 1);
            }
            foreach (int k in new[] { 1, 2, 3, 4, 5, 7, 9, 13, 16, 17, 21, 22, 23, 24, 27 }) {      // m x 2^e with m a power of 5: exact decimal ties and trailing zeros
                double m = Math.Pow(5, k);
                for (int e = -1074; e <= 1023 - 64; e += (e > -80 && e < 80) ? 1 : 9) {
                    double v = m * Math.Pow(2, e);
                    if (v == 0 || double.IsInfinity(v) || double.IsNaN(v)) continue;
                    yield return v;
                }
            }
            for (int i = -60; i <= 60; ++i) {                            // exact binary fractions and integers (trailing-zero paths)
                yield return i / 64.0; yield return i * 1024.0; yield return i * 12345.678125; yield return (double)i * i * i;
                yield return i * 1e15; yield return i * 5e-324;
            }
        }

        static IEnumerable<float> FloatClasses()
        {
            yield return 0f; yield return -0f; yield return float.Epsilon; yield return float.MaxValue; yield return -float.MaxValue;
            yield return 1.17549435E-38f; yield return FromBits(0x007FFFFFu); yield return FromBits(0x7F7FFFFEu);
            yield return 1f; yield return 0.1f; yield return 16777216f; yield return 16777218f; yield return 1e8f; yield return 1e9f;
            yield return 1e-4f; yield return 1e-5f; yield return 3.4e38f; yield return 8.5e37f;
            for (int e = -149; e <= 127; ++e) {
                float p = (float)Math.Pow(2, e);
                if (p == 0) continue;
                yield return p; yield return FromBits(Bits(p) + 1);
                if (Bits(p) > 0) yield return FromBits(Bits(p) - 1);
            }
            for (int e = -45; e <= 38; ++e) {
                float p = float.Parse("1e" + e, NumberStyles.Float, Inv);
                yield return p; yield return FromBits(Bits(p) + 1); yield return FromBits(Bits(p) - 1);
            }
            foreach (int k in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }) {
                float m = (float)Math.Pow(5, k);
                for (int e = -149; e <= 127 - 24; e += (e > -40 && e < 40) ? 1 : 5) {
                    float v = m * (float)Math.Pow(2, e);
                    if (v == 0 || float.IsInfinity(v) || float.IsNaN(v)) continue;
                    yield return v;
                }
            }
            for (int i = -60; i <= 60; ++i) {
                yield return i / 64f; yield return i * 1024f; yield return i * 12345.678125f; yield return i * 1e8f;
            }
        }

        static IEnumerable<double> RandomDoubles(int count, ulong seed)
        {
            state = seed;
            for (int i = 0; i < count; ++i) {
                double x;
                switch (i % 4) {
                    case 0: x = FromBits(Next()); break;                                                      // any bit pattern
                    case 1: x = ((Next() >> 11) * (1.0 / 9007199254740992.0) - 0.5) * 2000.0; break;          // typical coordinates
                    case 2: x = Math.Round(((Next() >> 11) * (1.0 / 9007199254740992.0) - 0.5) * 2000.0, (int)(Next() % 8)); break;   // short decimals
                    default: x = FromBits(Next() & 0x800FFFFFFFFFFFFFUL | ((Next() % 4) << 52)); break;       // subnormals and the smallest normals
                }
                if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                yield return x;
            }
        }

        static IEnumerable<float> RandomFloats(int count, ulong seed)
        {
            state = seed;
            for (int i = 0; i < count; ++i) {
                float x;
                switch (i % 4) {
                    case 0: x = FromBits((uint)Next()); break;
                    case 1: x = (float)(((Next() >> 11) * (1.0 / 9007199254740992.0) - 0.5) * 2000.0); break;
                    case 2: x = (float)Math.Round(((Next() >> 11) * (1.0 / 9007199254740992.0) - 0.5) * 2000.0, (int)(Next() % 6)); break;
                    default: x = FromBits((uint)Next() & 0x807FFFFFu | (uint)((Next() % 4) << 23)); break;
                }
                if (float.IsNaN(x) || float.IsInfinity(x)) continue;
                yield return x;
            }
        }

        // the digits and the decimal exponent of a text made by FormatDouble / FormatFloat: value = digits x 10^exp
        static void Split(string text, out bool negative, out string digits, out int exp)
        {
            negative = text.StartsWith("-");
            if (negative) text = text.Substring(1);
            int e = 0;
            int ePos = text.IndexOf('E');
            if (ePos >= 0) { e = int.Parse(text.Substring(ePos + 1), Inv); text = text.Substring(0, ePos); }
            int point = text.IndexOf('.');
            string d = point >= 0 ? text.Remove(point, 1) : text;
            if (point >= 0) e -= text.Length - point - 1;
            digits = d;
            exp = e;
            // (no leading zeros: "0.001" is 1 x 10^-3; trailing zeros of a plain integer are kept by the caller)
            string trimmed = digits.TrimStart('0');
            if (trimmed.Length == 0) trimmed = "0";
            digits = trimmed;
        }

        static string Join(bool negative, string digits, int exp)
        {
            return (negative ? "-" : "") + digits + "E" + exp.ToString(Inv);
        }

        // digits with the last digit dropped, rounded down and up
        static void Shorter(string digits, int exp, out string down, out string up, out int exp2)
        {
            string head = digits.Substring(0, digits.Length - 1);
            down = head;
            BigInteger h = BigInteger.Parse(head, Inv);
            up = (h + 1).ToString(Inv);
            exp2 = exp + 1;
        }

        static void CheckDouble(double x)
        {
            string s = MeshIOUtil.FormatDouble(x);
            double back;
            Assert.True(ExactDecimal.TryParseDouble(s, out back), s);
            Assert.True(Bits(x) == Bits(back), $"{Bits(x):X16} -> \"{s}\" -> {Bits(back):X16}");
            if (x == 0) return;
            bool neg; string digits; int exp;
            Split(s, out neg, out digits, out exp);
            digits = digits.TrimEnd('0') == "" ? "0" : digits;      // trailing zeros of a plain integer belong to the exponent
            while (digits.Length > 1 && digits.EndsWith("0")) { digits = digits.Substring(0, digits.Length - 1); ++exp; }
            if (digits.Length == 1) return;
            string down, up; int exp2;
            Shorter(digits, exp, out down, out up, out exp2);
            double bd, bu;
            ExactDecimal.TryParseDouble(Join(neg, down, exp2), out bd);
            ExactDecimal.TryParseDouble(Join(neg, up, exp2), out bu);
            Assert.False(Bits(bd) == Bits(x) || Bits(bu) == Bits(x), $"\"{s}\" is not the shortest for {Bits(x):X16}");
        }

        static void CheckFloat(float x)
        {
            string s = MeshIOUtil.FormatFloat(x);
            float back;
            Assert.True(ExactDecimal.TryParseFloat(s, out back), s);
            Assert.True(Bits(x) == Bits(back), $"{Bits(x):X8} -> \"{s}\" -> {Bits(back):X8}");
            if (x == 0) return;
            bool neg; string digits; int exp;
            Split(s, out neg, out digits, out exp);
            while (digits.Length > 1 && digits.EndsWith("0")) { digits = digits.Substring(0, digits.Length - 1); ++exp; }
            if (digits.Length == 1) return;
            string down, up; int exp2;
            Shorter(digits, exp, out down, out up, out exp2);
            float bd, bu;
            ExactDecimal.TryParseFloat(Join(neg, down, exp2), out bd);
            ExactDecimal.TryParseFloat(Join(neg, up, exp2), out bu);
            Assert.False(Bits(bd) == Bits(x) || Bits(bu) == Bits(x), $"\"{s}\" is not the shortest for {Bits(x):X8}");
        }

        // ---- exact reference: the Steele-White / Dragon4 definition with BigInteger -------------------------------------
        // value = m x 2^e; the rounding interval is [v - lowGap, v + highGap] (closed iff m is even), in units of 2^(e-2):
        // v = 4m, upper bound 4m + 2, lower bound 4m - 2 (4m - 1 when m is a power of two above the smallest exponent).
        // The shortest digit string inside it, the closest one to v among the shortest, ties to the even digit.
        static void Reference(BigInteger m, int e, bool narrowBelow, double approx, out BigInteger digits, out int exp10)
        {
            BigInteger vUnits = 4 * m, hUnits = 4 * m + 2, lUnits = narrowBelow ? 4 * m - 1 : 4 * m - 2;
            bool closed = m.IsEven;
            BigInteger num = BigInteger.Pow(2, Math.Max(e - 2, 0)), den = BigInteger.Pow(2, Math.Max(2 - e, 0));   // unit = num / den
            for (int t = (int)Math.Floor(Math.Log10(approx)) + 2; ; --t) {
                // candidates d with L <= d 10^t <= H: d 10^t den against X num (t >= 0), or d den against X num 10^-t
                BigInteger p10 = BigInteger.Pow(10, Math.Abs(t));
                BigInteger lhsScale = (t >= 0 ? p10 : BigInteger.One) * den, rhsScale = (t >= 0 ? BigInteger.One : p10) * num;
                // d >= L: d >= ceil(L rhsScale / lhsScale); d <= H: d <= floor(H rhsScale / lhsScale)
                BigInteger lo = CeilDiv(lUnits * rhsScale, lhsScale), hi = FloorDiv(hUnits * rhsScale, lhsScale);
                if (!closed) {
                    if (lo * lhsScale == lUnits * rhsScale) lo += 1;
                    if (hi * lhsScale == hUnits * rhsScale) hi -= 1;
                }
                if (lo > hi) continue;
                // the nearest to v, ties to even
                BigInteger q = vUnits * rhsScale, d0 = FloorDiv(q, lhsScale), r2 = 2 * (q - d0 * lhsScale);
                BigInteger d = r2 > lhsScale || (r2 == lhsScale && !d0.IsEven) ? d0 + 1 : d0;
                if (d < lo) d = lo;
                if (d > hi) d = hi;
                digits = d; exp10 = t;
                return;
            }
        }

        static BigInteger FloorDiv(BigInteger a, BigInteger b) { BigInteger r; BigInteger q = BigInteger.DivRem(a, b, out r); return (r.Sign < 0) ? q - 1 : q; }
        static BigInteger CeilDiv(BigInteger a, BigInteger b) { return -FloorDiv(-a, b); }

        static void CheckDoubleAgainstReference(double x)
        {
            if (x == 0) return;
            ulong b = Bits(x) & 0x7FFFFFFFFFFFFFFFUL;
            ulong mant = b & 0xFFFFFFFFFFFFFUL; int ex = (int)(b >> 52);
            BigInteger m = ex == 0 ? mant : (mant | (1UL << 52)); int e = ex == 0 ? -1074 : ex - 1075;
            BigInteger rd; int re;
            Reference(m, e, mant == 0 && ex > 1, Math.Abs(x), out rd, out re);
            ulong digits; int exp10;
            ShortestDecimal.Shortest(Math.Abs(x), out digits, out exp10);
            Assert.True(rd == digits && re == exp10, $"{b:X16}: reference {rd}E{re}, got {digits}E{exp10}");
        }

        static void CheckFloatAgainstReference(float x)
        {
            if (x == 0) return;
            uint b = Bits(x) & 0x7FFFFFFFu;
            uint mant = b & 0x7FFFFFu; int ex = (int)(b >> 23);
            BigInteger m = ex == 0 ? mant : (mant | (1u << 23)); int e = ex == 0 ? -149 : ex - 150;
            BigInteger rd; int re;
            Reference(m, e, mant == 0 && ex > 1, Math.Abs((double)x), out rd, out re);
            ulong digits; int exp10;
            ShortestDecimal.ShortestFloat(Math.Abs(x), out digits, out exp10);
            Assert.True(rd == digits && re == exp10, $"{b:X8}: reference {rd}E{re}, got {digits}E{exp10}");
        }

        [Fact]
        public void Double_MatchesTheExactReference()
        {
            int n = 0;
            foreach (double x in DoubleClasses()) { CheckDoubleAgainstReference(x); ++n; }
            foreach (double x in RandomDoubles(12000, 31)) { CheckDoubleAgainstReference(x); ++n; }
            Assert.True(n > 15000);
        }

        [Fact]
        public void Float_MatchesTheExactReference()
        {
            int n = 0;
            foreach (float x in FloatClasses()) { CheckFloatAgainstReference(x); ++n; }
            foreach (float x in RandomFloats(12000, 32)) { CheckFloatAgainstReference(x); ++n; }
            Assert.True(n > 12000);
        }

        [Fact]
        public void Double_Classes_ReadBackAndAreShortest()
        {
            int n = 0;
            foreach (double x in DoubleClasses()) { CheckDouble(x); ++n; }
            Assert.True(n > 6000);
        }

        [Fact]
        public void Float_Classes_ReadBackAndAreShortest()
        {
            int n = 0;
            foreach (float x in FloatClasses()) { CheckFloat(x); ++n; }
            Assert.True(n > 1500);
        }

        [Fact]
        public void Double_Random_ReadBackAndAreShortest()
        {
            int n = 0;
            foreach (double x in RandomDoubles(60000, 20261112)) { CheckDouble(x); ++n; }
            Assert.True(n > 59000);
        }

        [Fact]
        public void Float_Random_ReadBackAndAreShortest()
        {
            int n = 0;
            foreach (float x in RandomFloats(60000, 20261113)) { CheckFloat(x); ++n; }
            Assert.True(n > 59000);
        }

        // Differential against .NET Core's shortest "R" (3.0 and later): the same text, byte for byte
        [Fact]
        public void Double_MatchesPlatformR_WhereThatIsShortest()
        {
            if (!PlatformRIsShortest) return;     // (.NET Framework: the golden strings below carry the check)
            int n = 0;
            foreach (double x in DoubleClasses()) { Assert.Equal(x.ToString("R", Inv), MeshIOUtil.FormatDouble(x)); ++n; }
            foreach (double x in RandomDoubles(1500000, 777)) { Assert.Equal(x.ToString("R", Inv), MeshIOUtil.FormatDouble(x)); ++n; }
            Assert.True(n > 1000000);
        }

        [Fact]
        public void Float_MatchesPlatformR_WhereThatIsShortest()
        {
            if (!PlatformRIsShortest) return;
            int n = 0;
            foreach (float x in FloatClasses()) { Assert.Equal(x.ToString("R", Inv), MeshIOUtil.FormatFloat(x)); ++n; }
            foreach (float x in RandomFloats(1500000, 778)) { Assert.Equal(x.ToString("R", Inv), MeshIOUtil.FormatFloat(x)); ++n; }
            // every 4099th bit pattern of all floats (a stride, not a sample: it reaches every exponent and both signs)
            for (ulong b = 0; b < (1UL << 32); b += 4099) {
                float x = FromBits((uint)b);
                if (float.IsNaN(x) || float.IsInfinity(x)) continue;
                Assert.Equal(x.ToString("R", Inv), MeshIOUtil.FormatFloat(x));
                ++n;
            }
            Assert.True(n > 2000000);
        }

        // The text itself, from the shortest "R" of .NET Core 3.0 and later, checked on every framework: net48 used to give
        // "4.94065645841247E-324" for the smallest subnormal and a 17 digit text where 16 digits are enough
        [Theory]
        [InlineData(4.9406564584124654E-324, "5E-324")]
        [InlineData(-4.9406564584124654E-324, "-5E-324")]
        [InlineData(9.8813129168249309E-324, "1E-323")]
        [InlineData(2.2250738585072014E-308, "2.2250738585072014E-308")]
        [InlineData(1.7976931348623157E+308, "1.7976931348623157E+308")]
        [InlineData(0.0, "0")]
        [InlineData(1.0, "1")]
        [InlineData(0.1, "0.1")]
        [InlineData(0.3, "0.3")]
        [InlineData(1.0 / 3.0, "0.3333333333333333")]
        [InlineData(0.1 + 0.2, "0.30000000000000004")]
        [InlineData(0.0001, "0.0001")]
        [InlineData(0.00001, "1E-05")]
        [InlineData(9007199254740992.0, "9007199254740992")]
        [InlineData(9007199254740994.0, "9007199254740994")]
        [InlineData(18014398509481984.0, "18014398509481984")]
        [InlineData(8.41e21, "8.41E+21")]
        [InlineData(1e23, "1E+23")]
        [InlineData(1.0000000000000001e23, "1.0000000000000001E+23")]
        [InlineData(5e-320, "5E-320")]
        public void Double_Golden(double x, string expected)
        {
            Assert.Equal(expected, MeshIOUtil.FormatDouble(x));
        }

        [Fact]
        public void Double_Golden_ByBits()
        {
            Assert.Equal("2.225073858507201E-308", MeshIOUtil.FormatDouble(FromBits(0x000FFFFFFFFFFFFFUL)));
            Assert.Equal("-0", MeshIOUtil.FormatDouble(FromBits(0x8000000000000000UL)));
        }

        [Fact]
        public void Double_GoldenValues()
        {
            Assert.Equal("1000000000000000", MeshIOUtil.FormatDouble(1e15));
            Assert.Equal("10000000000000000", MeshIOUtil.FormatDouble(1e16));
            Assert.Equal("1E+17", MeshIOUtil.FormatDouble(1e17));
            Assert.Equal("1.2345678901234568E+20", MeshIOUtil.FormatDouble(123456789012345680000.0));
            Assert.Equal("-1234.5678", MeshIOUtil.FormatDouble(-1234.5678));
            Assert.Equal("4.35", MeshIOUtil.FormatDouble(4.35));
            Assert.Equal("5E-05", MeshIOUtil.FormatDouble(5e-5));
            Assert.Equal("0.00012345", MeshIOUtil.FormatDouble(0.00012345));
            Assert.Equal("1.5E-100", MeshIOUtil.FormatDouble(1.5e-100));
            Assert.Equal("NaN", MeshIOUtil.FormatDouble(double.NaN));
            Assert.Equal("Infinity", MeshIOUtil.FormatDouble(double.PositiveInfinity));
            Assert.Equal("-Infinity", MeshIOUtil.FormatDouble(double.NegativeInfinity));
        }

        [Fact]
        public void Float_GoldenValues()
        {
            Assert.Equal("1E-45", MeshIOUtil.FormatFloat(float.Epsilon));
            Assert.Equal("-1E-45", MeshIOUtil.FormatFloat(-float.Epsilon));
            Assert.Equal("1.1754944E-38", MeshIOUtil.FormatFloat(1.17549435E-38f));
            Assert.Equal("3.4028235E+38", MeshIOUtil.FormatFloat(float.MaxValue));
            Assert.Equal("0", MeshIOUtil.FormatFloat(0f));
            Assert.Equal("-0", MeshIOUtil.FormatFloat(-0f));
            Assert.Equal("0.1", MeshIOUtil.FormatFloat(0.1f));
            Assert.Equal("0.33333334", MeshIOUtil.FormatFloat(1f / 3f));
            Assert.Equal("16777216", MeshIOUtil.FormatFloat(16777216f));
            Assert.Equal("100000000", MeshIOUtil.FormatFloat(1e8f));
            Assert.Equal("1E+09", MeshIOUtil.FormatFloat(1e9f));
            Assert.Equal("123456790", MeshIOUtil.FormatFloat(123456790f));
            Assert.Equal("0.0001", MeshIOUtil.FormatFloat(1e-4f));
            Assert.Equal("1E-05", MeshIOUtil.FormatFloat(1e-5f));
            Assert.Equal("1.2345678E+10", MeshIOUtil.FormatFloat(1.2345678e10f));
            Assert.Equal("NaN", MeshIOUtil.FormatFloat(float.NaN));
            Assert.Equal("-Infinity", MeshIOUtil.FormatFloat(float.NegativeInfinity));
            Assert.Equal("Infinity", MeshIOUtil.FormatFloat(float.PositiveInfinity));
        }

        // Coordinates written by the real XYZ writer and read back by the XYZ reader are bit-identical, subnormals included
        [Fact]
        public void XyzWriter_Subnormals_ReadBack()
        {
            double[] xs = { 5e-320, -5e-324, 2.225073858507201E-308, 1e-310, 4.35, 0.1, 1.7976931348623157E+308 };
            var mesh = new DMesh3();
            for (int i = 0; i + 2 < xs.Length; ++i) mesh.AppendVertex(new Vector3d(xs[i], xs[i + 1], xs[i + 2]));
            var sw = new System.IO.StringWriter(Inv);
            Assert.Equal(IOCode.Ok, new XYZWriter().Write(sw, new List<WriteMesh> { new WriteMesh(mesh) }, WriteOptions.Defaults).code);
            string[] lines = sw.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(mesh.VertexCount, lines.Length);
            for (int i = 0; i < lines.Length; ++i) {
                string[] f = lines[i].Trim().Split(' ');
                Assert.True(f.Length >= 3);   // (the writer may append a normal)
                for (int k = 0; k < 3; ++k)
                    Assert.Equal(Bits(mesh.GetVertex(i)[k]), Bits(MeshIOUtil.ParseDouble(f[k])));
            }
        }
    }
}
