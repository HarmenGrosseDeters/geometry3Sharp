// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Uniform grid of triangle ids by the cells their bounding boxes overlap, with insert and remove, for
    /// meshes that change (eg during a reduction). Triangles overlapping more than MaxCellsPerTriangle
    /// cells are kept in a separate list that every query visits; when that list gets long (the triangles
    /// grew, eg late in a reduction), the grid is rebuilt with larger cells.
    /// </summary>
    public class TriangleHashGrid
    {
        public int MaxCellsPerTriangle = 64;

        Vector3d origin;
        double cellSize;
        Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        Dictionary<int, long[]> triCells = new Dictionary<int, long[]>();
        HashSet<int> large = new HashSet<int>();
        Dictionary<int, AxisAlignedBox3d> boxes = new Dictionary<int, AxisAlignedBox3d>();

        public TriangleHashGrid(Vector3d origin, double cellSize)
        {
            this.origin = origin;
            this.cellSize = cellSize;
        }

        Vector3i cell_of(Vector3d p)
        {
            return new Vector3i((int)Math.Floor((p.x - origin.x) / cellSize), (int)Math.Floor((p.y - origin.y) / cellSize), (int)Math.Floor((p.z - origin.z) / cellSize));
        }

        static long key(int i, int j, int k)
        {
            const long bias = 1 << 20;
            return (i + bias) | ((j + bias) << 21) | ((k + bias) << 42);
        }

        public void Insert(int tid, AxisAlignedBox3d box)
        {
            Remove(tid);
            boxes[tid] = box;
            insert_cells(tid, box);
            if (large.Count > 32 + boxes.Count / 16)
                rebuild(4 * cellSize);
        }

        void rebuild(double newCellSize)
        {
            cellSize = newCellSize;
            cells.Clear(); triCells.Clear(); large.Clear();
            foreach (var kv in boxes)
                insert_cells(kv.Key, kv.Value);
        }

        void insert_cells(int tid, AxisAlignedBox3d box)
        {
            Vector3i lo = cell_of(box.Min), hi = cell_of(box.Max);
            long n = (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1) * (hi.z - lo.z + 1);
            if (n > MaxCellsPerTriangle) {
                large.Add(tid);
                triCells[tid] = null;
                return;
            }
            long[] keys = new long[n];
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
            triCells[tid] = keys;
        }

        public void Remove(int tid)
        {
            long[] keys;
            if (triCells.TryGetValue(tid, out keys) == false)
                return;
            triCells.Remove(tid);
            boxes.Remove(tid);
            if (keys == null) {
                large.Remove(tid);
                return;
            }
            foreach (long kk in keys) {
                List<int> list;
                if (cells.TryGetValue(kk, out list)) {
                    list.Remove(tid);
                    if (list.Count == 0) cells.Remove(kk);
                }
            }
        }

        /// <summary> calls f for every triangle whose cells overlap the box (a triangle can come up more than once) </summary>
        public void Query(AxisAlignedBox3d box, Action<int> f)
        {
            Vector3i lo = cell_of(box.Min), hi = cell_of(box.Max);
            long n = (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1) * (hi.z - lo.z + 1);
            if (n > 4096) {
                foreach (int tid in new List<int>(triCells.Keys)) f(tid);
                return;
            }
            for (int k = lo.z; k <= hi.z; ++k)
                for (int j = lo.y; j <= hi.y; ++j)
                    for (int i = lo.x; i <= hi.x; ++i) {
                        List<int> list;
                        if (cells.TryGetValue(key(i, j, k), out list))
                            foreach (int tid in list.ToArray()) f(tid);
                    }
            foreach (int tid in large) f(tid);
        }
    }
}
