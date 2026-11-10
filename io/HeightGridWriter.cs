using System;
using System.Globalization;
using System.IO;

namespace g3
{
    /// <summary>
    /// Writers for <see cref="HeightGrid"/>, the inverse of <see cref="HeightGridReader"/>. ESRI ASCII round trips
    /// exactly (origin, cell size and heights are written as given). XYZ round trips the heights and the node positions
    /// up to rounding of the cell size (the reader takes the spacing from the extent), and only grids whose border rows
    /// and columns hold data: an all-NaN border has no points to recover it from.
    /// </summary>
    public static class HeightGridWriter
    {
        static string R(double v) { return MeshIOUtil.FormatDouble(v); }

        /// <summary> "x y z" per line, south row first; nodes without data are not written (the reader makes them gaps again) </summary>
        public static void WriteXyz(TextWriter w, HeightGrid grid)
        {
            foreach (double z0 in grid.Z)
                if (double.IsInfinity(z0)) throw new ArgumentException("the grid holds an infinite height");
            for (int j = 0; j < grid.NY; ++j)
                for (int i = 0; i < grid.NX; ++i) {
                    double z = grid[i, j];
                    if (!double.IsNaN(z))
                        w.Write(R(grid.X(i)) + " " + R(grid.Y(j)) + " " + R(z) + "\n");
                }
        }

        /// <summary> ESRI ASCII Grid with xllcenter / yllcenter (exact) and NODATA_value </summary>
        public static void WriteEsriAscii(TextWriter w, HeightGrid grid, double noData = -9999)
        {
            if (!(Math.Abs(noData) < double.PositiveInfinity)) throw new ArgumentException("noData must be finite", nameof(noData));
            foreach (double z0 in grid.Z)
                if (double.IsInfinity(z0)) throw new ArgumentException("the grid holds an infinite height");
                else if (z0 == noData) throw new ArgumentException("a height equals the NODATA value " + R(noData) + ": pass another noData", nameof(noData));
            w.Write("ncols " + grid.NX + "\nnrows " + grid.NY + "\nxllcenter " + R(grid.X0) + "\nyllcenter " + R(grid.Y0)
                + "\ncellsize " + R(grid.CellSize) + "\nNODATA_value " + R(noData) + "\n");
            for (int j = grid.NY - 1; j >= 0; --j) {
                for (int i = 0; i < grid.NX; ++i) {
                    double z = grid[i, j];
                    w.Write(i == 0 ? "" : " ");
                    w.Write(R(double.IsNaN(z) ? noData : z));
                }
                w.Write("\n");
            }
        }
    }
}
