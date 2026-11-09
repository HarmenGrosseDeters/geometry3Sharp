using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> polygon2d (the Clipper2 adapter) on squares with known areas. </summary>
    public class PolygonCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public PolygonCliTests(TestMeshes meshes) { m = meshes; }

        static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        /// <summary> a square [x0,x1] x [y0,y1] as a ring, closed </summary>
        static string Ring(double x0, double y0, double x1, double y1, bool ccw = true)
        {
            var p = new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0) };
            if (!ccw) p = p.Reverse().ToArray();
            return "[" + string.Join(",", p.Select(v => $"[{N(v.Item1)},{N(v.Item2)}]")) + "]";
        }

        static string Poly(params string[] rings) => "{\"type\":\"Polygon\",\"coordinates\":[" + string.Join(",", rings) + "]}";

        // the subject: [0,10]^2 with a hole [4,6]^2 (area 96); the clip: [5,15] x [0,10] (area 100); the overlap has the area 50 - 2 = 48
        static string Subject(double s = 1, double dx = 0, double dy = 0) => Poly(Ring(dx, dy, dx + 10 * s, dy + 10 * s), Ring(dx + 4 * s, dy + 4 * s, dx + 6 * s, dy + 6 * s, false));
        static string Clip(double s = 1, double dx = 0, double dy = 0) => Poly(Ring(dx + 5 * s, dy, dx + 15 * s, dy + 10 * s));

        (int exit, JsonObject r) P(string op, string subject, string clip = null, params string[] more)
        {
            var args = new System.Collections.Generic.List<string> { "polygon2d", "--op", op, "--subject-geojson", subject, "--compact" };
            if (clip != null) { args.Add("--clip-geojson"); args.Add(clip); }
            args.AddRange(more);
            return Run(args.ToArray());
        }

        static double Area(JsonObject r) => D(r["result"]["area"]);

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        [Theory]
        [InlineData("union", 148.0)]
        [InlineData("intersection", 48.0)]
        [InlineData("difference", 48.0)]
        [InlineData("xor", 100.0)]
        public void Booleans_HaveTheExpectedAreas(string op, double area)
        {
            var (exit, r) = P(op, Subject(), Clip());
            Assert.Equal(0, exit);
            Assert.Equal("success", S(r["clipper_status"]));
            Assert.Equal(area, Area(r), 6);
            Assert.Equal(96.0, D(r["subject"]["area"]), 9);
            Assert.Equal(100.0, D(r["clip"]["area"]), 9);
            Assert.False(B(r["empty_result"]));
        }

        [Fact]
        public void Union_OfTheSubjectAlone_MergesItsPolygons()
        {
            string multi = "{\"type\":\"MultiPolygon\",\"coordinates\":[[" + Ring(0, 0, 10, 10) + "],[" + Ring(5, 0, 15, 10) + "]]}";
            var (exit, r) = P("union", multi);
            Assert.Equal(0, exit);
            Assert.Equal(2, I(r["subject"]["polygons"]));
            Assert.Equal(1, I(r["result"]["polygons"]));
            Assert.Equal(150.0, Area(r), 9);
            Assert.Null(r["clip"]);
        }

        [Fact]
        public void FillRule_EvenOdd_LetsOverlapsCancel()
        {
            string multi = "{\"type\":\"MultiPolygon\",\"coordinates\":[[" + Ring(0, 0, 10, 10) + "],[" + Ring(5, 0, 15, 10) + "]]}";
            var (_, nonZero) = P("union", multi);
            var (e, evenOdd) = P("union", multi, null, "--fill-rule", "even_odd");
            Assert.Equal(0, e);
            Assert.Equal(150.0, Area(nonZero), 9);
            Assert.Equal(100.0, Area(evenOdd), 9);
        }

        [Fact]
        public void Offset_JoinsGiveTheirAreas()
        {
            string square = Poly(Ring(0, 0, 10, 10));
            Assert.Equal(144.0, Area(P("offset", square, null, "--delta", "1", "--join", "miter").r), 6);
            Assert.Equal(100 + 40 + Math.PI, Area(P("offset", square, null, "--delta", "1", "--join", "round", "--arc-tolerance", "0.001").r), 1);
            Assert.Equal(140.0 + 2.0, Area(P("offset", square, null, "--delta", "1", "--join", "bevel").r), 6);   // the four corners cut: 4 triangles of 1/2
            double squareJoin = Area(P("offset", square, null, "--delta", "1", "--join", "square").r);
            Assert.InRange(squareJoin, 142.0, 144.0);                                          // the corners squared off at the offset distance
            Assert.Equal(64.0, Area(P("offset", square, null, "--delta", "-1").r), 6);
            // the hole of the subject (2 x 2) closes under an offset of 1: 12 x 12 outer ring, no hole
            var (exit, r) = P("offset", Subject(), null, "--delta", "1", "--join", "miter");
            Assert.Equal(0, exit);
            Assert.Equal(144.0, Area(r), 6);
            Assert.Equal(0, I(r["result"]["holes"]));
        }

        [Fact]
        public void Offset_MiterLimit_IsUsed()
        {
            // a sharp corner: a triangle with a 20 degree apex; a small miter limit squares it off, a large one keeps the point
            string tri = Poly("[[0,0],[10,0],[10,3.6397],[0,0]]");
            double a1 = Area(P("offset", tri, null, "--delta", "1", "--join", "miter", "--miter-limit", "1.5").r);
            double a2 = Area(P("offset", tri, null, "--delta", "1", "--join", "miter", "--miter-limit", "20").r);
            Assert.True(a2 > a1 + 0.1, $"{a2} <= {a1}");
        }

        [Fact]
        public void Result_RingsAreOrientedAndTheHolesAreKept()
        {
            var (exit, r) = P("difference", Subject(), Clip(), "--return-geojson");
            Assert.Equal(0, exit);
            Assert.Equal(1, I(r["result"]["polygons"]));
            var geom = r["result_geojson"]["features"][0]["geometry"];
            Assert.Equal("MultiPolygon", S(geom["type"]));
            var rings = geom["coordinates"][0].AsArray();
            Assert.True(SignedArea(rings[0].AsArray()) > 0);                    // outer: counterclockwise
            for (int i = 1; i < rings.Count; i++) Assert.True(SignedArea(rings[i].AsArray()) < 0);   // holes: clockwise
            Assert.Equal(S(rings[0][0].ToJsonString()), S(rings[0][rings[0].AsArray().Count - 1].ToJsonString()));     // closed
            // difference [0,10]^2 \ [5,15]x[0,10] with the hole [4,6]^2 cut open by the clip: an L-shaped region of area 48, no hole left
            Assert.Equal(48.0, Area(r), 6);
        }

        static double SignedArea(JsonArray ring)
        {
            double a = 0;
            for (int i = 0; i + 1 < ring.Count; i++)
                a += D(ring[i][0]) * D(ring[i + 1][1]) - D(ring[i + 1][0]) * D(ring[i][1]);
            return a / 2;
        }

        [Fact]
        public void DisjointIntersection_IsAnEmptyOkResult()
        {
            var (exit, r) = P("intersection", Poly(Ring(0, 0, 1, 1)), Poly(Ring(5, 5, 6, 6)), "--return-geojson");
            Assert.Equal(0, exit);
            Assert.True(B(r["empty_result"]));
            Assert.Equal(0, I(r["result"]["polygons"]));
            Assert.Empty(r["result_geojson"]["features"][0]["geometry"]["coordinates"].AsArray());
        }

        [Fact]
        public void PowerOfTwoScale_IsExact_AndUtmCoordinatesWork()
        {
            var (_, a) = P("union", Subject(), Clip(), "--return-geojson");
            var (_, b) = P("union", Subject(4), Clip(4), "--return-geojson");
            Assert.Equal(16 * Area(a), Area(b));                                               // bit for bit
            var ca = a["result_geojson"]["features"][0]["geometry"]["coordinates"].ToJsonString();
            var cb = b["result_geojson"]["features"][0]["geometry"]["coordinates"].ToJsonString();
            Assert.Equal(Scale(ca, 4), cb);
            var (e, utm) = P("union", Subject(1, 328000.5, 5642000.25), Clip(1, 328000.5, 5642000.25));
            Assert.Equal(0, e);
            Assert.Equal(148.0, Area(utm), 5);
        }

        static string Scale(string json, double f)
        {
            var node = JsonNode.Parse(json);
            void Walk(JsonNode n)
            {
                if (n is JsonArray arr) {
                    if (arr.Count == 2 && arr[0] is JsonValue) { arr[0] = D(arr[0]) * f; arr[1] = D(arr[1]) * f; }
                    else foreach (var c in arr.ToList()) Walk(c);
                }
            }
            Walk(node);
            return node.ToJsonString();
        }

        [Fact]
        public void FilesInAndOut_CrsPassThrough_ZIsIgnored()
        {
            string subject = m.Path_("subject.geojson"), clip = m.Path_("clip.geojson"), output = m.Path_("out.geojson");
            File.WriteAllText(subject, "{\"type\":\"FeatureCollection\",\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"EPSG:25832\"}},\"features\":[" +
                "{\"type\":\"Feature\",\"properties\":{},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[0,0,5],[10,0,5],[10,10,5],[0,10,5],[0,0,5]]]}}]}");
            File.WriteAllText(clip, Clip());
            var (exit, r) = Run("polygon2d", subject, clip, output, "--op", "intersection", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(50.0, Area(r), 9);
            Assert.Equal(output, S(r["output"]));
            Assert.Contains(r["warnings"].AsArray().Select(S), w => w.Contains("z"));
            var written = JsonNode.Parse(File.ReadAllText(output));
            Assert.Equal("EPSG:25832", S(written["crs"]["properties"]["name"]));
            Assert.Equal("intersection", S(written["features"][0]["properties"]["op"]));
            // the written file is valid input again
            var (e2, again) = Run("polygon2d", output, "--op", "union", "--compact");
            Assert.Equal(0, e2);
            Assert.Equal(50.0, Area(again), 9);
        }

        [Fact]
        public void PrecisionBits_AreReportedInTheFrame()
        {
            var (_, fine) = P("union", Subject(), Clip());
            var (e, coarse) = P("union", Subject(), Clip(), "--precision-bits", "12");
            Assert.Equal(0, e);
            Assert.True(D(coarse["frame"]["resolution"]) > 100 * D(fine["frame"]["resolution"]));
        }

        [Theory]
        [InlineData("{\"type\":\"LineString\",\"coordinates\":[[0,0],[1,1]]}")]
        [InlineData("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,1],[0,0]]]}")]      // fewer than 3 distinct positions
        [InlineData("{\"type\":\"Polygon\",\"coordinates\":[]}")]
        [InlineData("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,\"a\"],[1,1]]]}")]
        [InlineData("{\"type\":\"Feature\",\"geometry\":null}")]
        [InlineData("{not json")]
        [InlineData("[1,2,3]")]
        public void BadGeoJson_IsInvalidArguments(string subject)
        {
            var (exit, r) = P("union", subject);
            AssertError(exit, r, "invalid_arguments");
        }

        [Fact]
        public void BadOptions_AreInvalidArguments()
        {
            string sq = Poly(Ring(0, 0, 1, 1));
            foreach (string[] bad in new[] {
                new[] { "--op", "intersection" },                                           // needs a clip
                new[] { "--op", "offset" },                                                 // needs a delta
                new[] { "--op", "offset", "--delta", "1", "--clip-geojson", sq },
                new[] { "--op", "union", "--delta", "1" },
                new[] { "--op", "offset", "--delta", "1", "--join", "pointy" },
                new[] { "--op", "offset", "--delta", "1", "--miter-limit", "0.5" },
                new[] { "--op", "offset", "--delta", "1", "--arc-tolerance", "-1" },
                new[] { "--op", "union", "--precision-bits", "7" },
                new[] { "--op", "union", "--precision-bits", "51" },
                new[] { "--op", "union", "--fill-rule", "odd" },
                new[] { "--op", "union", "--clip-geojson", sq, "--clip", "x.geojson" },
                new[] { "--op", "sideways" },
            }) {
                var (exit, r) = Run(new[] { "polygon2d", "--subject-geojson", sq, "--compact" }.Concat(bad).ToArray());
                AssertError(exit, r, "invalid_arguments");
            }
            var (e1, r1) = Run("polygon2d", "--op", "union"); AssertError(e1, r1, "invalid_arguments");
            var (e2, r2) = Run("polygon2d", m.Path_("a.geojson"), "--subject-geojson", sq, "--op", "union"); AssertError(e2, r2, "invalid_arguments");
        }

        [Fact]
        public void FileErrors_AreResultCodes()
        {
            var (e1, r1) = Run("polygon2d", m.Path_("missing.geojson"), "--op", "union"); AssertError(e1, r1, "read_error");
            string bad = m.Path_("bad.geojson"); File.WriteAllText(bad, "{nope");
            var (e2, r2) = Run("polygon2d", bad, "--op", "union"); AssertError(e2, r2, "read_error");
            string sq = m.Path_("sq.geojson"); File.WriteAllText(sq, Poly(Ring(0, 0, 1, 1)));
            var (e4, r4) = Run("polygon2d", sq, "--op", "union", "--output", m.Path_("no_such_dir/out.geojson"), "--compact"); AssertError(e4, r4, "write_error");
        }

        [Fact]
        public void UnusableExtent_IsOperationFailedWithTheClipperStatus()
        {
            var (exit, r) = P("union", Poly(Ring(0, 0, 1e-320, 1e-320)));
            AssertError(exit, r, "operation_failed");
            Assert.Equal("invalid_input", S(r["clipper_status"]));
        }

        [Fact]
        public void IsAnMcpTool()
        {
            var tool = Commands.Find("polygon2d");
            Assert.NotNull(tool);
            var schema = tool.InputSchema();
            Assert.Equal(new[] { "union", "intersection", "difference", "xor", "offset" },
                schema["properties"]["op"]["enum"].AsArray().Select(S).ToArray());
            Assert.Contains("op", schema["required"].AsArray().Select(S));
        }
    }
}
