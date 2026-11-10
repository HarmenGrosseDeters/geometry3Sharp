using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshBoolean.portable_atan2, the atan2 of the Antipodal boundary sum: within its error bound of Math.Atan2,
    /// and the same bits on every runtime (Math.Atan2 differs by an ulp between net10.0 and net48 on some arguments; the
    /// pinned hash below is checked by both test runs of CI, linux net10.0 and windows net48).
    /// </summary>
    public class PortableAtan2Tests
    {
        // SplitMix64: the same inputs on every runtime (doubles built from integers, no library random generator)
        static ulong state;
        static ulong Next()
        {
            ulong z = unchecked(state += 0x9E3779B97F4A7C15UL);
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }
        // a double in [-1, 1) times 2^e, e in -40..40 (exact: a 53-bit integer over 2^53, scaled by a power of two)
        static double Arg()
        {
            double m = (long)(Next() >> 11) / 9007199254740992.0 * 2 - 1;
            int e = (int)(Next() % 81) - 40;
            return m * Math.Pow(2, e);
        }

        static double[] Special()
        {
            return new[] { 0.0, 1, -1, 1e-300, -1e-300, 1e300, -1e300, 0.5, -0.5, 2, -2, Math.Sqrt(3), 1 / Math.Sqrt(3),
                Math.Sqrt(2) - 1, Math.Sqrt(2) + 1, 1 - 1e-16, 1 + 2.3e-16, 1e-8, 3.7e-5, 123456.789 };
        }

        [Fact]
        public void WithinTheErrorBoundOfMathAtan2()
        {
            state = 1;
            double worst = 0;
            for (int k = 0; k < 200000; ++k) {
                double y = Arg(), x = Arg();
                worst = Math.Max(worst, Math.Abs(ExactMeshBoolean.portable_atan2(y, x) - Math.Atan2(y, x)));
            }
            foreach (double y in Special())
                foreach (double x in Special()) {
                    if (x == 0 && y == 0) continue;
                    foreach (int sy in new[] { 1, -1 })
                        foreach (int sx in new[] { 1, -1 })
                            worst = Math.Max(worst, Math.Abs(ExactMeshBoolean.portable_atan2(sy * y, sx * x) - Math.Atan2(sy * y, sx * x)));
                }
            // (Math.Atan2 itself is within an ulp or so: the bound covers both with a margin)
            Assert.True(worst < ExactMeshBoolean.PortableAtan2Error / 4, $"worst {worst:R}");
            Assert.Equal(0.0, ExactMeshBoolean.portable_atan2(0, 0));
            Assert.Equal(0.0, ExactMeshBoolean.portable_atan2(0, 5));
        }

        [Fact]
        public void SameBitsOnEveryRuntime()
        {
            state = 2;
            ulong h = 1469598103934665603UL;
            for (int k = 0; k < 100000; ++k) {
                double y = Arg(), x = Arg();
                ulong bits = (ulong)BitConverter.DoubleToInt64Bits(ExactMeshBoolean.portable_atan2(y, x));
                h = unchecked((h ^ bits) * 1099511628211UL);
            }
            foreach (double y in Special())
                foreach (double x in Special()) {
                    ulong bits = (ulong)BitConverter.DoubleToInt64Bits(ExactMeshBoolean.portable_atan2(y, -x));
                    h = unchecked((h ^ bits) * 1099511628211UL);
                }
            Assert.Equal(PinnedHash, h);
        }

        const ulong PinnedHash = 13027935797655104570UL;
    }
}
