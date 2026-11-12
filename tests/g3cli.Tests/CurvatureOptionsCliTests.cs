using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> curvature (MeshCurvature), the fidelity options outer_offset and sharp_edge_min_height, the geodesic refinement, trace and path options (docs/design/CLI_MCP_UPDATE.md sections 2-4). </summary>
    public class CurvatureOptionsCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public CurvatureOptionsCliTests(TestMeshes meshes) { m = meshes; }

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        static string[][] Csv(string path) => File.ReadAllLines(path).Select(l => l.Split(',')).ToArray();

        // ---- curvature ----

        [Fact]
        public void Curvature_OfTheUnitSphere_IsOneOverRadius()
        {
            var (exit, r) = Run("curvature", m.Sphere, "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(I(r["vertices"]), I(r["valid_vertices"]));              // a closed manifold mesh: every vertex is valid
            Assert.Equal(0, I(r["boundary_vertices"]));
            Assert.InRange(D(r["mean_curvature"]["mean"]), 0.97, 1.03);            // H = 1 / r on the unit sphere
            Assert.InRange(D(r["gaussian_curvature"]["mean"]), 0.9, 1.1);          // K = 1 / r^2
            Assert.InRange(D(r["mixed_area_total"]), 4 * Math.PI * 0.97, 4 * Math.PI);
            Assert.Equal(4 * Math.PI, D(r["angle_deficit_total"]), 9);             // Gauss-Bonnet: 2 pi chi
            Assert.Empty(r["warnings"].AsArray());
            double size = D(r["size"]);
            Assert.InRange(size, 1.9, 2.1);
            Assert.Equal(D(r["mean_curvature"]["mean"]) * size, D(r["mean_curvature_scaled"]["mean"]), 9);
            Assert.Equal(D(r["gaussian_curvature"]["mean"]) * size * size, D(r["gaussian_curvature_scaled"]["mean"]), 9);
        }

        [Theory]
        [InlineData(-3)]
        [InlineData(0)]
        [InlineData(5)]
        public void Curvature_PowerOfTwoScales_ScaleHAndKBitForBit(int exponent)
        {
            // (both files are written the same way; a power of two scale is exact in the file's float coordinates too)
            double s = Math.Pow(2, exponent);
            string Write(string name, double scale)
            {
                DMesh3 sphere = StandardMeshReader.ReadMesh(m.Sphere);
                MeshTransforms.Scale(sphere, scale, scale, scale);
                string path = m.Path_(name);
                Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, sphere, new WriteOptions() { bWriteBinary = true }).code);
                return path;
            }
            var (_, a) = Run("curvature", Write($"sphere_unit_{exponent}.ply", 1), "--compact");
            var (e, b) = Run("curvature", Write($"sphere_{exponent}.ply", s), "--compact");
            Assert.Equal(0, e);
            Assert.Equal(D(a["mean_curvature"]["mean"]) / s, D(b["mean_curvature"]["mean"]));                    // bit for bit
            Assert.Equal(D(a["gaussian_curvature"]["mean"]) / (s * s), D(b["gaussian_curvature"]["mean"]));
            Assert.Equal(D(a["mean_curvature_scaled"]["mean"]), D(b["mean_curvature_scaled"]["mean"]), 9);       // the scaled statistics do not depend on the scale
            Assert.Equal(D(a["angle_deficit_total"]), D(b["angle_deficit_total"]));
        }

        [Fact]
        public void Curvature_OpenMesh_HasBoundaryVerticesThatAreNotValid()
        {
            var (exit, r) = Run("curvature", m.Open, "--compact");
            Assert.Equal(0, exit);
            Assert.True(I(r["boundary_vertices"]) > 0);
            Assert.True(I(r["valid_vertices"]) < I(r["vertices"]));
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("not valid"));
            Assert.Equal(I(r["valid_vertices"]), I(r["mean_curvature"]["count"]));               // only the valid vertices are in the statistics
            Assert.Equal(I(r["valid_vertices"]), I(r["gaussian_curvature_scaled"]["count"]));
        }

        [Fact]
        public void Curvature_Output_CsvAndJson_HaveEveryVertex()
        {
            string csv = m.Path_("curv.csv"), json = m.Path_("curv.json");
            var (e1, r) = Run("curvature", m.Open, "--output", csv, "--compact");
            Assert.Equal(0, e1);
            Assert.Equal(I(r["vertices"]), I(r["output_rows"]));
            var rows = Csv(csv);
            Assert.Equal(new[] { "vertex", "x", "y", "z", "mean", "gaussian", "mixed_area", "angle_deficit", "valid" }, rows[0]);
            Assert.Equal(I(r["vertices"]) + 1, rows.Length);
            Assert.All(rows, row => Assert.Equal(9, row.Length));
            Assert.Equal(I(r["valid_vertices"]), rows.Skip(1).Count(row => row[8] == "true"));
            var (e2, _) = Run("curvature", m.Open, "--output", json, "--compact");
            Assert.Equal(0, e2);
            var all = JsonNode.Parse(File.ReadAllText(json)).AsArray();
            Assert.Equal(I(r["vertices"]), all.Count);
            Assert.Equal(double.Parse(rows[1][4], CultureInfo.InvariantCulture), D(all[0]["mean"]));         // the same value, bit for bit
            Assert.Empty(Directory.GetFiles(m.Dir, "*.g3tmp"));
        }

        [Fact]
        public void Curvature_Errors()
        {
            var (e1, r1) = Run("curvature", m.Sphere, "--output", m.Path_("x.txt"), "--compact");
            AssertError(e1, r1, "unknown_format");
            var (e2, r2) = Run("curvature", m.Sphere, "--output", Path.Combine(m.Path_("no_such_dir"), "x.csv"), "--compact");
            AssertError(e2, r2, "write_error");
            var (e3, r3) = Run("curvature", m.Path_("missing.obj"), "--compact");
            AssertError(e3, r3, "read_error");
            var (e4, r4) = Run("curvature", m.Empty, "--compact");
            AssertError(e4, r4, "empty_mesh");
        }

        // ---- fidelity ----

        [Fact]
        public void Fidelity_NewOptions_ReachTheLibraryAndAreReported()
        {
            var (e0, d) = Run("fidelity", m.Sphere, m.Sphere, "--compact");
            Assert.Equal(0, e0);
            Assert.Equal(1e-5, D(d["options"]["outer_offset"]));                   // the library defaults
            Assert.Equal(1e-5, D(d["options"]["sharp_edge_min_height"]));
            var (exit, r) = Run("fidelity", m.Sphere, m.Sphere, "--outer-offset", "0.001", "--sharp-edge-min-height", "0.01", "--seed", "7", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(1e-3, D(r["options"]["outer_offset"]));
            Assert.Equal(1e-2, D(r["options"]["sharp_edge_min_height"]));
            Assert.Equal(7, I(r["options"]["seed"]));
            Assert.Equal(30.0, D(r["options"]["sharp_edge_angle"]));
        }

        [Theory]
        [InlineData("--outer-offset", "0")]
        [InlineData("--outer-offset", "-1")]
        [InlineData("--outer-offset", "1.5")]
        [InlineData("--outer-offset", "NaN")]
        [InlineData("--sharp-edge-min-height", "-0.1")]
        [InlineData("--sharp-edge-min-height", "2")]
        [InlineData("--sharp-edge-min-height", "NaN")]
        public void Fidelity_NewOptions_AreValidated(string name, string value)
        {
            var (exit, r) = Run("fidelity", m.Sphere, m.Sphere, name, value, "--compact");
            AssertError(exit, r, "invalid_arguments");
        }

        [Fact]
        public void Fidelity_OuterOffset_NeedsTheOuterSurface()
        {
            var (exit, r) = Run("fidelity", m.Sphere, m.Sphere, "--outer-offset", "0.001", "--outer-surface=false", "--compact");
            AssertError(exit, r, "invalid_arguments");
        }

        // ---- geodesic ----

        const int N = 12;
        string Grid(string name)
        {
            var sb = new StringBuilder();
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    sb.Append(FormattableString.Invariant($"v {i} {j} 0\n"));
            for (int j = 0; j + 1 < N; j++)
                for (int i = 0; i + 1 < N; i++) {
                    int a = j * N + i + 1, b = a + 1, c = a + N, d = c + 1;
                    sb.Append($"f {a} {b} {d}\nf {a} {d} {c}\n");
                }
            string path = m.Path_(name);
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        [Fact]
        public void Geodesic_IntrinsicRefine_ReportsTheRefinement()
        {
            // a strip of 20 x 1 in two triangles: angles of 2.9 degrees that only the refinement removes
            string strip = m.Path_("strip.obj");
            File.WriteAllText(strip, "v 0 0 0\nv 20 0 0\nv 20 1 0\nv 0 1 0\nf 1 2 3\nf 1 3 4\n");
            var (e0, plain) = Run("geodesic", strip, "--sources", "0", "--compact");
            Assert.Equal(0, e0);
            Assert.Equal("success", S(plain["refinement"]["status"]));                     // (split_obtuse_boundary_edges is on by default)
            Assert.Equal(0, I(plain["refinement"]["inserted_vertices"]));
            var (exit, r) = Run("geodesic", strip, "--sources", "0", "--intrinsic-refine", "--refine-min-angle", "30", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(30.0, D(r["refinement"]["min_angle_bound"]));
            Assert.True(I(r["refinement"]["inserted_vertices"]) > 10);
            Assert.True(D(r["refinement"]["min_angle"]) >= 30);
            Assert.Equal("success", S(r["refinement"]["status"]));
            Assert.Equal(4, I(r["distance_statistics"]["count"]));                          // still the distances at the mesh vertices
            Assert.InRange(D(r["distance_statistics"]["max"]), 19.5, 20.5);
            var (e2, limited) = Run("geodesic", strip, "--sources", "0", "--intrinsic-refine", "--refine-min-angle", "30", "--refine-max-insertions", "2", "--compact");
            Assert.Equal(0, e2);
            Assert.Equal(2, I(limited["refinement"]["inserted_vertices"]));
            Assert.Equal("insertion_limit_reached", S(limited["refinement"]["status"]));
            Assert.Contains(limited["warnings"].AsArray(), w => S(w).Contains("insertion_limit_reached"));
            var (e3, angleZero) = Run("geodesic", strip, "--sources", "0", "--intrinsic-refine", "--refine-min-angle", "0", "--compact");
            Assert.Equal(0, e3);
            Assert.Equal(0, I(angleZero["refinement"]["inserted_vertices"]));              // only the obtuse boundary edges
            var (e4, none) = Run("geodesic", Grid("refine_grid.obj"), "--sources", "0", "--no-split-obtuse-boundary-edges", "--compact");
            Assert.Equal(0, e4);
            Assert.Null(none["refinement"]);                                             // neither refinement nor the boundary splits
        }

        [Fact]
        public void Geodesic_Trace_FollowsTheDistanceDownToTheSource()
        {
            string grid = Grid("trace_grid.obj");
            int far = N * N - 1;
            var (exit, r) = Run("geodesic", grid, "--sources", "0", "--trace-from", far.ToString(), "--compact");
            Assert.Equal(0, exit);
            var trace = r["trace"].AsArray();
            Assert.Equal(I(r["trace_point_count"]), trace.Count);
            Assert.Equal(N - 1.0, D(trace[0][0]), 9);                                    // starts at the vertex
            Assert.Equal(N - 1.0, D(trace[0][1]), 9);
            Assert.Equal(0.0, D(trace[trace.Count - 1][0]), 9);                           // ends at the source vertex 0
            Assert.Equal(0.0, D(trace[trace.Count - 1][1]), 9);
            Assert.InRange(D(r["trace_length"]), (N - 1) * Math.Sqrt(2) * 0.99, (N - 1) * Math.Sqrt(2) * 1.15);
            var (e2, point) = Run("geodesic", grid, "--sources", "0", "--trace-from", "5.5,3.2,0", "--max-results", "2", "--compact");
            Assert.Equal(0, e2);
            Assert.Equal(2, point["trace"].AsArray().Count);
            Assert.True(B(point["trace_truncated"]));
            Assert.True(I(point["trace_point_count"]) > 2);
            Assert.Empty(point["warnings"].AsArray());
            var (e3, atSource) = Run("geodesic", grid, "--sources", "0", "--trace-from", "0", "--compact");       // the start is the source: a single point
            Assert.Equal(0, e3);
            Assert.Equal(1, I(atSource["trace_point_count"]));
            Assert.Contains(atSource["warnings"].AsArray(), w => S(w).Contains("single point"));
            var (e4, outside) = Run("geodesic", grid, "--sources", "0", "--trace-from", "1e300,0,0", "--compact");     // no triangle near enough for the library
            Assert.Equal(0, e4);
            Assert.True(I(outside["trace_point_count"]) <= 1);
            Assert.NotEmpty(outside["warnings"].AsArray());
        }

        [Fact]
        public void Geodesic_PathOptions_LimitTheShortening()
        {
            // a bumpy grid (fixed seed): the initial path along edges needs several shortening steps
            var rnd = new Random(3);
            const int B = 14;
            var sb = new StringBuilder();
            for (int j = 0; j < B; j++)
                for (int i = 0; i < B; i++)
                    sb.Append(FormattableString.Invariant($"v {i} {j} {0.6 * rnd.NextDouble()}\n"));
            for (int j = 0; j + 1 < B; j++)
                for (int i = 0; i + 1 < B; i++) {
                    int a = j * B + i + 1, b = a + 1, c = a + B, d = c + 1;
                    sb.Append($"f {a} {b} {d}\nf {a} {d} {c}\n");
                }
            string bumpy = m.Path_("bumpy.obj");
            File.WriteAllText(bumpy, sb.ToString());
            string last = (B * B - 1).ToString();
            var (e0, full) = Run("geodesic", bumpy, "--from", "0", "--to", last, "--compact");
            Assert.Equal(0, e0);
            Assert.Equal("success", S(full["path_status"]));
            Assert.True(I(full["iterations"]) > 3);
            var (e1, limited) = Run("geodesic", bumpy, "--from", "0", "--to", last, "--max-iterations", "1", "--compact");
            Assert.Equal(0, e1);
            Assert.Equal(1, I(limited["iterations"]));
            Assert.Equal("not_converged", S(limited["path_status"]));
            Assert.True(D(limited["length"]) > D(full["length"]));
            var (e2, tol) = Run("geodesic", bumpy, "--from", "0", "--to", last, "--angle-tolerance", "1e-3", "--compact");
            Assert.Equal(0, e2);
            Assert.Equal("success", S(tol["path_status"]));
        }

        [Theory]
        [InlineData("distance", "--refine-min-angle", "20")]                 // needs intrinsic_refine
        [InlineData("distance", "--refine-max-insertions", "5")]
        [InlineData("distance", "--intrinsic-refine", "--refine-min-angle", "31")]
        [InlineData("distance", "--intrinsic-refine", "--refine-min-angle", "-1")]
        [InlineData("distance", "--intrinsic-refine", "--refine-max-insertions", "-1")]
        [InlineData("distance", "--intrinsic-refine", "--no-intrinsic-delaunay")]
        [InlineData("distance", "--angle-tolerance", "0.001")]
        [InlineData("distance", "--max-iterations", "5")]
        [InlineData("distance", "--trace-from", "abc")]
        [InlineData("distance", "--trace-from", "1,2")]
        [InlineData("distance", "--trace-from", "99999")]
        [InlineData("path", "--angle-tolerance", "0")]
        [InlineData("path", "--angle-tolerance", "0.6")]
        [InlineData("path", "--angle-tolerance", "NaN")]
        [InlineData("path", "--max-iterations", "-1")]
        [InlineData("path", "--intrinsic-refine")]
        [InlineData("path", "--trace-from", "3")]
        public void Geodesic_NewOptions_AreValidated(string mode, params string[] more)
        {
            string grid = Grid("val_grid.obj");
            var args = mode == "distance" ? new[] { "geodesic", grid, "--sources", "0", "--compact" } : new[] { "geodesic", grid, "--from", "0", "--to", "5", "--compact" };
            var (exit, r) = Run(args.Concat(more).ToArray());
            AssertError(exit, r, "invalid_arguments");
        }
    }
}
