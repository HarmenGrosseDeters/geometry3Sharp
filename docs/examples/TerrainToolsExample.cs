using System.Collections.Generic;
using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Terrain engineering tools: error-certified DGM-to-TIN simplification (TerrainGridTin),
    /// per-cell cut/fill volumes (TerrainCellVolumes), and LiDAR ground filtering (ClothSimulationFilter).
    /// </summary>
    public static class TerrainToolsExample
    {
        /// <summary>
        /// Builds a regular height grid of a gentle parabolic bowl and simplifies it into a TIN
        /// with a guaranteed exact vertical error bound (SurfaceError &lt;= Epsilon).
        /// </summary>
        public static TerrainGridTin GridToCertifiedTin(double epsilon)
        {
            int nx = 17, ny = 17;
            var z = new double[nx * ny];
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    double dx = i - 8.0, dy = j - 8.0;
                    z[i + nx * j] = 0.02 * (dx * dx + dy * dy);
                }
            var grid = new HeightGrid(z, nx, ny, 0, 0, 1.0);
            var tin = new TerrainGridTin { Grid = grid, Epsilon = epsilon };
            tin.Compute();
            return tin;
        }

        /// <summary>
        /// Computes exact cut and fill volumes per cell of a 2x2 lattice between a flat ground at z = 10
        /// and a linear ramp from z = 8 at x = 0 through z = 10 at x = 5 to z = 12 at x = 10.
        /// </summary>
        public static TerrainCellVolumes CellCutFillVolumes()
        {
            var before = new HeightGrid(new double[9], 3, 3, 0, 0, 5.0);
            var after = new HeightGrid(new double[9], 3, 3, 0, 0, 5.0);
            for (int j = 0; j < 3; ++j)
                for (int i = 0; i < 3; ++i) {
                    before[i, j] = 10.0;
                    after[i, j] = i == 0 ? 8.0 : (i == 2 ? 12.0 : 10.0);
                }

            var cv = TerrainCellVolumes.Of(before, after, x0: 0, y0: 0, dx: 5.0, dy: 5.0, nx: 2, ny: 2);
            cv.Compute();
            return cv;
        }

        /// <summary>
        /// Separates ground points (z = 0) from elevated building/canopy points (z = 5)
        /// using ClothSimulationFilter.
        /// </summary>
        public static ClothSimulationFilter ClassifyGround(out int groundInputCount)
        {
            var points = new List<Vector3d>();
            for (int j = 0; j <= 10; ++j)
                for (int i = 0; i <= 10; ++i)
                    points.Add(new Vector3d(i, j, 0.0));
            groundInputCount = points.Count;
            // add elevated points representing a roof above the center
            for (int j = 4; j <= 6; ++j)
                for (int i = 4; i <= 6; ++i)
                    points.Add(new Vector3d(i, j, 5.0));

            var csf = new ClothSimulationFilter(points) { ClothResolution = 1.0, ClassThreshold = 0.5 };
            csf.Compute();
            return csf;
        }
    }
}
