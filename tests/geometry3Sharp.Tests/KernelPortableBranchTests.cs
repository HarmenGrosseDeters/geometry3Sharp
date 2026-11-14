using System;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The #if NET members of the exact kernel (registered in DeterministicApis.txt, G3D0005) against their portable
    /// twins, which the netstandard2.0 build runs: on .NET the test compares the two branches directly, value by value
    /// (on .NET Framework both sides are the portable branch).
    /// </summary>
    public unsafe class KernelPortableBranchTests
    {
        static ulong[] Words()
        {
            var r = new Random(8590);
            var w = new ulong[2000];
            for (int i = 0; i < w.Length; ++i) {
                ulong x = ((ulong)(uint)r.Next() << 32) | (uint)r.Next();
                int k = i % 64;
                w[i] = (i % 4 == 0) ? 1UL << k : (i % 4 == 1) ? (x >> k) | (1UL << (63 - k)) : (i % 4 == 2) ? x << k : x | 1;
                if (w[i] == 0) w[i] = 1;
            }
            w[0] = 1; w[1] = ulong.MaxValue; w[2] = 1UL << 63;
            return w;
        }

        static BigInteger Random(Random r, int bits)
        {
            var bytes = new byte[(bits + 7) / 8 + 1];
            r.NextBytes(bytes);
            bytes[bytes.Length - 1] = 0;
            BigInteger v = new BigInteger(bytes) & ((BigInteger.One << bits) - 1);
            return (r.Next(2) == 0) ? v : -v;
        }

        [Fact]
        public void NetBranches_EqualTheirPortableTwins()
        {
            ulong[] w = Words();
            for (int i = 0; i < w.Length; ++i) {
                ulong v = w[i];
                Assert.Equal(ExactPredicates.TrailingZerosPortable(v), ExactPredicates.trailing_zeros(v));
                Assert.Equal(FixedArith.LeadingZerosPortable(v), FixedArith.leading_zeros(v));
                ulong u = w[(i * 7 + 3) % w.Length], lo, lo2;
                ulong hi = FixedArith.MulHigh(v, u, out lo), hi2 = FixedArith.MulHighPortable(v, u, out lo2);
                Assert.Equal(hi2, hi);
                Assert.Equal(lo2, lo);
            }
            var rand = new Random(8591);
            ulong* a = stackalloc ulong[8], b = stackalloc ulong[4], c = stackalloc ulong[4];
            for (int k = 0; k < 3000; ++k) {
                int bits = 1 + k % 520;
                BigInteger x = Random(rand, bits);
                Assert.Equal(ExactPoint3.BitLengthPortable(BigInteger.Abs(x)), ExactPoint3.BitLength(x));
                // FixedArith.ToBig: the span constructor against ToBigPortable
                int len; bool neg;
                if (FixedPoint3.LoadN(x, a, 8, out len, out neg) && len > 0) {
                    BigInteger mag = FixedPoint3.ToBigPortable(a, len);
                    Assert.Equal(neg ? -mag : mag, FixedArith.ToBig(new FixedInt(a, len, neg)));
                    Assert.Equal(x, neg ? -mag : mag);
                }
                // FixedPoint3.LoadN (TryWriteBytes) against LoadPortable (ToByteArray), and FixedPoint3.ToBig
                for (int t = 0; t < 4; ++t) b[t] = c[t] = 0x5A5A5A5A5A5A5A5AUL;
                byte mb, mc;
                bool fb = FixedPoint3.Load(x, b, out mb), fc = FixedPoint3.LoadPortable(x, c, out mc);
                Assert.Equal(fc, fb);
                if (fb) {
                    Assert.Equal(mc, mb);
                    for (int t = 0; t < (mb & 7); ++t) Assert.Equal(c[t], b[t]);
                    Assert.Equal(x, FixedPoint3.ToBig(b, mb));
                    int l = mb & 7;
                    if (l > 0)
                        Assert.Equal(BigInteger.Abs(x), FixedPoint3.ToBigPortable(b, l));
                }
            }
        }
    }
}
