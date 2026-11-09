using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using gs;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> wrap (MeshAlphaWrap) and simplify --partitioned (PartitionedReducer). </summary>
    public class WrapPartitionedCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public WrapPartitionedCliTests(TestMeshes meshes) { m = meshes; }

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        static DMesh3 ReadMesh(string path)
        {
            DMesh3 mesh = StandardMeshReader.ReadMesh(path);
            Assert.NotNull(mesh);
            return mesh;
        }

        /// <summary> every vertex of input lies inside the wrap or on it (up to 1e-9) </summary>
        static void AssertEncloses(DMesh3 wrap, DMesh3 input)
        {
            var dev = new MeshDeviation(input, wrap) { Sign = MeshDeviation.Signs.WindingNumber };
            Assert.True(dev.Compute());
            Assert.True(dev.VertexStatistics.Max <= 1e-9, "a vertex of the input lies outside the wrap: " + dev.VertexStatistics.Max);
        }


        // ---- wrap ----

        [Fact]
        public void Wrap_Cube_IsAClosedCleanSolidThatEnclosesTheInput_WithTheLibraryDefaults()
        {
            string output = m.Path_("wrap_cube.stl");
            var (exit, r) = Run("wrap", m.Cube, output, "--compact");
            Assert.Equal(0, exit);
            Assert.Equal("success", S(r["wrap_status"]));
            double diag = 2 * Math.Sqrt(3);
            Assert.Equal(diag / 20, D(r["used_alpha"]), 9);                 // RelativeAlpha 20
            Assert.Equal(diag / 600, D(r["used_offset"]), 9);               // RelativeOffset 600
            Assert.True(B(r["mesh"]["closed"]));
            Assert.True(B(r["mesh"]["clean"]), r["mesh"].ToJsonString());
            Assert.Equal(1, I(r["mesh"]["components"]));
            Assert.InRange(D(r["mesh"]["volume"]), 8.0, 8.0 * 1.2);           // encloses the cube, close to it
            Assert.NotEmpty(r["counts"].AsObject());
            Assert.NotEmpty(r["phase_ms"].AsObject());
            AssertEncloses(ReadMesh(output), ReadMesh(m.Cube));
        }

        [Fact]
        public void Wrap_OpenAndOverlappingInputs_GiveClosedSolids()
        {
            foreach (string input in new[] { m.Open, m.Overlapping, m.Flipped }) {
                string output = m.Path_("wrap_" + Path.GetFileNameWithoutExtension(input) + ".ply");
                var (exit, r) = Run("wrap", input, output, "--compact");
                Assert.Equal(0, exit);
                Assert.True(B(r["mesh"]["clean"]), input + " " + r["mesh"].ToJsonString());
                AssertEncloses(ReadMesh(output), ReadMesh(input));
            }
        }

        [Fact]
        public void Wrap_AlphaAndOffsetOptions_AreUsed()
        {
            var (e1, r1) = Run("wrap", m.Sphere, m.Path_("w1.obj"), "--alpha", "0.3", "--offset", "0.01", "--compact");
            Assert.Equal(0, e1);
            Assert.Equal(0.3, D(r1["used_alpha"]), 12);
            Assert.Equal(0.01, D(r1["used_offset"]), 12);
            var (e2, r2) = Run("wrap", m.Sphere, m.Path_("w2.obj"), "--relative-alpha", "10", "--relative-offset", "100", "--compact");
            Assert.Equal(0, e2);
            double diag = 2 * Math.Sqrt(3);                                       // the sphere's bounding box [-1,1]^3
            Assert.Equal(diag / 10, D(r2["used_alpha"]), 6);
            Assert.Equal(diag / 100, D(r2["used_offset"]), 6);
        }

        [Fact]
        public void Wrap_SmallerAlpha_FollowsMoreDetail()
        {
            var (_, coarse) = Run("wrap", m.Overlapping, m.Path_("wc.obj"), "--alpha", "1.0", "--offset", "0.05", "--compact");
            var (_, fine) = Run("wrap", m.Overlapping, m.Path_("wf.obj"), "--alpha", "0.25", "--offset", "0.05", "--compact");
            Assert.True(I(fine["mesh"]["triangles"]) > I(coarse["mesh"]["triangles"]));
        }

        [Fact]
        public void Wrap_PointLimit_IsAValidWrapWithAWarning()
        {
            string output = m.Path_("wrap_limit.obj");
            var (exit, r) = Run("wrap", m.Sphere, output, "--alpha", "0.1", "--offset", "0.01", "--max-steiner-points", "50", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal("point_limit_reached", S(r["wrap_status"]));
            Assert.Contains(r["warnings"].AsArray().Select(S), w => w.Contains("max_steiner_points"));
            Assert.True(B(r["mesh"]["closed"]));
            AssertEncloses(ReadMesh(output), ReadMesh(m.Sphere));
        }

        [Fact]
        public void Wrap_RefinementOptions_ChangeTheResult()
        {
            string[] common = { "--alpha", "0.3", "--offset", "0.02", "--compact" };
            var (_, withAll) = Run(new[] { "wrap", m.Overlapping, m.Path_("wa.obj") }.Concat(common).ToArray());
            var (e, none) = Run(new[] { "wrap", m.Overlapping, m.Path_("wn.obj"), "--no-refine-non-manifold", "--refine-min-angle", "0" }.Concat(common).ToArray());
            Assert.Equal(0, e);
            Assert.NotEqual(I(withAll["mesh"]["triangles"]), I(none["mesh"]["triangles"]));
            Assert.True(B(none["mesh"]["closed"]));
        }

        [Fact]
        public void Wrap_Deviation_IsReported()
        {
            var (exit, r) = Run("wrap", m.Cube, m.Path_("wd.obj"), "--deviation", "--compact");
            Assert.Equal(0, exit);
            Assert.InRange(D(r["hausdorff"]["distance"]), 0, 0.5);
        }

        [Theory]
        [InlineData("--alpha", "0")]
        [InlineData("--alpha", "-1")]
        [InlineData("--offset", "0")]
        [InlineData("--relative-alpha", "-2")]
        [InlineData("--relative-offset", "0")]
        [InlineData("--max-steiner-points", "0")]
        [InlineData("--refine-min-angle", "60")]
        [InlineData("--refine-min-angle", "-1")]
        public void Wrap_BadOptions_AreInvalidArguments(string name, string value)
        {
            var (exit, r) = Run("wrap", m.Path_("missing.obj"), m.Path_("o.obj"), name, value);
            AssertError(exit, r, "invalid_arguments");
        }

        [Fact]
        public void Wrap_ConflictingAndBadInputs_AreResultCodes()
        {
            var (e1, r1) = Run("wrap", m.Cube, m.Path_("o.obj"), "--alpha", "0.1", "--relative-alpha", "10"); AssertError(e1, r1, "invalid_arguments");
            var (e2, r2) = Run("wrap", m.Cube, m.Path_("o.obj"), "--offset", "0.1", "--relative-offset", "10"); AssertError(e2, r2, "invalid_arguments");
            var (e3, r3) = Run("wrap", m.Garbage, m.Path_("o.obj")); AssertError(e3, r3, "read_error");
            var (e4, r4) = Run("wrap", m.Empty, m.Path_("o.obj")); AssertError(e4, r4, "empty_mesh");
            var (e5, r5) = Run("wrap", m.Cube, m.Path_("no_such_dir/o.obj")); AssertError(e5, r5, "write_error");
            Assert.False(File.Exists(m.Path_("o.obj")));
        }


        // ---- simplify --partitioned ----

        string Big => BigSphere();
        string bigPath;
        string BigSphere()
        {
            if (bigPath != null) return bigPath;
            bigPath = m.Path_("big_sphere.ply");
            DMesh3 bigger = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 46 }.Generate().MakeDMesh();   // 6 * 2 * 45^2 = 24300 triangles
            Assert.True(bigger.TriangleCount >= 20000);
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(bigPath, bigger, new WriteOptions() { bWriteBinary = true }).code);
            return bigPath;
        }

        [Fact]
        public void SimplifyPartitioned_ReducesAndIsIndependentOfThreads()
        {
            string o1 = m.Path_("p1.ply"), o4 = m.Path_("p4.ply");
            var (e1, r1) = Run("simplify", Big, o1, "--max-error", "1e-6", "--partitioned", "--threads", "1", "--compact");
            var (e4, r4) = Run("simplify", Big, o4, "--max-error", "1e-6", "--partitioned", "--threads", "4", "--compact");
            Assert.Equal(0, e1); Assert.Equal(0, e4);
            Assert.True(B(r1["used_partition"]));
            Assert.True(I(r1["mesh"]["triangles"]) < I(r1["input_mesh"]["triangles"]) / 2);
            Assert.True(B(r1["mesh"]["closed"]));
            Assert.True(B(r1["mesh"]["clean"]), r1["mesh"].ToJsonString());
            Assert.Equal(I(r1["mesh"]["triangles"]), I(r4["mesh"]["triangles"]));
            Assert.Equal(File.ReadAllBytes(o1), File.ReadAllBytes(o4));       // the same result for every thread count
        }

        [Fact]
        public void SimplifyPartitioned_SmallMeshRunsSerially_WithAWarning()
        {
            var (exit, r) = Run("simplify", m.Sphere, m.Path_("ps.obj"), "--max-error", "1e-4", "--partitioned", "--compact");
            Assert.Equal(0, exit);
            Assert.False(B(r["used_partition"]));
            Assert.Contains(r["warnings"].AsArray().Select(S), w => w.Contains("serial"));
        }

        [Fact]
        public void SimplifyPartitioned_OpenMesh_WarnsAboutTheBoundary()
        {
            var (exit, r) = Run("simplify", m.Open, m.Path_("po.obj"), "--max-error", "1e-4", "--partitioned", "--compact");
            Assert.Contains(r["warnings"].AsArray().Select(S), w => w.Contains("boundary"));
        }

        [Fact]
        public void Simplify_WithoutPartitioned_IsUnchanged()
        {
            var (exit, r) = Run("simplify", m.Sphere, m.Path_("sp.obj"), "--ratio", "0.5", "--compact");
            Assert.Equal(0, exit);
            Assert.Null(r["used_partition"]);
        }

        [Theory]
        [InlineData("--ratio", "0.5")]
        [InlineData("--triangles", "100")]
        public void SimplifyPartitioned_NeedsMaxError(string name, string value)
        {
            var (exit, r) = Run("simplify", m.Sphere, m.Path_("pe.obj"), name, value, "--partitioned");
            AssertError(exit, r, "invalid_arguments");
        }

        [Fact]
        public void SimplifyPartitioned_BadThreadsAndError()
        {
            var (e1, r1) = Run("simplify", m.Sphere, m.Path_("pe.obj"), "--max-error", "1e-4", "--partitioned", "--threads", "-1"); AssertError(e1, r1, "invalid_arguments");
            var (e2, r2) = Run("simplify", m.Sphere, m.Path_("pe.obj"), "--max-error", "0", "--partitioned"); AssertError(e2, r2, "invalid_arguments");
        }
    }
}
