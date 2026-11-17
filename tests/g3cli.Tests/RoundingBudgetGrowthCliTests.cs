using System.Text.Json.Nodes;
using Xunit;
using g3;
using gs;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary> --rounding-defect-scale and --rounding-max-growth: parsing, validation and pass-through to the library (#1048). </summary>
    public class RoundingBudgetGrowthCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public RoundingBudgetGrowthCliTests(TestMeshes meshes) { m = meshes; }

        // two nearly coincident spheres (318 initial defects, 19076 pair tests to finish), as SafeRoundingBudgetTests
        string[] Spheres(string tag)
        {
            DMesh3 a = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh(), b = new DMesh3(a);
            MeshTransforms.Rotate(b, Vector3d.Zero, new Quaterniond(new Vector3d(0.6, 0.3, 0.1).Normalized, 1e-10 * MathUtil.Rad2Deg));
            MeshTransforms.Translate(b, 1e-10 * new Vector3d(0.2, 0.9, 0.5));
            string pa = m.Path_(tag + "_a.g3mesh"), pb = m.Path_(tag + "_b.g3mesh");
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(pa, a, WriteOptions.Defaults).code);
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(pb, b, WriteOptions.Defaults).code);
            return new[] { pa, pb };
        }

        [Fact]
        public void Boolean_Options_ReachTheLibraryAndChangeTheBudget()
        {
            var s = Spheres("bool");
            var (exit, r) = Run("boolean", s[0], s[1], m.Path_("o0.stl"), "--op", "union");
            Assert.Equal(0, exit);
            Assert.Equal(50000.0, D(r["boolean"]["rounding_defect_scale"]));          // the defaults, unchanged
            Assert.Equal(4.0, D(r["boolean"]["rounding_max_growth"]));
            Assert.True(B(r["boolean"]["rounding_clean"]));

            // a budget of 8000 without growth stops at the limit; the growth of the defaults is too small at this scale
            (exit, r) = Run("boolean", s[0], s[1], m.Path_("o1.stl"), "--op", "union", "--rounding-max-pair-tests", "8000", "--rounding-defect-scale", "0");
            Assert.Equal(0, exit);
            Assert.Equal(0.0, D(r["boolean"]["rounding_defect_scale"]));
            Assert.True(B(r["boolean"]["rounding"]["evaluation_limit_reached"]));
            Assert.False(B(r["boolean"]["rounding_clean"]));
            // a scale of 100 grows it to 3.18 times and finishes clean; a ceiling of 1 forbids the growth
            (exit, r) = Run("boolean", s[0], s[1], m.Path_("o2.stl"), "--op", "union", "--rounding-max-pair-tests", "8000", "--rounding-defect-scale", "100");
            Assert.Equal(0, exit);
            Assert.False(B(r["boolean"]["rounding"]["evaluation_limit_reached"]));
            Assert.True(B(r["boolean"]["rounding_clean"]));
            (exit, r) = Run("boolean", s[0], s[1], m.Path_("o3.stl"), "--op", "union", "--rounding-max-pair-tests", "8000", "--rounding-defect-scale", "100", "--rounding-max-growth", "1");
            Assert.Equal(0, exit);
            Assert.Equal(1.0, D(r["boolean"]["rounding_max_growth"]));
            Assert.True(B(r["boolean"]["rounding"]["evaluation_limit_reached"]));
            // resolve takes them as well
            (exit, r) = Run("resolve", m.Overlapping, m.Path_("o4.stl"), "--rounding-defect-scale", "7", "--rounding-max-growth", "2.5");
            Assert.Equal(0, exit);
            Assert.Equal(7.0, D(r["boolean"]["rounding_defect_scale"]));
            Assert.Equal(2.5, D(r["boolean"]["rounding_max_growth"]));
        }

        [Fact]
        public void Repair_Options_ArePassedToTheRobustRepair()
        {
            var (exit, r) = Run("repair", m.Overlapping, m.Path_("r0.obj"), "--compact");
            Assert.Equal(0, exit);
            Assert.Equal(50000.0, D(r["robust_options"]["rounding_defect_scale"]));
            Assert.Equal(4.0, D(r["robust_options"]["rounding_max_growth"]));
            (exit, r) = Run("repair", m.Overlapping, m.Path_("r1.obj"), "--compact", "--rounding-defect-scale", "123", "--rounding-max-growth", "3");
            Assert.Equal(0, exit);
            Assert.Equal(123.0, D(r["robust_options"]["rounding_defect_scale"]));
            Assert.Equal(3.0, D(r["robust_options"]["rounding_max_growth"]));
            // the budget of the exact stage's rounding follows them (spheres at 8000: stopped without growth, finished with it)
            var s = Spheres("rep");
            var both = new DMesh3(StandardMeshReader.ReadMesh(s[0]));
            new MeshEditor(both).AppendMesh(StandardMeshReader.ReadMesh(s[1]));
            string input = m.Path_("rep_both.g3mesh");
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(input, both, WriteOptions.Defaults).code);
            (exit, r) = Run("repair", input, m.Path_("r2.obj"), "--rounding-max-pair-tests", "8000", "--rounding-defect-scale", "0", "--no-alpha-wrap", "--exact-resolve-rounds", "1");
            Assert.True(B(r["exact_resolve_rounding"]["evaluation_limit_reached"]));
            (exit, r) = Run("repair", input, m.Path_("r3.obj"), "--rounding-max-pair-tests", "8000", "--rounding-defect-scale", "100", "--no-alpha-wrap", "--exact-resolve-rounds", "1");
            Assert.False(B(r["exact_resolve_rounding"]["evaluation_limit_reached"]));
        }

        [Theory]
        [InlineData("--rounding-defect-scale", "-1")]
        [InlineData("--rounding-defect-scale", "NaN")]
        [InlineData("--rounding-max-growth", "0.5")]
        [InlineData("--rounding-max-growth", "NaN")]
        public void InvalidValues_AreRejected(string option, string value)
        {
            var (exit, r) = Run("resolve", m.Overlapping, m.Path_("bad.stl"), option, value);
            Assert.NotEqual(0, exit);
            Assert.Equal(ResultCodes.InvalidArguments, S(r["code"]));
        }
    }
}
