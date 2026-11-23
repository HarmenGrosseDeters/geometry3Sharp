using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactPoint3.ShardBits chooses the shard of ExactMeshArrangement.AddPoint (determinism proof stage 2: the shard may not come from a
    /// hash value, which is seeded per process): a function of the canonical value, the same for every representation, negative components
    /// included, and spread over the shards.
    /// </summary>
    public class ExactPoint3ShardBitsTests
    {
        [Fact]
        public void ShardBits_IsAFunctionOfTheValue()
        {
            var rand = new Random(31);
            for (int k = 0; k < 500; ++k) {
                BigInteger x = Big(rand, k), y = Big(rand, k + 1), z = Big(rand, k + 2), w = BigInteger.Abs(Big(rand, k + 3)) + 1;
                BigInteger f = rand.Next(1, 1000);
                ExactPoint3 a = ExactPoint3.Create(x, y, z, w), b = ExactPoint3.Create(x * f, y * f, z * f, w * f);     // (the same value: both canonical)
                Assert.True(a.Equals(b));
                Assert.Equal(a.ShardBits(), b.ShardBits());
            }
        }

        // the bits are those of the components, two's complement: pinned on a few values (no hash seed, no platform)
        [Fact]
        public void ShardBits_PinnedValues()
        {
            Assert.Equal(((1 * 31 + 2) * 31 + 3) * 31 + 4, ExactPoint3.Create(1, 2, 3, 4).ShardBits());
            ExactPoint3 neg = ExactPoint3.Create(-1, 0, 1, 1);
            Assert.Equal(unchecked((((int.MaxValue) * 31 + 0) * 31 + 1) * 31 + 1), neg.ShardBits());
            BigInteger big = (BigInteger.One << 100) + 5;
            Assert.Equal(unchecked((((5) * 31 + 0) * 31 + 0) * 31 + 1), ExactPoint3.Create(big, 0, 0, 1).ShardBits());
        }

        [Fact]
        public void ShardBits_SpreadsOverTheShards()
        {
            var rand = new Random(32);
            var counts = new int[16];
            for (int k = 0; k < 4096; ++k) {
                ExactPoint3 p = ExactPoint3.Create(rand.Next(-100000, 100000), rand.Next(-100000, 100000), rand.Next(-100000, 100000), rand.Next(1, 1000));
                counts[(p.ShardBits() & 0x7FFFFFFF) % 16]++;
            }
            Assert.True(counts.Min() > 150 && counts.Max() < 370, string.Join(",", counts));
        }

        static BigInteger Big(Random rand, int k)
        {
            BigInteger v = rand.Next(-1000000, 1000000);
            if (k % 3 == 0) v = (v << 70) + rand.Next();
            return v;
        }
    }
}
