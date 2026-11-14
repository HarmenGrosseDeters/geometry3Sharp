using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #861: the budget of TerrainSlopeDesign counts the reach of the cones D0 from the lowest built height. A ramp's road is
    /// built up to its share below the specified heights (mainly RoadMargin L = delta sMin L / 2), so its cones reach up to
    /// share / (sMin (1 - delta)) further; before, D0 came from the specified toe and only the 1.1 of the budget's terms
    /// covered that, which a long ramp of low grade with a large margin exceeds (here L = 250 at 2 %, delta = 0.01: 13 %
    /// further, against the 10 % of the 1.1). Uses only API of the base, so the new-tests-on-base gate runs it there
    /// </summary>
    public class TerrainSlopeRampReachTests
    {
        // a square pit of half 150 (toe at 0, slope 1:1, up to h) with a straight ramp of length L along its south wall
        internal static TerrainSlopeDesign LongRamp(double tol, double scale = 1, double L = 250, double grade = 2, double h = 10)
        {
            var start = new Vector2d(-L / 2, -150) * scale;
            var end = new Vector2d(L / 2, -150 - L * grade / 100 * 1.15) * scale;
            var toe = SlopeToe.Constant(TerrainSlopeRampTests.PitWithStart(150 * scale, 0, start), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var ramp = new SlopeRamp() { Toe = 0, Path = new List<Vector2d>() { start, end }, Grade = SlopeSpec.Percent(grade), Width = 4 * scale, Side = SlopeToe.Sides.Right };
            return new TerrainSlopeDesign() { Toes = { toe }, Ramps = { ramp }, HeightLimit = h * scale, Tolerance = tol * scale, SlopeMargin = 0.01 };
        }

        // the budget at the built reach, from the outputs (slope 1, so sMin = sMax = 1 and e_z = MinFeatureSize delta / 8):
        // the toe lowering's bound 2 e_z (1 + 4.4 D / MinFeatureSize), the margin delta 1.1 D, the ramps' share and the facets
        // 1.1 D (1 - cos(3/4 theta)) fit into Tolerance (the realization merge's share, about 1e-12 here, left out: this is
        // a necessary condition); it fails by about 0.05 when D is the reach from the specified toe
        static void AssertBudgetAtTheBuiltReach(TerrainSlopeDesign d, double h)
        {
            double delta = d.UsedSlopeMargin, mf = d.UsedMinFeatureSize, share = d.UsedRampShare;
            double D = (h + share) / (1 - delta);
            double ez = mf * delta / 8;
            double lambda = 2 * ez * (1 + 4 * 1.1 * D / mf);
            double facets = 1.1 * D * (1 - Math.Cos(0.75 * d.FacetAngle));
            double sum = lambda + delta * 1.1 * D + share + facets;
            Assert.True(share > 0.1 * h, $"the ramp's share {share}");
            Assert.True(sum <= d.Tolerance * (1 + 1e-9), $"the budget at the built reach {sum} exceeds Tolerance {d.Tolerance} (λ {lambda}, δ term {delta * 1.1 * D}, ramp {share}, facets {facets})");
        }

        [Fact]
        public void LongLowGradeRamp_TheBudgetCountsTheBuiltReach()
        {
            var d = LongRamp(1.7);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            AssertBudgetAtTheBuiltReach(d, 10);
            TerrainSlopeRampTests.AssertRamps(d, 6);
        }

        // the 2^k frames: the reach counts the share in the frame's units, so the design scales bit for bit (the facet angle,
        // the share, the surface)
        [Theory]
        [InlineData(1.0 / 1024)]
        [InlineData(1024.0)]
        public void LongLowGradeRamp_Pow2Scales(double scale)
        {
            var r = LongRamp(1.7);
            Assert.True(r.Compute(), r.Status + " " + r.Message);
            var d = LongRamp(1.7, scale);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Equal(r.FacetAngle, d.FacetAngle);
            Assert.Equal(r.UsedRampShare * scale, d.UsedRampShare);
            Assert.Equal(r.UsedSlopeMargin, d.UsedSlopeMargin);
            Assert.Equal(r.SlopeSurface.TriangleCount, d.SlopeSurface.TriangleCount);
            var s = new Similarity("scale", scale, Matrix3d.Identity, Vector3d.Zero);
            Assert.Equal("", Invariance.Difference(r.SlopeSurface, Invariance.MappedBack(d.SlopeSurface, s)));
        }
    }
}
