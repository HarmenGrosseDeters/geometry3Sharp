using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> polygon2d: the ops simplify, simplify_lines, clip_lines and offset_lines (docs/design/CLI_MCP_UPDATE.md section 6). </summary>
    public class PolygonLinesCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public PolygonLinesCliTests(TestMeshes meshes) { m = meshes; }

        static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);
        static string Line(double s, params double[] xy) =>
            "{\"type\":\"LineString\",\"coordinates\":[" + string.Join(",", Enumerable.Range(0, xy.Length / 2).Select(i => $"[{N(xy[2 * i] * s)},{N(xy[2 * i + 1] * s)}]")) + "]}";
        static string Square(double s, double x0 = 0, double y0 = 0, double size = 10) =>
            $"{{\"type\":\"Polygon\",\"coordinates\":[[[{N(s * x0)},{N(s * y0)}],[{N(s * (x0 + size))},{N(s * y0)}],[{N(s * (x0 + size))},{N(s * (y0 + size))}],[{N(s * x0)},{N(s * (y0 + size))}],[{N(s * x0)},{N(s * y0)}]]]}}";

        (int exit, JsonObject r) P(string op, string subject, string clip = null, params string[] more)
        {
            var args = new System.Collections.Generic.List<string> { "polygon2d", "--op", op, "--subject-geojson", subject, "--compact" };
            if (clip != null) { args.Add("--clip-geojson"); args.Add(clip); }
            args.AddRange(more);
            return Run(args.ToArray());
        }

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        [Fact]
        public void ClipLines_KeepsThePiecesInsideOrOutside()
        {
            string line = Line(1, -5, 5, 15, 5);
            var (e1, inside) = P("clip_lines", line, Square(1));
            Assert.Equal(0, e1);
            Assert.Equal(1, I(inside["result"]["lines"]));
            Assert.Equal(10.0, D(inside["result"]["length"]), 6);
            Assert.Equal(0.0, D(inside["result"]["bounds"]["min"][0]), 6);
            Assert.Equal(10.0, D(inside["result"]["bounds"]["max"][0]), 6);
            Assert.False(B(inside["empty_result"]));
            var (e2, outside) = P("clip_lines", line, Square(1), "--clip-type", "difference");
            Assert.Equal(0, e2);
            Assert.Equal(2, I(outside["result"]["lines"]));
            Assert.Equal(10.0, D(outside["result"]["length"]), 6);                   // 5 on each side
            var (e3, none) = P("clip_lines", Line(1, 20, 20, 30, 30), Square(1));
            Assert.Equal(0, e3);
            Assert.True(B(none["empty_result"]));
            Assert.Equal(0, I(none["result"]["lines"]));
        }

        [Fact]
        public void ClipLines_MultiLineString_HolesAndTheGeoJsonResult()
        {
            // a clip with a hole [4,6]^2; two lines through it
            string clip = "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[10,0],[10,10],[0,10],[0,0]],[[4,4],[4,6],[6,6],[6,4],[4,4]]]}";
            string lines = "{\"type\":\"MultiLineString\",\"coordinates\":[[[-1,5],[11,5]],[[5,-1],[5,11]]]}";
            var (exit, r) = P("clip_lines", lines, clip, "--return-geojson");
            Assert.Equal(0, exit);
            Assert.Equal(2, I(r["subject"]["lines"]));
            Assert.Equal(4, I(r["result"]["lines"]));                                  // each line is cut by the hole
            Assert.Equal(4 + 4 + 4 + 4.0, D(r["result"]["length"]), 6);
            var geometry = r["result_geojson"]["features"][0]["geometry"];
            Assert.Equal("MultiLineString", S(geometry["type"]));
            Assert.Equal(4, geometry["coordinates"].AsArray().Count);
            string output = m.Path_("clipped.geojson");
            var (e2, r2) = P("clip_lines", lines, clip, "--output", output);
            Assert.Equal(0, e2);
            Assert.Equal("MultiLineString", S(JsonNode.Parse(File.ReadAllText(output))["features"][0]["geometry"]["type"]));
        }

        [Fact]
        public void OffsetLines_EndTypesGiveTheBandsOfTheLibrary()
        {
            string line = Line(1, -5, 5, 15, 5);                                         // length 20, half width 1
            double Area(string end) { var (e, r) = P("offset_lines", line, null, "--delta", "1", "--end-type", end); Assert.Equal(0, e); return D(r["result"]["area"]); }
            Assert.Equal(40.0, Area("butt"), 6);
            Assert.Equal(44.0, Area("square"), 6);
            Assert.InRange(Area("round"), 40 + Math.PI * 0.99, 40 + Math.PI);          // two half discs
            Assert.InRange(Area("joined"), 40 + Math.PI * 0.99, 40 + Math.PI);          // (an open line: as round)
            var (exit, def) = P("offset_lines", line, null, "--delta", "1");
            Assert.Equal(0, exit);
            Assert.Equal(Area("round"), D(def["result"]["area"]));                       // round is the default
            Assert.Equal(1, I(def["result"]["polygons"]));
            var (e2, closedLine) = P("offset_lines", Line(1, 0, 0, 10, 0, 10, 10, 0, 10, 0, 0), null, "--delta", "1", "--end-type", "joined", "--join", "miter");
            Assert.Equal(0, e2);
            Assert.Equal(1, I(closedLine["result"]["holes"]));                           // a closed line offset on both sides: a ring
        }

        [Fact]
        public void Simplify_RemovesNearCollinearVertices()
        {
            string poly = "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[2,0.001],[4,0],[5,0.5],[6,0],[10,0],[10,10],[0,10],[0,0]]]}";
            var (e0, same) = P("simplify", poly, null, "--epsilon", "0");
            Assert.Equal(0, e0);
            Assert.Equal(8, I(same["result"]["vertices"]));
            var (e1, small) = P("simplify", poly, null, "--epsilon", "0.01");
            Assert.Equal(0, e1);
            Assert.Equal(7, I(small["result"]["vertices"]));
            var (e2, coarse) = P("simplify", poly, null, "--epsilon", "1");
            Assert.Equal(0, e2);
            Assert.Equal(4, I(coarse["result"]["vertices"]));
            Assert.Equal(100.0, D(coarse["result"]["area"]), 6);

            string line = Line(1, 0, 0, 2, 0.001, 4, 0, 5, 0.5, 6, 0, 8, 0.001, 10, 0);
            var (e3, ls) = P("simplify_lines", line, null, "--epsilon", "1");
            Assert.Equal(0, e3);
            Assert.Equal(7, I(ls["subject"]["vertices"]));
            Assert.Equal(2, I(ls["result"]["vertices"]));
            Assert.Equal(10.0, D(ls["result"]["length"]), 6);
            var (e4, ls2) = P("simplify_lines", line, null, "--epsilon", "0.01", "--return-geojson");
            Assert.Equal(0, e4);
            Assert.Equal(5, I(ls2["result"]["vertices"]));
            Assert.Equal(5, ls2["result_geojson"]["features"][0]["geometry"]["coordinates"][0].AsArray().Count);
        }

        [Theory]
        [InlineData(-20)]
        [InlineData(-3)]
        [InlineData(0)]
        [InlineData(7)]
        [InlineData(30)]
        public void PowerOfTwoScales_GiveTheSameResultBitForBit(int exponent)
        {
            double s = Math.Pow(2, exponent);
            string clip = Square(1), clipS = Square(s);
            var (_, a) = P("clip_lines", Line(1, -5, 3, 15, 7, 15, 9), clip, "--return-geojson");
            var (e, b) = P("clip_lines", Line(s, -5, 3, 15, 7, 15, 9), clipS, "--return-geojson");
            Assert.Equal(0, e);
            Assert.Equal(s * D(a["result"]["length"]), D(b["result"]["length"]));
            var (_, c) = P("offset_lines", Line(1, 0, 0, 10, 3), null, "--delta", "0.5");
            var (e2, d) = P("offset_lines", Line(s, 0, 0, 10, 3), null, "--delta", N(0.5 * s));
            Assert.Equal(0, e2);
            Assert.Equal(I(c["result"]["vertices"]), I(d["result"]["vertices"]));
            Assert.Equal(s * s * D(c["result"]["area"]), D(d["result"]["area"]));
            var (_, f) = P("simplify", Square(1, 0, 0, 10), null, "--epsilon", "0.5");
            var (e3, g) = P("simplify", Square(s, 0, 0, 10), null, "--epsilon", N(0.5 * s));
            Assert.Equal(0, e3);
            Assert.Equal(s * s * D(f["result"]["area"]), D(g["result"]["area"]));
        }

        [Fact]
        public void BadArguments_AreInvalidArguments()
        {
            string line = Line(1, 0, 0, 10, 0);
            foreach (var (op, more) in new[] {
                ("simplify", new string[0]),                                                      // no epsilon
                ("simplify", new[] { "--epsilon", "-1" }), ("simplify", new[] { "--epsilon", "NaN" }),
                ("offset_lines", new string[0]),                                                  // no delta
                ("offset_lines", new[] { "--delta", "-1" }), ("offset_lines", new[] { "--delta", "0" }), ("offset_lines", new[] { "--delta", "NaN" }),
                ("offset_lines", new[] { "--delta", "1", "--epsilon", "1" }),
                ("offset_lines", new[] { "--delta", "1", "--fill-rule", "even_odd" }),
                ("offset_lines", new[] { "--delta", "1", "--miter-limit", "2" }),                 // join is round
                ("offset_lines", new[] { "--delta", "1e9" }),                                     // far beyond the extent
                ("simplify_lines", new[] { "--epsilon", "1", "--fill-rule", "even_odd" }),
                ("simplify_lines", new[] { "--epsilon", "1", "--delta", "1" }),
                ("simplify_lines", new[] { "--epsilon", "1", "--end-type", "butt" }),
                ("clip_lines", new string[0]),                                                    // no clip
                ("union", new[] { "--clip-type", "difference" }),
                ("union", new[] { "--epsilon", "1" }),
            }) {
                var (exit, r) = P(op, op == "simplify" || op == "union" ? Square(1) : line, op == "union" ? Square(1) : null, more);
                AssertError(exit, r, "invalid_arguments");
            }
            var (e1, r1) = P("simplify", Square(1), Square(1), "--epsilon", "1");               // simplify takes no clip
            AssertError(e1, r1, "invalid_arguments");
            var (e2, r2) = P("clip_lines", Square(1), Square(1));                              // the subject must be lines
            AssertError(e2, r2, "invalid_arguments");
            var (e3, r3) = P("offset_lines", Square(1), null, "--delta", "1");
            AssertError(e3, r3, "invalid_arguments");
            var (e4, r4) = P("clip_lines", line, line);                                        // the clip must be polygons
            AssertError(e4, r4, "invalid_arguments");
            var (e5, r5) = P("simplify", line, null, "--epsilon", "1");                         // polygons for simplify
            AssertError(e5, r5, "invalid_arguments");
        }

        [Theory]
        [InlineData("{\"type\":\"LineString\",\"coordinates\":[[0,0]]}")]                       // one position
        [InlineData("{\"type\":\"LineString\",\"coordinates\":[[0,0],[1,\"a\"]]}")]
        [InlineData("{\"type\":\"LineString\",\"coordinates\":[[0,0],[1]]}")]
        [InlineData("{\"type\":\"LineString\"}")]
        [InlineData("{\"type\":\"MultiLineString\",\"coordinates\":[1]}")]
        [InlineData("{\"type\":\"Point\",\"coordinates\":[0,0]}")]
        [InlineData("{\"type\":\"FeatureCollection\",\"features\":[]}")]
        public void BadLines_AreRejected(string json)
        {
            var (exit, r) = P("simplify_lines", json, null, "--epsilon", "1");
            Assert.NotEqual(0, exit);
            Assert.Contains(S(r["code"]), new[] { "invalid_arguments", "empty_mesh" });
            string file = m.Path_("bad_lines.geojson");
            File.WriteAllText(file, json);
            var (e2, r2) = Run("polygon2d", file, "--op", "simplify_lines", "--epsilon", "1", "--compact");
            Assert.Contains(S(r2["code"]), new[] { "read_error", "empty_mesh" });
        }

        [Fact]
        public void OffsetLines_DeltaIsBoundedByTheSizeOfTheLines()
        {
            AssertError(P("offset_lines", Line(1, 1, 1, 1, 1), null, "--delta", "1e300").exit, P("offset_lines", Line(1, 1, 1, 1, 1), null, "--delta", "1e300").r, "invalid_arguments");
            var (e1, ok) = P("offset_lines", Line(1, 1, 1, 1, 1), null, "--delta", "100");        // a point: a disc of radius 100
            Assert.Equal(0, e1);
            Assert.InRange(D(ok["result"]["area"]), Math.PI * 100 * 100 * 0.99, Math.PI * 100 * 100);
        }

        [Fact]
        public void PolygonOpsStillRefuseLines()
        {
            var (exit, r) = P("union", Line(1, 0, 0, 10, 0));
            AssertError(exit, r, "invalid_arguments");
            Assert.Contains("Polygon", S(r["message"]));
        }
    }
}
