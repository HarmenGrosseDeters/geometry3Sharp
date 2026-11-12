using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> offset (MeshSignedDistanceGrid + MarchingCubes, root modes incl. LinearEdges; docs/design/CLI_MCP_UPDATE.md section 1). The cube is [-1, 1]^3. </summary>
    public class OffsetCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public OffsetCliTests(TestMeshes meshes) { m = meshes; }

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        // the volume of the cube [-1,1]^3 grown by d: 8 + 6 * 4 d + 12 * 2 * pi d^2 / 4 + 4 pi d^3 / 3
        static double RoundedBoxVolume(double d) => 8 + 24 * d + 6 * Math.PI * d * d + 4 * Math.PI * d * d * d / 3;

        [Fact]
        public void Outward_IsTheRoundedBox()
        {
            string output = m.Path_("off_out.obj");
            var (exit, r) = Run("offset", m.Cube, output, "--distance", "0.5", "--cell-size", "0.1", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal("ok", S(r["offset_status"]));
            Assert.Equal("none", S(r["enclosure_claim"]));
            Assert.Equal("single_lerp", S(r["root_mode"]));
            Assert.Null(r["root_steps"]);
            Assert.True(B(r["mesh"]["closed"]));
            Assert.InRange(D(r["mesh"]["volume"]), 0.99 * RoundedBoxVolume(0.5), 1.01 * RoundedBoxVolume(0.5));
            Assert.Equal(1.5, D(r["mesh"]["bounds"]["max"][0]), 3);
            Assert.Equal(-1.5, D(r["mesh"]["bounds"]["min"][2]), 3);
            Assert.Equal(8.0, D(r["input_mesh"]["volume"]), 9);
            Assert.Equal(35, I(r["cells"][0]));
            Assert.Equal(35L * 35 * 35, r["grid_cells"].GetValue<long>());
            Assert.True(File.Exists(output));
            Assert.Equal(I(r["mesh"]["triangles"]), StandardMeshReader.ReadMesh(output).TriangleCount);
        }

        [Fact]
        public void Inward_IsTheSmallerCube_AndTooDeepIsEmpty()
        {
            var (exit, r) = Run("offset", m.Cube, m.Path_("off_in.obj"), "--distance=-0.5", "--cell-size", "0.1", "--compact");
            Assert.Equal(0, exit);
            Assert.InRange(D(r["mesh"]["volume"]), 0.9, 1.05);                        // the cube [-0.5, 0.5]^3, its edges cut by the cells
            Assert.Equal(0.5, D(r["mesh"]["bounds"]["max"][1]), 3);
            string never = m.Path_("never.obj");
            var (e2, r2) = Run("offset", m.Cube, never, "--distance=-1.5", "--cell-size", "0.1", "--compact");
            AssertError(e2, r2, "operation_failed");
            Assert.Equal("empty", S(r2["offset_status"]));
            Assert.False(File.Exists(never));
        }

        [Fact]
        public void RelativeCellSize_DefaultsToAHundredthOfTheDiagonal()
        {
            double diagonal = 2 * Math.Sqrt(3);                                            // the box [-1, 1]^3
            var (e1, r1) = Run("offset", m.Cube, m.Path_("off_rel.obj"), "--distance", "0.2", "--compact");
            Assert.Equal(0, e1);
            Assert.Equal(0.01 * diagonal, D(r1["cell_size"]), 12);
            var (e2, r2) = Run("offset", m.Cube, m.Path_("off_rel2.obj"), "--distance", "0.2", "--relative-cell-size", "0.05", "--compact");
            Assert.Equal(0, e2);
            Assert.Equal(0.05 * diagonal, D(r2["cell_size"]), 12);
        }

        [Theory]
        [InlineData("single_lerp", null)]
        [InlineData("lerp_steps", "3")]
        [InlineData("bisection", "8")]
        [InlineData("linear_edges", null)]
        public void RootModes_GiveTheSameBoxWithinACell(string mode, string steps)
        {
            var args = new System.Collections.Generic.List<string> { "offset", m.Cube, m.Path_($"off_{mode}.obj"), "--distance", "0.5", "--cell-size", "0.1", "--root-mode", mode, "--compact" };
            if (steps != null) { args.Add("--root-steps"); args.Add(steps); }
            var (exit, r) = Run(args.ToArray());
            Assert.Equal(0, exit);
            Assert.Equal(mode, S(r["root_mode"]));
            if (steps != null) Assert.Equal(int.Parse(steps), I(r["root_steps"]));
            Assert.True(B(r["mesh"]["closed"]));
            Assert.InRange(D(r["mesh"]["volume"]), 0.99 * RoundedBoxVolume(0.5), 1.01 * RoundedBoxVolume(0.5));
        }

        [Fact]
        public void LinearEdges_FailsOnAViolatedPrecondition_WritesNothing_OtherModesAreFine()
        {
            // distance 0 on a grid whose corners lie on the faces of the cube: samples exactly at the iso value
            string output = m.Path_("off_linear.obj");
            var (exit, r) = Run("offset", m.Cube, output, "--distance", "0", "--cell-size", "0.25", "--root-mode", "linear_edges", "--compact");
            AssertError(exit, r, "operation_failed");
            Assert.Equal("linear_edges_precondition", S(r["offset_status"]));
            Assert.Equal("none", S(r["enclosure_claim"]));
            Assert.False(File.Exists(output));
            var (e2, ok) = Run("offset", m.Cube, m.Path_("off_single.obj"), "--distance", "0", "--cell-size", "0.25", "--compact");
            Assert.Equal(0, e2);
            Assert.True(B(ok["mesh"]["closed"]));
        }

        [Fact]
        public void ParallelOrNot_AndPowerOfTwoScales_GiveTheSameMesh()
        {
            var (_, a) = Run("offset", m.Cube, m.Path_("off_p.obj"), "--distance", "0.5", "--cell-size", "0.125", "--compact");
            var (e, b) = Run("offset", m.Cube, m.Path_("off_s.obj"), "--distance", "0.5", "--cell-size", "0.125", "--no-parallel", "--compact");
            Assert.Equal(0, e);
            Assert.Equal(I(a["mesh"]["triangles"]), I(b["mesh"]["triangles"]));
            Assert.Equal(D(a["mesh"]["volume"]), D(b["mesh"]["volume"]));
            // the cube scaled by 2^k, distance and cell with it: the same triangles, the volume times s^3
            foreach (int k in new[] { -3, 4 }) {
                double s = Math.Pow(2, k);
                DMesh3 cube = StandardMeshReader.ReadMesh(m.Cube);
                MeshTransforms.Scale(cube, s, s, s);
                string scaled = m.Path_($"cube_{k}.obj");
                Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(scaled, cube, WriteOptions.Defaults).code);
                var (e2, c) = Run("offset", scaled, m.Path_($"off_k{k}.obj"), "--distance", (0.5 * s).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    "--cell-size", (0.125 * s).ToString("R", System.Globalization.CultureInfo.InvariantCulture), "--compact");
                Assert.Equal(0, e2);
                Assert.Equal(I(a["mesh"]["triangles"]), I(c["mesh"]["triangles"]));
                Assert.Equal(D(a["mesh"]["volume"]) * s * s * s, D(c["mesh"]["volume"]), 6);
            }
        }

        [Theory]
        [InlineData("--distance", "NaN")]
        [InlineData("--distance", "0.5", "--cell-size", "0")]
        [InlineData("--distance", "0.5", "--cell-size", "-1")]
        [InlineData("--distance", "0.5", "--relative-cell-size", "0")]
        [InlineData("--distance", "0.5", "--relative-cell-size", "1.5")]
        [InlineData("--distance", "0.5", "--cell-size", "0.1", "--relative-cell-size", "0.01")]
        [InlineData("--distance", "0.5", "--root-steps", "3")]                                       // single_lerp
        [InlineData("--distance", "0.5", "--root-mode", "bisection", "--root-steps", "0")]
        [InlineData("--distance", "0.5", "--root-mode", "bisection", "--root-steps", "65")]
        [InlineData("--distance", "0.5", "--max-cells", "0")]
        [InlineData("--distance", "0.5", "--cell-size", "0.01", "--max-cells", "1000")]                // too many cells
        [InlineData("--distance", "1e9", "--cell-size", "0.1")]                                      // more than a million cells of distance
        public void BadArguments_AreInvalidArguments_AndWriteNothing(params string[] more)
        {
            string output = m.Path_("off_bad.obj");
            if (File.Exists(output)) File.Delete(output);
            var (exit, r) = Run(new[] { "offset", m.Cube, output, "--compact" }.Concat(more).ToArray());
            AssertError(exit, r, "invalid_arguments");
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void MissingDistance_OpenMesh_AndFormats()
        {
            var (e1, r1) = Run("offset", m.Cube, m.Path_("x.obj"), "--compact");
            AssertError(e1, r1, "invalid_arguments");
            string output = m.Path_("off_open.obj");
            var (e2, r2) = Run("offset", m.Open, output, "--distance", "0.1", "--cell-size", "0.1", "--compact");
            AssertError(e2, r2, "operation_failed");
            Assert.Contains("closed", S(r2["message"]));
            Assert.False(File.Exists(output));
            var (e3, r3) = Run("offset", m.Cube, m.Path_("x.dwg"), "--distance", "0.1", "--compact");
            AssertError(e3, r3, "unknown_format");
            var (e4, r4) = Run("offset", m.Path_("missing.obj"), m.Path_("x.obj"), "--distance", "0.1", "--compact");
            AssertError(e4, r4, "read_error");
            var (e5, r5) = Run("offset", m.Empty, m.Path_("x.obj"), "--distance", "0.1", "--compact");
            AssertError(e5, r5, "empty_mesh");
        }
    }
}
