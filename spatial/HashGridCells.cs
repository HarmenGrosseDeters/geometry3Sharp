using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// The cell range of a hash-grid query (PointHashGrid2d/3d, SegmentHashGrid2d). Upstream cast the range ends to int
    /// in the indexer: beyond 2^31 cells (eg UTM coordinates with small cells, or an infinite query point or radius)
    /// .NET 9 and later saturate to int.MaxValue, and `for (i = min; i &lt;= max; i++)` wrapped around forever; .NET
    /// Framework gives int.MinValue, so the range was wrong or reversed and nearby points were missed. A huge range
    /// took as long as its cell count (docs/ROBUSTNESS_SWEEP.md R4). The range is now computed in double; one that is
    /// not finite or leaves the int range scans every occupied cell, one with more cells than are occupied scans the
    /// occupied cells inside it. Scans visit the cells in the loop order (z, then y, then x), so ties go the same way.
    /// </summary>
    internal static class HashGridCells
    {
        /// <summary> the cells of [lo, hi] for cells of the given size; false if not finite or outside the int range (scan all) </summary>
        public static bool Range(Vector3d lo, Vector3d hi, double cellSize, out Vector3i min, out Vector3i max)
        {
            min = max = Vector3i.Zero;
            for (int d = 0; d < 3; ++d) {
                double a = Math.Floor(lo[d] / cellSize), b = Math.Floor(hi[d] / cellSize);
                if (!(a > int.MinValue && b < int.MaxValue && a <= b))
                    return false;
                min[d] = (int)a; max[d] = (int)b;
            }
            return true;
        }

        public static bool Range(Vector2d lo, Vector2d hi, double cellSize, out Vector2i min, out Vector2i max)
        {
            min = max = Vector2i.Zero;
            for (int d = 0; d < 2; ++d) {
                double a = Math.Floor(lo[d] / cellSize), b = Math.Floor(hi[d] / cellSize);
                if (!(a > int.MinValue && b < int.MaxValue && a <= b))
                    return false;
                min[d] = (int)a; max[d] = (int)b;
            }
            return true;
        }

        /// <summary> true if the range has more cells than are occupied (scanning those is cheaper than the loop) </summary>
        public static bool ScanOccupied(Vector3i min, Vector3i max, int occupied) =>
            ((double)max.x - min.x + 1) * ((double)max.y - min.y + 1) * ((double)max.z - min.z + 1) > Math.Max(64, occupied);

        public static bool ScanOccupied(Vector2i min, Vector2i max, int occupied) =>
            ((double)max.x - min.x + 1) * ((double)max.y - min.y + 1) > Math.Max(64, occupied);

        /// <summary> the occupied cells (all, or those in min..max) in loop order: z, then y, then x </summary>
        public static List<Vector3i> Occupied(IEnumerable<Vector3i> cells, bool all, Vector3i min, Vector3i max)
        {
            var result = new List<Vector3i>();
            foreach (Vector3i c in cells)
                if (all || (c.x >= min.x && c.x <= max.x && c.y >= min.y && c.y <= max.y && c.z >= min.z && c.z <= max.z))
                    result.Add(c);
            result.Sort((a, b) => a.z != b.z ? a.z.CompareTo(b.z) : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            return result;
        }

        public static List<Vector2i> Occupied(IEnumerable<Vector2i> cells, bool all, Vector2i min, Vector2i max)
        {
            var result = new List<Vector2i>();
            foreach (Vector2i c in cells)
                if (all || (c.x >= min.x && c.x <= max.x && c.y >= min.y && c.y <= max.y))
                    result.Add(c);
            result.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            return result;
        }
    }
}
