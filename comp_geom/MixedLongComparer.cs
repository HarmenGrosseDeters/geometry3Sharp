using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Equality comparer for 64-bit keys made of two 32-bit parts (a pair of vertex or source ids, a pair of grid cell coordinates), with
    /// a hash that mixes both. The default hash of a long is its low half xor its high half, which for the pair (a &lt;&lt; 32) | c is a ^ c:
    /// ids of neighbours in a mesh lie close to each other, so their xors fall on a few dozen values and 200 000 pairs make one long
    /// chain per lookup (a quadratic run: 20 of 22 seconds of a 56 000 triangle snap-grid mesh). Fibonacci hashing of the whole key.
    /// </summary>
    internal sealed class MixedLongComparer : IEqualityComparer<long>
    {
        public static readonly MixedLongComparer Instance = new MixedLongComparer();

        /// <summary> tests only (per thread): when set, [0] counts the hashes taken and [1] the key comparisons made by the sets and dictionaries
        /// that use the comparer; their ratio is the mean chain length probed, about 1 for a good hash and in the hundreds for lo ^ hi </summary>
        [System.ThreadStatic] internal static long[] CountsForTests;

        public bool Equals(long a, long b) { var n = CountsForTests; if (n != null) ++n[1]; return a == b; }

        public int GetHashCode(long key) { var n = CountsForTests; if (n != null) ++n[0]; return (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> 32); }
    }
}
