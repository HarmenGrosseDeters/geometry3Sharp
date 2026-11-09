using System;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using g3;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> deviation (MeshDeviation), thickness (MeshWallThickness) and fit (PrimitiveFit3), on generated shapes with known answers. </summary>
    public class ShapeAnalysisCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public ShapeAnalysisCliTests(TestMeshes meshes) { m = meshes; }

        static void AssertError(int exit, JsonObject r, string code)
        {
            Assert.Equal("error", S(r["status"]));
            Assert.Equal(code, S(r["code"]));
            Assert.Equal(ResultCodes.ExitCode(code), exit);
        }

        string Save(string name, DMesh3 mesh)
        {
            string path = m.Path_(name);
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, mesh, WriteOptions.Defaults).code);
            return path;
        }

        string Scaled(string name, string from, double x, double y, double z)
        {
            DMesh3 mesh = StandardMeshReader.ReadMesh(from);
            MeshTransforms.Scale(mesh, x, y, z);
            return Save(name, mesh);
        }


        // ---- deviation ----

        [Fact]
        public void Deviation_OfASphereScaledBy1_1_IsAboutPlusAndMinusOneTenth()
        {
            string big = Scaled("sphere_big.obj", m.Sphere, 1.1, 1.1, 1.1);
            var (e1, outside) = Run("deviation", big, m.Sphere, "--compact");
            Assert.Equal(0, e1);
            Assert.Equal(0.1, D(outside["vertex_statistics"]["mean"]), 2);     // positive: outside the reference's solid
            Assert.Equal(0.1, D(outside["vertex_statistics"]["max"]), 2);
            Assert.Equal("pseudo_normal", S(outside["sign"]));
            Assert.Null(outside["hausdorff_upper_bound"]);

            var (e2, inside) = Run("deviation", m.Sphere, big, "--compact");
            Assert.Equal(0, e2);
            Assert.Equal(-0.1, D(inside["vertex_statistics"]["mean"]), 2);      // negative: inside the reference

            var (e3, unsigned) = Run("deviation", m.Sphere, big, "--sign", "unsigned", "--compact");
            Assert.Equal(0, e3);
            Assert.Equal(0.1, D(unsigned["vertex_statistics"]["mean"]), 2);
            Assert.True(D(unsigned["vertex_statistics"]["min"]) > 0.05);

            var (e4, winding) = Run("deviation", big, m.Sphere, "--sign", "winding_number", "--compact");
            Assert.Equal(0, e4);
            Assert.Equal(0.1, D(winding["vertex_statistics"]["mean"]), 2);
        }

        [Fact]
        public void Deviation_Certified_BoundsTheHausdorffDistance()
        {
            string big = Scaled("sphere_big2.obj", m.Sphere, 1.1, 1.1, 1.1);
            var (exit, r) = Run("deviation", big, m.Sphere, "--certified", "--certified-tolerance", "1e-4", "--compact");
            Assert.Equal(0, exit);
            double lo = D(r["hausdorff_lower_bound"]), hi = D(r["hausdorff_upper_bound"]);
            Assert.True(lo <= hi);
            Assert.InRange(hi - lo, 0, 1.01e-4);
            Assert.InRange(lo, 0.09, 0.101);   // the farthest points of the faceted big sphere are its vertices at 0.1 from the reference
        }

        [Fact]
        public void Deviation_OfAMeshWithItself_IsZero()
        {
            var (exit, r) = Run("deviation", m.Cube, m.Cube, "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(0.0, D(r["statistics"]["max_absolute"]), 9);
            Assert.Equal(8, I(r["vertex_statistics"]["count"]));
        }

        [Theory]
        [InlineData("--sign", "sideways")]
        [InlineData("--certified-tolerance", "-1")]
        public void Deviation_BadOptions_AreInvalidArguments(string name, string value)
        {
            var (exit, r) = Run("deviation", m.Cube, m.Cube, name, value);
            AssertError(exit, r, "invalid_arguments");
            var (exit2, r2) = Run("deviation", m.Path_("missing.obj"), m.Cube, name, value);
            AssertError(exit2, r2, "invalid_arguments");
        }

        [Fact]
        public void Deviation_BadInputs_AreResultCodes()
        {
            var (e1, r1) = Run("deviation", m.Garbage, m.Cube); AssertError(e1, r1, "read_error");
            var (e2, r2) = Run("deviation", m.Cube, m.Empty); AssertError(e2, r2, "empty_mesh");
        }


        // ---- thickness ----

        [Fact]
        public void Thickness_OfAPlate_Ray_IsTheDistanceAcrossTheWall()
        {
            string plate = Scaled("plate.obj", m.Cube, 1, 1, 0.1);   // 2 x 2 x 0.2
            var (exit, r) = Run("thickness", plate, "--method", "ray", "--min-thickness", "0.5", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal("ray", S(r["method"]));
            Assert.Equal(0.2, D(r["statistics"]["min"]), 6);
            Assert.Equal(2.0, D(r["statistics"]["max"]), 6);
            Assert.Equal(0, I(r["triangles_without_value"]));
            Assert.Equal(4, I(r["thin_triangles"]));                           // top and bottom: 2 triangles each
            Assert.Equal(8.0 / 9.6, D(r["thin_area_share"]), 6);
            Assert.Equal(8, I(r["thin_vertices"]));
            Assert.Equal(4, r["thin_triangle_ids"].AsArray().Count);
            Assert.False(B(r["thin_triangle_ids_truncated"]));

            var (e2, limited) = Run("thickness", plate, "--method", "ray", "--min-thickness", "0.5", "--max-results", "1", "--compact");
            Assert.Equal(0, e2);
            Assert.Single(limited["thin_triangle_ids"].AsArray());
            Assert.True(B(limited["thin_triangle_ids_truncated"]));

            var (e3, none) = Run("thickness", plate, "--method", "ray", "--min-thickness", "0.1", "--compact");
            Assert.Equal(0, e3);
            Assert.Equal(0, I(none["thin_triangles"]));
        }

        [Fact]
        public void Thickness_MaxDistance_CountsLongerRaysAsMissing()
        {
            string plate = Scaled("plate_md.obj", m.Cube, 1, 1, 0.1);
            var (exit, r) = Run("thickness", plate, "--method", "ray", "--max-distance", "0.5", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(8, I(r["triangles_without_value"])); // the 8 side triangles see a wall 2 away
            Assert.Equal(0.2, D(r["statistics"]["max"]), 6);
        }

        [Theory]
        [InlineData("shape_diameter")]
        [InlineData("inscribed_sphere")]
        public void Thickness_OtherMethods_FindThePlateThinnerThanTheCube(string method)
        {
            string plate = Scaled("plate_" + method + ".obj", m.Cube, 1, 1, 0.1);
            var (e1, thin) = Run("thickness", plate, "--method", method, "--compact");
            var (e2, thick) = Run("thickness", m.Cube, "--method", method, "--compact");
            Assert.Equal(0, e1); Assert.Equal(0, e2);
            Assert.True(D(thin["statistics"]["median"]) < D(thick["statistics"]["median"]));
            Assert.True(D(thin["statistics"]["median"]) < 0.5);
        }

        [Fact]
        public void Thickness_ScalesWithTheMesh()
        {
            string big = Scaled("cube_x10.obj", m.Cube, 10, 10, 10);
            var (_, a) = Run("thickness", m.Cube, "--method", "ray", "--compact");
            var (_, b) = Run("thickness", big, "--method", "ray", "--compact");
            Assert.Equal(10 * D(a["statistics"]["mean"]), D(b["statistics"]["mean"]), 6);
        }

        [Theory]
        [InlineData("--method", "x")]
        [InlineData("--cone-angle", "0")]
        [InlineData("--ray-count", "0")]
        [InlineData("--max-distance", "-1")]
        [InlineData("--min-thickness", "-1")]
        [InlineData("--max-results", "-1")]
        public void Thickness_BadOptions_AreInvalidArguments(string name, string value)
        {
            var (exit, r) = Run("thickness", m.Path_("missing.obj"), name, value);
            AssertError(exit, r, "invalid_arguments");
        }

        [Fact]
        public void Thickness_BadInputs_AreResultCodes()
        {
            var (e1, r1) = Run("thickness", m.Garbage); AssertError(e1, r1, "read_error");
            var (e2, r2) = Run("thickness", m.Empty); AssertError(e2, r2, "empty_mesh");
        }


        // ---- fit ----

        string Gen(string name, MeshGenerator g) => Save(name, g.Generate().MakeDMesh());

        [Fact]
        public void Fit_Sphere_FindsCenterAndRadius()
        {
            string s = Scaled("sphere_r2.obj", m.Sphere, 2, 2, 2);
            var (exit, r) = Run("fit", s, "--primitive", "sphere", "--compact");
            Assert.Equal(0, exit);
            Assert.Equal("sphere", S(r["primitive"]));
            Assert.Equal("success", S(r["fit_result"]));
            Assert.Equal(2.0, D(r["sphere"]["radius"]), 3);
            Assert.All(r["sphere"]["center"].AsArray(), v => Assert.Equal(0.0, D(v), 3));
            Assert.Equal(I(r["points"]), I(r["inliers"]));
            Assert.True(D(r["rms_error"]) < 1e-3);
        }

        [Fact]
        public void Fit_Plane_Cylinder_Cone()
        {
            string plane = Gen("plane.obj", new GriddedRectGenerator() { Width = 4, Height = 3, EdgeVertices = 6 });
            var (e1, p) = Run("fit", plane, "--primitive", "plane", "--compact");
            Assert.Equal(0, e1);
            Assert.Equal(1.0, Math.Abs(D(p["plane"]["normal"][1])), 6);        // the generator's normal is the y axis
            Assert.True(D(p["max_error"]) < 1e-6);

            string cyl = Gen("cyl.obj", new OpenCylinderGenerator() { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 2, Slices = 32 });
            var (e2, c) = Run("fit", cyl, "--primitive", "cylinder", "--compact");
            Assert.Equal(0, e2);
            Assert.Equal(0.5, D(c["cylinder"]["radius"]), 3);
            Assert.Equal(2.0, D(c["cylinder"]["height"]), 3);

            // a cone without base (the generator's cap would add vertices off the lateral surface): apex at z = 2, base radius 1 at z = 0
            var obj = new System.Text.StringBuilder("v 0 0 2\n");
            const int n = 48;
            for (int i = 0; i < n; i++)
                obj.Append(FormattableString.Invariant($"v {Math.Cos(2 * Math.PI * i / n)} {Math.Sin(2 * Math.PI * i / n)} 0\n"));
            for (int i = 0; i < n; i++)
                obj.Append($"f 1 {2 + i} {2 + (i + 1) % n}\n");
            string cone = m.Path_("cone.obj");
            System.IO.File.WriteAllText(cone, obj.ToString());
            var (e3, k) = Run("fit", cone, "--primitive", "cone", "--compact");
            Assert.Equal(0, e3);
            Assert.Equal(Math.Atan(0.5) * 180 / Math.PI, D(k["cone"]["half_angle_degrees"]), 1);
            Assert.Equal(2.0, D(k["cone"]["apex"][2]), 1);
        }

        [Fact]
        public void Fit_All_ReportsEveryPrimitive_AndTheSphereFitsBest()
        {
            var (exit, r) = Run("fit", m.Sphere, "--primitive", "all", "--compact");
            Assert.Equal(0, exit);
            var fits = r["fits"].AsArray();
            Assert.Equal(new[] { "plane", "sphere", "cylinder", "cone" }, fits.Select(f => S(f["primitive"])).ToArray());
            var sphere = fits.First(f => S(f["primitive"]) == "sphere");
            Assert.Equal(1.0, D(sphere["inlier_share"]), 6);
            Assert.True(fits.Where(f => S(f["primitive"]) != "sphere" && S(f["fit_result"]) == "success")
                .All(f => D(f["inlier_share"]) < 1.0));
        }

        [Fact]
        public void Fit_IsDeterministic_AndSeedOptionIsWired()
        {
            var a = Run("fit", m.Sphere, "--primitive", "sphere", "--seed", "3", "--compact").result;
            var b = Run("fit", m.Sphere, "--primitive", "sphere", "--seed", "3", "--compact").result;
            Assert.Equal(a["sphere"].ToJsonString(), b["sphere"].ToJsonString());
            Assert.Equal(I(a["iterations"]), I(b["iterations"]));
            var (e, c) = Run("fit", m.Sphere, "--primitive", "sphere", "--max-iterations", "1", "--compact");
            Assert.Equal(0, e);
            Assert.Equal(1, I(c["iterations"]));
        }

        [Fact]
        public void Fit_NoModel_IsOperationFailedWithTheFitResult()
        {
            string tri = m.Path_("tri.obj");
            System.IO.File.WriteAllText(tri, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            var (exit, r) = Run("fit", tri, "--primitive", "sphere", "--compact");
            AssertError(exit, r, "operation_failed");
            Assert.Equal("too_few_points", S(r["fit_result"]));
            // a sphere needs a radius below the limit: a tiny max_radius rejects every candidate
            var (e2, r2) = Run("fit", m.Sphere, "--primitive", "sphere", "--max-radius", "0.01", "--compact");
            AssertError(e2, r2, "operation_failed");
            Assert.Equal("rejected_by_limits", S(r2["fit_result"]));
        }

        [Theory]
        [InlineData("--primitive", "torus")]
        [InlineData("--inlier-distance", "-1")]
        [InlineData("--max-radius", "-1")]
        [InlineData("--max-normal-angle", "0")]
        [InlineData("--max-iterations", "0")]
        public void Fit_BadOptions_AreInvalidArguments(string name, string value)
        {
            var (exit, r) = Run("fit", m.Path_("missing.obj"), name, value);
            AssertError(exit, r, "invalid_arguments");
        }

        [Fact]
        public void Fit_BadInputs_AreResultCodes()
        {
            var (e1, r1) = Run("fit", m.Garbage); AssertError(e1, r1, "read_error");
            var (e2, r2) = Run("fit", m.Empty); AssertError(e2, r2, "empty_mesh");
        }
    }
}
