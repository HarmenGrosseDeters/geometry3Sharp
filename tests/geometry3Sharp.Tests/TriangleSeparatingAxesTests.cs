using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TriangleSeparatingAxes: the conservative "apart" test in front of the pair test of SafeRounding's full pair search.
    /// Soundness is checked against exact arithmetic (the projections as integers, and the exact pair test): a pair that
    /// is apart along an axis has strictly separated exact projections on it, and no common point. The margin is checked
    /// from both sides: inputs whose computed projections are in the wrong order by two units in the last place (a
    /// smaller margin would call them apart), and pairs with a clear gap that must be apart.
    /// </summary>
    public class TriangleSeparatingAxesTests
    {
        const int Stride = TriangleSeparatingAxes.Stride;
        const double U = 1.1102230246251565e-16;   // 2^-53

        // ---- exact arithmetic on doubles: a double is m 2^e ----

        static (BigInteger m, int e) Parts(double d)
        {
            long bits = BitConverter.DoubleToInt64Bits(d);
            int e = (int)((bits >> 52) & 0x7ff);
            long m = bits & 0xfffffffffffffL;
            if (e == 0) e = 1; else m |= 1L << 52;
            return (bits < 0 ? -m : m, e - 1075);
        }

        // a.x exactly, as an integer in units of 2^-2150 (a product of two doubles is a multiple of 2^-2148)
        static BigInteger Dot(Vector3d a, Vector3d x)
        {
            BigInteger r = 0;
            for (int i = 0; i < 3; ++i) {
                var (m1, e1) = Parts(a[i]);
                var (m2, e2) = Parts(x[i]);
                r += (m1 * m2) << (e1 + e2 + 2150);
            }
            return r;
        }

        static Vector3d Axis(double[] data, int o, int k) => new Vector3d(data[o + 3 * k], data[o + 3 * k + 1], data[o + 3 * k + 2]);

        // the exact projections of the two triangles on the axis are strictly separated
        static bool ExactlySeparated(Vector3d a, Triangle3d t, Triangle3d u)
        {
            BigInteger[] pt = { Dot(a, t.V0), Dot(a, t.V1), Dot(a, t.V2) }, pu = { Dot(a, u.V0), Dot(a, u.V1), Dot(a, u.V2) };
            return pu.Min() > pt.Max() || pt.Min() > pu.Max();
        }

        static double[] Data(Triangle3d t)
        {
            var d = new double[Stride];
            TriangleSeparatingAxes.Compute(t.V0, t.V1, t.V2, d, 0);
            return d;
        }

        // data with the one axis a (as axis 0) and the triangle's interval on it; the other axes are zero (never apart)
        static double[] DataOfAxis(Vector3d a, Triangle3d t)
        {
            var d = new double[Stride];
            d[0] = a.x; d[1] = a.y; d[2] = a.z;
            for (int k = 0; k < 4; ++k)
                TriangleSeparatingAxes.SetOwnInterval(d, 0, k, t.V0, t.V1, t.V2);
            return d;
        }

        static bool Apart(Triangle3d t, Triangle3d u)
        {
            return TriangleSeparatingAxes.Apart(Data(t), 0, u.V0, u.V1, u.V2) || TriangleSeparatingAxes.Apart(Data(u), 0, t.V0, t.V1, t.V2);
        }

        // every claim of the filter on the pair, against exact arithmetic; returns whether the pair is apart
        static bool CheckPair(Triangle3d t, Triangle3d u, string what)
        {
            bool apart = false;
            foreach (var (own, other) in new[] { (t, u), (u, t) }) {
                double[] d = Data(own);
                for (int k = 0; k < 4; ++k)
                    if (TriangleSeparatingAxes.ApartAlong(d, 0, k, other.V0, other.V1, other.V2)) {
                        apart = true;
                        Assert.True(ExactlySeparated(Axis(d, 0, k), own, other), $"{what}: apart along axis {k}, but the exact projections are not separated");
                    }
                Assert.Equal(Enumerable.Range(0, 4).Any(k => TriangleSeparatingAxes.ApartAlong(d, 0, k, other.V0, other.V1, other.V2)),
                    TriangleSeparatingAxes.Apart(d, 0, other.V0, other.V1, other.V2));
            }
            if (apart && ExactTriangleIntersection.IsDegenerate(ref t) == false && ExactTriangleIntersection.IsDegenerate(ref u) == false)
                Assert.False(ExactTriangleIntersection.IntersectsBeyondShared(new Index3i(0, 1, 2), ref t, new Index3i(3, 4, 5), ref u), $"{what}: apart, but the triangles intersect");
            return apart;
        }

        static Vector3d Random(Random r, double size) => new Vector3d(size * (2 * r.NextDouble() - 1), size * (2 * r.NextDouble() - 1), size * (2 * r.NextDouble() - 1));

        // a long thin triangle: two corners close together, the third far along a random direction
        static Triangle3d Needle(Random r, Vector3d at, double length, double width)
        {
            Vector3d dir = Random(r, 1);
            return new Triangle3d(at, at + Random(r, width), at + length * dir);
        }

        // ---- soundness ----

        [Fact]
        public void Apart_ImpliesExactSeparationAndNoCommonPoint_RandomPairs()
        {
            var r = new Random(20261122);
            int apart = 0, close = 0, n = 0;
            for (int i = 0; i < 20000; ++i) {
                Triangle3d t, u;
                switch (i % 5) {
                    case 0:     // generic, overlapping boxes
                        t = new Triangle3d(Random(r, 1), Random(r, 1), Random(r, 1)); u = new Triangle3d(Random(r, 1), Random(r, 1), Random(r, 1)); break;
                    case 1:     // needles through one region
                        t = Needle(r, Random(r, 0.1), 2, 1e-3); u = Needle(r, Random(r, 0.1), 2, 1e-3); break;
                    case 2: {   // u nearly in the plane of t, a few units in the last place off
                        t = new Triangle3d(Random(r, 1), Random(r, 1), Random(r, 1));
                        Vector3d c = (t.V0 + t.V1 + t.V2) / 3, off = Random(r, 1e-15);
                        u = new Triangle3d(c + off, t.V1 + Random(r, 1e-15), c + Random(r, 1)); break;
                    }
                    case 3: {   // far from the origin (rounded coordinates of a small feature)
                        Vector3d far = new Vector3d(4.5e5, 5.7e6, 120);
                        t = Needle(r, far + Random(r, 0.01), 3, 1e-4); u = Needle(r, far + Random(r, 0.01), 3, 1e-4); break;
                    }
                    default: {  // parallel thin triangles, a small gap apart
                        t = Needle(r, Random(r, 0.1), 2, 1e-3);
                        Vector3d shift = Random(r, 1e-9 * Math.Pow(10, r.Next(8)));
                        u = new Triangle3d(t.V0 + shift, t.V1 + shift, t.V2 + shift); break;
                    }
                }
                n++;
                if (CheckPair(t, u, $"pair {i}")) apart++; else close++;
            }
            // (the cases are neither all apart nor all close: both answers are exercised)
            Assert.True(apart > n / 4 && close > n / 20, $"{apart} apart, {close} not, of {n}");
        }

        [Fact]
        public void SharedCornerOrCommonPoint_IsNeverApart()
        {
            var r = new Random(7);
            for (int i = 0; i < 5000; ++i) {
                Triangle3d t = i % 2 == 0 ? Needle(r, Random(r, 1), 2, 1e-3) : new Triangle3d(Random(r, 1), Random(r, 1), Random(r, 1));
                // a shared corner, at any of the positions
                Triangle3d u = new Triangle3d(Random(r, 1), Random(r, 1), Random(r, 1));
                u[r.Next(3)] = t[r.Next(3)];
                Assert.False(Apart(t, u), $"pair {i}: a shared corner");
                // a corner of u on an edge of t (the midpoint of two corners, exact where the sum is)
                Vector3d mid = 0.5 * (t.V0 + t.V1);
                if (Dot(new Vector3d(1, 1, 1), mid) * 2 == Dot(new Vector3d(1, 1, 1), t.V0) + Dot(new Vector3d(1, 1, 1), t.V1)
                    && 2 * mid.x == t.V0.x + t.V1.x && 2 * mid.y == t.V0.y + t.V1.y && 2 * mid.z == t.V0.z + t.V1.z)
                    Assert.False(Apart(t, new Triangle3d(mid, Random(r, 1), Random(r, 1))), $"pair {i}: a corner on an edge");
                // the same triangle
                Assert.False(Apart(t, t), $"pair {i}: one triangle");
            }
        }

        // ---- the margin ----

        // An axis and two points with exact a.y < a.x whose computed projections are in the other order, two units in the
        // last place apart: every rounding of p~ is near its bound (one product just above 1, two tiny ones). Found by a
        // search over 8e7 inputs; no input with three units was found, and the bound of the class comment allows fewer
        // than three (each projection is within 3u S of the exact one, S about 1, a unit in the last place is 2u)
        static readonly Vector3d WorstAxis = new Vector3d(1.781302307164903, 1.2419826938966207, 1.3592509964291244);
        static readonly Vector3d WorstLow = new Vector3d(0.5613870234039927, 9.302873918927682E-11, 1.2141342275841785E-11);    // computed 1.0000000001340126
        static readonly Vector3d WorstHigh = new Vector3d(0.5613870234038473, 2.962952717190121E-11, 7.026136681915325E-11);    // computed 1.000000000134013

        [Fact]
        public void WorstCase_ComputedOrderIsWrongByTwoUlps_NotApart()
        {
            // the facts the case rests on, exactly
            Assert.True(Dot(WorstAxis, WorstHigh) < Dot(WorstAxis, WorstLow), "exact order");
            double pl = (WorstAxis.x * WorstLow.x + WorstAxis.y * WorstLow.y) + WorstAxis.z * WorstLow.z;
            double ph = (WorstAxis.x * WorstHigh.x + WorstAxis.y * WorstHigh.y) + WorstAxis.z * WorstHigh.z;
            Assert.Equal(2, BitConverter.DoubleToInt64Bits(ph) - BitConverter.DoubleToInt64Bits(pl));
            // triangle t ends at WorstLow on the axis (its other corners project lower), u starts at WorstHigh: exactly,
            // u's smallest projection is below t's largest, so they are not separated on the axis, whatever is computed
            Vector3d down = -1e-3 * WorstAxis;
            var t = new Triangle3d(WorstLow, WorstLow + down + new Vector3d(1e-4, 0, 0), WorstLow + 2 * down);
            var u = new Triangle3d(WorstHigh, WorstHigh - down + new Vector3d(0, 1e-4, 0), WorstHigh - 2 * down);
            Assert.False(ExactlySeparated(WorstAxis, t, u));
            double[] dt = DataOfAxis(WorstAxis, t), du = DataOfAxis(WorstAxis, u);
            Assert.False(TriangleSeparatingAxes.ApartAlong(dt, 0, 0, u.V0, u.V1, u.V2), "a margin below 2u per side calls this pair apart");
            Assert.False(TriangleSeparatingAxes.ApartAlong(du, 0, 0, t.V0, t.V1, t.V2));
            // the margin as specified: Gamma times the magnitude, at least 4u S on each side (steps 3 to 5 of the class comment)
            double lo, hi;
            TriangleSeparatingAxes.Interval(WorstAxis.x, WorstAxis.y, WorstAxis.z, WorstHigh, WorstHigh, WorstHigh, out lo, out hi);
            Assert.Equal(8 * U, TriangleSeparatingAxes.Gamma);
            Assert.True(ph - lo >= 4 * U * ph && hi - ph >= 4 * U * ph, $"margin {ph - lo} / {hi - ph} at {ph}");
            Assert.True(ph - lo <= 9 * U * ph && hi - ph <= 9 * U * ph, "the margin is not more than Gamma s, up to the rounding of the interval end");
        }

        // the formula of the class comment, written out apart from the implementation
        static void Specified(Vector3d a, Vector3d[] x, out double lo, out double hi)
        {
            double pmin = double.PositiveInfinity, pmax = double.NegativeInfinity, smax = 0;
            foreach (Vector3d v in x) {
                double tx = a.x * v.x, ty = a.y * v.y, tz = a.z * v.z;
                double p = (tx + ty) + tz, s = (Math.Abs(tx) + Math.Abs(ty)) + Math.Abs(tz);
                pmin = Math.Min(pmin, p); pmax = Math.Max(pmax, p); smax = Math.Max(smax, s);
            }
            double m = 8 * U * smax + Math.Pow(2, -1072);
            lo = pmin - m; hi = pmax + m;
        }

        [Fact]
        public void Interval_IsTheSpecifiedFormula_AndEnclosesTheExactProjections()
        {
            // corners of very different magnitude (each corner in turn the largest, each coordinate in turn the dominant
            // product, mixed signs): the margin is that of the largest magnitude, whichever corner and term it comes from
            var r = new Random(13);
            for (int i = 0; i < 6000; ++i) {
                var a = Random(r, 1);
                var x = new Vector3d[3];
                for (int c = 0; c < 3; ++c) {
                    x[c] = Random(r, Math.Pow(10, -r.Next(6)));
                    if (r.Next(3) == 0) { Vector3d v = x[c]; v[r.Next(3)] *= 1e6; x[c] = v; }
                }
                int big = i % 3;
                x[big] = 1e3 * x[big];
                double lo, hi, elo, ehi;
                TriangleSeparatingAxes.Interval(a.x, a.y, a.z, x[0], x[1], x[2], out lo, out hi);
                Specified(a, x, out elo, out ehi);
                Assert.Equal(elo, lo);
                Assert.Equal(ehi, hi);
                BigInteger blo = Dot(new Vector3d(lo, 0, 0), new Vector3d(1, 0, 0)), bhi = Dot(new Vector3d(hi, 0, 0), new Vector3d(1, 0, 0));
                foreach (Vector3d v in x)
                    Assert.True(blo < Dot(a, v) && Dot(a, v) < bhi, $"case {i}: an exact projection outside the interval");
            }
        }

        [Fact]
        public void TiedProjections_ComputedApartByRounding_NotApart()
        {
            // y = x + d with a.d = 0 exactly (d a permutation of a's components): the exact projections are equal, the
            // computed ones differ by the rounding. Even mantissas in [1, 2) make x + d exact
            var r = new Random(11);
            Func<double> even = () => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1 + r.NextDouble()) & ~1L);
            int differ = 0;
            for (int i = 0; i < 200000; ++i) {
                var a = new Vector3d(even(), even(), even() * (r.Next(2) == 0 ? 1 : -1));
                var x = new Vector3d(even(), even(), even());
                Vector3d d = r.Next(3) == 0 ? new Vector3d(a.y, -a.x, 0) : r.Next(2) == 0 ? new Vector3d(a.z, 0, -a.x) : new Vector3d(0, a.z, -a.y);
                Vector3d y = x + d;
                if (Dot(a, x) != Dot(a, y))
                    continue;
                double px = (a.x * x.x + a.y * x.y) + a.z * x.z, py = (a.x * y.x + a.y * y.y) + a.z * y.z;
                if (px != py) differ++;
                // two "triangles" that are the points: tied exactly, never apart
                double[] dx = DataOfAxis(a, new Triangle3d(x, x, x));
                Assert.False(TriangleSeparatingAxes.ApartAlong(dx, 0, 0, y, y, y), $"case {i}: computed {px:R} and {py:R}");
            }
            Assert.True(differ > 10000, $"only {differ} cases with different computed projections");
        }

        [Fact]
        public void ClearGap_IsApart()
        {
            // two thin triangles side by side, a gap of 1e-9 of their length apart, tilted (no axis-aligned box separates them)
            var r = new Random(3);
            int apart = 0, n = 2000;
            for (int i = 0; i < n; ++i) {
                Triangle3d t = Needle(r, Random(r, 1), 2, 1e-3);
                Vector3d normal = (t.V1 - t.V0).Cross(t.V2 - t.V0).Normalized;
                Vector3d shift = 2e-9 * normal;
                var u = new Triangle3d(t.V0 + shift, t.V1 + shift, t.V2 + shift);
                if (CheckPair(t, u, $"pair {i}")) apart++;
            }
            Assert.Equal(n, apart);
            // the unit case: the gap against the margin. Parallel unit triangles at height h: apart for h well above the
            // margin (8u times the magnitude, here about 2), not below it
            var a = new Triangle3d(new Vector3d(1, 1, 1), new Vector3d(2, 1, 1), new Vector3d(1, 2, 1));
            Func<double, Triangle3d> lifted = (h) => new Triangle3d(new Vector3d(1, 1, 1 + h), new Vector3d(2, 1, 1 + h), new Vector3d(1, 2, 1 + h));
            Assert.True(Apart(a, lifted(64 * U)), "a gap of 64u is apart");
            Assert.False(Apart(a, lifted(2 * U)), "a gap of one unit in the last place is inside the margin");
            Assert.False(Apart(a, lifted(0)));
        }

        // ---- underflow, overflow, values that are not finite ----

        [Fact]
        public void PlaneThroughTheOrigin_ZeroProjections_AreUsed()
        {
            Assert.Equal(Math.Pow(2, -1072), TriangleSeparatingAxes.Tiny);
            // a triangle in the plane z = 0 projects to exactly zero on its normal: its interval is (-Tiny, Tiny), and a
            // parallel triangle is apart from it at any height
            var t = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            double[] d = Data(t);
            Assert.Equal(-TriangleSeparatingAxes.Tiny, d[12]);
            Assert.Equal(TriangleSeparatingAxes.Tiny, d[13]);
            foreach (double h in new[] { 5.0, 1e-9, 1e-200, -1e-200, -3.0 }) {
                var u = new Triangle3d(new Vector3d(0, 0, h), new Vector3d(1, 0, h), new Vector3d(0, 1, h));
                Assert.True(TriangleSeparatingAxes.ApartAlong(d, 0, 0, u.V0, u.V1, u.V2), $"height {h}");
                Assert.True(TriangleSeparatingAxes.ApartAlong(Data(u), 0, 0, t.V0, t.V1, t.V2), $"height {h}, from the other side");
                CheckPair(t, u, $"height {h}");
            }
            // in the same plane: not apart along the normal
            var same = new Triangle3d(new Vector3d(5, 5, 0), new Vector3d(6, 5, 0), new Vector3d(5, 6, 0));
            Assert.False(TriangleSeparatingAxes.ApartAlong(d, 0, 0, same.V0, same.V1, same.V2));
            Assert.True(Apart(t, same), "apart along an in-plane axis");
        }

        [Fact]
        public void NearTheBottomOfTheDoubles_GapsAboveTheAbsoluteMargin_AreApart()
        {
            // projections of the size 2^-1040: a relative gap is apart
            var a = new Vector3d(Math.Pow(2, -520), 0, 0);
            Func<double, Vector3d> at = (k) => new Vector3d(k * Math.Pow(2, -520), 0, 0);
            double[] d = DataOfAxis(a, new Triangle3d(at(1), at(1), at(1)));
            Assert.True(TriangleSeparatingAxes.ApartAlong(d, 0, 0, at(2), at(2), at(2)));
            Assert.True(TriangleSeparatingAxes.ApartAlong(d, 0, 0, at(1 + 1e-9), at(1 + 1e-9), at(1 + 1e-9)));
            // projections k 2^-1074, exact: the margin is Tiny = 4 units on each side, so a gap of 8 units is not apart
            // and one of 9 is
            a = new Vector3d(Math.Pow(2, -537), 0, 0);
            Func<double, Vector3d> low = (k) => new Vector3d(k * Math.Pow(2, -537), 0, 0);
            d = DataOfAxis(a, new Triangle3d(low(100), low(100), low(100)));
            Assert.False(TriangleSeparatingAxes.ApartAlong(d, 0, 0, low(108), low(108), low(108)), "a gap of 2 Tiny");
            Assert.True(TriangleSeparatingAxes.ApartAlong(d, 0, 0, low(109), low(109), low(109)), "a gap above 2 Tiny");
            Assert.False(TriangleSeparatingAxes.ApartAlong(d, 0, 0, low(92), low(92), low(92)), "a gap of 2 Tiny below");
            Assert.True(TriangleSeparatingAxes.ApartAlong(d, 0, 0, low(91), low(91), low(91)), "a gap above 2 Tiny below");
        }

        [Fact]
        public void SubnormalProducts_TiedExactly_ComputedDifferent_NotApart()
        {
            // products near 2^-1070 are rounded to multiples of 2^-1074 and the relative margin 8u s underflows to zero:
            // tied points get different computed projections, and only the absolute part of the margin (Tiny) is between them
            int wrong = 0;
            for (int i1 = 1; i1 < 60 && wrong < 50; i1 += 2)
                for (int i2 = 1; i2 < 60 && wrong < 50; i2 += 2)
                    for (int j = 1; j < 40; j += 2) {
                        var a = new Vector3d(i1 * Math.Pow(2, -540), i2 * Math.Pow(2, -540), 0);
                        var x = new Vector3d(j * Math.Pow(2, -538), (j + 2) * Math.Pow(2, -538), 0);
                        var y = x + new Vector3d(i2 * Math.Pow(2, -538), -i1 * Math.Pow(2, -538), 0);
                        Assert.Equal(Dot(a, x), Dot(a, y));
                        double px = (a.x * x.x + a.y * x.y) + a.z * x.z, py = (a.x * y.x + a.y * y.y) + a.z * y.z;
                        if (px == py)
                            continue;
                        wrong++;
                        Assert.False(TriangleSeparatingAxes.ApartAlong(DataOfAxis(a, new Triangle3d(x, x, x)), 0, 0, y, y, y), $"axis {i1}, {i2}, point {j}: {px:R} against {py:R}");
                    }
            Assert.True(wrong >= 50, $"{wrong} tied pairs with different computed projections");
        }

        [Fact]
        public void NotFiniteOrOverflowing_NeverApart()
        {
            var t = new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));
            var far = new Triangle3d(new Vector3d(0, 0, 5), new Vector3d(1, 0, 5), new Vector3d(0, 1, 5));
            Assert.True(Apart(t, far));
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
                for (int corner = 0; corner < 3; ++corner)
                    for (int c = 0; c < 3; ++c) {
                        Triangle3d u = far;
                        Vector3d v = u[corner]; v[c] = bad; u[corner] = v;
                        Assert.False(Apart(t, u), $"{bad} in corner {corner}");
                        Assert.False(Apart(u, t), $"{bad} in corner {corner}");
                    }
                // an axis that is not finite
                double[] d = DataOfAxis(new Vector3d(bad, 1, 1), t);
                Assert.False(TriangleSeparatingAxes.ApartAlong(d, 0, 0, far.V0, far.V1, far.V2));
            }
            // coordinates whose axes or products overflow: not apart; at the top of the rounding's range (2^200) still apart
            double big = 1e300;
            var tb = new Triangle3d(big * t.V0, big * t.V1, big * t.V2); var fb = new Triangle3d(big * far.V0, big * far.V1, big * far.V2);
            Assert.False(Apart(tb, fb));
            double top = Math.Pow(2, 200);
            Assert.True(Apart(new Triangle3d(top * t.V0, top * t.V1, top * t.V2), new Triangle3d(top * far.V0, top * far.V1, top * far.V2)));
            // a degenerate triangle has zero axes: never apart by its own, and the pair only by the other's
            var point = new Triangle3d(far.V0, far.V0, far.V0);
            Assert.False(TriangleSeparatingAxes.Apart(Data(point), 0, t.V0, t.V1, t.V2));
            Assert.True(TriangleSeparatingAxes.Apart(Data(t), 0, point.V0, point.V1, point.V2));
        }

        // ---- invariance ----

        [Fact]
        public void PowerOfTwoScales_SameAnswers_FromBothEndsOfTheRange()
        {
            var r = new Random(5);
            var pairs = new List<(Triangle3d, Triangle3d)>();
            for (int i = 0; i < 400; ++i) {
                Triangle3d t = Needle(r, Random(r, 0.1), 2, 1e-3);
                Vector3d shift = Random(r, 1e-15 * Math.Pow(10, r.Next(12)));
                pairs.Add((t, i % 2 == 0 ? Needle(r, Random(r, 0.1), 2, 1e-3) : new Triangle3d(t.V0 + shift, t.V1 + shift, t.V2 + shift)));
            }
            bool[] reference = pairs.Select(p => Apart(p.Item1, p.Item2)).ToArray();
            Assert.Contains(true, reference);
            Assert.Contains(false, reference);
            // (the axes scale by 2^2k and 2^3k and the projections by 2^3k and 2^4k, exactly: within [2^-200, 2^200] nothing
            // under- or overflows, and Tiny is below half a unit in the last place of every margin that is not zero)
            foreach (int k in new[] { -200, -150, -60, -1, 1, 37, 150, 199 }) {
                double f = Math.Pow(2, k);
                for (int i = 0; i < pairs.Count; ++i) {
                    Triangle3d t = pairs[i].Item1, u = pairs[i].Item2;
                    var ts = new Triangle3d(f * t.V0, f * t.V1, f * t.V2); var us = new Triangle3d(f * u.V0, f * u.V1, f * u.V2);
                    Assert.True(reference[i] == Apart(ts, us), $"pair {i} at 2^{k}");
                    // (the normal scales by 2^2k, the edge normals by 2^3k)
                    double[] d0 = Data(t), ds = Data(ts);
                    for (int c = 0; c < 12; ++c)
                        Assert.Equal(c < 3 ? d0[c] * f * f : d0[c] * f * f * f, ds[c]);
                }
            }
        }

        [Fact]
        public void Compute_AxesAreTheNormalAndTheEdgeNormals_IntervalsContainTheCorners()
        {
            var r = new Random(9);
            for (int i = 0; i < 2000; ++i) {
                var t = new Triangle3d(Random(r, 1), Random(r, 1), Random(r, 1));
                double[] d = Data(t);
                Vector3d n = Axis(d, 0, 0);
                Vector3d expected = (t.V1 - t.V0).Cross(t.V2 - t.V0);
                Assert.True((n - expected).Length <= 1e-14 * expected.Length, "the normal");
                Vector3d[] edges = { t.V1 - t.V0, t.V2 - t.V1, t.V0 - t.V2 };
                for (int k = 1; k < 4; ++k) {
                    Vector3d a = Axis(d, 0, k);
                    // (in the plane and perpendicular to edge k - 1, pointing to the side of the opposite corner)
                    Assert.True(Math.Abs(a.Dot(n)) <= 1e-12 * a.Length * n.Length && Math.Abs(a.Dot(edges[k - 1])) <= 1e-12 * a.Length * edges[k - 1].Length, $"axis {k}");
                    Assert.True(a.Dot(t[(k + 1) % 3]) > a.Dot(t[k - 1]), $"axis {k} points inwards");
                }
                for (int k = 0; k < 4; ++k) {
                    Vector3d a = Axis(d, 0, k);
                    BigInteger lo = Dot(new Vector3d(d[12 + 2 * k], 0, 0), new Vector3d(1, 0, 0)), hi = Dot(new Vector3d(d[13 + 2 * k], 0, 0), new Vector3d(1, 0, 0));
                    for (int c = 0; c < 3; ++c)
                        Assert.True(lo < Dot(a, t[c]) && Dot(a, t[c]) < hi, $"corner {c} outside the interval of axis {k}");
                }
            }
        }
    }
}
