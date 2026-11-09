using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>HeightGrid, the XYZ / ESRI ASCII readers and writers, and the grid TIN (procedural grids only).</summary>
    public class HeightGridTests
    {
        /// <summary>A hilly grid; with gaps: a hole in the middle and a missing corner node.</summary>
        static HeightGrid Procedural(int nx, int ny, double x0, double y0, double cell, bool gaps)
        {
            var g = new HeightGrid(new double[nx * ny], nx, ny, x0, y0, cell);
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i)
                    g[i, j] = 100 + 3 * Math.Sin(0.7 * i) + 2 * Math.Cos(0.4 * j) + 0.25 * i - 0.125 * j + 0.4 * Math.Sin(0.9 * i * j + 1);   // (the last term twists the cells: no cell is planar)
            if (gaps) {
                g[nx / 2, ny / 2] = double.NaN;
                g[nx / 2 + 1, ny / 2] = double.NaN;
                g[nx - 1, ny - 1] = double.NaN;
            }
            return g;
        }

        static string XyzText(HeightGrid g, bool shuffle = false, int seed = 1)
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            HeightGridWriter.WriteXyz(sw, g);
            if (!shuffle) return sw.ToString();
            var lines = sw.ToString().Split('\n').Where(l => l != "").ToList();
            var rand = new Random(seed);
            return string.Join("\n", lines.OrderBy(l => rand.Next())) + "\n";
        }

        static string EsriText(HeightGrid g, double noData = -9999)
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            HeightGridWriter.WriteEsriAscii(sw, g, noData);
            return sw.ToString();
        }

        static void AssertSameGrid(HeightGrid expected, HeightGrid actual)
        {
            Assert.Equal(expected.NX, actual.NX);
            Assert.Equal(expected.NY, actual.NY);
            Assert.Equal(expected.X0, actual.X0);
            Assert.Equal(expected.Y0, actual.Y0);
            Assert.Equal(expected.CellSize, actual.CellSize);
            for (int k = 0; k < expected.Z.Length; ++k)
                Assert.True(expected.Z[k].Equals(actual.Z[k]), $"node {k}: {expected.Z[k]} vs {actual.Z[k]}");
        }

        static HeightGridReadResult ReadXyz(string text, HeightGridReadOptions o = null) => HeightGridReader.ReadXyz(new StringReader(text), o);
        static HeightGridReadResult ReadEsri(string text, HeightGridReadOptions o = null) => HeightGridReader.ReadEsriAscii(new StringReader(text), o);

        // ---- XYZ ----

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Xyz_RoundTrip_FindsTheGrid_SortedOrNot_WithGaps(bool gaps, bool shuffle)
        {
            HeightGrid g = Procedural(9, 7, 350000.5, 5600001.25, 0.5, gaps);
            HeightGridReadResult r = ReadXyz(XyzText(g, shuffle));
            Assert.True(r.Ok, r.Message);
            AssertSameGrid(g, r.Grid);
            if (gaps) Assert.Equal(g.Z.Length - 3, r.Grid.ValidNodeCount());
        }

        [Fact]
        public void Xyz_AcceptsGeobasisStyleLinesAndOtherSeparators()
        {
            // NRW dgm1_xyz: "x y z" with two decimals; here with CRLF, tabs, commas, an extra column and a comment
            string text = "# comment\r\n350000.50 5600000.50 100.25\r\n350001.50\t5600000.50\t101.5\r\n"
                + "350000.50,5600001.50,102.75,7\r\n\r\n350001.50;5600001.50;103.0\r\n";
            HeightGridReadResult r = ReadXyz(text);
            Assert.True(r.Ok, r.Message);
            Assert.Equal(2, r.Grid.NX);
            Assert.Equal(2, r.Grid.NY);
            Assert.Equal(1.0, r.Grid.CellSize);
            Assert.Equal(350000.5, r.Grid.X0);
            Assert.Equal(5600000.5, r.Grid.Y0);
            Assert.Equal(new[] { 100.25, 101.5, 102.75, 103.0 }, r.Grid.Z);
        }

        [Fact]
        public void Xyz_NoDataValueBecomesNaN()
        {
            HeightGrid g = Procedural(4, 4, 0, 0, 1, false);
            g[2, 1] = -9999;
            HeightGridReadResult r = ReadXyz(XyzText(g), new HeightGridReadOptions { NoDataValue = -9999 });
            Assert.True(r.Ok, r.Message);
            Assert.True(double.IsNaN(r.Grid[2, 1]));
            Assert.Equal(15, r.Grid.ValidNodeCount());
        }

        [Fact]
        public void Xyz_WholeMissingColumn_KeepsTheSpacingOfTheRest()
        {
            // columns 0, 2, 3 present: the spacing is the smallest gap, column 1 is all gaps
            var sb = new System.Text.StringBuilder();
            foreach (int i in new[] { 0, 2, 3 })
                for (int j = 0; j < 3; ++j)
                    sb.Append(i + " " + j + " " + (i + 10 * j) + "\n");
            HeightGridReadResult r = ReadXyz(sb.ToString());
            Assert.True(r.Ok, r.Message);
            Assert.Equal(4, r.Grid.NX);
            Assert.Equal(3, r.Grid.NY);
            Assert.True(double.IsNaN(r.Grid[1, 0]) && double.IsNaN(r.Grid[1, 2]));
            Assert.Equal(23, r.Grid[3, 2]);
        }

        [Fact]
        public void Xyz_PointsNotOnAGrid_ReportNotOnGrid()
        {
            HeightGrid g = Procedural(6, 6, 0, 0, 1, false);
            string text = XyzText(g) + "2.4 3 5\n";
            HeightGridReadResult r = ReadXyz(text);
            Assert.Equal(HeightGridStatus.NotOnGrid, r.Status);
            Assert.Null(r.Grid);
            Assert.NotEqual("", r.Message);

            // scattered points
            var rand = new Random(3);
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < 50; ++k)
                sb.Append(rand.NextDouble() * 100 + " " + rand.NextDouble() * 100 + " " + rand.NextDouble() + "\n");
            Assert.Equal(HeightGridStatus.NotOnGrid, ReadXyz(sb.ToString()).Status);
        }

        [Fact]
        public void Xyz_RotatedGrid_IsNotOnAGrid()
        {
            HeightGrid g = Procedural(8, 8, 0, 0, 1, false);
            Matrix3d rot = Matrix3d.AxisAngleD(Vector3d.AxisZ, 31);
            var sb = new System.Text.StringBuilder();
            for (int j = 0; j < g.NY; ++j)
                for (int i = 0; i < g.NX; ++i) {
                    Vector3d p = rot * new Vector3d(g.X(i), g.Y(j), g[i, j]);
                    sb.Append(p.x.ToString("R", CultureInfo.InvariantCulture) + " " + p.y.ToString("R", CultureInfo.InvariantCulture) + " " + p.z.ToString("R", CultureInfo.InvariantCulture) + "\n");
                }
            Assert.Equal(HeightGridStatus.NotOnGrid, ReadXyz(sb.ToString()).Status);
        }

        [Fact]
        public void Xyz_Statuses()
        {
            Assert.Equal(HeightGridStatus.EmptyInput, ReadXyz("").Status);
            Assert.Equal(HeightGridStatus.EmptyInput, ReadXyz("# only a comment\n\n").Status);
            Assert.Equal(HeightGridStatus.BadLine, ReadXyz("0 0 1\n1 0\n").Status);
            Assert.Equal(HeightGridStatus.BadLine, ReadXyz("0 0 1\n1 abc 2\n").Status);
            Assert.Equal(HeightGridStatus.BadLine, ReadXyz("0 0 1\nNaN 0 2\n").Status);
            Assert.Equal(HeightGridStatus.BadLine, ReadXyz("0 0 1\n1e999 0 2\n").Status);
            Assert.Equal(HeightGridStatus.TooSmall, ReadXyz("0 0 1\n0 1 2\n0 2 3\n").Status);       // one column
            Assert.Equal(HeightGridStatus.TooSmall, ReadXyz("5 5 1\n").Status);
            Assert.Equal(HeightGridStatus.DuplicateNode, ReadXyz("0 0 1\n1 0 2\n0 1 3\n1 1 4\n1 1 5\n").Status);
            // 1 x 2 cells: not square
            Assert.Equal(HeightGridStatus.NonSquareCells, ReadXyz("0 0 1\n1 0 1\n0 2 1\n1 2 1\n0 4 1\n1 4 1\n").Status);
            // three points on a 401 x 401 lattice: the grid would be huge and nearly empty
            Assert.Equal(HeightGridStatus.TooSparse, ReadXyz("0 0 1\n1 1 1\n400 400 1\n").Status);
            // (blocks far apart, whose smallest gap could be jitter: no lattice is reported)
            var clusters = new List<(double, double, double)>();
            foreach (int ox in new[] { 0, 5000 })
                for (int i = 0; i < 10; ++i)
                    for (int j = 0; j < 10; ++j)
                        clusters.Add((ox + i, ox + j, i));
            Assert.Equal(HeightGridStatus.NotOnGrid, ReadXyz(Points(clusters)).Status);
        }

        [Fact]
        public void Xyz_TooLarge_UsesMaxNodes()
        {
            HeightGrid g = Procedural(10, 10, 0, 0, 1, false);
            Assert.Equal(HeightGridStatus.TooLarge, ReadXyz(XyzText(g), new HeightGridReadOptions { MaxNodes = 99 }).Status);
            Assert.True(ReadXyz(XyzText(g), new HeightGridReadOptions { MaxNodes = 100 }).Ok);
        }

        [Fact]
        public void Xyz_File_Missing_ReportsIoError()
        {
            HeightGridReadResult r = HeightGridReader.ReadXyzFile(Path.Combine(MeshFormatTestUtil.TempDir(), "nothing.xyz"));
            Assert.Equal(HeightGridStatus.IoError, r.Status);
            Assert.Equal(HeightGridStatus.IoError, HeightGridReader.ReadEsriAsciiFile(Path.Combine(MeshFormatTestUtil.TempDir(), "nothing.asc")).Status);
        }

        [Fact]
        public void Files_RoundTrip()
        {
            HeightGrid g = Procedural(7, 5, 10.5, 20.25, 0.25, true);
            string dir = MeshFormatTestUtil.TempDir();
            try {
                string xyz = Path.Combine(dir, "g.xyz"), asc = Path.Combine(dir, "g.asc");
                File.WriteAllText(xyz, XyzText(g));
                File.WriteAllText(asc, EsriText(g));
                AssertSameGrid(g, HeightGridReader.ReadXyzFile(xyz).Grid);
                AssertSameGrid(g, HeightGridReader.ReadEsriAsciiFile(asc).Grid);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        // ---- invariance: the points mapped by a similarity (scale and translation) give the mapped grid, bit for bit for 2^k ----

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Xyz_Invariance(string name)
        {
            Similarity s = Similarity.ByName(name);
            HeightGrid g = Procedural(9, 6, 3, -2, 0.5, true);
            string mapped;
            {
                var sb = new System.Text.StringBuilder();
                for (int j = 0; j < g.NY; ++j)
                    for (int i = 0; i < g.NX; ++i) {
                        if (double.IsNaN(g[i, j])) continue;
                        Vector3d p = s.Apply(new Vector3d(g.X(i), g.Y(j), g[i, j]));
                        sb.Append(p.x.ToString("R", CultureInfo.InvariantCulture) + " " + p.y.ToString("R", CultureInfo.InvariantCulture) + " " + p.z.ToString("R", CultureInfo.InvariantCulture) + "\n");
                    }
                mapped = sb.ToString();
            }
            HeightGridReadResult r = ReadXyz(mapped);
            if (s.IsRotated) {
                Assert.Equal(HeightGridStatus.NotOnGrid, r.Status);
                return;
            }
            Assert.True(r.Ok, r.Message);
            Assert.Equal(g.NX, r.Grid.NX);
            Assert.Equal(g.NY, r.Grid.NY);
            double tol = s.IsExact ? 0 : 1e-9 * s.Scale * 10 + 16 * s.Resolution(10) * s.Scale;
            Assert.True(Math.Abs(r.Grid.X0 - s.Apply(new Vector3d(g.X0, 0, 0)).x) <= tol);
            Assert.True(Math.Abs(r.Grid.Y0 - s.Apply(new Vector3d(0, g.Y0, 0)).y) <= tol);
            Assert.True(Math.Abs(r.Grid.CellSize - s.Scale * g.CellSize) <= tol);
            for (int k = 0; k < g.Z.Length; ++k) {
                if (double.IsNaN(g.Z[k])) { Assert.True(double.IsNaN(r.Grid.Z[k])); continue; }
                double expected = s.Apply(new Vector3d(0, 0, g.Z[k])).z;
                Assert.True(Math.Abs(expected - r.Grid.Z[k]) <= tol, $"node {k}");
                if (s.IsExact) Assert.Equal(expected, r.Grid.Z[k]);
            }
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        // ---- ESRI ASCII ----

        [Fact]
        public void Esri_OrientationHeaderAndNoData()
        {
            // the first row of the file is the north row; xllcorner is the corner of the lower-left cell
            string text = "ncols 3\nnrows 2\nxllcorner 100\nyllcorner 200\ncellsize 10\nNODATA_value -9999\n"
                + "1 2 3\n4 -9999 6\n";
            HeightGridReadResult r = ReadEsri(text);
            Assert.True(r.Ok, r.Message);
            HeightGrid g = r.Grid;
            Assert.Equal(3, g.NX);
            Assert.Equal(2, g.NY);
            Assert.Equal(105, g.X0);
            Assert.Equal(205, g.Y0);
            Assert.Equal(10, g.CellSize);
            Assert.Equal(4, g[0, 0]);            // south row
            Assert.True(double.IsNaN(g[1, 0]));
            Assert.Equal(6, g[2, 0]);
            Assert.Equal(1, g[0, 1]);            // north row
            Assert.Equal(3, g[2, 1]);
        }

        [Fact]
        public void Esri_CenterKeysDxDyAndCaseAndSeparators()
        {
            string text = "NCOLS 2 NROWS 2 XLLCENTER 5 YLLCENTER 6 DX 2 DY 2\r\n1\t2\r\n3,4";
            // (a comma is no separator in ESRI ASCII)
            Assert.Equal(HeightGridStatus.BadValue, ReadEsri(text).Status);
            HeightGridReadResult r = ReadEsri("NCOLS 2 NROWS 2 XLLCENTER 5 YLLCENTER 6 DX 2 DY 2\r\n1\t2\r\n3 4\r\n");
            Assert.True(r.Ok, r.Message);
            Assert.Equal(5, r.Grid.X0);
            Assert.Equal(6, r.Grid.Y0);
            Assert.Equal(2, r.Grid.CellSize);
            Assert.Equal(new[] { 3.0, 4.0, 1.0, 2.0 }, r.Grid.Z);
        }

        [Fact]
        public void Esri_NoDataWithoutHeaderEntryUsesTheOption()
        {
            string text = "ncols 2\nnrows 1\nxllcenter 0\nyllcenter 0\ncellsize 1\n7 -1\n";
            Assert.Equal(-1, ReadEsri(text).Grid[1, 0]);
            Assert.True(double.IsNaN(ReadEsri(text, new HeightGridReadOptions { NoDataValue = -1 }).Grid[1, 0]));
        }

        [Fact]
        public void Esri_RoundTrip_WithGaps()
        {
            HeightGrid g = Procedural(11, 8, 1234.5, 98.75, 0.125, true);
            HeightGridReadResult r = ReadEsri(EsriText(g));
            Assert.True(r.Ok, r.Message);
            AssertSameGrid(g, r.Grid);
        }

        [Fact]
        public void Esri_Statuses()
        {
            const string h = "ncols 2\nnrows 2\nxllcorner 0\nyllcorner 0\ncellsize 1\n";
            Assert.True(ReadEsri(h + "1 2 3 4").Ok);
            Assert.Equal(HeightGridStatus.EmptyInput, ReadEsri("  \n").Status);
            Assert.Equal(HeightGridStatus.Truncated, ReadEsri(h + "1 2 3").Status);
            Assert.Equal(HeightGridStatus.ExtraData, ReadEsri(h + "1 2 3 4 5").Status);
            Assert.Equal(HeightGridStatus.BadValue, ReadEsri(h + "1 2 x 4").Status);
            Assert.Equal(HeightGridStatus.BadValue, ReadEsri(h + "1 2 1e999 4").Status);
            Assert.Equal(HeightGridStatus.BadHeader, ReadEsri("nrows 2\nxllcorner 0\nyllcorner 0\ncellsize 1\n1 2 3 4").Status);
            Assert.Equal(HeightGridStatus.BadHeader, ReadEsri("ncols 2\nnrows 2\nxllcorner 0\ncellsize 1\n1 2 3 4").Status);
            Assert.Equal(HeightGridStatus.BadHeader, ReadEsri("ncols 2\nnrows 2\nxllcorner 0\nxllcenter 0\nyllcorner 0\ncellsize 1\n1 2 3 4").Status);
            Assert.Equal(HeightGridStatus.BadHeader, ReadEsri("ncols 2\nnrows 2\nxllcorner 0\nyllcorner 0\n1 2 3 4").Status);       // no cellsize
            Assert.Equal(HeightGridStatus.BadHeader, ReadEsri("ncols 2\nncols 2\nnrows 2\nxllcorner 0\nyllcorner 0\ncellsize 1\n1 2 3 4").Status);
            Assert.Equal(HeightGridStatus.BadHeader, ReadEsri("ncols 2.5\nnrows 2\nxllcorner 0\nyllcorner 0\ncellsize 1\n1 2 3 4 5").Status);
            Assert.Equal(HeightGridStatus.BadHeader, ReadEsri("ncols 0\nnrows 2\nxllcorner 0\nyllcorner 0\ncellsize 1\n").Status);
            Assert.Equal(HeightGridStatus.BadHeader, ReadEsri("ncols 2\nnrows 2\nxllcorner 0\nyllcorner 0\ncellsize 0\n1 2 3 4").Status);
            Assert.Equal(HeightGridStatus.BadHeader, ReadEsri("ncols 2\nnrows 2\nxllcorner 0\nyllcorner 0\ncellsize -1\n1 2 3 4").Status);
            Assert.Equal(HeightGridStatus.NonSquareCells, ReadEsri("ncols 2\nnrows 2\nxllcorner 0\nyllcorner 0\ndx 1\ndy 2\n1 2 3 4").Status);
            Assert.Equal(HeightGridStatus.NonSquareCells, ReadEsri("ncols 2\nnrows 2\nxllcorner 0\nyllcorner 0\ncellsize 1\ndy 2\n1 2 3 4").Status);
            Assert.Equal(HeightGridStatus.TooLarge, ReadEsri(h + "1 2 3 4", new HeightGridReadOptions { MaxNodes = 3 }).Status);
            Assert.True(ReadEsri(h + "1 2 3 4", new HeightGridReadOptions { MaxNodes = 4 }).Ok);
            Assert.Equal(HeightGridStatus.TooLarge, ReadEsri("ncols 100000\nnrows 100000\nxllcorner 0\nyllcorner 0\ncellsize 1\n1 2").Status);
        }

        [Fact]
        public void Esri_HugeClaimWithLittleData_DoesNotAllocateTheClaim()
        {
            // ncols * nrows is within MaxNodes, the file holds three values: Truncated, quickly and without a 500 MB array
            var o = new HeightGridReadOptions { MaxNodes = 1L << 27 };
            long before = GC.GetTotalMemory(true);
            HeightGridReadResult r = ReadEsri("ncols 11000\nnrows 11000\nxllcorner 0\nyllcorner 0\ncellsize 1\n1 2 3", o);
            Assert.Equal(HeightGridStatus.Truncated, r.Status);
            Assert.True(GC.GetTotalMemory(false) - before < 64L * 1024 * 1024);
        }

        // ---- the grid TIN ----

        [Fact]
        public void BuildMesh_IsTheTinOfHeight_AndSkipsNoDataCells()
        {
            HeightGrid g = Procedural(8, 6, 40, 70, 2, true);
            DMesh3 m = g.BuildMesh();
            int cells = 0;
            for (int j = 0; j < g.NY - 1; ++j)
                for (int i = 0; i < g.NX - 1; ++i)
                    if (!double.IsNaN(g[i, j]) && !double.IsNaN(g[i + 1, j]) && !double.IsNaN(g[i, j + 1]) && !double.IsNaN(g[i + 1, j + 1]))
                        ++cells;
            Assert.Equal(2 * cells, m.TriangleCount);
            Assert.True(cells < (g.NX - 1) * (g.NY - 1));
            Assert.True(m.IsCompact || m.VertexCount <= g.Z.Length);
            Assert.All(m.Vertices(), v => Assert.True(v.IsFinite));
            // counter-clockwise from above, no flipped or empty triangle
            foreach (int t in m.TriangleIndices())
                Assert.True(m.GetTriArea(t) > 0 && m.GetTriNormal(t).z > 0);
            // the fixed diagonal: the TIN's height at points of every triangle equals Height()
            var rand = new Random(5);
            int tested = 0;
            foreach (int t in m.TriangleIndices()) {
                Vector3d v0 = Vector3d.Zero, v1 = Vector3d.Zero, v2 = Vector3d.Zero; m.GetTriVertices(t, ref v0, ref v1, ref v2);
                for (int k = 0; k < 3; ++k) {
                    double a = rand.NextDouble(), b = rand.NextDouble() * (1 - a), c = 1 - a - b;
                    Vector3d p = a * v0 + b * v1 + c * v2;
                    Assert.True(Math.Abs(g.Height(p.x, p.y) - p.z) < 1e-9, $"triangle {t}");
                    ++tested;
                }
            }
            Assert.Equal(6 * cells, tested);
            // outside the grid and in the no-data cells: NaN
            Assert.True(double.IsNaN(g.Height(39.9, 70)));
            Assert.True(double.IsNaN(g.Height(g.X(g.NX / 2) + 0.5, g.Y(g.NY / 2) + 0.5)));
            Assert.True(double.IsNaN(g.Height(g.X(g.NX - 1) - 0.5, g.Y(g.NY - 1) - 0.5)));
            // the borders belong to the last cells
            Assert.Equal(g[0, 0], g.Height(g.X0, g.Y0));
            Assert.Equal(g[g.NX - 2, 0], g.Height(g.X(g.NX - 2), g.Y0), 12);
            Assert.Equal(g[g.NX - 1, 0], g.Height(g.X1, g.Y0), 12);
            Assert.Equal(g[0, g.NY - 1], g.Height(g.X0, g.Y1), 12);
            Assert.Equal(g[3, g.NY - 1], g.Height(g.X(3), g.Y1), 12);
            Assert.Equal(0.75 * g[g.NX - 1, g.NY - 2] + 0.25 * g[g.NX - 1, g.NY - 1], g.Height(g.X1, g.Y(g.NY - 2) + 0.25 * g.CellSize), 12);   // (the last cell, no read past the array)
            Assert.True(double.IsNaN(g.Height(g.X1 + 0.5, g.Y0)) && double.IsNaN(g.Height(g.X0, g.Y1 + 0.5)) && double.IsNaN(g.Height(g.X0, g.Y0 - 0.5)));
            Assert.Equal(0.75 * g[g.NX - 1, 1] + 0.25 * g[g.NX - 1, 2], g.Height(g.X1, g.Y(1) + 0.25 * g.CellSize), 12);
            Assert.Equal(0.5 * g[1, g.NY - 1] + 0.5 * g[2, g.NY - 1], g.Height(g.X(1) + 0.5 * g.CellSize, g.Y1), 12);
        }

        [Fact]
        public void BuildMesh_OriginWindowAndDegenerateGrids()
        {
            HeightGrid g = Procedural(6, 5, 1000, 2000, 1, false);
            DMesh3 m = g.BuildMesh(new Vector3d(1000, 2000, 100));
            Assert.Equal(2 * 5 * 4, m.TriangleCount);
            Assert.Equal(30, m.VertexCount);
            Assert.Equal(0, m.GetBounds().Min.x, 12);
            Assert.Equal(5, m.GetBounds().Max.x, 12);
            // only the cells [1,3) x [2,4): 4 cells, 9 nodes
            DMesh3 part = g.BuildMesh(default(Vector3d), 1, 2, 3, 4);
            Assert.Equal(8, part.TriangleCount);
            Assert.Equal(9, part.VertexCount);
            Assert.Equal(1001, part.GetBounds().Min.x, 12);
            // no cell: an empty mesh
            Assert.Equal(0, new HeightGrid(new double[] { 1 }, 1, 1, 0, 0, 1).BuildMesh().TriangleCount);
            Assert.True(double.IsNaN(new HeightGrid(new double[] { 1 }, 1, 1, 0, 0, 1).Height(0, 0)));
        }

        [Fact]
        public void BuildMesh_Window()
        {
            HeightGrid g = Procedural(10, 10, 5, 5, 1, false);
            HeightGrid w = g.Window(7, 8, 11.5, 20);
            Assert.Equal(5, w.NX);          // x = 7 .. 11
            Assert.Equal(7, w.NY);          // y = 8 .. 14 (the box reaches beyond the grid's last row, y = 14)
            Assert.Equal(7, w.X0);
            Assert.Equal(8, w.Y0);
            Assert.Equal(g[2, 3], w[0, 0]);
            Assert.Equal(g[6, 9], w[4, 6]);
        }

        // ---- review findings: lattice detection, tolerances, hostile input, writers ----

        static string Points(IEnumerable<(double x, double y, double z)> pts)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in pts)
                sb.Append(p.x.ToString("R", CultureInfo.InvariantCulture) + " " + p.y.ToString("R", CultureInfo.InvariantCulture) + " " + p.z.ToString("R", CultureInfo.InvariantCulture) + "\n");
            return sb.ToString();
        }

        [Fact]
        public void Xyz_RowIndexUsesTheYSpacing()
        {
            // dy is 5e-5 larger than dx (within the squareness tolerance): after 12000 rows the drift is 0.6 cell
            double dx = 1, dy = 1 + 5e-5;
            var pts = new List<(double, double, double)>();
            for (int j = 0; j < 12000; ++j) { pts.Add((0, j * dy, j)); pts.Add((dx, j * dy, j + 0.5)); }
            HeightGridReadResult r = ReadXyz(Points(pts));
            Assert.True(r.Ok, r.Message);
            Assert.Equal(12000, r.Grid.NY);
            Assert.Equal(12000 - 1, r.Grid[0, 11999]);
            Assert.Equal(7.5, r.Grid[1, 7]);
            Assert.Equal(0, r.Grid.ValidNodeCount() - 24000);
        }

        [Fact]
        public void Xyz_JitterWithinTheToleranceOnALargeGrid()
        {
            var rand = new Random(11);
            var pts = new List<(double, double, double)>();
            for (int j = 0; j < 3; ++j)
                for (int i = 0; i < 1000; ++i)
                    pts.Add((i + (rand.NextDouble() - 0.5) * 8e-4, j + (rand.NextDouble() - 0.5) * 8e-4, i + 1000 * j));
            HeightGridReadResult r = ReadXyz(Points(pts));
            Assert.True(r.Ok, r.Message);
            Assert.Equal(1000, r.Grid.NX);
            Assert.Equal(3, r.Grid.NY);
            Assert.Equal(1, r.Grid.CellSize, 5);       // (the step of the 1000 columns, not of the 3 rows)
            Assert.Equal(2999, r.Grid[999, 2]);
            // a near duplicate coordinate (a second text form of the same column) is no new column
            pts[1005] = (pts[1005].Item1 + 1e-7, pts[1005].Item2, pts[1005].Item3);
            Assert.True(ReadXyz(Points(pts)).Ok);
        }

        [Theory]
        [InlineData(0.9e-3, true)]
        [InlineData(1.1e-3, false)]
        public void Xyz_GridToleranceBoundary(double offset, bool ok)
        {
            var pts = new List<(double, double, double)>();
            for (int j = 0; j < 4; ++j)
                for (int i = 0; i < 4; ++i)
                    pts.Add((i + (i == 2 && j == 1 ? offset : 0), j, i + j));
            HeightGridReadResult r = ReadXyz(Points(pts));
            Assert.Equal(ok, r.Ok);
            if (!ok) Assert.Equal(HeightGridStatus.NotOnGrid, r.Status);
            Assert.True(ReadXyz(Points(pts), new HeightGridReadOptions { GridTolerance = 2e-3 }).Ok);
        }

        [Fact]
        public void Xyz_AMissingColumnBeyondThePatternIsNotALattice()
        {
            // x = 0, 2, 5: no two columns are neighbours, the spacing is not determined
            Assert.Equal(HeightGridStatus.NotOnGrid, ReadXyz("0 0 1\n2 0 1\n5 0 1\n0 1 1\n2 1 1\n5 1 1\n").Status);
            // x = 0, 2, 4, 5: the neighbours 4, 5 give the spacing
            HeightGridReadResult r = ReadXyz("0 0 1\n2 0 1\n4 0 1\n5 0 1\n0 1 1\n2 1 1\n4 1 1\n5 1 1\n");
            Assert.True(r.Ok, r.Message);
            Assert.Equal(6, r.Grid.NX);
        }

        [Fact]
        public void Xyz_NonDyadicCellSize_RoundTripsUpToRounding()
        {
            HeightGrid g = Procedural(9, 7, 350000.5, 5600001.25, 0.1, true);
            HeightGridReadResult r = ReadXyz(XyzText(g));
            Assert.True(r.Ok, r.Message);
            Assert.Equal(g.NX, r.Grid.NX);
            Assert.Equal(g.NY, r.Grid.NY);
            Assert.Equal(g.X0, r.Grid.X0);
            Assert.Equal(g.CellSize, r.Grid.CellSize, 9);       // (the x coordinates near 350000 have an ulp of 6e-11)
            Assert.Equal(g.Z, r.Grid.Z);
            HeightGridReadResult e = ReadEsri(EsriText(g));
            AssertSameGrid(g, e.Grid);          // ESRI ASCII is exact
        }

        [Fact]
        public void Xyz_SparseLimit_UsesMaxNodesPerPoint()
        {
            // 9 points on a 33 x 33 lattice: 1089 nodes > max(1024, 64 * 9)
            var pts = new List<(double, double, double)>();
            foreach (int i in new[] { 0, 1, 32 })
                foreach (int j in new[] { 0, 1, 32 })
                    pts.Add((i, j, i + j));
            string text = Points(pts);
            Assert.Equal(HeightGridStatus.TooSparse, ReadXyz(text).Status);
            Assert.True(ReadXyz(text, new HeightGridReadOptions { MaxNodesPerPoint = 128 }).Ok);
        }

        [Fact]
        public void Xyz_HostileInput()
        {
            Assert.Equal(HeightGridStatus.BadLine, ReadXyz("0 0 1\n1 0 1" + new string(' ', 10000) + "\n").Status);                    // a long line (cut at 4096 characters it would parse)
            Assert.Equal(HeightGridStatus.BadLine, ReadXyz("632000,5 5712000,5 12,3\n").Status);                                     // decimal commas
            Assert.Equal(HeightGridStatus.TooLarge, ReadXyz("0 0 1\n1 0 1\n0 1 1\n1 1 1\n", new HeightGridReadOptions { MaxPoints = 3 }).Status);
            Assert.True(ReadXyz("0 0 1\n1 0 1\n0 1 1\n1 1 1\n", new HeightGridReadOptions { MaxPoints = 4 }).Ok);
            Assert.True(ReadXyz("0,0,1\n1,0,1\n0,1,1\n1,1,1").Ok);                                                                     // commas alone separate, no final newline
        }

        [Fact]
        public void Esri_LongTokensAndHeaders()
        {
            const string h = "ncols 2\nnrows 2\nxllcorner 0\nyllcorner 0\ncellsize 1\n";
            string longNumber = "0." + new string('0', 1500) + "1";
            Assert.Equal(HeightGridStatus.BadValue, ReadEsri(h + "1 2 " + longNumber + " 4").Status);
            Assert.Equal(HeightGridStatus.BadHeader, ReadEsri("ncols " + longNumber + "\nnrows 2").Status);
            Assert.Equal(HeightGridStatus.BadHeader, ReadEsri("ncols 2\nnrows 2\nxllcorner 0\nyllcorner 0\nbyteorder LSBFIRST\ncellsize 1\n1 2 3 4").Status);
        }

        [Fact]
        public void Esri_MixedCornerCenterAndNoDataPrecedence()
        {
            HeightGridReadResult r = ReadEsri("ncols 2\nnrows 1\nxllcorner -10\nyllcenter -3\ncellsize 2\nNODATA_value 5\n5 6\n", new HeightGridReadOptions { NoDataValue = 6 });
            Assert.True(r.Ok, r.Message);
            Assert.Equal(-9, r.Grid.X0);
            Assert.Equal(-3, r.Grid.Y0);
            Assert.True(double.IsNaN(r.Grid[0, 0]));      // the header's NODATA_value (5) wins over the option (6)
            Assert.Equal(6, r.Grid[1, 0]);
            Assert.True(ReadEsri("ncols 1\nnrows 1\nxllcenter 1\nyllcenter 1\ncellsize 2\ndx 2\ndy 2\n3").Ok);
        }

        [Fact]
        public void Writers_RejectWhatTheReadersCannotReadBack()
        {
            HeightGrid g = Procedural(3, 3, 0, 0, 1, false);
            var w = new StringWriter();
            Assert.Throws<ArgumentException>(() => HeightGridWriter.WriteEsriAscii(w, g, double.NaN));
            Assert.Throws<ArgumentException>(() => HeightGridWriter.WriteEsriAscii(w, g, double.PositiveInfinity));
            g[1, 1] = -9999;
            Assert.Throws<ArgumentException>(() => HeightGridWriter.WriteEsriAscii(w, g));
            Assert.Equal(-9999, ReadEsri(EsriText(g, -8888)).Grid[1, 1]);
            g[1, 1] = double.PositiveInfinity;
            Assert.Throws<ArgumentException>(() => HeightGridWriter.WriteEsriAscii(w, g, -8888));
            Assert.Throws<ArgumentException>(() => HeightGridWriter.WriteXyz(w, g));
        }

        [Fact]
        public void Height_NoDataCorners_GiveNaNInBothTrianglesOfTheCell()
        {
            for (int corner = 0; corner < 4; ++corner) {
                HeightGrid g = Procedural(3, 3, 0, 0, 1, false);
                g[corner & 1, corner >> 1] = corner == 3 ? double.PositiveInfinity : double.NaN;
                Assert.True(double.IsNaN(g.Height(0.75, 0.25)), $"corner {corner}, lower triangle");
                Assert.True(double.IsNaN(g.Height(0.25, 0.75)), $"corner {corner}, upper triangle");
                Assert.Equal(2 * (4 - new[] { 1, 2, 2, 4 }[corner]), g.BuildMesh().TriangleCount);     // the cells that use the node are left out
            }
        }

        [Fact]
        public void Window_OutsideOrNonFiniteBoxesGiveAnEmptyGrid()
        {
            HeightGrid g = Procedural(3, 3, 0, 0, 1, false);
            foreach (var box in new[] { (100.0, 0.0, 200.0, 2.0), (0.0, 100.0, 2.0, 200.0), (-5.0, -5.0, -1.0, -1.0), (double.NaN, 0.0, 2.0, 2.0),
                                        (0.0, 0.0, double.PositiveInfinity, double.NaN), (3.0, 0.0, 2.0, 2.0) }) {
                HeightGrid w = g.Window(box.Item1, box.Item2, box.Item3, box.Item4);
                Assert.Equal(0, w.NX * w.NY);
                Assert.Empty(w.Z);
            }
            HeightGrid all = g.Window(double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity);
            AssertSameGrid(g, all);
        }

        [Fact]
        public void BuildMesh_WithGaps_IsAValidMesh()
        {
            HeightGrid g = Procedural(8, 6, 40, 70, 2, true);
            g[2, 2] = double.NaN; g[3, 3] = double.NaN;          // cells that touch only at a corner
            DMesh3 m = g.BuildMesh();
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly) || m.IsClosed() == false);
            Assert.Equal(m.VertexCount, g.BuildMesh().VertexCount);
            Assert.True(m.IsCompact);
        }

        [Fact]
        public void Constructor_RejectsInconsistentSizes()
        {
            Assert.Throws<ArgumentException>(() => new HeightGrid(new double[5], 2, 2, 0, 0, 1));
            Assert.Throws<ArgumentException>(() => new HeightGrid(new double[4], 2, 2, 0, 0, 0));
            Assert.Throws<ArgumentNullException>(() => new HeightGrid(null, 2, 2, 0, 0, 1));
        }
    }
}
