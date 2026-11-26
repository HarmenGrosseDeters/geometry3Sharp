using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1596: TpmsStatus.NotResolved, the thresholds, and the estimate of the Enclosure fraction (EnclosureFractionEstimate, MinDivisionsForEnclosure),
    /// which is computed without contouring.
    /// </summary>
    public class TpmsLatticeEnclosureEstimateTests
    {
        static TpmsLattice Make(TpmsType type, TpmsStyle style, double fraction, int divisions = 16, double scale = 1)
        {
            return new TpmsLattice {
                Type = type, Style = style, CellSize = scale, Divisions = divisions, VolumeFraction = fraction,
                Box = new AxisAlignedBox3d(Vector3d.Zero, scale), Enclosure = true
            };
        }

        [Fact]
        public void NotResolved_IsTheLastStatus_AndTheRefusalKeepsTheMeasures()
        {
            Assert.Equal(8, (int)TpmsStatus.NotResolved);
            Assert.Equal(7, (int)TpmsStatus.Cancelled);
            var t = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3);
            t.Compute();
            Assert.Equal(TpmsStatus.NotResolved, t.Status);
            Assert.Null(t.Mesh);
            Assert.True(t.RealizedVolumeFraction > TpmsLattice.EnclosureRefusalFraction);
            Assert.Contains("MinDivisionsForEnclosure: 32", t.Message);
            var thin = new TpmsLattice { Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = 1, Divisions = 16, VolumeFraction = 0.1, Box = new AxisAlignedBox3d(Vector3d.Zero, 1) };
            thin.Compute();
            Assert.Equal(TpmsStatus.NotResolved, thin.Status);
            Assert.True(thin.RealizedVolumeFraction < TpmsLattice.SheetRefusalRatio * 0.1);
            Assert.True(thin.MeanWallThickness > 0);
        }

        public static System.Collections.Generic.IEnumerable<object[]> Cases()
        {
            foreach (TpmsType type in new[] { TpmsType.Gyroid, TpmsType.SchwarzP, TpmsType.IWP })
                foreach (TpmsStyle style in new[] { TpmsStyle.Sheet, TpmsStyle.SkeletalLow })
                    foreach (double fraction in new[] { 0.2, 0.3, 0.5 })
                        yield return new object[] { type, style, fraction };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Estimate_MatchesTheRealizedFraction_AndTheMinimumIsNotRefused(TpmsType type, TpmsStyle style, double fraction)
        {
            var t = Make(type, style, fraction);
            int min = t.MinDivisionsForEnclosure();
            Assert.True(min >= 4 && min <= 512 && (min & (min - 1)) == 0, "min " + min);
            // a coarse and a fine grid: the estimate is not below the contoured result by more than 0.02 and above it by at most 0.08 (the periodic average
            // over the margin band of the box differs from the cut box; the refused one keeps its measures)
            foreach (int d in new[] { 16, Math.Max(min, 16) }) {
                var r = Make(type, style, fraction, d);
                double estimate = r.EnclosureFractionEstimate(d);
                r.Compute();
                Assert.InRange(estimate - r.RealizedVolumeFraction, -0.02, 0.08);
                if (d >= min) {
                    Assert.True(r.Status == TpmsStatus.Ok, type + " " + style + " " + fraction + " div " + d + ": " + r.Status + " " + r.Message);
                    Assert.Equal("", MeshRobustRepair.NotCleanReasons(r.Mesh));
                    Assert.True(r.RealizedVolumeFraction <= 1);
                }
            }
        }

        [Fact]
        public void Estimate_FallsWithDivisions_AndIsExactUnderPowerOfTwoScaling()
        {
            var t = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3);
            double previous = double.PositiveInfinity;
            foreach (int d in new[] { 8, 16, 32, 64, 128, 256 }) {
                double e = t.EnclosureFractionEstimate(d);
                Assert.True(e < previous, "estimate at " + d + ": " + e);
                previous = e;
            }
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                var scaled = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 16, s.Scale);
                Assert.Equal(t.EnclosureFractionEstimate(16), scaled.EnclosureFractionEstimate(16));
                Assert.Equal(t.MinDivisionsForEnclosure(), scaled.MinDivisionsForEnclosure());
            }
            Assert.Equal(t.EnclosureFractionEstimate(16), t.EnclosureFractionEstimate(16));   // no state
        }

        [Fact]
        public void Estimate_IsNaN_WhereItDoesNotApply()
        {
            var hybrid = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3);
            hybrid.SecondType = TpmsType.IWP; hybrid.Blend = TpmsLattice.SigmoidBlend(0, 1, 0.5);
            Assert.True(double.IsNaN(hybrid.EnclosureFractionEstimate(16)));
            Assert.Equal(0, hybrid.MinDivisionsForEnclosure());
            var graded = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3);
            graded.VolumeFractionAt = p => 0.3;
            Assert.True(double.IsNaN(graded.EnclosureFractionEstimate(16)));
            Assert.True(double.IsNaN(Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3).EnclosureFractionEstimate(3)));
            Assert.True(double.IsNaN(Make(TpmsType.Gyroid, TpmsStyle.Sheet, 1.5).EnclosureFractionEstimate(16)));
        }
    }
}
