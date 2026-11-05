using System;
using System.Collections.Generic;

namespace Meshing2Acceptance
{
    /// <summary>
    /// The pairs of closed integer boxes {xlo, xhi, ylo, yhi} that overlap. Each box is given by integer ranks, so every
    /// comparison is exact. The conformity check uses it for triangle-vertex and edge-edge candidates.
    /// </summary>
    public static class BoxPairs
    {
        /// <summary>
        /// Calls back for every overlapping pair, each once: (index in A, index in B), or, with <paramref name="same"/>, every
        /// unordered pair of distinct boxes of A once. The pairs come in no particular order.
        ///
        /// A sweep over x keeps the active boxes, those whose x range still reaches the sweep line, in two segment trees over y.
        /// The query is the y range [lo, hi] of the box that starts at the sweep line. A box overlaps it in y if and only if one of
        /// these holds:
        /// - its y range contains lo. A stabbing query finds these: the box is stored at the canonical nodes of its y range, and the
        ///   query walks the path from leaf lo to the root.
        /// - its lower end lies in (lo, hi]. A range query finds these: the box is stored on the path from the leaf of its lower end
        ///   to the root, and the query scans the canonical nodes of (lo, hi].
        /// The two cases are disjoint. Within each case a box is found at most once, because its canonical nodes, and the
        /// canonical nodes of a query, are disjoint. A box whose x range ended before the sweep line is removed from a list when the
        /// scan meets it. So the time is O((n + pairs) log n), where the plain sweep needs O(n * active) when long boxes keep the
        /// active list long.
        /// </summary>
        public static void Overlaps(int[][] boxesA, int[][] boxesB, bool same, Action<int, int> hit)
        {
            var events = Events(boxesA, boxesB, same);
            int maxY = 0;
            foreach (var b in boxesA) maxY = Math.Max(maxY, b[3]);
            if (!same) foreach (var b in boxesB) maxY = Math.Max(maxY, b[3]);
            var treeA = new Tree(maxY + 1);
            var treeB = same ? treeA : new Tree(maxY + 1);
            foreach (var ev in events) {
                var mine = ev.kind == 0 ? boxesA[ev.id] : boxesB[ev.id];
                // the boxes of the other side (with same: the earlier boxes of A)
                var others = same || ev.kind == 1 ? treeA : treeB;
                var otherBoxes = same || ev.kind == 1 ? boxesA : boxesB;
                others.Query(mine[2], mine[3], ev.x, otherBoxes, id => { if (ev.kind == 0) hit(ev.id, id); else hit(id, ev.id); });
                (ev.kind == 0 ? treeA : treeB).Insert(ev.id, mine[2], mine[3]);
            }
        }

        /// <summary>The plain sweep over x: the reference for <see cref="Overlaps"/> in check mode and in the tests.</summary>
        public static void OverlapsBySweep(int[][] boxesA, int[][] boxesB, bool same, Action<int, int> hit)
        {
            var events = Events(boxesA, boxesB, same);
            var activeA = new List<int>(); var activeB = new List<int>();
            foreach (var ev in events) {
                var mine = ev.kind == 0 ? boxesA[ev.id] : boxesB[ev.id];
                var others = same ? activeA : (ev.kind == 0 ? activeB : activeA);
                var otherBoxes = same || ev.kind == 1 ? boxesA : boxesB;
                for (int i = others.Count - 1; i >= 0; --i) {
                    int id = others[i];
                    var box = otherBoxes[id];
                    if (box[1] < ev.x) { others[i] = others[others.Count - 1]; others.RemoveAt(others.Count - 1); continue; }
                    if (box[3] >= mine[2] && box[2] <= mine[3]) {
                        if (ev.kind == 0) hit(ev.id, id); else hit(id, ev.id);
                    }
                }
                (ev.kind == 0 ? activeA : activeB).Add(ev.id);
            }
        }

        /// <summary>The boxes by their lower x end; at equal x, A before B, then by index (boxes that start at the same x overlap
        /// there, and the later one finds the earlier one).</summary>
        static List<(int x, int kind, int id)> Events(int[][] boxesA, int[][] boxesB, bool same)
        {
            var events = new List<(int x, int kind, int id)>(boxesA.Length + (same ? 0 : boxesB.Length));
            for (int i = 0; i < boxesA.Length; ++i) events.Add((boxesA[i][0], 0, i));
            if (!same) for (int i = 0; i < boxesB.Length; ++i) events.Add((boxesB[i][0], 1, i));
            events.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.kind != q.kind ? p.kind.CompareTo(q.kind) : p.id.CompareTo(q.id));
            return events;
        }

        /// <summary>Two segment trees over the y ranks 0 .. size-1, in the usual array layout (leaf of y at P + y).</summary>
        sealed class Tree
        {
            readonly int P;
            readonly List<int>[] span;    // the boxes stored at the canonical nodes of their y range
            readonly List<int>[] lower;   // the boxes stored on the path from the leaf of their lower y end to the root

            public Tree(int size)
            {
                P = 1;
                while (P < size) P <<= 1;
                span = new List<int>[2 * P];
                lower = new List<int>[2 * P];
            }

            static void Add(List<int>[] lists, int node, int id) => (lists[node] ?? (lists[node] = new List<int>(4))).Add(id);

            public void Insert(int id, int ylo, int yhi)
            {
                for (int l = ylo + P, r = yhi + 1 + P; l < r; l >>= 1, r >>= 1) {
                    if ((l & 1) != 0) Add(span, l++, id);
                    if ((r & 1) != 0) Add(span, --r, id);
                }
                // (not at the root: a range query (lo, hi] never covers leaf 0, so it never takes the root as a canonical node)
                for (int n = ylo + P; n > 1; n >>= 1) Add(lower, n, id);
            }

            /// <summary>Calls back for each stored box whose y range meets [ylo, yhi] and whose x range reaches x; boxes whose x range
            /// ended before x are dropped from the lists the scan passes.</summary>
            public void Query(int ylo, int yhi, int x, int[][] boxes, Action<int> hit)
            {
                for (int n = ylo + P; n >= 1; n >>= 1) Scan(span[n], x, boxes, hit);
                for (int l = ylo + 1 + P, r = yhi + 1 + P; l < r; l >>= 1, r >>= 1) {
                    if ((l & 1) != 0) Scan(lower[l++], x, boxes, hit);
                    if ((r & 1) != 0) Scan(lower[--r], x, boxes, hit);
                }
            }

            static void Scan(List<int> list, int x, int[][] boxes, Action<int> hit)
            {
                if (list == null) return;
                for (int i = list.Count - 1; i >= 0; --i) {
                    int id = list[i];
                    if (boxes[id][1] < x) { list[i] = list[list.Count - 1]; list.RemoveAt(list.Count - 1); continue; }
                    hit(id);
                }
            }
        }
    }
}
