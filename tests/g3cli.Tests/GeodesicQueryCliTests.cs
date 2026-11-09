using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> geodesic (MeshGeodesicDistance, MeshGeodesicPath) and query3d (MeshBatchQueries) on generated meshes with known answers. </summary>
    public class GeodesicQueryCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public GeodesicQueryCliTests(TestMeshes meshes) { m = meshes; }

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        const int N = 12;   // grid of N x N vertices, spacing 1; vertex id = j * N + i

        /// <summary> a flat grid with alternating diagonals, in the XY plane </summary>
        string Grid(string name, double shear = 0)
        {
            var sb = new StringBuilder();
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    sb.Append(FormattableString.Invariant($"v {i + shear * j} {j} 0\n"));
            for (int j = 0; j + 1 < N; j++)
                for (int i = 0; i + 1 < N; i++) {
                    int a = j * N + i + 1, b = a + 1, c = a + N, d = c + 1;
                    if ((i + j) % 2 == 0) sb.Append($"f {a} {b} {d}\nf {a} {d} {c}\n");
                    else sb.Append($"f {a} {b} {c}\nf {b} {d} {c}\n");
                }
            string path = m.Path_(name);
            File.WriteAllText(path, sb.ToString());
            return path;
        }


        // ---- geodesic: distance ----

        [Fact]
        public void Geodesic_Distance_OnAPlane_IsEuclidean()
        {
            string grid = Grid("grid.obj");
            int far = N * N - 1;
            var (exit, r) = Run("geodesic", grid, "--sources", "0", "--targets", $"{far},{N - 1},0", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal("distance", S(r["mode"]));
            var t = r["targets"].AsArray();
            double diagonal = (N - 1) * Math.Sqrt(2);
            Assert.Equal(diagonal, D(t[0]["distance"]), 0);                      // heat method: about 2 % plus half an edge
            Assert.InRange(D(t[0]["distance"]), 0.95 * diagonal - 1, 1.05 * diagonal + 1);
            Assert.InRange(D(t[1]["distance"]), N - 1 - 1.5, N - 1 + 1.5);       // along an edge direction
            Assert.Equal(0.0, D(t[2]["distance"]), 6);                           // the source itself
            Assert.Equal(N * N, I(r["distance_statistics"]["count"]));
            Assert.Equal(0.0, D(r["distance_statistics"]["min"]), 6);
            Assert.InRange(D(r["distance_statistics"]["max"]), 0.95 * diagonal - 1, 1.05 * diagonal + 1);
            Assert.Equal(0, I(r["vertices_without_distance"]));
        }

        [Fact]
        public void Geodesic_Distance_OnASphere_TheAntipodeIsPiAway()
        {
            var (exit, r) = Run("geodesic", m.Sphere, "--sources", "0", "--compact");
            Assert.Equal(0, exit);
            Assert.InRange(D(r["distance_statistics"]["max"]), Math.PI * 0.95, Math.PI * 1.05);
        }

        [Fact]
        public void Geodesic_Distance_SeveralSources_AreTheNearest()
        {
            string grid = Grid("grid2.obj");
            var (_, one) = Run("geodesic", grid, "--sources", "0", "--compact");
            var (e, two) = Run("geodesic", grid, "--sources", $"0,{N * N - 1}", "--compact");
            Assert.Equal(0, e);
            Assert.True(D(two["distance_statistics"]["max"]) < D(one["distance_statistics"]["max"]) * 0.75);
        }

        [Fact]
        public void Geodesic_Distance_ComponentsWithoutSource_AreReported()
        {
            var (exit, r) = Run("geodesic", m.Overlapping, "--sources", "0", "--compact");   // two cubes in one file
            Assert.Equal(0, exit);
            Assert.True(I(r["vertices_without_distance"]) > 0);
            Assert.Contains(r["warnings"].AsArray().Select(S), w => w.Contains("no distance"));
        }

        [Fact]
        public void Geodesic_Distance_TimeFactorAndIntrinsicOptionsAreUsed()
        {
            string grid = Grid("grid3.obj");
            string D1(params string[] more) => Run(new[] { "geodesic", grid, "--sources", "0", "--targets", $"{N * N - 1}", "--compact" }.Concat(more).ToArray()).result["targets"][0]["distance"].ToJsonString();
            Assert.NotEqual(D1(), D1("--time-factor", "20"));
            // a sheared grid has obtuse triangles: the intrinsic Delaunay triangulation flips edges, unless it is switched off
            string sheared = Grid("sheared.obj", 1.5);
            int Flips(params string[] more) => I(Run(new[] { "geodesic", sheared, "--sources", "0", "--compact" }.Concat(more).ToArray()).result["intrinsic_flips"]);
            Assert.True(Flips() > 0);
            Assert.Equal(0, Flips("--no-intrinsic-delaunay"));
        }


        // ---- geodesic: path ----

        [Fact]
        public void Geodesic_Path_OnAPlane_IsTheStraightLine()
        {
            string grid = Grid("grid4.obj");
            int far = N * N - 1;
            var (exit, r) = Run("geodesic", grid, "--from", "0", "--to", far.ToString(), "--compact");
            Assert.Equal(0, exit);
            Assert.Equal("path", S(r["mode"]));
            Assert.Equal("success", S(r["path_status"]));
            Assert.Equal((N - 1) * Math.Sqrt(2), D(r["length"]), 9);
            Assert.True(D(r["initial_length"]) >= D(r["length"]));
            Assert.Equal(0, I(r["path_vertices"][0]));
            Assert.Equal(far, I(r["path_vertices"][I(r["path_vertex_count"]) - 1]));
            // all points on the line y = x
            Assert.All(r["path"].AsArray(), p => Assert.Equal(D(p[0]), D(p[1]), 9));
            Assert.Equal(I(r["path_point_count"]), r["path"].AsArray().Count);
            Assert.False(B(r["path_truncated"]));
        }

        [Fact]
        public void Geodesic_Path_Through_KeepsWaypoints_AndClosedAddsTheWayBack()
        {
            string grid = Grid("grid5.obj");
            int a = 0, b = N - 1, c = (N - 1) * N;   // three corners
            var (e1, open) = Run("geodesic", grid, "--through", $"{a},{b},{c}", "--compact");
            Assert.Equal(0, e1);
            Assert.Equal((N - 1) + (N - 1) * Math.Sqrt(2), D(open["length"]), 9);
            var (e2, closed) = Run("geodesic", grid, "--through", $"{a},{b},{c}", "--closed", "--compact");
            Assert.Equal(0, e2);
            Assert.Equal(2 * (N - 1) + (N - 1) * Math.Sqrt(2), D(closed["length"]), 9);
            Assert.True(B(closed["closed"]));
        }

        [Fact]
        public void Geodesic_Path_MaxResultsTruncatesTheListsButNotTheCounts()
        {
            string grid = Grid("grid6.obj");
            var (exit, r) = Run("geodesic", grid, "--from", "0", "--to", (N * N - 1).ToString(), "--max-results", "2", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(2, r["path"].AsArray().Count);
            Assert.True(I(r["path_point_count"]) > 2);
            Assert.True(B(r["path_truncated"]));
        }

        [Fact]
        public void Geodesic_Path_BetweenComponents_IsOperationFailed()
        {
            var (exit, r) = Run("geodesic", m.Overlapping, "--from", "0", "--to", "8", "--compact");   // the two cubes are separate components
            AssertError(exit, r, "operation_failed");
            Assert.Equal("no_path", S(r["path_status"]));
        }

        [Theory]
        [InlineData("--from", "0")]                        // from without to
        [InlineData("--to", "3")]
        [InlineData("--sources", "x")]
        [InlineData("--sources", "-1")]
        [InlineData("--sources", "")]
        public void Geodesic_BadArguments_AreInvalidArguments(string name, string value)
        {
            var (exit, r) = Run("geodesic", m.Path_("missing.obj"), name, value);
            AssertError(exit, r, "invalid_arguments");
        }

        [Fact]
        public void Geodesic_ModeAndIdChecks()
        {
            string grid = Grid("grid7.obj");
            var (e1, r1) = Run("geodesic", grid); AssertError(e1, r1, "invalid_arguments");
            var (e2, r2) = Run("geodesic", grid, "--sources", "0", "--from", "0", "--to", "1"); AssertError(e2, r2, "invalid_arguments");
            var (e3, r3) = Run("geodesic", grid, "--through", "5", ""); AssertError(e3, r3, "invalid_arguments");
            var (e4, r4) = Run("geodesic", grid, "--sources", "100000"); AssertError(e4, r4, "invalid_arguments");
            Assert.Contains("not a vertex", S(r4["message"]));
            var (e5, r5) = Run("geodesic", grid, "--from", "0", "--to", "100000"); AssertError(e5, r5, "invalid_arguments");
            var (e6, r6) = Run("geodesic", grid, "--sources", "0", "--time-factor", "0"); AssertError(e6, r6, "invalid_arguments");
            var (e7, r7) = Run("geodesic", m.Garbage, "--sources", "0"); AssertError(e7, r7, "read_error");
        }


        // ---- query3d ----

        [Fact]
        public void Query3d_Rays_HitTheCube()
        {
            var (exit, r) = Run("query3d", m.Cube, "--rays", "-5,0.1,0.2,2,0,0;0,0,0,1,0,0;-5,5,0,1,0,0", "--compact");   // outside, from inside, a miss; unnormalized direction
            Assert.Equal(0, exit);
            var rays = r["rays"];
            Assert.Equal(3, I(rays["count"]));
            Assert.Equal(2, I(rays["hits"]));
            var res = rays["results"].AsArray();
            Assert.Equal(4.0, D(res[0]["t"]), 9);                                   // t in mesh units: the direction is normalized
            Assert.Equal(-1.0, D(res[0]["point"][0]), 9);
            Assert.Equal(0.1, D(res[0]["point"][1]), 9);
            Assert.InRange(I(res[0]["triangle"]), 0, 11);
            Assert.Equal(1.0, D(res[1]["t"]), 9);                                   // back face seen from inside
            Assert.Null(res[2]["triangle"]);
            Assert.Null(res[2]["t"]);
            Assert.All(res, x => Assert.Equal("ok", S(x["status"])));
        }

        [Fact]
        public void Query3d_AnyHit_AndMaxDistance()
        {
            var (e1, r) = Run("query3d", m.Cube, "--rays", "-5,0,0,1,0,0;-5,5,0,1,0,0", "--any-hit", "--compact");
            Assert.Equal(0, e1);
            Assert.Equal(1, I(r["rays"]["hits"]));
            Assert.True(B(r["rays"]["results"][0]["hit"]));
            Assert.False(B(r["rays"]["results"][1]["hit"]));
            Assert.Null(r["rays"]["results"][0]["triangle"]);
            var (e2, near) = Run("query3d", m.Cube, "--rays", "-5,0,0,1,0,0", "--max-distance", "3", "--compact");   // the hit is at t = 4
            Assert.Equal(0, e2);
            Assert.Equal(0, I(near["rays"]["hits"]));
        }

        [Fact]
        public void Query3d_Points_NearestAndInside()
        {
            var (exit, r) = Run("query3d", m.Cube, "--points", "3,0,0;0,0,0;0.5,0.2,-0.3", "--nearest", "--inside", "crossings", "--compact");
            Assert.Equal(0, exit);
            var pts = r["points"];
            Assert.Equal(3, I(pts["count"]));
            var res = pts["results"].AsArray();
            Assert.Equal(2.0, D(res[0]["nearest"]["distance"]), 9);
            Assert.Equal(1.0, D(res[0]["nearest"]["closest_point"][0]), 9);
            Assert.Equal(1.0, res[0]["nearest"]["barycentric"].AsArray().Sum(D), 9);
            Assert.Equal(1.0, D(res[1]["nearest"]["distance"]), 9);                // the center is 1 from each face
            Assert.Equal(0.5, D(res[2]["nearest"]["distance"]), 9);                // to the face x = 1
            Assert.Equal(new[] { "outside", "inside", "inside" }, res.Select(x => S(x["inside"])).ToArray());
            Assert.Equal(2, I(pts["inside_count"]));
            Assert.Equal(3, I(pts["nearest_found"]));
        }

        [Fact]
        public void Query3d_WindingNumber_WorksOnAnOpenMesh_AndNearestMaxDistance()
        {
            var (e1, r) = Run("query3d", m.Open, "--points", "0,0,0;4,4,4", "--inside", "winding_number", "--compact");
            Assert.Equal(0, e1);
            Assert.Equal(new[] { "inside", "outside" }, r["points"]["results"].AsArray().Select(x => S(x["inside"])).ToArray());
            // two overlapping cubes: in the overlap the crossings parity is even (outside), the winding number is 2 (inside)
            var (ea, cross) = Run("query3d", m.Overlapping, "--points", "0.5,0,0;-0.5,0,0", "--inside", "crossings", "--compact");
            var (eb, wind) = Run("query3d", m.Overlapping, "--points", "0.5,0,0;-0.5,0,0", "--inside", "winding_number", "--compact");
            Assert.Equal(new[] { "outside", "inside" }, cross["points"]["results"].AsArray().Select(x => S(x["inside"])).ToArray());
            Assert.Equal(new[] { "inside", "inside" }, wind["points"]["results"].AsArray().Select(x => S(x["inside"])).ToArray());
            var (e2, n) = Run("query3d", m.Cube, "--points", "3,0,0", "--nearest", "--max-distance", "1.5", "--compact");   // the cube is 2 away
            Assert.Equal(0, e2);
            Assert.Equal(0, I(n["points"]["nearest_found"]));
            Assert.Null(n["points"]["results"][0]["nearest"]["triangle"]);
        }

        [Fact]
        public void Query3d_ResultsDoNotDependOnOrderOrParallel()
        {
            var rnd = new Random(7);
            var rays = Enumerable.Range(0, 200).Select(_ => $"{rnd.NextDouble() * 6 - 3},{rnd.NextDouble() * 6 - 3},-5,{rnd.NextDouble() - 0.5},{rnd.NextDouble() - 0.5},1").ToList();
            JsonArray Results(System.Collections.Generic.IEnumerable<string> list, params string[] more) =>
                Run(new[] { "query3d", m.Sphere, "--rays", string.Join(";", list), "--max-results", "1000", "--compact" }.Concat(more).ToArray()).result["rays"]["results"].AsArray();
            var a = Results(rays);
            var b = Results(rays, "--no-parallel");
            Assert.Equal(a.ToJsonString(), b.ToJsonString());
            var reversed = Results(Enumerable.Reverse(rays)).Reverse().ToList();
            for (int i = 0; i < rays.Count; i++)
                Assert.Equal(a[i].ToJsonString(), reversed[i].ToJsonString());
            Assert.Contains(a, x => x["triangle"] != null);
            Assert.Contains(a, x => x["triangle"] == null);
        }

        [Fact]
        public void Query3d_FileInput_AndTruncation()
        {
            string file = m.Path_("points.txt");
            File.WriteAllText(file, "3,0,0\n0,0,0\n\n1 2 3\n");
            var (exit, r) = Run("query3d", m.Cube, "--points", "@" + file, "--inside", "crossings", "--max-results", "2", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(3, I(r["points"]["count"]));
            Assert.Equal(2, r["points"]["results"].AsArray().Count);
            Assert.True(B(r["points"]["truncated"]));
            Assert.Equal(1, I(r["points"]["inside_count"]));                         // counts cover all points
        }

        [Theory]
        [InlineData("--rays", "1,2,3")]
        [InlineData("--rays", "0,0,0,0,0,0")]                                         // zero direction
        [InlineData("--rays", "a,b,c,d,e,f")]
        [InlineData("--rays", "")]
        [InlineData("--rays", "@/no/such/file")]
        [InlineData("--points", "1,2")]
        [InlineData("--max-distance", "0")]
        [InlineData("--max-results", "-1")]
        [InlineData("--inside", "sometimes")]
        public void Query3d_BadArguments_AreInvalidArguments(string name, string value)
        {
            var (exit, r) = Run("query3d", m.Path_("missing.obj"), name, value, "--rays", "0,0,0,1,0,0");
            AssertError(exit, r, "invalid_arguments");
        }

        [Fact]
        public void Query3d_NeedsQueries_AndAMethodForPoints()
        {
            var (e1, r1) = Run("query3d", m.Cube); AssertError(e1, r1, "invalid_arguments");
            var (e2, r2) = Run("query3d", m.Cube, "--points", "0,0,0"); AssertError(e2, r2, "invalid_arguments");
            var (e3, r3) = Run("query3d", m.Cube, "--rays", "0,0,0,1,0,0", "--nearest"); AssertError(e3, r3, "invalid_arguments");
            var (e4, r4) = Run("query3d", m.Garbage, "--rays", "0,0,0,1,0,0"); AssertError(e4, r4, "read_error");
            var (e5, r5) = Run("query3d", m.Empty, "--rays", "0,0,0,1,0,0"); AssertError(e5, r5, "empty_mesh");
        }
    }
}
