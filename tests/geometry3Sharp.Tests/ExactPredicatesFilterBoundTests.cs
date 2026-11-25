using System;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The size of Orient3dBound (math/ExactPredicates.cs), the stage A constant of Orient3d, checked on an input whose
    /// rounding error is large, through the library's own evaluation (VolumeSumWithBound evaluates the determinant as
    /// Orient3dFilter does and returns its bound). Sign tests cannot see a bound that is too small (Shewchuk's bound is
    /// several times the typical error): the hand mutant H5 of #1572, 7 u to 4 u, survived all 522 predicate tests
    /// (docs/qa/exact-bound-inventory.md, gap G6). The proof of the constant is EP-2 of tools/ErrorBoundAudit and
    /// no Z3 lemma (docs/qa/exact-bound-inventory.md section 7); this test is the witness that a smaller constant is wrong.
    /// It reaches Orient3dFilter through the shared constant only: a change of the bound expression inside Orient3dFilter
    /// alone is the ErrorBoundAudit anchors' to catch.
    /// </summary>
    public class ExactPredicatesFilterBoundTests
    {
        // found by a local search (hill climbing on the error / (permanent + |det|) over the ulps of the twelve
        // coordinates): the error of the evaluated determinant is 4.30 u (permanent + |det|) and 4.69 u permanent.
        // The coordinates are the shortest round-trip decimals of the doubles.
        static readonly double[] Coordinates = {
            0.3640557419018742, -1.4664898648868974, 0.6463590366829446,      // o
            -1.8905698592157203, 0.6400851890221262, 0.6474126792005838,      // a
            4.376956089525014, 0.13574913818499035, 0.17814099369685746,      // b
            -0.7130060362935128, 7.8161039759070094, -0.23345173296915972,    // c
        };

        // x * 2^shift as an integer; asserts that it is exact (the coordinates are multiples of 2^-55, sum and bound of 2^-180)
        static BigInteger Scaled(double x, int shift)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else man |= 1L << 52;
            int e = exp - 1075 + shift;
            Assert.True(e >= 0 || (e > -64 && (man & ((1L << -e) - 1)) == 0), "not exact at this shift");
            BigInteger m = man;
            BigInteger r = e >= 0 ? m << e : m >> -e;
            return bits < 0 ? -r : r;
        }

        [Fact]
        public void Orient3dBound_CoversAnErrorOfMoreThanFourUlpsOfThePermanent()
        {
            const int S = 60;     // every coordinate is a multiple of 2^-55
            var c = new BigInteger[12];
            for (int k = 0; k < 12; ++k) c[k] = Scaled(Coordinates[k], S);
            // det((a - o), (b - o), (c - o)) = w . (u x v) exactly, times 2^(3 S)
            BigInteger ux = c[3] - c[0], uy = c[4] - c[1], uz = c[5] - c[2];
            BigInteger vx = c[6] - c[0], vy = c[7] - c[1], vz = c[8] - c[2];
            BigInteger wx = c[9] - c[0], wy = c[10] - c[1], wz = c[11] - c[2];
            BigInteger exact = wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx);

            // triangle 0 (o, o, o) contributes exactly 0; triangle 1 is (a, b, c) about o
            int[] corners = { 0, 0, 0, 1, 2, 3 };
            Assert.True(ExactPredicates.VolumeSumWithBound(Coordinates, corners, out double sum, out double bound));
            BigInteger err = BigInteger.Abs(Scaled(sum, 3 * S) - exact);
            BigInteger b = Scaled(bound, 3 * S);
            Assert.True(err <= b, $"|S_fp - S| = {(double)err / Math.Pow(2, 3 * S)} exceeds the bound {bound}");

            // the input is a witness against a smaller constant: the error is 4.30 u (permanent + |det|), so with
            // Orient3dBound = 4 u (the bound is then about 4 u (permanent + |det|), with the recursive-sum term g |det|,
            // g ~ 4 u) the assertion above fails; checked by hand with 4 u and 4.25 u
            Assert.True(sum > 1.9 && sum < 1.92);
        }
    }
}
