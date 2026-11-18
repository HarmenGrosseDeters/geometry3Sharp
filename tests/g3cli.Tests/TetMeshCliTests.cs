using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary>
    /// tetmesh (MeshTetrahedralization, TetMeshWriter, TetMeshMarkers; docs/design/TET_MESHING.md stage P2): formats, markers, regions, the
    /// target flags (P5(a)), deviation, errors. The file contents are checked in detail by TetMeshExportTests in the library tests; here the
    /// files are only checked for their counts against the report.
    /// </summary>
    public class TetMeshCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public TetMeshCliTests(TestMeshes meshes) { m = meshes; }

        // a coarse wrap keeps the runs short; the defaults are tested in the library
        static readonly string[] Fast = { "--relative-alpha", "10", "--compact" };

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        static int CountLines(string path, string startsWith) => File.ReadAllLines(path).Count(l => l.StartsWith(startsWith));

        [Fact]
        public void Medit_ReportsStatusCountsQualityTargetsAndDeviation()
        {
            string output = m.Path_("tet.mesh");
            var (exit, r) = Run(new[] { "tetmesh", m.Cube, output, "--deviation" }.Concat(Fast).ToArray());
            Assert.Equal(0, exit);
            Assert.Equal("ok", S(r["status"]));
            Assert.Equal("success", S(r["tet_status"]));
            Assert.Equal("success", S(r["wrap_status"]));
            Assert.Equal("medit", S(r["format"]));
            Assert.True(B(r["valid"]));
            Assert.Null(r["not_valid_reason"]);
            Assert.True(D(r["used_alpha"]) > 0 && D(r["used_offset"]) > 0);
            JsonNode c = r["counts"];
            Assert.True(I(c["tets"]) > 100);
            Assert.Equal(1, I(c["regions"]));
            // the file has the reported counts: the line after "Tetrahedra" / "Triangles" / "Vertices"
            string[] lines = File.ReadAllLines(output);
            Assert.Equal(I(c["tets"]), int.Parse(lines[Array.IndexOf(lines, "Tetrahedra") + 1]));
            Assert.Equal(I(c["boundary_triangles"]), int.Parse(lines[Array.IndexOf(lines, "Triangles") + 1]));
            Assert.Equal(I(c["vertices"]), int.Parse(lines[Array.IndexOf(lines, "Vertices") + 1]));
            Assert.Equal(I(c["boundary_triangles"]), I(r["boundary_mesh"]["triangles"]));
            Assert.True(B(r["boundary_mesh"]["closed"]));
            Assert.True(B(r["boundary_mesh"]["clean"]));
            // quality and targets (P5(a)): checked on the output
            JsonNode t = r["targets"];
            Assert.Equal(10, D(t["min_dihedral_degrees"]));
            Assert.Equal(2, D(t["max_radius_edge"]));
            Assert.Equal(D(r["quality"]["min_dihedral_degrees"]["worst"]), D(t["worst_min_dihedral_degrees"]));
            Assert.Equal(D(r["quality"]["radius_edge"]["worst"]), D(t["worst_radius_edge"]));
            Assert.Equal(I(r["quality"]["tets_below_10_degrees"]), I(t["tets_below_min_dihedral"]));
            Assert.Equal(I(r["quality"]["tets_radius_edge_above_2"]), I(t["tets_above_max_radius_edge"]));
            Assert.Equal(I(t["tets_below_min_dihedral"]) == 0, B(t["min_dihedral_met"]));
            Assert.Equal(I(t["tets_above_max_radius_edge"]) == 0, B(t["radius_edge_met"]));
            Assert.Equal(B(t["min_dihedral_met"]) && B(t["radius_edge_met"]), B(t["met"]));
            // markers and deviation: the cube has 12 triangles, the boundary is the offset wrap around it
            Assert.Equal("triangle_id", S(r["markers"]["kind"]));
            Assert.InRange(I(r["markers"]["distinct"]), 1, 12);
            Assert.Equal(1, I(r["markers"]["ref_offset"]));
            double maxDist = D(r["deviation"]["boundary_to_input_max"]);
            Assert.InRange(maxDist, D(r["used_offset"]) * 0.99, D(r["used_alpha"]) * 1.5);
            Assert.True(D(r["deviation"]["boundary_to_input_mean"]) <= maxDist);
            Assert.True(B(r["deviation"]["hausdorff"]["converged"]));
            Assert.True(D(r["deviation"]["hausdorff"]["distance"]) >= D(r["used_offset"]) * 0.99);
            Assert.Equal(0, r["warnings"].AsArray().Count);
            // the steps of the run are reported
            Assert.True(I(r["steps"]["tet_splits"]) > 0);
        }

        [Theory]
        [InlineData("tet.msh", "auto", "msh41", "$MeshFormat\n4.1 0 8\n")]
        [InlineData("tet22.msh", "msh22", "msh22", "$MeshFormat\n2.2 0 8\n")]
        [InlineData("tet41.out", "msh41", "msh41", "$MeshFormat\n4.1 0 8\n")]
        [InlineData("tet.vtk", "auto", "vtk", "# vtk DataFile Version 3.0\n")]
        [InlineData("tet_m.out", "medit", "medit", "MeshVersionFormatted 2\n")]
        public void Format_FollowsTheExtensionOrTheOption(string file, string format, string reported, string header)
        {
            string output = m.Path_(file);
            var (exit, r) = Run(new[] { "tetmesh", m.Cube, output, "--format", format }.Concat(Fast).ToArray());
            Assert.Equal(0, exit);
            Assert.Equal(reported, S(r["format"]));
            string text = File.ReadAllText(output);
            Assert.StartsWith(header, text);
            Assert.DoesNotContain("\r", text);
            if (reported == "vtk")
                Assert.Equal(I(r["counts"]["tets"]) + I(r["counts"]["boundary_triangles"]), int.Parse(text.Split('\n').First(l => l.StartsWith("CELL_TYPES")).Split(' ')[1]));
            else if (reported.StartsWith("msh"))
                Assert.Equal(I(r["counts"]["tets"]) + I(r["counts"]["boundary_triangles"]), int.Parse(reported == "msh22"
                    ? text.Split('\n')[text.Split('\n').ToList().IndexOf("$Elements") + 1]
                    : text.Split('\n')[text.Split('\n').ToList().IndexOf("$Elements") + 1].Split(' ')[1]));
        }

        [Fact]
        public void Markers_AreGroupIds_IfTheInputHasGroups()
        {
            // the cube as an OBJ with two groups: the bottom (two triangles) and the rest
            string input = m.Path_("two_groups.obj");
            File.WriteAllText(input, string.Join("\n", File.ReadAllLines(m.Cube).Where(l => l.StartsWith("v ")).Concat(new[] {
                "g bottom", "f 1 3 2", "f 1 4 3", "g rest", "f 5 6 7", "f 5 7 8", "f 1 2 6", "f 1 6 5", "f 2 3 7", "f 2 7 6", "f 3 4 8", "f 3 8 7", "f 4 1 5", "f 4 5 8" })) + "\n");
            var (exit, r) = Run(new[] { "tetmesh", input, m.Path_("grouped.mesh") }.Concat(Fast).ToArray());
            Assert.Equal(0, exit);
            Assert.Equal("group_id", S(r["markers"]["kind"]));
            Assert.Equal(2, I(r["markers"]["distinct"]));
        }

        [Fact]
        public void MarkersOff_WritesZeroRefs_AndNoRegions()
        {
            string output = m.Path_("nomarkers.mesh");
            var (exit, r) = Run(new[] { "tetmesh", m.Cube, output, "--no-markers" }.Concat(Fast).ToArray());
            Assert.Equal(0, exit);
            Assert.Null(r["markers"]);
            Assert.Null(r["counts"]["regions"]);
            Assert.Null(r["deviation"]["boundary_to_input_max"]);
            string[] lines = File.ReadAllLines(output);
            Assert.All(lines.Skip(Array.IndexOf(lines, "Tetrahedra") + 2).TakeWhile(l => l != "End"), l => Assert.EndsWith(" 0", l));
            Assert.All(lines.Skip(Array.IndexOf(lines, "Triangles") + 2).TakeWhile(l => l != "Tetrahedra"), l => Assert.EndsWith(" 0", l));
        }

        [Fact]
        public void Targets_FollowTheOptions()
        {
            var (e1, off) = Run(new[] { "tetmesh", m.Cube, m.Path_("t_off.mesh"), "--min-dihedral", "0", "--max-radius-edge", "1000" }.Concat(Fast).ToArray());
            Assert.Equal(0, e1);
            Assert.True(B(off["targets"]["min_dihedral_met"]));                         // a target that is off is met
            Assert.True(B(off["targets"]["radius_edge_met"]));
            Assert.Equal(0, D(off["targets"]["min_dihedral_degrees"]));
            Assert.Equal(1000, D(off["targets"]["max_radius_edge"]));
            Assert.True(B(off["targets"]["met"]));
            // a demanding radius-edge target stays false where tets at the fixed surface cannot be refined: the flag is measured, not assumed
            var (e2, tight) = Run(new[] { "tetmesh", m.Cube, m.Path_("t_tight.mesh"), "--max-radius-edge", "1.5" }.Concat(Fast).ToArray());
            Assert.Equal(0, e2);
            Assert.False(B(tight["targets"]["radius_edge_met"]));
            Assert.False(B(tight["targets"]["met"]));
            Assert.True(I(tight["targets"]["tets_above_max_radius_edge"]) > 0);
        }

        [Fact]
        public void PointLimit_IsAValidMeshWithAWarning()
        {
            string output = m.Path_("limit.mesh");
            var (exit, r) = Run(new[] { "tetmesh", m.Cube, output, "--max-steiner-points", "5" }.Concat(Fast).ToArray());
            Assert.Equal(0, exit);
            Assert.Equal("point_limit_reached", S(r["tet_status"]));
            Assert.True(B(r["valid"]));
            Assert.True(File.Exists(output));
            Assert.Contains(r["warnings"].AsArray(), w => S(w).Contains("max_steiner_points"));
        }

        [Fact]
        public void WrapFailure_FailsWithStatus_AndWritesNothing()
        {
            string point = m.Path_("tet_point.obj");
            File.WriteAllText(point, "v 1 1 1\nv 1 1 1\nv 1 1 1\nf 1 2 3\n");
            string output = m.Path_("tet_point.mesh");
            var (exit, r) = Run("tetmesh", point, output, "--compact");
            Assert.NotEqual(0, exit);
            Assert.Equal("error", S(r["status"]));
            Assert.False(File.Exists(output));
            Assert.NotNull(r["tet_status"]);
            Assert.NotNull(r["wrap_status"]);
        }

        [Fact]
        public void Errors_HaveTheirResultCodes()
        {
            var (e, r) = Run("tetmesh", m.Cube, m.Path_("x.stl"), "--compact"); AssertError(e, r, "unknown_format");
            (e, r) = Run("tetmesh", m.Cube, m.Path_("x.mesh"), "--format", "gmsh", "--compact"); AssertError(e, r, "invalid_arguments");
            (e, r) = Run("tetmesh", m.Path_("missing.obj"), m.Path_("x.mesh"), "--compact"); AssertError(e, r, "read_error");
            (e, r) = Run("tetmesh", m.Garbage, m.Path_("x.mesh"), "--compact"); Assert.NotEqual(0, e); Assert.Equal("error", S(r["status"]));
            (e, r) = Run("tetmesh", m.Empty, m.Path_("x.mesh"), "--compact"); AssertError(e, r, "empty_mesh");
            (e, r) = Run("tetmesh", m.Cube, Path.Combine(m.Dir, "no_such_dir", "x.mesh"), "--compact"); AssertError(e, r, "write_error");
            (e, r) = Run("tetmesh", m.Cube, "--compact"); AssertError(e, r, "invalid_arguments");
            foreach (string[] bad in new[] {
                new[] { "--relative-alpha", "0" }, new[] { "--relative-alpha", "1001" }, new[] { "--relative-offset", "100001" },
                new[] { "--min-dihedral", "-1" }, new[] { "--min-dihedral", "71" }, new[] { "--max-radius-edge", "1.4" }, new[] { "--max-radius-edge", "1001" },
                new[] { "--max-circumradius", "-1" }, new[] { "--max-circumradius", "0.001" }, new[] { "--max-steiner-points", "0" }, new[] { "--max-steiner-points", "2000001" } }) {
                (e, r) = Run(new[] { "tetmesh", m.Cube, m.Path_("lim.mesh"), "--compact" }.Concat(bad).ToArray());
                AssertError(e, r, "invalid_arguments");
                Assert.False(File.Exists(m.Path_("lim.mesh")), string.Join(" ", bad));
            }
        }

        [Fact]
        public void MaxCircumradius_IsAcceptedAndRefines()
        {
            var (e1, plain) = Run(new[] { "tetmesh", m.Cube, m.Path_("c0.mesh") }.Concat(Fast).ToArray());
            var (e2, sized) = Run(new[] { "tetmesh", m.Cube, m.Path_("c1.mesh"), "--max-circumradius", "0.5" }.Concat(Fast).ToArray());
            Assert.Equal(0, e1); Assert.Equal(0, e2);
            Assert.True(I(sized["counts"]["tets"]) > I(plain["counts"]["tets"]));
            Assert.True(B(sized["valid"]));
        }

        [Fact]
        public void Help_ListsTheCommandAndItsArguments()
        {
            var (exit, r) = Run("help", "tetmesh");
            Assert.Equal(0, exit);
            Assert.Equal("tetmesh", S(r["name"]));
            JsonNode a = r["arguments"];
            Assert.True(B(a["input"]["required"]) && B(a["output"]["required"]));
            Assert.Equal(new[] { "auto", "medit", "msh41", "msh22", "vtk" }, a["format"]["enum"].AsArray().Select(x => S(x)).ToArray());
            foreach (string name in new[] { "relative_alpha", "relative_offset", "min_dihedral", "max_radius_edge", "max_circumradius", "max_steiner_points", "improve", "markers", "deviation", "self_intersections" })
                Assert.NotNull(a[name]);
            Assert.Equal("boolean", S(a["markers"]["type"]));
            Assert.Contains("Medit", S(r["description"]));
            Assert.Contains("no reader", S(r["description"]));
            Assert.Contains("tetmesh", Run("help").result["commands"].AsArray().Select(c => S(c["name"])));
        }
    }
}
