using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactLimbSigns.Orient2 and InCircle (the last stages of ExactMeshArrangement.Orient and InCircle, for points
    /// beyond the fixed width): the signs equal reference determinants written here (the 3 x 3 expansion, and for the
    /// in-circle test the 4 x 4 determinant of the lifted points, another formula than the kernel's), for operands of
    /// every width up to and beyond the stack limit, in the three projections, with exact zeros (collinear and
    /// cocircular points by construction) and values one unit off; the path of every call (stack limbs or the
    /// BigInteger form) and its scratch use against the derived need.
    /// </summary>
    public class ExactLimbSignsTestsPlanar
    {
        static readonly int[][] Axes = { new[] { 1, 2 }, new[] { 2, 0 }, new[] { 0, 1 } };

        static BigInteger Rand(Random r, int bits) { return ExactLimbSignsTests.Rand(r, bits); }
        static int Limbs(BigInteger v) { return ExactLimbSignsTests.Limbs(v); }
        static BigInteger Full(int n) { return ExactLimbSignsTests.Full(n); }
        static bool StackLimbs { get { return ExactLimbSignsTests.StackLimbs; } }

        // the limbs of the widest of the three entries of v that the determinants read, and their sum
        static int Row(ExactPoint3 v, int i, int j) { return Math.Max(Limbs(v[i]), Math.Max(Limbs(v[j]), Limbs(v.W))); }
        static int Sum(ExactPoint3 v, int i, int j) { return Limbs(v[i]) + Limbs(v[j]) + Limbs(v.W); }

        // a point with the entries x, y in the axes i, j, z in the third one
        static ExactPoint3 At(int i, int j, BigInteger x, BigInteger y, BigInteger z, BigInteger w)
        {
            var c = new BigInteger[3];
            c[i] = x; c[j] = y; c[3 - i - j] = z;
            return ExactPoint3.CreateUnreduced(c[0], c[1], c[2], w);
        }

        // the same point with another homogeneous factor
        static ExactPoint3 Times(ExactPoint3 v, BigInteger k) { return ExactPoint3.CreateUnreduced(v.X * k, v.Y * k, v.Z * k, v.W * k); }

        static ExactPoint3 Point(Random r, int limbs)
        {
            return ExactPoint3.CreateUnreduced(Rand(r, ExactLimbSignsTests.Width(r, limbs)), Rand(r, ExactLimbSignsTests.Width(r, limbs)), Rand(r, ExactLimbSignsTests.Width(r, limbs)),
                BigInteger.Abs(Rand(r, Math.Max(1, ExactLimbSignsTests.Width(r, limbs)))));
        }

        // the reference of Orient2: the determinant of the rows (v[i], v[j], v.W), expanded along the third column
        static int OrientBig(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, int i, int j)
        {
            BigInteger det = a.W * (b[i] * c[j] - b[j] * c[i]) - b.W * (a[i] * c[j] - a[j] * c[i]) + c.W * (a[i] * b[j] - a[j] * b[i]);
            return det.Sign;
        }

        // the reference of InCircle: the 4 x 4 determinant of the lifted points, rows (x w, y w, x^2 + y^2, w^2) =
        // w^2 (x / w, y / w, |.|^2, 1); the kernel evaluates the 3 x 3 determinant of the differences to d instead
        static int InCircleLifted(ExactPoint3 p, ExactPoint3 q, ExactPoint3 r, ExactPoint3 d, int i, int j)
        {
            var m = new BigInteger[4, 4];
            var v = new[] { p, q, r, d };
            for (int k = 0; k < 4; ++k) {
                m[k, 0] = v[k][i] * v[k].W; m[k, 1] = v[k][j] * v[k].W; m[k, 2] = v[k][i] * v[k][i] + v[k][j] * v[k][j]; m[k, 3] = v[k].W * v[k].W;
            }
            return ExactMeshArrangement.Det4(m).Sign;
        }

        static int OrientNeed(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, int i, int j) { return 5 * Row(a, i, j) + 14 * (Row(b, i, j) + Row(c, i, j)) + 11; }

        // the path of Orient2: the stack while operands and scratch space take at most MaxDeterminantLimbs limbs, and
        // then inside the derived need; returns whether that is inside the limit
        static bool CheckOrientPath(int used, ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, int i, int j, string what)
        {
            bool onStack = used >= 0;
            long limbs = Sum(a, i, j) + Sum(b, i, j) + Sum(c, i, j) + ExactLimbSigns.OrientScratch(Row(a, i, j), Row(b, i, j), Row(c, i, j));
            bool stack = StackLimbs && limbs <= ExactLimbSigns.MaxDeterminantLimbs;
            int need = OrientNeed(a, b, c, i, j);
            Assert.True(onStack == stack && (stack ? (used >= 0 && used <= need) : used == -1), $"{what}: on the stack {onStack}, {used} scratch limbs, {limbs} limbs in all, need {need}");
            return limbs <= ExactLimbSigns.MaxDeterminantLimbs;
        }

        static int InCircleNeed(ExactPoint3 p, ExactPoint3 q, ExactPoint3 r, ExactPoint3 d, int i, int j)
        {
            int wd = Row(d, i, j);
            return 27 * (Row(p, i, j) + wd) + 45 * (Row(q, i, j) + Row(r, i, j) + 2 * wd) + 99;
        }

        static bool CheckInCirclePath(int used, ExactPoint3 p, ExactPoint3 q, ExactPoint3 r, ExactPoint3 d, int i, int j, string what)
        {
            bool onStack = used >= 0;
            int wd = Row(d, i, j);
            long limbs = Sum(p, i, j) + Sum(q, i, j) + Sum(r, i, j) + Sum(d, i, j) + ExactLimbSigns.InCircleScratch(Row(p, i, j) + wd, Row(q, i, j) + wd, Row(r, i, j) + wd);
            bool stack = StackLimbs && limbs <= ExactLimbSigns.MaxDeterminantLimbs;
            int need = InCircleNeed(p, q, r, d, i, j);
            Assert.True(onStack == stack && (stack ? (used >= 0 && used <= need) : used == -1), $"{what}: on the stack {onStack}, {used} scratch limbs, {limbs} limbs in all, need {need}");
            return limbs <= ExactLimbSigns.MaxDeterminantLimbs;
        }

        static int Orient(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, int i, int j) { return ExactLimbSigns.Orient2(a, b, c, i, j); }
        static int InCircle(ExactPoint3 p, ExactPoint3 q, ExactPoint3 r, ExactPoint3 d, int i, int j) { return ExactLimbSigns.InCircle(p, q, r, d, i, j); }

        // The signs themselves, on points whose answer is known: a counter-clockwise triangle is positive, a point
        // inside the circle of a counter-clockwise triangle is positive; in each projection, on both paths.
        [Fact]
        public void KnownSigns()
        {
            BigInteger one = BigInteger.One;
            foreach (int[] ax in Axes) {
                int i = ax[0], j = ax[1];
                foreach (int w in new[] { 1, 7 }) {
                    // (w: another homogeneous factor, so that the general rows are taken too)
                    Func<int, int, ExactPoint3> at = (x, y) => Times(At(i, j, x, y, 5, one), w);
                    ExactPoint3 a = at(1, 0), b = at(0, 1), c = at(-1, 0);
                    foreach (bool big in new[] { false, true }) {
                        Func<ExactPoint3, ExactPoint3, ExactPoint3, int> orient = (u, v, x) => big ? ExactLimbSigns.Orient2Big(u, v, x, i, j) : ExactLimbSigns.Orient2(u, v, x, i, j);
                        Func<ExactPoint3, int> circle = x => big ? ExactLimbSigns.InCircleBig(a, b, c, x, i, j) : ExactLimbSigns.InCircle(a, b, c, x, i, j);
                        Assert.Equal(1, orient(a, b, c));
                        Assert.Equal(-1, orient(b, a, c));
                        Assert.Equal(0, orient(a, at(0, 0), c));
                        Assert.Equal(1, circle(at(0, 0)));
                        Assert.Equal(0, circle(at(0, -1)));
                        Assert.Equal(-1, circle(at(0, -2)));
                        Assert.Equal(-1, big ? ExactLimbSigns.InCircleBig(b, a, c, at(0, 0), i, j) : ExactLimbSigns.InCircle(b, a, c, at(0, 0), i, j));
                    }
                }
            }
        }

        [Fact]
        public void Orient2_MatchesBigInteger()
        {
            var r = new Random(21);
            int zeros = 0, nonzero = 0, fallback = 0, onStack = 0;
            for (int n = 0; n < 6000; ++n) {
                int i = Axes[n % 3][0], j = Axes[n % 3][1];
                // (widths up to 110 limbs and 9 beyond: three rows of 101 limbs are the widest uniform ones on the stack)
                ExactPoint3 a = Point(r, 110), b = Point(r, 110), c = Point(r, 110);
                int kind = (n / 3) % 3;
                if (kind != 0) {
                    // c on the line through a and b (integer points), or one unit beside it; then other homogeneous factors
                    a = ExactPoint3.CreateUnreduced(a.X, a.Y, a.Z, BigInteger.One); b = ExactPoint3.CreateUnreduced(b.X, b.Y, b.Z, BigInteger.One);
                    BigInteger s = Rand(r, r.Next(0, 70));
                    c = ExactPoint3.CreateUnreduced(a.X + s * (b.X - a.X) + (kind == 2 ? 1 : 0), a.Y + s * (b.Y - a.Y), a.Z + s * (b.Z - a.Z) + (kind == 2 ? 1 : 0), BigInteger.One);
                    a = Times(a, BigInteger.Abs(Rand(r, r.Next(1, 130)))); b = Times(b, BigInteger.Abs(Rand(r, r.Next(1, 130)))); c = Times(c, BigInteger.Abs(Rand(r, r.Next(1, 130))));
                }
                int expected = OrientBig(a, b, c, i, j);
                int used;
                int sign = ExactLimbSigns.Orient2(a, b, c, i, j, out used);
                if (CheckOrientPath(used, a, b, c, i, j, $"case {n}")) onStack++; else fallback++;
                Assert.True(sign == expected, $"case {n}");
                Assert.True(expected == ExactLimbSigns.Orient2Big(a, b, c, i, j), $"case {n}, the BigInteger form");
                // two points exchanged: the opposite sign
                Assert.True(-expected == Orient(b, a, c, i, j), $"case {n}, exchanged");
                if (expected == 0) zeros++; else nonzero++;
            }
            Assert.True(zeros > 1500 && nonzero > 3000 && fallback > 150 && onStack > 3000, $"{zeros} zeros, {nonzero} others, {fallback} beyond the limit, {onStack} inside");
        }

        [Fact]
        public void InCircle_MatchesBigInteger()
        {
            var r = new Random(22);
            int zeros = 0, nonzero = 0, fallback = 0, onStack = 0;
            for (int n = 0; n < 6000; ++n) {
                int i = Axes[n % 3][0], j = Axes[n % 3][1];
                // (widths up to 14 limbs and 9 beyond: four points of 17 limbs are the widest uniform ones on the stack)
                ExactPoint3 p = Point(r, 14), q = Point(r, 14), s = Point(r, 14), d = Point(r, 14);
                int kind = (n / 3) % 3;
                if (kind != 0) {
                    // four points of one circle (a point turned by quarter turns about an integer centre), or the fourth
                    // one unit off it; then other homogeneous factors
                    BigInteger cx = Rand(r, ExactLimbSignsTests.Width(r, 14)), cy = Rand(r, ExactLimbSignsTests.Width(r, 14)), u = Rand(r, r.Next(1, 500)), v = Rand(r, r.Next(1, 500));
                    Func<BigInteger> z = () => Rand(r, r.Next(0, 300));
                    Func<BigInteger> k = () => BigInteger.Abs(Rand(r, r.Next(1, 130)));
                    p = Times(At(i, j, cx + u, cy + v, z(), BigInteger.One), k());
                    q = Times(At(i, j, cx - v, cy + u, z(), BigInteger.One), k());
                    s = Times(At(i, j, cx - u, cy - v, z(), BigInteger.One), k());
                    d = Times(At(i, j, cx + v + (kind == 2 ? 1 : 0), cy - u, z(), BigInteger.One), k());
                }
                int expected = InCircleLifted(p, q, s, d, i, j);
                int used;
                int sign = ExactLimbSigns.InCircle(p, q, s, d, i, j, out used);
                if (CheckInCirclePath(used, p, q, s, d, i, j, $"case {n}")) onStack++; else fallback++;
                Assert.True(sign == expected, $"case {n}");
                Assert.True(expected == ExactLimbSigns.InCircleBig(p, q, s, d, i, j), $"case {n}, the BigInteger form");
                // two points of the triangle exchanged: the opposite sign
                Assert.True(-expected == InCircle(q, p, s, d, i, j), $"case {n}, exchanged");
                if (expected == 0) zeros++; else nonzero++;
            }
            Assert.True(zeros > 1500 && nonzero > 3000 && fallback > 300 && onStack > 3000, $"{zeros} zeros, {nonzero} others, {fallback} beyond the limit, {onStack} inside");
        }

        // a point whose entries in the axes i, j and W have the given limbs, with every bit below the sign bit set (the
        // entry in the third axis, which the determinants do not read, is the widest of all)
        static ExactPoint3 Wide(int i, int j, int x, int y, int w, int variant)
        {
            return At(i, j, (variant % 2 == 0) ? Full(x) - variant : -Full(x) + variant, Full(y) - 3 * variant, Full(600), Full(w) - 5 * variant);
        }

        // The limit is on the operands and the scratch space together, and the scratch space follows the rows.
        [Fact]
        public void Limits_AreOperandsAndScratch()
        {
            int used;
            foreach (int[] ax in Axes) {
                int i = ax[0], j = ax[1];
                // Orient2, rows of 101 limbs (45 x 101 + 32 = 4577 limbs in all), the first row with one entry of 105
                // (4605), and a second entry of it of 104 or 105 limbs: 4608 and 4609
                foreach (int e in new[] { 104, 105 }) {
                    ExactPoint3 a = Wide(i, j, 105, e, 101, 0), b = Wide(i, j, 101, 101, 101, 1), c = Wide(i, j, 101, 101, 101, 2);
                    int sign = ExactLimbSigns.Orient2(a, b, c, i, j, out used);
                    Assert.Equal(e == 104, CheckOrientPath(used, a, b, c, i, j, $"orient, {e}"));
                    Assert.Equal(StackLimbs && e == 104, used >= 0);
                    Assert.Equal(OrientBig(a, b, c, i, j), sign);
                }
                // one wide row in each position: [row, limbs, inside]
                foreach (int[] w in new[] { new[] { 0, 400, 1 }, new[] { 0, 520, 0 }, new[] { 1, 200, 1 }, new[] { 1, 260, 0 }, new[] { 2, 200, 1 }, new[] { 2, 260, 0 } }) {
                    var v = new ExactPoint3[3];
                    for (int k = 0; k < 3; ++k) v[k] = (k == w[0]) ? Wide(i, j, w[1], w[1], w[1], k) : Wide(i, j, 1, 1, 1, k);
                    int sign = ExactLimbSigns.Orient2(v[0], v[1], v[2], i, j, out used);
                    Assert.Equal(w[2] == 1, CheckOrientPath(used, v[0], v[1], v[2], i, j, $"orient, row {w[0]} of {w[1]} limbs"));
                    Assert.Equal(StackLimbs && w[2] == 1, used >= 0);
                    Assert.Equal(OrientBig(v[0], v[1], v[2], i, j), sign);
                }
                // InCircle, points of 17 limbs with the first one (24, e, 17): 4608 limbs in all for e = 10, 4609 for 11
                foreach (int e in new[] { 10, 11 }) {
                    ExactPoint3 p = Wide(i, j, 24, e, 17, 0), q = Wide(i, j, 17, 17, 17, 1), r = Wide(i, j, 17, 17, 17, 2), d = Wide(i, j, 17, 17, 17, 3);
                    int sign = ExactLimbSigns.InCircle(p, q, r, d, i, j, out used);
                    Assert.Equal(e == 10, CheckInCirclePath(used, p, q, r, d, i, j, $"in-circle, {e}"));
                    Assert.Equal(StackLimbs && e == 10, used >= 0);
                    Assert.Equal(InCircleLifted(p, q, r, d, i, j), sign);
                }
                // one wide point in each position
                foreach (int[] w in new[] { new[] { 0, 100, 1 }, new[] { 0, 140, 0 }, new[] { 1, 60, 1 }, new[] { 1, 90, 0 }, new[] { 2, 60, 1 }, new[] { 2, 90, 0 }, new[] { 3, 30, 1 }, new[] { 3, 36, 0 } }) {
                    var v = new ExactPoint3[4];
                    for (int k = 0; k < 4; ++k) v[k] = (k == w[0]) ? Wide(i, j, w[1], w[1], w[1], k) : Wide(i, j, 1, 1, 1, k);
                    int sign = ExactLimbSigns.InCircle(v[0], v[1], v[2], v[3], i, j, out used);
                    Assert.Equal(w[2] == 1, CheckInCirclePath(used, v[0], v[1], v[2], v[3], i, j, $"in-circle, point {w[0]} of {w[1]} limbs"));
                    Assert.Equal(StackLimbs && w[2] == 1, used >= 0);
                    Assert.Equal(InCircleLifted(v[0], v[1], v[2], v[3], i, j), sign);
                }
            }
        }

        // an entry of `limbs` limbs: random bits below the top one, which is set
        static BigInteger Dense(Random r, int limbs)
        {
            byte[] bytes = new byte[8 * limbs + 1];
            r.NextBytes(bytes);
            bytes[8 * limbs] = 0; bytes[8 * limbs - 1] = (byte)(0x40 | (bytes[8 * limbs - 1] & 0x3F));
            return r.Next(2) == 0 ? new BigInteger(bytes) : -new BigInteger(bytes);
        }

        // At the widest uniform operands on the stack, without a relation between them, a call takes nearly all of the
        // derived need: the need is not padded.
        [Fact]
        public void LargestOperands_StayInsideTheDerivedScratchNeed()
        {
            var r = new Random(23);
            for (int n = 0; n < 20; ++n) {
                Func<int, ExactPoint3> point = limbs => ExactPoint3.CreateUnreduced(Dense(r, limbs), Dense(r, limbs), Dense(r, limbs), BigInteger.Abs(Dense(r, limbs)));
                int used;
                ExactPoint3 a = point(101), b = point(101), c = point(101);
                int sign = ExactLimbSigns.Orient2(a, b, c, 1, 2, out used);
                Assert.True(CheckOrientPath(used, a, b, c, 1, 2, $"orient {n}"));
                Assert.Equal(33 * 101 + 11, OrientNeed(a, b, c, 1, 2));
                Assert.Equal(OrientBig(a, b, c, 1, 2), sign);
                if (StackLimbs) {
                    Assert.True(used >= 33 * 101, $"orient {n}: {used} scratch limbs of {OrientNeed(a, b, c, 1, 2)}");
                }
                ExactPoint3 p = point(17), q = point(17), s = point(17), d = point(17);
                sign = ExactLimbSigns.InCircle(p, q, s, d, 1, 2, out used);
                Assert.True(CheckInCirclePath(used, p, q, s, d, 1, 2, $"in-circle {n}"));
                Assert.Equal(117 * 34 + 99, InCircleNeed(p, q, s, d, 1, 2));
                Assert.Equal(InCircleLifted(p, q, s, d, 1, 2), sign);
                if (StackLimbs) {
                    Assert.True(used >= 117 * 34, $"in-circle {n}: {used} scratch limbs of {InCircleNeed(p, q, s, d, 1, 2)}");
                }
            }
        }

        // Signs that do not depend on the frame: a common translation and a common factor 2^k of integer points, and
        // another positive homogeneous factor of a point, keep the sign in each projection.
        [Fact]
        public void TranslationScaleAndHomogeneousFactor_KeepTheSign()
        {
            var r = new Random(24);
            for (int n = 0; n < 3000; ++n) {
                int i = Axes[n % 3][0], j = Axes[n % 3][1];
                var v = new ExactPoint3[4];
                for (int k = 0; k < 4; ++k) v[k] = ExactPoint3.CreateUnreduced(Rand(r, r.Next(1, 400)), Rand(r, r.Next(1, 400)), Rand(r, r.Next(1, 400)), BigInteger.One);
                if (n % 2 == 1) {
                    // (nearly cocircular and nearly collinear: the signs of small determinants)
                    v[2] = ExactPoint3.CreateUnreduced(2 * v[1].X - v[0].X + r.Next(-1, 2), 2 * v[1].Y - v[0].Y, 2 * v[1].Z - v[0].Z + r.Next(-1, 2), BigInteger.One);
                }
                int orient = Orient(v[0], v[1], v[2], i, j), circle = InCircle(v[0], v[1], v[2], v[3], i, j);
                BigInteger tx = Rand(r, r.Next(1, 600)), ty = Rand(r, r.Next(1, 600)), tz = Rand(r, r.Next(1, 600));
                int shift = r.Next(1, 200);
                var moved = new ExactPoint3[4]; var scaled = new ExactPoint3[4]; var other = new ExactPoint3[4];
                for (int k = 0; k < 4; ++k) {
                    moved[k] = ExactPoint3.CreateUnreduced(v[k].X + tx, v[k].Y + ty, v[k].Z + tz, BigInteger.One);
                    scaled[k] = ExactPoint3.CreateUnreduced(v[k].X << shift, v[k].Y << shift, v[k].Z << shift, BigInteger.One);
                    other[k] = Times(v[k], BigInteger.Abs(Rand(r, r.Next(1, 300))));
                }
                Assert.Equal(orient, Orient(moved[0], moved[1], moved[2], i, j));
                Assert.Equal(orient, Orient(scaled[0], scaled[1], scaled[2], i, j));
                Assert.Equal(orient, Orient(other[0], other[1], other[2], i, j));
                Assert.Equal(circle, InCircle(moved[0], moved[1], moved[2], moved[3], i, j));
                Assert.Equal(circle, InCircle(scaled[0], scaled[1], scaled[2], scaled[3], i, j));
                Assert.Equal(circle, InCircle(other[0], other[1], other[2], other[3], i, j));
            }
        }

#if NET
        // On the stack: no allocation inside the limit (the BigInteger determinants allocate every temporary).
        [Fact]
        public void InsideTheLimit_NoAllocation()
        {
            var r = new Random(25);
            var pts = new List<ExactPoint3[]>();
            for (int n = 0; n < 200; ++n) {
                var v = new ExactPoint3[4];
                for (int k = 0; k < 4; ++k) v[k] = ExactPoint3.CreateUnreduced(Rand(r, r.Next(1, 700)), Rand(r, r.Next(1, 700)), Rand(r, r.Next(1, 700)), BigInteger.Abs(Rand(r, r.Next(1, 700))));
                pts.Add(v);
            }
            long sum = 0, allocated = 0, big = 0;
            for (int pass = 0; pass < 3; ++pass) {
                long before = GC.GetAllocatedBytesForCurrentThread();
                foreach (var v in pts) {
                    int used;
                    sum += ExactLimbSigns.Orient2(v[0], v[1], v[2], 0, 1, out used);
                    if (used < 0) big++;
                    sum += ExactLimbSigns.InCircle(v[0], v[1], v[2], v[3], 1, 2, out used);
                    if (used < 0) big++;
                }
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Assert.True(allocated == 0 && big == 0, $"{allocated} bytes allocated in {3 * pts.Count} calls of each, {big} of them in BigIntegers (sum {sum})");
        }
#endif
    }
}
