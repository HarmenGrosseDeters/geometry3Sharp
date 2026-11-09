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

        [Theory]
        [InlineData("--relative-thresholds", "0.01,-1")]
        [InlineData("--relative-thresholds", "abc")]
        [InlineData("--surface-samples", "0")]
        [InlineData("--edge-samples", "-3")]
        [InlineData("--volume-samples", "0")]
        [InlineData("--sharp-edge-angle", "181")]
        public void Fidelity_BadOptions_AreInvalidArguments(string name, string value)
        {
            var (exit, r) = Run("fidelity", m.Cube, m.Cube, name, value);
            AssertError(exit, r, "invalid_arguments");
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

            var (e3, limited) = Run("quality", m.Overlapping, "--max-components", "1", "--compact");
            Assert.Equal(0, e3);
            Assert.Single(limited["topology"]["component_list"].AsArray());
            Assert.True(B(limited["topology"]["component_list_truncated"]));
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
