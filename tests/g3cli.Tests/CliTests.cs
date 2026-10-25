using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    public class CliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public CliTests(TestMeshes meshes) { m = meshes; }

        static void AssertOk(int exit, JsonObject r)
        {
            Assert.True(exit == 0, r.ToJsonString());
            Assert.Equal("ok", S(r["status"]));
            Assert.Equal("ok", S(r["code"]));
        }

        static void AssertError(int exit, JsonObject r, string code, int expectedExit)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.False(string.IsNullOrEmpty(S(r["message"])));
            Assert.Equal(expectedExit, exit);
        }

        static void AssertCleanCube(JsonNode mesh, double volume)
        {
            Assert.True(B(mesh["closed"]), mesh.ToJsonString());
            Assert.True(B(mesh["clean"]), mesh.ToJsonString());
            Assert.Equal(0, I(mesh["self_intersections"]));
            Assert.InRange(D(mesh["volume"]), volume - 1e-6, volume + 1e-6);
        }


        // ---- measure ----

        [Fact]
        public void Measure_Cube()
        {
            var (exit, r) = Run("measure", m.Cube);
            AssertOk(exit, r);
            JsonNode mesh = r["mesh"];
            Assert.Equal(8, I(mesh["vertices"]));
            Assert.Equal(12, I(mesh["triangles"]));
            Assert.Equal(1, I(mesh["components"]));
            Assert.Equal(0, I(mesh["genus"]));
            Assert.True(B(mesh["winding_closed"]));
            Assert.InRange(D(mesh["area"]), 24 - 1e-9, 24 + 1e-9);
            Assert.Equal(-1, D(mesh["bounds"]["min"][0]), 9);
            Assert.Equal(2, D(mesh["bounds"]["size"][2]), 9);
            AssertCleanCube(mesh, 8);
            Assert.Null(mesh["not_clean_reason"]);
        }

        [Fact]
        public void Measure_OpenAndSelfIntersecting()
        {
            var (exit, r) = Run("measure", m.Open);
            AssertOk(exit, r);
            Assert.False(B(r["mesh"]["closed"]));
            Assert.Null(r["mesh"]["volume"]);
            Assert.Equal("not closed", S(r["mesh"]["not_clean_reason"]));

            (exit, r) = Run("measure", m.Overlapping);
            AssertOk(exit, r);
            Assert.True(B(r["mesh"]["closed"]));
            Assert.Equal(2, I(r["mesh"]["components"]));
            Assert.True(I(r["mesh"]["self_intersections"]) > 0);
            Assert.Equal("self-intersections", S(r["mesh"]["not_clean_reason"]));

            (exit, r) = Run("measure", m.Overlapping, "--no-self-intersections");
            AssertOk(exit, r);
            Assert.Null(r["mesh"]["self_intersections"]);
            Assert.False(B(r["mesh"]["clean"]));
        }

        [Fact]
        public void Measure_Hausdorff()
        {
            var (exit, r) = Run("measure", m.Cube, "--compare", m.Shifted, "--tolerance", "1e-6");
            AssertOk(exit, r);
            JsonNode h = r["hausdorff"];
            Assert.True(B(h["converged"]));
            Assert.InRange(D(h["lower_bound"]), 1 - 1e-6, 1 + 1e-9);
            Assert.InRange(D(h["distance"]), 1 - 1e-9, 1 + 1e-5);
        }


        // ---- convert ----

        [Theory]
        [InlineData("stl")]
        [InlineData("obj")]
        [InlineData("off")]
        [InlineData("ply")]
        [InlineData("3mf")]
        [InlineData("glb")]
        [InlineData("gltf")]
        [InlineData("g3mesh")]
        public void Convert_RoundTrip(string format)
        {
            string output = m.Path_("roundtrip." + format);
            var (exit, r) = Run("convert", m.Cube, output);
            AssertOk(exit, r);
            Assert.Equal(12, I(r["triangles"]));
            Assert.True(File.Exists(output));

            // and back to OBJ
            string back = m.Path_("roundtrip_" + format + ".obj");
            (exit, r) = Run("convert", output, back);
            AssertOk(exit, r);

            (exit, r) = Run("measure", back);
            AssertOk(exit, r);
            Assert.Equal(12, I(r["mesh"]["triangles"]));
            Assert.Equal(8, I(r["mesh"]["vertices"]));
            AssertCleanCube(r["mesh"], 8);
        }

        // ---- uv ----

        static string WriteTorus(TestMeshes m)
        {
            var torus = new g3.DMesh3();
            int nu = 32, nv = 12;
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    torus.AppendVertex(new g3.Vector3d((2 + 0.7 * Math.Cos(v)) * Math.Cos(u), (2 + 0.7 * Math.Cos(v)) * Math.Sin(u), 0.7 * Math.Sin(v)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int a = i * nv + j, b = ((i + 1) % nu) * nv + j, c = ((i + 1) % nu) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
                    torus.AppendTriangle(a, b, c);
                    torus.AppendTriangle(a, c, d);
                }
            string path = m.Path_("torus.obj");
            g3.StandardMeshWriter.WriteMesh(path, torus, g3.WriteOptions.Defaults);
            return path;
        }

        [Fact]
        public void Uv_TorusAndSphere_OverlapFreeAtlas()
        {
            foreach (string input in new[] { WriteTorus(m), m.Sphere, m.Cube }) {
                string output = m.Path_(Path.GetFileNameWithoutExtension(input) + "_atlas.g3mesh");
                var (exit, r) = Run("uv", input, output);
                AssertOk(exit, r);
                Assert.Equal("success", S(r["atlas_status"]));
                Assert.Equal(0, I(r["overlapping_pairs"]));
                Assert.Equal(0, I(r["inverted_triangles"]));
                Assert.Equal(0, I(r["distortion"]["flipped_triangles"]));
                Assert.Equal(0, I(r["float_uv_defects"]));
                Assert.Equal(0, I(r["float_uv_sub_resolution_defects"]));
                Assert.True(D(r["distortion"]["mean_area"]) < 0.25);
                Assert.True(I(r["charts"]) >= 1);
                Assert.True(D(r["seam_length"]) > 0);
                Assert.Empty(r["warnings"].AsArray());
                // the written mesh has the UVs, and the input's triangles
                g3.DMesh3 atlas = g3.StandardMeshReader.ReadMesh(output);
                Assert.True(atlas.HasVertexUVs);
                Assert.Equal(I(r["input_mesh"]["triangles"]), atlas.TriangleCount);
                Assert.Equal(I(r["vertices"]), atlas.VertexCount);
                Assert.True(g3.UVOverlap.Compute(atlas).IsInjective);
            }
            // the cube: cones at its corners
            var (e2, r2) = Run("uv", m.Cube, m.Path_("cube_atlas.obj"));
            AssertOk(e2, r2);
            Assert.Equal(8, I(r2["cones"]));
            Assert.Equal(1, I(r2["charts"]));
            Assert.Equal(1, I(r2["chart_methods"]["cones"]));
            Assert.Contains("vt ", File.ReadAllText(m.Path_("cube_atlas.obj")));
        }

        [Fact]
        public void Uv_LimitsAndWarnings()
        {
            // one chart of a sphere exceeds the limits: ok, but reported
            var (exit, r) = Run("uv", m.Sphere, m.Path_("single.obj"), "--method", "single");
            AssertOk(exit, r);
            Assert.Equal("distortion_limit_exceeded", S(r["atlas_status"]));
            Assert.Equal(1, I(r["charts_over_limit"]));
            Assert.Equal(0, I(r["overlapping_pairs"]));
            Assert.NotEmpty(r["warnings"].AsArray());

            // STL does not store UVs
            (exit, r) = Run("uv", m.Cube, m.Path_("atlas.stl"));
            AssertOk(exit, r);
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("does not store UVs"));

            (exit, r) = Run("uv", m.Cube, m.Path_("a.obj"), "--max_charts", "0");
            AssertError(exit, r, "invalid_arguments", 2);
            (exit, r) = Run("uv", m.Cube, m.Path_("a.obj"), "--padding", "0.5");
            AssertError(exit, r, "invalid_arguments", 2);
            (exit, r) = Run("uv", m.Cube, m.Path_("a.obj"), "--method", "magic");
            AssertError(exit, r, "invalid_arguments", 2);
            (exit, r) = Run("uv", m.Empty, m.Path_("a.obj"));
            AssertError(exit, r, "empty_mesh", 1);
        }

        [Fact]
        public void Convert_AsciiStl()
        {
            string output = m.Path_("ascii.stl");
            var (exit, r) = Run("convert", m.Sphere, output, "--binary=false");
            AssertOk(exit, r);
            Assert.StartsWith("solid", File.ReadAllText(output));
            (exit, r) = Run("measure", output, "--compare", m.Sphere, "--tolerance", "1e-7");
            AssertOk(exit, r);
            // ASCII STL stores floats
            Assert.InRange(D(r["hausdorff"]["distance"]), 0, 1e-6);
        }


        // ---- repair ----

        [Theory]
        [InlineData("robust")]
        [InlineData("auto")]
        public void Repair_ClosesOpenCube(string method)
        {
            string output = m.Path_("repaired_" + method + ".stl");
            var (exit, r) = Run("repair", m.Open, output, "--method", method);
            AssertOk(exit, r);
            Assert.False(B(r["input_mesh"]["closed"]));
            AssertCleanCube(r["mesh"], 8);

            (exit, r) = Run("measure", output);
            AssertOk(exit, r);
            AssertCleanCube(r["mesh"], 8);
        }

        [Fact]
        public void Repair_RobustReportsFallback()
        {
            // overlapping shells: MeshAutoRepair keeps the self-intersections, so they are resolved exactly
            var (exit, r) = Run("repair", m.Overlapping, m.Path_("repaired_overlap.obj"), "--solidify-resolution", "64");
            AssertOk(exit, r);
            Assert.True(B(r["used_exact_resolve"]));
            Assert.False(B(r["used_solidify"]));
            Assert.Equal("self-intersections", S(r["fallback_reason"]));
            Assert.True(B(r["mesh"]["closed"]));
            Assert.InRange(D(r["mesh"]["volume"]), 11.5, 12.5);

            // without the exact stage the solidify fallback runs
            (exit, r) = Run("repair", m.Overlapping, m.Path_("repaired_overlap2.obj"), "--solidify-resolution", "64", "--no-exact-resolve");
            AssertOk(exit, r);
            Assert.False(B(r["used_exact_resolve"]));
            Assert.True(B(r["used_solidify"]));
            Assert.Equal("self-intersections", S(r["fallback_reason"]));
            Assert.True(B(r["mesh"]["closed"]));
            Assert.InRange(D(r["mesh"]["volume"]), 11.5, 12.5);
        }


        // ---- boolean, resolve ----

        [Theory]
        [InlineData("union", 12.0)]
        [InlineData("intersection", 4.0)]
        [InlineData("difference", 4.0)]
        public void Boolean_Cubes(string op, double volume)
        {
            string output = m.Path_("bool_" + op + ".obj");
            var (exit, r) = Run("boolean", m.Cube, m.Shifted, output, "--op", op);
            AssertOk(exit, r);
            Assert.True(B(r["exact_winding_a"]));
            Assert.True(I(r["boolean"]["intersecting_pairs"]) > 0);
            AssertCleanCube(r["mesh"], volume);

            (exit, r) = Run("measure", output);
            AssertOk(exit, r);
            AssertCleanCube(r["mesh"], volume);
        }

        [Theory]
        [InlineData("union", 20.0, 2)]
        [InlineData("difference", 4.0, 1)]
        [InlineData("at_least", 4.0, 1)]
        public void Boolean_MoreThanTwoInputs(string op, double volume, int components)
        {
            // cube, shifted cube (overlap 4) and a disjoint cube in one operation
            string output = m.Path_("nary_" + op + ".obj");
            var args = new List<string>() { "boolean", m.Cube, m.Shifted, output, "--op", op, "--more", m.Far };
            if (op == "at_least")
                args.AddRange(new[] { "--k", "2" });
            var (exit, r) = Run(args.ToArray());
            AssertOk(exit, r);
            Assert.Equal(3, r["inputs"].AsArray().Count);
            Assert.Equal(new[] { true, true, true }, r["exact_winding"].AsArray().Select(B).ToArray());
            Assert.Equal(components, I(r["mesh"]["components"]));
            AssertCleanCube(r["mesh"], volume);
        }

        [Fact]
        public void Boolean_MoreThanTwoInputs_IntersectionAndErrors()
        {
            var (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("i3.obj"), "--op", "intersection", "--more", m.Far + ";" + m.Cube);
            AssertOk(exit, r);
            Assert.Equal(4, r["inputs"].AsArray().Count);
            Assert.True(B(r["empty_result"]));
            // k: needed for at_least, at least 1, not with other operations
            (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("x.obj"), "--op", "at_least");
            AssertError(exit, r, "invalid_arguments", 2);
            (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("x.obj"), "--op", "at_least", "--k", "0");
            AssertError(exit, r, "invalid_arguments", 2);
            (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("x.obj"), "--op", "union", "--k", "2");
            AssertError(exit, r, "invalid_arguments", 2);
            // an open third input, rejected on request: the status is in the message
            (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("x.obj"), "--op", "union", "--more", m.Open, "--allow-open=false");
            AssertError(exit, r, "operation_failed", 1);
            Assert.Contains("OpenInput", S(r["message"]));
            (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("x.obj"), "--op", "union", "--more", m.Garbage);
            AssertError(exit, r, "read_error", 3);
            Assert.False(File.Exists(m.Path_("x.obj")));
        }

        [Fact]
        public void Boolean_OpenInputRejectedOnRequest()
        {
            var (exit, r) = Run("boolean", "--a", m.Open, "--b", m.Shifted, "--output", m.Path_("x.obj"), "--op", "union", "--allow-open=false");
            AssertError(exit, r, "operation_failed", 1);
            Assert.Contains("closed", S(r["message"]));
            Assert.False(File.Exists(m.Path_("x.obj")));
        }

        [Fact]
        public void Boolean_DisjointIntersectionIsEmpty()
        {
            var (exit, r) = Run("boolean", m.Cube, m.Far, m.Path_("empty_int.obj"), "--op", "intersection");
            AssertOk(exit, r);
            Assert.True(B(r["empty_result"]));
            Assert.Equal(0, I(r["mesh"]["triangles"]));

            (exit, r) = Run("boolean", m.Cube, m.Far, m.Path_("disjoint_union.obj"), "--op", "union");
            AssertOk(exit, r);
            Assert.Equal(2, I(r["mesh"]["components"]));
            AssertCleanCube(r["mesh"], 16);
        }

        [Fact]
        public void Resolve_OverlappingCubes()
        {
            string output = m.Path_("resolved.ply");
            var (exit, r) = Run("resolve", m.Overlapping, output);
            AssertOk(exit, r);
            Assert.True(I(r["input_mesh"]["self_intersections"]) > 0);
            Assert.Equal(1, I(r["mesh"]["components"]));
            AssertCleanCube(r["mesh"], 12);
        }


        // ---- remesh, simplify ----

        [Fact]
        public void Remesh_Sphere()
        {
            string output = m.Path_("remeshed.obj");
            var (exit, r) = Run("remesh", m.Sphere, output, "--edge-length", "0.1", "--iterations", "10", "--deviation");
            AssertOk(exit, r);
            JsonNode mesh = r["mesh"];
            Assert.True(B(mesh["closed"]));
            Assert.True(I(mesh["triangles"]) > I(r["input_mesh"]["triangles"]), "edge length 0.1 should refine");
            Assert.InRange(D(mesh["area"]), D(r["input_mesh"]["area"]) * 0.98, D(r["input_mesh"]["area"]) * 1.02);
            Assert.InRange(D(r["hausdorff"]["distance"]), 0, 0.02);
        }

        [Fact]
        public void Remesh_BoxKeepsCorners()
        {
            var (exit, r) = Run("remesh", m.Cube, m.Path_("remeshed_cube.stl"), "--edge-length", "0.4", "--deviation");
            AssertOk(exit, r);
            AssertCleanCube(r["mesh"], 8);
            // "distance" is the certified upper bound, within the default tolerance (1e-4 of the diagonal)
            Assert.InRange(D(r["hausdorff"]["lower_bound"]), 0, 1e-6);
            Assert.InRange(D(r["hausdorff"]["distance"]), 0, 1e-3);
        }

        [Fact]
        public void Simplify_Sphere()
        {
            var (exit, r) = Run("simplify", m.Sphere, m.Path_("simplified.obj"), "--ratio", "0.25", "--deviation");
            AssertOk(exit, r);
            int input = I(r["input_mesh"]["triangles"]);
            Assert.InRange(I(r["mesh"]["triangles"]), 1, input / 4 + 2);
            Assert.True(B(r["mesh"]["closed"]));
            Assert.InRange(D(r["hausdorff"]["distance"]), 0, 0.1);

            (exit, r) = Run("simplify", m.Sphere, m.Path_("simplified100.stl"), "--triangles", "100");
            AssertOk(exit, r);
            Assert.InRange(I(r["mesh"]["triangles"]), 1, 100);
        }

        [Fact]
        public void Simplify_NeedsExactlyOneTarget()
        {
            var (exit, r) = Run("simplify", m.Sphere, m.Path_("s.obj"));
            AssertError(exit, r, "invalid_arguments", 2);
            (exit, r) = Run("simplify", m.Sphere, m.Path_("s.obj"), "--ratio", "0.5", "--triangles", "10");
            AssertError(exit, r, "invalid_arguments", 2);
            (exit, r) = Run("simplify", m.Sphere, m.Path_("s.obj"), "--ratio", "1.5");
            AssertError(exit, r, "invalid_arguments", 2);
            Assert.False(File.Exists(m.Path_("s.obj")));
        }


        // ---- errors ----

        [Fact]
        public void Errors_Files()
        {
            var (exit, r) = Run("measure", m.Path_("missing.stl"));
            AssertError(exit, r, "read_error", 3);

            (exit, r) = Run("measure", m.Path_("mesh.xyz"));
            AssertError(exit, r, "unknown_format", 3);

            (exit, r) = Run("measure", m.Garbage);
            AssertError(exit, r, "read_error", 3);

            (exit, r) = Run("measure", m.Empty);
            AssertError(exit, r, "empty_mesh", 1);

            (exit, r) = Run("convert", m.Cube, m.Path_("out.xyz"));
            AssertError(exit, r, "unknown_format", 3);

            (exit, r) = Run("convert", m.Cube, Path.Combine(m.Dir, "no", "such", "dir", "out.stl"));
            AssertError(exit, r, "write_error", 4);
        }

        [Fact]
        public void Errors_Arguments()
        {
            var (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("b.obj"));
            AssertError(exit, r, "invalid_arguments", 2);
            Assert.Contains("op", S(r["message"]));

            (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("b.obj"), "--op", "xor");
            AssertError(exit, r, "invalid_arguments", 2);

            (exit, r) = Run("measure", m.Cube, "--bogus", "1");
            AssertError(exit, r, "invalid_arguments", 2);

            (exit, r) = Run("remesh", m.Cube, m.Path_("r.obj"), "--iterations", "many");
            AssertError(exit, r, "invalid_arguments", 2);

            (exit, r) = Run("remesh", m.Cube, m.Path_("r.obj"), "--edge-length", "-1");
            AssertError(exit, r, "invalid_arguments", 2);

            (exit, r) = Run("measure", m.Cube, m.Shifted);
            AssertError(exit, r, "invalid_arguments", 2);

            // the solidify grid of the robust repair grows with the resolution, so it is bounded
            (exit, r) = Run("repair", m.Cube, m.Path_("r.obj"), "--solidify-resolution", "100000");
            AssertError(exit, r, "invalid_arguments", 2);
            Assert.Contains("solidify_resolution", S(r["message"]));

            (exit, r) = Run("frobnicate");
            AssertError(exit, r, "unknown_command", 2);
        }

        [Fact]
        public void Help_ListsCommands()
        {
            var (exit, r) = Run();
            AssertOk(exit, r);
            var names = r["commands"].AsArray().Select(c => S(c["name"])).ToArray();
            Assert.Equal(new[] { "measure", "repair", "boolean", "resolve", "remesh", "simplify", "uv", "convert", "mcp" }, names);
            Assert.Equal(3, I(r["result_codes"]["read_error"]));

            (exit, r) = Run("help", "boolean");
            AssertOk(exit, r);
            Assert.True(B(r["arguments"]["op"]["required"]));

            (exit, r) = Run("repair", "--help");
            AssertOk(exit, r);
            Assert.Equal("robust", S(r["arguments"]["method"]["default"]));

            var sw = new StringWriter();
            Assert.Equal(0, Cli.Run(new[] { "version", "--compact" }, sw));
            Assert.Single(sw.ToString().Trim().Split('\n'));
        }
    }
}
