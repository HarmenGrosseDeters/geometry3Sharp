using System;
using System.Globalization;
using System.IO;
using Xunit;
using System.Text.Json.Nodes;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary>
    /// mesh2d reports "area" and "largest_area" in the coordinate units and "area_relative" in units of the squared extent of the mesh. The
    /// areas are summed in units of a power of two near the extent, so the products of the edge vectors do not underflow (the report was 0
    /// below coordinates of about 1e-160); where the area is below the smallest double, "area_relative" still tells it.
    /// </summary>
    public class Mesh2dAreaScaleTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public Mesh2dAreaScaleTests(TestMeshes meshes) { m = meshes; }

        static string F(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        // a square of side 3s (an L-free, hole-free domain), a point inside, at the offset (ox, oy)
        string Square(double s, double ox, double oy) =>
            $"{{\"points\": [[{F(ox)},{F(oy)}],[{F(ox + 3 * s)},{F(oy)}],[{F(ox + 3 * s)},{F(oy + 3 * s)}],[{F(ox)},{F(oy + 3 * s)}],[{F(ox + s)},{F(oy + 2 * s)}]], \"rings\": [[0,1,2,3]]}}";

        JsonObject RunMesh(double s, double ox = 0, double oy = 0)
        {
            string json = m.Path_($"area_{F(s)}_{F(ox)}.json"), output = m.Path_($"area_{F(s)}_{F(ox)}.obj");
            File.WriteAllText(json, Square(s, ox, oy));
            var (exit, r) = Run("mesh2d", json, output, "--quality", "false");
            Assert.True(exit == 0, r.ToJsonString());
            return r;
        }

        [Theory]
        [InlineData(1e60)]
        [InlineData(1.0)]
        [InlineData(1e-100)]
        [InlineData(1e-150)]
        [InlineData(1e-155)]
        [InlineData(1e-160)]
        public void Area_IsTheSquareArea_AtEveryScale(double s)
        {
            var r = RunMesh(s);
            Assert.Equal(1.0, D(r["area_relative"]), 12);          // 9 s^2 over the squared extent (3 s)^2 = 1
            double expected = 9 * s * s;
            Assert.True(expected > 0, "the case is about areas the double can hold");
            Assert.Equal(1.0, D(r["area"]) / expected, s <= 1e-155 ? 3 : 12);       // (subnormal areas keep 52 - k bits)
            Assert.True(D(r["largest_area"]) > 0);
        }

        [Fact]
        public void BelowTheSmallestDouble_TheAreaIsZero_TheRelativeAreaIsNot()
        {
            var r = RunMesh(1e-200);
            Assert.Equal(0.0, D(r["area"]));                       // 9e-400 is no double
            Assert.Equal(1.0, D(r["area_relative"]), 12);
            Assert.True(D(r["smallest_angle"]) > 20, r.ToJsonString());      // the angles never depended on the scale
        }

        [Theory]
        [InlineData(1.0 / 1024, 0.0, 0.0, true)]
        [InlineData(1024.0, 0.0, 0.0, true)]
        [InlineData(1.0, 4096.0, -1024.0, true)]
        [InlineData(1e-3, 0.0, 0.0, false)]
        [InlineData(1e3, 0.0, 0.0, false)]
        [InlineData(1.0, 632117.25, 5712043.5, true)]
        [InlineData(1e-3, 632117.25, 5712043.5, false)]
        public void RelativeArea_DoesNotChangeUnderSimilarities(double scale, double ox, double oy, bool exact)
        {
            double reference = D(RunMesh(1.0)["area_relative"]);
            double got = D(RunMesh(scale, ox, oy)["area_relative"]);
            // exact: powers of two and offsets that keep the coordinates exact (the square side 3 is a multiple of a quarter at these
            // offsets) give the same value; the others round the coordinates a little
            double tolerance = exact ? 0 : ox != 0 ? 1e-6 : 1e-12;
            Assert.True(Math.Abs(got - reference) <= tolerance * reference, $"{got:R} vs {reference:R}");
        }

        [Fact]
        public void Area_OfTheStandardCases_IsUnchanged()
        {
            var r = RunMesh(1.0);
            Assert.Equal(9.0, D(r["area"]), 12);
            Assert.Equal(1.0, D(r["area_relative"]), 12);
        }
    }
}
