using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The exact points of ExactMeshArrangement in fixed width (FixedPoint3, FixedArith;
    /// docs/design/FIXED_WIDTH_EXACT_POINTS.md): the conversions at every bit length and at the admission limit
    /// 2^255 - 1, the portable paths of netstandard2.0, the predicates against their BigInteger formulas up to the
    /// limit, and whole booleans that must be bit-identical with the fixed width switched off, also where fixed and
    /// BigInteger points are mixed.
    /// </summary>
    public unsafe class ExactPoint3FixedWidthTests
    {
        static readonly BigInteger Limit = (BigInteger.One << 255) - 1;

        static BigInteger RandomBits(Random rand, int bits)
        {
            if (bits <= 0) return BigInteger.Zero;
            var bytes = new byte[(bits + 7) / 8 + 1];
            rand.NextBytes(bytes);
            bytes[bytes.Length - 1] = 0;
            BigInteger v = new BigInteger(bytes) & ((BigInteger.One << bits) - 1);
            return v | (BigInteger.One << (bits - 1));      // exactly `bits` bits
        }

        // a random value of a random bit length up to maxBits, either sign; sometimes an extreme value
        static BigInteger RandomValue(Random rand, int maxBits, bool extremes = true)
        {
            int kind = extremes ? rand.Next(20) : 20;
            BigInteger v;
            if (kind == 0) v = Limit;
            else if (kind == 1) v = BigInteger.Zero;
            else if (kind == 2) v = BigInteger.One;
            else if (kind == 3) v = (BigInteger.One << (64 * rand.Next(1, 4))) - 1;     // full limbs
            else if (kind == 4) v = BigInteger.One << (64 * rand.Next(1, 4));
            else v = RandomBits(rand, rand.Next(1, maxBits + 1));
            if (v > Limit) v = Limit;
            return (rand.Next(2) == 0) ? v : -v;
        }

        static BigInteger RandomPositive(Random rand, int maxBits, bool extremes = true)
        {
            BigInteger w = BigInteger.Abs(RandomValue(rand, maxBits, extremes));
            return w.IsZero ? BigInteger.One : w;
        }

        static FixedPoint3 Fixed(ExactPoint3 p)
        {
            FixedPoint3 f;
            Assert.True(FixedPoint3.TryCreate(p, out f));
            return f;
        }

        [Fact]
        public void RoundTrip_EveryBitLength_AndTheLimit()
        {
            var rand = new Random(1);
            var values = new List<BigInteger> { 0, 1, -1, Limit, -Limit, Limit + 1, -(Limit + 1), BigInteger.One << 256, -(BigInteger.One << 256) };
            for (int bits = 1; bits <= 300; ++bits)
                for (int r = 0; r < 4; ++r) {
                    BigInteger v = RandomBits(rand, bits);
                    values.Add(v); values.Add(-v);
                }
            for (int k = 1; k <= 5; ++k) {
                values.Add(BigInteger.One << (64 * k)); values.Add(-(BigInteger.One << (64 * k)));
                values.Add((BigInteger.One << (64 * k)) - 1); values.Add(-((BigInteger.One << (64 * k)) - 1));
            }
            foreach (BigInteger v in values) {
                bool fits = BigInteger.Abs(v) <= Limit;
                // in each component, W positive
                foreach (int k in new[] { 0, 1, 2, 3 }) {
                    BigInteger x = (k == 0) ? v : 3, y = (k == 1) ? v : -5, z = (k == 2) ? v : 0, w = (k == 3) ? BigInteger.Abs(v) : 7;
                    if (w.IsZero) continue;
                    ExactPoint3 p = ExactPoint3.CreateUnreduced(x, y, z, w);
                    FixedPoint3 f;
                    Assert.Equal(fits, FixedPoint3.TryCreate(p, out f));
                    if (fits) {
                        ExactPoint3 back = f.ToExact();
                        Assert.Equal(p.X, back.X); Assert.Equal(p.Y, back.Y); Assert.Equal(p.Z, back.Z); Assert.Equal(p.W, back.W);
                        Assert.Equal(p.W.IsOne, f.WIsOne);
                    }
                }
                // the portable paths give the same limbs and values
                ulong* a = stackalloc ulong[4], b = stackalloc ulong[4];
                for (int i = 0; i < 4; ++i) a[i] = b[i] = 0;
                byte ma, mb;
                bool fa = FixedPoint3.Load(v, a, out ma), fb = FixedPoint3.LoadPortable(v, b, out mb);
                Assert.Equal(fits, fa);
                Assert.Equal(fits, fb);
                if (fits) {
                    Assert.Equal(ma, mb);
                    for (int i = 0; i < 4; ++i) Assert.Equal(a[i], b[i]);
                    Assert.Equal(v, FixedPoint3.ToBig(a, ma));
                    int len = ma & 7;
                    if (len > 0)
                        Assert.Equal(BigInteger.Abs(v), FixedPoint3.ToBigPortable(a, len));
                    Assert.Equal(v.IsZero ? 0 : (int)((ExactPoint3.BitLength(v) + 63) / 64), len);
                }
            }
        }

        [Fact]
        public void UsedLimbs_AreTheMinimum()
        {
            var rand = new Random(2);
            for (int bits = 0; bits <= 255; ++bits) {
                BigInteger v = RandomBits(rand, bits);
                if (rand.Next(2) == 0) v = -v;
                ulong* a = stackalloc ulong[4];
                for (int i = 0; i < 4; ++i) a[i] = 0;
                byte m;
                Assert.True(FixedPoint3.Load(v, a, out m));
                int len = m & 7;
                Assert.Equal((bits + 63) / 64, len);
                Assert.Equal(v.Sign < 0, (m & 0x80) != 0);
                for (int i = len; i < 4; ++i) Assert.Equal(0UL, a[i]);
            }
        }

        [Fact]
        public void SameValueAndHash_MatchExactPoint3Equality()
        {
            var rand = new Random(3);
            var points = new List<ExactPoint3>();
            for (int k = 0; k < 400; ++k)
                points.Add(ExactPoint3.Create(RandomValue(rand, 254, false), RandomValue(rand, 254, false), RandomValue(rand, 254, false), RandomPositive(rand, 254, false)));
            // and near-equal ones: one component off by one, or the sign
            for (int k = 0; k < 200; ++k) {
                ExactPoint3 p = points[k];
                points.Add(ExactPoint3.CreateUnreduced(p.X, p.Y, p.Z + 1, p.W));
                points.Add(ExactPoint3.CreateUnreduced(-p.X, p.Y, p.Z, p.W));
                points.Add(ExactPoint3.CreateUnreduced(p.X, p.Y, p.Z, p.W));
            }
            var fixeds = points.Select(Fixed).ToArray();
            for (int i = 0; i < points.Count; ++i)
                for (int j = i; j < Math.Min(points.Count, i + 50); ++j) {
                    bool eq = points[i].Equals(points[j]);
                    Assert.Equal(eq, FixedPoint3.SameValue(ref fixeds[i], ref fixeds[j]));
                    if (eq) Assert.Equal(fixeds[i].Hash(), fixeds[j].Hash());
                }
            for (int i = 0; i < 200; ++i) {
                int j = 400 + 3 * i + 2;        // the copy of point i
                Assert.True(FixedPoint3.SameValue(ref fixeds[i], ref fixeds[j]));
                Assert.Equal(fixeds[i].Hash(), fixeds[j].Hash());
            }
        }

        [Fact]
        public void MulHighPortable_MatchesBigInteger()
        {
            var rand = new Random(4);
            var values = new List<ulong> { 0, 1, 2, uint.MaxValue, (ulong)uint.MaxValue + 1, ulong.MaxValue, ulong.MaxValue - 1, 1UL << 63 };
            var buf = new byte[8];
            for (int k = 0; k < 2000; ++k) { rand.NextBytes(buf); values.Add(BitConverter.ToUInt64(buf, 0)); }
            for (int i = 0; i < values.Count; i += (i < 8) ? 1 : 3)
                for (int j = 0; j < values.Count; j += 37) {
                    ulong a = values[i], b = values[j];
                    BigInteger exact = new BigInteger(a) * new BigInteger(b);
                    ulong lo, hi = FixedArith.MulHighPortable(a, b, out lo);
                    Assert.Equal(exact, (new BigInteger(hi) << 64) + new BigInteger(lo));
                    ulong lo2, hi2 = FixedArith.MulHigh(a, b, out lo2);
                    Assert.Equal(hi, hi2);
                    Assert.Equal(lo, lo2);
                }
        }

        // ---- the predicates against their BigInteger formulas (as in ExactMeshArrangement) ----

        static BigInteger Det3(BigInteger a, BigInteger b, BigInteger c, BigInteger d, BigInteger e, BigInteger f, BigInteger g, BigInteger h, BigInteger i)
        {
            return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        }

        static int OrientBig(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, int i, int j)
        {
            return Det3(a[i], a[j], a.W, b[i], b[j], b.W, c[i], c[j], c.W).Sign;
        }

        static void Row(ExactPoint3 v, ExactPoint3 d, int i, int j, out BigInteger x, out BigInteger y, out BigInteger l)
        {
            BigInteger A, B;
            if (v.W.IsOne && d.W.IsOne) {
                A = v[i] - d[i]; B = v[j] - d[j];
                x = A; y = B;
            } else {
                A = v[i] * d.W - d[i] * v.W; B = v[j] * d.W - d[j] * v.W;
                BigInteger s = v.W * d.W;
                x = A * s; y = B * s;
            }
            l = A * A + B * B;
        }

        static int InCircleBig(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d, int i, int j)
        {
            BigInteger ax, ay, al, bx, by, bl, cx, cy, cl;
            Row(a, d, i, j, out ax, out ay, out al);
            Row(b, d, i, j, out bx, out by, out bl);
            Row(c, d, i, j, out cx, out cy, out cl);
            return Det3(ax, ay, al, bx, by, bl, cx, cy, cl).Sign;
        }

        static readonly BigInteger[] AlphaBig = ExactMeshArrangement.Alpha.Select(a => new BigInteger(a * 9007199254740992.0)).ToArray();     // a 2^53

        static int CompareBig(ExactPoint3 pa, ExactPoint3 pb)
        {
            BigInteger fa = (AlphaBig[0] * pa.X + AlphaBig[1] * pa.Y + AlphaBig[2] * pa.Z) * pb.W;
            BigInteger fb = (AlphaBig[0] * pb.X + AlphaBig[1] * pb.Y + AlphaBig[2] * pb.Z) * pa.W;
            int c = fa.CompareTo(fb);
            if (c != 0) return c;
            for (int k = 0; k < 3; ++k) {
                c = (pa[k] * pb.W).CompareTo(pb[k] * pa.W);
                if (c != 0) return c;
            }
            return 0;
        }

        static ulong[] AlphaLimbs { get { return AlphaBig.Select(a => (ulong)a).ToArray(); } }

        static ExactPoint3 RandomPoint(Random rand, int maxBits, bool integer)
        {
            return ExactPoint3.CreateUnreduced(RandomValue(rand, maxBits), RandomValue(rand, maxBits), RandomValue(rand, maxBits),
                integer ? BigInteger.One : RandomPositive(rand, maxBits));
        }

        // the extreme cases: every component at +-(2^255 - 1), which reaches the largest values of the bound table
        static ExactPoint3 ExtremePoint(Random rand)
        {
            Func<BigInteger> c = () => (rand.Next(2) == 0) ? Limit : -Limit;
            return ExactPoint3.CreateUnreduced(c(), c(), c(), Limit);
        }

        [Fact]
        public void Kernels_MatchBigInteger_AtAllSizes()
        {
            var rand = new Random(5);
            ulong[] alpha = AlphaLimbs;
            for (int k = 0; k < 6000; ++k) {
                int maxBits = new[] { 8, 60, 64, 110, 128, 200, 255 }[k % 7];
                bool integer = (k % 5 == 0);
                ExactPoint3 a, b, c, d;
                if (k % 11 == 0) { a = ExtremePoint(rand); b = ExtremePoint(rand); c = ExtremePoint(rand); d = ExtremePoint(rand); }
                else { a = RandomPoint(rand, maxBits, integer); b = RandomPoint(rand, maxBits, integer); c = RandomPoint(rand, maxBits, integer); d = RandomPoint(rand, maxBits, integer); }
                if (k % 13 == 0) c = a;                     // degenerate: zero determinants
                if (k % 17 == 0) d = b;
                int i = k % 3, j = (i + 1 + (k / 3) % 2) % 3;
                if (i == j) j = (i + 1) % 3;
                FixedPoint3 fa = Fixed(a), fb = Fixed(b), fc = Fixed(c), fd = Fixed(d);
                Assert.Equal(OrientBig(a, b, c, i, j), FixedArith.Orient(ref fa, ref fb, ref fc, i, j));
                Assert.Equal(InCircleBig(a, b, c, d, i, j), FixedArith.InCircle(ref fa, ref fb, ref fc, ref fd, i, j));
                Assert.Equal(CompareBig(a, b), FixedArith.ComparePoints(ref fa, ref fb, alpha[0], alpha[1], alpha[2]));
                Assert.Equal(CompareBig(a, c), FixedArith.ComparePoints(ref fa, ref fc, alpha[0], alpha[1], alpha[2]));
            }
        }

        [Fact]
        public void Kernels_ExactZeros_CollinearCocircularEqualKeys()
        {
            var rand = new Random(6);
            ulong[] alpha = AlphaLimbs;
            for (int k = 0; k < 500; ++k) {
                // collinear: c = a + t (b - a) in homogeneous coordinates, with large weights
                BigInteger w = RandomPositive(rand, 60, false);
                ExactPoint3 a = ExactPoint3.CreateUnreduced(RandomValue(rand, 120, false), RandomValue(rand, 120, false), RandomValue(rand, 120, false), w);
                ExactPoint3 b = ExactPoint3.CreateUnreduced(RandomValue(rand, 120, false), RandomValue(rand, 120, false), RandomValue(rand, 120, false), w);
                BigInteger t = RandomValue(rand, 30, false);
                ExactPoint3 c = ExactPoint3.CreateUnreduced(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y), a.Z + t * (b.Z - a.Z), w);
                FixedPoint3 fa = Fixed(a), fb = Fixed(b), fc = Fixed(c);
                for (int i = 0; i < 3; ++i) {
                    int j = (i + 1) % 3;
                    Assert.Equal(0, OrientBig(a, b, c, i, j));
                    Assert.Equal(0, FixedArith.Orient(ref fa, ref fb, ref fc, i, j));
                }
                // cocircular: points of the circle x^2 + y^2 = 25 scaled by s, over a common weight
                var circle = new[] { (3, 4), (5, 0), (0, 5), (-3, -4), (-4, 3), (4, -3) }.OrderBy(_ => rand.Next()).Take(4).ToArray();
                BigInteger s = RandomPositive(rand, 150, false), ww = RandomPositive(rand, 50, false), ox = RandomValue(rand, 150, false), oy = RandomValue(rand, 150, false);
                var pts = circle.Select(q => ExactPoint3.CreateUnreduced(ox + s * q.Item1, oy + s * q.Item2, RandomValue(rand, 100, false), ww)).ToArray();
                var f = pts.Select(Fixed).ToArray();
                Assert.Equal(0, InCircleBig(pts[0], pts[1], pts[2], pts[3], 0, 1));
                Assert.Equal(0, FixedArith.InCircle(ref f[0], ref f[1], ref f[2], ref f[3], 0, 1));
                // equal order keys: alpha . (a - b) = 0 with a != b is impossible for the irrational-like alpha only in
                // the limit; test equal points and points differing in one coordinate
                ExactPoint3 e = ExactPoint3.CreateUnreduced(a.X * 3, a.Y * 3, a.Z * 3, a.W * 3);     // the same point, other weight
                FixedPoint3 fe = Fixed(e);
                Assert.Equal(0, CompareBig(a, e));
                Assert.Equal(0, FixedArith.ComparePoints(ref fa, ref fe, alpha[0], alpha[1], alpha[2]));
            }
        }

        [Fact]
        public void ComparePoints_EqualOrderKeys_DecidedByTheCoordinates()
        {
            // alpha . (b - a) = 0 for b - a = (0, alpha2, -alpha1) / g (and for (alpha1, -alpha0, 0) / g): the order key
            // ties, the coordinates decide, in y (resp. x) after an equal x (resp. none)
            ulong[] alpha = AlphaLimbs;
            BigInteger a0 = AlphaBig[0], a1 = AlphaBig[1], a2 = AlphaBig[2];
            BigInteger g12 = BigInteger.GreatestCommonDivisor(a1, a2), g01 = BigInteger.GreatestCommonDivisor(a0, a1);
            var rand = new Random(8);
            for (int k = 0; k < 200; ++k) {
                BigInteger w = (k % 2 == 0) ? BigInteger.One : RandomPositive(rand, 60, false);
                ExactPoint3 a = ExactPoint3.CreateUnreduced(RandomValue(rand, 150, false), RandomValue(rand, 150, false), RandomValue(rand, 150, false), w);
                BigInteger t = RandomValue(rand, 20, false);
                if (t.IsZero) t = 1;
                var others = new[] {
                    ExactPoint3.CreateUnreduced(a.X, a.Y + t * w * a2 / g12, a.Z - t * w * a1 / g12, w),
                    ExactPoint3.CreateUnreduced(a.X + t * w * a1 / g01, a.Y - t * w * a0 / g01, a.Z, w),
                };
                foreach (ExactPoint3 b in others) {
                    FixedPoint3 fa = Fixed(a), fb = Fixed(b);
                    int expected = CompareBig(a, b);
                    Assert.NotEqual(0, expected);
                    Assert.Equal(expected, FixedArith.ComparePoints(ref fa, ref fb, alpha[0], alpha[1], alpha[2]));
                    Assert.Equal(-expected, FixedArith.ComparePoints(ref fb, ref fa, alpha[0], alpha[1], alpha[2]));
                }
            }
        }

        [Fact]
        public void ExactEquals_FixedAndBigInteger_NeverEqual()
        {
            var mesh = new DMesh3();
            mesh.AppendVertex(new Vector3d(0, 0, 0)); mesh.AppendVertex(new Vector3d(1, 0, 0)); mesh.AppendVertex(new Vector3d(0, 1, 0));
            mesh.AppendTriangle(0, 1, 2);
            var arr = new ExactMeshArrangement(new List<DMesh3>() { mesh });
            arr.Compute();
            BigInteger huge = BigInteger.One << 300;
            int small = arr.AddPoint(ExactPoint3.FromInteger(1, 2, 3));
            int big = arr.AddPoint(ExactPoint3.FromInteger(huge, 2, 3));
            int big2 = arr.AddPoint(ExactPoint3.FromInteger(huge + 1, 2, 3));
            int small2 = arr.AddPoint(ExactPoint3.Create(2, 4, 6, 2));       // the same point as small
            Assert.True(arr.IsExactFixed(small));
            Assert.False(arr.IsExactFixed(big));
            Assert.Equal(small, small2);
            Assert.False(arr.ExactEquals(small, big));
            Assert.False(arr.ExactEquals(big, small));
            Assert.False(arr.ExactEquals(big, big2));
            Assert.True(arr.ExactEquals(big, big));
            Assert.Equal(arr.ExactHash(small), arr.ExactHash(small2));
        }

        [Fact]
        public void BitLength_MatchesPortable()
        {
            var rand = new Random(9);
            var values = new List<BigInteger> { 0, 1, -1, 2, 255, 256, -256, BigInteger.One << 64, (BigInteger.One << 64) - 1 };
            for (int bits = 1; bits <= 1100; bits += (bits < 300) ? 1 : 17)
                values.Add(RandomBits(rand, bits));
            foreach (BigInteger v in values) {
                long expected = 0;
                for (BigInteger a = BigInteger.Abs(v); a.IsZero == false; a >>= 1) expected++;
                Assert.Equal(ExactPoint3.BitLengthPortable(BigInteger.Abs(v)), ExactPoint3.BitLength(v));
                Assert.Equal(expected, ExactPoint3.BitLength(v));
                Assert.Equal(expected, ExactPoint3.BitLength(-v));
            }
        }

        [Fact]
        public void TryExactDouble_IsTheConversionOfTheExactPoint()
        {
            // front_winding takes the corners' doubles from the arrangement (ExactMeshBoolean.corner_double) instead of
            // converting their exact points: the same doubles, bit for bit, for both representations
            DMesh3 box = Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1));
            DMesh3 sphere = Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 6);
            foreach (bool fixedWidth in new[] { true, false }) {
                var arr = new ExactMeshArrangement(new List<DMesh3>() { box, sphere }) { FixedWidthPoints = fixedWidth };
                arr.Compute();
                int doubles = 0;
                for (int id = 0; id < arr.PointCount; ++id) {
                    Vector3d d;
                    ExactPoint3 p = arr.Exact(id);
                    if (arr.TryExactDouble(id, out d) == false)
                        continue;
                    doubles++;
                    Assert.True(p.W.IsOne);
                    Assert.Equal(BitConverter.DoubleToInt64Bits((double)p.X), BitConverter.DoubleToInt64Bits(d.x));
                    Assert.Equal(BitConverter.DoubleToInt64Bits((double)p.Y), BitConverter.DoubleToInt64Bits(d.y));
                    Assert.Equal(BitConverter.DoubleToInt64Bits((double)p.Z), BitConverter.DoubleToInt64Bits(d.z));
                    Assert.Equal(p.X, new BigInteger(d.x)); Assert.Equal(p.Y, new BigInteger(d.y)); Assert.Equal(p.Z, new BigInteger(d.z));
                }
                Assert.True(doubles > 0);
            }
        }

        // ---- whole booleans: bit-identical with and without the fixed-width points ----

        static DMesh3 Box(Vector3d lo, Vector3d hi)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(lo, hi)) }.Generate().MakeDMesh();
        }

        static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices)
        {
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(s, center);
            return s;
        }

        static string Fingerprint(ExactMeshBoolean b)
        {
            var sb = new StringBuilder();
            DMesh3 m = b.Result;
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                sb.Append(t).Append(':').Append(f.a).Append(',').Append(f.b).Append(',').Append(f.c).Append(';');
            }
            if (b.ResultExactVertices != null)
                sb.Append('|').Append(string.Join(";", b.ResultExactVertices.Select(p => $"{p.X},{p.Y},{p.Z},{p.W}")));
            return sb.ToString();
        }

        // the boolean with fixed-width points equals the one without (bit for bit); returns the fixed-width one
        static ExactMeshBoolean AssertSameWithoutFixedWidth(Func<ExactMeshBoolean> make, bool disableFilters = false)
        {
            ExactMeshBoolean on = make(), off = make();
            on.ComputeExactVertices = off.ComputeExactVertices = true;
            on.DisableFilters = off.DisableFilters = disableFilters;
            off.FixedWidthPoints = false;
            Assert.True(on.Compute(), on.Error);
            Assert.True(off.Compute(), off.Error);
            Assert.Equal(Fingerprint(off), Fingerprint(on));
            ResultChecks.AssertClosed(on.Result);
            Assert.True(Invariance.AllFinite(on.Result));
            return on;
        }

        // how many of the arrangement's computed exact points are in fixed width, and how many are not
        static (int fixedCount, int bigCount) Representations(ExactMeshArrangement arr)
        {
            int f = 0, b = 0;
            for (int id = 0; id < arr.PointCount; ++id) {
                if (arr.IsExactFixed(id)) f++; else b++;
            }
            return (f, b);
        }

        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Union, false)]
        [InlineData(ExactMeshBoolean.Operations.Intersection, false)]
        [InlineData(ExactMeshBoolean.Operations.Difference, false)]
        [InlineData(ExactMeshBoolean.Operations.Difference, true)]
        public void BoxAndSphere_SameWithoutFixedWidth(ExactMeshBoolean.Operations op, bool disableFilters)
        {
            DMesh3 box = Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1));
            DMesh3 sphere = Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 6);
            var b = AssertSameWithoutFixedWidth(() => new ExactMeshBoolean(box, sphere, op), disableFilters);
            var (f, big) = Representations(b.Arrangement);
            Assert.True(f > 0);
            Assert.Equal(0, big);       // small coordinates: every point fits
        }

        [Fact]
        public void CoplanarBoxGrid_SameWithoutFixedWidth()
        {
            // n-ary, coplanar faces: merged points, cocircular points (InCircle exact stages), stacks
            var boxes = new List<DMesh3>();
            for (int i = 0; i < 2; ++i)
                for (int j = 0; j < 2; ++j)
                    for (int k = 0; k < 2; ++k) {
                        boxes.Add(Box(new Vector3d(0.9 * i, 0.9 * j, 0.9 * k), new Vector3d(0.9 * i + 1, 0.9 * j + 1, 0.9 * k + 1)));
                        boxes.Add(Box(new Vector3d(0.9 * i + 0.25, 0.9 * j + 0.25, 0.9 * k + 0.25), new Vector3d(0.9 * i + 0.75, 0.9 * j + 0.75, 0.9 * k + 0.75)));
                    }
            Func<int[], bool> outerMinusInner = (w) => w.Where((x, m) => m % 2 == 0 && x > 0).Any() && w.Where((x, m) => m % 2 == 1 && x > 0).Any() == false;
            AssertSameWithoutFixedWidth(() => new ExactMeshBoolean(boxes, outerMinusInner));
            AssertSameWithoutFixedWidth(() => new ExactMeshBoolean(boxes, outerMinusInner), true);
        }

        [Fact]
        public void MixedRepresentations_SameWithoutFixedWidth()
        {
            // a far-away tiny box makes the grid fine (ScaleExp small): the coordinates of the large meshes become
            // integers of about 2^200, so their constructed points exceed 2^255 and stay BigInteger, while the input
            // points and the points of the tiny box are fixed
            double big = 1e30, tiny = 1e-30;
            DMesh3 a = Box(new Vector3d(-big, -1.1 * big, -0.9 * big), new Vector3d(0.3 * big, 0.6 * big, 1.1 * big));
            MeshEditor.Append(a, Box(new Vector3d(5 * big, 5 * big, 5 * big), new Vector3d(5 * big + tiny, 5 * big + tiny, 5 * big + 2 * tiny)));
            DMesh3 b = Sphere(new Vector3d(-0.6 * big, -0.5 * big, 0.2 * big), 0.7 * big, 6);
            foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Difference }) {
                var r = AssertSameWithoutFixedWidth(() => new ExactMeshBoolean(a, b, op));
                var (f, bigCount) = Representations(r.Arrangement);
                Assert.True(f > 0, "no point in fixed width");
                Assert.True(bigCount > 0, "no point beyond the fixed width");
            }
        }

        [Fact]
        public void ExactPoints_ComputedConcurrently_AreComplete()
        {
            // many threads compute the exact points of the same ids at once: every reader sees the whole value
            DMesh3 box = Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1));
            DMesh3 sphere = Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 8);
            for (int run = 0; run < 5; ++run) {
                // (both computed on one thread: the same point ids)
                ExactMeshArrangement reference, arr;
                gParallel.SerialOnThisThread = true;
                try {
                    reference = new ExactMeshArrangement(new List<DMesh3>() { box, sphere }) { FixedWidthPoints = false };
                    reference.Compute();
                    arr = new ExactMeshArrangement(new List<DMesh3>() { box, sphere });
                    arr.Compute();
                } finally {
                    gParallel.SerialOnThisThread = false;
                }
                Assert.Equal(reference.PointCount, arr.PointCount);
                int n = arr.PointCount;
                // (most implicit points have not been computed by Compute: the filters decided without them)
                var ids = Enumerable.Range(0, n).SelectMany(id => Enumerable.Repeat(id, 4)).OrderBy(id => (id * 7919L + run) % 104729).ToArray();
                var got = new ExactPoint3[ids.Length];
                System.Threading.Tasks.Parallel.For(0, ids.Length, k => got[k] = arr.Exact(ids[k]));
                for (int k = 0; k < ids.Length; ++k)
                    Assert.Equal(reference.Exact(ids[k]), got[k]);
            }
        }
    }
}
