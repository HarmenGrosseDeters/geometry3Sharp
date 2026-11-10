using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using gs;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary>
    /// The dgm and dgm_tin commands: height grid files in (XYZ ASCII, ESRI ASCII Grid), the grid report, the grid written again, the grid TIN and
    /// the TIN with a guaranteed error out. The commands add no geometry of their own, so the outputs are checked against the library's result
    /// on the same inputs (written as g3mesh, bit for bit); every status path and input error is checked.
    /// </summary>
    public class DgmCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public DgmCliTests(TestMeshes meshes) { m = meshes; }

        static string F(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        static HeightGrid Grid(int n = 33, bool gaps = false)
        {
            var g = new HeightGrid(new double[n * n], n, n, 350000.5, 5600000.25, 0.5);
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) g[i, j] = 3 * Math.Sin(i * 0.1) * Math.Cos(j * 0.13) + 0.2 * Math.Sin(0.3 * i + 0.2 * j);
            if (gaps) for (int j = 10; j <= 14; ++j) for (int i = 10; i <= 14; ++i) g[i, j] = double.NaN;
            return g;
        }

        string WriteGrid(string name, HeightGrid g, bool esri)
        {
            string path = m.Path_(name);
            using (var w = new StreamWriter(path)) { if (esri) HeightGridWriter.WriteEsriAscii(w, g); else HeightGridWriter.WriteXyz(w, g); }
            return path;
        }

        static void AssertSameMesh(DMesh3 expected, string path)
        {
            DMesh3 got = StandardMeshReader.ReadMesh(path);
            Assert.NotNull(got);
            Assert.Equal(expected.VertexIndices().Select(expected.GetVertex).ToList(), got.VertexIndices().Select(got.GetVertex).ToList());
            Assert.Equal(expected.TriangleIndices().Select(expected.GetTriangle), got.TriangleIndices().Select(got.GetTriangle));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Dgm_ReportsTheGrid_WritesBothFormatsAndTheGridTin(bool esri)
        {
            HeightGrid g = Grid(33, true);
            string input = WriteGrid(esri ? "g.asc" : "g.xyz", g, esri);
            string xyz = m.Path_("out_" + esri + ".xyz"), asc = m.Path_("out_" + esri + ".asc"), mesh = m.Path_("out_" + esri + ".g3mesh");
            var (exit, r) = Run("dgm", input, xyz, "--mesh", mesh);
            Assert.True(exit == 0, r.ToJsonString());
            Assert.Equal(33, I(r["nx"])); Assert.Equal(33, I(r["ny"]));
            Assert.Equal(0.5, D(r["cell_size"])); Assert.Equal(350000.5, D(r["x0"])); Assert.Equal(5600000.25, D(r["y0"]));
            Assert.Equal(33 * 33 - 25, I(r["valid_nodes"])); Assert.Equal(25, I(r["no_data_nodes"]));
            Assert.Equal(g.Z.Where(HeightGrid.IsData).Min(), D(r["z_min"])); Assert.Equal(g.Z.Where(HeightGrid.IsData).Max(), D(r["z_max"]));
            AssertSameMesh(g.BuildMesh(), mesh);
            // the written XYZ file reads back as the same grid; and as ESRI via the extension
            HeightGridReadResult back = HeightGridReader.ReadXyzFile(xyz);
            Assert.True(back.Ok, back.Message);
            Assert.Equal(g.Z, back.Grid.Z);
            Assert.Equal("esri", S(Run("dgm", xyz, asc).result["output_format"]));
            HeightGridReadResult back2 = HeightGridReader.ReadEsriAsciiFile(asc);
            Assert.True(back2.Ok, back2.Message);
            Assert.Equal(g.Z, back2.Grid.Z);
            Assert.Equal(g.CellSize, back2.Grid.CellSize); Assert.Equal(g.X0, back2.Grid.X0); Assert.Equal(g.Y0, back2.Grid.Y0);
        }

        [Fact]
        public void Dgm_NoDataValue_AndFormatOverride()
        {
            string path = m.Path_("nodata.txt");
            File.WriteAllText(path, "ncols 3\nnrows 2\nxllcenter 0\nyllcenter 0\ncellsize 1\n1 2 3\n4 -77 6\n");
            var (exit, r) = Run("dgm", path, "--format", "esri", "--no-data", "-77");
            Assert.True(exit == 0, r.ToJsonString());
            Assert.Equal(5, I(r["valid_nodes"]));
            // without it the value is a height
            Assert.Equal(6, I(Run("dgm", path, "--format", "esri").result["valid_nodes"]));
        }

        [Fact]
        public void Dgm_Errors_HaveResultCodes()
        {
            Assert.Equal(ResultCodes.ReadError, S(Run("dgm", m.Path_("missing.xyz")).result["code"]));
            string bad = m.Path_("bad.xyz"); File.WriteAllText(bad, "1 2 three\n");
            var (exit, r) = Run("dgm", bad);
            Assert.Equal(ResultCodes.ReadError, S(r["code"])); Assert.Equal(3, exit); Assert.Equal("bad_line", S(r["grid_status"]));
            string good = WriteGrid("e.xyz", Grid(), false);
            Assert.Equal(ResultCodes.UnknownFormat, S(Run("dgm", good, "--mesh", m.Path_("x.unknown")).result["code"]));
            Assert.Equal(ResultCodes.WriteError, S(Run("dgm", good, Path.Combine(m.Dir, "no_such_dir", "o.xyz")).result["code"]));
            // a NODATA value equal to a height cannot be written
            string flat = m.Path_("flat.xyz"); File.WriteAllText(flat, "0 0 -9999\n1 0 1\n0 1 1\n1 1 1\n");
            Assert.Equal(ResultCodes.InvalidArguments, S(Run("dgm", flat, m.Path_("flat.asc")).result["code"]));
            // a grid without a cell with data has no mesh
            string line = m.Path_("line.xyz"); File.WriteAllText(line, "0 0 1\n1 0 1\n0 1 NaN\n1 1 NaN\n");
            Assert.Equal(ResultCodes.EmptyMesh, S(Run("dgm", line, "--mesh", m.Path_("l.g3mesh")).result["code"]));
        }

        [Fact]
        public void Dgm_NeverDamagesTheInput_AndWritesNothingWhenItFails()
        {
            HeightGrid g = Grid(8);
            string input = WriteGrid("keep.asc", g, true);
            byte[] before = File.ReadAllBytes(input);
            // output is the input; a NODATA value equal to a height (the writer refuses it)
            Assert.Equal(ResultCodes.InvalidArguments, S(Run("dgm", input, input).result["code"]));
            double z = g.Z[3];
            Assert.Equal(ResultCodes.InvalidArguments, S(Run("dgm", input, input + ".copy.asc", "--output-no-data", F(z)).result["code"]));
            Assert.False(File.Exists(input + ".copy.asc"));
            Assert.Equal(before, File.ReadAllBytes(input));
            Assert.Equal(ResultCodes.InvalidArguments, S(Run("dgm", input, "").result["code"]));
            // no cell with data: the mesh fails, and the grid is not written either
            string line = m.Path_("line2.xyz"); File.WriteAllText(line, "0 0 1\n1 0 1\n0 1 NaN\n1 1 NaN\n");
            string o = m.Path_("line2_out.xyz");
            Assert.Equal(ResultCodes.EmptyMesh, S(Run("dgm", line, o, "--mesh", m.Path_("line2.g3mesh")).result["code"]));
            Assert.False(File.Exists(o)); Assert.False(File.Exists(m.Path_("line2.g3mesh")));
        }

        [Fact]
        public void Dgm_XyzOutput_WarnsWhenTheBorderHasNoData()
        {
            string path = m.Path_("border.asc");
            File.WriteAllText(path, "ncols 3\nnrows 3\nxllcenter 0\nyllcenter 0\ncellsize 1\nNODATA_value -9999\n-9999 -9999 -9999\n1 2 3\n4 5 6\n");
            var (exit, r) = Run("dgm", path, m.Path_("border.xyz"));
            Assert.True(exit == 0, r.ToJsonString());
            Assert.Contains("xyz_border_no_data", r["warnings"].ToJsonString());
            Assert.Empty(Run("dgm", path, m.Path_("border2.asc")).result["warnings"].AsArray());
        }

        static string Pos(double x, double y, double? z = null) => "[" + F(x) + ", " + F(y) + (z.HasValue ? ", " + F(z.Value) : "") + "]";

        static string LineGeoJson(HeightGrid g, bool withZ, double offset = 0)
        {
            var pts = new List<string>();
            for (int q = 0; q <= 40; ++q) {
                double x = g.X0 + 1.5 + q * 0.3, y = g.Y0 + 2 + q * 0.2;
                pts.Add(withZ ? Pos(x, y, g.Height(x, y) + offset) : Pos(x, y));
            }
            return "{\"type\": \"Feature\", \"properties\": {}, \"geometry\": {\"type\": \"LineString\", \"coordinates\": [" + string.Join(", ", pts) + "]}}";
        }

        [Fact]
        public void DgmTin_IsTheLibrarysResult_WithTheGuaranteeReported()
        {
            HeightGrid g = Grid(33, true);
            string input = WriteGrid("t.asc", g, true), output = m.Path_("t.g3mesh"), lines = m.Path_("t_lines.geojson");
            File.WriteAllText(lines, LineGeoJson(g, false));
            var (exit, r) = Run("dgm_tin", input, output, "--epsilon", "0.05", "--breaklines", lines);
            Assert.True(exit == 0, r.ToJsonString());
            // (the grid read back from the file, as the command reads it)
            HeightGrid g2 = HeightGridReader.ReadEsriAsciiFile(input).Grid;
            var lib = new TerrainGridTin() { Grid = g2, Epsilon = 0.05, Breaklines = new List<TerrainBreakline> { new TerrainBreakline(Enumerable.Range(0, 41).Select(q => new Vector2d(g2.X0 + 1.5 + q * 0.3, g2.Y0 + 2 + q * 0.2))) } };
            Assert.True(lib.Compute());
            AssertSameMesh(lib.Result, output);
            Assert.Equal("ok", S(r["tin_status"]));
            Assert.Equal(lib.SurfaceError, D(r["surface_error"])); Assert.Equal(lib.NodeError, D(r["node_error"]));
            Assert.True(D(r["surface_error"]) <= 0.05);
            Assert.Equal(lib.Rounds, I(r["rounds"])); Assert.Equal(1, I(r["breaklines"])); Assert.Equal(lib.BreaklineEdges.Count, I(r["breakline_edges"]));
            Assert.True(I(r["mesh"]["triangles"]) < I(r["grid"]["grid_triangles"]));
            // the inline form gives the same, and a second run the same again
            string output2 = m.Path_("t2.g3mesh");
            var (exit2, r2) = Run("dgm_tin", input, output2, "--epsilon", "0.05", "--breaklines-geojson", LineGeoJson(g, false));
            Assert.True(exit2 == 0, r2.ToJsonString());
            AssertSameMesh(lib.Result, output2);
        }

        [Fact]
        public void DgmTin_SurveyHeights_FromTheInput()
        {
            HeightGrid g = Grid();
            string input = WriteGrid("s.xyz", g, false), output = m.Path_("s.g3mesh");
            var (exit, r) = Run("dgm_tin", input, output, "--epsilon", "0.1", "--breaklines-geojson", LineGeoJson(g, true, 0.02), "--breakline-heights", "input");
            Assert.True(exit == 0, r.ToJsonString());
            Assert.InRange(D(r["breakline_error"]), 0.01, 0.1);
            // a line 0.3 above the surface cannot be within epsilon: the status says so, the code is operation_failed
            var (exit2, r2) = Run("dgm_tin", input, m.Path_("s2.g3mesh"), "--epsilon", "0.1", "--breaklines-geojson", LineGeoJson(g, true, 0.3), "--breakline-heights", "input");
            Assert.Equal(1, exit2);
            Assert.Equal(ResultCodes.OperationFailed, S(r2["code"])); Assert.Equal("breakline_height_deviates", S(r2["tin_status"]));
            Assert.True(D(r2["breakline_error"]) > 0.25);
            // heights from the grid ignore the z
            Assert.Equal(0, Run("dgm_tin", input, m.Path_("s3.g3mesh"), "--epsilon", "0.1", "--breaklines-geojson", LineGeoJson(g, true, 0.3)).exit);
            // input heights need z
            Assert.Equal(ResultCodes.InvalidArguments, S(Run("dgm_tin", input, m.Path_("s4.g3mesh"), "--epsilon", "0.1", "--breaklines-geojson", LineGeoJson(g, false), "--breakline-heights", "input").result["code"]));
        }

        [Fact]
        public void DgmTin_Errors_HaveResultCodes()
        {
            HeightGrid g = Grid();
            string input = WriteGrid("er.xyz", g, false), output = m.Path_("er.g3mesh");
            string Code(params string[] a) => S(Run(a).result["code"]);
            Assert.Equal(ResultCodes.InvalidArguments, Code("dgm_tin", input, output, "--epsilon", "0"));
            Assert.Equal(ResultCodes.InvalidArguments, Code("dgm_tin", input, output, "--epsilon", "0.1", "--max-rounds", "0"));
            Assert.Equal(ResultCodes.InvalidArguments, Code("dgm_tin", input, output, "--epsilon", "1e-18"));        // below the rounding margin (TerrainGridTin says InvalidInput)
            Assert.Equal(ResultCodes.UnknownFormat, Code("dgm_tin", input, m.Path_("er.nope"), "--epsilon", "0.1"));
            Assert.Equal(ResultCodes.ReadError, Code("dgm_tin", m.Path_("none.xyz"), output, "--epsilon", "0.1"));
            Assert.Equal(ResultCodes.ReadError, Code("dgm_tin", input, output, "--epsilon", "0.1", "--breaklines", m.Path_("none.geojson")));
            Assert.Equal(ResultCodes.InvalidArguments, Code("dgm_tin", input, output, "--epsilon", "0.1", "--breaklines-geojson", "{not json"));
            Assert.Equal(ResultCodes.InvalidArguments, Code("dgm_tin", input, output, "--epsilon", "0.1", "--breaklines-geojson", "{\"type\": \"Point\", \"coordinates\": [1, 2]}"));
            string both = m.Path_("both.geojson"); File.WriteAllText(both, LineGeoJson(g, false));
            Assert.Equal(ResultCodes.InvalidArguments, Code("dgm_tin", input, output, "--epsilon", "0.1", "--breaklines", both, "--breaklines-geojson", LineGeoJson(g, false)));
            string nodata = m.Path_("nodata.xyz"); File.WriteAllText(nodata, "0 0 NaN\n1 0 NaN\n0 1 NaN\n1 1 NaN\n");
            Assert.Equal(ResultCodes.EmptyMesh, Code("dgm_tin", nodata, output, "--epsilon", "0.1"));
        }

        [Fact]
        public void DgmTin_BreaklineGeoJson_Variants()
        {
            HeightGrid g = Grid();
            string input = WriteGrid("v.xyz", g, false);
            double x = g.X0, y = g.Y0;
            string poly = "{\"type\": \"Polygon\", \"coordinates\": [[" + string.Join(", ", new[] { Pos(x + 3, y + 3), Pos(x + 9, y + 3), Pos(x + 9, y + 9), Pos(x + 3, y + 9), Pos(x + 3, y + 3) }) + "]]}";
            var (exit, r) = Run("dgm_tin", input, m.Path_("v1.g3mesh"), "--epsilon", "0.1", "--breaklines-geojson", poly);
            Assert.True(exit == 0, r.ToJsonString());
            Assert.Equal(1, I(r["breaklines"]));
            string multi = "{\"type\": \"FeatureCollection\", \"features\": [{\"type\": \"Feature\", \"geometry\": {\"type\": \"MultiLineString\", \"coordinates\": [[" + Pos(x + 2, y + 12) + ", " + Pos(x + 7, y + 13) + "], [" + Pos(x + 8, y + 2) + ", " + Pos(x + 12, y + 6) + "]]}}]}";
            var (exit2, r2) = Run("dgm_tin", input, m.Path_("v2.g3mesh"), "--epsilon", "0.1", "--breaklines-geojson", multi);
            Assert.True(exit2 == 0, r2.ToJsonString());
            Assert.Equal(2, I(r2["breaklines"]));
            Assert.True(I(r2["breakline_edges"]) >= 2);
        }

        [Fact]
        public void FuzzedBreaklineGeoJson_NeverAnInternalError()
        {
            HeightGrid g = Grid(17);
            string input = WriteGrid("f.xyz", g, false), output = m.Path_("f.g3mesh");
            string valid = "{\"type\": \"FeatureCollection\", \"features\": [{\"type\": \"Feature\", \"properties\": {}, \"geometry\": {\"type\": \"LineString\", \"coordinates\": [" +
                Pos(g.X0 + 1, g.Y0 + 1, 0.5) + ", " + Pos(g.X0 + 4, g.Y0 + 3, 0.6) + "]}}, {\"type\": \"Polygon\", \"coordinates\": [[" +
                string.Join(", ", new[] { Pos(g.X0 + 2, g.Y0 + 5), Pos(g.X0 + 5, g.Y0 + 5), Pos(g.X0 + 5, g.Y0 + 7), Pos(g.X0 + 2, g.Y0 + 5) }) + "]]}]}";
            var rnd = new Random(20261109);
            const string chars = "[]{},:\"0123456789.-eE truefalsnulPolygonLineStringMulti";
            var allowed = new[] { ResultCodes.Ok, ResultCodes.InvalidArguments, ResultCodes.OperationFailed };
            for (int iter = 0; iter < 150; ++iter) {
                var sb = new StringBuilder(valid);
                for (int e = 0, edits = 1 + rnd.Next(3); e < edits && sb.Length > 1; ++e) {
                    int pos = rnd.Next(sb.Length);
                    switch (rnd.Next(4)) {
                        case 0: sb.Remove(pos, 1); break;
                        case 1: sb[pos] = chars[rnd.Next(chars.Length)]; break;
                        case 2: sb.Insert(pos, sb[pos]); break;
                        default: sb.Length = pos; break;
                    }
                }
                string text = sb.ToString();
                var (exit, r) = Run("dgm_tin", input, output, "--epsilon", "0.2", "--breaklines-geojson", text, "--breakline-heights", iter % 2 == 0 ? "grid" : "input");
                Assert.True(allowed.Contains(S(r["code"])), text + " -> " + r.ToJsonString());
                Assert.Equal(ResultCodes.ExitCode(S(r["code"])), exit);
            }
        }
    }
}
