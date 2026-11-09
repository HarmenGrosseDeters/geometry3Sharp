using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> The analysis commands: fidelity (MeshFidelityMetrics) and quality (TriangleQualityStatistics, MeshTopologySummary). </summary>
    public class AnalysisCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public AnalysisCliTests(TestMeshes meshes) { m = meshes; }

        static readonly string[] Fast = { "--surface-samples", "2000", "--edge-samples", "500", "--volume-samples", "4000" };

        (int exit, JsonObject r) Fid(string reference, string mesh, params string[] more)
            => Run(new[] { "fidelity", reference, mesh, "--compact" }.Concat(Fast).Concat(more).ToArray());

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        [Fact]
        public void Fidelity_IdenticalMeshes_AreAPerfectMatch()
        {
            var (exit, r) = Fid(m.Sphere, m.Sphere);
            Assert.Equal(0, exit);
            Assert.Equal("ok", S(r["code"]));
            Assert.Equal(3, r["f_score"].AsArray().Count);
            Assert.All(r["f_score"].AsArray(), v => Assert.Equal(1.0, D(v), 9));
            Assert.True(D(r["chamfer"]) < 1e-9);
            Assert.True(D(r["volume_iou"]) > 0.999);
            Assert.Equal(0.0, D(r["normals_mesh_to_reference"]["reversed_fraction"]), 9);
            Assert.Empty(r["topology_changes"].AsArray());
            Assert.Equal(S(r["reference_quality"].ToJsonString()), S(r["mesh_quality"].ToJsonString()));
        }

        [Fact]
        public void Fidelity_DisjointMeshes_HaveNoOverlap()
        {
            var (exit, r) = Fid(m.Cube, m.Far, "--relative-thresholds", "0.01,0.1");
            Assert.Equal(0, exit);
            Assert.Equal(new[] { 0.01, 0.1 }, r["relative_thresholds"].AsArray().Select(D));
            Assert.All(r["f_score"].AsArray(), v => Assert.Equal(0.0, D(v)));
            Assert.Equal(0.0, D(r["volume_iou"]));
            Assert.True(D(r["chamfer"]) > 0.5); // relative to the reference's size
        }

        [Fact]
        public void Fidelity_SeesReversedTriangles_AndOptionsSwitchGroupsOff()
        {
            var (exit, r) = Fid(m.Sphere, m.Flipped, "--no-outer-surface", "--no-curvature");
            Assert.Equal(0, exit);
            double reversed = D(r["normals_mesh_to_reference"]["reversed_fraction"]);
            Assert.InRange(reversed, 0.02, 0.1); // every 20th triangle
            Assert.Null(r["outer"]);
            Assert.Null(r["curvature"]);
        }

        [Fact]
        public void Fidelity_Open_ReportsTopologyChange()
        {
            var (exit, r) = Fid(m.Cube, m.Open);
            Assert.Equal(0, exit);
            Assert.Contains("boundary_loops_added", r["topology_changes"].AsArray().Select(S));
            Assert.Equal(1, I(r["mesh_topology"]["boundary_loops"]));
            // the open cube lies on the cube: all of it is precise, but a twelfth of the reference is missing
            Assert.Equal(12, I(r["reference_quality"]["triangles"]));
            Assert.Equal(11, I(r["mesh_quality"]["triangles"]));
            Assert.Equal(1.0, D(r["precision"][0]), 6);
            Assert.InRange(D(r["recall"][0]), 0.85, 0.95);
        }

        [Fact]
        public void Fidelity_IsReproducible_AndIndependentOfParallel()
        {
            var a = Fid(m.Sphere, m.Flipped, "--seed", "5").r.ToJsonString();
            var b = Fid(m.Sphere, m.Flipped, "--seed", "5").r.ToJsonString();
            var c = Fid(m.Sphere, m.Flipped, "--seed", "5", "--no-parallel").r.ToJsonString();
            Assert.Equal(Strip(a), Strip(b));
            Assert.Equal(Strip(a), Strip(c));
        }

        static string Strip(string json) => json.Substring(0, json.IndexOf("\"elapsed_ms\""));

        [Fact]
        public void Fidelity_DistancesAreRelativeToSize_AndScaleInvariant()
        {
            var (e1, a) = Fid(m.Cube, m.Far);
            Assert.Equal(0, e1);
            Assert.Equal(2 * System.Math.Sqrt(3), D(a["size"]), 6); // diagonal of [-1,1]^3
            // both meshes scaled by 1000: size scales, the relative values do not
            string big = m.Path_("cube_big.obj"), bigFar = m.Path_("far_big.obj");
            Scaled(m.Cube, big, 1000); Scaled(m.Far, bigFar, 1000);
            var (e2, b) = Fid(big, bigFar);
            Assert.Equal(0, e2);
            Assert.Equal(1000 * D(a["size"]), D(b["size"]), 3);
            Assert.Equal(D(a["chamfer"]), D(b["chamfer"]), 9);
            Assert.Equal(D(a["distance_mesh_to_reference"]), D(b["distance_mesh_to_reference"]), 9);
            Assert.Equal(D(a["max_distance_reference_to_mesh"]), D(b["max_distance_reference_to_mesh"]), 9);
            // a shift of 5 on a diagonal of 3.46: far more than the cube's own size, so the mean distance exceeds 1
            Assert.True(D(a["distance_mesh_to_reference"]) > 1);
        }

        static void Scaled(string from, string to, double factor)
        {
            var mesh = g3.StandardMeshReader.ReadMesh(from);
            g3.MeshTransforms.Scale(mesh, factor, factor, factor);
            g3.StandardMeshWriter.WriteMesh(to, mesh, g3.WriteOptions.Defaults);
        }

        [Fact]
        public void Fidelity_OptionsTakeEffect()
        {
            string baseline = Strip(Fid(m.Sphere, m.Flipped, "--seed", "1").r.ToJsonString());
            Assert.NotEqual(baseline, Strip(Fid(m.Sphere, m.Flipped, "--seed", "2").r.ToJsonString()));
            string[] fast = { "--surface-samples", "2000", "--edge-samples", "500", "--volume-samples", "4000" };
            // a sphere has no sharp edges at 30 degrees, a cube has; at 180 nothing is sharp
            var (_, cube30) = Run(new[] { "fidelity", m.Cube, m.Cube, "--compact" }.Concat(fast).ToArray());
            Assert.True(D(cube30["sharp_edge_length_reference"]) > 0);
            var (_, cube180) = Run(new[] { "fidelity", m.Cube, m.Cube, "--compact", "--sharp-edge-angle", "180" }.Concat(fast).ToArray());
            Assert.Equal(0.0, D(cube180["sharp_edge_length_reference"]));
            Assert.Equal(D(cube30["sharp_edge_length_reference"]), D(cube30["sharp_edge_length_mesh"]), 9);
            // surface and volume samples change the sampled results
            var (_, few) = Run("fidelity", m.Sphere, m.Flipped, "--compact", "--surface-samples", "300", "--edge-samples", "100", "--volume-samples", "1000");
            var (_, many) = Run("fidelity", m.Sphere, m.Flipped, "--compact", "--surface-samples", "3000", "--edge-samples", "100", "--volume-samples", "1000");
            Assert.NotEqual(D(few["normals_mesh_to_reference"]["samples"]), D(many["normals_mesh_to_reference"]["samples"]));
        }

        [Fact]
        public void Fidelity_BlocksCarryValues()
        {
            var (exit, r) = Fid(m.Cube, m.Open);
            Assert.Equal(0, exit);
            Assert.InRange(D(r["normal_consistency"]), 0.5, 1.0);
            Assert.Equal(0.0, D(r["distance_mesh_to_reference"]), 6);        // the open cube lies on the cube
            Assert.True(D(r["distance_reference_to_mesh"]) > 0);               // the missing triangle
            Assert.True(D(r["max_distance_reference_to_mesh"]) > D(r["max_distance_mesh_to_reference"]));
            Assert.True(D(r["outer"]["share"]) > 0);
            Assert.NotNull(r["curvature"]["total_abs_mean_reference"]);
            Assert.True(D(r["sharp_edge_length_reference"]) > 0);
            Assert.True(D(r["volume_missing"]) >= 0);
        }

        [Fact]
        public void Fidelity_InfinityIsAString_NotNull()
        {
            // the cube has sharp edges, the sphere none: the edge Chamfer distance is infinite
            var (exit, r) = Fid(m.Cube, m.Sphere);
            Assert.Equal(0, exit);
            Assert.Equal("infinity", S(r["edge_chamfer"]));
        }

        [Theory]
        [InlineData("--relative-thresholds", "0.01,-1")]
        [InlineData("--relative-thresholds", "abc")]
        [InlineData("--surface-samples", "0")]
        [InlineData("--edge-samples", "-3")]
        [InlineData("--volume-samples", "0")]
        [InlineData("--sharp-edge-angle", "181")]
        [InlineData("--surface-samples", "2147483647")]
        [InlineData("--relative-thresholds", "1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17")]
        public void Fidelity_BadOptions_AreInvalidArguments(string name, string value)
        {
            var (exit, r) = Run("fidelity", m.Cube, m.Cube, name, value);
            AssertError(exit, r, "invalid_arguments");
            // validated before the inputs are read
            var (exit2, r2) = Run("fidelity", m.Path_("missing.obj"), m.Cube, name, value);
            AssertError(exit2, r2, "invalid_arguments");
        }

        [Fact]
        public void Fidelity_BadInputs_AreResultCodes()
        {
            var (e, bad) = Fid(m.Cube, m.Garbage);
            AssertError(e, bad, "read_error");
            var (exit, r) = Fid(m.Empty, m.Cube);
            Assert.NotEqual(0, exit);
            Assert.Equal("error", S(r["status"]));
        }

        [Fact]
        public void Quality_Sphere_IsOneGenusZeroComponent()
        {
            var (exit, r) = Run("quality", m.Sphere, "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(768, I(r["quality"]["triangles"]));
            Assert.Equal(0, I(r["quality"]["degenerate"]));
            Assert.True(D(r["quality"]["min_angle"]["worst"]) > 20);
            Assert.True(D(r["quality"]["min_angle"]["median"]) >= D(r["quality"]["min_angle"]["worst"]));
            var t = r["topology"];
            Assert.Equal(1, I(t["components"]));
            Assert.Equal(2, I(t["euler_characteristic"]));
            Assert.Equal(0.0, D(t["genus"]));
            Assert.Equal(0, I(t["boundary_loops"]));
            Assert.Single(t["component_list"].AsArray());
        }

        [Fact]
        public void Quality_Cube_HasRightAngledTriangles()
        {
            var (exit, r) = Run("quality", m.Cube, "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(12, I(r["quality"]["triangles"]));
            Assert.Equal(45.0, D(r["quality"]["min_angle"]["worst"]), 6);
            Assert.Equal(90.0, D(r["quality"]["max_angle"]["worst"]), 6);
            Assert.Equal(0, I(r["quality"]["max_angle_above_90"]));
            Assert.Equal(0, I(r["quality"]["min_angle_below_30"]));
            Assert.Equal(0, I(r["quality"]["min_angle_below_10"]));
            Assert.Equal(0, I(r["quality"]["max_angle_above_160"]));
            Assert.Equal(12, I(r["quality"]["aspect_ratio_above_1_3"])); // right isosceles triangles: aspect ratio ~1.5
            Assert.Equal(0, I(r["quality"]["condition_above_1_3"])); // condition 1.155
        }

        [Fact]
        public void Quality_OpenAndSeveralComponents_AndComponentLimit()
        {
            var (e1, open) = Run("quality", m.Open, "--compact");
            Assert.Equal(0, e1);
            Assert.Equal(1, I(open["topology"]["boundary_loops"]));
            Assert.Equal(3, I(open["topology"]["boundary_edges"]));

            var (e2, two) = Run("quality", m.Overlapping, "--compact");
            Assert.Equal(0, e2);
            Assert.Equal(2, I(two["topology"]["components"]));
            Assert.Equal(2, I(two["topology"]["closed_components"]));
            Assert.Equal(2, two["topology"]["component_list"].AsArray().Count);

            var (e0, none) = Run("quality", m.Overlapping, "--max-components", "0", "--compact");
            Assert.Equal(0, e0);
            Assert.Empty(none["topology"]["component_list"].AsArray());
            Assert.True(B(none["topology"]["component_list_truncated"]));

            var (e3, limited) = Run("quality", m.Overlapping, "--max-components", "1", "--compact");
            Assert.Equal(0, e3);
            Assert.Single(limited["topology"]["component_list"].AsArray());
            Assert.True(B(limited["topology"]["component_list_truncated"]));
        }

        [Fact]
        public void Quality_DegenerateAndSliverTriangles_AreCounted_InfinityIsNotNull()
        {
            string path = m.Path_("sliver.obj");
            System.IO.File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 2 0 0\nv 3 0 0\nv 4 0.001 0\nf 1 2 3\nf 1 2 4\nf 2 4 6\n");
            var (exit, r) = Run("quality", path, "--compact");
            Assert.Equal(0, exit);
            var q = r["quality"];
            Assert.Equal(3, I(q["triangles"]));
            Assert.Equal(1, I(q["degenerate"]));
            Assert.Equal("infinity", S(q["aspect_ratio"]["worst"]));
            Assert.Equal("infinity", S(q["condition"]["worst"]));
            Assert.True(I(q["min_angle_below_1"]) >= 2);
            Assert.True(I(q["max_angle_above_160"]) >= 2);
            Assert.True(I(q["scaled_jacobian_below_0_5"]) >= 2);
            Assert.True(I(q["min_angle_below_10"]) >= I(q["min_angle_below_1"]));
            Assert.True(D(q["min_angle"]["worst"]) < 1e-6);
        }

        [Fact]
        public void Quality_Errors()
        {
            var (e1, r1) = Run("quality", m.Empty); AssertError(e1, r1, "empty_mesh");
            var (e2, r2) = Run("quality", m.Garbage); AssertError(e2, r2, "read_error");
            var (e3, r3) = Run("quality", m.Cube, "--max-components", "-1"); AssertError(e3, r3, "invalid_arguments");
        }

        [Fact]
        public void BothCommands_AreMcpTools()
        {
            Assert.NotNull(Commands.Find("fidelity"));
            Assert.NotNull(Commands.Find("quality"));
            var schema = Commands.Find("fidelity").InputSchema();
            Assert.Contains("reference", schema["required"].AsArray().Select(S));
            Assert.Contains("mesh", schema["required"].AsArray().Select(S));
        }
    }
}
