using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SnapGrid's double filters against their exact tests. Pixel against the exact quotient (PixelExact): the same index for
    /// every x, on pixel boundaries (x a half-integer multiple of w, and its neighbouring doubles), at random, for decimal and binary widths, near 2^50 pixels and
    /// beyond, and for zero, subnormal and huge values. CrossesInterior's filter against CrossesInteriorExact: where the filter
    /// decides it agrees, on segments through pixel corners, along pixel sides, ending on them, and at random, in frames 2^k
    /// </summary>
    public class SnapGridPixelFilterTests
    {
        static double Up(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + (x >= 0 ? 1 : -1));
        static double Down(double x) => x == 0 ? -double.Epsilon : BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) - (x > 0 ? 1 : -1));

        [Fact]
        public void Filter_AgreesWithTheExactQuotient()
        {
            var rnd = new Random(20261104);
            // (negative, zero-like and extreme widths too: the filter steps aside where w &lt;= 0, and x / w may underflow or overflow)
            double[] widths = { 0.01, 0.25, 0.3, 1.0 / 3, 1, 1e-3, 7.5, 1e-9, 0.1 * 3, 2.5e6, -0.3, -1, 5e-324, 1e-310, 1e300 };
            int checkedValues = 0, onBoundary = 0;
            foreach (double w in widths) {
                var grid = new SnapGrid(w);
                void Check(double x)
                {
                    if (double.IsNaN(x) || double.IsInfinity(x)) return;
                    ++checkedValues;
                    long exact;
                    try { exact = grid.PixelExact(x); } catch (OverflowException) { return; }   // (beyond a long: as before)
                    Assert.True(grid.Pixel(x) == exact, "w " + w.ToString("R") + ", x " + x.ToString("R"));
                }
                Check(0); Check(-0.0); Check(double.Epsilon); Check(-double.Epsilon); Check(1e-300); Check(-1e-300); Check(1e300); Check(-1e300);
                // r = -2^52 + 1/2 and its neighbours (f = -2^52: f - 1/2 is a tie)
                foreach (double t in new[] { -4503599627370495.5, -4503599627370496.0, -4503599627370494.5, 4503599627370495.5 }) { Check(t * w); Check(Up(t * w)); Check(Down(t * w)); }
                for (int n = 0; n < 4000; ++n) {
                    // around a pixel boundary (k + 1/2) w, as close as the doubles go, at magnitudes up to and past 2^50 pixels
                    long k = (long)((rnd.NextDouble() - 0.5) * Math.Pow(2, rnd.Next(0, 53)));
                    double b = (k + 0.5) * w;
                    if ((b / w) == k + 0.5) ++onBoundary;
                    Check(b); Check(Up(b)); Check(Down(b)); Check(Up(Up(b))); Check(Down(Down(b)));
                    Check(k * w);
                    // anywhere
                    Check((rnd.NextDouble() - 0.5) * w * Math.Pow(10, rnd.Next(0, 16)));
                }
            }
            Assert.True(checkedValues > 200000 && onBoundary > 1000, checkedValues + " values, " + onBoundary + " on a boundary in doubles");
        }

        [Fact]
        public void CrossesInteriorFilter_AgreesWithTheExactTest()
        {
            var rnd = new Random(7);
            double[] widths = { 0.01, 0.25, 0.3, 1.0 / 3, 1, 7.5, 1e-6 };
            int[] frames = { -520, -470, -200, -30, 0, 30, 200, 440 };
            int decided = 0, undecided = 0, crossing = 0;
            foreach (double w0 in widths)
                foreach (int k in frames) {
                    double f = Math.Pow(2, k), w = w0 * f;
                    var grid = new SnapGrid(w);
                    for (int n = 0; n < 3000; ++n) {
                        long kx = rnd.Next(-50, 50) + (rnd.Next(4) == 0 ? (long)1 << rnd.Next(20, 50) : 0), ky = rnd.Next(-50, 50);
                        // pixel indices from 2^52 up, where k -+ 1/2 is no double (the filter leaves them to the exact test)
                        if (rnd.Next(10) == 0) kx = ((long)1 << rnd.Next(52, 58)) + rnd.Next(0, 4);
                        double cx = kx * w, cy = ky * w, h = w / 2;
                        // endpoints: on corners, on sides, near them by a few ulps, or anywhere nearby
                        Vector2d Pick()
                        {
                            switch (rnd.Next(5)) {
                                case 0: return new Vector2d(cx + (rnd.Next(2) * 2 - 1) * h, cy + (rnd.Next(2) * 2 - 1) * h);
                                case 1: return new Vector2d(cx + (rnd.Next(2) * 2 - 1) * h, cy + (rnd.NextDouble() - 0.5) * 3 * w);
                                case 2: { var c = new Vector2d(cx + h, cy - h); return new Vector2d(Up(c.x), Down(c.y)); }
                                case 3: return new Vector2d(cx + (rnd.NextDouble() - 0.5) * 4 * w, cy + h);
                                default: return new Vector2d(cx + (rnd.NextDouble() - 0.5) * 4 * w, cy + (rnd.NextDouble() - 0.5) * 4 * w);
                            }
                        }
                        var a = Pick(); var b = rnd.Next(8) == 0 ? a : Pick();
                        bool exact = grid.CrossesInteriorExact(a, b, kx, ky);
                        int filter = grid.CrossesInteriorFilter(a, b, kx, ky);
                        if (filter == 0) ++undecided;
                        else {
                            ++decided;
                            Assert.True((filter > 0) == exact, "w " + w.ToString("R") + ", pixel " + kx + " " + ky + ", a " + a.x.ToString("R") + " " + a.y.ToString("R") + ", b " + b.x.ToString("R") + " " + b.y.ToString("R"));
                        }
                        if (exact) ++crossing;
                        Assert.Equal(exact, grid.CrossesInterior(a, b, kx, ky));
                    }
                }
            // at the guards' edges (magnitudes around 1e-140 and 1e140), mixed exponents (a tiny, b huge), the extreme pixel index and
            // widths the filter leaves alone (w <= 0)
            foreach (double w in new[] { 1e-141, 3e-140, 1e139, 7e139, 0.3, -0.25 }) {
                var grid = new SnapGrid(w);
                foreach (long kx in new[] { 0L, 1L, -3L, long.MinValue, long.MaxValue })
                    foreach (var (a, b) in new[] {
                        (new Vector2d(-w, 0), new Vector2d(w, 0)), (new Vector2d(1e-150, 1e-150), new Vector2d(1e150, w)),
                        (new Vector2d(kx * w - w, -w), new Vector2d(kx * w + w, w)), (new Vector2d(0.4 * w, 0.4 * w), new Vector2d(0.4 * w, 0.4 * w)),
                        (new Vector2d(-1e-300, 2e-300), new Vector2d(3 * w, -2 * w)) }) {
                        bool exact = grid.CrossesInteriorExact(a, b, kx, 0);
                        int filter = grid.CrossesInteriorFilter(a, b, kx, 0);
                        Assert.True(filter == 0 || (filter > 0) == exact, "w " + w.ToString("R") + ", pixel " + kx + ", a " + a + ", b " + b);
                        Assert.Equal(exact, grid.CrossesInterior(a, b, kx, 0));
                    }
            }
            // the filter decides many cases, both ways, and leaves the touching ones (most of these picks, by design) to the exact test
            Assert.True(decided > 20000 && crossing > 10000 && undecided > 1000, decided + " decided, " + undecided + " undecided, " + crossing + " crossing");
        }
    }
}
