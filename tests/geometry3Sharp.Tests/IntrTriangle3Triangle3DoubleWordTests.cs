using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The certified double-word arithmetic (DoubleWord) and its use for the end points of a generic triangle crossing
    /// (IntrTriangle3Triangle3.Find): every operation's Err bounds its distance to the exact value (property tests against
    /// BigInteger fractions), a tie or near tie is never certified (the exact route rounds half to even), and each
    /// coordinate of Find's end points is the correctly rounded exact value, on random, axis-aligned and tie inputs.
    /// docs/design/TRI3TRI3_DOUBLE_WORD.md.
    /// </summary>
    public class IntrTriangle3Triangle3DoubleWordTests
    {
        // ---- the oracle: exact fractions of doubles ----
        readonly struct Q
        {
            public readonly BigInteger N, D;
            public Q(BigInteger n, BigInteger d) { if (d.Sign < 0) { n = -n; d = -d; } N = n; D = d; }
            public static Q From(double x)
            {
                if (x == 0) return new Q(0, 1);
                long bits = BitConverter.DoubleToInt64Bits(x);
                bool neg = bits < 0; int e = (int)((bits >> 52) & 0x7FF); long m = bits & 0xFFFFFFFFFFFFFL;
                if (e == 0) e = 1; else m |= 1L << 52;
                e -= 1075;
                BigInteger n = m; if (neg) n = -n;
                return e >= 0 ? new Q(n << e, 1) : new Q(n, BigInteger.One << -e);
            }
            public static Q operator +(Q a, Q b) => new Q(a.N * b.D + b.N * a.D, a.D * b.D);
            public static Q operator -(Q a, Q b) => new Q(a.N * b.D - b.N * a.D, a.D * b.D);
            public static Q operator *(Q a, Q b) => new Q(a.N * b.N, a.D * b.D);
            public static Q operator /(Q a, Q b) => new Q(a.N * b.D, a.D * b.N);
            public int Sign => N.Sign;
            public int CompareTo(Q o) => (N * o.D).CompareTo(o.N * D);
            static int BitLength(BigInteger x)
            {
                byte[] b = BigInteger.Abs(x).ToByteArray();
                int top = b.Length - 1;
                while (top > 0 && b[top] == 0) top--;
                int bits = top * 8;
                for (int v = b[top]; v > 0; v >>= 1) bits++;
                return bits;
            }
            // about 20 digits: for measuring an error
            public double Approx()
            {
                if (N.IsZero) return 0;
                BigInteger a = BigInteger.Abs(N);
                int shift = 70 + BitLength(D) - BitLength(a);
                BigInteger q = shift >= 0 ? (a << shift) / D : a / (D << -shift);
                double r = Math.Pow(2, -shift) * (double)q;
                return N.Sign < 0 ? -r : r;
            }
            // the double nearest to the value, ties to even (independent of the code under test: the candidates around an approximation)
            public double Nearest()
            {
                double c = Approx();
                var cands = new[] { Down(Down(c)), Down(c), c, Up(c), Up(Up(c)) };
                double best = cands[0]; Q bestErr = Abs(From(best) - this);
                foreach (double d in cands.Skip(1)) {
                    Q e = Abs(From(d) - this);
                    int cmp = e.CompareTo(bestErr);
                    if (cmp < 0 || (cmp == 0 && IsEven(d) && !IsEven(best))) { best = d; bestErr = e; }
                }
                return best;
            }
            static Q Abs(Q a) => a.Sign < 0 ? new Q(-a.N, a.D) : a;
            static bool IsEven(double d) => (BitConverter.DoubleToInt64Bits(d) & 1) == 0;
            static double Up(double x) { if (x == 0) return double.Epsilon; long b = BitConverter.DoubleToInt64Bits(x); return BitConverter.Int64BitsToDouble(x > 0 ? b + 1 : b - 1); }
            static double Down(double x) => -Up(-x);
        }

        static Q Value(DoubleWord x) => Q.From(x.Hi) + Q.From(x.Lo);

        // |x - exact| <= x.Err, exactly
        static bool Within(DoubleWord x, Q exact)
        {
            Q d = Value(x) - exact;
            if (d.Sign < 0) d = new Q(-d.N, d.D);
            return d.CompareTo(Q.From(x.Err)) <= 0;
        }

        // a random double-word: the exact difference of two doubles, or such a value times a double (one more operation)
        static DoubleWord RandomWord(Random r, out Q exact)
        {
            double Mantissa() => (r.NextDouble() * 2 - 1) * Math.Pow(2, r.Next(-30, 30));
            double a = Mantissa(), b = Mantissa();
            if (r.Next(3) == 0) b = a * (1 + Math.Pow(2, -r.Next(30, 52)));      // cancellation
            DoubleWord x = DoubleWord.Diff(a, b);
            exact = Q.From(a) - Q.From(b);
            return x;
        }

        // ---- the bound with operands that themselves carry an error: exact operands at the extremes of [value - Err, value + Err] ----

        static Q At(DoubleWord x, int k) => Value(x) + Q.From(x.Err) * new Q(k, 1 << 20);      // k / 2^20 of the way to the edge (k = +-2^20: the edge)

        static DoubleWord WithError(Random r)
        {
            double hi = (r.NextDouble() * 1.5 + 0.5) * Math.Pow(2, r.Next(-20, 20)) * (r.Next(2) == 0 ? -1 : 1);
            double lo = hi * Math.Pow(2, -r.Next(54, 70)) * (r.NextDouble() * 2 - 1);
            // errors from far below the rounding of the result to a large part of the value
            double err = Math.Abs(hi) * Math.Pow(2, -r.Next(3, 110)) * (r.NextDouble() + 0.5);
            return new DoubleWord(hi, lo, err);
        }

        static readonly int[] Ks = { -(1 << 20), 1 << 20, 0, -(1 << 19), 1 << 18 };

        [Fact]
        public void ErrorBound_HoldsAtTheOperandErrorExtremes_Sum()
        {
            // x = 1 +- 1, y = 1 +- 2^-53: the exact value 3 + 2^-53 is 1 + 2^-53 above the computed sum 2, which the rounded Err (1) must still cover
            var a = new DoubleWord(1, 0, 1); var b = new DoubleWord(1, 0, DoubleWord.U);
            var sum = a + b;
            Assert.True(Within(sum, At(a, 1 << 20) + At(b, 1 << 20)), "sum err " + sum.Err);
            var r = new Random(401);
            for (int i = 0; i < 4000; i++) {
                var x = WithError(r); var y = WithError(r);
                var z = x + y;
                if (z.IsValid == false) continue;
                foreach (int kx in Ks) foreach (int ky in Ks)
                    Assert.True(Within(z, At(x, kx) + At(y, ky)), $"sum {i} {kx} {ky}");
            }
        }

        [Fact]
        public void ErrorBound_HoldsAtTheOperandErrorExtremes_Product()
        {
            var r = new Random(402);
            int valid = 0;
            for (int i = 0; i < 4000; i++) {
                var x = WithError(r); var y = WithError(r);
                var z = x * y;
                if (z.IsValid == false) continue;
                valid++;
                foreach (int kx in Ks) foreach (int ky in Ks)
                    Assert.True(Within(z, At(x, kx) * At(y, ky)), $"product {i} {kx} {ky}");
            }
            Assert.True(valid > 3500);
            // the operand terms alone: x = 1 +- 2^-20, y = 1 +- 2^-53 (the term |y| ex is the largest, rounded down by the sum)
            var a = new DoubleWord(1, 0, Math.Pow(2, -20)); var b = new DoubleWord(1, 0, DoubleWord.U);
            var p = a * b;
            Assert.True(Within(p, At(a, 1 << 20) * At(b, 1 << 20)));
        }

        [Fact]
        public void ErrorBound_HoldsAtTheOperandErrorExtremes_Quotient()
        {
            var r = new Random(403);
            int valid = 0;
            for (int i = 0; i < 4000; i++) {
                var x = WithError(r); var y = WithError(r);
                var z = DoubleWord.Div(x, y);
                if (z.IsValid == false) continue;
                valid++;
                foreach (int kx in Ks) foreach (int ky in Ks)
                    Assert.True(Within(z, At(x, kx) / At(y, ky)), $"quotient {i} {kx} {ky}");
            }
            Assert.True(valid > 3000, "valid " + valid);
        }

        [Fact]
        public void Range_IncludesItsEnds_AndRejectsWhatIsBeyond()
        {
            double lo = Math.Pow(2, -480), hi = Math.Pow(2, 480);
            Assert.True(DoubleWord.Of(lo).IsValid);
            Assert.True(DoubleWord.Of(hi).IsValid);
            Assert.True(DoubleWord.Of(-lo).IsValid);
            Assert.True(DoubleWord.Of(-hi).IsValid);
            Assert.False(DoubleWord.Of(Up(hi)).IsValid);
            Assert.False(DoubleWord.Of(Down(lo)).IsValid);
            Assert.True(DoubleWord.Of(Up(lo)).IsValid);
            Assert.True(DoubleWord.Of(Down(hi)).IsValid);
            Assert.True(DoubleWord.Of(0).IsValid);
            Assert.False(DoubleWord.Of(double.NaN).IsValid);
            Assert.False(DoubleWord.Of(double.PositiveInfinity).IsValid);
            Assert.False((new DoubleWord(1, 0, double.PositiveInfinity) + DoubleWord.Of(1)).IsValid);
            Assert.False((DoubleWord.Of(1) * new DoubleWord(1, 0, double.PositiveInfinity)).IsValid);
        }
        static double Up(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1);
        static double Down(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) - 1);

        [Fact]
        public void Div_OfTheErrorsOfItsOperands_IsCertified_AndInvalidForADenominatorThatMayBeZero()
        {
            // the denominator 1 +- 1: may be zero
            Assert.False(DoubleWord.Div(DoubleWord.Of(1), new DoubleWord(1, 0, 1)).IsValid);
            Assert.False(DoubleWord.Div(DoubleWord.Of(1), new DoubleWord(1, 0, 1.5)).IsValid);
            Assert.True(DoubleWord.Div(DoubleWord.Of(1), new DoubleWord(1, 0, 0.5)).IsValid);
            Assert.False(DoubleWord.Div(DoubleWord.Invalid, DoubleWord.Of(1)).IsValid);
            Assert.False(DoubleWord.Div(DoubleWord.Of(1), DoubleWord.Invalid).IsValid);
            Assert.False(DoubleWord.Div(DoubleWord.Of(1), DoubleWord.Of(0)).IsValid);
            // the low word counts against the denominator: (1, -2^-53) has |y'| = 1 - 2^-53 at best
            var y = new DoubleWord(1, -DoubleWord.U, 0);
            var q = DoubleWord.Div(DoubleWord.Of(1), y);
            Assert.True(q.IsValid && Within(q, Q.From(1) / (Q.From(1) - Q.From(DoubleWord.U))));
        }

        [Fact]
        public void Add_ErrBoundsTheError()
        {
            var r = new Random(1);
            for (int i = 0; i < 4000; i++) {
                DoubleWord x = RandomWord(r, out Q ex), y = RandomWord(r, out Q ey);
                DoubleWord z = x + y;
                Assert.True(z.IsValid);
                Assert.True(Within(z, ex + ey), "sum " + i);
                // also with operands that carry an error: the sum of a sum
                DoubleWord w = RandomWord(r, out Q ew);
                DoubleWord s2 = z + w;
                Assert.True(Within(s2, ex + ey + ew), "sum of sums " + i);
                // the bound is about 2^-100 of the operands, not much larger (so that it certifies)
                Assert.True(z.Err <= Math.Pow(2, -99) * (Math.Abs(x.Hi) + Math.Abs(y.Hi)), "Err of a sum");
            }
        }

        [Fact]
        public void Mul_ErrBoundsTheError()
        {
            var r = new Random(2);
            for (int i = 0; i < 4000; i++) {
                DoubleWord x = RandomWord(r, out Q ex), y = RandomWord(r, out Q ey);
                DoubleWord z = x * y;
                if (z.IsValid == false) continue;
                Assert.True(Within(z, ex * ey), "product " + i);
                DoubleWord w = RandomWord(r, out Q ew);
                DoubleWord p3 = (z + w) * y;
                if (p3.IsValid) Assert.True(Within(p3, (ex * ey + ew) * ey), "chain " + i);
            }
        }

        [Fact]
        public void Div_ErrBoundsTheError()
        {
            var r = new Random(3);
            int valid = 0;
            for (int i = 0; i < 4000; i++) {
                DoubleWord x = RandomWord(r, out Q ex), y = RandomWord(r, out Q ey);
                if (ey.Sign == 0) continue;
                DoubleWord q = DoubleWord.Div(x, y);
                if (q.IsValid == false) continue;
                valid++;
                Assert.True(Within(q, ex / ey), "quotient " + i);
                // with operands that carry an error
                DoubleWord w = RandomWord(r, out Q ew);
                DoubleWord num = x * y + w, den = y + w;
                DoubleWord q2 = DoubleWord.Div(num, den);
                if (q2.IsValid && (ey + ew).Sign != 0) Assert.True(Within(q2, (ex * ey + ew) / (ey + ew)), "quotient of results " + i);
            }
            Assert.True(valid > 3000, "valid " + valid);
        }

        // adversarial leaf: the exact difference of two doubles that are powers of two, next to them, nearly equal (cancellation), of both signs
        static int exponentRange = 40;
        static DoubleWord Leaf(Random r, out Q exact)
        {
            double Pick()
            {
                double sign = r.Next(2) == 0 ? 1 : -1;
                double p = Math.Pow(2, r.Next(-exponentRange, exponentRange));
                switch (r.Next(5)) {
                    case 0: return sign * p;
                    case 1: return sign * p * (1 + Math.Pow(2, -52));
                    case 2: return sign * p * (1 - Math.Pow(2, -53));
                    case 3: return sign * p * (1 + r.NextDouble());
                    default: return sign * p * (1 + r.Next(1, 8) * Math.Pow(2, -52));
                }
            }
            double a = Pick(), b = (r.Next(3) == 0) ? a * (1 + (r.Next(2) == 0 ? 1 : -1) * Math.Pow(2, -r.Next(40, 53))) : Pick();
            exact = Q.From(a) - Q.From(b);
            return DoubleWord.Diff(a, b);
        }

        // a leaf, or the sum or product of two operands (a result that carries an error), with its exact value
        static DoubleWord Operand(Random r, int depth, out Q exact)
        {
            if (depth > 0 && r.Next(2) == 0) {
                DoubleWord x = Operand(r, depth - 1, out Q ex), y = Operand(r, depth - 1, out Q ey);
                bool sum = r.Next(2) == 0;
                DoubleWord z = sum ? x + y : x * y;
                if (z.IsValid) { exact = sum ? ex + ey : ex * ey; return z; }
            }
            return Leaf(r, out exact);
        }

        readonly Xunit.Abstractions.ITestOutputHelper output;
        public IntrTriangle3Triangle3DoubleWordTests(Xunit.Abstractions.ITestOutputHelper output) { this.output = output; }

        // |value - exact| / Err
        static double Ratio(DoubleWord x, Q exact)
        {
            Q d = Value(x) - exact;
            if (d.Sign < 0) d = new Q(-d.N, d.D);
            return (d / Q.From(x.Err)).Approx();
        }

        [Theory]
        [InlineData(40)]
        [InlineData(150)]
        public void ObservedError_IsAtMostTheBound_Ratios(int range)
        {
            exponentRange = range;
            var r = new Random(21);
            double maxAdd = 0, maxMul = 0, maxDiv = 0;
            int nAdd = 0, nMul = 0, nDiv = 0;
            for (int i = 0; i < 6000; i++) {
                DoubleWord x = Operand(r, 2, out Q ex), y = Operand(r, 2, out Q ey);
                DoubleWord s = x + y, p = x * y;
                if (s.IsValid && s.Err > 0) { maxAdd = Math.Max(maxAdd, Ratio(s, ex + ey)); nAdd++; }
                if (p.IsValid && p.Err > 0) { maxMul = Math.Max(maxMul, Ratio(p, ex * ey)); nMul++; }
                if (ey.Sign != 0) {
                    DoubleWord q = DoubleWord.Div(x, y);
                    if (q.IsValid && q.Err > 0) { maxDiv = Math.Max(maxDiv, Ratio(q, ex / ey)); nDiv++; }
                }
            }
            output.WriteLine($"largest observed error / bound: sum {maxAdd:G3} ({nAdd}), product {maxMul:G3} ({nMul}), quotient {maxDiv:G3} ({nDiv})");
            Assert.True(maxAdd <= 1 && maxMul <= 1 && maxDiv <= 1, $"sum {maxAdd}, product {maxMul}, quotient {maxDiv}");
            Assert.True(nAdd > 2500 && nMul > 1500 && nDiv > 1000, $"{nAdd} {nMul} {nDiv}");
        }

        [Fact]
        public void OutOfRange_IsNotCertified()
        {
            DoubleWord big = DoubleWord.Of(Math.Pow(2, 481)), tiny = DoubleWord.Of(Math.Pow(2, -481)), ok = DoubleWord.Of(1);
            Assert.False(big.IsValid);
            Assert.False(tiny.IsValid);
            Assert.False((ok + big).IsValid);
            Assert.False((ok * big).IsValid);
            Assert.True(DoubleWord.Of(Math.Pow(2, 300)).IsValid);
            Assert.False((DoubleWord.Of(Math.Pow(2, 300)) * DoubleWord.Of(Math.Pow(2, 300))).IsValid);       // 2^600: out of range
            Assert.False(DoubleWord.Of(double.NaN).IsValid);
            Assert.False(DoubleWord.Of(double.PositiveInfinity).IsValid);
            Assert.False(DoubleWord.Div(ok, DoubleWord.Of(0)).IsValid);
            Assert.True(DoubleWord.Of(0).IsValid);
        }

        [Fact]
        public void RoundsTo_NeverCertifiesATieOrNearTie()
        {
            double half = Math.Pow(2, -53), u = Math.Pow(2, -52);
            double rounded;
            // exactly the midpoint of 1 and 1 + 2^-52: |Lo| + Err = half the spacing is not "less than"
            Assert.False(new DoubleWord(1, half, 0).RoundsTo(out rounded));
            // (1 is a power of two: the spacing below it is 2^-53, so the rounding interval below is 2^-54 wide, and a value is
            // certified only inside the smaller side)
            Assert.False(new DoubleWord(1, half / 2, half / 2).RoundsTo(out rounded));
            Assert.True(new DoubleWord(1, half / 4, half / 8).RoundsTo(out rounded));
            Assert.Equal(1.0, rounded);
            // below a power of two the spacing is half: 1 - 2^-54 is the midpoint of 1 - 2^-53 and 1
            Assert.False(new DoubleWord(1, -Math.Pow(2, -54), 0).RoundsTo(out rounded));
            Assert.True(new DoubleWord(1, -Math.Pow(2, -55), Math.Pow(2, -56)).RoundsTo(out rounded));
            Assert.False(new DoubleWord(1, -Math.Pow(2, -55), Math.Pow(2, -55)).RoundsTo(out rounded));
            // not a power of two: spacing 2^-52 on both sides
            Assert.True(new DoubleWord(1 + u, -half * 0.75, half * 0.2).RoundsTo(out rounded));
            Assert.False(new DoubleWord(1 + u, -half * 0.75, half * 0.25).RoundsTo(out rounded));
            // negative values mirror
            Assert.False(new DoubleWord(-1, -half, 0).RoundsTo(out rounded));
            Assert.True(new DoubleWord(-1, -half / 4, half / 8).RoundsTo(out rounded));
            // zero: certified only if it is an exact zero
            Assert.True(new DoubleWord(0, 0, 0).RoundsTo(out rounded));
            Assert.True(new DoubleWord(-0.0, 0, 0).RoundsTo(out rounded));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(rounded));       // +0, not -0
            Assert.False(new DoubleWord(0, 0, 1e-300).RoundsTo(out rounded));
            Assert.False(new DoubleWord(1e-40, 0, double.PositiveInfinity).RoundsTo(out rounded));
        }

        // ---- the crossing of an edge with a plane ----
        static Q Dot(Q[] n, Q x, Q y, Q z) => n[0] * x + n[1] * y + n[2] * z;

        // the exact crossing coordinates of the segment s-t with the plane of tri, rounded to nearest, ties to even
        static Vector3d Crossing(Vector3d s, Vector3d t, Triangle3d tri)
        {
            Q[] v0 = { Q.From(tri.V0.x), Q.From(tri.V0.y), Q.From(tri.V0.z) };
            Q[] e1 = { Q.From(tri.V1.x) - v0[0], Q.From(tri.V1.y) - v0[1], Q.From(tri.V1.z) - v0[2] };
            Q[] e2 = { Q.From(tri.V2.x) - v0[0], Q.From(tri.V2.y) - v0[1], Q.From(tri.V2.z) - v0[2] };
            Q[] n = { e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0] };
            Q ds = Dot(n, Q.From(s.x) - v0[0], Q.From(s.y) - v0[1], Q.From(s.z) - v0[2]);
            Q dt = Dot(n, Q.From(t.x) - v0[0], Q.From(t.y) - v0[1], Q.From(t.z) - v0[2]);
            Q w = ds / (ds - dt);
            return new Vector3d((Q.From(s.x) + w * (Q.From(t.x) - Q.From(s.x))).Nearest(),
                                (Q.From(s.y) + w * (Q.From(t.y) - Q.From(s.y))).Nearest(),
                                (Q.From(s.z) + w * (Q.From(t.z) - Q.From(s.z))).Nearest());
        }

        static int Side(Triangle3d tri, Vector3d p) => ExactPredicates.Orient3d(tri.V0, tri.V1, tri.V2, p);

        // random crossing edges of the plane of a triangle, in the families of the audit
        static IEnumerable<(Vector3d s, Vector3d t, Triangle3d tri)> Edges(int count, int seed, bool axis, double scale)
        {
            var r = new Random(seed);
            int made = 0;
            while (made < count) {
                Vector3d P() => axis ? new Vector3d(r.Next(0, 5) * 0.5, r.Next(0, 5) * 0.5, r.Next(-2, 3) * 0.5) * scale
                                     : new Vector3d(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1) * scale;
                var tri = new Triangle3d(P(), P(), P());
                if (ExactTriangleIntersection.IsDegenerate(ref tri)) continue;
                Vector3d s = P(), t = P();
                if (Side(tri, s) * Side(tri, t) >= 0) continue;
                made++;
                yield return (s, t, tri);
            }
        }

        public static IEnumerable<object[]> Families()
        {
            foreach (bool axis in new[] { false, true })
                foreach (double scale in new[] { 1.0, 1e-5, 4096.0, 1e9 })
                    yield return new object[] { axis, scale };
        }

        [Theory]
        [MemberData(nameof(Families))]
        public void CrossingPoint_IsTheCorrectlyRoundedExactCrossing(bool axis, double scale)
        {
            int certified = 0, n = 0;
            foreach (var (s, t, tri0) in Edges(500, 7, axis, scale)) {
                Triangle3d tri = tri0;
                DoubleWord[] normal = IntrTriangle3Triangle3.plane_normal(ref tri);
                Vector3d expected = Crossing(s, t, tri);
                Vector3d got = IntrTriangle3Triangle3.crossing_point(s, t, ref tri, normal);
                Assert.True(got == expected, $"crossing {got} instead of {expected} (axis {axis}, scale {scale})");
                // the exact route alone rounds the same
                Assert.True(IntrTriangle3Triangle3.crossing_exact(s, t, ref tri) == expected, "exact route");
                if (IntrTriangle3Triangle3.crossing_dd(s, t, tri.V0, normal, out Vector3d x)) {
                    certified++;
                    Assert.True(x == expected, "certified double-word point");
                }
                n++;
            }
            Assert.True(certified > 0.5 * n, "certified " + certified + " of " + n);
        }

        [Fact]
        public void CrossingPoint_AtExactTies_RoundsHalfToEven_AndIsNeverCertified()
        {
            // plane z = 0, s.z = -1, t.z = 5: w = 1/6 (not a dyadic fraction: the double-word division has rounding noise);
            // s.x on the 2^-52 grid, t.x - s.x = 3 k 2^-52 (k odd): x = s.x + k 2^-52 / 2 is exactly the midpoint of two doubles
            var tri = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            DoubleWord[] normal = IntrTriangle3Triangle3.plane_normal(ref tri);
            double u = Math.Pow(2, -52);
            var r = new Random(11);
            int ties = 0;
            for (int i = 0; i < 400; i++) {
                double scale = Math.Pow(2, r.Next(-6, 7));
                double sx = (1 + r.Next(0, 1 << 20) * u) * scale;
                long k = 2 * r.Next(1, 40) + 1;
                double tx = sx + 3 * k * u * scale;
                Vector3d s = new Vector3d(sx, 0.25, -1), t = new Vector3d(tx, 0.5, 5);
                Q x = Q.From(sx) + (Q.From(tx) - Q.From(sx)) / new Q(6, 1);
                double expected = x.Nearest();
                // (it is a tie: x is an odd multiple of half the grid, grid = u * scale)
                Assert.True(Q.From(expected).CompareTo(x) != 0, "a tie is not a double");
                Vector3d got = IntrTriangle3Triangle3.crossing_point(s, t, ref tri, normal);
                Assert.True(got.x == expected, $"x {got.x:R} instead of {expected:R} for s.x {sx:R}, t.x {tx:R}");
                Assert.False(IntrTriangle3Triangle3.crossing_dd(s, t, tri.V0, normal, out Vector3d dd), "a tie must not be certified: " + sx.ToString("R") + " " + tx.ToString("R"));
                Assert.True(IntrTriangle3Triangle3.crossing_exact(s, t, ref tri).x == expected, "exact route");
                ties++;
            }
            Assert.Equal(400, ties);
        }

        [Fact]
        public void CrossingPoint_NearTies_AreRoundedLikeTheExactRoute()
        {
            // the tie above with t.z moved by 1 ulp: the crossing is 2^-106 off the midpoint, either side
            var tri = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            DoubleWord[] normal = IntrTriangle3Triangle3.plane_normal(ref tri);
            double u = Math.Pow(2, -52);
            var r = new Random(12);
            int certifiedWrongSide = 0;
            for (int i = 0; i < 600; i++) {
                double sx = 1 + r.Next(0, 1 << 20) * u;
                long k = 2 * r.Next(1, 40) + 1;
                double tx = sx + 3 * k * u;
                double tz = (i % 2 == 0) ? 5 : 5 + 4 * u * 2;          // 5, or 5 + 8 u (a nearby double)
                double sz = -1;
                Vector3d s = new Vector3d(sx, 0.25, sz), t = new Vector3d(tx, 0.5, tz);
                Vector3d expected = Crossing(s, t, tri);
                Vector3d got = IntrTriangle3Triangle3.crossing_point(s, t, ref tri, normal);
                Assert.True(got == expected, $"near tie {got.x:R} instead of {expected.x:R}");
                if (IntrTriangle3Triangle3.crossing_dd(s, t, tri.V0, normal, out Vector3d dd) && dd != expected)
                    certifiedWrongSide++;
            }
            Assert.Equal(0, certifiedWrongSide);
        }

        // ---- Find: the end points ----
        static IEnumerable<(Triangle3d a, Triangle3d b)> HitPairs(int count, int seed, bool axis)
        {
            var r = new Random(seed);
            int made = 0;
            while (made < count) {
                double c = (r.NextDouble() - 0.5);
                Vector3d P(double o) => axis ? new Vector3d(r.Next(0, 5), r.Next(0, 5), r.Next(-2, 3)) * 0.5 + new Vector3d(o, o, 0)
                                             : new Vector3d(o + r.NextDouble(), o + r.NextDouble(), o + r.NextDouble());
                var a = new Triangle3d(P(0), P(0), P(0)); var b = new Triangle3d(P(c), P(c), P(c));
                if (ExactTriangleIntersection.IsDegenerate(ref a) || ExactTriangleIntersection.IsDegenerate(ref b)) continue;
                if (IntrTriangle3Triangle3.Intersects(ref a, ref b) == false) continue;
                made++;
                yield return (a, b);
            }
        }

        [Fact]
        public void RoundRatio_RoundsOnceInTheSubnormalRange()
        {
            BigInteger one = BigInteger.One;
            // 2^-1075 (1 + 2^-54): just above half the smallest subnormal: rounds up to it (rounding to 53 bits first would give exactly the tie, and 0)
            Assert.Equal(double.Epsilon, IntrTriangle3Triangle3.round_ratio((one << 54) + 1, one << 54, -1075));
            // 2^-1075 exactly: a tie, to even: 0
            Assert.Equal(0.0, IntrTriangle3Triangle3.round_ratio(one, one, -1075));
            // 1.5 * 2^-1074: a tie between 1 and 2 units, to even: 2 units
            Assert.Equal(2 * double.Epsilon, IntrTriangle3Triangle3.round_ratio(3, 2, -1074));
            // just below the tie: 1 unit; just above: 2 units
            Assert.Equal(double.Epsilon, IntrTriangle3Triangle3.round_ratio((one << 54) * 3 - 1, one << 55, -1074));
            Assert.Equal(2 * double.Epsilon, IntrTriangle3Triangle3.round_ratio((one << 54) * 3 + 1, one << 55, -1074));
            // below half: 0, positive and negative (the sign of the quotient is kept, as in IEEE division)
            Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(IntrTriangle3Triangle3.round_ratio(-1, 3, -1075)));
            // below half, positive: +0 (not -0)
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(IntrTriangle3Triangle3.round_ratio(1, 3, -1075)));
            // a numerator much larger than the denominator (a negative shift of the denominator) and the exact and the odd tie
            Assert.Equal(Math.Pow(2, 200) / 3 * 3 / 3, IntrTriangle3Triangle3.round_ratio(one << 200, 3, 0) * 3 / 3, 0);
            Assert.Equal((double)(one << 100) / 3, IntrTriangle3Triangle3.round_ratio(one << 100, 3, 0));
            // a normal result is unchanged: 1/3 and its scalings
            Assert.Equal(1.0 / 3, IntrTriangle3Triangle3.round_ratio(1, 3, 0));
            Assert.Equal(Math.Pow(2, -900) / 3, IntrTriangle3Triangle3.round_ratio(1, 3, -900));
            Assert.Equal(-Math.Pow(2, 700) / 3, IntrTriangle3Triangle3.round_ratio(-1, 3, 700));
        }

        [Fact]
        public void Intersects_AtHugeScale_AgreesWithTheBaseScale()
        {
            // cross products overflow at 2^700: disjoint and touching pairs as well as crossing ones
            var a = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(0, 2, 0));
            var cases = new[] {
                new Triangle3d(new Vector3d(0.5, 0.5, -1), new Vector3d(0.5, 0.5, 1), new Vector3d(3, 3, 0)),      // crossing
                new Triangle3d(new Vector3d(5, 5, 1), new Vector3d(6, 5, 1), new Vector3d(5, 6, 1)),                // disjoint
                new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(0, 0, 1), new Vector3d(-1, -1, 1)),              // touching at a corner
                new Triangle3d(new Vector3d(0.5, 0.5, 1e-3), new Vector3d(1, 0.5, 1e-3), new Vector3d(0.5, 1, 1e-3)),  // parallel above
            };
            foreach (double e in new[] { 700, -700 }) {
                double s = Math.Pow(2, e);
                Triangle3d Big(Triangle3d x) => new Triangle3d(x.V0 * s, x.V1 * s, x.V2 * s);
                foreach (var b in cases) {
                    var a0 = a; var b0 = b; var a1 = Big(a); var b1 = Big(b);
                    Assert.True(IntrTriangle3Triangle3.Intersects(ref a0, ref b0) == IntrTriangle3Triangle3.Intersects(ref a1, ref b1), "Intersects at 2^" + e);
                    Assert.True(new IntrTriangle3Triangle3(a, b).Test() == new IntrTriangle3Triangle3(Big(a), Big(b)).Test(), "Test at 2^" + e);
                    Assert.True(new IntrTriangle3Triangle3(a, b).Find() == new IntrTriangle3Triangle3(Big(a), Big(b)).Find(), "Find at 2^" + e);
                }
            }
        }

        [Fact]
        public void FindTransversal_IsTakenForGenericCrossings_AndLeavesTouchingContactsToTheExactRoute()
        {
            int generic = 0;
            foreach (var (a, b) in HitPairs(300, 41, false)) {
                var f = new IntrTriangle3Triangle3(a, b);
                Assert.True(f.find_transversal(), "a generic crossing takes the fast route");
                Assert.Equal(IntersectionType.Segment, f.Type);
                generic++;
            }
            Assert.Equal(300, generic);
            // an edge of A crossing an edge of B in a point, the other ends of the contact on opposite sides of it: a point contact
            var r = new Random(5);
            int touching = 0;
            for (int it = 0; it < 400 && touching < 30; it++) {
                Vector3d V() => new Vector3d(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1);
                Vector3d u = V(), w = V(), a3 = V(), b3 = V();
                var A = new Triangle3d(-u, u, a3); var B = new Triangle3d(-w, w, b3);
                if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B)) continue;
                var f = new IntrTriangle3Triangle3(A, B);
                if (f.Find() == false || f.Type != IntersectionType.Point) continue;
                touching++;
                Assert.False(new IntrTriangle3Triangle3(A, B).find_transversal(), "a touching contact is left to the exact route");
                Assert.Equal(Vector3d.Zero, f.Points[0]);
            }
            Assert.True(touching >= 10, "touching " + touching);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Find_EndPoints_AreCorrectlyRoundedCrossingsOfTheEdges(bool axis)
        {
            int segments = 0;
            foreach (var (a, b) in HitPairs(1500, 21, axis)) {
                var f = new IntrTriangle3Triangle3(a, b);
                Assert.True(f.Find());
                if (f.Type != IntersectionType.Segment) continue;
                segments++;
                // each end point is a corner of a triangle, or the rounded crossing of an edge of one triangle with the plane of
                // the other (exact rounding to nearest)
                var cands = new List<Vector3d> { a.V0, a.V1, a.V2, b.V0, b.V1, b.V2 };
                foreach (var (tri, other) in new[] { (a, b), (b, a) })
                    for (int i = 0; i < 3; i++) {
                        Vector3d s = tri[i], t = tri[(i + 1) % 3];
                        if (Side(other, s) * Side(other, t) < 0)
                            cands.Add(Crossing(s, t, other));
                    }
                Assert.Contains(f.Points[0], cands);
                Assert.Contains(f.Points[1], cands);
            }
            Assert.True(segments > 900, "segments " + segments);
        }
    }
}
