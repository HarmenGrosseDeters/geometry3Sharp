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
    /// <summary> The additive result fields of boolean / resolve / repair (statuses, contacts, counters, scale codes) and of mesh2d. </summary>
    public class ResultFieldsCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public ResultFieldsCliTests(TestMeshes meshes) { m = meshes; }

        [Fact]
        public void Boolean_ReportsStatusCountsAndContacts()
        {
            var (exit, r) = Run("boolean", m.Cube, m.Shifted, m.Path_("u.stl"), "--op", "union", "--compact");
            Assert.Equal(0, exit);
            var b = r["boolean"];
            Assert.Equal("success", S(b["status"]));
            Assert.Equal(0, I(b["contacts"]));
            Assert.Equal(0, I(b["reoriented_triangles"]));
            Assert.True(I(b["counts"]["intersecting_pairs"]) > 0);
            Assert.Equal(I(b["intersecting_pairs"]), I(b["counts"]["intersecting_pairs"]));
            Assert.True(I(b["counts"]["points"]) > 0);
            Assert.True(I(b["counts"]["pieces"]) > 0);
            Assert.Equal(I(b["contacts"]), I(b["counts"]["contacts"]));
            Assert.Equal(0, I(b["rounding"]["initial_false_contacts"]));
            Assert.Equal(0, I(b["rounding"]["remaining_false_contacts"]));
            Assert.Equal(0, I(b["rounding"]["vertex_removals"]));
            Assert.Null(b["rounding"]["smallest_rejected_drift"]);              // no edit was rejected
            Assert.Null(b["rounding"]["remaining_defect_at"]);
        }

        [Fact]
        public void Boolean_SolidsThatTouchAtAPoint_CountTheContact()
        {
            string corner = m.Path_("corner.stl");
            var cube = TestMeshes.CubeMesh(new Vector3d(2, 2, 2));          // touches the cube [-1,1]^3 at (1,1,1) only
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(corner, cube, WriteOptions.Defaults).code);
            var (exit, r) = Run("boolean", m.Cube, corner, m.Path_("touch.3mf"), "--op", "union", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(1, I(r["boolean"]["contacts"]));                      // the shared corner
            Assert.Equal(1, I(r["boolean"]["counts"]["contacts"]));
        }

        [Fact]
        public void Boolean_ReorientedTriangles_AreCounted()
        {
            // the sphere with every 20th triangle reversed is closed but not consistently oriented: the exact operation reorients them
            var (exit, r) = Run("resolve", m.Flipped, m.Path_("resolved_flipped.ply"), "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(39, I(r["boolean"]["reoriented_triangles"]));          // 768 triangles, every 20th reversed
        }

        [Fact]
        public void Boolean_Failure_CarriesTheStatusAndTheLibraryText()
        {
            var (exit, r) = Run("boolean", m.Open, m.Cube, m.Path_("f.stl"), "--op", "union", "--allow-open=false", "--compact");
            Assert.Equal(ResultCodes.ExitCode("operation_failed"), exit);
            Assert.Equal("open_input", S(r["boolean_status"]));
            Assert.False(string.IsNullOrEmpty(S(r["boolean_error"])));
            Assert.Equal("open_input", S(r["boolean"]["status"]));
            Assert.False(File.Exists(m.Path_("f.stl")));
        }

        [Fact]
        public void Repair_ScaleDisparity_HasItsCodeInTheErrorResult()
        {
            // two overlapping cubes of size 2e-3 and a third one 1e5 away: degenerate triangles only by the diagonal of all of them
            DMesh3 a = TestMeshes.CubeMesh(Vector3d.Zero);
            var editor = new MeshEditor(a);
            editor.AppendMesh(TestMeshes.CubeMesh(new Vector3d(1, 0, 0)));
            editor.AppendMesh(TestMeshes.CubeMesh(new Vector3d(1e8, 0, 0)));
            MeshTransforms.Scale(a, 1e-3, 1e-3, 1e-3);
            string input = m.Path_("disparity.obj"), output = m.Path_("disparity_out.obj");
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(input, a, WriteOptions.Defaults).code);
            var (exit, r) = Run("repair", input, output, "--compact");
            Assert.Equal(ResultCodes.ExitCode("operation_failed"), exit);
            Assert.Equal("scale_disparity", S(r["result_code"]));
            Assert.Equal("scale_disparity", S(r["exact_resolve_code"]));
            Assert.StartsWith("scale disparity", S(r["result_reason"]));
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void Repair_ResultCodes_AreNullForOrdinaryRepairs()
        {
            var (exit, r) = Run("repair", m.Overlapping, m.Path_("rep.stl"), "--compact");
            Assert.Equal(0, exit);
            Assert.Null(r["result_code"]);
            Assert.Null(r["exact_resolve_code"]);
        }

        // ---- repair: the options and result fields added with the CLI / MCP update (docs/design/CLI_MCP_UPDATE.md, PR A) ----

        [Fact]
        public void Repair_NewOptions_ReachTheLibraryAndAreReported()
        {
            var (e0, d) = Run("repair", m.Overlapping, m.Path_("rep_defaults.obj"), "--compact");
            Assert.Equal(0, e0);
            var o = d["robust_options"];                                        // the library's defaults
            Assert.Equal(4, I(o["exact_resolve_rounds"]));
            Assert.Equal(300.0, D(o["wrap_relative_alpha"]));
            Assert.Equal(3000.0, D(o["wrap_relative_offset"]));
            Assert.False(B(o["alpha_wrap_quality_pass"]));
            Assert.True(B(o["passthrough_clean_parts"]));
            Assert.Equal(1e-4, D(o["separate_fraction"]));

            var (exit, r) = Run("repair", m.Overlapping, m.Path_("rep_opts.obj"), "--compact", "--exact-resolve-rounds", "2",
                "--wrap-relative-alpha", "150", "--wrap-relative-offset", "1500", "--alpha-wrap-quality-pass",
                "--no-passthrough-clean-parts", "--separate-fraction", "0.001");
            Assert.Equal(0, exit);
            o = r["robust_options"];
            Assert.Equal(2, I(o["exact_resolve_rounds"]));
            Assert.Equal(150.0, D(o["wrap_relative_alpha"]));
            Assert.Equal(1500.0, D(o["wrap_relative_offset"]));
            Assert.True(B(o["alpha_wrap_quality_pass"]));
            Assert.False(B(o["passthrough_clean_parts"]));
            Assert.Equal(1e-3, D(o["separate_fraction"]));
            Assert.InRange(I(r["exact_resolve_rounds_used"]), 1, 2);
        }

        [Fact]
        public void Repair_NewResultFields_AreReported()
        {
            var (exit, r) = Run("repair", m.Overlapping, m.Path_("rep_fields.obj"), "--compact");
            Assert.Equal(0, exit);
            Assert.True(I(r["exact_resolve_rounds_used"]) >= 1);               // the exact stage ran
            Assert.Equal(0, I(r["passed_through_triangles"]));
            Assert.False(B(r["alpha_wrap_quality_improved"]));
            Assert.Null(r["alpha_wrap_min_angle"]);                            // the result is not an alpha wrap: NaN is null
            Assert.Null(r["alpha_wrap_max_angle"]);
            Assert.Null(r["alpha_wrap_share_above_170"]);
            var phases = r["phase_milliseconds"].AsObject();
            Assert.True(phases.ContainsKey("repair"));
            Assert.True(phases.ContainsKey("exact"));
            Assert.All(phases, kv => Assert.True(kv.Value.GetValue<long>() >= 0));
        }

        [Fact]
        public void Repair_PassThroughCleanParts_PassesAFarCleanCubeThrough()
        {
            // two overlapping cubes and a clean cube 10 away: the far cube is passed through untouched (12 triangles)
            DMesh3 mesh = TestMeshes.CubeMesh(Vector3d.Zero);
            var editor = new MeshEditor(mesh);
            editor.AppendMesh(TestMeshes.CubeMesh(new Vector3d(1, 0, 0)));
            editor.AppendMesh(TestMeshes.CubeMesh(new Vector3d(10, 0, 0)));
            string input = m.Path_("three_cubes.obj");
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(input, mesh, WriteOptions.Defaults).code);
            var (e1, on) = Run("repair", input, m.Path_("three_on.obj"), "--compact");
            Assert.Equal(0, e1);
            Assert.Equal(12, I(on["passed_through_triangles"]));
            var (e2, off) = Run("repair", input, m.Path_("three_off.obj"), "--compact", "--no-passthrough-clean-parts");
            Assert.Equal(0, e2);
            Assert.Equal(0, I(off["passed_through_triangles"]));
            Assert.True(B(on["mesh"]["clean"]));
            Assert.True(B(off["mesh"]["clean"]));
        }

        [Fact]
        public void Repair_NewOptions_AreValidated()
        {
            string[][] bad = {
                new[] { "--exact-resolve-rounds", "0" }, new[] { "--exact-resolve-rounds", "65" },
                new[] { "--wrap-relative-alpha", "0" }, new[] { "--wrap-relative-alpha", "-3" },
                new[] { "--wrap-relative-offset", "0" }, new[] { "--wrap-relative-offset", "-3" },
                new[] { "--separate-fraction", "0" }, new[] { "--separate-fraction", "1" },
            };
            foreach (var a in bad) {
                string output = m.Path_("rep_bad.obj");
                var (exit, r) = Run(new[] { "repair", m.Overlapping, output, "--compact" }.Concat(a).ToArray());
                Assert.Equal(ResultCodes.ExitCode("invalid_arguments"), exit);
                Assert.Equal("invalid_arguments", S(r["code"]));
                Assert.False(File.Exists(output));
            }
        }

        [Fact]
        public void Repair_ReasonCodes_MapEveryScaleReason()
        {
            Assert.Null(Commands.ReasonCode(null));
            Assert.Null(Commands.ReasonCode(""));
            Assert.Null(Commands.ReasonCode("some other reason"));
            Assert.Equal("scale_not_finite", S(Commands.ReasonCode(MeshRobustRepair.ScaleNotFinite)));
            Assert.Equal("scale_disparity", S(Commands.ReasonCode(MeshRobustRepair.ScaleDisparity)));
            Assert.Equal("outside_range", S(Commands.ReasonCode(MeshRobustRepair.OutsideRange)));
            Assert.Equal("lost_far_part", S(Commands.ReasonCode(MeshRobustRepair.LostFarPart)));
            Assert.Equal("wrap_too_coarse", S(Commands.ReasonCode(MeshRobustRepair.WrapTooCoarse)));
            // (the library appends text to a reason)
            Assert.Equal("lost_far_part", S(Commands.ReasonCode("x; " + MeshRobustRepair.LostFarPart)));
        }

        [Fact]
        public void Repair_OutsideRange_HasItsCodeInTheErrorResult()
        {
            // a coordinate beyond 2^400 is not repaired
            DMesh3 big = TestMeshes.CubeMesh(Vector3d.Zero);
            MeshTransforms.Scale(big, 1e150, 1e150, 1e150);
            string input = m.Path_("huge.obj"), output = m.Path_("huge_out.obj");
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(input, big, WriteOptions.Defaults).code);
            var (exit, r) = Run("repair", input, output, "--compact");
            Assert.Equal(ResultCodes.ExitCode("operation_failed"), exit);
            Assert.Equal("outside_range", S(r["result_code"]));
            Assert.False(File.Exists(output));
        }

        [Fact]
        public void Mesh2d_ReportsTheEffectiveOptionsAndTheBadTriangles()
        {
            const string geometry = "{\"points\": [[0,0],[10,0],[10,10],[0,10]]}";
            var (e1, plain) = Run("mesh2d", "--geometry", geometry, "--output", m.Path_("q1.obj"), "--min-angle", "25", "--compact");
            Assert.Equal(0, e1);
            Assert.False(B(plain["conforming"]));
            Assert.True(B(plain["resolve_crossings"]));
            Assert.Null(plain["steiner_limit"]);                                // not given: the library's default applies
            Assert.Empty(plain["bad_triangle_ids"].AsArray());
            var (e2, conf) = Run("mesh2d", "--geometry", geometry, "--output", m.Path_("q2.obj"), "--conforming", "--steiner-limit", "500", "--no-resolve-crossings", "--compact");
            Assert.Equal(0, e2);
            Assert.True(B(conf["conforming"]));
            Assert.False(B(conf["resolve_crossings"]));
            Assert.Equal(500, I(conf["steiner_limit"]));
            var (e3, cdt) = Run("mesh2d", "--geometry", geometry, "--output", m.Path_("q3.obj"), "--no-quality", "--compact");
            Assert.Equal(0, e3);
            Assert.False(B(cdt["conforming"]));
            Assert.Null(cdt["steiner_limit"]);
        }

        [Fact]
        public void Mesh2d_BadTriangles_AreListedByTheirIdInTheWrittenMesh()
        {
            // a 10 degree corner cannot meet a 30 degree bound: the triangles at it are bad
            const string geometry = "{\"points\": [[0,0],[10,0],[9.8481,1.7365]], \"rings\": [[0,1,2]]}";
            string output = m.Path_("bad.obj");
            var (exit, r) = Run("mesh2d", "--geometry", geometry, "--output", output, "--min-angle", "30", "--compact");
            Assert.Equal(0, exit);
            int bad = I(r["bad_triangles"]);
            Assert.True(bad > 0);
            var ids = r["bad_triangle_ids"].AsArray().Select(I).ToArray();
            Assert.Equal(Math.Min(bad, 100), ids.Length);
            DMesh3 mesh = StandardMeshReader.ReadMesh(output);
            foreach (int t in ids) {
                Assert.True(mesh.IsTriangle(t));
                Vector3d a = mesh.GetVertex(mesh.GetTriangle(t).a), b = mesh.GetVertex(mesh.GetTriangle(t).b), c = mesh.GetVertex(mesh.GetTriangle(t).c);
                double min = Math.Min(Math.Min(Vector3d.AngleD(b - a, c - a), Vector3d.AngleD(a - b, c - b)), Vector3d.AngleD(a - c, b - c));
                Assert.True(min < 30 - 1e-6, $"triangle {t} has min angle {min}");
            }
            Assert.True(I(r["diagnostics_count"]) >= 0);
        }
    }
}
