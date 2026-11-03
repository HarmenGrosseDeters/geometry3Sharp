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
    /// The slope command (TerrainSlopeDesign): GeoJSON toes and a TIN mesh file in, the composite or replacement, the
    /// surface and region meshes, the catch and target lines as GeoJSON and a report out. The command adds no geometry of
    /// its own, so the outputs are checked against the library's result on the same inputs (written as g3mesh, bit for
    /// bit), and every status path and input error is checked.
    /// </summary>
    public class SlopeCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public SlopeCliTests(TestMeshes meshes) { m = meshes; }

        static string F(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        // a tilted terrain z = 5 + 0.03 x, a 16 x 16 grid on [-40, 40]^2 moved by t
        static DMesh3 Terrain(Vector3d t, double half = 40)
        {
            var mesh = new DMesh3();
            int n = 16;
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = -half + 2 * half * i / n, y = -half + 2 * half * j / n;
                    mesh.AppendVertex(new Vector3d(x + t.x, y + t.y, 5 + 0.03 * x + t.z));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int v = j * (n + 1) + i;
                    mesh.AppendTriangle(v, v + 1, v + n + 2);
                    mesh.AppendTriangle(v, v + n + 2, v + n + 1);
                }
            return mesh;
        }

        // a square pit toe (half side 5) at c, at height c.z
        static List<Vector3d> Square(Vector3d c, double half = 5)
        {
            return new List<Vector3d> { c + new Vector3d(-half, -half, 0), c + new Vector3d(half, -half, 0), c + new Vector3d(half, half, 0), c + new Vector3d(-half, half, 0) };
        }

        static string Pos(Vector3d p) => $"[{F(p.x)}, {F(p.y)}, {F(p.z)}]";

        static string PolygonFeature(List<Vector3d> ring, string props)
        {
            var pts = ring.Concat(new[] { ring[0] }).Select(Pos);
            return "{\"type\": \"Feature\", \"geometry\": {\"type\": \"Polygon\", \"coordinates\": [[" + string.Join(", ", pts) + "]]}, \"properties\": {" + props + "}}";
        }

        static string LineFeature(List<Vector3d> line, string props)
        {
            return "{\"type\": \"Feature\", \"geometry\": {\"type\": \"LineString\", \"coordinates\": [" + string.Join(", ", line.Select(Pos)) + "]}, \"properties\": {" + props + "}}";
        }

        static string Collection(params string[] features) => "{\"type\": \"FeatureCollection\", \"features\": [" + string.Join(", ", features) + "]}";

        string WriteTerrain(string name, DMesh3 mesh)
        {
            string path = m.Path_(name);
            IOWriteResult r = StandardMeshWriter.WriteMesh(path, mesh, WriteOptions.Defaults);
            Assert.Equal(IOCode.Ok, r.code);
            return path;
        }

        static DMesh3 ReadMesh(string path)
        {
            DMesh3 mesh = StandardMeshReader.ReadMesh(path);
            Assert.NotNull(mesh);
            return mesh;
        }

        // the library's result on the same inputs (the reference of the command's outputs)
        static TerrainSlopeDesign Library(DMesh3 terrain, List<SlopeToe> toes, Action<TerrainSlopeDesign> set)
        {
            var d = new TerrainSlopeDesign() { Toes = toes, Terrain = new DMesh3(terrain), Tolerance = 0.05, BuildComposite = true };
            set?.Invoke(d);
            d.Compute();
            return d;
        }

        static SlopeToe Toe(List<Vector3d> pts, bool closed, SlopeToe.Sides side, SlopeSpec spec)
        {
            return SlopeToe.Constant(pts, closed, side, spec);
        }

        static void AssertSameMesh(DMesh3 expected, string path)
        {
            DMesh3 got = ReadMesh(path);
            Assert.Equal(expected.VertexCount, got.VertexCount);
            Assert.Equal(expected.TriangleCount, got.TriangleCount);
            var a = expected.VertexIndices().Select(expected.GetVertex).ToList();
            var b = got.VertexIndices().Select(got.GetVertex).ToList();
            Assert.Equal(a, b);
            Assert.Equal(expected.TriangleIndices().Select(expected.GetTriangle), got.TriangleIndices().Select(got.GetTriangle));
        }

        [Fact]
        public void PitInCut_OutputsAreTheLibrarysResult()
        {
            var terrain = Terrain(Vector3d.Zero);
            string t = WriteTerrain("pit_terrain.obj", terrain), toes = m.Path_("pit.geojson");
            File.WriteAllText(toes, Collection(PolygonFeature(Square(Vector3d.Zero), "\"side\": \"outside\", \"slope\": 1")));
            string output = m.Path_("pit.g3mesh"), surface = m.Path_("pit_surface.g3mesh"), region = m.Path_("pit_region.g3mesh"), lines = m.Path_("pit_catch.geojson");
            var (exit, r) = Run("slope", ReadBack(t), toes, output, "--tolerance", "0.05", "--surface", surface, "--region", region, "--catch-lines", lines);
            Assert.True(exit == 0, r.ToJsonString());
            Assert.Equal("success", S(r["design_status"]));
            Assert.Equal("none", S(r["status_kind"]));
            Assert.True(B(r["complete"]));
            Assert.False(B(r["local_frame"]));

            var d = Library(ReadMesh(ReadBack(t)), new List<SlopeToe> { Toe(Square(Vector3d.Zero), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)) }, null);
            Assert.Equal(TerrainSlopeDesign.Statuses.Success, d.Status);
            AssertSameMesh(d.Composite, output);
            AssertSameMesh(d.SlopeSurface, surface);
            AssertSameMesh(d.SlopeRegion, region);
            Assert.Equal(d.Volume, D(r["volume"]));
            Assert.Equal(d.CutVolume, D(r["cut_volume"]));
            Assert.Equal(d.UsedMinFeatureSize, D(r["min_feature_size"]));
            Assert.Equal(d.Attempts, I(r["attempts"]));
            Assert.Equal(d.CatchLines.Count, r["catch_lines"].AsArray().Count);
            Assert.True(B(r["catch_lines"][0]["closed"]));
            Assert.Equal(d.Composite.TriangleCount, I(r["mesh"]["triangles"]));

            // the catch line as a closed GeoJSON LineString, the library's points
            var g = JsonNode.Parse(File.ReadAllText(lines)).AsObject();
            Assert.Equal("FeatureCollection", S(g["type"]));
            var coords = g["features"][0]["geometry"]["coordinates"].AsArray();
            Assert.Equal(d.CatchLines[0].Points.Count + 1, coords.Count);
            for (int i = 0; i < d.CatchLines[0].Points.Count; ++i)
                Assert.Equal(d.CatchLines[0].Points[i], new Vector3d(D(coords[i][0]), D(coords[i][1]), D(coords[i][2])));
            Assert.Equal(coords[0].ToJsonString(), coords[coords.Count - 1].ToJsonString());
            Assert.Equal("none", S(g["features"][0]["properties"]["start_reason"]));
        }

        // the terrain as the command reads it (OBJ rounds nothing here, but the reference reads the same file)
        static string ReadBack(string path) => path;

        [Fact]
        public void OpenToeInFillWithSegmentSlopes_ReplacementAndTargetLines()
        {
            // an open toe (three sides of a square) on the right, raised above the terrain: fill; per segment slopes; the
            // replacement only; level lines at two heights
            var terrain = Terrain(Vector3d.Zero);
            string t = WriteTerrain("fill_terrain.obj", terrain), toes = m.Path_("fill.geojson");
            var line = Square(new Vector3d(0, 0, 9)).Take(3).ToList();
            File.WriteAllText(toes, Collection(LineFeature(line, "\"side\": \"right\", \"segment_slopes\": [1.5, {\"degrees\": 30}]")));
            string output = m.Path_("fill.g3mesh"), target = m.Path_("fill_levels.geojson");
            var (exit, r) = Run("slope", t, toes, output, "--tolerance", "0.05", "--direction", "fill", "--composite", "replacement",
                "--target-heights", "7, 6", "--target-lines", target);
            var specs = new[] { SlopeSpec.Ratio(1.5), SlopeSpec.Degrees(30) };
            var toe = new SlopeToe() { Points = line, Closed = false, Side = SlopeToe.Sides.Right, CutStart = specs, CutEnd = specs, FillStart = specs, FillEnd = specs };
            var d = Library(ReadMesh(t), new List<SlopeToe> { toe }, x => {
                x.Direction = TerrainSlopeDesign.Directions.Fill; x.CompositeKind = TerrainSlopeDesign.CompositeKinds.Replacement; x.TargetHeights = new List<double> { 7, 6 };
            });
            Assert.True(d.Status == TerrainSlopeDesign.Statuses.Success || d.Status == TerrainSlopeDesign.Statuses.NoDaylight, d.Status + " " + d.Message);
            Assert.True(exit == 0, r.ToJsonString());
            Assert.Equal(TerrainSlopeDesignSnake(d.Status), S(r["design_status"]));
            AssertSameMesh(d.Replacement, output);
            Assert.Equal(d.ReplacedTerrainTriangles.Count, I(r["replaced_terrain_triangles"]));
            Assert.Equal(d.FillVolume, D(r["fill_volume"]));
            Assert.Equal(d.TargetHeightLines.Count, I(r["target_height_lines"]));
            var g = JsonNode.Parse(File.ReadAllText(target)).AsObject();
            Assert.Equal(d.TargetHeightLines.Count, g["features"].AsArray().Count);
            Assert.True(d.TargetHeightLines.Count > 0);
            Assert.Equal(d.TargetHeightLines.Select(l => l.Height), g["features"].AsArray().Select(f => D(f["properties"]["height"])));
        }

        static string TerrainSlopeDesignSnake(TerrainSlopeDesign.Statuses s) => s == TerrainSlopeDesign.Statuses.NoDaylight ? "no_daylight" : "success";

        [Fact]
        public void ToeLeavingTheTerrain_NoDaylight_IsAResultWithAWarning()
        {
            var terrain = Terrain(Vector3d.Zero);
            string t = WriteTerrain("nd_terrain.obj", terrain), toes = m.Path_("nd.geojson");
            File.WriteAllText(toes, Collection(PolygonFeature(Square(new Vector3d(37.3, 1.1, 0)), "\"side\": \"outside\", \"slope\": 1")));
            var (exit, r) = Run("slope", t, toes, m.Path_("nd.g3mesh"), "--tolerance", "0.05", "--slope-margin", "1e-4");
            Assert.True(exit == 0, r.ToJsonString());
            Assert.Equal("no_daylight", S(r["design_status"]));
            Assert.False(B(r["complete"]));
            Assert.Contains(r["warnings"].AsArray(), w => S(w).StartsWith("no_daylight"));
            Assert.Contains(r["catch_lines"].AsArray(), l => S(l["end_reason"]) == "footprint_boundary" || S(l["start_reason"]) == "footprint_boundary");
        }

        [Fact]
        public void LocalFrame_OutputsAreLocal_WithTheOrigin()
        {
            // the pit at UTM; the outputs are the library's local outputs, bit for bit, and used_origin is reported
            var c = new Vector3d(632117.25, 5712043.5, 113.75);
            var terrain = Terrain(c);
            string t = m.Path_("utm_terrain.g3mesh");
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(t, terrain, WriteOptions.Defaults).code);
            string toes = m.Path_("utm.geojson");
            File.WriteAllText(toes, "{\"type\": \"FeatureCollection\", \"crs\": {\"type\": \"name\", \"properties\": {\"name\": \"EPSG:25832\"}}, \"features\": [" +
                PolygonFeature(Square(c), "\"side\": \"outside\", \"slope\": 1") + "]}");
            string output = m.Path_("utm.g3mesh"), lines = m.Path_("utm_catch.geojson");
            var (exit, r) = Run("slope", t, toes, output, "--tolerance", "0.05", "--local-frame", "--catch-lines", lines);
            Assert.True(exit == 0, r.ToJsonString());
            var d = Library(ReadMesh(t), new List<SlopeToe> { Toe(Square(c), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)) }, x => x.LocalFrame = true);
            Assert.Equal(TerrainSlopeDesign.Statuses.Success, d.Status);
            Assert.False(d.LocalFrameFallback);
            Assert.True(B(r["local_frame"]));
            Assert.False(B(r["local_frame_fallback"]));
            Assert.Equal(d.UsedOrigin.x, D(r["used_origin"][0]));
            Assert.Equal(d.UsedOrigin.y, D(r["used_origin"][1]));
            AssertSameMesh(d.Composite, output);
            Assert.True(ReadMesh(output).GetBounds().Max.xy.Length < 100);
            Assert.Contains(r["warnings"].AsArray(), w => S(w).StartsWith("local_frame:"));
            // (the crs passed through, to the report and the lines; the lines carry the origin)
            Assert.Equal("EPSG:25832", S(r["crs"]["properties"]["name"]));
            var g = JsonNode.Parse(File.ReadAllText(lines)).AsObject();
            Assert.Equal("EPSG:25832", S(g["crs"]["properties"]["name"]));
            Assert.Equal(d.UsedOrigin.x, D(g["used_origin"][0]));
            Assert.Equal(d.CatchLines[0].Points[0], new Vector3d(D(g["features"][0]["geometry"]["coordinates"][0][0]),
                D(g["features"][0]["geometry"]["coordinates"][0][1]), D(g["features"][0]["geometry"]["coordinates"][0][2])));
        }

        [Fact]
        public void InlineToesWithDefaults_AndAuto()
        {
            // a bare Polygon geometry inline, side and slope from the options; mixed cut and fill
            var terrain = Terrain(Vector3d.Zero);
            string t = WriteTerrain("inline_terrain.obj", terrain);
            // (at z 5 the toe meets the terrain on its crossing: TerrainSlopeAutoToeOnTerrainTests)
            var ring = Square(new Vector3d(0, 0, 5));
            string geo = "{\"type\": \"Polygon\", \"coordinates\": [[" + string.Join(", ", ring.Concat(new[] { ring[0] }).Select(Pos)) + "]]}";
            var (exit, r) = Run("slope", t, "--toes-geojson", geo, "--output", m.Path_("inline.g3mesh"), "--tolerance", "0.05",
                "--side", "outside", "--slope", "degrees:40", "--direction", "auto");
            Assert.True(exit == 0, r.ToJsonString());
            var d = Library(ReadMesh(t), new List<SlopeToe> { Toe(ring, true, SlopeToe.Sides.Outside, SlopeSpec.Degrees(40)) }, x => x.Direction = TerrainSlopeDesign.Directions.Auto);
            Assert.Equal(TerrainSlopeDesignSnake(d.Status), S(r["design_status"]));
            AssertSameMesh(d.Composite, m.Path_("inline.g3mesh"));
            Assert.Equal(d.CutVolume, D(r["cut_volume"]));
            Assert.Equal(d.FillVolume, D(r["fill_volume"]));
        }

        [Fact]
        public void NoComposite_CatchLinesOnly()
        {
            string t = WriteTerrain("none_terrain.obj", Terrain(Vector3d.Zero)), toes = m.Path_("none.geojson");
            File.WriteAllText(toes, Collection(PolygonFeature(Square(Vector3d.Zero), "\"side\": \"outside\", \"slope\": 1")));
            var (exit, r) = Run("slope", t, toes, "--tolerance", "0.05", "--composite", "none", "--catch-lines", m.Path_("none.geojson.out"));
            Assert.True(exit == 0, r.ToJsonString());
            Assert.Null(r["mesh"]);
            Assert.Equal(1, r["catch_lines"].AsArray().Count);
        }

        static void AssertError(int exit, JsonObject r, string code, string contains = null)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
            if (contains != null) Assert.Contains(contains, S(r["message"]));
        }

        [Fact]
        public void InputErrors()
        {
            string t = WriteTerrain("err_terrain.obj", Terrain(Vector3d.Zero)), toes = m.Path_("err.geojson"), o = m.Path_("err.g3mesh");
            var sq = Square(Vector3d.Zero);
            (int, JsonObject) run(string json, params string[] extra) {
                File.WriteAllText(toes, json);
                return Run(new[] { "slope", t, toes, o, "--tolerance", "0.05" }.Concat(extra).ToArray());
            }
            var (exit, r) = run(Collection(PolygonFeature(sq, "\"slope\": 1")));
            AssertError(exit, r, ResultCodes.ReadError, "side");
            (exit, r) = run(Collection(PolygonFeature(sq, "\"side\": \"outside\"")));
            AssertError(exit, r, ResultCodes.ReadError, "slope");
            (exit, r) = run(Collection(PolygonFeature(sq, "\"side\": \"up\", \"slope\": 1")));
            AssertError(exit, r, ResultCodes.ReadError, "side");
            (exit, r) = run(Collection(PolygonFeature(sq, "\"side\": \"outside\", \"slope\": -1")));
            AssertError(exit, r, ResultCodes.ReadError, "valid slope");
            (exit, r) = run(Collection(PolygonFeature(sq, "\"side\": \"outside\", \"slope\": {\"degrees\": 95}")));
            AssertError(exit, r, ResultCodes.ReadError, "valid slope");
            (exit, r) = run(Collection(PolygonFeature(sq, "\"side\": \"outside\", \"segment_slopes\": [1, 1]")));
            AssertError(exit, r, ResultCodes.ReadError, "segment_slopes");
            (exit, r) = run(Collection(LineFeature(sq.Take(1).ToList(), "\"side\": \"left\", \"slope\": 1")));
            AssertError(exit, r, ResultCodes.ReadError, "at least 2");
            (exit, r) = run("{\"type\": \"Feature\", \"geometry\": {\"type\": \"LineString\", \"coordinates\": [[0, 0], [1, 1]]}, \"properties\": {\"side\": \"left\", \"slope\": 1}}");
            AssertError(exit, r, ResultCodes.ReadError, "[x, y, z]");
            (exit, r) = run("{\"type\": \"Polygon\", \"coordinates\": [[[0,0,0],[1,0,0],[1,1,0],[0,0,0]], [[0.2,0.2,0],[0.4,0.2,0],[0.3,0.3,0],[0.2,0.2,0]]]}", "--side", "outside", "--slope", "1");
            AssertError(exit, r, ResultCodes.ReadError, "holes");
            (exit, r) = run("{\"type\": \"Point\", \"coordinates\": [0, 0, 0]}", "--side", "outside", "--slope", "1");
            AssertError(exit, r, ResultCodes.ReadError, "LineString or a Polygon");
            (exit, r) = run("{\"type\": \"FeatureCollection\", \"features\": []}");
            AssertError(exit, r, ResultCodes.ReadError, "no toe");
            (exit, r) = run("not json");
            AssertError(exit, r, ResultCodes.ReadError, "not valid JSON");
            // a self-crossing toe: the library's InvalidInput, with its status in the error result
            var bow = new List<Vector3d> { new Vector3d(-5, -5, 0), new Vector3d(5, 5, 0), new Vector3d(5, -5, 0), new Vector3d(-5, 5, 0) };
            (exit, r) = run(Collection(PolygonFeature(bow, "\"side\": \"outside\", \"slope\": 1")));
            AssertError(exit, r, ResultCodes.ReadError, "InvalidInput");
            Assert.Equal("invalid_input", S(r["design_status"]));
            Assert.Equal("invalid_input", S(r["status_kind"]));
            // the arguments
            string ok = Collection(PolygonFeature(sq, "\"side\": \"outside\", \"slope\": 1"));
            (exit, r) = run(ok, "--tolerance", "0");
            AssertError(exit, r, ResultCodes.InvalidArguments, "given twice");
            File.WriteAllText(toes, ok);
            (exit, r) = Run("slope", t, toes, o, "--tolerance", "-1");
            AssertError(exit, r, ResultCodes.InvalidArguments, "tolerance");
            (exit, r) = Run("slope", t, toes, "--tolerance", "0.05");
            AssertError(exit, r, ResultCodes.InvalidArguments, "output");
            (exit, r) = Run("slope", t, toes, o, "--tolerance", "0.05", "--composite", "none");
            AssertError(exit, r, ResultCodes.InvalidArguments, "output");
            (exit, r) = Run("slope", t, toes, o, "--tolerance", "0.05", "--toes-geojson", ok);
            AssertError(exit, r, ResultCodes.InvalidArguments, "exactly one");
            (exit, r) = Run("slope", t, toes, o, "--tolerance", "0.05", "--target-lines", m.Path_("x.geojson"));
            AssertError(exit, r, ResultCodes.InvalidArguments, "target_heights");
            (exit, r) = Run("slope", t, toes, o, "--tolerance", "0.05", "--target-heights", "1,x");
            AssertError(exit, r, ResultCodes.InvalidArguments, "target_heights");
            (exit, r) = Run("slope", t, toes, o, "--tolerance", "0.05", "--slope", "steep:1");
            AssertError(exit, r, ResultCodes.InvalidArguments, "kind");
            (exit, r) = Run("slope", t, toes, m.Path_("err.xyz"), "--tolerance", "0.05");
            AssertError(exit, r, ResultCodes.UnknownFormat);
            (exit, r) = Run("slope", m.Path_("missing.obj"), toes, o, "--tolerance", "0.05");
            AssertError(exit, r, ResultCodes.ReadError);
        }

        // the status paths of the library's failures, with the design's status in the error result
        [Fact]
        public void DesignFailures_CarryTheDesignStatus()
        {
            string t = WriteTerrain("st_terrain.obj", Terrain(Vector3d.Zero)), toes = m.Path_("st.geojson"), o = m.Path_("st.g3mesh");
            // a toe that rises 30 over 20 against a slope of 1:1: ToeSteeperThanSlope (operation_failed), segment 0 of toe 0
            File.WriteAllText(toes, Collection(LineFeature(new List<Vector3d> { new Vector3d(-10, 0, 0), new Vector3d(10, 0, 30) }, "\"side\": \"left\", \"slope\": 1")));
            var (exit, r) = Run("slope", t, toes, o, "--tolerance", "0.05");
            AssertError(exit, r, ResultCodes.OperationFailed, "ToeSteeperThanSlope");
            Assert.Equal("toe_steeper_than_slope", S(r["design_status"]));
            Assert.Equal("geometrically_infeasible", S(r["status_kind"]));
            Assert.Equal("[[0,0]]", r["toe_problems"].ToJsonString());
            Assert.False(File.Exists(o));
            // a self-crossing toe inline: invalid_arguments (the toes are the argument)
            var bow = new List<Vector3d> { new Vector3d(-5, -5, 0), new Vector3d(5, 5, 0), new Vector3d(5, -5, 0), new Vector3d(-5, 5, 0) };
            (exit, r) = Run("slope", t, "--toes-geojson", Collection(PolygonFeature(bow, "\"side\": \"outside\", \"slope\": 1")), "--output", o, "--tolerance", "0.05");
            AssertError(exit, r, ResultCodes.InvalidArguments, "InvalidInput");
            Assert.True(r["toe_problems"].AsArray().Count > 0);
            // a tolerance below the numerical margins: a parameter, invalid_arguments also with a toes file
            File.WriteAllText(toes, Collection(PolygonFeature(Square(Vector3d.Zero), "\"side\": \"outside\", \"slope\": 1")));
            (exit, r) = Run("slope", t, toes, o, "--tolerance", "1e-12");
            AssertError(exit, r, ResultCodes.InvalidArguments, "Tolerance");
            Assert.Equal("invalid_input", S(r["design_status"]));
            // a terrain that is not a height field under the slope (a loose triangle over the pit): read_error, also inline
            var folded = Terrain(Vector3d.Zero);
            int a = folded.AppendVertex(new Vector3d(-12, -2, 6)), b = folded.AppendVertex(new Vector3d(-6, -2, 6)), c = folded.AppendVertex(new Vector3d(-9, 3, 6));
            folded.AppendTriangle(a, b, c);
            string tf = WriteTerrain("st_folded.obj", folded);
            (exit, r) = Run("slope", tf, "--toes-geojson", Collection(PolygonFeature(Square(Vector3d.Zero), "\"side\": \"outside\", \"slope\": 1")), "--output", o, "--tolerance", "0.05");
            AssertError(exit, r, ResultCodes.ReadError, "NotHeightField");
            Assert.Equal("not_height_field", S(r["design_status"]));
            Assert.Equal("invalid_input", S(r["status_kind"]));
        }

        [Fact]
        public void ParserAndArgumentChecks()
        {
            string t = WriteTerrain("pc_terrain.obj", Terrain(Vector3d.Zero)), toes = m.Path_("pc.geojson"), o = m.Path_("pc.g3mesh");
            var sq = Square(Vector3d.Zero);
            (int, JsonObject) run(string json, params string[] extra) {
                File.WriteAllText(toes, json);
                return Run(new[] { "slope", t, toes, o, "--tolerance", "0.05" }.Concat(extra).ToArray());
            }
            string ok = Collection(PolygonFeature(sq, "\"side\": \"outside\", \"slope\": 1"));
            var (exit, r) = run(ok, "--slope-margin", "0.5");
            AssertError(exit, r, ResultCodes.InvalidArguments, "slope_margin");
            (exit, r) = run(ok, "--direction", "auto", "--height-limit", "20");
            AssertError(exit, r, ResultCodes.InvalidArguments, "height_limit");
            (exit, r) = run(Collection(LineFeature(sq, "\"side\": \"outside\", \"slope\": 1")));
            AssertError(exit, r, ResultCodes.ReadError, "open toe");
            (exit, r) = run(Collection(PolygonFeature(sq, "\"side\": \"outside\", \"slope\": 1, \"segment_slopes\": [1, 1, 1, 1]")));
            AssertError(exit, r, ResultCodes.ReadError, "give one of them");
            (exit, r) = run(Collection(PolygonFeature(sq, "\"side\": \"outside\", \"slope\": 1, \"slope\": 2")));
            AssertError(exit, r, ResultCodes.ReadError, "twice");
            (exit, r) = run("{\"type\": \"Feature\", \"properties\": [1], \"geometry\": {\"type\": \"LineString\", \"coordinates\": [[0,0,0],[1,0,0]]}}", "--side", "left", "--slope", "1");
            AssertError(exit, r, ResultCodes.ReadError, "not an object");
            // a GeoJSON output into a missing directory: write_error before anything is written
            (exit, r) = run(ok, "--catch-lines", Path.Combine(m.Dir, "no_such_dir", "c.geojson"));
            AssertError(exit, r, ResultCodes.WriteError, "directory");
            Assert.False(File.Exists(o));
            // 1:n on the command line, as conventionally written
            (exit, r) = Run("slope", t, "--toes-geojson", "{\"type\": \"Polygon\", \"coordinates\": [[" + string.Join(", ", sq.Concat(new[] { sq[0] }).Select(Pos)) + "]]}",
                "--output", o, "--tolerance", "0.05", "--side", "outside", "--slope", "1:1.5");
            Assert.True(exit == 0, r.ToJsonString());
            Assert.True(File.Exists(o));
        }

        // an error result keeps command, status, code and message when the details carry the same keys
        [Fact]
        public void ErrorData_DoesNotOverrideTheResultFields()
        {
            var cmd = new Command() { Name = "x", Run = a => throw new CommandException(ResultCodes.OperationFailed, "m",
                new JsonObject() { ["code"] = "other", ["status"] = "ok", ["detail"] = 3 }) };
            var r = Commands.Run(cmd, cmd.ParseCommandLine(new string[0]));
            Assert.Equal(ResultCodes.OperationFailed, S(r["code"]));
            Assert.Equal("error", S(r["status"]));
            Assert.Equal("m", S(r["message"]));
            Assert.Equal(3, I(r["detail"]));
        }

        // structural mutations (wrong types, missing parts, odd values) of a valid input: a result code, never internal_error
        [Fact]
        public void StructurallyWrongGeoJson_NeverAnInternalError()
        {
            string t = WriteTerrain("sw_terrain.obj", Terrain(Vector3d.Zero, 20));
            string geom = "{\"type\": \"Polygon\", \"coordinates\": [[[-3,-3,0],[3,-3,0],[3,3,0],[-3,3,0],[-3,-3,0]]]}";
            var cases = new[] {
                "{\"type\": \"Feature\", \"geometry\": " + geom + ", \"properties\": {\"side\": 1, \"slope\": 1}}",
                "{\"type\": \"Feature\", \"geometry\": " + geom + ", \"properties\": {\"side\": \"outside\", \"slope\": \"1\"}}",
                "{\"type\": \"Feature\", \"geometry\": " + geom + ", \"properties\": {\"side\": \"outside\", \"slope\": {\"ratio\": {\"ratio\": 1}}}}",
                "{\"type\": \"Feature\", \"geometry\": " + geom + ", \"properties\": {\"side\": \"outside\", \"slope\": {\"ratio\": 1, \"degrees\": 3}}}",
                "{\"type\": \"Feature\", \"geometry\": " + geom + ", \"properties\": {\"side\": \"outside\", \"slope\": 1e400}}",
                "{\"type\": \"Feature\", \"geometry\": " + geom + ", \"properties\": {\"side\": \"outside\", \"segment_slopes\": \"1\"}}",
                "{\"type\": \"Feature\", \"geometry\": {\"type\": \"Polygon\", \"coordinates\": [[[\"-3\",-3,0],[3,-3,0],[3,3,0],[-3,3,0],[\"-3\",-3,0]]]}, \"properties\": {\"side\": \"outside\", \"slope\": 1}}",
                "{\"type\": \"Feature\", \"geometry\": {\"type\": \"Polygon\", \"coordinates\": [[[-3,-3],[3,-3],[3,3],[-3,-3]]]}, \"properties\": {\"side\": \"outside\", \"slope\": 1}}",
                "{\"type\": \"Feature\", \"geometry\": {\"type\": \"Polygon\", \"coordinates\": [[-3,-3,0]]}, \"properties\": {\"side\": \"outside\", \"slope\": 1}}",
                "{\"type\": \"Feature\", \"geometry\": {\"type\": \"Polygon\", \"coordinates\": 7}, \"properties\": {\"side\": \"outside\", \"slope\": 1}}",
                "{\"type\": \"Feature\", \"geometry\": {\"type\": \"Polygon\", \"coordinates\": [[[-3,-3,0],[3,-3,0],[3,3,0],[-3,3,0],[-3,-3,0]]]}, \"properties\": {\"side\": \"outside\", \"slope\": 1}, \"crs\": 5}",
                "{\"type\": \"FeatureCollection\", \"crs\": [1, 2], \"features\": [" + "{\"type\": \"Feature\", \"geometry\": " + geom + ", \"properties\": {\"side\": \"outside\", \"slope\": 1}}" + "]}",
                "{\"type\": \"FeatureCollection\", \"features\": [" + geom + "]}",
                "{\"type\": \"FeatureCollection\", \"features\": {}}",
                "{\"type\": \"GeometryCollection\", \"geometries\": [" + geom + "]}",
                "{\"type\": \"Feature\", \"geometry\": null, \"properties\": {\"side\": \"outside\", \"slope\": 1}}",
                "{\"type\": \"Feature\", \"geometry\": " + geom + ", \"properties\": {\"side\": \"outside\", \"slope\": -0.0}}",
                "{\"type\": \"Feature\", \"geometry\": {\"type\": \"LineString\", \"coordinates\": [[0,0,0],[0,0,0]]}, \"properties\": {\"side\": \"left\", \"slope\": 1}}",
                "[]", "null", "3", "\"x\"",
            };
            var allowed = new[] { ResultCodes.Ok, ResultCodes.InvalidArguments, ResultCodes.OperationFailed };
            foreach (string text in cases) {
                var (exit, r) = Run("slope", t, "--toes-geojson", text, "--output", m.Path_("sw.g3mesh"), "--tolerance", "0.05", "--side", "outside", "--slope", "1");
                Assert.True(allowed.Contains(S(r["code"])), text + " -> " + r.ToJsonString());
                Assert.Equal(ResultCodes.ExitCode(S(r["code"])), exit);
            }
        }

        // the example of the README parses (two toes, the crs kept)
        [Fact]
        public void ReadmeExample_Parses()
        {
            string readme = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "g3cli", "README.md"));
            int i = readme.IndexOf("## Slope design input"), j = readme.IndexOf("```json", i) + 7, k = readme.IndexOf("```", j);
            var r = SlopeToesGeoJson.Parse(readme.Substring(j, k - j), ResultCodes.ReadError, "the README");
            Assert.Equal(2, r.Toes.Count);
            Assert.True(r.Toes[0].Closed);
            Assert.Equal(4, r.Toes[0].Points.Count);
            Assert.Equal(SlopeToe.Sides.Left, r.Toes[1].Side);
            Assert.Equal(SlopeSpec.Ratio(1.5).Value, r.Toes[1].CutStart[0].Value);
            Assert.Equal(SlopeSpec.Ratio(2).Value, r.Toes[1].FillStart[0].Value);
            Assert.Equal("EPSG:25832", S(r.Crs["properties"]["name"]));
        }

        static string RepoRoot()
        {
            string d = AppContext.BaseDirectory;
            while (d != null && File.Exists(Path.Combine(d, "CLAUDE.md")) == false) d = Path.GetDirectoryName(d);
            Assert.NotNull(d);
            return d;
        }

        [Fact]
        public void FuzzedGeoJson_NeverAnInternalError()
        {
            // mutations of a valid input (deleted, replaced, duplicated characters, truncation): every one is answered with a
            // result code, never an exception or internal_error
            string t = WriteTerrain("fuzz_terrain.obj", Terrain(Vector3d.Zero, 20));
            string valid = Collection(PolygonFeature(Square(Vector3d.Zero, 3), "\"side\": \"outside\", \"slope\": {\"ratio\": 1}"),
                LineFeature(new List<Vector3d> { new Vector3d(-15, -15, 4), new Vector3d(-12, -15, 4) }, "\"side\": \"left\", \"cut_slope\": 1, \"fill_slope\": 2"));
            var rnd = new Random(20261103);
            const string chars = "[]{},:\"0123456789.-eE truefalsnulPolygonLineStringside";
            var allowed = new[] { ResultCodes.Ok, ResultCodes.InvalidArguments, ResultCodes.OperationFailed };
            string output = m.Path_("fuzz.g3mesh");
            for (int iter = 0; iter < 200; ++iter) {
                var sb = new StringBuilder(valid);
                int edits = 1 + rnd.Next(3);
                for (int e = 0; e < edits && sb.Length > 1; ++e) {
                    int pos = rnd.Next(sb.Length);
                    switch (rnd.Next(4)) {
                        case 0: sb.Remove(pos, 1); break;
                        case 1: sb[pos] = chars[rnd.Next(chars.Length)]; break;
                        case 2: sb.Insert(pos, sb[pos]); break;
                        default: sb.Length = pos; break;
                    }
                }
                string text = sb.ToString();
                var (exit, r) = Run("slope", t, "--toes-geojson", text, "--output", output, "--tolerance", "0.05", "--composite", "replacement");
                Assert.True(allowed.Contains(S(r["code"])), text + " -> " + r.ToJsonString());
                Assert.Equal(ResultCodes.ExitCode(S(r["code"])), exit);
            }
        }
    }
}
