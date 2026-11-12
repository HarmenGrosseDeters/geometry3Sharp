using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> hull2d (ConvexHull2; docs/design/CLI_MCP_UPDATE.md section 8). </summary>
    public class Hull2dCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public Hull2dCliTests(TestMeshes meshes) { m = meshes; }

        static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        static double SignedArea(JsonArray polygon)
        {
            double s = 0;
            for (int i = 0; i < polygon.Count; i++) {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
                s += D(a[0]) * D(b[1]) - D(b[0]) * D(a[1]);
            }
            return s / 2;
        }

        (int exit, JsonObject r) Hull(string points, params string[] more) =>
            Run(new[] { "hull2d", "--points", points, "--compact" }.Concat(more).ToArray());

        [Fact]
        public void Rectangle_WithInteriorPoints_IsItsFourCorners_Counterclockwise()
        {
            const string pts = "0,0;4,0;4,3;0,3;2,1;1,1;3,2";
            var (exit, r) = Hull(pts);
            Assert.Equal(0, exit);
            Assert.Equal(2, I(r["dimension"]));
            Assert.Equal(4, I(r["hull_vertices"]));
            Assert.Equal(7, I(r["points"]));
            Assert.Equal(12.0, D(r["area"]), 12);
            Assert.Equal(14.0, D(r["perimeter"]), 12);
            Assert.True(SignedArea(r["polygon"].AsArray()) > 0);                                  // counterclockwise
            var input = new[] { (0.0, 0.0), (4, 0), (4, 3), (0, 3), (2, 1), (1, 1), (3, 2) };
            var idx = r["indices"].AsArray().Select(I).ToArray();
            Assert.Equal(new[] { 0, 1, 2, 3 }, idx.OrderBy(i => i).ToArray());                    // the corners, indices into the input
            for (int i = 0; i < idx.Length; i++) {
                Assert.Equal(input[idx[i]].Item1, D(r["polygon"][i][0]));
                Assert.Equal(input[idx[i]].Item2, D(r["polygon"][i][1]));
            }
            Assert.Equal("double", S(r["query"]));
            Assert.Empty(r["warnings"].AsArray());
        }

        [Fact]
        public void Degenerate_Collinear_OnePoint_AndTheEpsilon()
        {
            var (e1, line) = Hull("0,0;1,1;2,2;3,3");
            Assert.Equal(0, e1);
            Assert.Equal(1, I(line["dimension"]));
            Assert.Equal(2, I(line["hull_vertices"]));
            Assert.Equal(new[] { 0, 3 }, line["indices"].AsArray().Select(I).OrderBy(i => i).ToArray());
            Assert.Equal(0.0, D(line["area"]));
            Assert.Equal(2 * 3 * Math.Sqrt(2), D(line["perimeter"]), 9);
            var (e2, point) = Hull("1,1;1,1");
            Assert.Equal(0, e2);
            Assert.Equal(0, I(point["dimension"]));
            Assert.Equal(1, I(point["hull_vertices"]));
            var (e3, single) = Hull("5,6");
            Assert.Equal(0, e3);
            Assert.Equal(0, I(single["dimension"]));
            Assert.Equal(5.0, D(single["polygon"][0][0]));
            // a triangle of extent 1e-3: a polygon with the default epsilon 1e-8, one point with epsilon 1
            const string tiny = "0,0;1e-3,0;0,1e-3";
            var (e4, small) = Hull(tiny);
            Assert.Equal(0, e4);
            Assert.Equal(2, I(small["dimension"]));
            var (e5, big) = Hull(tiny, "--epsilon", "1");
            Assert.Equal(0, e5);
            Assert.Equal(0, I(big["dimension"]));
            Assert.Equal(1e-8, D(small["epsilon"]));
        }

        [Fact]
        public void TheLineTest_IsRelativeToTheExtent_TheOnePointTestIsAbsolute()
        {
            // 1e-9 off the line over an extent of 1: within epsilon * extent (1e-8): a line; with a smaller epsilon: a polygon
            var (e1, line) = Hull("0,0;1,0;0.5,1e-9");
            Assert.Equal(0, e1);
            Assert.Equal(1, I(line["dimension"]));
            var (e2, poly) = Hull("0,0;1,0;0.5,1e-9", "--epsilon", "1e-12");
            Assert.Equal(0, e2);
            Assert.Equal(2, I(poly["dimension"]));
            // diagonal and shuffled collinear points: the two extreme points, whatever the order
            foreach (string pts in new[] { "3,3;0,0;2,2;1,1", "1,1;3,3;2,2;0,0", "-1,-2;3,6;1,2;0,0" }) {
                var (e, r) = Hull(pts);
                Assert.Equal(0, e);
                Assert.Equal(1, I(r["dimension"]));
                var ends = r["polygon"].AsArray().Select(v => (D(v[0]), D(v[1]))).OrderBy(v => v).ToArray();
                var all = pts.Split(';').Select(t => t.Split(',')).Select(t => (double.Parse(t[0], CultureInfo.InvariantCulture), double.Parse(t[1], CultureInfo.InvariantCulture))).OrderBy(v => v).ToArray();
                Assert.Equal(all.First(), ends.First());
                Assert.Equal(all.Last(), ends.Last());
            }
        }

        [Fact]
        public void TooManyPoints_AreRefused_AndAnOverflowingAreaIsWarnedAbout()
        {
            string many = string.Join(";", Enumerable.Range(0, 50001).Select(i => $"{i},{i % 7}"));
            var (e1, r1) = Hull(many);
            AssertError(e1, r1, "invalid_arguments");
            Assert.Contains("50000", S(r1["message"]));
            var (e2, ok) = Hull(string.Join(";", Enumerable.Range(0, 50000).Select(i => $"{i},{i % 7}")));
            Assert.Equal(0, e2);
            Assert.Equal(2, I(ok["dimension"]));
            var (e3, big) = Hull("0,0;1e300,1e300;1e300,0;0,1e300");
            Assert.Equal(0, e3);
            Assert.Equal(4, I(big["hull_vertices"]));
            Assert.Contains(big["warnings"].AsArray(), w => S(w).Contains("double range"));
        }

        [Fact]
        public void Int64_RoundsToAGrid_AndSaysSo()
        {
            var (exit, r) = Hull("0,0;4,0;4,3;0,3;2,1", "--query", "int64");
            Assert.Equal(0, exit);
            Assert.Equal("int64", S(r["query"]));
            Assert.Equal(4, I(r["hull_vertices"]));
            Assert.Equal(12.0, D(r["area"]), 6);
            Assert.Single(r["warnings"].AsArray());
            // the rounding to the 2^20 grid of the bounding square (unit about 0.95 here) drops the point (500000, 0.3) from the hull
            const string close = "0,0;1000000,0;500000,0.3;1000000,0.4";
            var (e2, exact) = Hull(close);
            var (e3, rounded) = Hull(close, "--query", "int64");
            Assert.Equal(0, e2); Assert.Equal(0, e3);
            Assert.Equal(4, I(exact["hull_vertices"]));
            Assert.Equal(3, I(rounded["hull_vertices"]));
            Assert.Equal(250000.0, D(exact["area"]), 6);
            Assert.Equal(200000.0, D(rounded["area"]), 6);
        }

        [Theory]
        [InlineData(-8)]
        [InlineData(0)]
        [InlineData(12)]
        public void RandomPoints_HullContainsAllPoints_AndIsInvariantToOrderAndPowerOfTwoScales(int exponent)
        {
            double s = Math.Pow(2, exponent);
            var rnd = new Random(11);
            var pts = Enumerable.Range(0, 300).Select(_ => (x: rnd.NextDouble() * 10 - 5, y: rnd.NextDouble() * 6 - 3)).ToArray();
            string Text(System.Collections.Generic.IEnumerable<(double x, double y)> p, double scale) => string.Join(";", p.Select(v => $"{N(v.x * scale)},{N(v.y * scale)}"));
            var (e, r) = Hull(Text(pts, s));
            Assert.Equal(0, e);
            Assert.Equal(2, I(r["dimension"]));
            var poly = r["polygon"].AsArray();
            Assert.True(SignedArea(poly) > 0);
            // every point is inside or on the hull (left of every edge), the hull is convex
            for (int i = 0; i < poly.Count; i++) {
                double ax = D(poly[i][0]), ay = D(poly[i][1]), bx = D(poly[(i + 1) % poly.Count][0]), by = D(poly[(i + 1) % poly.Count][1]);
                foreach (var p in pts)
                    Assert.True((bx - ax) * (p.y * s - ay) - (by - ay) * (p.x * s - ax) >= -1e-9 * s * s);
                double cx = D(poly[(i + 2) % poly.Count][0]), cy = D(poly[(i + 2) % poly.Count][1]);
                Assert.True((bx - ax) * (cy - by) - (by - ay) * (cx - bx) > 0);
            }
            // the same hull points for a shuffled input
            var shuffled = pts.OrderBy(_ => rnd.Next()).ToArray();
            var (e2, r2) = Hull(Text(shuffled, s));
            Assert.Equal(0, e2);
            Assert.True(Math.Abs(D(r["area"]) - D(r2["area"])) <= 1e-12 * D(r["area"]));            // (the sum runs in another order)
            var a = poly.Select(v => (D(v[0]), D(v[1]))).OrderBy(v => v).ToArray();
            var b = r2["polygon"].AsArray().Select(v => (D(v[0]), D(v[1]))).OrderBy(v => v).ToArray();
            Assert.Equal(a, b);
            // 2^k scale: the same hull points times the scale, bit for bit
            var (_, unit) = Hull(Text(pts, 1));
            var u = unit["polygon"].AsArray().Select(v => (D(v[0]) * s, D(v[1]) * s)).OrderBy(v => v).ToArray();
            Assert.Equal(u, a);
        }

        [Fact]
        public void MeshInput_UsesTheXyOfTheVertices()
        {
            var (exit, r) = Run("hull2d", m.Cube, "--compact");
            Assert.Equal(0, exit);
            Assert.Equal("mesh", S(r["source"]));
            Assert.Equal(m.Cube, S(r["input"]));
            Assert.Equal(8, I(r["points"]));
            Assert.Equal(4, I(r["hull_vertices"]));
            Assert.Equal(4.0, D(r["area"]), 12);                                                  // the square [-1, 1]^2
            Assert.Equal(8.0, D(r["perimeter"]), 12);
            Assert.All(r["indices"].AsArray(), i => Assert.InRange(I(i), 0, 7));
        }

        [Fact]
        public void Lists_JsonAndFilePoints_AndTheCut()
        {
            var (e1, a) = Hull("[[0,0],[4,0],[4,3],[0,3],[2,1]]");
            Assert.Equal(0, e1);
            Assert.Equal(12.0, D(a["area"]), 12);
            string file = m.Path_("hull_points.txt");
            File.WriteAllText(file, "0,0\n4,0\n4,3\n0,3\n2,1\n");
            var (e2, b) = Hull("@" + file);
            Assert.Equal(0, e2);
            Assert.Equal(12.0, D(b["area"]), 12);
            var (e3, cut) = Hull("[[0,0],[4,0],[4,3],[0,3],[2,1]]", "--max-results", "2");
            Assert.Equal(0, e3);
            Assert.Equal(4, I(cut["hull_vertices"]));
            Assert.Equal(2, cut["indices"].AsArray().Count);
            Assert.Equal(2, cut["polygon"].AsArray().Count);
            Assert.True(B(cut["truncated"]));
        }

        [Fact]
        public void GeoJson_PolygonLineStringPoint_FileAndInline()
        {
            string output = m.Path_("hull.geojson");
            var (e1, r) = Hull("0,0;4,0;4,3;0,3;2,1", "--output", output, "--return-geojson");
            Assert.Equal(0, e1);
            var file = JsonNode.Parse(File.ReadAllText(output));
            Assert.Equal(file.ToJsonString(), r["hull_geojson"].ToJsonString());
            Assert.Equal("Polygon", S(file["geometry"]["type"]));
            var ring = file["geometry"]["coordinates"][0].AsArray();
            Assert.Equal(5, ring.Count);                                                          // closed
            Assert.Equal(ring[0].ToJsonString(), ring[4].ToJsonString());
            Assert.True(SignedArea(ring) > 0);
            var (e2, line) = Hull("0,0;1,1;2,2", "--return-geojson");
            Assert.Equal(0, e2);
            Assert.Equal("LineString", S(line["hull_geojson"]["geometry"]["type"]));
            var (e3, point) = Hull("3,4", "--return-geojson");
            Assert.Equal(0, e3);
            Assert.Equal("Point", S(point["hull_geojson"]["geometry"]["type"]));
        }

        [Theory]
        [InlineData("--epsilon", "0")]
        [InlineData("--epsilon", "-1")]
        [InlineData("--epsilon", "NaN")]
        [InlineData("--max-results", "-1")]
        [InlineData("--query", "exact")]
        public void BadOptions_AreInvalidArguments(string name, string value)
        {
            var (exit, r) = Hull("0,0;1,0;0,1", name, value);
            AssertError(exit, r, "invalid_arguments");
        }

        [Theory]
        [InlineData("")]
        [InlineData("1,2,3")]
        [InlineData("1,a")]
        [InlineData("1,2;3")]
        [InlineData("[[1,2],[3]]")]
        [InlineData("[[1,2],[3,\"x\"]]")]
        [InlineData("1,1e999")]
        public void BadPoints_AreInvalidArguments(string points)
        {
            var (exit, r) = Run("hull2d", "--points", points, "--compact");
            AssertError(exit, r, "invalid_arguments");
        }

        [Fact]
        public void InputErrors()
        {
            var (e1, r1) = Run("hull2d", "--compact");
            AssertError(e1, r1, "invalid_arguments");
            var (e2, r2) = Run("hull2d", m.Cube, "--points", "0,0;1,1", "--compact");
            AssertError(e2, r2, "invalid_arguments");
            var (e3, r3) = Run("hull2d", m.Path_("missing.obj"), "--compact");
            AssertError(e3, r3, "read_error");
            var (e4, r4) = Run("hull2d", m.Empty, "--compact");                                   // one vertex, no triangle: a point is fine
            Assert.Equal(0, e4);
            Assert.Equal(0, I(r4["dimension"]));
            string none = m.Path_("no_vertices.off");
            File.WriteAllText(none, "OFF\n0 0 0\n");
            var (e6, r6) = Run("hull2d", none, "--compact");
            AssertError(e6, r6, "empty_mesh");
            var (e5, r5) = Run("hull2d", "--points", "0,0;1,0;0,1", "--output", Path.Combine(m.Path_("no_such_dir"), "h.geojson"), "--compact");
            AssertError(e5, r5, "write_error");
        }
    }
}
