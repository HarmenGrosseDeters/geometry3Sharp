// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Grid of triangle ids (non-negative, eg DMesh3 triangle ids) by the cells their bounding boxes overlap,
    /// with insert and remove, for meshes that change (eg during a reduction). Hierarchical: level 0 has the
    /// given cell size, each further level LevelFactor times larger cells; a triangle is stored on the
    /// finest level where its box overlaps at most MaxCellsPerTriangle cells, a query visits every level. So
    /// triangles that grow (late in a reduction) or a few large ones among many small ones cost neither a
    /// long list that every query visits nor a rebuild with coarser cells for all.
    /// Not thread-safe, also not for concurrent queries (Query marks the triangles it has seen).
    /// </summary>
    public class TriangleHashGrid
    {
        /// <summary> a triangle is stored on the finest level where its box overlaps at most this many cells </summary>
        public int MaxCellsPerTriangle = 16;
        /// <summary> cell size ratio of consecutive levels </summary>
        public const int LevelFactor = 4;

        Vector3d origin;
        double cellSize;
        // per level: cell key -> triangles
        List<Dictionary<long, List<int>>> levels = new List<Dictionary<long, List<int>>>();
        struct Entry
        {
            public bool Present;
            public int Level;       // -1: not on a level (non-finite box), in `unplaced`
            public long[] Keys;
            public AxisAlignedBox3d Box;
            public int Stamp;       // the last query that saw it
        }
        Entry[] entries = new Entry[0];
        int count_present;
        int stamp;
        HashSet<int> unplaced = new HashSet<int>();

        const int MaxLevels = 20;
        const long Bias = 1 << 20;

        public TriangleHashGrid(Vector3d origin, double cellSize)
        {
            this.origin = origin;
            this.cellSize = cellSize;
        }

        /// <summary> number of levels in use (diagnostics) </summary>
        public int LevelCount { get { return levels.Count; } }
        /// <summary> number of triangles in the grid </summary>
        public int Count { get { return count_present; } }

        double level_size(int level)
        {
            return cellSize * Math.Pow(LevelFactor, level);
        }

        // cell range of the box on the level; false if out of the key range (or not finite)
        bool cell_range(AxisAlignedBox3d box, int level, out Vector3i lo, out Vector3i hi)
        {
            double s = level_size(level);
            lo = hi = Vector3i.Zero;
            for (int d = 0; d < 3; ++d) {
                double a = Math.Floor((box.Min[d] - origin[d]) / s), b = Math.Floor((box.Max[d] - origin[d]) / s);
                if (!(a >= -Bias + 1 && b < Bias - 1 && a <= b))
                    return false;
                lo[d] = (int)a; hi[d] = (int)b;
            }
            return true;
        }

        static long count(Vector3i lo, Vector3i hi)
        {
            return (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1) * (hi.z - lo.z + 1);
        }

        static long key(int i, int j, int k)
        {
            return (i + Bias) | ((j + Bias) << 21) | ((k + Bias) << 42);
        }

        static Vector3i unkey(long kk)
        {
            const long mask = (1 << 21) - 1;
            return new Vector3i((int)((kk & mask) - Bias), (int)(((kk >> 21) & mask) - Bias), (int)(((kk >> 42) & mask) - Bias));
        }

        public void Insert(int tid, AxisAlignedBox3d box)
        {
            if (tid < 0)
                throw new ArgumentOutOfRangeException("tid");
            Remove(tid);
            if (tid >= entries.Length)
                Array.Resize(ref entries, Math.Max(tid + 1, 2 * entries.Length));
            Entry e = new Entry() { Present = true, Level = -1, Box = box, Stamp = entries[tid].Stamp };
            for (int level = 0; level < MaxLevels; ++level) {
                Vector3i lo, hi;
                if (cell_range(box, level, out lo, out hi) == false)
                    break;
                if (count(lo, hi) > MaxCellsPerTriangle)
                    continue;
                while (levels.Count <= level)
                    levels.Add(new Dictionary<long, List<int>>());
                var cells = levels[level];
                long[] keys = new long[count(lo, hi)];
                int idx = 0;
                for (int k = lo.z; k <= hi.z; ++k)
                    for (int j = lo.y; j <= hi.y; ++j)
                        for (int i = lo.x; i <= hi.x; ++i) {
                            long kk = key(i, j, k);
                            List<int> list;
                            if (cells.TryGetValue(kk, out list) == false) {
                                list = new List<int>();
                                cells[kk] = list;
                            }
                            list.Add(tid);
                            keys[idx++] = kk;
                        }
                e.Level = level;
                e.Keys = keys;
                break;
            }
            if (e.Level < 0)
                unplaced.Add(tid);
            entries[tid] = e;
            count_present++;
        }

        public void Remove(int tid)
        {
            if (tid < 0 || tid >= entries.Length || entries[tid].Present == false)
                return;
            Entry e = entries[tid];
            entries[tid] = new Entry() { Stamp = e.Stamp };
            count_present--;
            if (e.Level < 0) {
                unplaced.Remove(tid);
                return;
            }
            var cells = levels[e.Level];
            foreach (long kk in e.Keys) {
                List<int> list;
                if (cells.TryGetValue(kk, out list)) {
                    // swap-remove: the order within a cell does not matter
                    int i = list.IndexOf(tid);
                    if (i >= 0) {
                        list[i] = list[list.Count - 1];
                        list.RemoveAt(list.Count - 1);
                    }
                    if (list.Count == 0) cells.Remove(kk);
                }
            }
        }

        /// <summary> the box stored for the triangle </summary>
        public bool TryGetBox(int tid, out AxisAlignedBox3d box)
        {
            if (tid < 0 || tid >= entries.Length || entries[tid].Present == false) {
                box = AxisAlignedBox3d.Empty;
                return false;
            }
            box = entries[tid].Box;
            return true;
        }

        /// <summary>
        /// calls f once for every triangle whose (closed) box intersects the given box, and for every triangle with a
        /// non-finite box; f must not insert or remove triangles
        /// </summary>
        public void Query(AxisAlignedBox3d box, Action<int> f)
        {
            if (++stamp == int.MaxValue) {
                for (int i = 0; i < entries.Length; ++i) entries[i].Stamp = 0;
                stamp = 1;
            }
            for (int level = 0; level < levels.Count; ++level) {
                var cells = levels[level];
                if (cells.Count == 0)
                    continue;
                Vector3i lo, hi;
                bool inRange = cell_range(box, level, out lo, out hi);
                if (inRange == false || count(lo, hi) > cells.Count) {
                    // the box overlaps more cells than are occupied (or leaves the key range): go through
                    // the occupied ones
                    foreach (var kv in cells) {
                        Vector3i c = unkey(kv.Key);
                        if (inRange == false || (c.x >= lo.x && c.x <= hi.x && c.y >= lo.y && c.y <= hi.y && c.z >= lo.z && c.z <= hi.z))
                            visit(kv.Value, ref box, f);
                    }
                    continue;
                }
                for (int k = lo.z; k <= hi.z; ++k)
                    for (int j = lo.y; j <= hi.y; ++j)
                        for (int i = lo.x; i <= hi.x; ++i) {
                            List<int> list;
                            if (cells.TryGetValue(key(i, j, k), out list))
                                visit(list, ref box, f);
                        }
            }
            foreach (int tid in unplaced) f(tid);
        }

        void visit(List<int> list, ref AxisAlignedBox3d box, Action<int> f)
        {
            for (int q = 0; q < list.Count; ++q) {
                int tid = list[q];
                if (entries[tid].Stamp == stamp)
                    continue;
                entries[tid].Stamp = stamp;
                if (overlap(ref entries[tid].Box, ref box))
                    f(tid);
            }
        }

        // closed boxes (touching counts, also flat boxes of axis-aligned triangles)
        static bool overlap(ref AxisAlignedBox3d a, ref AxisAlignedBox3d b)
        {
            return a.Min.x <= b.Max.x && b.Min.x <= a.Max.x && a.Min.y <= b.Max.y && b.Min.y <= a.Max.y
                && a.Min.z <= b.Max.z && b.Min.z <= a.Max.z;
        }
    }
}
