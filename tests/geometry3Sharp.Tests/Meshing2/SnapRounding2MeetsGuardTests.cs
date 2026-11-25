using System;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Direct unit tests for the 2^29 signed-64-bit overflow guard in SnapRounding2.MeetsPixelCentreInterior (#1402).
    /// Kept in its own file so SnapRounding2IndexTests stays compilable against base master for CI's new-tests-on-base check.
    /// </summary>
    public class SnapRounding2MeetsGuardTests
    {
        static bool MeetsPixelCentreInteriorExact((long, long) p, (long, long) q, (long, long) k)
        {
            long px = p.Item1, py = p.Item2, qx = q.Item1, qy = q.Item2, kx = k.Item1, ky = k.Item2;
            long lx = px < qx ? px : qx, hx = px > qx ? px : qx;
            long ly = py < qy ? py : qy, hy = py > qy ? py : qy;
            if (hx < kx || lx > kx || hy < ky || ly > ky) return false;
            if (px == qx && py == qy) return px == kx && py == ky;
            BigInteger dx = (BigInteger)(qx - px) * 2, dy = (BigInteger)(qy - py) * 2;
            BigInteger ux0 = (BigInteger)(kx - px) * 2 - 1, ux1 = ux0 + 2;
            BigInteger uy0 = (BigInteger)(ky - py) * 2 - 1, uy1 = uy0 + 2;
            int pos = 0, neg = 0;
            void Side(BigInteger o) { if (o.Sign > 0) ++pos; else if (o.Sign < 0) ++neg; }
            Side(dx * uy0 - dy * ux0);
            Side(dx * uy0 - dy * ux1);
            Side(dx * uy1 - dy * ux1);
            Side(dx * uy1 - dy * ux0);
            return pos > 0 && neg > 0;
        }

        [Fact]
        public void MeetsPixelCentreInterior_2To29Guard_MatchesExactBigIntegerAcrossOverflowBoundary()
        {
            // At M = 1_518_500_250 (> 2^29, < 2^31), 4*M^2 = 9,223,372,038,156,250,000 exceeds long.MaxValue
            // while 4*M^2 - 4*M = 9,223,372,032,082,249,000 stays below long.MaxValue. In unchecked 64-bit long
            // arithmetic, three corner orientations wrap negative while one stays positive, flipping false -> true.
            const long M = 1_518_500_250L;
            var overflowCases = new ((long, long) p, (long, long) q, (long, long) k, bool expected)[] {
                ((0, 0), (M, M), (0, M), false),
                ((0, 0), (M, M), (M, 0), false),
                ((M, M), (0, 0), (0, M), false),
                ((0, 0), (-M, M), (0, M), false),
                ((0, 0), (M, -M), (M, 0), false),
                ((0, 0), (-M, -M), (0, -M), false),
                ((0, 0), (M, M), (M / 2, M / 2), true),
                ((0, 0), (M, M), (M / 2, M / 2 + 1), false),
            };
            foreach (var (p, q, k, expected) in overflowCases) {
                Assert.Equal(expected, MeetsPixelCentreInteriorExact(p, q, k));
                Assert.Equal(expected, SnapRounding2.MeetsPixelCentreInterior(p, q, k));
            }

            // Sweep across SafeSpan = 2^29 boundary and larger spans in both axes.
            long[] spans = {
                (1L << 29) - 1,
                1L << 29,
                (1L << 29) + 1,
                M,
                1L << 31,
                1L << 32,
                1L << 40
            };
            foreach (long s in spans) {
                var probes = new ((long, long) p, (long, long) q, (long, long) k)[] {
                    ((0, 0), (s, s), (0, s)),
                    ((0, 0), (s, s), (s, 0)),
                    ((0, 0), (s, s), (s / 2, s / 2)),
                    ((0, 0), (s, s + 1), (s / 2, s / 2)),
                    ((0, 0), (s, s + 1), (s / 2, s / 2 + 2)),
                    ((-s / 2, -s / 2), (s / 2, s / 2), (-s / 2, s / 2)),
                };
                foreach (var (p, q, k) in probes) {
                    bool exact = MeetsPixelCentreInteriorExact(p, q, k);
                    Assert.Equal(exact, SnapRounding2.MeetsPixelCentreInterior(p, q, k));
                }
            }
        }
    }
}
