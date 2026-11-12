using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> polygon2d: minkowski_sum and minkowski_difference (docs/design/CLI_MCP_UPDATE.md section 6). </summary>
    public class PolygonMinkowskiCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public PolygonMinkowskiCliTests(TestMeshes meshes) { m = meshes; }

        static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);
        static string Poly(double s, params double[] xy) =>
            "{\"type\":\"Polygon\",\"coordinates\":[[" + string.Join(",", Enumerable.Range(0, xy.Length / 2).Select(i => $"[{N(xy[2 * i] * s)},{N(xy[2 * i + 1] * s)}]")) + "]]}";
        static string Line(double s, params double[] xy) =>
            "{\"type\":\"LineString\",\"coordinates\":[" + string.Join(",", Enumerable.Range(0, xy.Length / 2).Select(i => $"[{N(xy[2 * i] * s)},{N(xy[2 * i + 1] * s)}]")) + "]}";

        static readonly double[] Square2 = { -1, -1, 1, -1, 1, 1, -1, 1, -1, -1 };               // the pattern [-1, 1]^2
        static readonly double[] Square10 = { 0, 0, 10, 0, 10, 10, 0, 10, 0, 0 };

        (int exit, JsonObject r) M(string op, string subject, string pattern, params string[] more) =>
            Run(new[] { "polygon2d", "--op", op, "--subject-geojson", subject, "--pattern-geojson", pattern, "--compact" }.Concat(more).ToArray());

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        [Fact]
        public void ClosedPath_GrowsThePolygonByThePattern()
        {
            var (exit, r) = M("minkowski_sum", Poly(1, Square10), Poly(1, Square2));
            Assert.Equal(0, exit);
            Assert.Equal("success", S(r["clipper_status"]));
            Assert.True(B(r["path_closed"]));
            Assert.Equal(1, I(r["result"]["polygons"]));
            Assert.Equal(0, I(r["result"]["holes"]));
            Assert.Equal(144.0, D(r["result"]["area"]), 6);                                    // [-1, 11]^2
            Assert.Equal(-1.0, D(r["result"]["bounds"]["min"][0]), 6);
            Assert.Equal(11.0, D(r["result"]["bounds"]["max"][1]), 6);
            Assert.Equal(1, I(r["pattern"]["polygons"]));
            var (e2, diff) = M("minkowski_difference", Poly(1, Square10), Poly(1, Square2));       // the pattern is symmetric: the same
            Assert.Equal(0, e2);
            Assert.Equal(144.0, D(diff["result"]["area"]), 6);
        }

        [Fact]
        public void OpenPath_SweepsThePattern_DifferenceMirrorsIt_OrientationDoesNotMatter()
        {
            string line = Line(1, 0, 0, 10, 0);
            var (e1, band) = M("minkowski_sum", line, Poly(1, Square2), "--path-closed=false");
            Assert.Equal(0, e1);
            Assert.False(B(band["path_closed"]));
            Assert.Equal(1, I(band["subject"]["lines"]));
            Assert.Equal(24.0, D(band["result"]["area"]), 6);                                    // [-1, 11] x [-1, 1]
            string tri = Poly(1, 0, 0, 2, 0, 0, 2, 0, 0), triCw = Poly(1, 0, 0, 0, 2, 2, 0, 0, 0);
            foreach (string pattern in new[] { tri, triCw }) {
                var (e2, sum) = M("minkowski_sum", line, pattern, "--path-closed=false");
                Assert.Equal(0, e2);
                Assert.Equal(22.0, D(sum["result"]["area"]), 6);                                 // 2 + 10 * 2
                Assert.Equal(0.0, D(sum["result"]["bounds"]["min"][0]), 6);
                Assert.Equal(12.0, D(sum["result"]["bounds"]["max"][0]), 6);
                Assert.Equal(2.0, D(sum["result"]["bounds"]["max"][1]), 6);
                var (e3, diff) = M("minkowski_difference", line, pattern, "--path-closed=false");
                Assert.Equal(0, e3);
                Assert.Equal(22.0, D(diff["result"]["area"]), 6);
                Assert.Equal(-2.0, D(diff["result"]["bounds"]["min"][0]), 6);                    // the pattern mirrored at its reference point
                Assert.Equal(10.0, D(diff["result"]["bounds"]["max"][0]), 6);
                Assert.Equal(-2.0, D(diff["result"]["bounds"]["min"][1]), 6);
                Assert.Equal(0.0, D(diff["result"]["bounds"]["max"][1]), 6);
            }
        }

        [Fact]
        public void AnOpenPathAroundASquare_LeavesAHole()
        {
            // the boundary of [0, 10]^2 as an open path (a closed line), swept with the square [-1, 1]^2: a frame of width 2
            var (exit, r) = M("minkowski_sum", Line(1, 0, 0, 10, 0, 10, 10, 0, 10, 0, 0), Poly(1, Square2), "--path-closed=false");
            Assert.Equal(0, exit);
            Assert.Equal(1, I(r["result"]["polygons"]));
            Assert.Equal(1, I(r["result"]["holes"]));
            Assert.Equal(144.0 - 64.0, D(r["result"]["area"]), 6);
        }

        [Theory]
        [InlineData(-20)]
        [InlineData(-3)]
        [InlineData(6)]
        [InlineData(30)]
        public void PowerOfTwoScales_GiveTheSameResultBitForBit(int exponent)
        {
            double s = Math.Pow(2, exponent);
            string tri(double k) => Poly(k, 0, 0, 2, 0, 0, 2, 0, 0);
            var (_, a) = M("minkowski_sum", Line(1, 0, 0, 10, 3, 4, 8), tri(1), "--path-closed=false", "--return-geojson");
            var (e, b) = M("minkowski_sum", Line(s, 0, 0, 10, 3, 4, 8), tri(s), "--path-closed=false", "--return-geojson");
            Assert.Equal(0, e);
            Assert.Equal(I(a["result"]["vertices"]), I(b["result"]["vertices"]));
            Assert.Equal(s * s * D(a["result"]["area"]), D(b["result"]["area"]));
            var ra = a["result_geojson"]["features"][0]["geometry"]["coordinates"][0][0].AsArray();
            var rb = b["result_geojson"]["features"][0]["geometry"]["coordinates"][0][0].AsArray();
            for (int i = 0; i < ra.Count; i++) {
                Assert.Equal(D(ra[i][0]) * s, D(rb[i][0]));
                Assert.Equal(D(ra[i][1]) * s, D(rb[i][1]));
            }
        }

        [Fact]
        public void ClosedPath_ConcaveClockwiseSubject_AsymmetricPattern_FarReferencePoint()
        {
            // an L shape (area 3), written clockwise, with the triangle (0,0), (2,0), (0,2) as the pattern
            double[] l = { 0, 0, 0, 2, 1, 2, 1, 1, 2, 1, 2, 0, 0, 0 };
            string lCcw = Poly(1, 0, 0, 2, 0, 2, 1, 1, 1, 1, 2, 0, 2, 0, 0), lCw = Poly(1, l);
            string tri = Poly(1, 0, 0, 2, 0, 0, 2, 0, 0);
            var (e1, sumCcw) = M("minkowski_sum", lCcw, tri);
            var (e2, sumCw) = M("minkowski_sum", lCw, tri);
            Assert.Equal(0, e1); Assert.Equal(0, e2);
            Assert.Equal(D(sumCcw["result"]["area"]), D(sumCw["result"]["area"]), 9);
            Assert.Equal(0.0, D(sumCcw["result"]["bounds"]["min"][0]), 6);                       // the pattern lies in x, y >= 0
            Assert.Equal(4.0, D(sumCcw["result"]["bounds"]["max"][0]), 6);
            Assert.Equal(0, I(sumCcw["result"]["holes"]));                                       // interior included: no hole in the L
            var (e3, diff) = M("minkowski_difference", lCcw, tri);                                // the pattern mirrored: x, y <= 0
            Assert.Equal(0, e3);
            Assert.Equal(-2.0, D(diff["result"]["bounds"]["min"][0]), 6);
            Assert.Equal(2.0, D(diff["result"]["bounds"]["max"][0]), 6);
            Assert.Equal(-2.0, D(diff["result"]["bounds"]["min"][1]), 6);
            Assert.Equal(2.0, D(diff["result"]["bounds"]["max"][1]), 6);
            Assert.True(D(diff["result"]["area"]) > 3 && D(sumCcw["result"]["area"]) > 3);          // both contain the L (area 3); the areas differ for a concave shape
            // the same pattern with its reference point 1e6 away: the result is shifted by 1e6
            var (e4, far) = M("minkowski_sum", lCcw, Poly(1, 1e6, 1e6, 1e6 + 2, 1e6, 1e6, 1e6 + 2, 1e6, 1e6));
            Assert.Equal(0, e4);
            Assert.Equal(1e6, D(far["result"]["bounds"]["min"][0]), 3);
            Assert.Equal(D(sumCcw["result"]["area"]), D(far["result"]["area"]), 3);
        }

        [Fact]
        public void TheVertexProductIsCapped_PreserveCollinearIsRefused()
        {
            string Circle(int n) => "{\"type\":\"Polygon\",\"coordinates\":[[" + string.Join(",", Enumerable.Range(0, n).Select(i => $"[{N(Math.Cos(2 * Math.PI * i / n))},{N(Math.Sin(2 * Math.PI * i / n))}]")) + $",[{N(1.0)},{N(0.0)}]]]}}";
            var (e1, r1) = M("minkowski_sum", Circle(400), Circle(300));                           // 120000 pairs
            AssertError(e1, r1, "invalid_arguments");
            Assert.Contains("100000", S(r1["message"]));
            var (e2, ok) = M("minkowski_sum", Circle(100), Circle(100));
            Assert.Equal(0, e2);
            Assert.Equal(1, I(ok["result"]["polygons"]));
            var (e3, r3) = M("minkowski_sum", Poly(1, Square10), Poly(1, Square2), "--preserve-collinear");
            AssertError(e3, r3, "invalid_arguments");
        }

        [Fact]
        public void FilesAndOutput()
        {
            string subject = m.Path_("mk_subject.geojson"), pattern = m.Path_("mk_pattern.geojson"), output = m.Path_("mk_out.geojson");
            File.WriteAllText(subject, Poly(1, Square10));
            File.WriteAllText(pattern, Poly(1, Square2));
            var (exit, r) = Run("polygon2d", subject, "--pattern", pattern, "--op", "minkowski_sum", "--output", output, "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(output, S(r["output"]));
            Assert.Equal("MultiPolygon", S(JsonNode.Parse(File.ReadAllText(output))["features"][0]["geometry"]["type"]));
        }

        [Fact]
        public void BadArguments_AreInvalidArguments()
        {
            string p = Poly(1, Square10), pat = Poly(1, Square2), line = Line(1, 0, 0, 10, 0);
            AssertError(M("minkowski_sum", p, pat, "--clip-geojson", pat), "invalid_arguments");
            AssertError(M("minkowski_sum", p, pat, "--delta", "1"), "invalid_arguments");
            AssertError(M("minkowski_sum", p, pat, "--epsilon", "1"), "invalid_arguments");
            AssertError(M("minkowski_sum", p, pat, "--join", "miter"), "invalid_arguments");
            AssertError(M("minkowski_sum", p, pat, "--fill-rule", "even_odd"), "invalid_arguments");
            AssertError(M("minkowski_sum", line, pat), "invalid_arguments");                      // path_closed: a polygon is needed
            AssertError(M("minkowski_sum", p, pat, "--path-closed=false"), "invalid_arguments");  // a LineString is needed
            AssertError(M("minkowski_sum", p, line), "invalid_arguments");                        // the pattern must be a polygon
            AssertError(M("minkowski_sum", p, Poly(1, Square10) .Replace("]]}", "],[[1,1],[2,1],[2,2],[1,1]]]}")), "invalid_arguments");   // a hole in the pattern
            AssertError(M("minkowski_sum", "{\"type\":\"MultiPolygon\",\"coordinates\":[" + p.Substring(p.IndexOf("[[[")) .TrimEnd('}') + "," + p.Substring(p.IndexOf("[[[")).TrimEnd('}') + "]}", pat), "invalid_arguments");   // two polygons
            AssertError(M("minkowski_sum", "{\"type\":\"MultiLineString\",\"coordinates\":[[[0,0],[1,0]],[[0,1],[1,1]]]}", pat, "--path-closed=false"), "invalid_arguments");   // two lines
            AssertError(Run("polygon2d", "--op", "minkowski_sum", "--subject-geojson", p, "--compact"), "invalid_arguments");       // no pattern
            AssertError(Run("polygon2d", "--op", "minkowski_sum", "--pattern-geojson", pat, "--compact"), "invalid_arguments");     // no subject
            AssertError(M("minkowski_sum", p, pat, "--precision-bits", "5"), "invalid_arguments");
            // the pattern options belong to the minkowski ops
            AssertError(M("union", p, pat), "invalid_arguments");
            AssertError(Run("polygon2d", "--op", "union", "--subject-geojson", p, "--path-closed=false", "--compact"), "invalid_arguments");
            AssertError(M("offset_lines", line, pat, "--delta", "1"), "invalid_arguments");
        }

        static void AssertError((int exit, JsonObject r) x, string code) => AssertError(x.exit, x.r, code);
    }
}
