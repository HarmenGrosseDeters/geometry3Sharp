using System;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> tpms (TpmsLattice, docs/design/TPMS_LATTICE.md): types, styles, fraction, wall thickness, box and mesh domain, enclosure, errors. </summary>
    public class TpmsCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public TpmsCliTests(TestMeshes meshes) { m = meshes; }

        static void AssertError(int exit, JsonObject r, string code, string tpmsStatus = null)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
            if (tpmsStatus != null) Assert.Equal(tpmsStatus, S(r["tpms_status"]));
        }

        [Fact]
        public void Default_IsAGyroidSheetInTwoPeriods()
        {
            string output = m.Path_("tpms_default.obj");
            var (exit, r) = Run("tpms", output, "--divisions", "12", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal("ok", S(r["tpms_status"]));
            Assert.Equal("gyroid", S(r["type"]));
            Assert.Equal("sheet", S(r["style"]));
            Assert.False(B(r["enclosure"]));
            Assert.True(B(r["mesh"]["closed"]));
            Assert.True(B(r["mesh"]["clean"]));
            Assert.True(D(r["mesh"]["volume"]) > 0);
            Assert.InRange(D(r["realized_volume_fraction"]), 0.2, 0.4);
            Assert.True(D(r["mean_wall_thickness"]) > 0);
            Assert.True(File.Exists(output));
            Assert.Equal(I(r["mesh"]["triangles"]), StandardMeshReader.ReadMesh(output).TriangleCount);
            // the default box is two periods: the mesh stays inside it
            Assert.InRange(D(r["mesh"]["bounds"]["max"][0]), 1.0, 2.05);
            Assert.InRange(D(r["mesh"]["bounds"]["min"][2]), -0.05, 1.0);
        }

        [Theory]
        [InlineData("gyroid", "sheet")]
        [InlineData("schwarz_p", "skeletal_low")]
        [InlineData("schwarz_d", "skeletal_high")]
        [InlineData("neovius", "sheet")]
        [InlineData("iwp", "skeletal_low")]
        [InlineData("lidinoid", "skeletal_low")]
        public void EveryType_IsAClosedCleanSolidNearTheTarget(string type, string style)
        {
            var (exit, r) = Run("tpms", m.Path_("tpms_" + type + ".stl"), "--type", type, "--style", style, "--divisions", "16", "--volume-fraction", "0.4", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal("ok", S(r["tpms_status"]));
            Assert.True(B(r["mesh"]["closed"]));
            Assert.True(B(r["mesh"]["clean"]));
            Assert.InRange(D(r["realized_volume_fraction"]), 0.4 - 0.06, 0.4 + 0.06);
            // the option names map to the library enums: the same lattice built directly has the same mesh
            var types = new System.Collections.Generic.Dictionary<string, TpmsType> {
                { "gyroid", TpmsType.Gyroid }, { "schwarz_p", TpmsType.SchwarzP }, { "schwarz_d", TpmsType.SchwarzD }, { "neovius", TpmsType.Neovius }, { "iwp", TpmsType.IWP }, { "lidinoid", TpmsType.Lidinoid } };
            var styles = new System.Collections.Generic.Dictionary<string, TpmsStyle> {
                { "sheet", TpmsStyle.Sheet }, { "skeletal_low", TpmsStyle.SkeletalLow }, { "skeletal_high", TpmsStyle.SkeletalHigh } };
            var direct = new TpmsLattice { Type = types[type], Style = styles[style], CellSize = 1, Divisions = 16, VolumeFraction = 0.4, Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 2)) };
            direct.Compute();
            Assert.Equal(TpmsStatus.Ok, direct.Status);
            Assert.Equal(direct.Mesh.TriangleCount, I(r["mesh"]["triangles"]));
            Assert.Equal(direct.RealizedVolumeFraction, D(r["realized_volume_fraction"]), 9);
        }

        [Fact]
        public void WallThickness_IsReported()
        {
            var (exit, r) = Run("tpms", m.Path_("tpms_wall.obj"), "--wall-thickness", "0.1", "--divisions", "24", "--compact");
            Assert.Equal(0, exit);
            Assert.InRange(D(r["mean_wall_thickness"]), 0.07, 0.13);
        }

        [Fact]
        public void MeshDomain_ClipsToTheInsideOfAClosedMesh()
        {
            // the cube [-1, 1]^3: the lattice must stay inside it up to the rounding of the cut edges
            var (exit, r) = Run("tpms", m.Path_("tpms_domain.obj"), "--domain", m.Cube, "--style", "skeletal_low", "--volume-fraction", "0.5", "--divisions", "12", "--compact");
            Assert.Equal(0, exit);
            Assert.True(B(r["mesh"]["clean"]));
            Assert.InRange(D(r["mesh"]["bounds"]["max"][1]), 0.5, 1.1);
            Assert.InRange(D(r["mesh"]["bounds"]["min"][1]), -1.1, -0.5);
            // a sphere domain (radius 1): every vertex is inside it (up to the cut rounding); a box would reach the corners at sqrt 3
            string sphereOut = m.Path_("tpms_domain_sphere.obj");
            (exit, r) = Run("tpms", sphereOut, "--domain", m.Sphere, "--style", "skeletal_low", "--volume-fraction", "0.5", "--divisions", "16", "--compact");
            Assert.Equal(0, exit);
            double maxRadius = 0;
            foreach (Vector3d v in StandardMeshReader.ReadMesh(sphereOut).Vertices()) maxRadius = Math.Max(maxRadius, v.Length);
            Assert.InRange(maxRadius, 0.8, 1.1);
        }

        [Fact]
        public void Enclosure_NeedsADyadicGrid()
        {
            var (exit, r) = Run("tpms", m.Path_("tpms_enclosure.obj"), "--enclosure", "true", "--divisions", "16", "--compact");
            Assert.Equal(0, exit);
            Assert.True(B(r["enclosure"]));
            Assert.True(D(r["iso_value_used"]) > Math.Sqrt(3) * (1.0 / 16) / 2);
            string rejected = m.Path_("tpms_enclosure_bad.obj");
            File.Delete(rejected);
            (exit, r) = Run("tpms", rejected, "--enclosure", "true", "--cell-size", "0.3", "--compact");
            AssertError(exit, r, "invalid_arguments", "invalid_options");
            Assert.False(File.Exists(rejected));
        }

        [Fact]
        public void BadArguments_AreReportedAndWriteNothing()
        {
            string output = m.Path_("tpms_bad.obj");
            File.Delete(output);
            var (exit, r) = Run("tpms", output, "--box-min", "0,0,0", "--box-max", "1,1", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", output, "--box-min", "1,1,1", "--box-max", "0,2,2", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", output, "--domain", m.Cube, "--box-min", "0,0,0", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", output, "--divisions", "3", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", output, "--volume-fraction", "1", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", output, "--wall-thickness", "0.1", "--volume-fraction", "0.3", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", output, "--wall-thickness", "0.1", "--style", "skeletal_low", "--compact");
            AssertError(exit, r, "invalid_arguments", "invalid_options");
            (exit, r) = Run("tpms", output, "--type", "diamond", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", output, "--max-cells", "100", "--compact");
            AssertError(exit, r, "invalid_arguments", "too_large");
            (exit, r) = Run("tpms", m.Path_("tpms_bad.xyz"), "--compact");
            AssertError(exit, r, "unknown_format");
            Assert.False(File.Exists(output));
        }
    }
}
