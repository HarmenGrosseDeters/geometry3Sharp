using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary>
    /// query3d: the full results as a CSV / JSON file (--output), --inside auto, JSON ray and point lists, --normalize
    /// (docs/design/CLI_MCP_UPDATE.md section 5, PR B).
    /// </summary>
    public class Query3dOutputCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public Query3dOutputCliTests(TestMeshes meshes) { m = meshes; }

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        static string[][] ReadCsv(string path) => File.ReadAllLines(path).Select(l => l.Split(',')).ToArray();

        [Fact]
        public void Output_Rays_CsvHasEveryRay_AndMatchesTheResult()
        {
            string csv = m.Path_("rays.csv");
            var (exit, r) = Run("query3d", m.Cube, "--rays", "-5,0.1,0.2,2,0,0;0,0,0,1,0,0;-5,5,0,1,0,0", "--max-results", "1", "--output", csv, "--compact");
            Assert.Equal(0, exit);
            Assert.Single(r["rays"]["results"].AsArray());               // the JSON result is cut ...
            Assert.Equal(3, I(r["output_rows"]));                                  // ... the file is not
            Assert.Equal("csv", S(r["output_format"]));
            var rows = ReadCsv(csv);
            Assert.Equal(new[] { "index", "triangle", "t", "point_x", "point_y", "point_z", "status" }, rows[0]);
            Assert.Equal(4, rows.Length);
            Assert.Equal("0", rows[1][0]);
            Assert.Equal(4.0, double.Parse(rows[1][2], CultureInfo.InvariantCulture), 9);
            Assert.Equal(-1.0, double.Parse(rows[1][3], CultureInfo.InvariantCulture), 9);
            Assert.Equal(D(r["rays"]["results"][0]["t"]), double.Parse(rows[1][2], CultureInfo.InvariantCulture));   // round trip, bit for bit
            Assert.Equal(1.0, double.Parse(rows[2][2], CultureInfo.InvariantCulture), 9);
            Assert.Equal(new[] { "2", "", "", "", "", "", "ok" }, rows[3]);        // a miss: empty cells, same columns
        }

        [Fact]
        public void Output_AnyHit_And_PointsJson()
        {
            string any = m.Path_("any.csv");
            var (e1, _) = Run("query3d", m.Cube, "--rays", "-5,0,0,1,0,0;-5,5,0,1,0,0", "--any-hit", "--output", any, "--compact");
            Assert.Equal(0, e1);
            var rows = ReadCsv(any);
            Assert.Equal(new[] { "index", "hit", "status" }, rows[0]);
            Assert.Equal(new[] { "0", "true", "ok" }, rows[1]);
            Assert.Equal(new[] { "1", "false", "ok" }, rows[2]);

            string json = m.Path_("pts.json");
            var (e2, r) = Run("query3d", m.Cube, "--points", "3,0,0;0,0,0;0.5,0.2,-0.3", "--nearest", "--inside", "crossings", "--max-results", "1", "--output", json, "--compact");
            Assert.Equal(0, e2);
            var all = JsonNode.Parse(File.ReadAllText(json)).AsArray();
            Assert.Equal(3, all.Count);
            Assert.Single(r["points"]["results"].AsArray());
            Assert.Equal(2.0, D(all[0]["nearest"]["distance"]), 9);
            Assert.Equal("inside", S(all[1]["inside"]));
            Assert.Equal(0, I(all[0]["index"]));
            all[0].AsObject().Remove("index");
            Assert.Equal(r["points"]["results"][0].ToJsonString(), all[0].ToJsonString());      // the same entry as in the result

            string pcsv = m.Path_("pts.csv");
            var (e3, _) = Run("query3d", m.Cube, "--points", "3,0,0;0,0,0", "--nearest", "--inside", "crossings", "--output", pcsv, "--compact");
            Assert.Equal(0, e3);
            rows = ReadCsv(pcsv);
            Assert.Equal(new[] { "index", "point_x", "point_y", "point_z", "nearest_status", "nearest_triangle", "nearest_closest_point_x", "nearest_closest_point_y", "nearest_closest_point_z",
                "nearest_barycentric_x", "nearest_barycentric_y", "nearest_barycentric_z", "nearest_distance", "inside" }, rows[0]);
            Assert.Equal("outside", rows[1][13]);
            Assert.Equal("inside", rows[2][13]);
            Assert.All(rows, row => Assert.Equal(14, row.Length));
        }

        [Fact]
        public void Output_Errors_WriteNothing()
        {
            string o = m.Path_("never.csv");
            var (e1, r1) = Run("query3d", m.Cube, "--rays", "0,0,0,1,0,0", "--points", "0,0,0", "--inside", "crossings", "--output", o, "--compact");
            AssertError(e1, r1, "invalid_arguments");
            var (e2, r2) = Run("query3d", m.Cube, "--rays", "0,0,0,1,0,0", "--output", m.Path_("never.txt"), "--compact");
            AssertError(e2, r2, "unknown_format");
            var (e3, r3) = Run("query3d", m.Cube, "--rays", "0,0,0,1,0,0", "--output", Path.Combine(m.Path_("no_such_dir"), "x.csv"), "--compact");
            AssertError(e3, r3, "write_error");
            var (e4, r4) = Run("query3d", m.Cube, "--rays", "0,0,0,0,0,0", "--output", o, "--compact");        // zero direction
            AssertError(e4, r4, "invalid_arguments");
            Assert.False(File.Exists(o));
            Assert.False(File.Exists(m.Path_("never.txt")));
        }

        [Fact]
        public void InsideAuto_PicksCrossingsForAClosedMesh_AndTheWindingNumberOtherwise()
        {
            var (e1, closed) = Run("query3d", m.Cube, "--points", "0,0,0;3,0,0", "--inside", "auto", "--compact");
            Assert.Equal(0, e1);
            Assert.Equal("crossings", S(closed["points"]["method_used"]));
            Assert.Empty(closed["warnings"].AsArray());
            Assert.Equal(new[] { "inside", "outside" }, closed["points"]["results"].AsArray().Select(x => S(x["inside"])).ToArray());

            var (e2, open) = Run("query3d", m.Open, "--points", "0,0,0;4,4,4", "--inside", "auto", "--compact");
            Assert.Equal(0, e2);
            Assert.Equal("winding_number", S(open["points"]["method_used"]));
            Assert.Single(open["warnings"].AsArray());
            Assert.Equal(new[] { "inside", "outside" }, open["points"]["results"].AsArray().Select(x => S(x["inside"])).ToArray());

            // an explicit method is reported as used
            var (e3, explicitWinding) = Run("query3d", m.Cube, "--points", "0,0,0", "--inside", "winding_number", "--compact");
            Assert.Equal(0, e3);
            Assert.Equal("winding_number", S(explicitWinding["points"]["method_used"]));
            var (e4, explicitCrossings) = Run("query3d", m.Open, "--points", "0,0,0", "--inside", "crossings", "--compact");
            Assert.Equal(0, e4);
            Assert.Equal("crossings", S(explicitCrossings["points"]["method_used"]));
            Assert.Single(explicitCrossings["warnings"].AsArray());                // (as before)
        }

        [Fact]
        public void JsonLists_AreReadLikeTheTextForm()
        {
            const string text = "-5,0.1,0.2,2,0,0;0,0,0,1,0,0";
            const string objects = "[{\"origin\": [-5, 0.1, 0.2], \"direction\": [2, 0, 0]}, {\"origin\": [0, 0, 0], \"direction\": [1, 0, 0]}]";
            const string arrays = "[[-5, 0.1, 0.2, 2, 0, 0], [0, 0, 0, 1, 0, 0]]";
            var (e0, expected) = Run("query3d", m.Cube, "--rays", text, "--compact");
            Assert.Equal(0, e0);
            string file = m.Path_("rays.json");
            File.WriteAllText(file, objects);
            foreach (string[] a in new[] { new[] { "--rays", objects }, new[] { "--rays", arrays }, new[] { "--rays", "@" + file } }) {
                var (exit, r) = Run(new[] { "query3d", m.Cube, "--compact" }.Concat(a).ToArray());
                Assert.Equal(0, exit);
                Assert.Equal(expected["rays"].ToJsonString(), r["rays"].ToJsonString());
            }
            var (ep, p) = Run("query3d", m.Cube, "--points", "[[0, 0, 0], [3, 0, 0]]", "--inside", "crossings", "--compact");
            Assert.Equal(0, ep);
            Assert.Equal(new[] { "inside", "outside" }, p["points"]["results"].AsArray().Select(x => S(x["inside"])).ToArray());
        }

        [Fact]
        public void JsonLists_BadEntries_AreInvalidArguments()
        {
            string[] badRays = {
                "[", "{}", "[]", "[[1,2,3]]", "[[0,0,0,1,0]]", "[[0,0,0,1,0,\"x\"]]", "[{\"origin\": [0,0,0]}]",
                "[{\"origin\": [0,0,0], \"direction\": [1,0]}]", "[{\"origin\": [0,0,0], \"direction\": [1,0,0], \"extra\": 1}]", "[null]", "[1]",
                "[[0,0,0,1,0,1e999]]",
                "[{\"origin\": [0,0,0], \"origin\": [9,9,9], \"direction\": [1,0,0]}]",     // a duplicate key
            };
            foreach (string bad in badRays) {
                var (exit, r) = Run("query3d", m.Cube, "--rays", bad, "--compact");
                AssertError(exit, r, "invalid_arguments");
            }
            var (e2, r2) = Run("query3d", m.Cube, "--points", "[[0,0]]", "--inside", "crossings", "--compact");
            AssertError(e2, r2, "invalid_arguments");
        }

        [Fact]
        public void Output_ToTheInputListFile_UpperCaseExtension_AndNoResults()
        {
            string rays = m.Path_("rays_in.json");
            File.WriteAllText(rays, "[[-5,0,0,1,0,0]]");
            var (e1, r1) = Run("query3d", m.Cube, "--rays", "@" + rays, "--output", rays, "--compact");
            AssertError(e1, r1, "invalid_arguments");
            Assert.Equal("[[-5,0,0,1,0,0]]", File.ReadAllText(rays));                // the input list is untouched

            string upper = m.Path_("UPPER.CSV");
            var (e2, r2) = Run("query3d", m.Cube, "--rays", "-5,0,0,1,0,0", "--max-results", "0", "--output", upper, "--compact");
            Assert.Equal(0, e2);
            Assert.Empty(r2["rays"]["results"].AsArray());
            Assert.Equal(2, File.ReadAllLines(upper).Length);                        // header and the one ray
            Assert.Empty(Directory.GetFiles(m.Dir, "*.g3tmp"));                      // no temporary file is left

            // an existing output file is replaced as a whole
            File.WriteAllText(m.Path_("old.csv"), string.Join("\n", Enumerable.Repeat("junk,junk", 50)));
            var (e3, _) = Run("query3d", m.Cube, "--rays", "-5,0,0,1,0,0", "--output", m.Path_("old.csv"), "--compact");
            Assert.Equal(0, e3);
            Assert.Equal(2, File.ReadAllLines(m.Path_("old.csv")).Length);
        }

        [Fact]
        public void Normalize_False_ReportsNonUnitDirections()
        {
            var (exit, r) = Run("query3d", m.Cube, "--rays", "-5,0,0,1,0,0;-5,0,0,2,0,0", "--normalize=false", "--compact");
            Assert.Equal(0, exit);
            var res = r["rays"]["results"].AsArray();
            Assert.Equal("ok", S(res[0]["status"]));
            Assert.Equal(4.0, D(res[0]["t"]), 9);
            Assert.Equal("not_normalized", S(res[1]["status"]));
            Assert.Null(res[1]["triangle"]);
            Assert.Equal(1, I(r["rays"]["not_ok"]));
            Assert.Single(r["warnings"].AsArray());
            var (e2, r2) = Run("query3d", m.Cube, "--points", "0,0,0", "--inside", "crossings", "--normalize=false", "--compact");
            AssertError(e2, r2, "invalid_arguments");
            var (e3, r3) = Run("query3d", m.Cube, "--rays", "0,0,0,0,0,0", "--normalize=false", "--compact");      // a zero direction is rejected either way
            AssertError(e3, r3, "invalid_arguments");
        }
    }
}
