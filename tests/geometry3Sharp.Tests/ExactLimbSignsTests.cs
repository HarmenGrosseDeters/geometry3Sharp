using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactLimbSigns: the signs of the plane side, the dot and the 4 x 4 determinant on stack limbs equal the BigInteger
    /// expressions, for operands of every width up to and beyond the stack limit, at the limb boundaries, with exact
    /// zeros, and without allocation.
    /// </summary>
    public class ExactLimbSignsTests
    {
        // a value of exactly `bits` bits (0: zero) with a random sign; every eighth one at a limb boundary pattern
        static BigInteger Rand(Random r, int bits)
        {
            if (bits <= 0) return BigInteger.Zero;
            BigInteger v;
            int kind = r.Next(8);
            if (kind == 0) v = BigInteger.One << (bits - 1);                         // a power of two
            else if (kind == 1) v = (BigInteger.One << bits) - 1;                    // all ones
            else {
                byte[] b = new byte[bits / 8 + 2];
                r.NextBytes(b);
                b[b.Length - 1] = 0;
                v = new BigInteger(b) & ((BigInteger.One << bits) - 1);
                v |= BigInteger.One << (bits - 1);
            }
            return r.Next(2) == 0 ? v : -v;
        }

        // widths: small, around the limb boundaries, the classification's sizes, up to the stack limit and beyond it
        static int Width(Random r)
        {
            switch (r.Next(8)) {
                case 0: return r.Next(0, 4);
                case 1: return 64 * r.Next(1, 14) + r.Next(-2, 3);
                case 2: return r.Next(1, 130);
                case 3: return r.Next(130, 900);
                case 4: return r.Next(900, 64 * ExactLimbSigns.MaxLimbs - 8);
                case 5: return 64 * ExactLimbSigns.MaxLimbs + r.Next(-70, 70);
                case 6: return r.Next(64 * ExactLimbSigns.MaxLimbs, 64 * ExactLimbSigns.MaxLimbs + 600);
                default: return r.Next(1, 400);
            }
        }

        [Fact]
        public void PlaneSide_MatchesBigInteger()
        {
            var r = new Random(11);
            int zeros = 0, wide = 0;
            for (int i = 0; i < 30000; ++i) {
                BigInteger a = Rand(r, Width(r)), b = Rand(r, Width(r)), c = Rand(r, Width(r)), x = Rand(r, Width(r)), y = Rand(r, Width(r)), z = Rand(r, Width(r));
                BigInteger w = Rand(r, Width(r)), d = Rand(r, Width(r));
                int kind = i % 4;
                if (kind == 1) { w = BigInteger.One; d = a * x + b * y + c * z; }                       // on the plane
                else if (kind == 2) { w = BigInteger.One; d = a * x + b * y + c * z + (r.Next(2) == 0 ? 1 : -1); }    // one unit off it
                int expected = (a * x + b * y + c * z - d * w).Sign;
                Assert.True(expected == ExactLimbSigns.PlaneSide(a, b, c, d, x, y, z, w), $"case {i}");
                if (expected == 0) zeros++;
                if (ExactPoint3.BitLength(a) > 64 * ExactLimbSigns.MaxLimbs) wide++;
            }
            Assert.True(zeros > 5000 && wide > 1000, $"{zeros} zeros, {wide} wide");
        }

        [Fact]
        public void Dot_MatchesBigInteger()
        {
            var r = new Random(12);
            int zeros = 0;
            for (int i = 0; i < 30000; ++i) {
                BigInteger a = Rand(r, Width(r)), b = Rand(r, Width(r)), c = Rand(r, Width(r)), x = Rand(r, Width(r)), y = Rand(r, Width(r)), z = Rand(r, Width(r));
                if (i % 3 == 1) { x = b; y = -a; z = BigInteger.Zero; }                 // orthogonal
                else if (i % 3 == 2) { x = b * c; y = -2 * a * c; z = a * b + (r.Next(2) == 0 ? 0 : 1); }   // orthogonal or one unit off
                int expected = (a * x + b * y + c * z).Sign;
                Assert.True(expected == ExactLimbSigns.Dot(a, b, c, x, y, z), $"case {i}");
                if (expected == 0) zeros++;
            }
            Assert.True(zeros > 5000, $"{zeros} zeros");
        }

        static ExactPoint3 Point(Random r)
        {
            BigInteger w = BigInteger.Abs(Rand(r, Math.Max(1, Width(r))));
            return ExactPoint3.CreateUnreduced(Rand(r, Width(r)), Rand(r, Width(r)), Rand(r, Width(r)), w);
        }

        [Fact]
        public void Det4_MatchesBigInteger()
        {
            var r = new Random(13);
            int zeros = 0, nonzero = 0;
            for (int i = 0; i < 6000; ++i) {
                ExactPoint3 a = Point(r), b = Point(r), c = Point(r), d = Point(r);
                if (i % 3 == 1) {
                    // d in the plane of a, b, c: integer points (W = 1), d = a + s (b - a) + t (c - a)
                    a = ExactPoint3.CreateUnreduced(Rand(r, Width(r)), Rand(r, Width(r)), Rand(r, Width(r)), BigInteger.One);
                    b = ExactPoint3.CreateUnreduced(Rand(r, Width(r)), Rand(r, Width(r)), Rand(r, Width(r)), BigInteger.One);
                    c = ExactPoint3.CreateUnreduced(Rand(r, Width(r)), Rand(r, Width(r)), Rand(r, Width(r)), BigInteger.One);
                    BigInteger s = Rand(r, r.Next(0, 70)), t = Rand(r, r.Next(0, 70));
                    BigInteger off = (i % 2 == 0) ? BigInteger.Zero : BigInteger.One;
                    d = ExactPoint3.CreateUnreduced(a.X + s * (b.X - a.X) + t * (c.X - a.X), a.Y + s * (b.Y - a.Y) + t * (c.Y - a.Y), a.Z + s * (b.Z - a.Z) + t * (c.Z - a.Z) + off, BigInteger.One);
                }
                int expected = ExactLimbSigns.Det4Big(a, b, c, d);
                Assert.True(expected == ExactLimbSigns.Det4(a, b, c, d), $"case {i}");
                // two rows exchanged: the opposite sign
                Assert.True(-expected == ExactLimbSigns.Det4(b, a, c, d), $"case {i}, rows exchanged");
                if (expected == 0) zeros++; else nonzero++;
            }
            Assert.True(zeros > 500 && nonzero > 3000, $"{zeros} zeros, {nonzero} others");
        }

        // The scratch space of each kernel holds its largest case: every operand at the stack limit with all bits set
        // (FixedArith throws when its arena is exhausted), and mixed widths.
        [Fact]
        public void LargestOperands_FitTheScratchSpace()
        {
            BigInteger big = (BigInteger.One << (64 * ExactLimbSigns.MaxLimbs - 1)) - 1;      // MaxLimbs limbs with its sign bit
            Assert.Equal((big * big + big * big + big * big - big * -big).Sign, ExactLimbSigns.PlaneSide(big, big, big, big, big, big, big, -big));
            Assert.Equal(1, ExactLimbSigns.Dot(big, big, big, big, big, big));
            var p = ExactPoint3.CreateUnreduced(big, -big, big, big);
            var q = ExactPoint3.CreateUnreduced(-big, big, big - 1, big);
            var s = ExactPoint3.CreateUnreduced(big, big - 2, -big, big - 3);
            var t = ExactPoint3.CreateUnreduced(big - 5, -big, -big, big);
            Assert.Equal(ExactLimbSigns.Det4Big(p, q, s, t), ExactLimbSigns.Det4(p, q, s, t));
            var r = new Random(14);
            for (int i = 0; i < 300; ++i) {
                Func<BigInteger> v = () => r.Next(3) == 0 ? (r.Next(2) == 0 ? big : -big) : Rand(r, r.Next(0, 3) * 64 + r.Next(1, 64));
                var a = ExactPoint3.CreateUnreduced(v(), v(), v(), BigInteger.Abs(v()) + 1);
                var b = ExactPoint3.CreateUnreduced(v(), v(), v(), BigInteger.Abs(v()) + 1);
                var c = ExactPoint3.CreateUnreduced(v(), v(), v(), BigInteger.Abs(v()) + 1);
                var d = ExactPoint3.CreateUnreduced(v(), v(), v(), BigInteger.Abs(v()) + 1);
                Assert.Equal(ExactLimbSigns.Det4Big(a, b, c, d), ExactLimbSigns.Det4(a, b, c, d));
                Assert.Equal((a.X * b.X + a.Y * b.Y + a.Z * b.Z - a.W * b.W).Sign, ExactLimbSigns.PlaneSide(a.X, a.Y, a.Z, a.W, b.X, b.Y, b.Z, b.W));
            }
        }

        // A common factor 2^k of the plane or of the point moves every operand across the limbs and keeps the sign;
        // a negative factor reverses it.
        [Fact]
        public void PowersOfTwo_KeepTheSign()
        {
            var r = new Random(15);
            for (int i = 0; i < 3000; ++i) {
                BigInteger a = Rand(r, r.Next(1, 300)), b = Rand(r, r.Next(1, 300)), c = Rand(r, r.Next(1, 300)), d = Rand(r, r.Next(1, 500));
                BigInteger x = Rand(r, r.Next(1, 700)), y = Rand(r, r.Next(1, 700)), z = Rand(r, r.Next(1, 700)), w = BigInteger.Abs(Rand(r, r.Next(1, 700)));
                int sign = ExactLimbSigns.PlaneSide(a, b, c, d, x, y, z, w);
                int k = r.Next(1, 200), j = r.Next(1, 200);
                Assert.Equal(sign, ExactLimbSigns.PlaneSide(a << k, b << k, c << k, d << k, x << j, y << j, z << j, w << j));
                Assert.Equal(-sign, ExactLimbSigns.PlaneSide(-(a << k), -(b << k), -(c << k), -(d << k), x, y, z, w));
                var p0 = ExactPoint3.CreateUnreduced(x, y, z, w); var p1 = ExactPoint3.CreateUnreduced(a, b, c, w + 1); var p2 = ExactPoint3.CreateUnreduced(d, a, x, w + 2); var p3 = ExactPoint3.CreateUnreduced(y, d, b, w + 3);
                int det = ExactLimbSigns.Det4(p0, p1, p2, p3);
                Assert.Equal(det, ExactLimbSigns.Det4(ExactPoint3.CreateUnreduced(x << k, y << k, z << k, w << k), p1, p2, p3));
            }
        }

#if NET
        // On the stack: no allocation for operands inside the limit (the BigInteger expressions allocate every temporary).
        [Fact]
        public void InsideTheLimit_NoAllocation()
        {
            var r = new Random(16);
            var cases = new List<BigInteger[]>();
            for (int i = 0; i < 200; ++i) {
                var v = new BigInteger[16];
                for (int k = 0; k < 16; ++k) v[k] = Rand(r, r.Next(1, 900));
                v[3] = BigInteger.Abs(v[3]) + 1; v[7] = BigInteger.Abs(v[7]) + 1; v[11] = BigInteger.Abs(v[11]) + 1; v[15] = BigInteger.Abs(v[15]) + 1;
                cases.Add(v);
            }
            var pts = new List<ExactPoint3[]>();
            foreach (var v in cases)
                pts.Add(new[] { ExactPoint3.CreateUnreduced(v[0], v[1], v[2], v[3]), ExactPoint3.CreateUnreduced(v[4], v[5], v[6], v[7]), ExactPoint3.CreateUnreduced(v[8], v[9], v[10], v[11]), ExactPoint3.CreateUnreduced(v[12], v[13], v[14], v[15]) });
            long sum = 0, allocated = 0;
            for (int pass = 0; pass < 3; ++pass) {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < cases.Count; ++i) {
                    var v = cases[i];
                    sum += ExactLimbSigns.PlaneSide(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]);
                    sum += ExactLimbSigns.Dot(v[0], v[1], v[2], v[4], v[5], v[6]);
                    sum += ExactLimbSigns.Det4(pts[i][0], pts[i][1], pts[i][2], pts[i][3]);
                }
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Assert.True(allocated == 0, $"{allocated} bytes allocated in {3 * cases.Count} calls (sum {sum})");
        }
#endif
    }
}
