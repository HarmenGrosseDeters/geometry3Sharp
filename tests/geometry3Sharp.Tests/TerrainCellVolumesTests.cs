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
    }
}
