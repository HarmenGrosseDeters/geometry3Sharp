using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The refusals of TerrainSlopeDesign that certify nothing: a smaller Tolerance (RequiredCatchLineAccuracy) that
    /// fails, and a composite whose snap the exact certificate refuses at every margin (SlopeViolation, NotCertified),
    /// also when the larger margin needs more cone facets than MaxFanDirections.
    /// </summary>
    public class CoverageGate2CDesignTests
    {
        // ---- RequiredCatchLineAccuracy: the next round's Tolerance fails ----

        [Fact]
        public void RequiredAccuracy_NextToleranceBelowTheNumericalMargins_NamesTheFailure()
        {
            // flat terrain, a pit: the first round succeeds with catch lines of uncertainty about Tolerance / s; 1e-5 m is
            // reachable by a smaller Tolerance (about 8e-6), which is below the numerical margins (1.8e-4) and refused
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-80, -80, 80, 80), 8, 8, (x, y) => 6);
            var toe = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var d = new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = 0.05, HeightLimit = 80, RequiredCatchLineAccuracy = 1e-5 };
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.CatchLineAccuracyNotReached, d.Status);
            Assert.Equal(TerrainSlopeDesign.StatusKinds.NotCertified, d.StatusKind);
            Assert.StartsWith("the catch line's horizontal uncertainty exceeds RequiredCatchLineAccuracy; a smaller Tolerance (", d.Message);
            Assert.Contains(") failed: InvalidInput: ", d.Message);
            Assert.Contains("is below the numerical margins", d.Message);
            // (the failed round's Tolerance, the caller's kept; the previous round's catch lines, with their uncertainties)
            Assert.True(d.UsedTolerance < 0.05 && d.UsedTolerance > 0, d.UsedTolerance.ToString("R"));
            Assert.Equal(0.05, d.Tolerance);
            var l = Assert.Single(d.CatchLines);
            Assert.NotEmpty(l.Points);
            Assert.Equal(l.Gradient.Count, l.Uncertainty.Count);
            Assert.All(l.Uncertainty, u => Assert.True(u > 0 && double.IsFinite(u)));
            Assert.Null(d.SlopeSurface);
            Assert.Null(d.Composite);
            Assert.Equal(0, d.Volume);
        }

        // ---- the composite's snap certificate fails at every margin ----

        // the 7-gon of TerrainSlopeMixedTests (0.07, 0.123, 2.1) at 1e-4 scale, rotated, at UTM coordinates: the toe (1 mm)
        // is far below the frame's feature size, the catch points' moves exceed the share of Tolerance, at every margin
        static TerrainSlopeDesign Tiny(TerrainSlopeDesign.Directions dir)
        {
            var s = new Similarity("scale 1e-4, rotated about z, utm", 1e-4, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), new Vector3d(632117.25, 5712043.5, 113.75));
            return TerrainSlopeMixedTests.Make(0.07, 0.123, 2.1, dir, s);
        }

        static void AssertNotCertified(TerrainSlopeDesign d)
        {
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.SlopeViolation, d.Status);
            Assert.Equal(TerrainSlopeDesign.StatusKinds.NotCertified, d.StatusKind);
            Assert.Contains("the composite is not certified", d.Message);
            Assert.Contains("fits into Tolerance (TERRAIN.md 5.5)", d.Message);
            Assert.Null(d.SlopeSurface);
            Assert.Null(d.Composite);
            Assert.Equal(0, d.Volume);
        }

        [Theory]
        [InlineData(TerrainSlopeDesign.Directions.Cut)]
        [InlineData(TerrainSlopeDesign.Directions.Fill)]
        public void SnapCertificate_FailingAtEveryMargin_IsNotCertified(TerrainSlopeDesign.Directions dir)
        {
            var d = Tiny(dir);
            AssertNotCertified(d);
            // (the margin raised by 8 up to the largest, 1e-3: 1e-6 .. 5.12e-4, four attempts; then the verdict of the last)
            Assert.Equal(4, d.Attempts);
            Assert.Equal(5.12e-4, d.UsedSlopeMargin, 12);
            Assert.DoesNotContain("cone facets per turn", d.Message);
            Assert.False(d.Message.StartsWith("cut: ") || d.Message.StartsWith("fill: "));
        }

        [Fact]
        public void SnapCertificate_FailingAtEveryMargin_InAMixedRun_NamesTheRun()
        {
            var d = Tiny(TerrainSlopeDesign.Directions.Auto);
            AssertNotCertified(d);
            Assert.StartsWith("cut: ", d.Message);
            Assert.Equal(TerrainSlopeDesign.Statuses.SlopeViolation, d.AutoCut.Status);
            Assert.Equal(4, d.AutoCut.Attempts);
        }

        // ---- a larger margin needs more cone facets than MaxFanDirections ----

        // with 24 facets per turn the first margin fits (Tolerance 0.98 x 4.9e-6 m) and the larger margins, whose numerical margins
        // take more of Tolerance, do not: said so, with the Tolerance they fit from (before: a bare "failed" of the last attempt)
        [Theory]
        [InlineData(0.98)]
        [InlineData(0.96)]
        public void LargerMarginNeedingMoreFacets_IsReportedWithTheToleranceItFitsFrom(double toleranceFactor)
        {
            var d = Tiny(TerrainSlopeDesign.Directions.Cut);
            d.MaxFanDirections = 24;
            d.Tolerance *= toleranceFactor;
            double given = d.Tolerance;
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.SlopeViolation, d.Status);
            Assert.Equal(TerrainSlopeDesign.StatusKinds.NotCertified, d.StatusKind);
            Assert.Contains("the composite is not certified", d.Message);
            Assert.Contains("; a larger margin needs more than 24 cone facets per turn (they fit from Tolerance ", d.Message);
            Assert.DoesNotContain("does not fit into Tolerance", d.Message);
            // (the Tolerance named is the one the facets fit from: larger than the given, and the last text of the message)
            Assert.True(d.FanTolerance > given, $"{d.FanTolerance:R} <= {given:R}");
            Assert.EndsWith(FormattableString.Invariant($"(they fit from Tolerance {d.FanTolerance:R})"), d.Message);
            Assert.True(d.UsedSlopeMargin > 1e-6, d.UsedSlopeMargin.ToString("R"));
            Assert.Equal(given, d.Tolerance);
            Assert.Null(d.SlopeSurface);
        }
    }
}
