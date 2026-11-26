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
            Assert.True(t.RealizedVolumeFraction > 1);
            Assert.Contains("MinDivisionsForEnclosure: 32", t.Message);
            var thin = new TpmsLattice { Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = 1, Divisions = 16, VolumeFraction = 0.1, Box = new AxisAlignedBox3d(Vector3d.Zero, 1) };
            thin.Compute();
            Assert.Equal(TpmsStatus.NotResolved, thin.Status);
            Assert.True(thin.RealizedVolumeFraction < TpmsLattice.SheetRefusalRatio * 0.1);
            Assert.True(thin.MeanWallThickness > 0);
        }

        public static System.Collections.Generic.IEnumerable<object[]> Cases()
        {
            yield return new object[] { TpmsType.Gyroid, TpmsStyle.Sheet, 0.3 };         // minimum 32
            yield return new object[] { TpmsType.Gyroid, TpmsStyle.Sheet, 0.5 };         // minimum 64: only the coarse grids are contoured
            yield return new object[] { TpmsType.SchwarzP, TpmsStyle.SkeletalLow, 0.3 };
            yield return new object[] { TpmsType.IWP, TpmsStyle.Sheet, 0.2 };
            yield return new object[] { TpmsType.IWP, TpmsStyle.SkeletalLow, 0.5 };
            yield return new object[] { TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.2 };
        }

        // the part of the box grown by the margin that the result fills (the measure of the refusal)
        static double Filled(TpmsLattice t, double half)
        {
            double growth = Math.Pow((2 * half + 2 * t.IsoValueUsed) / (2 * half), 3);
            return t.RealizedVolumeFraction / growth;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Estimate_MatchesTheFilledFraction_AndTheMinimumIsNotRefused(TpmsType type, TpmsStyle style, double fraction)
        {
            var t = Make(type, style, fraction);
            int min = t.MinDivisionsForEnclosure();
            Assert.True(min >= 4 && min <= 512 && (min & (min - 1)) == 0, "min " + min);
            // grids of 16 and 32 cubes per period (and the minimum where it is 16 or 32); a finer grid costs seconds
            foreach (int d in new[] { 16, 32 }) {
                var r = Make(type, style, fraction, d);
                double estimate = r.EnclosureFractionEstimate(d);
                r.Compute();
                Assert.True(r.IsoValueUsed > 0);
                // the estimate is not below the contoured result by more than 0.02 and above it by at most 0.08 (the periodic average
                // over the margin band of the box differs from the cut box; the refused one keeps its measures)
                Assert.InRange(estimate - Filled(r, 1), -0.02, 0.08);
                if (d >= min) {
                    Assert.True(r.Status == TpmsStatus.Ok, type + " " + style + " " + fraction + " div " + d + ": " + r.Status + " " + r.Message);
                    Assert.Equal("", MeshRobustRepair.NotCleanReasons(r.Mesh));
                    Assert.True(Filled(r, 1) < TpmsLattice.EnclosureRefusalFraction);
                }
            }
        }

        [Theory]
        [InlineData(0.5, 32, true)]     // fills 0.93 of the grown box for any box size: Ok, although RealizedVolumeFraction is 1.01 (box 2) and 1.09 (box 1)
        [InlineData(0.3, 16, false)]    // fills 0.99 and more: the solid box, refused for any box size
        public void Refusal_DoesNotDependOnTheBoxSize(double fraction, int divisions, bool ok)
        {
            double previous = -1;
            foreach (double half in new[] { 0.5, 1.0 }) {
                var t = new TpmsLattice {
                    Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = 1, Divisions = divisions, VolumeFraction = fraction,
                    Box = new AxisAlignedBox3d(Vector3d.Zero, half), Enclosure = true
                };
                t.Compute();
                Assert.Equal(ok ? TpmsStatus.Ok : TpmsStatus.NotResolved, t.Status);
                double filled = Filled(t, half);
                Assert.Equal(filled >= TpmsLattice.EnclosureRefusalFraction, !ok);
                if (ok) { Assert.NotNull(t.Mesh); Assert.True(t.RealizedVolumeFraction > 1, "realized " + t.RealizedVolumeFraction); }   // a fraction of the box, above 1, and no refusal
                else Assert.Null(t.Mesh);
                if (previous >= 0) Assert.InRange(filled - previous, -0.01, 0.01);   // the same measure at both sizes
                previous = filled;
            }
        }

        [Fact]
        public void Estimate_FallsWithDivisions_AndIsExactUnderPowerOfTwoScaling()
        {
            var t = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3);
            double previous = double.PositiveInfinity;
            foreach (int d in new[] { 8, 16, 32, 64, 128, 256 }) {
                double e = t.EnclosureFractionEstimate(d);
                Assert.True(e < previous || d <= 16 && e == 1, "estimate at " + d + ": " + e);   // 1 where the margin swallows the voids
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
            var thick = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 16);
            thick.WallThickness = 0.97 / TpmsLattice.AreaRatio(TpmsType.Gyroid); // a fraction of the first-order map of 0.97, above 0.95: invalid for Compute
            Assert.True(double.IsNaN(thick.EnclosureFractionEstimate(16)));
            Assert.True(double.IsNaN(Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 16, 0.3).EnclosureFractionEstimate(16)));   // a cube of 0.3 / 16: not dyadic
            var graded = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3);
            graded.VolumeFractionAt = p => 0.3;
            Assert.True(double.IsNaN(graded.EnclosureFractionEstimate(16)));
            Assert.True(double.IsNaN(Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3).EnclosureFractionEstimate(3)));
            Assert.True(double.IsNaN(Make(TpmsType.Gyroid, TpmsStyle.Sheet, 1.5).EnclosureFractionEstimate(16)));
        }
    }
}
