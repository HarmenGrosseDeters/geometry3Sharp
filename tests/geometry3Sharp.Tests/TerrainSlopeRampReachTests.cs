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
        static void AssertBudgetAtTheBuiltReach(TerrainSlopeDesign d, double h, double z0 = 0)
        {
            double delta = d.UsedSlopeMargin, mf = d.UsedMinFeatureSize, share = d.UsedRampShare;
            double D = (h + share) / (1 - delta);
            double ez = mf * delta / 8;
            double lambda = 2 * ez * (1 + 4 * 1.1 * D / mf);
            double facets = 1.1 * D * (1 - Math.Cos(0.75 * d.FacetAngle));
            double sum = lambda + delta * 1.1 * D + share + facets;
            Assert.True(share > 0.1 * h, $"the ramp's share {share} (toe at {z0})");
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

        // every frame of Similarity.All (rotated about z; a translated frame in LocalFrame): the budget holds at the built reach
        // and the design certifies; the 2^k frames are bit for bit (the facet angle, the share, the surface)
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void LongLowGradeRamp_Similarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var d = LongRamp(1.7);
            foreach (var t in d.Toes) t.Points = t.Points.Select(p => s.Apply(p)).ToList();
            foreach (var r in d.Ramps) { r.Path = r.Path.Select(p => s.Apply(new Vector3d(p.x, p.y, 0)).xy).ToList(); r.Width = s.ApplyLength(r.Width); }
            d.HeightLimit = s.Apply(new Vector3d(0, 0, d.HeightLimit)).z;
            d.Tolerance = s.ApplyLength(d.Tolerance);
            d.LocalFrame = s.Translation != Vector3d.Zero;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            AssertBudgetAtTheBuiltReach(d, s.ApplyLength(10), s.Translation.z);
            var r0 = LongRamp(1.7);
            Assert.True(r0.Compute(), r0.Status + " " + r0.Message);
            if (s.IsExact) {
                Assert.Equal(r0.FacetAngle, d.FacetAngle);
                Assert.Equal(r0.UsedRampShare * s.Scale, d.UsedRampShare);
                Assert.Equal("", Invariance.Difference(r0.SlopeSurface, Invariance.MappedBack(d.SlopeSurface, s)));
            } else
                Assert.True(Math.Abs(d.UsedRampShare / s.Scale - r0.UsedRampShare) <= 1e-6 * r0.UsedRampShare, $"{d.UsedRampShare / s.Scale} {r0.UsedRampShare}");
        }
    }
}
