using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace g3
{
    /// <summary>
    /// The parallel building blocks of the boolean's classification and assembly (docs/perf/BOOLEAN_THREAD_SCALING.md,
    /// levers 2 and 4): the undirected edges of a triangle list grouped by a stable radix sort, and a union-find whose
    /// partition and roots do not depend on the order or the schedule of the unions.
    ///
    /// Every function gives the same result for every schedule, every block size and the serial path (SerialOnThisThread),
    /// bit for bit: the sort is stable, so its output is unique; the union-find always links the larger root under the
    /// smaller one, so the root of a class is its smallest member whatever the order of the unions. Blocks are fixed ranges
    /// of the index space, each written by one task only.
    /// </summary>
    internal static class ExactMeshEdgeGroups
    {
        /// <summary> below this many entries the sort and the loops over edge groups run serially (a parallel loop costs more than they save); a test hook </summary>
        internal static int ParallelFrom = 1 << 16;

        /// <summary> entries per block of a loop over a sorted range or an index range; a test hook (small blocks put many group borders in a small mesh) </summary>
        internal static int BlockSize = 1 << 13;

        /// <summary>
        /// body(b) for every block b in [0, blocks): one call directly for a single block, else in parallel (the blocks are
        /// disjoint ranges, each body writes its own slots)
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void ForBlocks(int blocks, Action<int> body)
        {
            if (blocks == 1)
                body(0);
            else if (blocks > 1)
                gParallel.ForRange(blocks, body);
        }

        /// <summary> the number of blocks of count entries when the work runs in parallel, 1 when it runs serially (fewer than ParallelFrom entries) </summary>
        internal static int Blocks(int count)
        {
            return (count < ParallelFrom) ? 1 : (count + BlockSize - 1) / BlockSize;
        }

        /// <summary> the entries per block when count entries are split into blocks (the last block can have fewer): block b is [b * Per, min(count, (b + 1) * Per)) </summary>
        internal static int Per(int count, int blocks)
        {
            return Math.Max(1, (count + blocks - 1) / blocks);
        }

        /// <summary>
        /// the undirected edges of n triangles, sorted: edgeKeys[k] is the edge (ExactMeshArrangement.EdgeKey), edgeTri[k] the
        /// triangle; equal edges are adjacent, in the order of their triangles. tri must be callable from several threads.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void GroupEdges(int n, Func<int, Index3i> tri, out long[] edgeKeys, out int[] edgeTri)
        {
            long[] keys = new long[3 * n];
            int[] items = new int[3 * n];
            int blocks = Blocks(3 * n);
            int per = Per(n, blocks);
            var blockMax = new long[blocks];
            ForBlocks(blocks, [MethodImpl(JitHints.Optimize)] (int b) => {
                int end = Math.Min(n, (b + 1) * per);
                long max = 0;
                for (int i = b * per; i < end; ++i) {
                    Index3i t = tri(i);
                    long k0 = ExactMeshArrangement.EdgeKey(t.a, t.b), k1 = ExactMeshArrangement.EdgeKey(t.b, t.c), k2 = ExactMeshArrangement.EdgeKey(t.c, t.a);
                    keys[3 * i] = k0;
                    keys[3 * i + 1] = k1;
                    keys[3 * i + 2] = k2;
                    items[3 * i] = items[3 * i + 1] = items[3 * i + 2] = i;
                    max = Math.Max(max, Math.Max(k0, Math.Max(k1, k2)));
                }
                blockMax[b] = max;
            });
            long maxKey = 0;
            foreach (long m in blockMax)
                maxKey = Math.Max(maxKey, m);
            if (blocks == 1)
                SortByKeySerial(keys, items);
            else
                SortByKeyParallel(keys, items, blocks, maxKey);
            edgeKeys = keys;
            edgeTri = items;
        }

        /// <summary>
        /// sorts keys (non-negative) and items by the keys, stably (equal keys keep the order of their items): a radix
        /// sort by 11-bit digits, skipping the digits that are the same in all keys; in parallel from ParallelFrom entries
        /// on (the result is unique, so it does not depend on the blocks)
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void SortByKey(long[] keysIn, int[] itemsIn)
        {
            int blocks = Blocks(keysIn.Length);
            if (blocks == 1) {
                SortByKeySerial(keysIn, itemsIn);
                return;
            }
            long max = 0;
            foreach (long k in keysIn)
                max = Math.Max(max, k);
            SortByKeyParallel(keysIn, itemsIn, blocks, max);
        }

        /// <summary>
        /// the stable radix sort of one thread over the keys in the range [lo, hi): by digits of 8 bits for a small range, else of 11
        /// (a table of 256 or 2048 counters per pass costs as much as a few hundred entries), only the digits in which some keys
        /// differ; scratch (same range) is overwritten; the result is in keys, items; count has 2049 entries
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        static void SortRange(long[] keys, int[] items, long[] scratchKeys, int[] scratchItems, int lo, int hi, int[] count)
        {
            if (hi - lo < 2)
                return;
            int bits = (hi - lo < 4096) ? 8 : 11, digits = 1 << bits, mask = digits - 1;
            long differ = 0, first = keys[lo];
            for (int i = lo + 1; i < hi; ++i)
                differ |= keys[i] ^ first;
            long[] k = keys, k2 = scratchKeys;
            int[] it = items, it2 = scratchItems;
            for (int shift = 0; shift < 64; shift += bits) {
                // (a digit that all keys share: nothing to sort)
                if (((differ >> shift) & mask) == 0)
                    continue;
                Array.Clear(count, 0, digits + 1);
                for (int i = lo; i < hi; ++i)
                    count[(int)((k[i] >> shift) & mask) + 1]++;
                for (int d = 0; d < digits; ++d)
                    count[d + 1] += count[d];
                for (int i = lo; i < hi; ++i) {
                    int at = lo + count[(int)((k[i] >> shift) & mask)]++;
                    k2[at] = k[i];
                    it2[at] = it[i];
                }
                long[] tk = k; k = k2; k2 = tk;
                int[] ti = it; it = it2; it2 = ti;
            }
            if (k != keys) {
                Array.Copy(k, lo, keys, lo, hi - lo);
                Array.Copy(it, lo, items, lo, hi - lo);
            }
        }

        [MethodImpl(JitHints.Optimize)]
        static void SortByKeySerial(long[] keys, int[] items)
        {
            SortRange(keys, items, new long[keys.Length], new int[keys.Length], 0, keys.Length, new int[(1 << 11) + 1]);
        }

        const int Buckets = 256;

        // The parallel sort: the keys are cut into Buckets ranges of their high part (key >> 32, scaled to its maximum: a monotone
        // map, so the ranges follow each other in key order), moved there by a stable partition (a histogram per block, the offsets
        // of every (bucket, block) pair in bucket-major, block-minor order, the scatter of each block to its own offsets), and each
        // bucket is then sorted on its own by the serial radix sort (a bucket is small enough for the cache). The stable partition
        // keeps equal keys in the order of their items, the bucket sort keeps it, so the result is the unique stable order of the
        // keys, whatever the blocks and the schedule.
        [MethodImpl(JitHints.Optimize)]
        static void SortByKeyParallel(long[] keys, int[] items, int blocks, long maxKey)
        {
            int n = keys.Length;
            int per = Per(n, blocks);
            // bucket of a key: floor(high * scale / 2^32), at most Buckets - 1 for high <= maxHigh
            ulong scale = ((ulong)Buckets << 32) / ((ulong)(maxKey >> 32) + 1);
            long[] k2 = new long[n];
            int[] i2 = new int[n];
            var count = new int[blocks * Buckets];
            ForBlocks(blocks, [MethodImpl(JitHints.Optimize)] (int b) => HistogramBuckets(keys, count, b * Buckets, b * per, Math.Min(n, (b + 1) * per), scale));
            var start = new int[Buckets + 1];
            int running = 0;
            for (int d = 0; d < Buckets; ++d) {
                start[d] = running;
                for (int b = 0; b < blocks; ++b) {
                    int c = count[b * Buckets + d];
                    count[b * Buckets + d] = running;
                    running += c;
                }
            }
            start[Buckets] = running;
            ForBlocks(blocks, [MethodImpl(JitHints.Optimize)] (int b) => ScatterBuckets(keys, items, k2, i2, count, b * Buckets, b * per, Math.Min(n, (b + 1) * per), scale));
            // (each bucket sorted on its own: from the partition (k2, i2) with the old arrays as scratch, the result back in them)
            gParallel.ForRange(Buckets, () => new int[(1 << 11) + 1], [MethodImpl(JitHints.Optimize)] (int d, int[] local) => {
                int lo = start[d], hi = start[d + 1];
                if (hi - lo < 2) {
                    Array.Copy(k2, lo, keys, lo, hi - lo);
                    Array.Copy(i2, lo, items, lo, hi - lo);
                    return;
                }
                SortRange(k2, i2, keys, items, lo, hi, local);
                Array.Copy(k2, lo, keys, lo, hi - lo);
                Array.Copy(i2, lo, items, lo, hi - lo);
            });
        }

        [MethodImpl(JitHints.Optimize)]
        static void HistogramBuckets(long[] keys, int[] count, int o, int lo, int hi, ulong scale)
        {
            Array.Clear(count, o, Buckets);
            for (int i = lo; i < hi; ++i)
                count[o + (int)(((ulong)(keys[i] >> 32) * scale) >> 32)]++;
        }

        [MethodImpl(JitHints.Optimize)]
        static void ScatterBuckets(long[] keysFrom, int[] itemsFrom, long[] keysTo, int[] itemsTo, int[] offsets, int o, int lo, int hi, ulong scale)
        {
            for (int i = lo; i < hi; ++i) {
                long key = keysFrom[i];
                int at = offsets[o + (int)(((ulong)(key >> 32) * scale) >> 32)]++;
                keysTo[at] = key;
                itemsTo[at] = itemsFrom[i];
            }
        }

        // ---- union-find over int slots: the root of a class is its smallest member -----------------------------------------

        /// <summary> parent[i] = i </summary>
        internal static int[] NewForest(int count)
        {
            int[] parent = new int[count];
            for (int k = 0; k < parent.Length; ++k)
                parent[k] = k;
            return parent;
        }

        /// <summary> the root of x, halving the path (the writes only point a slot at one of its ancestors, so concurrent finds and unions stay correct) </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static int Find(int[] parent, int x)
        {
            while (true) {
                int p = Volatile.Read(ref parent[x]);
                if (p == x)
                    return x;
                int gp = Volatile.Read(ref parent[p]);
                if (gp != p)
                    parent[x] = gp;
                x = gp;
            }
        }

        /// <summary>
        /// joins the classes of x and y, linking the larger root under the smaller one: whatever the order of the unions, a
        /// class ends with its smallest member as root. atomic: safe against concurrent unions (compare-and-swap on the root
        /// being linked); not atomic: for one thread
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static void Union(int[] parent, int x, int y, bool atomic)
        {
            while (true) {
                x = Find(parent, x);
                y = Find(parent, y);
                if (x == y)
                    return;
                if (x < y) {
                    int t = x; x = y; y = t;
                }
                // (x > y: x goes under y)
                if (atomic == false) {
                    parent[x] = y;
                    return;
                }
                if (Interlocked.CompareExchange(ref parent[x], y, x) == x)
                    return;
            }
        }

        /// <summary>
        /// Numbers the classes of the forest in the order of their roots: vids[s] is the number of the class of slot s, rootSlot[v]
        /// the root (the smallest slot) of class v; returns the number of classes. Slots are numbered by their first appearance in
        /// slot order, as a serial scan would number them, because the root is the smallest slot of its class.
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static int NumberClasses(int[] parent, out int[] vids, out int[] rootSlot)
        {
            int slots = parent.Length;
            int blocks = Blocks(slots), per = Per(slots, blocks);
            int[] start = new int[blocks + 1];
            // (the roots of each block, then their numbers by a prefix sum over the blocks)
            ForBlocks(blocks, [MethodImpl(JitHints.Optimize)] (int b) => {
                int c = 0, end = Math.Min(slots, (b + 1) * per);
                for (int s = b * per; s < end; ++s)
                    if (parent[s] == s)
                        c++;
                start[b + 1] = c;
            });
            for (int b = 0; b < blocks; ++b)
                start[b + 1] += start[b];
            int classes = start[blocks];
            int[] numbers = new int[slots], roots = new int[classes];
            ForBlocks(blocks, [MethodImpl(JitHints.Optimize)] (int b) => {
                int next = start[b], end = Math.Min(slots, (b + 1) * per);
                for (int s = b * per; s < end; ++s)
                    if (parent[s] == s) {
                        numbers[s] = next;
                        roots[next++] = s;
                    }
            });
            // (the roots keep their numbers, the others read the number of their root)
            ForBlocks(blocks, [MethodImpl(JitHints.Optimize)] (int b) => {
                int end = Math.Min(slots, (b + 1) * per);
                for (int s = b * per; s < end; ++s)
                    if (parent[s] != s)
                        numbers[s] = numbers[Find(parent, s)];
            });
            vids = numbers;
            rootSlot = roots;
            return classes;
        }

        /// <summary>
        /// The points of the triangles in the order of their ids: used is the list of the point ids that a triangle has, usedIndex
        /// (size pointCount) the position of a point in it, -1 for a point that no triangle has. (Marks written by several tasks
        /// are all the same value.)
        /// </summary>
        [MethodImpl(JitHints.Optimize)]
        internal static List<int> UsedPoints(List<Index3i> tris, int pointCount, out int[] usedIndex)
        {
            int n = tris.Count;
            usedIndex = new int[pointCount];
            int blocks = Blocks(3 * n), per = Per(n, blocks);
            int[] mark = usedIndex;
            ForBlocks(blocks, [MethodImpl(JitHints.Optimize)] (int b) => {
                int end = Math.Min(n, (b + 1) * per);
                for (int t = b * per; t < end; ++t) {
                    Index3i f = tris[t];
                    mark[f.a] = 1;
                    mark[f.b] = 1;
                    mark[f.c] = 1;
                }
            });
            var used = new List<int>();
            for (int p = 0; p < pointCount; ++p) {
                if (usedIndex[p] != 0) {
                    usedIndex[p] = used.Count;
                    used.Add(p);
                } else {
                    usedIndex[p] = -1;
                }
            }
            return used;
        }
    }
}
