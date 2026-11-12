using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> query3d --camera: the rays of a pinhole camera, the distance image (docs/design/CLI_MCP_UPDATE.md section 5.4). The cube is [-1, 1]^3. </summary>
    public class CameraCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public CameraCliTests(TestMeshes meshes) { m = meshes; }

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        const string Front = "-5,0,0;0,0,0;90;16;16";           // looks along +x at the cube from 4 in front of its face; up is +z, so right is -y

        [Fact]
        public void TheCube_IsSeenByTheCentralPixels()
        {
            var (exit, r) = Run("query3d", m.Cube, "--camera", Front, "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(256, I(r["rays"]["count"]));
            Assert.Equal(16, I(r["rays"]["hits"]));                                      // |u| < 1/4 of the half width: 4 x 4 pixels
            var cam = r["camera"];
            Assert.Equal(16, I(cam["width"]));
            Assert.Equal(256, I(cam["pixels"]));
            Assert.False(B(cam["up_replaced"]));
            Assert.Equal(1.0, D(cam["forward"][0]), 12);
            Assert.Equal(-1.0, D(cam["right"][1]), 12);
            Assert.Equal(1.0, D(cam["up"][2]), 12);
        }

        [Fact]
        public void DistanceImage_Pgm_HasTheDistancesOfTheNearestHits()
        {
            string pgm = m.Path_("depth.pgm");
            var (exit, r) = Run("query3d", m.Cube, "--camera", Front, "--depth-output", pgm, "--compact");
            Assert.Equal(0, exit);
            var d = r["depth_image"];
            Assert.Equal(pgm, S(d["file"]));
            Assert.Equal("pgm", S(d["format"]));
            Assert.Equal(16, I(d["hits"]));
            // the nearest pixel: (1/16, 1/16) off the axis at distance 4; the farthest (3/16, 3/16)
            Assert.Equal(4 * Math.Sqrt(1 + 2 * 0.0625 * 0.0625), D(d["t_min"]), 12);
            Assert.Equal(4 * Math.Sqrt(1 + 2 * 0.1875 * 0.1875), D(d["t_max"]), 12);
            var lines = File.ReadAllLines(pgm);
            Assert.Equal("P2", lines[0]);
            Assert.Equal("16 16", lines[2]);
            Assert.Equal("65535", lines[3]);
            var values = lines.Skip(4).SelectMany(l => l.Split(' ')).Select(int.Parse).ToArray();
            Assert.Equal(256, values.Length);
            Assert.Equal(16, values.Count(v => v > 0));
            Assert.Equal(65535, values.Max());                                           // the nearest hit
            Assert.Equal(1, values.Where(v => v > 0).Min());                             // the farthest hit
            Assert.Equal(0, values[0]);                                                  // a corner is a miss
            Assert.Equal(65535, values[7 * 16 + 7]);                                     // the four central pixels are the nearest: brightest
            Assert.Equal(65535, values[8 * 16 + 8]);
            Assert.Equal(1, values[6 * 16 + 6]);                                         // the corners of the hit square (pixels 6..9) are the farthest
            Assert.Equal(1, values[9 * 16 + 9]);
        }

        [Fact]
        public void TheHorizontalFieldOfView_FollowsTheAspectRatio()
        {
            // fov is vertical: a 32 x 8 image is four times as wide as high: 2 columns x 2 rows of the cube face
            var (exit, r) = Run("query3d", m.Cube, "--camera", "-5,0,0;0,0,0;90;32;8", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(4, I(r["rays"]["hits"]));
            var (e2, tall) = Run("query3d", m.Cube, "--camera", "-5,0,0;0,0,0;90;8;32", "--compact");   // 8 wide, 32 high: the width is a quarter of the height, so all 8 columns and 8 rows hit
            Assert.Equal(0, e2);
            Assert.Equal(64, I(tall["rays"]["hits"]));
        }

        [Fact]
        public void DistanceImage_Csv_IsHeightRowsOfWidthColumns_SameAsTheHits()
        {
            string csv = m.Path_("depth.csv");
            var (exit, r) = Run("query3d", m.Cube, "--camera", "-5,0,0;0,0,0;90;8;4", "--depth-output", csv, "--compact");
            Assert.Equal(0, exit);
            var rows = File.ReadAllLines(csv).Select(l => l.Split(',')).ToArray();
            Assert.Equal(4, rows.Length);
            Assert.All(rows, row => Assert.Equal(8, row.Length));
            Assert.Equal(I(r["rays"]["hits"]), rows.SelectMany(x => x).Count(v => v.Length > 0));
            foreach (string v in rows.SelectMany(x => x).Where(v => v.Length > 0))
                Assert.InRange(double.Parse(v, CultureInfo.InvariantCulture), 4.0, 4.5);
        }

        [Fact]
        public void TheImage_IsOrientedUpAndRight()
        {
            // a cube above the axis (z = 3) appears in the top rows; one at y = 3 appears on the left (right is -y when looking along +x with z up)
            string Write(string name, Vector3d at)
            {
                string path = m.Path_(name);
                Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, TestMeshes.CubeMesh(at), WriteOptions.Defaults).code);
                return path;
            }
            string up = Write("cube_up.obj", new Vector3d(0, 0, 3)), side = Write("cube_side.obj", new Vector3d(0, 3, 0));
            var (e1, a) = Run("query3d", up, "--camera", Front, "--output", m.Path_("cam_up.csv"), "--max-results", "0", "--compact");
            Assert.Equal(0, e1);
            var rowsUp = File.ReadAllLines(m.Path_("cam_up.csv")).Skip(1).Select(l => l.Split(',')).Where(c => c[3].Length > 0).ToArray();   // index, pixel_x, pixel_y, triangle, ...
            Assert.NotEmpty(rowsUp);
            Assert.All(rowsUp, c => Assert.InRange(int.Parse(c[2]), 0, 7));                           // pixel_y: the top half (also rays that pass under the front face hit the bottom face)
            var (e2, b) = Run("query3d", side, "--camera", Front, "--output", m.Path_("cam_side.csv"), "--max-results", "0", "--compact");
            Assert.Equal(0, e2);
            var rowsSide = File.ReadAllLines(m.Path_("cam_side.csv")).Skip(1).Select(l => l.Split(',')).Where(c => c[3].Length > 0).ToArray();
            Assert.NotEmpty(rowsSide);
            Assert.All(rowsSide, c => Assert.InRange(int.Parse(c[1]), 0, 7));                         // pixel_x: the left half
        }

        [Fact]
        public void OutputFile_HasThePixelOfEveryRay()
        {
            string csv = m.Path_("cam_all.csv");
            var (exit, r) = Run("query3d", m.Cube, "--camera", "-5,0,0;0,0,0;90;4;3", "--output", csv, "--max-results", "1", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(12, I(r["output_rows"]));
            Assert.Single(r["rays"]["results"].AsArray());
            var rows = File.ReadAllLines(csv).Select(l => l.Split(',')).ToArray();
            Assert.Equal("index", rows[0][0]);
            Assert.Equal("pixel_x", rows[0][1]);
            Assert.Equal("pixel_y", rows[0][2]);
            Assert.Equal(13, rows.Length);
            Assert.Equal(new[] { "5", "1", "1" }, new[] { rows[6][0], rows[6][1], rows[6][2] });      // index 5 = row 1, column 1
        }

        [Fact]
        public void LookingAlongTheUpDirection_ReplacesIt_AndAnExplicitUpTurnsTheImage()
        {
            var (e1, a) = Run("query3d", m.Cube, "--camera", "0,0,-5;0,0,0;30;8;8", "--compact");       // along +z, the default up is +z
            Assert.Equal(0, e1);
            Assert.True(B(a["camera"]["up_replaced"]));
            Assert.Equal(1.0, D(a["camera"]["forward"][2]), 12);
            Assert.True(I(a["rays"]["hits"]) > 0);
            var (e2, b) = Run("query3d", m.Cube, "--camera", Front, "--up", "0,1,0", "--compact");
            Assert.Equal(0, e2);
            Assert.False(B(b["camera"]["up_replaced"]));
            Assert.Equal(1.0, D(b["camera"]["up"][1]), 12);
            Assert.Equal(I(b["rays"]["hits"]), 16);
        }

        [Fact]
        public void ATinyUpVector_IsNormalizedLikeAnyOther()
        {
            var (exit, r) = Run("query3d", m.Cube, "--camera", Front, "--up", "0,0,1e-200", "--compact");
            Assert.Equal(0, exit);
            Assert.False(B(r["camera"]["up_replaced"]));
            Assert.Equal(1.0, D(r["camera"]["up"][2]), 12);
            Assert.Equal(16, I(r["rays"]["hits"]));
        }

        [Fact]
        public void ParallelOrNot_AndPowerOfTwoScales_GiveTheSameImage()
        {
            string a = m.Path_("depth_a.csv"), b = m.Path_("depth_b.csv");
            Assert.Equal(0, Run("query3d", m.Cube, "--camera", "-5,0.3,0.2;0,0,0;70;32;24", "--depth-output", a, "--compact").exit);
            Assert.Equal(0, Run("query3d", m.Cube, "--camera", "-5,0.3,0.2;0,0,0;70;32;24", "--depth-output", b, "--no-parallel", "--compact").exit);
            Assert.Equal(File.ReadAllText(a), File.ReadAllText(b));
            foreach (int k in new[] { -4, 5 }) {
                double s = Math.Pow(2, k);
                DMesh3 cube = StandardMeshReader.ReadMesh(m.Cube);
                MeshTransforms.Scale(cube, s, s, s);
                string scaled = m.Path_($"cube_cam_{k}.obj"), csv = m.Path_($"depth_k{k}.csv");
                Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(scaled, cube, WriteOptions.Defaults).code);
                string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);
                Assert.Equal(0, Run("query3d", scaled, "--camera", $"{N(-5 * s)},{N(0.3 * s)},{N(0.2 * s)};0,0,0;70;32;24", "--depth-output", csv, "--compact").exit);
                var unit = File.ReadAllLines(a).Select(l => l.Split(',')).ToArray();
                var sc = File.ReadAllLines(csv).Select(l => l.Split(',')).ToArray();
                for (int j = 0; j < unit.Length; j++)
                    for (int i = 0; i < unit[j].Length; i++) {
                        Assert.Equal(unit[j][i].Length == 0, sc[j][i].Length == 0);
                        if (unit[j][i].Length > 0)
                            Assert.Equal(double.Parse(unit[j][i], CultureInfo.InvariantCulture) * s, double.Parse(sc[j][i], CultureInfo.InvariantCulture), 9);
                    }
            }
        }

        [Fact]
        public void AnyHit_WorksWithACamera_ButNotWithADepthImage()
        {
            var (e1, r) = Run("query3d", m.Cube, "--camera", Front, "--any-hit", "--compact");
            Assert.Equal(0, e1);
            Assert.Equal(16, I(r["rays"]["hits"]));
            var (e2, r2) = Run("query3d", m.Cube, "--camera", Front, "--any-hit", "--depth-output", m.Path_("x.pgm"), "--compact");
            AssertError(e2, r2, "invalid_arguments");
        }

        [Theory]
        [InlineData("-5,0,0;0,0,0;90;16")]                       // four parts
        [InlineData("-5,0,0;0,0,0;90;16;16;1")]
        [InlineData("-5,0;0,0,0;90;16;16")]
        [InlineData("-5,0,0;-5,0,0;90;16;16")]                   // target = position
        [InlineData("-5,0,0;0,0,0;0;16;16")]                     // fov
        [InlineData("-5,0,0;0,0,0;179;16;16")]
        [InlineData("-5,0,0;0,0,0;NaN;16;16")]
        [InlineData("-5,0,0;0,0,0;90;0;16")]
        [InlineData("-5,0,0;0,0,0;90;16;1.5")]
        [InlineData("-5,0,0;0,0,0;90;20000;2")]
        [InlineData("-5,0,0;0,0,0;90;1001;1000")]                // more than a million pixels
        [InlineData("a,0,0;0,0,0;90;16;16")]
        public void BadCameras_AreInvalidArguments(string camera)
        {
            var (exit, r) = Run("query3d", m.Cube, "--camera", camera, "--compact");
            AssertError(exit, r, "invalid_arguments");
        }

        [Fact]
        public void BadCombinations_AreInvalidArguments()
        {
            AssertError2(Run("query3d", m.Cube, "--camera", Front, "--rays", "0,0,0,1,0,0", "--compact"));
            AssertError2(Run("query3d", m.Cube, "--camera", Front, "--normalize=false", "--compact"));
            AssertError2(Run("query3d", m.Cube, "--rays", "0,0,0,1,0,0", "--up", "0,1,0", "--compact"));
            AssertError2(Run("query3d", m.Cube, "--rays", "0,0,0,1,0,0", "--depth-output", m.Path_("x.pgm"), "--compact"));
            AssertError2(Run("query3d", m.Cube, "--camera", Front, "--up", "0,0,0", "--compact"));
            AssertError2(Run("query3d", m.Cube, "--camera", Front, "--up", "1,2", "--compact"));
            AssertError2(Run("query3d", m.Cube, "--camera", Front, "--points", "0,0,0", "--inside", "auto", "--output", m.Path_("x.csv"), "--compact"));
            var (e, r) = Run("query3d", m.Cube, "--camera", Front, "--depth-output", m.Path_("x.png"), "--compact");
            AssertError(e, r, "unknown_format");
            var (e2, r2) = Run("query3d", m.Cube, "--camera", Front, "--depth-output", Path.Combine(m.Path_("no_such_dir"), "d.pgm"), "--compact");
            AssertError(e2, r2, "write_error");
            var (e3, r3) = Run("query3d", m.Cube, "--compact");
            AssertError(e3, r3, "invalid_arguments");
            // nothing is written when the depth image cannot be: the per-ray file does not exist either
            string o2 = m.Path_("cam_o2.csv");
            var (e4, r4) = Run("query3d", m.Cube, "--camera", Front, "--output", o2, "--depth-output", Path.Combine(m.Path_("no_such_dir"), "x.pgm"), "--compact");
            AssertError(e4, r4, "write_error");
            Assert.False(File.Exists(o2));
            var (e5, r5) = Run("query3d", m.Cube, "--camera", Front, "--output", m.Path_("same.csv"), "--depth-output", m.Path_("same.csv"), "--compact");
            AssertError(e5, r5, "invalid_arguments");
        }

        static void AssertError2((int exit, JsonObject r) x) => AssertError(x.exit, x.r, "invalid_arguments");
    }
}
