using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>Cut and fill per cell between two surfaces (TerrainCellVolumes): the sum over the cells is the total of TerrainVolumeDifference, analytic cells, swap, invariance, reuse, determinism.</summary>
    public class TerrainCellVolumesTests
    {
        static DMesh3 Grid(double x0, double y0, double x1, double y1, int n, Func<double, double, double> h, bool flip = false)
        {
            DMesh3 mesh = new DMesh3();
            int[,] vid = new int[n, n];
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    double x = x0 + (x1 - x0) * i / (n - 1), y = y0 + (y1 - y0) * j / (n - 1);
                    vid[i, j] = mesh.AppendVertex(new Vector3d(x, y, h(x, y)));
                }
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    if (flip) { mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i, j + 1]); mesh.AppendTriangle(vid[i + 1, j], vid[i + 1, j + 1], vid[i, j + 1]); }
                    else { mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]); mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]); }
                }
            return mesh;
        }

        static TerrainCellVolumes Run(DMesh3 a, DMesh3 b, double x0, double y0, double dx, double dy, int nx, int ny)
        {
            var c = new TerrainCellVolumes(a, b, x0, y0, dx, dy, nx, ny);
            Assert.True(c.Compute());
            return c;
        }

        // two rough surfaces with different triangulations that cross each other
        static (DMesh3 before, DMesh3 after) Pair(double ox = 0, double oy = 0, double oz = 0, double s = 1)
        {
            DMesh3 b = Grid(ox, oy, ox + 20 * s, oy + 20 * s, 14, (x, y) => oz + s * (2 * Math.Sin((x - ox) / s * 0.4) + Math.Cos((y - oy) / s * 0.3)));
            DMesh3 a = Grid(ox, oy, ox + 20 * s, oy + 20 * s, 9, (x, y) => oz + s * (0.15 * (x - ox) / s - 1.5 + Math.Sin((y - oy) / s * 0.5)), true);
            return (b, a);
        }

        static double Sum(double[] v) => v.Sum();

        [Theory]
        [InlineData(1.7, 1.3)]
        [InlineData(5.0, 5.0)]
        [InlineData(0.9, 20.0)]
        [InlineData(33.0, 33.0)]
        public void SumOverTheCells_IsTheTotalOfTerrainVolumeDifference(double dx, double dy)
        {
            var (before, after) = Pair();
            var total = new TerrainVolumeDifference(before, after);
            Assert.True(total.Compute());
            int nx = (int)Math.Ceiling(20 / dx), ny = (int)Math.Ceiling(20 / dy);
            TerrainCellVolumes c = Run(before, after, 0, 0, dx, dy, nx, ny);
            double scale = total.CutVolume + total.FillVolume;
            Assert.True(total.CutVolume > 1 && total.FillVolume > 1, "the surfaces cross");
            Assert.InRange(Math.Abs(Sum(c.CutVolume) - total.CutVolume) / scale, 0, 1e-12);
            Assert.InRange(Math.Abs(Sum(c.FillVolume) - total.FillVolume) / scale, 0, 1e-12);
            Assert.Equal(total.CutArea, Sum(c.CutArea), 9);
            Assert.Equal(total.FillArea, Sum(c.FillArea), 9);
            Assert.Equal(total.OverlapArea, Sum(c.OverlapArea), 9);
            Assert.Equal(total.CutVolume, c.TotalCutVolume); Assert.Equal(total.FillVolume, c.TotalFillVolume);
            Assert.InRange(Math.Abs(c.OutsideCutVolume) / scale, 0, 1e-12);
            // cell areas: the full cells inside the footprint have the cell's area
            Assert.All(Enumerable.Range(0, nx * ny).Where(k => (k % nx + 1) * dx <= 20 && (k / nx + 1) * dy <= 20), k => Assert.Equal(dx * dy, c.OverlapArea[k], 9));
        }

        [Fact]
        public void Cells_AreAnalytic_ForAPlaneAgainstAPlane()
        {
            // before z = 0, after z = x over [-5, 5]^2: per cell [a,b] x [c,d]: fill (b^2 - a^2) / 2 (d - c) where x > 0, cut the same where x < 0
            DMesh3 before = Grid(-5, -5, 5, 5, 2, (x, y) => 0), after = Grid(-5, -5, 5, 5, 7, (x, y) => x, true);
            double d = 2.5;
            TerrainCellVolumes c = Run(before, after, -5, -5, d, d, 4, 4);
            for (int j = 0; j < 4; ++j)
                for (int i = 0; i < 4; ++i) {
                    double a = -5 + i * d, b = a + d, h = (b * b - a * a) / 2 * d;
                    int k = i + 4 * j;
                    if (a >= 0) { Assert.Equal(h, c.FillVolume[k], 12); Assert.Equal(0, c.CutVolume[k], 12); Assert.Equal(d * d, c.FillArea[k], 12); }
                    else { Assert.Equal(-h, c.CutVolume[k], 12); Assert.Equal(0, c.FillVolume[k], 12); Assert.Equal(d * d, c.CutArea[k], 12); }
                }
            Assert.Equal(0, c.OutsideCutVolume, 12);
        }

        [Fact]
        public void SwappingTheSurfaces_SwapsCutAndFillInEveryCell()
        {
            var (before, after) = Pair();
            TerrainCellVolumes ab = Run(before, after, 0, 0, 3.3, 2.9, 7, 7), ba = Run(after, before, 0, 0, 3.3, 2.9, 7, 7);
            for (int k = 0; k < 49; ++k) {
                Assert.Equal(ab.CutVolume[k], ba.FillVolume[k], 10); Assert.Equal(ab.FillVolume[k], ba.CutVolume[k], 10);
                Assert.Equal(ab.CutArea[k], ba.FillArea[k], 10); Assert.Equal(ab.OverlapArea[k], ba.OverlapArea[k], 10);
            }
        }

        [Fact]
        public void ALatticeSmallerThanTheFootprint_LeavesTheRestOutside()
        {
            var (before, after) = Pair();
            var total = new TerrainVolumeDifference(before, after); Assert.True(total.Compute());
            TerrainCellVolumes c = Run(before, after, 5, 5, 4, 4, 2, 2);        // [5, 13]^2
            Assert.True(Sum(c.CutVolume) + Sum(c.FillVolume) < total.CutVolume + total.FillVolume);
            Assert.Equal(total.CutVolume, Sum(c.CutVolume) + c.OutsideCutVolume, 9);
            Assert.True(c.OutsideCutVolume + c.OutsideFillVolume > 0);
            // a lattice that misses the footprint, and one that covers more than it
            TerrainCellVolumes far = Run(before, after, 100, 100, 5, 5, 3, 3);
            Assert.Equal(0, Sum(far.OverlapArea)); Assert.Equal(total.CutVolume, far.OutsideCutVolume, 9);
            TerrainCellVolumes big = Run(before, after, -50, -50, 10, 10, 12, 12);
            Assert.Equal(total.CutVolume, Sum(big.CutVolume), 9);
            Assert.Equal(400, Sum(big.OverlapArea), 9);
        }

        [Fact]
        public void EdgeCells_GetOnlyTheirOwnPart_NotThePartOutsideTheLattice()
        {
            // before z = 0, after z = 1 over [0, 10]^2 (two triangles each, so a polygon spans the whole square): fill = area of the cell
            DMesh3 before = Grid(0, 0, 10, 10, 2, (x, y) => 0), after = Grid(0, 0, 10, 10, 2, (x, y) => 1, true);
            // a lattice [-3, 5] x [-3, 5] of 4 x 4 cells of 2: only the cells at x, y >= 0 have a footprint; the overlay polygons cross the lattice border
            TerrainCellVolumes c = Run(before, after, -3, -3, 2, 2, 4, 4);
            for (int j = 0; j < 4; ++j)
                for (int i = 0; i < 4; ++i) {
                    double wx = Math.Max(0, Math.Min(-3 + 2 * (i + 1), 10) - Math.Max(-3 + 2 * i, 0)), wy = Math.Max(0, Math.Min(-3 + 2 * (j + 1), 10) - Math.Max(-3 + 2 * j, 0));
                    Assert.Equal(wx * wy, c.FillVolume[i + 4 * j], 12);
                    Assert.Equal(wx * wy, c.OverlapArea[i + 4 * j], 12);
                }
            Assert.Equal(75, c.OutsideFillVolume, 9);      // the cells cover [0, 5]^2 = 25 of the 100
        }

        [Fact]
        public void ASingleCellInsideManySmallPolygons_GetsExactlyItsOwnArea()
        {
            // surfaces with triangles of size 1 on the integer lattice, the single cell [0.5, 1.5]^2 cuts four of their cells: every polygon crosses the lattice border
            DMesh3 before = Grid(0, 0, 10, 10, 11, (x, y) => 0), after = Grid(0, 0, 10, 10, 11, (x, y) => 2, true);
            TerrainCellVolumes c = Run(before, after, 0.5, 0.5, 1, 1, 1, 1);
            Assert.Equal(1, c.OverlapArea[0], 12);
            Assert.Equal(2, c.FillVolume[0], 12);
            Assert.Equal(198, c.OutsideFillVolume, 9);
        }

        [Fact]
        public void HeightGrids_AreTheirGridTins()
        {
            var g1 = new HeightGrid(Enumerable.Range(0, 81).Select(k => Math.Sin(k * 0.3)).ToArray(), 9, 9, 100, 200, 2);
            var g2 = new HeightGrid(Enumerable.Range(0, 81).Select(k => Math.Cos(k * 0.2) * 0.8).ToArray(), 9, 9, 100, 200, 2);
            g2[3, 3] = double.NaN;
            TerrainCellVolumes c = TerrainCellVolumes.Of(g1, g2, 100, 200, 2, 2, 8, 8);
            Assert.True(c.Compute());
            var total = new TerrainVolumeDifference(g1.BuildMesh(), g2.BuildMesh()); Assert.True(total.Compute());
            Assert.Equal(total.CutVolume, Sum(c.CutVolume), 10); Assert.Equal(total.FillVolume, Sum(c.FillVolume), 10);
            // the cells next to the no-data node have no overlap (the 4 cells around node (3,3))
            foreach (var (i, j) in new[] { (2, 2), (3, 2), (2, 3), (3, 3) }) Assert.Equal(0, c.OverlapArea[i + 8 * j]);
            Assert.Equal(4 * 60, Sum(c.OverlapArea), 9);
            // ForGridCells: the cells of the grid
            TerrainCellVolumes f = TerrainCellVolumes.ForGridCells(g1.BuildMesh(), g2.BuildMesh(), g1);
            f.Compute();
            Assert.Equal(c.CutVolume, f.CutVolume);
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Invariance_ScaleAndTranslation(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) return;       // cells are axis aligned
            var (b0, a0) = Pair();
            TerrainCellVolumes ref0 = Run(b0, a0, 1, 2, 3.3, 2.9, 7, 7);
            var (b1, a1) = (Map(b0, s), Map(a0, s));
            TerrainCellVolumes c = Run(b1, a1, s.Scale * 1 + s.Translation.x, s.Scale * 2 + s.Translation.y, s.Scale * 3.3, s.Scale * 2.9, 7, 7);
            double s3 = s.Scale * s.Scale * s.Scale, s2 = s.Scale * s.Scale;
            for (int k = 0; k < 49; ++k) {
                if (s.IsExact) {      // a power of two: bit for bit
                    Assert.Equal(s3 * ref0.CutVolume[k], c.CutVolume[k]); Assert.Equal(s3 * ref0.FillVolume[k], c.FillVolume[k]);
                    Assert.Equal(s2 * ref0.OverlapArea[k], c.OverlapArea[k]);
                } else {
                    double tol = 1e-8 * s3 * (1 + Math.Abs(s.Translation.x + s.Translation.y) / 1e3);
                    Assert.InRange(Math.Abs(c.CutVolume[k] - s3 * ref0.CutVolume[k]), 0, tol); Assert.InRange(Math.Abs(c.FillVolume[k] - s3 * ref0.FillVolume[k]), 0, tol);
                }
            }
        }

        static DMesh3 Map(DMesh3 m, Similarity s)
        {
            var r = new DMesh3(m);
            foreach (int v in r.VertexIndices()) r.SetVertex(v, s.Apply(r.GetVertex(v)));
            return r;
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Fact]
        public void Compute_Twice_AndRepeated_GiveTheSameBits()
        {
            var (before, after) = Pair();
            var c = new TerrainCellVolumes(before, after, 0, 0, 1.7, 1.3, 12, 16);
            Assert.True(c.Compute());
            double[] cut = (double[])c.CutVolume.Clone(), fill = (double[])c.FillVolume.Clone();
            Assert.True(c.Compute());
            Assert.Equal(cut, c.CutVolume); Assert.Equal(fill, c.FillVolume);
            for (int k = 0; k < 5; ++k) {
                var u = Run(before, after, 0, 0, 1.7, 1.3, 12, 16);
                Assert.Equal(cut, u.CutVolume); Assert.Equal(fill, u.FillVolume);
            }
        }

        [Fact]
        public void InvalidInput_ReturnsFalse()
        {
            var (before, after) = Pair();
            Assert.False(new TerrainCellVolumes(before, after, 0, 0, 0, 1, 2, 2).Compute());
            Assert.False(new TerrainCellVolumes(before, after, 0, 0, 1, double.NaN, 2, 2).Compute());
            Assert.False(new TerrainCellVolumes(before, after, 0, 0, 1, 1, 0, 2).Compute());
            Assert.False(new TerrainCellVolumes(before, after, double.NaN, 0, 1, 1, 2, 2).Compute());
            Assert.False(new TerrainCellVolumes(before, new DMesh3(), 0, 0, 1, 1, 2, 2).Compute());
            // overlapping triangles in a surface: no height field
            DMesh3 twice = new DMesh3(before);
            new MeshEditor(twice).AppendMesh(Grid(0.3, 0.3, 20.3, 20.3, 5, (x, y) => 0));
            var bad = new TerrainCellVolumes(twice, after, 0, 0, 1, 1, 2, 2);
            Assert.False(bad.Compute());
            Assert.True(bad.BeforeOverlaps > 0);
        }

        // ---- 3D blocks (elevation bands) ----

        static TerrainCellVolumes RunBands(DMesh3 a, DMesh3 b, double[] bands, double x0 = 1, double y0 = 2, double dx = 3.3, double dy = 2.9, int nx = 7, int ny = 7)
        {
            var c = new TerrainCellVolumes(a, b, x0, y0, dx, dy, nx, ny) { BandBoundaries = bands };
            Assert.True(c.Compute());
            return c;
        }

        static readonly double[] AllHeights = { double.NegativeInfinity, double.PositiveInfinity };

        [Fact]
        public void OneBandOverAllHeights_IsThe2DCellResult()
        {
            var (before, after) = Pair();
            TerrainCellVolumes c = RunBands(before, after, AllHeights), plain = Run(before, after, 1, 2, 3.3, 2.9, 7, 7);
            Assert.Equal(1, c.BandCount);
            Assert.Equal(49, c.BlockCutVolume.Length);
            Assert.True(Sum(plain.CutVolume) > 1 && Sum(plain.FillVolume) > 1);
            for (int k = 0; k < 49; ++k) {
                double tol = 1e-12 * (plain.CutVolume[k] + plain.FillVolume[k] + 1e-3);
                Assert.InRange(Math.Abs(c.BlockCutVolume[k] - plain.CutVolume[k]), 0, tol);
                Assert.InRange(Math.Abs(c.BlockFillVolume[k] - plain.FillVolume[k]), 0, tol);
            }
        }

        [Fact]
        public void TheBandsDoNotChangeThe2DOutputs_Bitwise()
        {
            var (before, after) = Pair();
            TerrainCellVolumes c = RunBands(before, after, new[] { -1.0, 0.5, 2.0 }), plain = Run(before, after, 1, 2, 3.3, 2.9, 7, 7);
            Assert.Equal(plain.CutVolume, c.CutVolume); Assert.Equal(plain.FillVolume, c.FillVolume);
            Assert.Equal(plain.CutArea, c.CutArea); Assert.Equal(plain.FillArea, c.FillArea); Assert.Equal(plain.OverlapArea, c.OverlapArea);
            Assert.Equal(plain.TotalCutVolume, c.TotalCutVolume); Assert.Equal(plain.TotalFillVolume, c.TotalFillVolume);
            Assert.Null(plain.BlockCutVolume); Assert.Equal(0, plain.BandCount);
        }

        [Fact]
        public void TheSumOverTheBands_IsThe2DCellResult()
        {
            var (before, after) = Pair();
            // the surfaces lie in [-4, 4]; bands that cut through the slopes, with open ends
            double[] bands = { double.NegativeInfinity, -2.5, -1.1, 0, 0.3, 1.7, double.PositiveInfinity };
            TerrainCellVolumes c = RunBands(before, after, bands), plain = Run(before, after, 1, 2, 3.3, 2.9, 7, 7);
            Assert.Equal(6, c.BandCount);
            for (int k = 0; k < 49; ++k) {
                double cut = 0, fill = 0;
                for (int q = 0; q < 6; ++q) { cut += c.BlockCutVolume[q * 49 + k]; fill += c.BlockFillVolume[q * 49 + k]; }
                double tol = 1e-12 * (plain.CutVolume[k] + plain.FillVolume[k] + 1e-3);
                Assert.InRange(Math.Abs(cut - plain.CutVolume[k]), 0, tol);
                Assert.InRange(Math.Abs(fill - plain.FillVolume[k]), 0, tol);
            }
            Assert.Contains(Enumerable.Range(0, 6), q => Enumerable.Range(0, 49).Any(k => c.BlockCutVolume[q * 49 + k] > 0) && Enumerable.Range(0, 49).Any(k => c.BlockFillVolume[q * 49 + k] > 0));
        }

        [Fact]
        public void ASurfaceCrossingABandBoundary_IsSplitExactly()
        {
            // before z = 0, after z = x over [0, 8] x [0, 4] (fill), one cell: the column at x has the height x. Band [2, 5]: thickness clamp(x, 2, 5) - 2 for x
            // in [0, 8] with the clamp of 0: 0 for x <= 2, x - 2 up to 5 (3), then 3. Integral over x: (3 * 3 / 2) + 3 * 3 = 13.5, times the width 4 = 54
            DMesh3 before = Grid(0, 0, 8, 4, 2, (x, y) => 0), after = Grid(0, 0, 8, 4, 5, (x, y) => x, true);
            TerrainCellVolumes c = RunBands(before, after, new[] { 2.0, 5.0 }, 0, 0, 8, 4, 1, 1);
            Assert.Equal(54, c.BlockFillVolume[0], 12); Assert.Equal(0, c.BlockCutVolume[0], 12);
            // three bands: [-inf, 2] holds x^2/2 up to 2 and then 2 * 6: 2 * 4 ... exact per band: [-inf, 2]: (2 + 12) * 4 = 56, [2, 5]: 54, [5, inf]: (3 * 3 / 2) * 4 = 18
            TerrainCellVolumes d = RunBands(before, after, new[] { double.NegativeInfinity, 2.0, 5.0, double.PositiveInfinity }, 0, 0, 8, 4, 1, 1);
            Assert.Equal(56, d.BlockFillVolume[0], 12); Assert.Equal(54, d.BlockFillVolume[1], 12); Assert.Equal(18, d.BlockFillVolume[2], 12);
            // the same surfaces below zero: cut, mirrored
            DMesh3 down = Grid(0, 0, 8, 4, 5, (x, y) => -x, true);
            TerrainCellVolumes e = RunBands(before, down, new[] { -5.0, -2.0 }, 0, 0, 8, 4, 1, 1);
            Assert.Equal(54, e.BlockCutVolume[0], 12); Assert.Equal(0, e.BlockFillVolume[0], 12);
        }

        [Fact]
        public void BothSurfacesInsideOneBand_AndAColumnAcrossTwoBands()
        {
            // before z = 1, after z = 3 (fill thickness 2): bands [0, 2], [2, 4]: 1 each; the band [2.5, 4] holds 0.5, [4, 6] nothing
            DMesh3 before = Grid(0, 0, 4, 4, 2, (x, y) => 1), after = Grid(0, 0, 4, 4, 3, (x, y) => 3, true);
            TerrainCellVolumes c = RunBands(before, after, new[] { 0.0, 2.0, 2.5, 4.0, 6.0 }, 0, 0, 4, 4, 1, 1);
            Assert.Equal(16.0, c.BlockFillVolume[0], 12); Assert.Equal(8.0, c.BlockFillVolume[1], 12);
            Assert.Equal(8.0, c.BlockFillVolume[2], 12); Assert.Equal(0, c.BlockFillVolume[3], 12);
            Assert.All(c.BlockCutVolume, v => Assert.Equal(0, v));
        }

        [Fact]
        public void SwappingTheSurfaces_SwapsCutAndFillInEveryBlock()
        {
            var (before, after) = Pair();
            double[] bands = { double.NegativeInfinity, -1.2, 0.4, 1.9, double.PositiveInfinity };
            TerrainCellVolumes ab = RunBands(before, after, bands), ba = RunBands(after, before, bands);
            for (int k = 0; k < 4 * 49; ++k) {
                Assert.Equal(ab.BlockCutVolume[k], ba.BlockFillVolume[k], 10); Assert.Equal(ab.BlockFillVolume[k], ba.BlockCutVolume[k], 10);
            }
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Blocks_Invariance_ScaleAndZTranslation(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) return;       // cells are axis aligned
            var (b0, a0) = Pair();
            double[] bands = { -2.5, -0.7, 0.4, 1.9 };
            TerrainCellVolumes ref0 = RunBands(b0, a0, bands, 1, 2, 3.3, 2.9, 7, 7);
            var (b1, a1) = (Map(b0, s), Map(a0, s));
            double[] mapped = bands.Select(z => s.Scale * z + s.Translation.z).ToArray();       // the bands move with the surfaces
            TerrainCellVolumes c = RunBands(b1, a1, mapped, s.Scale * 1 + s.Translation.x, s.Scale * 2 + s.Translation.y, s.Scale * 3.3, s.Scale * 2.9, 7, 7);
            double s3 = s.Scale * s.Scale * s.Scale;
            for (int k = 0; k < 3 * 49; ++k) {
                if (s.IsExact) {      // a power of two: bit for bit
                    Assert.Equal(s3 * ref0.BlockCutVolume[k], c.BlockCutVolume[k]); Assert.Equal(s3 * ref0.BlockFillVolume[k], c.BlockFillVolume[k]);
                } else {
                    double tol = 1e-8 * s3 * (1 + Math.Abs(s.Translation.x + s.Translation.y + s.Translation.z) / 1e3);
                    Assert.InRange(Math.Abs(c.BlockCutVolume[k] - s3 * ref0.BlockCutVolume[k]), 0, tol); Assert.InRange(Math.Abs(c.BlockFillVolume[k] - s3 * ref0.BlockFillVolume[k]), 0, tol);
                }
            }
        }

        [Fact]
        public void Blocks_Compute_Twice_AndRepeated_GiveTheSameBits()
        {
            var (before, after) = Pair();
            var c = new TerrainCellVolumes(before, after, 0, 0, 1.7, 1.3, 12, 16) { BandBoundaries = new[] { -3.0, -0.5, 1.1, 3.0 } };
            Assert.True(c.Compute());
            double[] cut = (double[])c.BlockCutVolume.Clone(), fill = (double[])c.BlockFillVolume.Clone();
            Assert.True(c.Compute());
            Assert.Equal(cut, c.BlockCutVolume); Assert.Equal(fill, c.BlockFillVolume);
            for (int k = 0; k < 5; ++k) {
                var u = RunBands(before, after, c.BandBoundaries, 0, 0, 1.7, 1.3, 12, 16);
                Assert.Equal(cut, u.BlockCutVolume); Assert.Equal(fill, u.BlockFillVolume);
            }
            // switching the bands off on the same instance drops the blocks
            c.BandBoundaries = null;
            Assert.True(c.Compute());
            Assert.Null(c.BlockCutVolume); Assert.Equal(0, c.BandCount);
        }

        [Fact]
        public void Blocks_ParallelEqualsSerial()
        {
            var (before, after) = Pair();
            double[] bands = { -3.0, -0.5, 1.1, 3.0 };
            TerrainCellVolumes par = RunBands(before, after, bands, 0, 0, 1.7, 1.3, 12, 16), ser;
            gParallel.SerialOnThisThread = true;
            try { ser = RunBands(before, after, bands, 0, 0, 1.7, 1.3, 12, 16); }
            finally { gParallel.SerialOnThisThread = false; }
            Assert.Equal(ser.BlockCutVolume, par.BlockCutVolume); Assert.Equal(ser.BlockFillVolume, par.BlockFillVolume);
        }

        [Fact]
        public void InvalidBands_ReturnFalse()
        {
            var (before, after) = Pair();
            foreach (double[] bad in new[] { new double[0], new[] { 1.0 }, new[] { 1.0, 1.0 }, new[] { 2.0, 1.0 }, new[] { 0.0, double.NaN }, new[] { double.PositiveInfinity, double.NegativeInfinity } }) {
                var c = new TerrainCellVolumes(before, after, 0, 0, 5, 5, 4, 4) { BandBoundaries = bad };
                Assert.False(c.Compute());
                Assert.Null(c.BlockCutVolume);
            }
            // bands times cells above the cap
            Assert.False(new TerrainCellVolumes(before, after, 0, 0, 0.02, 0.02, 1000, 1000) { BandBoundaries = new[] { 0.0, 1.0, 2.0 } }.Compute());
        }

        [Fact]
        public void The2DCellValues_ArePinnedToTheBitsBeforeTheBlocksRefactor()
        {
            // bit patterns computed with the code before integrate was split into integrate_diff (master at the time of the 3D blocks PR)
            var (before, after) = Pair();
            var c = Run(before, after, 1, 2, 3.3, 2.9, 7, 7);
            var pins = new (int k, long cut, long fill, long cutArea, long overlap)[] {
                (0, 0x40369D003A111C14, 0, 0x402323D70A3D70A4, 0x402323D70A3D70A4), (8, 0x402E02AD0FB8A995, 0, 0x402323D70A3D70A3, 0x402323D70A3D70A3),
                (17, 0, 0x4034ED71E6B39F67, 0, 0x402323D70A3D70A4), (24, 0, 0x403A8723B8F622EA, 0, 0x402323D70A3D70A6),
                (30, 0x3EFB6C081997FE39, 0x40311C3C9E444C49, 0x3F609515BD9B38C1, 0x402323D70A3D70A3), (40, 0x401391F25273EC36, 0x3FAF3B52EFD686B2, 0x401ACF2129C16D4A, 0x401CFFFFFFFFFFFC) };
            foreach (var p in pins) {
                Assert.Equal(p.cut, BitConverter.DoubleToInt64Bits(c.CutVolume[p.k])); Assert.Equal(p.fill, BitConverter.DoubleToInt64Bits(c.FillVolume[p.k]));
                Assert.Equal(p.cutArea, BitConverter.DoubleToInt64Bits(c.CutArea[p.k])); Assert.Equal(p.overlap, BitConverter.DoubleToInt64Bits(c.OverlapArea[p.k]));
            }
            Assert.Equal(0x40748FA11DD0CA38, BitConverter.DoubleToInt64Bits(c.TotalCutVolume)); Assert.Equal(0x4072C1601B0322D7, BitConverter.DoubleToInt64Bits(c.TotalFillVolume));
        }

        [Fact]
        public void ASurfaceExactlyOnABandBoundary_AddsNothingToTheBandAbove()
        {
            // before z = 1, after z = 3 over 4 x 4: the top surface lies exactly at the boundary 3
            DMesh3 before = Grid(0, 0, 4, 4, 2, (x, y) => 1), after = Grid(0, 0, 4, 4, 3, (x, y) => 3, true);
            TerrainCellVolumes c = RunBands(before, after, new[] { 1.0, 3.0, 5.0 }, 0, 0, 4, 4, 1, 1);
            Assert.Equal(32.0, c.BlockFillVolume[0], 12); Assert.Equal(0, c.BlockFillVolume[1]);
        }
    }
}
