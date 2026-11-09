// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Cut and fill volume per cell of a regular XY lattice (grid cells, block-model columns) between two terrain surfaces
    /// (docs/design/TERRAIN.md section 9.3, "2D columns"). Each surface is a height field (a TIN, or a <see cref="HeightGrid"/>, whose
    /// surface is the grid TIN <see cref="HeightGrid.BuildMesh"/>). The computation is that of <see cref="TerrainVolumeDifference"/>: the
    /// overlay polygons on which both surfaces are linear are additionally clipped by the cell rectangles, split at the zero line of the
    /// difference and integrated exactly (area times the value at the centroid), relative to a local origin.
    /// <para>Accuracy: exact up to double rounding. Every overlay polygon is cut into its cell parts by exact-in-form clipping, so
    /// the sum over all cells of a quantity equals the total of <see cref="TerrainVolumeDifference"/> (the same polygons, split) up to a relative
    /// rounding of about 1e-13 of the sum of the absolute parts (the outside parts are the difference). Swapping the surfaces swaps cut
    /// and fill cell by cell. A part of the footprint outside the lattice is in <see cref="OutsideCutVolume"/> and
    /// <see cref="OutsideFillVolume"/>. The sums over the cells are taken in a fixed order, so the result does not depend on the number of threads.</para>
    /// </summary>
    public class TerrainCellVolumes
    {
        public DMesh3 Before, After;
        /// <summary> the lattice: cell (i, j) is [X0 + i DX, X0 + (i + 1) DX] x [Y0 + j DY, Y0 + (j + 1) DY], index i + NX j </summary>
        public double X0, Y0, DX, DY;
        public int NX, NY;
        public Func<bool> CancelF = () => false;

        // outputs, per cell (index i + NX j)
        /// <summary> volume where After is below Before (material removed) in the cell </summary>
        public double[] CutVolume { get; private set; }
        /// <summary> volume where After is above Before (material added) in the cell </summary>
        public double[] FillVolume { get; private set; }
        public double[] CutArea { get; private set; }
        public double[] FillArea { get; private set; }
        /// <summary> area of the footprint common to both surfaces in the cell </summary>
        public double[] OverlapArea { get; private set; }
        /// <summary> the totals of <see cref="TerrainVolumeDifference"/> over the whole footprint (also outside the lattice) </summary>
        public double TotalCutVolume { get; private set; }
        public double TotalFillVolume { get; private set; }
        public double TotalOverlapArea { get; private set; }
        /// <summary> total minus the sum over the cells: the part outside the lattice (up to rounding) </summary>
        public double OutsideCutVolume { get { return TotalCutVolume - Sum(CutVolume); } }
        public double OutsideFillVolume { get { return TotalFillVolume - Sum(FillVolume); } }
        public int BeforeOverlaps { get; private set; }
        public int AfterOverlaps { get; private set; }
        public bool RequireHeightField = true;

        public TerrainCellVolumes(DMesh3 before, DMesh3 after, double x0, double y0, double dx, double dy, int nx, int ny)
        {
            Before = before; After = after; X0 = x0; Y0 = y0; DX = dx; DY = dy; NX = nx; NY = ny;
        }

        /// <summary> the cells of a grid (NX - 1 by NY - 1) for the surfaces given as TINs </summary>
        public static TerrainCellVolumes ForGridCells(DMesh3 before, DMesh3 after, HeightGrid grid)
        {
            return new TerrainCellVolumes(before, after, grid.X0, grid.Y0, grid.CellSize, grid.CellSize, grid.NX - 1, grid.NY - 1);
        }

        /// <summary> both surfaces as height grids (their grid TINs) </summary>
        public static TerrainCellVolumes Of(HeightGrid before, HeightGrid after, double x0, double y0, double dx, double dy, int nx, int ny)
        {
            return new TerrainCellVolumes(before.BuildMesh(), after.BuildMesh(), x0, y0, dx, dy, nx, ny);
        }

        static double Sum(double[] a)
        {
            double s = 0;
            if (a != null) foreach (double v in a) s += v;
            return s;
        }

        /// <summary> Computes the volumes. Returns false for an empty or invalid lattice, an empty surface, a surface that is no height field (<see cref="RequireHeightField"/>) or a cancel. </summary>
        public bool Compute()
        {
            CutVolume = FillVolume = CutArea = FillArea = OverlapArea = null;
            TotalCutVolume = TotalFillVolume = TotalOverlapArea = 0;
            if (Before == null || After == null || !(DX > 0) || !(DY > 0) || double.IsInfinity(DX) || double.IsInfinity(DY) || NX < 1 || NY < 1 || (long)NX * NY > (1L << 26)
                || double.IsNaN(X0) || double.IsNaN(Y0) || double.IsInfinity(X0) || double.IsInfinity(Y0))
                return false;
            var diff = new TerrainVolumeDifference(Before, After) {
                CancelF = CancelF, RequireHeightField = RequireHeightField,
                Lattice = new TerrainVolumeDifference.CellLattice() { X0 = X0, Y0 = Y0, DX = DX, DY = DY, NX = NX, NY = NY }
            };
            bool ok = diff.Compute();
            BeforeOverlaps = diff.BeforeOverlaps; AfterOverlaps = diff.AfterOverlaps;
            if (!ok) return false;
            int n = NX * NY;
            CutVolume = new double[n]; FillVolume = new double[n]; CutArea = new double[n]; FillArea = new double[n]; OverlapArea = new double[n];
            for (int c = 0; c < n; ++c) {
                FillVolume[c] = diff.CellValues[5 * c]; CutVolume[c] = diff.CellValues[5 * c + 1];
                FillArea[c] = diff.CellValues[5 * c + 2]; CutArea[c] = diff.CellValues[5 * c + 3]; OverlapArea[c] = diff.CellValues[5 * c + 4];
            }
            TotalCutVolume = diff.CutVolume; TotalFillVolume = diff.FillVolume; TotalOverlapArea = diff.OverlapArea;
            return true;
        }
    }
}
