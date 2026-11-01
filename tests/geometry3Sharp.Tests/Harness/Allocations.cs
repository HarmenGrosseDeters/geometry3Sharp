#if NET8_0_OR_GREATER
using System;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// What a warmed call allocates on this thread. The runtime can allocate there itself while it optimizes the code (a test failed with
    /// 5456 bytes on one CI run, and passed on the next), so this takes the least of a few runs: code that allocates does so in each of them.
    /// </summary>
    internal static class Allocations
    {
        internal static long Least(int calls, Action hot)
        {
            for (int i = 0; i < calls; ++i) hot();
            long least = long.MaxValue;
            for (int attempt = 0; attempt < 5 && least != 0; ++attempt) {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < calls; ++i) hot();
                least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            return least;
        }
    }
}
#endif
