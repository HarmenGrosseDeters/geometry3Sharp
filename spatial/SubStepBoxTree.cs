// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// A box tree of moving primitives for MeshSweepCollision's broad phase (docs/design/CCD.md 10.11). Each primitive
    /// has a whole-step box and K sub-step boxes; every node holds the union of its primitives' boxes, whole and per
    /// sub-step. A query or a pair of nodes carries the mask of the sub-steps in which the boxes still overlap and is
    /// pruned when the mask is empty, as the multi-segment motion-blur BVHs of ray tracing keep one box per time segment
    /// in each node (Grünschloß et al. 2011, Woop et al. 2017). A pair of primitives is reported if and only if their
    /// closed whole-step boxes overlap and their closed boxes of some sub-step overlap: the unions of a node contain those
    /// of its primitives, so the pruning loses no such pair, and the leaves test the primitives' own boxes. The set of
    /// pairs therefore depends only on the boxes; the tree's order (Morton codes of the whole-step box centres, median
    /// splits) only decides which boxes share a node and the order in which pairs are reported, and depends only on the
    /// input.
    ///
    /// Layout of a primitive's boxes (Stride = 6 (K + 1) doubles): the whole-step box (min x y z, max x y z), then the box
    /// of each sub-step in the same form.
    /// </summary>
    internal sealed class SubStepBoxTree
    {
        const int LeafSize = 8;
        public readonly int K, Stride;
        readonly ulong all;                 // the mask of all K sub-steps
        readonly int[] order;               // the primitive at each leaf position
        readonly double[] leaf;             // the boxes by leaf position (Stride doubles each)
        int nodeCount;
        readonly double[] node;             // the boxes of the nodes (Stride doubles each), the root first
        readonly int[] nodeStart, nodeSize, nodeLeft, nodeRight;

        /// <summary>
        /// the tree of n primitives with K sub-steps: whole[6 i ..] is the whole-step box of primitive i; fill(i, dst,
        /// offset) writes its K sub-step boxes to dst[offset ..] (6 K doubles; called in parallel, once per primitive)
        /// </summary>
        public SubStepBoxTree(double[] whole, int n, int k, Action<int, double[], int> fill, bool parallel)
        {
            K = k;
            Stride = 6 * (k + 1);
            all = k == 64 ? ulong.MaxValue : (1UL << k) - 1;
            order = morton_order(whole, n);
            leaf = new double[checked((long)n * Stride)];
            for_blocks(n, parallel, pos => {
                int i = order[pos];
                long o = (long)pos * Stride;
                Array.Copy(whole, 6L * i, leaf, o, 6);
                fill(i, leaf, (int)(o + 6));
            });
            int cap = capacity(n);
            node = new double[checked((long)cap * Stride)];
            nodeStart = new int[cap]; nodeSize = new int[cap]; nodeLeft = new int[cap]; nodeRight = new int[cap];
            if (n > 0)
                build(0, n);
            // the boxes: of the leaves from their primitives (in parallel), then of the inner nodes from their children
            // (each node is numbered before its children, so in reverse order)
            var leaves = new List<int>();
            for (int a = 0; a < nodeCount; ++a)
                if (nodeLeft[a] < 0)
                    leaves.Add(a);
            for_blocks(leaves.Count, parallel, li => {
                int a = leaves[li];
                set_empty(a);
                for (int pos = nodeStart[a]; pos < nodeStart[a] + nodeSize[a]; ++pos)
                    contain(a, leaf, (long)pos * Stride);
            });
            for (int a = nodeCount - 1; a >= 0; --a)
                if (nodeLeft[a] >= 0) {
                    set_empty(a);
                    contain(a, node, (long)nodeLeft[a] * Stride);
                    contain(a, node, (long)nodeRight[a] * Stride);
                }
        }

        // the nodes of a tree of n primitives: halving splits leave at least LeafSize / 2 in every leaf but a root leaf
        static int capacity(int n) { return 2 * (n / (LeafSize / 2)) + 3; }

        /// <summary> the doubles the leaves and nodes of a tree of n primitives with k sub-steps take </summary>
        public static long Doubles(long n, int k)
        {
            return (n + 2 * (n / (LeafSize / 2)) + 3) * 6 * (k + 1);
        }

        /// <summary> the number of primitives </summary>
        public int Count { get { return order.Length; } }

        // the items per block of the parallel build (fixed, so the blocks depend only on the input; tests set smaller
        // values to build small trees in parallel)
        internal static volatile int BuildBlock = 1024;
        static void for_blocks(int n, bool parallel, Action<int> f)
        {
            int size = BuildBlock;
            int blocks = (n + size - 1) / size;
            Action<int> block = b => { for (int i = b * size; i < Math.Min(n, (b + 1) * size); ++i) f(i); };
            if (parallel && blocks > 1)
                gParallel.ForRange(blocks, block);
            else
                for (int b = 0; b < blocks; ++b) block(b);
        }

        void set_empty(int a)
        {
            long o = (long)a * Stride;
            for (int s = 0; s <= K; ++s, o += 6)
                for (int c = 0; c < 3; ++c) {
                    node[o + c] = double.PositiveInfinity;
                    node[o + 3 + c] = double.NegativeInfinity;
                }
        }

        void contain(int a, double[] src, long so)
        {
            long o = (long)a * Stride;
            for (int s = 0; s <= K; ++s, o += 6, so += 6)
                for (int c = 0; c < 3; ++c) {
                    node[o + c] = Math.Min(node[o + c], src[so + c]);
                    node[o + 3 + c] = Math.Max(node[o + 3 + c], src[so + 3 + c]);
                }
        }

        int build(int start, int count)
        {
            int a = nodeCount++;
            nodeStart[a] = start; nodeSize[a] = count; nodeLeft[a] = nodeRight[a] = -1;
            if (count <= LeafSize)
                return a;
            int half = count / 2;
            int l = build(start, half);
            int r = build(start + half, count - half);
            nodeLeft[a] = l;
            nodeRight[a] = r;
            return a;
        }

        // Stryker disable all: the order only decides which boxes share a node (performance, not the pairs)
        // the primitives sorted by the Morton code of their whole-step box centres over the box of all centres (21 bits
        // per axis), ties by index (a total order: the same on every runtime)
        static int[] morton_order(double[] whole, int n)
        {
            var ord = new int[n];
            var codes = new long[n];
            double[] lo = { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
            double[] hi = { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            for (int i = 0; i < n; ++i)
                for (int c = 0; c < 3; ++c) {
                    double m = 0.5 * whole[6 * i + c] + 0.5 * whole[6 * i + 3 + c];
                    lo[c] = Math.Min(lo[c], m);
                    hi[c] = Math.Max(hi[c], m);
                }
            const double cells = 2097151;   // 2^21 - 1
            var scale = new double[3];
            for (int c = 0; c < 3; ++c)
                scale[c] = hi[c] - lo[c] > 0 ? cells / (hi[c] - lo[c]) : 0;
            for (int i = 0; i < n; ++i) {
                ord[i] = i;
                long code = 0;
                for (int c = 0; c < 3; ++c) {
                    double m = 0.5 * whole[6 * i + c] + 0.5 * whole[6 * i + 3 + c];
                    double t = (m - lo[c]) * scale[c];
                    uint q = t >= 0 ? (t < cells ? (uint)t : (uint)cells) : 0u;
                    code |= spread(q) << (2 - c);
                }
                codes[i] = code;
            }
            sort(codes, ord);
            // one far box (or a centre that is not finite) squeezes the others into a few cells (fewer than n / 4
            // distinct codes): the coordinates as ranks instead, so that it only takes the last rank (as TriangleBoxTree)
            int distinct = n > 0 ? 1 : 0;
            for (int i = 1; i < n; ++i)
                if (codes[i] != codes[i - 1]) distinct++;
            if (4 * distinct < n) {
                uint[][] r = { ranks(whole, n, 0), ranks(whole, n, 1), ranks(whole, n, 2) };
                for (int i = 0; i < n; ++i) {
                    ord[i] = i;
                    codes[i] = spread(r[0][i]) << 2 | spread(r[1][i]) << 1 | spread(r[2][i]);
                }
                sort(codes, ord);
            }
            return ord;
        }

        // by code, ties by index
        static void sort(long[] codes, int[] ord)
        {
            int n = codes.Length;
            Array.Sort(codes, ord);
            for (int s0 = 0, s1; s0 < n; s0 = s1) {
                for (s1 = s0 + 1; s1 < n && codes[s1] == codes[s0]; ++s1) { }
                if (s1 - s0 > 1)
                    Array.Sort(ord, s0, s1 - s0);
            }
        }

        // per primitive, the rank of its centre's coordinate on the axis among all centres (as Array.Sort orders doubles:
        // NaN first; equal values one rank), scaled to 0 .. 2^21 - 1
        static uint[] ranks(double[] whole, int n, int axis)
        {
            var c = new double[n];
            var idx = new int[n];
            for (int i = 0; i < n; ++i) {
                c[i] = 0.5 * whole[6 * i + axis] + 0.5 * whole[6 * i + 3 + axis];
                idx[i] = i;
            }
            Array.Sort(c, idx);
            var rank = new uint[n];
            int first = 0;
            for (int k = 0; k < n; ++k) {
                if (k > 0 && c[k].CompareTo(c[k - 1]) != 0)
                    first = k;
                rank[idx[k]] = (uint)((long)first * 2097151 / Math.Max(1, n - 1));
            }
            return rank;
        }

        /// <summary> tests: the primitive at each leaf position (the tree's order) </summary>
        internal int[] Order { get { return order; } }

        // the 21 bits of v at every third bit position
        static long spread(uint v)
        {
            ulong x = v & 0x1fffff;
            x = (x | x << 32) & 0x1f00000000ffffUL;
            x = (x | x << 16) & 0x1f0000ff0000ffUL;
            x = (x | x << 8) & 0x100f00f00f00f00fUL;
            x = (x | x << 4) & 0x10c30c30c30c30c3UL;
            x = (x | x << 2) & 0x1249249249249249UL;
            return (long)x;
        }
        // Stryker restore all

        // the closed boxes a[ao ..] and b[bo ..] (min x y z, max x y z) overlap
        static bool overlaps(double[] a, long ao, double[] b, long bo)
        {
            return !(a[ao + 3] < b[bo] || a[ao] > b[bo + 3] || a[ao + 4] < b[bo + 1] || a[ao + 1] > b[bo + 4]
                     || a[ao + 5] < b[bo + 2] || a[ao + 2] > b[bo + 5]);
        }

        // the sub-steps of mask in which the sub-step boxes of a (from ao, the whole-step box first) and of b overlap
        ulong sub_overlaps(double[] a, long ao, double[] b, long bo, ulong mask)
        {
            ulong r = 0;
            for (int s = 0; s < K; ++s) {
                ulong bit = 1UL << s;
                if ((mask & bit) != 0 && overlaps(a, ao + 6 + 6 * s, b, bo + 6 + 6 * s))
                    r |= bit;
            }
            return r;
        }

        /// <summary>
        /// the primitives whose whole-step box and some sub-step box overlap those of the query (q: Stride doubles, the
        /// layout of the primitives), appended to result in an order that depends only on the tree
        /// </summary>
        public void Query(double[] q, List<int> result, ref int[] stack, ref ulong[] masks)
        {
            if (nodeCount == 0)
                return;
            if (stack == null) { stack = new int[256]; masks = new ulong[256]; }
            int top = 0;
            stack[top] = 0; masks[top++] = all;
            while (top > 0) {
                --top;
                int a = stack[top];
                ulong m = masks[top];
                long ao = (long)a * Stride;
                if (overlaps(node, ao, q, 0) == false)
                    continue;
                m = sub_overlaps(node, ao, q, 0, m);
                if (m == 0)
                    continue;
                if (nodeLeft[a] < 0) {
                    for (int pos = nodeStart[a], e = nodeStart[a] + nodeSize[a]; pos < e; ++pos) {
                        long po = (long)pos * Stride;
                        if (overlaps(leaf, po, q, 0) && sub_overlaps(leaf, po, q, 0, m) != 0)
                            result.Add(order[pos]);
                    }
                } else {
                    stack[top] = nodeLeft[a]; masks[top++] = m;
                    stack[top] = nodeRight[a]; masks[top++] = m;
                }
            }
        }

        /// <summary>
        /// The work items of SelfPairs: node pairs (a, a) for the pairs within node a, (a, b) for the pairs between nodes
        /// a and b; together they give every reported pair exactly once. Split breadth first until there are at least
        /// minTasks (fewer for small trees); the list depends only on the tree.
        /// </summary>
        public List<Index2i> SelfPairTasks(int minTasks)
        {
            var tasks = new List<Index2i>();
            if (nodeCount == 0)
                return tasks;
            tasks.Add(new Index2i(0, 0));
            for (bool split = true; split && tasks.Count < minTasks;) {
                split = false;
                var next = new List<Index2i>(3 * tasks.Count);
                foreach (Index2i t in tasks) {
                    int a = t.a, b = t.b;
                    if (a == b) {
                        if (nodeLeft[a] < 0) { next.Add(t); continue; }
                        int l = nodeLeft[a], r = nodeRight[a];
                        next.Add(new Index2i(l, l)); next.Add(new Index2i(l, r)); next.Add(new Index2i(r, r));
                        split = true;
                    } else if (nodes_overlap(a, b, all) != 0) {
                        if (nodeLeft[a] < 0 && nodeLeft[b] < 0) { next.Add(t); continue; }
                        if (split_first(a, b)) { next.Add(new Index2i(nodeLeft[a], b)); next.Add(new Index2i(nodeRight[a], b)); }
                        else { next.Add(new Index2i(a, nodeLeft[b])); next.Add(new Index2i(a, nodeRight[b])); }
                        split = true;
                    }
                }
                tasks = next;
            }
            return tasks;
        }

        // the sub-steps of mask in which nodes a and b overlap, 0 if their whole-step boxes do not
        ulong nodes_overlap(int a, int b, ulong mask)
        {
            long ao = (long)a * Stride, bo = (long)b * Stride;
            if (overlaps(node, ao, node, bo) == false)
                return 0;
            return sub_overlaps(node, ao, node, bo, mask);
        }

        bool split_first(int a, int b)
        {
            return nodeLeft[a] >= 0 && (nodeLeft[b] < 0 || nodeSize[a] >= nodeSize[b]);
        }

        struct Item { public int A, B; public ulong Mask; }

        /// <summary>
        /// the pairs (i, j) of different primitives of work item t (SelfPairTasks) whose whole-step boxes and some
        /// sub-step boxes overlap, appended to result, in an order that depends only on the tree
        /// </summary>
        public void SelfPairs(Index2i t, List<Index2i> result)
        {
            var stack = new List<Item> { new Item { A = t.a, B = t.b, Mask = all } };
            while (stack.Count > 0) {
                Item top = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                int a = top.A, b = top.B;
                if (a == b) {
                    if (nodeLeft[a] < 0) {
                        for (int i = nodeStart[a], e = nodeStart[a] + nodeSize[a]; i < e; ++i)
                            for (int j = i + 1; j < e; ++j)
                                leaf_pair(i, j, all, result);
                    } else {
                        int l = nodeLeft[a], r = nodeRight[a];
                        stack.Add(new Item { A = r, B = r, Mask = all });
                        stack.Add(new Item { A = l, B = r, Mask = all });
                        stack.Add(new Item { A = l, B = l, Mask = all });
                    }
                    continue;
                }
                ulong m = nodes_overlap(a, b, top.Mask);
                if (m == 0)
                    continue;
                if (nodeLeft[a] < 0 && nodeLeft[b] < 0) {
                    for (int i = nodeStart[a], ei = nodeStart[a] + nodeSize[a]; i < ei; ++i)
                        for (int j = nodeStart[b], ej = nodeStart[b] + nodeSize[b]; j < ej; ++j)
                            leaf_pair(i, j, m, result);
                } else if (split_first(a, b)) {
                    stack.Add(new Item { A = nodeRight[a], B = b, Mask = m });
                    stack.Add(new Item { A = nodeLeft[a], B = b, Mask = m });
                } else {
                    stack.Add(new Item { A = a, B = nodeRight[b], Mask = m });
                    stack.Add(new Item { A = a, B = nodeLeft[b], Mask = m });
                }
            }
        }

        void leaf_pair(int i, int j, ulong mask, List<Index2i> result)
        {
            long io = (long)i * Stride, jo = (long)j * Stride;
            if (overlaps(leaf, io, leaf, jo) && sub_overlaps(leaf, io, leaf, jo, mask) != 0)
                result.Add(new Index2i(order[i], order[j]));
        }
    }
}
