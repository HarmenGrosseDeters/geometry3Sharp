using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Direct unit tests for SnapRounding2 certified floating-point filters and exact 64-bit span sorting:
    /// MeetsFilter (open interior and Hobby half-open contact), SpanLong / CompareMetLong, EntryInterval,
    /// and TryCrossingPixelFast.
    /// Kept in its own file so SnapRounding2IndexTests.cs stays compilable against base master for CI's
    /// new-tests-on-base check, while its class name matches the stryker-mesh-snaprounding shard filter.
    /// </summary>
    public class SnapRounding2IndexTestsFastPaths
    {
        static (BigInteger px, BigInteger py, BigInteger qx, BigInteger qy, BigInteger W) ToUnits(Vector2d a, Vector2d b, double w)
        {
            double[] vals = { a.x, a.y, b.x, b.y, w };
            var mant = new BigInteger[5];
            var exp = new int[5];
            int lo = int.MaxValue;
            for (int i = 0; i < 5; ++i) {
                ExactPoint3.Decompose(vals[i], out mant[i], out exp[i]);
                if (!mant[i].IsZero && exp[i] < lo) lo = exp[i];
            }
            if (lo == int.MaxValue) lo = 0;
            BigInteger U(int i) => mant[i].IsZero ? BigInteger.Zero : mant[i] << (exp[i] - lo);
            return (2 * U(0), 2 * U(1), 2 * U(2), 2 * U(3), U(4));
        }

        static bool ExactMeets(Vector2d a, Vector2d b, long kx, long ky, double w, bool interior)
        {
            var (px, py, qx, qy, W) = ToUnits(a, b, w);
            BigInteger x0 = (2 * (BigInteger)kx - 1) * W, x1 = (2 * (BigInteger)kx + 1) * W;
            BigInteger y0 = (2 * (BigInteger)ky - 1) * W, y1 = (2 * (BigInteger)ky + 1) * W;
            BigInteger lx = BigInteger.Min(px, qx), hx = BigInteger.Max(px, qx);
            BigInteger ly = BigInteger.Min(py, qy), hy = BigInteger.Max(py, qy);
            if (hx < x0 || lx > x1 || hy < y0 || ly > y1) return false;
            int pos = 0, neg = 0;
            bool point = px == qx && py == qy;
            if (!point) {
                BigInteger dx = qx - px, dy = qy - py;
                void S(BigInteger cx, BigInteger cy) { int o = (dx * (cy - py) - dy * (cx - px)).Sign; if (o > 0) ++pos; else if (o < 0) ++neg; }
                S(x0, y0); S(x1, y0); S(x1, y1); S(x0, y1);
            }
            bool open = point ? (px > x0 && px < x1 && py > y0 && py < y1) : (hx > x0 && lx < x1 && hy > y0 && ly < y1 && pos > 0 && neg > 0);
            if (open) return true;
            if (interior) return false;
            if (!point && (pos == 4 || neg == 4)) return false;
            if (py == qy && (py == y0 || py == y1)) return py == y0 && hx >= x0 && lx < x1;
            if (px == qx && (px == x0 || px == x1)) return px == x0 && hy >= y0 && ly < y1;
            if (px >= x0 && px <= x1 && py >= y0 && py <= y1) return px < x1 && py < y1;
            if (qx >= x0 && qx <= x1 && qy >= y0 && qy <= y1) return qx < x1 && qy < y1;
            return ((qx - px) * (y0 - py) - (qy - py) * (x0 - px)).IsZero && lx <= x0 && x0 <= hx && ly <= y0 && y0 <= hy;
        }

        static (((BigInteger num, BigInteger den) entry, (BigInteger num, BigInteger den) exit) t, (long, long) p) ExactSpan(
            BigInteger px, BigInteger py, BigInteger qx, BigInteger qy, BigInteger W, (long, long) k)
        {
            BigInteger dx = qx - px, dy = qy - py;
            BigInteger absDx = BigInteger.Abs(dx), absDy = BigInteger.Abs(dy);
            BigInteger x0 = (2 * (BigInteger)k.Item1 - 1) * W, x1 = (2 * (BigInteger)k.Item1 + 1) * W;
            BigInteger y0 = (2 * (BigInteger)k.Item2 - 1) * W, y1 = (2 * (BigInteger)k.Item2 + 1) * W;
            (BigInteger n, BigInteger d) enter = (BigInteger.Zero, BigInteger.One);
            (BigInteger n, BigInteger d) leave = (BigInteger.One, BigInteger.One);
            void Slab(BigInteger p, BigInteger d, BigInteger den, BigInteger lo, BigInteger hi)
            {
                if (d.IsZero) return;
                BigInteger nIn = d.Sign > 0 ? lo - p : p - hi;
                BigInteger nOut = d.Sign > 0 ? hi - p : p - lo;
                if (nIn * enter.d > enter.n * den) enter = (nIn, den);
                if (nOut * leave.d < leave.n * den) leave = (nOut, den);
            }
            Slab(px, dx, absDx, x0, x1);
            Slab(py, dy, absDy, y0, y1);
            return ((enter, leave), k);
        }

        static BigInteger FloorDiv(BigInteger a, BigInteger b)
        {
            var q = BigInteger.DivRem(a, b, out var r);
            if (!r.IsZero && (r.Sign < 0) != (b.Sign < 0)) q -= 1;
            return q;
        }

        static (long kx, long ky) ExactCrossingPixel(Vector2d a, Vector2d b, Vector2d c, Vector2d d, double w)
        {
            double[] vals = { a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y, w };
            var mant = new BigInteger[9];
            var exp = new int[9];
            int lo = int.MaxValue;
            for (int i = 0; i < 9; ++i) {
                ExactPoint3.Decompose(vals[i], out mant[i], out exp[i]);
                if (!mant[i].IsZero && exp[i] < lo) lo = exp[i];
            }
            BigInteger U(int i) => mant[i].IsZero ? BigInteger.Zero : mant[i] << (exp[i] - lo);
            BigInteger ax = 2 * U(0), ay = 2 * U(1), bx = 2 * U(2), by = 2 * U(3);
            BigInteger cx = 2 * U(4), cy = 2 * U(5), dx = 2 * U(6), dy = 2 * U(7), W = U(8);
            BigInteger den = (bx - ax) * (dy - cy) - (by - ay) * (dx - cx);
            BigInteger num = (cx - ax) * (dy - cy) - (cy - ay) * (dx - cx);
            if (den.Sign < 0) { den = -den; num = -num; }
            BigInteger qx = FloorDiv(ax * den + num * (bx - ax) + W * den, 2 * W * den);
            BigInteger qy = FloorDiv(ay * den + num * (by - ay) + W * den, 2 * W * den);
            return ((long)qx, (long)qy);
        }

        [Fact]
        public void MeetsFilter_MatchesExactMeets_BothInteriorAndHalfOpen()
        {
            // Explicit boundary/corner/degenerate/range cases that MUST return 0 (undecided).
            double w1 = 1.0;
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-2, -0.5), new Vector2d(2, -0.5), 0, 0, w1)); // bottom side
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-2, 0.5), new Vector2d(2, 0.5), 0, 0, w1));   // top side
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-0.5, -2), new Vector2d(-0.5, 2), 0, 0, w1)); // left side
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(0.5, -2), new Vector2d(0.5, 2), 0, 0, w1));   // right side
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-1.0, 0.0), new Vector2d(0.0, -1.0), 0, 0, w1)); // corner (-0.5,-0.5)
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(0.0, 0.0), new Vector2d(0.0, 0.0), 0, 0, w1));   // degenerate point inside
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-2, 0), new Vector2d(-0.5, 0), 0, 0, w1));       // ends on x0
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(0.5, 0), new Vector2d(2, 0), 0, 0, w1));         // ends on x1
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(0, -2), new Vector2d(0, -0.5), 0, 0, w1));       // ends on y0
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(0, 0.5), new Vector2d(0, 2), 0, 0, w1));         // ends on y1

            // Guard edges: w <= 0, |k| >= 2^52, extreme coordinates outside (1e-140, 1e140).
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-1, 0), new Vector2d(1, 0), 0, 0, 0.0));
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-1, 0), new Vector2d(1, 0), 0, 0, -1.0));
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-1, 0), new Vector2d(1, 0), 1L << 52, 0, 1.0));
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-1, 0), new Vector2d(1, 0), -(1L << 52), 0, 1.0));
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-1, 0), new Vector2d(1, 0), 0, long.MinValue, 1.0));
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-1, 0), new Vector2d(1, 0), 0, long.MaxValue, 1.0));
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-1e-145, 0), new Vector2d(1e-145, 0), 0, 0, 1e-145));
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-1e145, 0), new Vector2d(1e145, 0), 0, 0, 1e145));

            // Coords & sides in (1e-140, 1e140), while products l/r fail OkFilter (~1e-200 and ~1e200): must return 0.
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-2e-100, -1e-100), new Vector2d(2e-100, 1e-100), 0, 0, 1e-100));
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-2e100, -1e100), new Vector2d(2e100, 1e100), 0, 0, 1e100));
            // Coords & products l/r (~1e-136) pass OkFilter, while SN-5 bounds b (~1e-150) fail OkFilter: must return 0.
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-2e-68, -1e-68), new Vector2d(2e-68, 1e-68), 0, 0, 1e-68));

            // Clear interior crossing -> +1; clear outside (box and separating line) -> -1.
            Assert.Equal(1, SnapRounding2.MeetsFilter(new Vector2d(-2, -1), new Vector2d(2, 1), 0, 0, 1.0));
            Assert.Equal(-1, SnapRounding2.MeetsFilter(new Vector2d(-3, 0), new Vector2d(-1, 0), 0, 0, 1.0));
            Assert.Equal(-1, SnapRounding2.MeetsFilter(new Vector2d(-1, 0.8), new Vector2d(0.8, 2.6), 0, 0, 1.0));

            // Near-corner orientation within the 1e-14 SN-5 bound on all 4 corners (o0..o3) -> each must return 0.
            double epsUlp = ExactInterval.Up(-0.5) - (-0.5);
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-1.0, 0.0), new Vector2d(0.0, -1.0 + 2 * epsUlp), 0, 0, 1.0)); // o0 (-0.5,-0.5)
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(1.0, 0.0), new Vector2d(0.0, -1.0 + 2 * epsUlp), 0, 0, 1.0));  // o1 ( 0.5,-0.5)
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(1.0, 0.0), new Vector2d(0.0, 1.0 - 2 * epsUlp), 0, 0, 1.0));   // o2 ( 0.5, 0.5)
            Assert.Equal(0, SnapRounding2.MeetsFilter(new Vector2d(-1.0, 0.0), new Vector2d(0.0, 1.0 - 2 * epsUlp), 0, 0, 1.0));  // o3 (-0.5, 0.5)

            // Random fuzz across binary and decimal widths and multiple exponent frames:
            // Whenever MeetsFilter returns non-zero, it MUST agree with BOTH interior=true AND interior=false!
            var rnd = new Random(20261128);
            double[] widths = { 0.01, 0.25, 0.3, 1.0 / 3.0, 1.0, 7.5 };
            int[] frames = { -450, -100, 0, 100, 420 };
            int posCount = 0, negCount = 0, zeroCount = 0;
            foreach (double w0 in widths) {
                foreach (int fr in frames) {
                    double w = w0 * Math.Pow(2, fr);
                    for (int n = 0; n < 400; ++n) {
                        long kx = rnd.Next(-20, 20) + (rnd.Next(6) == 0 ? (1L << rnd.Next(15, 45)) : 0);
                        long ky = rnd.Next(-20, 20);
                        if (rnd.Next(15) == 0) kx = (1L << rnd.Next(52, 56)) + rnd.Next(0, 3);
                        double cx = kx * w, cy = ky * w, h = 0.5 * w;
                        Vector2d Pick()
                        {
                            switch (rnd.Next(5)) {
                                case 0: return new Vector2d(cx + (rnd.Next(2) * 2 - 1) * h, cy + (rnd.Next(2) * 2 - 1) * h);
                                case 1: return new Vector2d(cx + (rnd.Next(2) * 2 - 1) * h, cy + (rnd.NextDouble() - 0.5) * 3 * w);
                                case 2: { var c = new Vector2d(cx - h, cy - h); return new Vector2d(ExactInterval.Up(c.x), ExactInterval.Down(c.y)); }
                                case 3: return new Vector2d(cx + (rnd.NextDouble() - 0.5) * 4 * w, cy - h);
                                default: return new Vector2d(cx + (rnd.NextDouble() - 0.5) * 4 * w, cy + (rnd.NextDouble() - 0.5) * 4 * w);
                            }
                        }
                        var a = Pick();
                        var b = rnd.Next(10) == 0 ? a : Pick();
                        int mf = SnapRounding2.MeetsFilter(a, b, kx, ky, w);
                        if (mf == 0) {
                            ++zeroCount;
                        } else {
                            bool exInt = ExactMeets(a, b, kx, ky, w, true);
                            bool exCon = ExactMeets(a, b, kx, ky, w, false);
                            Assert.Equal(mf > 0, exInt);
                            Assert.Equal(mf > 0, exCon);
                            if (mf > 0) ++posCount; else ++negCount;
                        }
                    }
                }
            }
            Assert.True(posCount > 500 && negCount > 1000 && zeroCount > 500,
                $"Counts: +1={posCount}, -1={negCount}, 0={zeroCount}");
        }

        [Fact]
        public void SpanLong_And_CompareMetLong_MatchExactBigIntegerSpanAndCompareMet()
        {
            // Cover all 4 quadrants, horizontal/vertical, degenerate point, entry ties broken by exit,
            // and entry+exit ties broken by pixel (X, then Y), plus large 64-bit coordinates.
            long wL = 1000L;
            var segments = new (long px, long py, long qx, long qy)[] {
                (0, 0, 10 * 2 * wL, 7 * 2 * wL),          // dx > 0, dy > 0
                (10 * 2 * wL, 7 * 2 * wL, 0, 0),          // dx < 0, dy < 0
                (0, 7 * 2 * wL, 10 * 2 * wL, 0),          // dx > 0, dy < 0
                (10 * 2 * wL, 0, 0, 7 * 2 * wL),          // dx < 0, dy > 0
                (0, 0, 10 * 2 * wL, 0),                   // horizontal dx > 0
                (10 * 2 * wL, 0, 0, 0),                   // horizontal dx < 0
                (0, 0, 0, 10 * 2 * wL),                   // vertical dy > 0
                (0, 10 * 2 * wL, 0, 0),                   // vertical dy < 0
                (wL, 0, 10 * 2 * wL, 5 * 2 * wL),         // starts on shared side x = +wL between pixel (0,0) and (1,0): entry tie broken by exit!
                (0, wL, 5 * 2 * wL, 10 * 2 * wL),         // starts on shared side y = +wL: entry tie broken by exit!
                (0, 0, 0, 0),                             // degenerate point
                (100000000L * wL, -50000000L * wL, 100000010L * wL, -49999993L * wL), // large coordinates
            };

            foreach (var (px, py, qx, qy) in segments) {
                long dx = qx - px, dy = qy - py;
                long absDx = Math.Abs(dx), absDy = Math.Abs(dy);
                long kMinX = Math.Min(px, qx) / (2 * wL) - 1, kMaxX = Math.Max(px, qx) / (2 * wL) + 1;
                long kMinY = Math.Min(py, qy) / (2 * wL) - 1, kMaxY = Math.Max(py, qy) / (2 * wL) + 1;
                var longList = new List<SnapRounding2.MetLong>();
                var bigList = new List<(((BigInteger num, BigInteger den) entry, (BigInteger num, BigInteger den) exit) t, (long, long) p)>();
                for (long kx = kMinX; kx <= kMaxX; ++kx) {
                    for (long ky = kMinY; ky <= kMaxY; ++ky) {
                        // Only test pixels whose closed square the segment actually meets
                        long x0 = (2 * kx - 1) * wL, x1 = (2 * kx + 1) * wL;
                        long y0 = (2 * ky - 1) * wL, y1 = (2 * ky + 1) * wL;
                        if (Math.Max(px, qx) < x0 || Math.Min(px, qx) > x1 || Math.Max(py, qy) < y0 || Math.Min(py, qy) > y1) continue;
                        var ex = ExactSpan(px, py, qx, qy, wL, (kx, ky));
                        if (ex.t.entry.num * ex.t.exit.den > ex.t.exit.num * ex.t.entry.den) continue; // misses closed square
                        var ml = SnapRounding2.SpanLong(px, py, dx, dy, absDx, absDy, wL, (kx, ky));
                        Assert.Equal(ex.t.entry.num, (BigInteger)ml.EnN);
                        Assert.Equal(ex.t.entry.den, (BigInteger)ml.EnD);
                        Assert.Equal(ex.t.exit.num, (BigInteger)ml.ExN);
                        Assert.Equal(ex.t.exit.den, (BigInteger)ml.ExD);
                        Assert.Equal(kx, ml.X);
                        Assert.Equal(ky, ml.Y);
                        longList.Add(ml);
                        bigList.Add(ex);
                    }
                }
                Assert.True(longList.Count > 0);
                for (int i = 0; i < longList.Count; ++i) {
                    for (int j = 0; j < longList.Count; ++j) {
                        int cL = Math.Sign(SnapRounding2.CompareMetLong(longList[i], longList[j]));
                        int cB = Math.Sign(SnapRounding2.CompareMet(bigList[i], bigList[j]));
                        Assert.Equal(cB, cL);
                    }
                }
            }

            // Explicitly exercise CompareMetLong tie-breaks: entry tie -> exit (equal and unequal den) -> X -> Y.
            var baseM = new SnapRounding2.MetLong { EnN = 1, EnD = 2, ExN = 2, ExD = 3, X = 5, Y = 7 };
            var sameEnDiffDen = new SnapRounding2.MetLong { EnN = 2, EnD = 4, ExN = 3, ExD = 4, X = 5, Y = 7 }; // ex 2/3 < 3/4
            var sameEnExDiffX = new SnapRounding2.MetLong { EnN = 2, EnD = 4, ExN = 4, ExD = 6, X = 6, Y = 7 };
            var sameEnExDiffY = new SnapRounding2.MetLong { EnN = 2, EnD = 4, ExN = 4, ExD = 6, X = 5, Y = 8 };
            Assert.True(SnapRounding2.CompareMetLong(baseM, sameEnDiffDen) < 0);
            Assert.True(SnapRounding2.CompareMetLong(sameEnDiffDen, baseM) > 0);
            Assert.True(SnapRounding2.CompareMetLong(baseM, sameEnExDiffX) < 0);
            Assert.True(SnapRounding2.CompareMetLong(sameEnExDiffX, baseM) > 0);
            Assert.True(SnapRounding2.CompareMetLong(baseM, sameEnExDiffY) < 0);
            Assert.True(SnapRounding2.CompareMetLong(sameEnExDiffY, baseM) > 0);
            Assert.Equal(0, SnapRounding2.CompareMetLong(baseM, baseM));

            // Overflow-safety test for the u.EnD == v.EnD and u.ExD == v.ExD short-circuits:
            // Valid useLong horizontal segment: dx = 10^10, dy = 0, wL = 1 (adx * (ady + 2*wL + 4) = 6e10 < 1e18).
            // At kx = 250_000_000 (EnN = 5e8 - 1), EnN * 10^10 ~ +5e18 < long.MaxValue;
            // at kx = 500_000_000 (EnN = 1e9 - 1), EnN * 10^10 ~ 1e19 > long.MaxValue wraps negative, flipping the sign!
            const long bigDx = 10_000_000_000L;
            var ml1 = SnapRounding2.SpanLong(0, 0, bigDx, 0, bigDx, 0, 1, (250_000_000L, 0));
            var ml2 = SnapRounding2.SpanLong(0, 0, bigDx, 0, bigDx, 0, 1, (500_000_000L, 0));
            Assert.Equal(bigDx, ml1.EnD);
            Assert.Equal(bigDx, ml2.EnD);
            Assert.True(SnapRounding2.CompareMetLong(ml1, ml2) < 0);
            Assert.True(SnapRounding2.CompareMetLong(ml2, ml1) > 0);
            // Same entry, large equal exit denominator where ExN * ExD overflows 64-bit signed long if cross-multiplied:
            var exHi1 = new SnapRounding2.MetLong { EnN = 3_000_000_000L, EnD = bigDx, ExN = 4_000_000_000L, ExD = bigDx, X = 1, Y = 0 };
            var exHi2 = new SnapRounding2.MetLong { EnN = 3_000_000_000L, EnD = bigDx, ExN = 5_000_000_000L, ExD = bigDx, X = 2, Y = 0 };
            Assert.True(SnapRounding2.CompareMetLong(exHi1, exHi2) < 0);
            Assert.True(SnapRounding2.CompareMetLong(exHi2, exHi1) > 0);
        }

        [Fact]
        public void EntryInterval_ContainsExactEntryParameter_AcrossAllOrientationsAndExtremes()
        {
            // Guard checks: w <= 0, |k| >= 2^52 (both signs on both axes), subnormal dx (dxI contains 0) -> [0, 1].
            void CheckGuard(Vector2d pa, Vector2d pb, double w, long kx, long ky)
            {
                var axI = ExactInterval.Of(pa.x); var ayI = ExactInterval.Of(pa.y);
                var dxI = ExactInterval.Of(pb.x) - axI; var dyI = ExactInterval.Of(pb.y) - ayI;
                SnapRounding2.EntryInterval(pa, pb, axI, ayI, dxI, dyI, ExactInterval.Of(w), kx, ky, out double lo, out double hi);
                Assert.Equal(0.0, lo);
                Assert.Equal(1.0, hi);
            }
            CheckGuard(new Vector2d(0, 0), new Vector2d(1, 1), 0.0, 0, 0);
            CheckGuard(new Vector2d(0, 0), new Vector2d(1, 1), -1.0, 0, 0);
            CheckGuard(new Vector2d(0, 0), new Vector2d(1, 1), 1.0, 1L << 52, 0);
            CheckGuard(new Vector2d(0, 0), new Vector2d(1, 1), 1.0, -(1L << 52), 0);
            CheckGuard(new Vector2d(0, 0), new Vector2d(1, 1), 1.0, 0, 1L << 52);
            CheckGuard(new Vector2d(0, 0), new Vector2d(1, 1), 1.0, 0, -(1L << 52));
            CheckGuard(new Vector2d(0, 0), new Vector2d(double.Epsilon, 0), 1.0, 0, 0); // dxI.Lo == 0 -> division returns whole -> [0, 1]

            // Random meeting pixels across binary/decimal widths, all 4 quadrants, horizontal/vertical, and scales:
            var rnd = new Random(1337);
            double[] widths = { 0.003, 1.0 / 1024.0, 0.3, 1.0, 7.5 };
            int[] frames = { -150, 0, 150 };
            int checkedPixels = 0, separatedPairs = 0;
            foreach (double w0 in widths) {
                foreach (int fr in frames) {
                    double w = w0 * Math.Pow(2, fr);
                    var wI = ExactInterval.Of(w);
                    for (int s = 0; s < 40; ++s) {
                        Vector2d pa, pb;
                        switch (s % 6) {
                            case 0: pa = new Vector2d(0, 0); pb = new Vector2d(12 * w, 0); break;             // horizontal +
                            case 1: pa = new Vector2d(12 * w, 0); pb = new Vector2d(0, 0); break;             // horizontal -
                            case 2: pa = new Vector2d(0, 0); pb = new Vector2d(0, 12 * w); break;             // vertical +
                            case 3: pa = new Vector2d(0, 12 * w); pb = new Vector2d(0, 0); break;             // vertical -
                            default:
                                pa = new Vector2d((rnd.NextDouble() - 0.5) * 20 * w, (rnd.NextDouble() - 0.5) * 20 * w);
                                pb = new Vector2d((rnd.NextDouble() - 0.5) * 20 * w, (rnd.NextDouble() - 0.5) * 20 * w);
                                break;
                        }
                        var (px, py, qx, qy, W) = ToUnits(pa, pb, w);
                        var axI = ExactInterval.Of(pa.x); var ayI = ExactInterval.Of(pa.y);
                        var dxI = ExactInterval.Of(pb.x) - axI; var dyI = ExactInterval.Of(pb.y) - ayI;
                        var intervals = new List<(double lo, double hi)>();
                        var grid = new SnapGrid(w);
                        long kMinX = Math.Min(grid.Pixel(pa.x), grid.Pixel(pb.x)) - 1;
                        long kMaxX = Math.Max(grid.Pixel(pa.x), grid.Pixel(pb.x)) + 1;
                        long kMinY = Math.Min(grid.Pixel(pa.y), grid.Pixel(pb.y)) - 1;
                        long kMaxY = Math.Max(grid.Pixel(pa.y), grid.Pixel(pb.y)) + 1;
                        for (long kx = kMinX; kx <= kMaxX; ++kx) {
                            for (long ky = kMinY; ky <= kMaxY; ++ky) {
                                if (!ExactMeets(pa, pb, kx, ky, w, false)) continue;
                                var ex = ExactSpan(px, py, qx, qy, W, (kx, ky));
                                Q exactEntry = new Q(ex.t.entry.num, ex.t.entry.den);
                                SnapRounding2.EntryInterval(pa, pb, axI, ayI, dxI, dyI, wI, kx, ky, out double lo, out double hi);
                                Assert.True(lo >= 0.0 && lo <= hi && hi <= 1.0);
                                Assert.True(Q.FromDouble(lo) <= exactEntry && exactEntry <= Q.FromDouble(hi),
                                    $"Entry {exactEntry} outside [{lo:R}, {hi:R}]");
                                intervals.Add((lo, hi));
                                ++checkedPixels;
                            }
                        }
                        for (int i = 0; i < intervals.Count; ++i)
                            for (int j = i + 1; j < intervals.Count; ++j)
                                if (intervals[i].hi < intervals[j].lo || intervals[j].hi < intervals[i].lo)
                                    ++separatedPairs;
                    }
                }
            }
            Assert.True(checkedPixels > 3000 && separatedPairs > 15000,
                $"checked={checkedPixels}, separated={separatedPairs}");
        }

        [Fact]
        public void TryCrossingPixelFast_MatchesExactBigIntegerCrossingPixel()
        {
            var grid1 = new SnapGrid(1.0);
            // Parallel segments -> den contains 0 -> false.
            Assert.False(SnapRounding2.TryCrossingPixelFast(new Vector2d(0, 0), new Vector2d(1, 1), new Vector2d(0, 1), new Vector2d(1, 2), grid1, out _, out _));
            // Non-positive or infinite grid width -> false.
            Assert.False(SnapRounding2.TryCrossingPixelFast(new Vector2d(0, 0), new Vector2d(2, 2), new Vector2d(0, 2), new Vector2d(2, 0), new SnapGrid(0.0), out _, out _));
            Assert.False(SnapRounding2.TryCrossingPixelFast(new Vector2d(0, 0), new Vector2d(2, 2), new Vector2d(0, 2), new Vector2d(2, 0), new SnapGrid(-1.0), out _, out _));
            Assert.False(SnapRounding2.TryCrossingPixelFast(new Vector2d(0, 0), new Vector2d(2, 2), new Vector2d(0, 2), new Vector2d(2, 0), new SnapGrid(double.PositiveInfinity), out _, out _));
            // Crossing right on x pixel boundary (0.5, 0.25) where ONLY x straddles 0.5 (ky0 == ky1 == 0) -> must return false (kills kx0 != kx1 removal).
            Assert.False(SnapRounding2.TryCrossingPixelFast(
                new Vector2d(0.0, 0.0), new Vector2d(1.0, 0.5),
                new Vector2d(0.0, 0.5), new Vector2d(1.0, 0.0),
                grid1, out _, out _));
            // Crossing right on y pixel boundary (0.25, 0.5) where ONLY y straddles 0.5 (kx0 == kx1 == 0) -> must return false (kills ky0 != ky1 removal).
            Assert.False(SnapRounding2.TryCrossingPixelFast(
                new Vector2d(0.0, 0.0), new Vector2d(0.5, 1.0),
                new Vector2d(0.5, 0.0), new Vector2d(0.0, 1.0),
                grid1, out _, out _));

            // All 4 clauses of PixelLimit (|k| == 2^40): |coord / w| = 2^40 - 0.25 < MaxPixelRatio passes the ratio
            // guard, then SnapGrid.Pixel returns +-2^40 which must be rejected by PixelLimit so PixelOf throws.
            double edgePos = (double)(1L << 40) - 0.25;
            double edgeNeg = -(double)(1L << 40) + 0.25;
            Assert.False(SnapRounding2.TryCrossingPixelFast(
                new Vector2d(edgePos - 10, -10), new Vector2d(edgePos + 10, 10),
                new Vector2d(edgePos - 10, 10), new Vector2d(edgePos + 10, -10), grid1, out _, out _));
            Assert.False(SnapRounding2.TryCrossingPixelFast(
                new Vector2d(edgeNeg - 10, -10), new Vector2d(edgeNeg + 10, 10),
                new Vector2d(edgeNeg - 10, 10), new Vector2d(edgeNeg + 10, -10), grid1, out _, out _));
            Assert.False(SnapRounding2.TryCrossingPixelFast(
                new Vector2d(-10, edgePos - 10), new Vector2d(10, edgePos + 10),
                new Vector2d(10, edgePos - 10), new Vector2d(-10, edgePos + 10), grid1, out _, out _));
            Assert.False(SnapRounding2.TryCrossingPixelFast(
                new Vector2d(-10, edgeNeg - 10), new Vector2d(10, edgeNeg + 10),
                new Vector2d(10, edgeNeg - 10), new Vector2d(-10, edgeNeg + 10), grid1, out _, out _));

            // Crossing well beyond 2^40 on X and on Y separately (hits MaxPixelRatio guard on both ix and iy, never throws OverflowException).
            double hugeCoord = (double)(1L << 41);
            Assert.False(SnapRounding2.TryCrossingPixelFast(
                new Vector2d(hugeCoord - 10, -10), new Vector2d(hugeCoord + 10, 10),
                new Vector2d(hugeCoord - 10, 10), new Vector2d(hugeCoord + 10, -10),
                grid1, out _, out _));
            Assert.False(SnapRounding2.TryCrossingPixelFast(
                new Vector2d(-10, hugeCoord - 10), new Vector2d(10, hugeCoord + 10),
                new Vector2d(10, hugeCoord - 10), new Vector2d(-10, hugeCoord + 10),
                grid1, out _, out _));
            // Extreme scale ratio beyond long range (1e100 / 1e-100 = 1e200) on X and on Y separately -> false without throwing!
            Assert.False(SnapRounding2.TryCrossingPixelFast(
                new Vector2d(1e100, 0), new Vector2d(1e100 + 1e90, 1e90),
                new Vector2d(1e100, 1e90), new Vector2d(1e100 + 1e90, 0),
                new SnapGrid(1e-100), out _, out _));
            Assert.False(SnapRounding2.TryCrossingPixelFast(
                new Vector2d(0, 1e100), new Vector2d(1e90, 1e100 + 1e90),
                new Vector2d(1e90, 1e100), new Vector2d(0, 1e100 + 1e90),
                new SnapGrid(1e-100), out _, out _));

            // Random proper crossings across binary & decimal tolerances:
            var rnd = new Random(998877);
            double[] widths = { 1.0 / 1024.0, 1.0 / 1048576.0, 0.003, 0.3, 1.0 };
            int fastHits = 0, total = 0;
            foreach (double w in widths) {
                var grid = new SnapGrid(w);
                for (int n = 0; n < 400; ++n) {
                    var a = new Vector2d(rnd.NextDouble(), rnd.NextDouble());
                    var b = new Vector2d(rnd.NextDouble(), rnd.NextDouble());
                    var c = new Vector2d(rnd.NextDouble(), rnd.NextDouble());
                    var d = new Vector2d(rnd.NextDouble(), rnd.NextDouble());
                    int o1 = ExactPredicates.Orient2d(a, b, c), o2 = ExactPredicates.Orient2d(a, b, d);
                    if (o1 * o2 >= 0) continue;
                    int o3 = ExactPredicates.Orient2d(c, d, a), o4 = ExactPredicates.Orient2d(c, d, b);
                    if (o3 * o4 >= 0) continue;
                    ++total;
                    var (exKx, exKy) = ExactCrossingPixel(a, b, c, d, w);
                    if (SnapRounding2.TryCrossingPixelFast(a, b, c, d, grid, out long kx, out long ky)) {
                        ++fastHits;
                        Assert.Equal(exKx, kx);
                        Assert.Equal(exKy, ky);
                    }
                }
            }
            Assert.True(total > 300 && fastHits * 100 >= total * 95,
                $"TryCrossingPixelFast hit {fastHits}/{total}");
        }
    }
}
