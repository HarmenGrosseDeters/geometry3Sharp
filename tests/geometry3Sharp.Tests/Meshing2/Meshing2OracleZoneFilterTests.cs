using System;
using System.Numerics;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The oracle's double filter of "within the merge zone" (Oracle.ZoneFilter, used by InMergeZone for the distance to a source and to
    /// the apex) and the radius bounds it is given (Oracle.RadiusBounds), against the exact answer: wherever the filter decides, either
    /// way, it agrees with the exact distance, on points placed at the radius (relative offsets 1e-2 to 1e-15 of either sign), on
    /// segments and on single points, in frames 2^k from 2^-300 to 2^400 (scaling by a power of two keeps every double and the exact
    /// answer). The fast path's verdicts only stay those of the reference path if every decision is right.
    /// </summary>
    public class Meshing2OracleZoneFilterTests
    {
        // (2^-460 and 2^460 reach the ends of the filter's range [1e-140, 1e140] and beyond: there it may only decide rightly or not at all)
        static readonly int[] Frames = { -460, -440, -300, -150, -60, 0, 60, 150, 300, 400, 440, 460 };

        static bool Same(double v, double scaled, double f) => !double.IsInfinity(scaled) && (v == 0 || Math.Abs(scaled) >= 2.2250738585072014E-308) && scaled / f == v;

        /// <summary>Z3Pilot proves that the final +1e-300 in DistanceQueryBox
        /// changes no binary64 result: Q.Bounds already pads d2hi by 1e-300.
        /// Replay the equivalent survivor from #376 through the native code. The points
        /// include the origin and tiny coordinates: at UTM magnitudes (ulp ~1e-10) a pad
        /// changed to 1e-20 or 1e-12 would be absorbed too and the test would not see it;
        /// near zero, with d2 = 0 (reach ~1e-150), it changes the box bits.</summary>
        [Fact]
        public void SmtEquivalentSurvivor_FinalAbsoluteReachPadChangesNoBits()
        {
            var points = new[] {
                Pt.FromDoubles(632117.25, 5712043.5), Pt.FromDoubles(0, 0),
                Pt.FromDoubles(1e-30, -3e-40), Pt.FromDoubles(-0.375, 0.25) };
            foreach (int exponent in new[] { -1000, -500, -100, 0, 500, 1000 }) {
                Q distance2 = Q.FromDouble(Math.Pow(2, exponent));
                foreach (Q d2 in new[] { Q.Zero, distance2, distance2 / new Q(3, 1) })
                foreach (Pt p in points) {
                    Assert.True(Oracle.DistanceQueryBox(p, d2, out double xl, out double xh, out double yl, out double yh));
                    Assert.True(d2.Bounds(out _, out double hi));
                    Assert.True(p.Bounds(false, out double pxl, out double pxh));
                    Assert.True(p.Bounds(true, out double pyl, out double pyh));
                    // The actual survivor: omit only the final +1e-300.
                    double reach = Math.Sqrt(Math.Max(0, hi)) * (1 + 1e-12);
                    Assert.Equal(BitConverter.DoubleToInt64Bits(pxl-reach), BitConverter.DoubleToInt64Bits(xl));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(pxh+reach), BitConverter.DoubleToInt64Bits(xh));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(pyl-reach), BitConverter.DoubleToInt64Bits(yl));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(pyh+reach), BitConverter.DoubleToInt64Bits(yh));
                }
            }
        }

        [Fact]
        public void NearTheRadius_AgreesWithTheExactDistance_BothWays_InEveryFrame()
        {
            var rnd = new Random(20261104);
            int within = 0, farther = 0, cases = 0;
            for (int n = 0; n < 4000; ++n) {
                double mag = Math.Pow(10, rnd.Next(-3, 8)), ox = (rnd.NextDouble() - 0.5) * mag * 10, oy = (rnd.NextDouble() - 0.5) * mag * 10;
                double ax = ox + (rnd.NextDouble() - 0.5) * mag, ay = oy + (rnd.NextDouble() - 0.5) * mag;
                double bx = ax, by = ay;
                bool segment = rnd.Next(3) != 0;
                if (segment) { bx = ox + (rnd.NextDouble() - 0.5) * mag; by = oy + (rnd.NextDouble() - 0.5) * mag; }
                double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                // the radius: 1e-8 to 1 of the magnitude (the zone is the tube over a small sine: larger than the tube)
                double radius = mag * Math.Pow(10, -rnd.Next(0, 9)) * (0.5 + rnd.NextDouble());
                // the point: off the segment (or the point) by the radius times 1 + rel, rel of either sign, 1e-2 to 1e-15
                double rel = (rnd.Next(2) == 0 ? -1 : 1) * Math.Pow(10, -rnd.Next(2, 16));
                double off = radius * (1 + rel);
                double nx, ny, t;
                if (segment && len > 0) { nx = -(by - ay) / len; ny = (bx - ax) / len; t = rnd.NextDouble(); }
                else { double angle = rnd.NextDouble() * 2 * Math.PI; nx = Math.Cos(angle); ny = Math.Sin(angle); t = 0; }
                double px = ax + t * (bx - ax) + off * nx, py = ay + t * (by - ay) + off * ny;
                // the radius as the zone gives it, squared exactly from a double
                Q r2 = Q.FromDouble(radius) * Q.FromDouble(radius);
                bool exact = Exact.Dist2ToSegment(Pt.FromDoubles(ax, ay), Pt.FromDoubles(bx, by), Pt.FromDoubles(px, py)) <= r2;
                ++cases;
                foreach (int k in Frames) {
                    double f = Math.Pow(2, k);
                    double sax = ax * f, say = ay * f, sbx = bx * f, sby = by * f, spx = px * f, spy = py * f;
                    if (!Same(ax, sax, f) || !Same(ay, say, f) || !Same(bx, sbx, f) || !Same(by, sby, f) || !Same(px, spx, f) || !Same(py, spy, f)) continue;
                    Q s = Q.FromDouble(f);
                    Oracle.RadiusBounds(r2 * s * s, out double lo, out double hi);
                    int got = Oracle.ZoneFilter(sax, say, sbx, sby, spx, spy, lo, hi);
                    if (got == 0) continue;
                    if (got > 0) ++within; else ++farther;
                    Assert.True((got > 0) == exact, "case " + n + " at 2^" + k + ": filter " + got + ", exact " + (exact ? "within" : "farther")
                        + " (a " + ax + " " + ay + ", b " + bx + " " + by + ", p " + px + " " + py + ", radius " + radius + ", rel " + rel + ")");
                }
            }
            // the filter is useful both ways (most of the Herne tiles' zone tests are "within")
            Assert.True(within > cases && farther > cases, "decided " + within + " within and " + farther + " farther over " + cases + " cases");
        }

        /// <summary> the call sites' decision: the filter's answer where it decides, the exact test only where it does not (an undecided
        /// filter taken as either answer would differ from the exact test in the band of the slack) </summary>
        [Fact]
        public void WithinZone_TakesTheExactTestOnlyWhenUndecided()
        {
            int calls = 0;
            bool Exact(bool answer) { ++calls; return answer; }
            Assert.True(Oracle.WithinZone(1, () => Exact(false)));
            Assert.False(Oracle.WithinZone(-1, () => Exact(true)));
            Assert.Equal(0, calls);
            Assert.False(Oracle.WithinZone(0, () => Exact(false)));
            Assert.True(Oracle.WithinZone(0, () => Exact(true)));
            Assert.Equal(2, calls);
        }

        /// <summary>
        /// The query box of the domain-edge index (Oracle.DistanceQueryBox): a segment exactly sqrt(d2) from p, d2 its exact squared
        /// distance, has a box (Oracle.SegmentBox, as the grid stores it) that meets the query box, in the frames 2^-500 to 2^500. Where
        /// the squares do not fit doubles (the far frames) DistanceQueryBox refuses and the caller scans every edge. A query box narrower
        /// than the widening by sqrt(d2) misses the segment at the edge of the distance, the case the exact test still accepts
        /// (d &lt;= d2): the widening from d2's lower bound with a factor below 1 fails here. Each margin alone (the factor 1 + 1e-12, the
        /// 1e-300, d2's upper bound) is one of several layers with Bounds' own pads, so dropping a single one changes nothing
        /// </summary>
        [Fact]
        public void DistanceQueryBox_MeetsEverySegmentAtExactlyTheDistance()
        {
            var rnd = new Random(500);
            int[] frames = { -500, -480, -300, 0, 300, 480, 500 };
            int checkedFrames = 0;
            for (int n = 0; n < 3000; ++n) {
                double mag = Math.Pow(10, rnd.Next(-3, 6));
                double ax = (rnd.NextDouble() - 0.5) * mag, ay = (rnd.NextDouble() - 0.5) * mag;
                double bx = ax, by = ay;
                int kind = rnd.Next(3);   // 0 a segment, 1 a horizontal one, 2 a point
                if (kind != 2) { bx = (rnd.NextDouble() - 0.5) * mag; by = kind == 1 ? ay : (rnd.NextDouble() - 0.5) * mag; }
                double px = (rnd.NextDouble() - 0.5) * mag, py = (rnd.NextDouble() - 0.5) * mag;
                foreach (int k in frames) {
                    double f = Math.Pow(2, k);
                    double sax = ax * f, say = ay * f, sbx = bx * f, sby = by * f, spx = px * f, spy = py * f;
                    if (!Same(ax, sax, f) || !Same(ay, say, f) || !Same(bx, sbx, f) || !Same(by, sby, f) || !Same(px, spx, f) || !Same(py, spy, f)) continue;
                    Pt a = Pt.FromDoubles(sax, say), b = Pt.FromDoubles(sbx, sby), p = Pt.FromDoubles(spx, spy);
                    Q d2 = Exact.Dist2ToSegment(a, b, p);
                    if (!Oracle.DistanceQueryBox(p, d2, out double xl, out double xh, out double yl, out double yh)) continue;
                    var box = Oracle.SegmentBox(a, b);
                    if (double.IsNaN(box[0])) continue;
                    ++checkedFrames;
                    Assert.False(box[1] < xl || box[0] > xh || box[3] < yl || box[2] > yh,
                        "case " + n + " at 2^" + k + ": the segment's box misses the query box (a " + sax + " " + say + ", b " + sbx + " " + sby + ", p " + spx + " " + spy + ")");
                }
            }
            Assert.True(checkedFrames > 10000, "only " + checkedFrames + " frames checked");
        }

        [Fact]
        public void RadiusBounds_EncloseTheExactRadius()
        {
            var rnd = new Random(7);
            for (int n = 0; n < 2000; ++n) {
                // a ratio of large integers, as the zone's (tube^2 Z^2 / sin^2) is
                var num = new BigInteger(rnd.NextDouble() * 1e15) * BigInteger.Pow(2, rnd.Next(0, 200)) + 1;
                var den = new BigInteger(rnd.NextDouble() * 1e15) * BigInteger.Pow(2, rnd.Next(0, 200)) + 1;
                var r2 = new Q(num, den);
                Oracle.RadiusBounds(r2, out double lo, out double hi);
                Assert.True(lo > 0 && hi < double.PositiveInfinity, "unknown bounds for " + r2);
                Assert.True(Q.FromDouble(lo) * Q.FromDouble(lo) <= r2, "lo^2 above r2 for " + r2);
                Assert.True(Q.FromDouble(hi) * Q.FromDouble(hi) >= r2, "hi^2 below r2 for " + r2);
                // and tight: within 1e-11 relative (the filter decides near the radius)
                Assert.True(hi / lo < 1 + 1e-11, "bounds too wide: " + lo + " " + hi);
            }
            Oracle.RadiusBounds(Q.Zero, out double zl, out double zh);
            Assert.Equal(0, zl);
            Assert.True(zh >= 0 && zh < 1e-149);   // (Bounds pads by 1e-300 absolute: the radius bound of 0 is about 1e-150)
        }
    }
}
