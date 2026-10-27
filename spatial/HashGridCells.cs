using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// The cell range of a hash-grid query (PointHashGrid2d/3d, SegmentHashGrid2d). A range that reaches int.MaxValue
    /// (cell indices beyond 2^31 saturate there on .NET 9 and later, eg UTM coordinates with small cells, or an
    /// infinite query) made `for (i = min; i &lt;= max; i++)` wrap around forever, and a huge range (a large radius)
    /// took as long as its cell count (docs/ROBUSTNESS_SWEEP.md R4). Such ranges scan the occupied cells instead.
    /// </summary>
    internal static class HashGridCells
    {
        /// <summary> true if the query should scan the occupied cells instead of looping over the range </summary>
        public static bool ScanOccupied(Vector3i min, Vector3i max, int occupied)
        {
            if (max.x == int.MaxValue || max.y == int.MaxValue || max.z == int.MaxValue)
                return true;
            double cells = ((double)max.x - min.x + 1) * ((double)max.y - min.y + 1) * ((double)max.z - min.z + 1);
            return cells > Math.Max(64, occupied);
        }

        public static bool ScanOccupied(Vector2i min, Vector2i max, int occupied)
        {
            if (max.x == int.MaxValue || max.y == int.MaxValue)
                return true;
            double cells = ((double)max.x - min.x + 1) * ((double)max.y - min.y + 1);
            return cells > Math.Max(64, occupied);
        }

        public static bool Contains(Vector3i min, Vector3i max, Vector3i idx) =>
            idx.x >= min.x && idx.x <= max.x && idx.y >= min.y && idx.y <= max.y && idx.z >= min.z && idx.z <= max.z;

        public static bool Contains(Vector2i min, Vector2i max, Vector2i idx) =>
            idx.x >= min.x && idx.x <= max.x && idx.y >= min.y && idx.y <= max.y;
    }
}
