using System;
using System.Numerics;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The oracle's double filter of "vertex within the tube of a source" (Oracle.TubeFilter, used by InventedVertices and Marks on the
    /// fast path) against the exact distance: wherever it decides, it agrees with Exact.Dist2ToSegment, on points placed at the tube's
    /// boundary (relative offsets 1e-3 to 1e-12), near and beyond the source's ends, exactly on axis-parallel lines, and in the frames
    /// 2^k from 2^-300 to 2^512 (scaling by a power of two keeps every double and the exact answer).
    /// </summary>
    public class Meshing2OracleTubeFilterTests
    {
        static readonly int[] Frames = { -300, -200, -150, -60, 0, 60, 150, 200, 300, 512 };

        [Fact]
        public void NearTheBoundary_AgreesWithTheExactDistance_InEveryFrame()
        {
            var rnd = new Random(20261103);
            int decided = 0, decidedNormal = 0, normalCases = 0;
            for (int n = 0; n < 3000; ++n) {
                // a source: diagonal, axis-parallel or short, at magnitudes 1e-3 to 1e7 around an offset origin
                double mag = Math.Pow(10, rnd.Next(-3, 8)), ox = (rnd.NextDouble() - 0.5) * mag * 10, oy = (rnd.NextDouble() - 0.5) * mag * 10;
                double ax = ox + (rnd.NextDouble() - 0.5) * mag, ay = oy + (rnd.NextDouble() - 0.5) * mag;
                double bx = ox + (rnd.NextDouble() - 0.5) * mag, by = oy + (rnd.NextDouble() - 0.5) * mag;
                int kind = rnd.Next(4);
                if (kind == 1) by = ay;            // horizontal
                else if (kind == 2) bx = ax;       // vertical
                double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                if (!(len > 0)) continue;
                // the tube: 1e-14 to 1e-2 of the source's length
                double tube = len * Math.Pow(10, -rnd.Next(2, 15));
                // the point: along the source (beyond its ends too), off it by the tube times 1 + rel, rel of either sign and
                // 1e-3 to 1e-12; or exactly on the line
                double t = rnd.Next(5) == 0 ? (rnd.Next(2) == 0 ? -1 : 1) * rnd.NextDouble() * 3 * tube / len + (rnd.Next(2) == 0 ? 0 : 1) : rnd.NextDouble() * 1.2 - 0.1;
                double rel = (rnd.Next(2) == 0 ? -1 : 1) * Math.Pow(10, -rnd.Next(3, 13));
                double off = rnd.Next(6) == 0 ? 0 : tube * (1 + rel);
                double nx = -(by - ay) / len, ny = (bx - ax) / len;
                double px = ax + t * (bx - ax) + off * nx, py = ay + t * (by - ay) + off * ny;
                if (kind == 1 && off == 0) py = ay;
                if (kind == 2 && off == 0) px = ax;
                Q tube2 = Q.FromDouble(tube) * Q.FromDouble(tube);
                bool exact = Exact.Dist2ToSegment(Pt.FromDoubles(ax, ay), Pt.FromDoubles(bx, by), Pt.FromDoubles(px, py)) <= tube2;
                ++normalCases;
                foreach (int k in Frames) {
                    double f = Math.Pow(2, k);
                    double sax = ax * f, say = ay * f, sbx = bx * f, sby = by * f, spx = px * f, spy = py * f;
                    // a frame that leaves the doubles (overflow, or a coordinate that would round as a subnormal) is no frame of the case
                    if (!Same(ax, sax, f) || !Same(ay, say, f) || !Same(bx, sbx, f) || !Same(by, sby, f) || !Same(px, spx, f) || !Same(py, spy, f)) continue;
                    Q s = Q.FromDouble(f), stube2 = tube2 * s * s;
                    double lo = 0, hi = 0;
                    int got = stube2.Bounds(out lo, out hi) ? Oracle.TubeFilter(sax, say, sbx, sby, spx, spy, lo, hi) : 0;
                    if (got == 0) continue;
                    ++decided;
                    if (k == 0) ++decidedNormal;
                    Assert.True((got > 0) == exact, "case " + n + " at 2^" + k + ": filter " + got + ", exact " + (exact ? "within" : "farther")
                        + " (a " + ax + " " + ay + ", b " + bx + " " + by + ", p " + px + " " + py + ", tube " + tube + ")");
                }
            }
            // the filter is useful: it decides most cases in the unscaled frame (at a relative offset of 1e-12 from the tube it may not)
            Assert.True(decidedNormal > normalCases * 7 / 10, "the filter decided " + decidedNormal + " of " + normalCases);
            Assert.True(decided > decidedNormal, "the scaled frames decided nothing");
        }

        static bool Same(double v, double scaled, double f) => !double.IsInfinity(scaled) && (v == 0 || Math.Abs(scaled) >= 2.2250738585072014E-308) && scaled / f == v;

        [Fact]
        public void TheExactAnswer_DoesNotDependOnTheFrame()
        {
            // the test above compares each frame with the unscaled exact answer; that answer is the frame's own (powers of two scale
            // the distance and the tube alike)
            var a = Pt.FromDoubles(1, 1); var b = Pt.FromDoubles(5, 4); var p = Pt.FromDoubles(3, 2.6);
            Q d = Exact.Dist2ToSegment(a, b, p);
            foreach (int k in Frames) {
                double f = Math.Pow(2, k);
                Q s = Q.FromDouble(f);
                Assert.Equal(d * s * s, Exact.Dist2ToSegment(Pt.FromDoubles(f, f), Pt.FromDoubles(5 * f, 4 * f), Pt.FromDoubles(3 * f, 2.6 * f)));
            }
        }
    }
}
