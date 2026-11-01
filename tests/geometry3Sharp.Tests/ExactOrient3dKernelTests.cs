using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The 64-bit integer exact stage of ExactPredicates.Orient3d (orient3d_int, docs/design/CHEAPER_CONSTRUCTIONS.md
    /// section 4.2) against the BigInteger determinant: same sign whenever it admits the input, admission exactly for
    /// coordinate differences below 2^62 units of the lowest set bit, and the public predicate unchanged.
    /// </summary>
    public class ExactOrient3dKernelTests
    {
        static int Kernel(Vector3d a, Vector3d b, Vector3d c, Vector3d d, out bool admitted)
        {
            int s;
            admitted = ExactPredicates.orient3d_int(a, b, c, d, out s);
            return s;
        }

        static void AssertAgrees(Vector3d a, Vector3d b, Vector3d c, Vector3d d, ref int admittedCount)
        {
            int expected = ExactPredicates.orient3d_bigint(a, b, c, d);
            bool admitted;
            int s = Kernel(a, b, c, d, out admitted);
            if (admitted) {
                admittedCount++;
                Assert.True(expected == s, $"kernel {s}, BigInteger {expected}: {a} {b} {c} {d}");
            }
            Assert.Equal(expected, ExactPredicates.Orient3d(a, b, c, d));
            Assert.Equal(expected, ExactPredicates.Orient3dExact(a, b, c, d));
        }

        // m 2^e with |m| < 2^bits (bits <= 53)
        static double Coordinate(Random r, int bits, int e)
        {
            long m = (long)(r.NextDouble() * Math.Pow(2, bits));
            return (r.Next(2) == 0 ? -m : m) * Math.Pow(2, e);
        }

        [Fact]
        public void RandomInputs_SameSignAsBigInteger()
        {
            var r = new Random(1);
            int admitted = 0, n = 200000;
            for (int k = 0; k < n; ++k) {
                // coordinates with mantissas of up to 53 bits at exponents spread over 0..20 above a common unit:
                // differences from a few bits up to beyond the admission limit of 2^62
                int e = r.Next(-60, 60), spread = r.Next(0, 22), bits = r.Next(1, 54);
                var v = new Vector3d[4];
                for (int i = 0; i < 4; ++i)
                    v[i] = new Vector3d(Coordinate(r, bits, e + r.Next(spread)), Coordinate(r, bits, e + r.Next(spread)), Coordinate(r, bits, e + r.Next(spread)));
                AssertAgrees(v[0], v[1], v[2], v[3], ref admitted);
            }
            Assert.True(admitted > n / 2, $"admitted {admitted} of {n}");
        }

        [Fact]
        public void CoplanarFloatInputs_Zero()
        {
            // float coordinates (as CAD meshes have them) on planes with exact float relations: the exact stages of
            // these are the 90 % with result 0 on real inputs
            var r = new Random(2);
            int admitted = 0, n = 50000;
            for (int k = 0; k < n; ++k) {
                float ox = (float)(r.NextDouble() * 200 - 100), oy = (float)(r.NextDouble() * 200 - 100), oz = (float)(r.NextDouble() * 200 - 100);
                var v = new Vector3d[4];
                for (int i = 0; i < 4; ++i) {
                    // points o + s (1, 0, 0.5) + t (0, 1, 0.25) with s, t multiples of 2^-8: exact in float and double
                    double s = r.Next(-4096, 4096) / 256.0, t = r.Next(-4096, 4096) / 256.0;
                    v[i] = new Vector3d((double)ox + s, (double)oy + t, (double)oz + 0.5 * s + 0.25 * t);
                }
                AssertAgrees(v[0], v[1], v[2], v[3], ref admitted);
                if (v[0] != v[1] && v[0] != v[2] && v[0] != v[3] && v[1] != v[2] && v[1] != v[3] && v[2] != v[3])
                    Assert.Equal(0, ExactPredicates.orient3d_bigint(v[0], v[1], v[2], v[3]));
            }
            Assert.True(admitted > n * 9 / 10, $"admitted {admitted} of {n}");
        }

        [Fact]
        public void LargestDifferences_AtTheBound()
        {
            // the unit is 1 (a coordinate 1 or 3); differences up to 2^62 - 2^9 + small are admitted, at 2^62 not.
            // The products of the determinant reach about 6 (2^62)^3, the bound of the 192-bit sum.
            double M = Math.Pow(2, 62) - Math.Pow(2, 9);        // a double: 2^9 (2^53 - 1)
            double[] values = { 0, 1, 3, M, -M, M / 2, -M / 2, Math.Pow(2, 61), -Math.Pow(2, 61) + 1, Math.Pow(2, 62), -Math.Pow(2, 62) };
            var r = new Random(3);
            int admitted = 0, rejected = 0;
            for (int k = 0; k < 200000; ++k) {
                var v = new Vector3d[4];
                for (int i = 0; i < 4; ++i)
                    v[i] = new Vector3d(values[r.Next(values.Length)], values[r.Next(values.Length)], values[r.Next(values.Length)]);
                int before = admitted;
                AssertAgrees(v[0], v[1], v[2], v[3], ref admitted);
                if (admitted == before) rejected++;
            }
            Assert.True(admitted > 10000 && rejected > 10000, $"admitted {admitted}, rejected {rejected}");
            // all twelve differences at the largest admitted magnitude, determinant far from 0
            var a = new Vector3d(0, 0, 1);
            bool adm;
            int s = Kernel(a, new Vector3d(M, -M, 1 + M), new Vector3d(-M, -M, 1 - M), new Vector3d(M, M, 1 - M), out adm);
            Assert.True(adm);
            Assert.Equal(ExactPredicates.orient3d_bigint(a, new Vector3d(M, -M, 1 + M), new Vector3d(-M, -M, 1 - M), new Vector3d(M, M, 1 - M)), s);
            // a difference of 2^62 units is not admitted
            Kernel(a, new Vector3d(Math.Pow(2, 62), 0, 1), new Vector3d(0, 1, 1), new Vector3d(1, 1, 1), out adm);
            Assert.False(adm);
            Kernel(a, new Vector3d(Math.Pow(2, 62) - Math.Pow(2, 10), 0, 1), new Vector3d(0, 1, 1), new Vector3d(1, 1, 1), out adm);
            Assert.True(adm);
        }

        [Fact]
        public void DifferencesNotRepresentable_ExactByTwoDiff()
        {
            // x - y needs more than 53 bits (the low part lo of TwoDiff is nonzero): 2^60 + 1 - (-2^60 ...)
            var r = new Random(4);
            int admitted = 0;
            for (int k = 0; k < 100000; ++k) {
                var v = new Vector3d[4];
                for (int i = 0; i < 4; ++i) {
                    double big = Math.Pow(2, r.Next(40, 61)) * (r.Next(2) == 0 ? 1 : -1);
                    v[i] = new Vector3d(big + r.Next(-1000, 1000) * 64, r.Next(-1000, 1000), (i % 2 == 0) ? big : r.Next(-7, 8));
                }
                AssertAgrees(v[0], v[1], v[2], v[3], ref admitted);
            }
            Assert.True(admitted > 50000, $"admitted {admitted}");
        }

        [Fact]
        public void ExtremeExponents_FallBack()
        {
            bool adm;
            // lowest bit below 2^-1022 (subnormal coordinates): not admitted, the public predicate still exact
            var a = new Vector3d(double.Epsilon, 0, 0);
            var b = new Vector3d(0, double.Epsilon * 3, 0);
            var c = new Vector3d(0, 0, double.Epsilon * 5);
            var d = new Vector3d(double.Epsilon * 7, double.Epsilon * 9, double.Epsilon);
            Kernel(a, b, c, d, out adm);
            Assert.False(adm);
            Assert.Equal(ExactPredicates.orient3d_bigint(a, b, c, d), ExactPredicates.Orient3dExact(a, b, c, d));
            // lowest bit 2^-1022: admitted
            double u = Math.Pow(2, -1022);
            Assert.Equal(ExactPredicates.orient3d_bigint(new Vector3d(u, 0, 0), new Vector3d(0, 3 * u, 0), new Vector3d(0, 0, 5 * u), new Vector3d(7 * u, 9 * u, u)),
                Kernel(new Vector3d(u, 0, 0), new Vector3d(0, 3 * u, 0), new Vector3d(0, 0, 5 * u), new Vector3d(7 * u, 9 * u, u), out adm));
            Assert.True(adm);
            // huge coordinates: lowest bit 2^1000, admitted; a difference that overflows is not
            double h = Math.Pow(2, 1000);
            Assert.Equal(ExactPredicates.orient3d_bigint(new Vector3d(h, 0, 0), new Vector3d(0, 3 * h, 0), new Vector3d(0, 0, 5 * h), new Vector3d(7 * h, -9 * h, h)),
                Kernel(new Vector3d(h, 0, 0), new Vector3d(0, 3 * h, 0), new Vector3d(0, 0, 5 * h), new Vector3d(7 * h, -9 * h, h), out adm));
            Assert.True(adm);
            Kernel(new Vector3d(double.MaxValue, 0, 0), new Vector3d(-double.MaxValue, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1), out adm);
            Assert.False(adm);
            // lowest bit 2^1023: 2^-1023 would be subnormal, not admitted
            double g = Math.Pow(2, 1023);
            Kernel(new Vector3d(g, 0, 0), new Vector3d(0, g, 0), new Vector3d(0, 0, g), new Vector3d(-g, -g, g), out adm);
            Assert.False(adm);
            Assert.Equal(ExactPredicates.orient3d_bigint(new Vector3d(g, 0, 0), new Vector3d(0, g, 0), new Vector3d(0, 0, g), new Vector3d(-g, -g, g)),
                ExactPredicates.Orient3dExact(new Vector3d(g, 0, 0), new Vector3d(0, g, 0), new Vector3d(0, 0, g), new Vector3d(-g, -g, g)));
        }

        [Fact]
        public void Invariance_Pow2ScalesGiveTheSameResult()
        {
            // the 2^k scales move only the common exponent: admission and sign are the same bit for bit; the other
            // similarities change the coordinates, so the kernel is compared with the BigInteger determinant there
            var r = new Random(5);
            var inputs = new List<Vector3d[]>();
            for (int k = 0; k < 20000; ++k) {
                float ox = (float)(r.NextDouble() * 200 - 100), oy = (float)(r.NextDouble() * 200 - 100);
                var v = new Vector3d[4];
                for (int i = 0; i < 4; ++i) {
                    double s = r.Next(-4096, 4096) / 256.0, t = r.Next(-4096, 4096) / 256.0;
                    v[i] = new Vector3d(ox + s, oy + t, 0.5 * s + 0.25 * t + ((k % 3 == 0 && i == 3) ? Math.Pow(2, -20) : 0));
                }
                inputs.Add(v);
            }
            foreach (Similarity sim in Similarity.All()) {
                int admittedCount = 0;
                foreach (Vector3d[] v in inputs) {
                    Vector3d a = sim.Apply(v[0]), b = sim.Apply(v[1]), c = sim.Apply(v[2]), d = sim.Apply(v[3]);
                    if (sim.IsExact) {
                        bool adm0, adm1;
                        int s0 = Kernel(v[0], v[1], v[2], v[3], out adm0), s1 = Kernel(a, b, c, d, out adm1);
                        Assert.Equal(adm0, adm1);
                        Assert.Equal(s0, s1);
                        Assert.Equal(ExactPredicates.Orient3d(v[0], v[1], v[2], v[3]), ExactPredicates.Orient3d(a, b, c, d));
                    }
                    AssertAgrees(a, b, c, d, ref admittedCount);
                }
            }
        }

        [Fact]
        public void TrailingZerosPortable_MatchesBitOperations()
        {
            var r = new Random(6);
            for (int k = 0; k < 64; ++k)
                Assert.Equal(k, ExactPredicates.TrailingZerosPortable(1UL << k));
            for (int k = 0; k < 100000; ++k) {
                ulong v = ((ulong)(uint)r.Next() << 32 | (uint)r.Next()) << r.Next(64);
                if (v == 0) continue;
                // the lowest set bit, isolated: v & -v = 2^tz
                ulong low = v & (~v + 1);
                int expected = 0;
                while ((1UL << expected) != low) expected++;
                Assert.Equal(expected, ExactPredicates.TrailingZerosPortable(v));
            }
        }
    }
}
