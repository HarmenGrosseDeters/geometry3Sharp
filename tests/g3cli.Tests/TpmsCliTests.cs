using System;
using System.IO;
using System.Linq;
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
        public void ThinSheet_OnOk_ReportsTheLatticeMessageAsAWarning()
        {
            // a wall far thinner than two cubes: marching cubes loses most of it, TpmsLattice stays Ok with a Message (#1596), the CLI passes it on
            var (exit, r) = Run("tpms", m.Path_("tpms_thin.obj"), "--wall-thickness", "0.04", "--divisions", "12", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal("ok", S(r["tpms_status"]));
            string[] w = r["warnings"].AsArray().Select(x => S(x)).ToArray();
            Assert.Contains(w, x => x.StartsWith("tpms: Sheet: the realized volume fraction") && x.Contains("TwoLevelSheet"));
        }

        [Fact]
        public void EnclosureAboveFractionOne_OnOk_ReportsTheLatticeMessageAsAWarning()
        {
            var (exit, r) = Run("tpms", m.Path_("tpms_enc1.obj"), "--enclosure", "true", "--divisions", "4", "--volume-fraction", "0.9", "--compact");
            Assert.Equal(0, exit);
            string[] w = r["warnings"].AsArray().Select(x => S(x)).ToArray();
            Assert.Contains(w, x => x.StartsWith("tpms: Enclosure: the realized volume fraction") && x.Contains("above 1"));
        }

        [Fact]
        public void OrdinaryLattice_HasNoLatticeMessageWarning()
        {
            var (exit, r) = Run("tpms", m.Path_("tpms_nomsg.obj"), "--divisions", "12", "--compact");
            Assert.Equal(0, exit);
            Assert.DoesNotContain(r["warnings"].AsArray().Select(x => S(x)), x => x.StartsWith("tpms:"));
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
            // far from the default volume fraction 0.3 (mean thickness about 0.1): an ignored option is seen
            var (exit, r) = Run("tpms", m.Path_("tpms_wall.obj"), "--wall-thickness", "0.04", "--divisions", "32", "--compact");
            Assert.Equal(0, exit);
            Assert.InRange(D(r["mean_wall_thickness"]), 0.03, 0.05);
            Assert.InRange(D(r["realized_volume_fraction"]), 0.08, 0.17);
        }

        [Fact]
        public void BoxAndOrigin_MatchTheLibraryCalledDirectly()
        {
            var (exit, r) = Run("tpms", m.Path_("tpms_box.obj"), "--type", "schwarz_p", "--cell-size", "2", "--divisions", "8", "--box-min", "-1,0,1", "--box-max", "3,2,2.5",
                "--origin", "0.5,0.25,-0.5", "--parallel", "false", "--compact");
            Assert.Equal(0, exit);
            var direct = new TpmsLattice { Type = TpmsType.SchwarzP, CellSize = 2, Divisions = 8, Box = new AxisAlignedBox3d(new Vector3d(-1, 0, 1), new Vector3d(3, 2, 2.5)), Origin = new Vector3d(0.5, 0.25, -0.5), ParallelCompute = false };
            direct.Compute();
            Assert.Equal(TpmsStatus.Ok, direct.Status);
            Assert.Equal(direct.Mesh.TriangleCount, I(r["mesh"]["triangles"]));
            Assert.Equal(direct.RealizedVolumeFraction, D(r["realized_volume_fraction"]), 9);
            Assert.Equal(MeshMeasure(direct.Mesh), D(r["mesh"]["volume"]), 6);
            // the box lives in the lattice frame, so it moves with the origin: world box [-0.5, 3.5] x [0.25, 2.25] x [0.5, 2]; each axis is checked (a swap or a dropped option would show)
            Assert.InRange(D(r["mesh"]["bounds"]["min"][0]), -0.55, -0.3);
            Assert.InRange(D(r["mesh"]["bounds"]["max"][1]), 1.9, 2.3);
            Assert.InRange(D(r["mesh"]["bounds"]["max"][2]), 1.6, 2.05);
            // without the origin the same lattice sits at the box itself (the frame moves the whole result)
            (exit, r) = Run("tpms", m.Path_("tpms_box0.obj"), "--type", "schwarz_p", "--cell-size", "2", "--divisions", "8", "--box-min", "-1,0,1", "--box-max", "3,2,2.5", "--compact");
            Assert.Equal(0, exit);
            Assert.InRange(D(r["mesh"]["bounds"]["min"][0]), -1.05, -0.8);
        }

        static double MeshMeasure(DMesh3 mesh) => MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;

        [Fact]
        public void Parallel_GivesTheSameMesh()
        {
            var (e1, a) = Run("tpms", m.Path_("tpms_par1.obj"), "--divisions", "12", "--parallel", "true", "--compact");
            var (e2, b) = Run("tpms", m.Path_("tpms_par0.obj"), "--divisions", "12", "--parallel", "false", "--compact");
            var (e3, c) = Run("tpms", m.Path_("tpms_par2.obj"), "--divisions", "12", "--parallel", "true", "--compact");
            Assert.Equal(0, e1 + e2 + e3);
            Assert.Equal(I(a["mesh"]["triangles"]), I(b["mesh"]["triangles"]));
            Assert.Equal(D(a["mesh"]["volume"]), D(b["mesh"]["volume"]));
            Assert.Equal(D(a["mesh"]["volume"]), D(c["mesh"]["volume"]));
        }

        [Fact]
        public void ScaleByAPowerOfTwo_GivesTheSameMeshScaled()
        {
            var (e1, a) = Run("tpms", m.Path_("tpms_s1.obj"), "--divisions", "12", "--compact");
            var (e2, b) = Run("tpms", m.Path_("tpms_s4.obj"), "--divisions", "12", "--cell-size", "4", "--box-max", "8,8,8", "--compact");
            Assert.Equal(0, e1 + e2);
            Assert.Equal(I(a["mesh"]["triangles"]), I(b["mesh"]["triangles"]));
            Assert.Equal(D(a["mesh"]["volume"]) * 64, D(b["mesh"]["volume"]));
        }

        [Fact]
        public void Hybrid_MatchesTheLibraryCalledDirectly()
        {
            var (exit, r) = Run("tpms", m.Path_("tpms_hybrid.obj"), "--type", "gyroid", "--second-type", "iwp", "--blend", "0,1,0.5", "--style", "skeletal_low", "--volume-fraction", "0.35",
                "--divisions", "12", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal("ok", S(r["tpms_status"]));
            Assert.Equal("iwp", S(r["second_type"]));
            Assert.True(B(r["mesh"]["clean"]));
            var direct = new TpmsLattice { Type = TpmsType.Gyroid, SecondType = TpmsType.IWP, Blend = TpmsLattice.SigmoidBlend(0, 1, 0.5), Style = TpmsStyle.SkeletalLow, CellSize = 1, Divisions = 12,
                VolumeFraction = 0.35, Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 2)) };
            direct.Compute();
            Assert.Equal(TpmsStatus.Ok, direct.Status);
            Assert.Equal(direct.Mesh.TriangleCount, I(r["mesh"]["triangles"]));
            Assert.Equal(direct.RealizedVolumeFraction, D(r["realized_volume_fraction"]), 9);
            Assert.Equal(direct.SecondLevel, D(r["second_level"]), 9);
            Assert.Equal(direct.Level, D(r["level"]), 9);
            Assert.NotEqual(direct.Level, D(r["second_level"]));
            Assert.Equal(2.0, D(r["blend_gradient_bound"]), 12);
            // the pure gyroid is a different mesh: the second type and the blend are not ignored
            (exit, r) = Run("tpms", m.Path_("tpms_pure.obj"), "--style", "skeletal_low", "--volume-fraction", "0.35", "--divisions", "12", "--compact");
            Assert.Equal(0, exit);
            Assert.NotEqual(I(r["mesh"]["triangles"]), direct.Mesh.TriangleCount);
            Assert.Null(r["second_type"]);
            // the axis selects the plane: on a longer box the blend across y is the library's axis 1, and not its axis 0
            (exit, r) = Run("tpms", m.Path_("tpms_hybrid_y.obj"), "--type", "gyroid", "--second-type", "iwp", "--blend", "1,1.5,0.5", "--style", "skeletal_low", "--volume-fraction", "0.35",
                "--divisions", "12", "--box-max", "2,3,2", "--compact");
            Assert.Equal(0, exit);
            Func<int, int> count = axis => {
                var l = new TpmsLattice { Type = TpmsType.Gyroid, SecondType = TpmsType.IWP, Blend = TpmsLattice.SigmoidBlend(axis, 1.5, 0.5), Style = TpmsStyle.SkeletalLow, CellSize = 1, Divisions = 12,
                    VolumeFraction = 0.35, Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 3, 2)) };
                l.Compute();
                Assert.Equal(TpmsStatus.Ok, l.Status);
                return l.Mesh.TriangleCount;
            };
            Assert.Equal(count(1), I(r["mesh"]["triangles"]));
            Assert.NotEqual(count(0), I(r["mesh"]["triangles"]));
        }

        [Theory]
        [InlineData("schwarz_p", TpmsType.SchwarzP)]
        [InlineData("schwarz_d", TpmsType.SchwarzD)]
        [InlineData("neovius", TpmsType.Neovius)]
        [InlineData("lidinoid", TpmsType.Lidinoid)]
        [InlineData("gyroid", TpmsType.Gyroid)]
        public void Hybrid_EverySecondTypeNameSelectsItsType(string name, TpmsType expected)
        {
            // (a different first type, so that second type gyroid is not the pure lattice either)
            var (exit, r) = Run("tpms", m.Path_("tpms_second_" + name + ".obj"), "--type", "schwarz_p", "--second-type", name, "--blend", "0,1,0.5", "--divisions", "8", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(name, S(r["second_type"]));
            var direct = new TpmsLattice { Type = TpmsType.SchwarzP, SecondType = expected, Blend = TpmsLattice.SigmoidBlend(0, 1, 0.5), CellSize = 1, Divisions = 8,
                Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 2)) };
            direct.Compute();
            Assert.Equal(TpmsStatus.Ok, direct.Status);
            Assert.Equal(direct.Mesh.TriangleCount, I(r["mesh"]["triangles"]));
        }

        [Fact]
        public void Hybrid_WorksWithEnclosureOnADyadicGrid()
        {
            var (exit, r) = Run("tpms", m.Path_("tpms_hybrid_enc.obj"), "--second-type", "iwp", "--blend", "0,1,0.5", "--enclosure", "true", "--divisions", "16", "--compact");
            Assert.Equal(0, exit);
            Assert.True(B(r["enclosure"]));
            Assert.True(B(r["mesh"]["clean"]));
            var direct = new TpmsLattice { Type = TpmsType.Gyroid, SecondType = TpmsType.IWP, Blend = TpmsLattice.SigmoidBlend(0, 1, 0.5), CellSize = 1, Divisions = 16,
                Enclosure = true, Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 2)) };
            direct.Compute();
            Assert.Equal(TpmsStatus.Ok, direct.Status);
            Assert.Equal(direct.Mesh.TriangleCount, I(r["mesh"]["triangles"]));
            Assert.Equal(direct.IsoValueUsed, D(r["iso_value_used"]), 12);
            Assert.Equal("iwp", S(r["second_type"]));
        }

        [Fact]
        public void Hybrid_BadArguments_AreReportedAndWriteNothing()
        {
            string output = m.Path_("tpms_hybrid_bad.obj");
            File.Delete(output);
            var (exit, r) = Run("tpms", output, "--second-type", "iwp", "--compact");
            AssertError(exit, r, "invalid_arguments");
            Assert.Contains("together", S(r["message"]));
            (exit, r) = Run("tpms", output, "--blend", "0,1,0.5", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", output, "--second-type", "iwp", "--blend", "3,1,0.5", "--compact");
            AssertError(exit, r, "invalid_arguments");
            Assert.Contains("axis", S(r["message"]));
            (exit, r) = Run("tpms", output, "--second-type", "iwp", "--blend", "0,1,0", "--compact");
            AssertError(exit, r, "invalid_arguments");
            Assert.Contains("width", S(r["message"]));
            (exit, r) = Run("tpms", output, "--second-type", "iwp", "--blend", "0,1", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", output, "--second-type", "pyramid", "--blend", "0,1,0.5", "--compact");
            AssertError(exit, r, "invalid_arguments");
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void VerifySelfIntersectionsOff_IsReportedNotSilent()
        {
            var (exit, r) = Run("tpms", m.Path_("tpms_noverify.obj"), "--divisions", "12", "--verify-self-intersections", "false", "--compact");
            Assert.Equal(0, exit);
            Assert.False(B(r["self_intersection_verified"]));
            Assert.Contains("verify_self_intersections", r["warnings"].ToJsonString());
            (exit, r) = Run("tpms", m.Path_("tpms_verify.obj"), "--divisions", "12", "--compact");
            Assert.True(B(r["self_intersection_verified"]));
        }

        [Fact]
        public void Grade_MatchesTheLibraryAndReportsTheSmallestPeriod()
        {
            string[] a = { "--cell-size", "1", "--divisions", "16", "--box-min", "-1,-1,0", "--box-max", "1,1,4", "--grade", "2,0,1,4,2,1.5" };
            var (exit, r) = Run(new[] { "tpms", m.Path_("tpms_grade.obj") }.Concat(a).Concat(new[] { "--compact" }).ToArray());
            Assert.Equal(0, exit);
            var direct = new TpmsLattice { CellSize = 1, Divisions = 16, Box = new AxisAlignedBox3d(new Vector3d(-1, -1, 0), new Vector3d(1, 1, 4)), Grade = TpmsLattice.LinearGrade(2, 0, 1, 4, 2, 1.5) };
            direct.Compute();
            Assert.Equal(TpmsStatus.Ok, direct.Status);
            Assert.Equal(direct.Mesh.TriangleCount, I(r["mesh"]["triangles"]));
            Assert.Equal(direct.MinLocalCellSize, D(r["min_local_cell_size"]), 9);
            // bad grades
            string output = m.Path_("tpms_grade_bad.obj");
            File.Delete(output);
            (exit, r) = Run("tpms", output, "--grade", "3,0,1,4,2,1", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", output, "--grade", "2,4,1,0,2,1", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", output, "--grade", "2,0,1,4", "--compact");
            AssertError(exit, r, "invalid_arguments");
            Assert.False(File.Exists(output));
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
            (exit, r) = Run("tpms", output, "--origin", "0,nan,0", "--compact");
            AssertError(exit, r, "invalid_arguments");
            Assert.Contains("finite", S(r["message"]));
            (exit, r) = Run("tpms", output, "--origin", "0,x,0", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", output, "--max-cells", "400000001", "--compact");
            AssertError(exit, r, "invalid_arguments");
            Assert.Contains("max_cells", S(r["message"]));
            (exit, r) = Run("tpms", output, "--max-cells", "0", "--compact");
            AssertError(exit, r, "invalid_arguments");
            (exit, r) = Run("tpms", m.Path_("tpms_bad.xyz"), "--compact");
            AssertError(exit, r, "unknown_format");
            Assert.False(File.Exists(output));
        }
    }
}
