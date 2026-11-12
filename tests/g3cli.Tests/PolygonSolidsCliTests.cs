using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> polygon_solids (PlanarComplex.FindSolidRegions): loops from GeoJSON rings and closed lines to polygons with holes. </summary>
    public class PolygonSolidsCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public PolygonSolidsCliTests(TestMeshes meshes) { m = meshes; }

        static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        /// <summary> a closed ring of a square, counterclockwise (or clockwise) </summary>
        static string Square(double x, double y, double size, bool ccw = true)
        {
            double[][] p = { new[] { x, y }, new[] { x + size, y }, new[] { x + size, y + size }, new[] { x, y + size }, new[] { x, y } };
            if (!ccw) p = p.Reverse().ToArray();
            return "[" + string.Join(",", p.Select(v => $"[{N(v[0])},{N(v[1])}]")) + "]";
        }

        static string Circle(double radius, int n = 360, double scale = 1)
        {
            var pts = Enumerable.Range(0, n).Select(i => $"[{N(scale * radius * Math.Cos(2 * Math.PI * i / n))},{N(scale * radius * Math.Sin(2 * Math.PI * i / n))}]").ToList();
            pts.Add(pts[0]);
            return "[" + string.Join(",", pts) + "]";
        }

        static string Collection(params string[] geometries) =>
            "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", geometries.Select(g => "{\"type\":\"Feature\",\"properties\":{},\"geometry\":" + g + "}")) + "]}";
        static string Poly(string ring) => "{\"type\":\"Polygon\",\"coordinates\":[" + ring + "]}";
        static string Line(string ring) => "{\"type\":\"LineString\",\"coordinates\":" + ring + "}";

        (int exit, JsonObject r) Solids(string geojson, params string[] more) =>
            Run(new[] { "polygon_solids", "--input-geojson", geojson, "--compact" }.Concat(more).ToArray());

        static double SignedArea(JsonNode ring)
        {
            var a = ring.AsArray();
            double s = 0;
            for (int i = 0; i + 1 < a.Count; i++)
                s += D(a[i][0]) * D(a[i + 1][1]) - D(a[i + 1][0]) * D(a[i][1]);
            return s / 2;
        }

        [Fact]
        public void LoopsFromSeveralSources_FindTheirNesting()
        {
            // a square of 100, a square of 60 inside it (a hole), a square of 20 inside that (an island); two Polygons and a closed line, all counterclockwise,
            // and an open line that is no loop
            string g = Collection(Poly(Square(0, 0, 100)), Poly(Square(20, 20, 60)), Line(Square(40, 40, 20)), Line("[[0,0],[5,5]]"));
            var (exit, r) = Solids(g);
            Assert.Equal(0, exit);
            Assert.Equal(3, I(r["loops"]["input"]));
            Assert.Equal(1, I(r["loops"]["open_lines_ignored"]));
            Assert.Equal(2, I(r["solids"]));
            Assert.Equal(1, I(r["holes"]));
            Assert.Equal(100 * 100 - 60 * 60 + 20 * 20, D(r["area"]));
            Assert.Equal(60 * 60, D(r["holes_area"]));
            Assert.Equal(2, I(r["result"]["polygons"]));
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("open lines"));
        }

        [Fact]
        public void TheOrientationOfTheInputRingsDoesNotMatter()
        {
            string ccw = Collection(Poly(Square(0, 0, 100)), Poly(Square(20, 20, 60)));
            string cw = Collection(Poly(Square(0, 0, 100, false)), Poly(Square(20, 20, 60, false)));
            string mixed = Collection(Poly(Square(0, 0, 100, false)), Poly(Square(20, 20, 60)));
            foreach (string g in new[] { ccw, cw, mixed }) {
                var (exit, r) = Solids(g);
                Assert.Equal(0, exit);
                Assert.Equal(1, I(r["solids"]));
                Assert.Equal(1, I(r["holes"]));
                Assert.Equal(100 * 100 - 60 * 60, D(r["area"]));
            }
        }

        [Fact]
        public void TrustOrientations_AnInnerLoopOfTheSameOrientationIsNoHole()
        {
            string g = Collection(Poly(Square(0, 0, 10)), Poly(Square(3, 3, 4)));      // both counterclockwise
            var (e1, byContainment) = Solids(g);
            Assert.Equal(0, e1);
            Assert.Equal(1, I(byContainment["solids"]));
            Assert.Equal(1, I(byContainment["holes"]));
            var (e2, trusted) = Solids(g, "--trust-orientations");
            Assert.Equal(0, e2);
            Assert.Equal(2, I(trusted["solids"]));
            Assert.Equal(0, I(trusted["holes"]));
            Assert.Equal(100 + 16, D(trusted["area"]));
        }

        [Fact]
        public void OverlappingHoles_AreDroppedWithAWarning_UnlessAllowed()
        {
            string g = Collection(Poly(Square(0, 0, 20)), Poly(Square(2, 2, 8)), Poly(Square(6, 6, 8)));      // two holes that overlap
            var (e1, checkedHoles) = Solids(g);
            Assert.Equal(0, e1);
            Assert.Equal(1, I(checkedHoles["holes"]));
            Assert.Equal(400 - 64, D(checkedHoles["area"]));
            Assert.Contains(checkedHoles["warnings"].AsArray(), w => S(w).Contains("not part of any solid"));
            var (e2, allowed) = Solids(g, "--allow-overlapping-holes");
            Assert.Equal(0, e2);
            Assert.Equal(2, I(allowed["holes"]));
            Assert.Equal(128, D(allowed["holes_area"]));
            Assert.Empty(allowed["warnings"].AsArray());
        }

        [Fact]
        public void TheResultIsGeoJson_OutputFileAndInline()
        {
            string output = m.Path_("solids.geojson");
            string input = m.Path_("loops.geojson");
            File.WriteAllText(input, Collection(Poly(Square(0, 0, 100, false)), Poly(Square(20, 20, 60, false))));
            var (exit, r) = Run("polygon_solids", input, output, "--return-geojson", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(output, S(r["output"]));
            var file = JsonNode.Parse(File.ReadAllText(output));
            Assert.Equal(file.ToJsonString(), r["result_geojson"].ToJsonString());
            var multi = file["features"][0]["geometry"];
            Assert.Equal("MultiPolygon", S(multi["type"]));
            var rings = multi["coordinates"][0].AsArray();
            Assert.Equal(2, rings.Count);
            Assert.Equal(100 * 100, SignedArea(rings[0]));           // outer ring counterclockwise
            Assert.Equal(-60 * 60, SignedArea(rings[1]));            // hole clockwise
        }

        [Fact]
        public void TheCrsIsPassedThrough_AndZIsIgnoredWithAWarning()
        {
            string g = "{\"type\":\"Feature\",\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"EPSG:25832\"}},\"properties\":{},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[0,0,5],[4,0,5],[4,4,5],[0,4,5],[0,0,5]]]}}";
            var (exit, r) = Solids(g, "--return-geojson");
            Assert.Equal(0, exit);
            Assert.Equal("EPSG:25832", S(r["result_geojson"]["crs"]["properties"]["name"]));
            Assert.Equal(16, D(r["area"]));
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("third coordinates"));
        }

        [Fact]
        public void Tolerances_TheLibraryDefaultSimplifies_TheOptionsChangeIt()
        {
            // circles of radius 1.01 and 1 as 360-gons (720 vertices): the library's absolute tolerance of 0.1 simplifies them to a few vertices
            string g = Collection(Poly(Circle(1.01)), Poly(Circle(1)));
            var (e1, byDefault) = Solids(g);
            Assert.Equal(0, e1);
            Assert.Equal(0.1, D(byDefault["used_tolerance"]["absolute"]));              // owner 2026-11-12: the library default, stated in the help
            Assert.Equal(0.0, D(byDefault["used_tolerance"]["relative"]));
            Assert.Equal(16, I(byDefault["result"]["vertices"]));
            var (e2, none) = Solids(g, "--simplify-tolerance", "0");
            Assert.Equal(0, e2);
            Assert.Equal(720, I(none["result"]["vertices"]));
            Assert.Equal(1, I(none["solids"]));
            Assert.Equal(1, I(none["holes"]));
            Assert.InRange(D(none["holes_area"]), Math.PI * 0.999, Math.PI);
            // both given: the smaller applies to each loop (5e-5 of a diagonal of 2.9 is 1.4e-4, below 0.1)
            var (e3, both) = Solids(g, "--relative-tolerance", "5e-5");
            Assert.Equal(0, e3);
            Assert.Equal(5e-5, D(both["used_tolerance"]["relative"]));
            Assert.Equal(720, I(both["result"]["vertices"]));
            // the relative tolerance alone simplifies when it is large
            var (e4, alone) = Solids(g, "--simplify-tolerance", "0", "--relative-tolerance", "5e-2");
            Assert.Equal(0, e4);
            Assert.Equal(16, I(alone["result"]["vertices"]));
        }

        [Theory]
        [InlineData(-20)]
        [InlineData(-8)]
        [InlineData(0)]
        [InlineData(8)]
        [InlineData(20)]
        public void RelativeTolerance_PowerOfTwoScales_GiveTheSameResultBitForBit(int exponent)
        {
            double s = Math.Pow(2, exponent);
            var (_, unit) = Solids(Collection(Poly(Circle(1.01)), Poly(Circle(1))), "--simplify-tolerance", "0", "--relative-tolerance", "5e-5", "--return-geojson");
            var (e, scaled) = Solids(Collection(Poly(Circle(1.01, 360, s)), Poly(Circle(1, 360, s))), "--simplify-tolerance", "0", "--relative-tolerance", "5e-5", "--return-geojson");
            Assert.Equal(0, e);
            Assert.Equal(I(unit["result"]["vertices"]), I(scaled["result"]["vertices"]));
            Assert.Equal(1, I(scaled["holes"]));
            var a = unit["result_geojson"]["features"][0]["geometry"]["coordinates"][0][0].AsArray();
            var b = scaled["result_geojson"]["features"][0]["geometry"]["coordinates"][0][0].AsArray();
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) {
                Assert.Equal(D(a[i][0]) * s, D(b[i][0]));
                Assert.Equal(D(a[i][1]) * s, D(b[i][1]));
            }
        }

        [Fact]
        public void NoClosedLoop_IsOperationFailed()
        {
            var (e1, r1) = Solids(Line("[[0,0],[5,5],[9,1]]"));
            AssertError(e1, r1, "operation_failed");
            Assert.Contains("open lines", S(r1["message"]));
            var (e2, r2) = Solids("{\"type\":\"FeatureCollection\",\"features\":[]}");
            AssertError(e2, r2, "operation_failed");
        }

        [Fact]
        public void BadArguments_AreInvalidArguments()
        {
            string g = Collection(Poly(Square(0, 0, 10)));
            foreach (string[] a in new[] {
                new[] { "--simplify-tolerance", "-1" }, new[] { "--simplify-tolerance", "NaN" }, new[] { "--simplify-tolerance", "1e999" },
                new[] { "--relative-tolerance", "-0.1" }, new[] { "--relative-tolerance", "NaN" } }) {
                var (exit, r) = Solids(g, a);
                AssertError(exit, r, "invalid_arguments");
            }
            var (e1, r1) = Run("polygon_solids", "--compact");
            AssertError(e1, r1, "invalid_arguments");
            string file = m.Path_("both.geojson");
            File.WriteAllText(file, g);
            var (e2, r2) = Run("polygon_solids", file, "--input-geojson", g, "--compact");
            AssertError(e2, r2, "invalid_arguments");
        }

        [Fact]
        public void IdenticalLoops_AreRemovedWithAWarning()
        {
            // the same square twice (the second written clockwise and from another corner): one solid, not two overlapping ones
            string rotated = "[[10,0],[10,10],[0,10],[0,0],[10,0]]";
            var (exit, r) = Solids(Collection(Poly(Square(0, 0, 10)), Poly(rotated)));
            Assert.Equal(0, exit);
            Assert.Equal(1, I(r["loops"]["duplicates_removed"]));
            Assert.Equal(1, I(r["solids"]));
            Assert.Equal(100, D(r["area"]));
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("identical"));
            var (e2, other) = Solids(Collection(Poly(Square(0, 0, 10)), Poly(Square(0, 0, 11))));      // not identical
            Assert.Equal(0, e2);
            Assert.Equal(0, I(other["loops"]["duplicates_removed"]));
        }

        [Fact]
        public void ALineWithABadPosition_IsInvalidArguments_AlsoWhenOpen()
        {
            foreach (string line in new[] { "[[\"a\",0],[1,1],[2,0],[\"a\",0]]", "[[0,0],[1,\"b\"],[2,2]]", "[[0,0],[1],[2,2]]" }) {
                var (exit, r) = Solids(Line(line));
                AssertError(exit, r, "invalid_arguments");
            }
        }

        [Theory]
        [InlineData("{\"type\":\"Point\",\"coordinates\":[0,0]}")]
        [InlineData("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,1],[0,0]]]}")]      // fewer than 3 distinct positions
        [InlineData("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,\"a\"],[1,1]]]}")]
        [InlineData("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1],[1,1]]]}")]
        [InlineData("{\"type\":\"Polygon\"}")]
        [InlineData("{\"type\":\"LineString\",\"coordinates\":[[0,0],[1,\"a\"],[2,2],[0,0]]}")]
        [InlineData("{\"type\":\"MultiPolygon\",\"coordinates\":[1]}")]
        [InlineData("{\"type\":\"Feature\",\"geometry\":null}")]
        [InlineData("{not json")]
        [InlineData("[1,2,3]")]
        public void BadGeoJson_IsInvalidArguments_AndReadError_ForAFile(string json)
        {
            var (exit, r) = Solids(json);
            AssertError(exit, r, "invalid_arguments");
            string file = m.Path_("bad.geojson");
            File.WriteAllText(file, json);
            var (e2, r2) = Run("polygon_solids", file, "--compact");
            AssertError(e2, r2, "read_error");
        }

        [Fact]
        public void FileAndWriteErrors()
        {
            var (e1, r1) = Run("polygon_solids", m.Path_("missing.geojson"), "--compact");
            AssertError(e1, r1, "read_error");
            string input = m.Path_("ok.geojson");
            File.WriteAllText(input, Collection(Poly(Square(0, 0, 10))));
            var (e2, r2) = Run("polygon_solids", input, Path.Combine(m.Path_("no_such_dir"), "x.geojson"), "--compact");
            AssertError(e2, r2, "write_error");
        }

        [Fact]
        public void TooManyLoops_AreRefused()
        {
            var loops = Enumerable.Range(0, 4001).Select(i => Poly(Square(i * 2, 0, 1)));
            var (exit, r) = Solids(Collection(loops.ToArray()));
            AssertError(exit, r, "invalid_arguments");
            Assert.Contains("4000", S(r["message"]));
        }
    }
}
