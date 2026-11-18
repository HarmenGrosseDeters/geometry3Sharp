using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactLimbSigns: the signs of the plane side, the dot and the 4 x 4 determinant on stack limbs equal the BigInteger
    /// expressions, for operands of every width up to and beyond the stack limits, at the limb boundaries, with exact
    /// zeros, and without allocation. The path of every call (the stack or the BigInteger expression) is the one the
    /// limits give, and the scratch space a call used is inside the need the design note derives.
    /// </summary>
    public class ExactLimbSignsTests
    {
#if NET
        internal static readonly bool StackLimbs = true;
#else
        internal static readonly bool StackLimbs = false;        // netstandard2.0: every call is the BigInteger expression
#endif
        // eight operands of this many limbs are the widest plane side on the stack
        internal const int Wide = ExactLimbSigns.MaxOperandLimbs / 8;

        // the limbs ExactLimbSigns counts for v: the bytes of its two's complement, rounded up
        internal static int Limbs(BigInteger v) { return (v.ToByteArray().Length + 7) / 8; }

        // n limbs with every bit below the sign bit set
        internal static BigInteger Full(int n) { return (BigInteger.One << (64 * n - 1)) - 1; }

        // the path of a side or a dot: the stack while the operands take at most MaxOperandLimbs limbs in all, and then
        // inside the derived need
        static void CheckSidePath(int used, int total, int need, string what)
        {
            bool stack = StackLimbs && total <= ExactLimbSigns.MaxOperandLimbs;
            Assert.True(stack ? (used >= 0 && used <= need) : used == -1, $"{what}: {used} scratch limbs, {total} operand limbs, need {need}");
        }

        static int RowLimbs(ExactPoint3 p) { return Math.Max(Math.Max(Limbs(p.X), Limbs(p.Y)), Math.Max(Limbs(p.Z), Limbs(p.W))); }
        static int PointLimbs(ExactPoint3 p) { return Limbs(p.X) + Limbs(p.Y) + Limbs(p.Z) + Limbs(p.W); }

        // the derived scratch need of the determinant (docs/design/EXACT_LIMB_SIGNS.md)
        static int DeterminantNeed(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d)
        {
            return 7 * RowLimbs(a) + 27 * RowLimbs(b) + 63 * (RowLimbs(c) + RowLimbs(d)) + 71;
        }

        // the path of a determinant: the stack while operands and scratch space take at most MaxDeterminantLimbs limbs;
        // returns whether that is the stack
        static bool CheckDeterminantPath(int used, ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, ExactPoint3 d, string what)
        {
            long limbs = PointLimbs(a) + PointLimbs(b) + PointLimbs(c) + PointLimbs(d) + ExactLimbSigns.DeterminantScratch(RowLimbs(a), RowLimbs(b), RowLimbs(c), RowLimbs(d));
            bool stack = StackLimbs && limbs <= ExactLimbSigns.MaxDeterminantLimbs;
            int need = DeterminantNeed(a, b, c, d);
            Assert.True(stack ? (used >= 0 && used <= need) : used == -1, $"{what}: {used} scratch limbs, {limbs} limbs in all, need {need}");
            return limbs <= ExactLimbSigns.MaxDeterminantLimbs;
        }

        // a value of exactly `bits` bits (0: zero) with a random sign; every eighth one at a limb boundary pattern
        internal static BigInteger Rand(Random r, int bits)
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

        // widths in bits: small, around the limb boundaries, the classification's sizes, up to `limbs` limbs and beyond
        internal static int Width(Random r, int limbs = Wide)
        {
            switch (r.Next(8)) {
                case 0: return r.Next(0, 4);
                case 1: return 64 * r.Next(1, 14) + r.Next(-2, 3);
                case 2: return r.Next(1, 130);
                case 3: return r.Next(130, 900);
                case 4: return r.Next(Math.Min(900, 32 * limbs), 64 * limbs - 8);
                case 5: return 64 * limbs + r.Next(-70, 70);
                case 6: return r.Next(64 * limbs, 64 * limbs + 600);
                default: return r.Next(1, 400);
            }
        }

        [Fact]
        public void PlaneSide_MatchesBigInteger()
        {
            var r = new Random(11);
            int zeros = 0, fallback = 0, onStack = 0, decidedByD = 0;
            for (int i = 0; i < 30000; ++i) {
                // (every fifth block of four cases: all operands wide, the sums of their limbs on both sides of the limit)
                Func<int> width = () => ((i / 4) % 5 == 4) ? r.Next(64 * 28, 64 * 48) : Width(r);
                BigInteger a = Rand(r, width()), b = Rand(r, width()), c = Rand(r, width()), x = Rand(r, width()), y = Rand(r, width()), z = Rand(r, width());
                BigInteger w = Rand(r, width()), d = Rand(r, width());
                int kind = i % 4;
                if (kind == 1) { w = BigInteger.One; d = a * x + b * y + c * z; }                       // on the plane
                else if (kind == 2) { w = BigInteger.One; d = a * x + b * y + c * z + (r.Next(2) == 0 ? 1 : -1); }    // one unit off it
                int expected = (a * x + b * y + c * z - d * w).Sign;
                int used;
                Assert.True(expected == ExactLimbSigns.PlaneSide(a, b, c, d, x, y, z, w, out used), $"case {i}");
                Assert.True(expected == ExactLimbSigns.PlaneSide(a, b, c, d, x, y, z, w), $"case {i}, without the hook");
                int total = Limbs(a) + Limbs(b) + Limbs(c) + Limbs(d) + Limbs(x) + Limbs(y) + Limbs(z) + Limbs(w);
                CheckSidePath(used, total, 4 * total + 6, $"case {i}");
                if (total > ExactLimbSigns.MaxOperandLimbs) fallback++; else onStack++;
                // the integer point (W = 1): in half of these cases D decides the sign against A X + B Y + C Z
                Assert.True((a * x + b * y + c * z - d).Sign == ExactLimbSigns.PlaneSide(a, b, c, d, x, y, z, out used), $"case {i}, integer point");
                Assert.True((a * x + b * y + c * z - d).Sign == ExactLimbSigns.PlaneSide(a, b, c, d, x, y, z), $"case {i}, integer point, without the hook");
                CheckSidePath(used, total - Limbs(w), 4 * (total - Limbs(w)) + 6, $"case {i}, integer point");
                if ((a * x + b * y + c * z - d).Sign != (a * x + b * y + c * z).Sign) decidedByD++;
                if (expected == 0) zeros++;
            }
            // (the cases beyond the limit are counted by the sum of the operands' limbs, which is what selects the path)
            Assert.True(zeros > 5000 && fallback > 1500 && onStack > 10000 && decidedByD > 5000, $"{zeros} zeros, {fallback} beyond the limit, {onStack} inside, {decidedByD} decided by D");
        }

        // #1047: the plane and the point prepared once (PreparedLimbs) give the sign of PlaneSide on the same eight integers: random
        // operands with exact zeros, points on and one unit off the plane, operands at the limb boundaries, sums of limbs on both
        // sides of the stack limit; one prepared plane against many prepared points and the reverse (the sharing of the real use)
        [Fact]
        public void PreparedPlaneSide_MatchesPlaneSide()
        {
            var r = new Random(41);
            int zeros = 0, fallback = 0, onStack = 0, negative = 0, positive = 0;
            BigInteger[] edge = { 0, 1, -1, BigInteger.One << 63, -(BigInteger.One << 63), (BigInteger.One << 64) - 1, BigInteger.One << 64, -(BigInteger.One << 127), Full(4), -Full(4), Full(5) + 1 };
            for (int i = 0; i < 20000; ++i) {
                Func<int> width = () => ((i / 4) % 5 == 4) ? r.Next(64 * 28, 64 * 48) : Width(r);
                BigInteger[] v = new BigInteger[8];
                for (int k = 0; k < 8; ++k)
                    v[k] = (i % 7 == 3) ? edge[r.Next(edge.Length)] : Rand(r, width());
                if (i % 4 == 1) { v[7] = BigInteger.One; v[3] = v[0] * v[4] + v[1] * v[5] + v[2] * v[6]; }
                else if (i % 4 == 2) { v[7] = BigInteger.One; v[3] = v[0] * v[4] + v[1] * v[5] + v[2] * v[6] + (r.Next(2) == 0 ? 1 : -1); }
                int expected = ExactLimbSigns.PlaneSide(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]);
                Assert.True(expected == (v[0] * v[4] + v[1] * v[5] + v[2] * v[6] - v[3] * v[7]).Sign, $"case {i}, the reference");
                var plane = new PreparedLimbs(v[0], v[1], v[2], v[3]);
                var point = new PreparedLimbs(v[4], v[5], v[6], v[7]);
                Assert.True(expected == PreparedLimbs.PlaneSide(plane, point), $"case {i}");
                // (the same prepared objects again, and against the operands of the next case: nothing is consumed or left behind)
                Assert.True(expected == PreparedLimbs.PlaneSide(plane, point), $"case {i}, again");
                int total = 0;
                for (int k = 0; k < 8; ++k) total += Limbs(v[k]);
                if (total > ExactLimbSigns.MaxOperandLimbs) fallback++; else onStack++;
                if (expected == 0) zeros++; else if (expected < 0) negative++; else positive++;
            }
            Assert.True(zeros > 2000 && negative > 3000 && positive > 3000 && fallback > 1000 && onStack > 8000, $"{zeros} zeros, {negative} negative, {positive} positive, {fallback} beyond the limit, {onStack} inside");

            // one plane against many points, and one point against many planes
            var rr = new Random(42);
            BigInteger[] pl = { Rand(rr, 200), Rand(rr, 190), Rand(rr, 180), Rand(rr, 400) };
            var preparedPlane = new PreparedLimbs(pl[0], pl[1], pl[2], pl[3]);
            for (int i = 0; i < 2000; ++i) {
                BigInteger x = Rand(rr, 150), y = Rand(rr, 150), z = Rand(rr, 150), w = BigInteger.Abs(Rand(rr, 100)) + 1;
                Assert.True(ExactLimbSigns.PlaneSide(pl[0], pl[1], pl[2], pl[3], x, y, z, w) == PreparedLimbs.PlaneSide(preparedPlane, new PreparedLimbs(x, y, z, w)), $"one plane, point {i}");
                var preparedPoint = new PreparedLimbs(x, y, z, w);
                BigInteger a = Rand(rr, 120), b = Rand(rr, 120), c = Rand(rr, 120), d = Rand(rr, 300);
                Assert.True(ExactLimbSigns.PlaneSide(a, b, c, d, x, y, z, w) == PreparedLimbs.PlaneSide(new PreparedLimbs(a, b, c, d), preparedPoint), $"one point, plane {i}");
            }
        }

        // #1047: the stack budget: operands whose limbs sum to exactly MaxOperandLimbs (the stack path) and to one more (the BigInteger
        // expression), and a plane alone beyond the budget (nothing is loaded for it)
        [Fact]
        public void PreparedPlaneSide_StackBudgetBoundary()
        {
            var r = new Random(44);
            int atLimit = 0, beyond = 0;
            for (int i = 0; i < 400; ++i) {
                // eight operands of n limbs each, one of them widened so that the sum is 320 (+ 0 or 1)
                int extra = i % 2;
                BigInteger[] v = new BigInteger[8];
                for (int k = 0; k < 8; ++k)
                    v[k] = Full(40) - r.Next(1000);                          // limbs_of = 41: 40 limbs of magnitude plus the sign bit... checked below
                int total = 0;
                for (int k = 0; k < 8; ++k) total += Limbs(v[k]);
                // (tune the last operand so that the total is the limit or one above)
                int want = ExactLimbSigns.MaxOperandLimbs + extra;
                int n = Limbs(v[7]) + (want - total);
                v[7] = (BigInteger.One << (64 * (n - 1))) + r.Next(1, 1000);
                for (int k = 0; k < 7; ++k) if (r.Next(2) == 0) v[k] = -v[k];
                total = 0;
                for (int k = 0; k < 8; ++k) total += Limbs(v[k]);
                Assert.Equal(want, total);
                if (total == ExactLimbSigns.MaxOperandLimbs) atLimit++; else beyond++;
                int expected = (v[0] * v[4] + v[1] * v[5] + v[2] * v[6] - v[3] * v[7]).Sign;
                Assert.Equal(expected, PreparedLimbs.PlaneSide(new PreparedLimbs(v[0], v[1], v[2], v[3]), new PreparedLimbs(v[4], v[5], v[6], v[7])));
                Assert.Equal(expected, ExactLimbSigns.PlaneSide(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]));
            }
            Assert.True(atLimit == 200 && beyond == 200);
            // a plane of its own beyond the budget
            BigInteger big = (BigInteger.One << (64 * 330)) + 12345;
            BigInteger one = new BigInteger(7), two = new BigInteger(-3);
            Assert.Equal((big * 5 + one * two + 1 * 9 - big * 1).Sign, PreparedLimbs.PlaneSide(new PreparedLimbs(big, one, 1, big), new PreparedLimbs(5, two, 9, 1)));
        }

        // #1047: prepared operands are shared by the threads of the classification: the sign does not depend on the schedule
        [Fact]
        public void PreparedPlaneSide_IsThreadSafe()
        {
            var r = new Random(43);
            var planes = new PreparedLimbs[16];
            var points = new PreparedLimbs[64];
            var raw = new BigInteger[16][];
            for (int i = 0; i < planes.Length; ++i) {
                raw[i] = new[] { Rand(r, 128), Rand(r, 128), Rand(r, 128), Rand(r, 300) };
                planes[i] = new PreparedLimbs(raw[i][0], raw[i][1], raw[i][2], raw[i][3]);
            }
            var pr = new BigInteger[64][];
            for (int j = 0; j < points.Length; ++j) {
                pr[j] = new[] { Rand(r, 100), Rand(r, 100), Rand(r, 100), BigInteger.Abs(Rand(r, 60)) + 1 };
                points[j] = new PreparedLimbs(pr[j][0], pr[j][1], pr[j][2], pr[j][3]);
            }
            var expected = new int[16, 64];
            for (int i = 0; i < 16; ++i)
                for (int j = 0; j < 64; ++j)
                    expected[i, j] = ExactLimbSigns.PlaneSide(raw[i][0], raw[i][1], raw[i][2], raw[i][3], pr[j][0], pr[j][1], pr[j][2], pr[j][3]);
            int bad = 0;
            System.Threading.Tasks.Parallel.For(0, 8, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 8 }, t => {
                for (int rep = 0; rep < 300; ++rep)
                    for (int i = 0; i < 16; ++i)
                        for (int j = 0; j < 64; ++j)
                            if (PreparedLimbs.PlaneSide(planes[(i + t) % 16], points[(j + rep) % 64]) != expected[(i + t) % 16, (j + rep) % 64])
                                System.Threading.Interlocked.Increment(ref bad);
            });
            Assert.Equal(0, bad);
        }

        [Fact]
        public void Dot_MatchesBigInteger()
        {
            var r = new Random(12);
            int zeros = 0, fallback = 0, onStack = 0;
            for (int i = 0; i < 30000; ++i) {
                BigInteger a = Rand(r, Width(r)), b = Rand(r, Width(r)), c = Rand(r, Width(r)), x = Rand(r, Width(r)), y = Rand(r, Width(r)), z = Rand(r, Width(r));
                if (i % 3 == 1) { x = b; y = -a; z = BigInteger.Zero; }                 // orthogonal
                else if (i % 3 == 2) { x = b * c; y = -2 * a * c; z = a * b + (r.Next(2) == 0 ? 0 : 1); }   // orthogonal or one unit off
                int expected = (a * x + b * y + c * z).Sign;
                int used;
                Assert.True(expected == ExactLimbSigns.Dot(a, b, c, x, y, z, out used), $"case {i}");
                Assert.True(expected == ExactLimbSigns.Dot(a, b, c, x, y, z), $"case {i}, without the hook");
                int total = Limbs(a) + Limbs(b) + Limbs(c) + Limbs(x) + Limbs(y) + Limbs(z);
                CheckSidePath(used, total, 3 * total + 3, $"case {i}");
                if (total > ExactLimbSigns.MaxOperandLimbs) fallback++; else onStack++;
                if (expected == 0) zeros++;
            }
            Assert.True(zeros > 5000 && fallback > 300 && onStack > 10000, $"{zeros} zeros, {fallback} beyond the limit, {onStack} inside");
        }

        // the widths of the determinant's operands go up to DetWide limbs and 9 beyond: four rows of 25 limbs are the
        // widest uniform ones on the stack, so the random rows are on both sides of the limit (one case in seven beyond)
        const int DetWide = 22;

        static ExactPoint3 Point(Random r)
        {
            BigInteger w = BigInteger.Abs(Rand(r, Math.Max(1, Width(r, DetWide))));
            return ExactPoint3.CreateUnreduced(Rand(r, Width(r, DetWide)), Rand(r, Width(r, DetWide)), Rand(r, Width(r, DetWide)), w);
        }

        [Fact]
        public void Det4_MatchesBigInteger()
        {
            var r = new Random(13);
            int zeros = 0, nonzero = 0, fallback = 0, onStack = 0;
            for (int i = 0; i < 6000; ++i) {
                ExactPoint3 a = Point(r), b = Point(r), c = Point(r), d = Point(r);
                if (i % 3 == 1) {
                    // d in the plane of a, b, c: integer points (W = 1), d = a + s (b - a) + t (c - a)
                    a = ExactPoint3.CreateUnreduced(Rand(r, Width(r, DetWide)), Rand(r, Width(r, DetWide)), Rand(r, Width(r, DetWide)), BigInteger.One);
                    b = ExactPoint3.CreateUnreduced(Rand(r, Width(r, DetWide)), Rand(r, Width(r, DetWide)), Rand(r, Width(r, DetWide)), BigInteger.One);
                    c = ExactPoint3.CreateUnreduced(Rand(r, Width(r, DetWide)), Rand(r, Width(r, DetWide)), Rand(r, Width(r, DetWide)), BigInteger.One);
                    BigInteger s = Rand(r, r.Next(0, 70)), t = Rand(r, r.Next(0, 70));
                    BigInteger off = (i % 2 == 0) ? BigInteger.Zero : BigInteger.One;
                    d = ExactPoint3.CreateUnreduced(a.X + s * (b.X - a.X) + t * (c.X - a.X), a.Y + s * (b.Y - a.Y) + t * (c.Y - a.Y), a.Z + s * (b.Z - a.Z) + t * (c.Z - a.Z) + off, BigInteger.One);
                }
                int expected = ExactLimbSigns.Det4Big(a, b, c, d);
                int used;
                Assert.True(expected == ExactLimbSigns.Det4(a, b, c, d, out used), $"case {i}");
                Assert.True(expected == ExactLimbSigns.Det4(a, b, c, d), $"case {i}, without the hook");
                if (CheckDeterminantPath(used, a, b, c, d, $"case {i}")) onStack++; else fallback++;
                // two rows exchanged: the opposite sign
                Assert.True(-expected == ExactLimbSigns.Det4(b, a, c, d, out used), $"case {i}, rows exchanged");
                CheckDeterminantPath(used, b, a, c, d, $"case {i}, rows exchanged");
                if (expected == 0) zeros++; else nonzero++;
            }
            Assert.True(zeros > 500 && nonzero > 3000 && fallback > 500 && onStack > 3000, $"{zeros} zeros, {nonzero} others, {fallback} beyond the limit, {onStack} inside");
        }

        // The limit of the sides and the dot is on the sum of the operands' limbs: one wide operand beside small ones is
        // on the stack, and the sum MaxOperandLimbs is the last one that is.
        [Fact]
        public void SideLimit_IsTheSumOfTheOperandLimbs()
        {
            BigInteger one = Full(1), w = Full(Wide), w1 = Full(Wide + 1);
            int used;
            // 300 limbs in each position beside seven operands of one limb
            for (int k = 0; k < 8; ++k) {
                var v = new BigInteger[8];
                for (int j = 0; j < 8; ++j) v[j] = (j == k) ? -Full(300) : one - j;
                int expected = (v[0] * v[4] + v[1] * v[5] + v[2] * v[6] - v[3] * v[7]).Sign;
                Assert.Equal(expected, ExactLimbSigns.PlaneSide(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], out used));
                CheckSidePath(used, 307, 4 * 307 + 6, $"wide operand {k}");
                // (in the first two products: the 301 limbs of the wide product, 2 for each other one and three sums of
                // 301 or 302, so the need of 4 total + 6 is not padded)
                if (StackLimbs && k % 4 < 2) Assert.True(used >= 301 + 6 + 3 * 301, $"wide operand {k}: {used} scratch limbs");
                if (k < 7) {
                    Assert.Equal((v[0] * v[4] + v[1] * v[5] + v[2] * v[6] - v[3]).Sign, ExactLimbSigns.PlaneSide(v[0], v[1], v[2], v[3], v[4], v[5], v[6], out used));
                    CheckSidePath(used, 306, 4 * 306 + 6, $"wide operand {k}, integer point");
                }
                if (k != 3 && k != 7) {
                    Assert.Equal((v[0] * v[4] + v[1] * v[5] + v[2] * v[6]).Sign, ExactLimbSigns.Dot(v[0], v[1], v[2], v[4], v[5], v[6], out used));
                    CheckSidePath(used, 305, 3 * 305 + 3, $"wide operand {k}, dot");
                }
            }
            // the plane side: eight operands of 40 limbs are the limit, one limb more in any of them is beyond it
            Assert.Equal(1, ExactLimbSigns.PlaneSide(w, w, w, -w, w, w, w, w, out used));
            CheckSidePath(used, ExactLimbSigns.MaxOperandLimbs, 4 * ExactLimbSigns.MaxOperandLimbs + 6, "at the limit");
            Assert.Equal(StackLimbs, used >= 0);
            for (int k = 0; k < 8; ++k) {
                var v = new BigInteger[8];
                for (int j = 0; j < 8; ++j) v[j] = (j == k) ? w1 : w;
                Assert.Equal(1, ExactLimbSigns.PlaneSide(v[0], v[1], v[2], -v[3], v[4], v[5], v[6], v[7], out used));
                Assert.True(used == -1, $"operand {k} one limb wider: {used}");
            }
            // the integer point: seven operands
            Assert.Equal(1, ExactLimbSigns.PlaneSide(w, w, w, -Full(2 * Wide), w, w, w, out used));
            Assert.Equal(StackLimbs, used >= 0);
            Assert.Equal(-1, ExactLimbSigns.PlaneSide(w, w, w, Full(2 * Wide + 1), w, w, w, out used));
            Assert.Equal(-1, used);
            // the dot: six operands
            Assert.Equal(1, ExactLimbSigns.Dot(w, w, Full(3 * Wide), w, w, w, out used));
            Assert.Equal(StackLimbs, used >= 0);
            Assert.Equal(-1, ExactLimbSigns.Dot(w, w, -Full(3 * Wide + 1), w, w, w, out used));
            Assert.Equal(-1, used);
        }

        // four points whose rows have the given limbs: the first entry of a row has all of them, the others `rest[row]`
        static ExactPoint3[] Rows(int[] limbs, int[] rest)
        {
            var p = new ExactPoint3[4];
            for (int k = 0; k < 4; ++k) {
                BigInteger f = Full(limbs[k]), g = Full(rest[k]);
                p[k] = ExactPoint3.CreateUnreduced((k % 2 == 0) ? f : -f, g - k, -g + 2 * k + 1, g - 3 * k);
            }
            return p;
        }

        // The limit of the determinant is on its operands and its scratch space together, and the scratch space follows
        // the rows: the first row costs 8 limbs per limb, the second 28, the others 64.
        [Fact]
        public void DeterminantLimit_IsOperandsAndScratch()
        {
            int used;
            // rows of 27, 25, 25, 25 limbs, three entries of the second row shorter: 4620 - 3 k limbs in all
            foreach (int k in new[] { 4, 3 }) {
                var p = Rows(new[] { 27, 25, 25, 25 }, new[] { 27, 25 - k, 25, 25 });
                Assert.Equal(ExactLimbSigns.Det4Big(p[0], p[1], p[2], p[3]), ExactLimbSigns.Det4(p[0], p[1], p[2], p[3], out used));
                bool inside = CheckDeterminantPath(used, p[0], p[1], p[2], p[3], $"k = {k}");
                Assert.Equal(k == 4, inside);
                Assert.Equal(StackLimbs && k == 4, used >= 0);
            }
            // one wide row beside rows of one limb
            foreach (int[] wide in new[] { new[] { 0, 300, 1 }, new[] { 0, 400, 0 }, new[] { 1, 100, 1 }, new[] { 1, 150, 0 }, new[] { 2, 50, 1 }, new[] { 2, 68, 0 }, new[] { 3, 50, 1 }, new[] { 3, 68, 0 } }) {
                int[] limbs = { 1, 1, 1, 1 };
                limbs[wide[0]] = wide[1];
                var p = Rows(limbs, limbs);
                Assert.Equal(ExactLimbSigns.Det4Big(p[0], p[1], p[2], p[3]), ExactLimbSigns.Det4(p[0], p[1], p[2], p[3], out used));
                bool inside = CheckDeterminantPath(used, p[0], p[1], p[2], p[3], $"row {wide[0]} of {wide[1]} limbs");
                Assert.Equal(wide[2] == 1, inside);
                Assert.Equal(StackLimbs && wide[2] == 1, used >= 0);
            }
        }

        // The scratch space of each kernel holds its largest case, and the high-water mark of the arena is inside the
        // derived need: every operand at the widest that is on the stack, with all bits set (FixedArith throws when its
        // arena is exhausted), and mixed widths.
        [Fact]
        public void LargestOperands_StayInsideTheDerivedScratchNeed()
        {
            BigInteger big = Full(Wide);
            int used;
            Assert.Equal((big * big + big * big + big * big - big * -big).Sign, ExactLimbSigns.PlaneSide(big, big, big, big, big, big, big, -big, out used));
            CheckSidePath(used, 8 * Wide, 4 * (8 * Wide) + 6, "plane side");
            // (four products of 80 limbs, three sums of 81: the need of 4 total + 6 is for one product with all the limbs)
            if (StackLimbs) Assert.Equal(4 * 2 * Wide + 3 * (2 * Wide + 1), used);
            Assert.Equal(1, ExactLimbSigns.Dot(big, big, big, big, big, big, out used));
            CheckSidePath(used, 6 * Wide, 3 * (6 * Wide) + 3, "dot");
            if (StackLimbs) Assert.Equal(3 * 2 * Wide + 2 * (2 * Wide + 1), used);
            Assert.Equal(1, ExactLimbSigns.PlaneSide(big, big, big, -big, big, big, big, out used));
            CheckSidePath(used, 7 * Wide, 4 * (7 * Wide) + 6, "plane side of an integer point");
            if (StackLimbs) Assert.Equal(3 * 2 * Wide + 3 * (2 * Wide + 1), used);
            // the determinant: rows of 25 limbs are the widest uniform ones on the stack
            BigInteger det = Full(25);
            var p = ExactPoint3.CreateUnreduced(det, -det, det, det);
            var q = ExactPoint3.CreateUnreduced(-det, det, det - 1, det);
            var s = ExactPoint3.CreateUnreduced(det, det - 2, -det, det - 3);
            var t = ExactPoint3.CreateUnreduced(det - 5, -det, -det, det);
            Assert.Equal(ExactLimbSigns.Det4Big(p, q, s, t), ExactLimbSigns.Det4(p, q, s, t, out used));
            Assert.True(CheckDeterminantPath(used, p, q, s, t, "determinant"));
            // rows of 25 limbs without a relation between them: the need is 160 w + 71 = 4071 limbs, and a call takes at
            // least 160 w of them (an operand has no carry into its sign bit, so a difference takes up to one limb less
            // than the bound counts, and so do the sums: 71 limbs at most), so the need is not padded
            var r = new Random(14);
            for (int i = 0; i < 20; ++i) {
                // (random bits below the top one, which is set)
                Func<BigInteger> e = () => {
                    byte[] bytes = new byte[8 * 25 + 1];
                    r.NextBytes(bytes);
                    bytes[8 * 25] = 0; bytes[8 * 25 - 1] = (byte)(0x40 | (bytes[8 * 25 - 1] & 0x3F));
                    return r.Next(2) == 0 ? new BigInteger(bytes) : -new BigInteger(bytes);
                };
                var a = ExactPoint3.CreateUnreduced(e(), e(), e(), BigInteger.Abs(e()));
                var b = ExactPoint3.CreateUnreduced(e(), e(), e(), BigInteger.Abs(e()));
                var c = ExactPoint3.CreateUnreduced(e(), e(), e(), BigInteger.Abs(e()));
                var d = ExactPoint3.CreateUnreduced(e(), e(), e(), BigInteger.Abs(e()));
                Assert.Equal(ExactLimbSigns.Det4Big(a, b, c, d), ExactLimbSigns.Det4(a, b, c, d, out used));
                Assert.True(CheckDeterminantPath(used, a, b, c, d, $"dense case {i}"));
                Assert.Equal(160 * 25 + 71, DeterminantNeed(a, b, c, d));
                if (StackLimbs) Assert.True(used >= 160 * 25, $"dense case {i}: {used} scratch limbs of {DeterminantNeed(a, b, c, d)}");
            }
            int onStack = 0;
            for (int i = 0; i < 300; ++i) {
                Func<BigInteger> v = () => r.Next(3) == 0 ? (r.Next(2) == 0 ? det : -det) : Rand(r, r.Next(0, 3) * 64 + r.Next(1, 64));
                var a = ExactPoint3.CreateUnreduced(v(), v(), v(), BigInteger.Abs(v()) + 1);
                var b = ExactPoint3.CreateUnreduced(v(), v(), v(), BigInteger.Abs(v()) + 1);
                var c = ExactPoint3.CreateUnreduced(v(), v(), v(), BigInteger.Abs(v()) + 1);
                var d = ExactPoint3.CreateUnreduced(v(), v(), v(), BigInteger.Abs(v()) + 1);
                Assert.Equal(ExactLimbSigns.Det4Big(a, b, c, d), ExactLimbSigns.Det4(a, b, c, d, out used));
                if (CheckDeterminantPath(used, a, b, c, d, $"case {i}")) onStack++;
                Assert.Equal((a.X * b.X + a.Y * b.Y + a.Z * b.Z - a.W * b.W).Sign, ExactLimbSigns.PlaneSide(a.X, a.Y, a.Z, a.W, b.X, b.Y, b.Z, b.W, out used));
                CheckSidePath(used, PointLimbs(a) + PointLimbs(b), 4 * (PointLimbs(a) + PointLimbs(b)) + 6, $"case {i}, plane side");
            }
            Assert.True(onStack > 100, $"{onStack} determinants inside the limit");
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
            // The first passes may allocate once (JIT tiering / OSR, lazy per-thread state, issue #939): run until three
            // consecutive passes allocate nothing, at most 12 passes. The same inputs are replayed, so an allocation that
            // depends on the inputs or on every call shows in every pass; a warm-up allocation only restarts the run.
            const int MaxPasses = 12, RequiredCleanPasses = 3;
            long sum = 0;
            var perPass = new List<long>();
            int zeroRun = 0;
            for (int pass = 0; pass < MaxPasses && zeroRun < RequiredCleanPasses; ++pass) {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < cases.Count; ++i) {
                    var v = cases[i];
                    sum += ExactLimbSigns.PlaneSide(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]);
                    sum += ExactLimbSigns.Dot(v[0], v[1], v[2], v[4], v[5], v[6]);
                    sum += ExactLimbSigns.PlaneSide(v[0], v[1], v[2], v[3], v[4], v[5], v[6]);
                    sum += ExactLimbSigns.Det4(pts[i][0], pts[i][1], pts[i][2], pts[i][3]);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                perPass.Add(allocated);
                zeroRun = allocated == 0 ? zeroRun + 1 : 0;
            }
            Assert.True(zeroRun >= RequiredCleanPasses, $"expected {RequiredCleanPasses} consecutive passes without allocation; bytes allocated per pass of {cases.Count} cases: {string.Join(", ", perPass)} (sum {sum})");
        }
#endif
    }
}
