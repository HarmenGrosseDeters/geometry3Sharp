using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The bound on the cone facets (#808, TerrainSlopeDesign.MaxFanDirections, docs/design/TERRAIN.md 5.1): a Tolerance whose
    /// facet share needs more facets per turn is refused with InvalidInput, and the message names a Tolerance from which they
    /// fit (FanTolerance). The pit is the 7-gon of TerrainSlopeFanDirectionsTests
    /// </summary>
    public class TerrainSlopeFanBoundTests
    {
        static TerrainSlopeDesign Pit(double tol, int maxFan = 512) { var d = TerrainSlopeFanDirectionsTests.Pit(tol); d.MaxFanDirections = maxFan; return d; }

        // the table of #808: from 1.7 times the margins down to the smallest valid Tolerance (bits found by bisection), where
        // theta rounded to 0 and nTheta was int.MaxValue (int.MinValue on net48); one ulp below it is below the margins
        [Theory]
        [InlineData(3e-5)]
        [InlineData(1.8e-5)]
        [InlineData(1.7600023111460758e-05)]
        [InlineData(1.760001947437013e-05)]
        [InlineData(1.760001947348195e-05)]
        public void Issue808_Table_RefusedAtOnce(double tol)
        {
            var d = Pit(tol);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            Assert.Contains("cone facets", d.Message);
            Assert.Contains(d.FanTolerance.ToString("R"), d.Message);
            Assert.True(d.FanTolerance > 1e-4, d.FanTolerance.ToString("R"));
            Assert.Equal(0, d.FacetAngle);
            // (one ulp below the smallest valid Tolerance: the numerical margins, no facets named)
            if (tol == 1.760001947348195e-05) {
                d = Pit(1.7600019473481947e-05);
                Assert.False(d.Compute());
                Assert.Contains("numerical margins", d.Message);
                Assert.Equal(0, d.FanTolerance);
            }
        }

        [Fact]
        public void FanDirections_ThetaZeroAndNonPositive_AreRefused()
        {
            // (a facet share so small that 1 - share rounds to 1: theta 0, 2 pi / theta infinite, cast to int before #808)
            Assert.Equal(-1, TerrainSlopeDesign.FanDirections(1e-300, 1, 4096));
            Assert.Equal(-1, TerrainSlopeDesign.FanDirections(0, 1, 4096));
            Assert.Equal(-1, TerrainSlopeDesign.FanDirections(double.NaN, 1, 4096));
            // the share of 64 facets (3/4 theta from a tangent plane, theta = 2 pi / 64): 64, refused by a bound of 63
            double share = (1 - Math.Cos(0.75 * 2 * Math.PI / 64)) * (1 + 1e-12);
            Assert.Equal(64, TerrainSlopeDesign.FanDirections(share, 1, 64));
            Assert.Equal(-1, TerrainSlopeDesign.FanDirections(share, 1, 63));
            Assert.Equal(64, TerrainSlopeDesign.FanDirections(share * 1024, 1024, 4096));
            // (a coarse share: the floor of 16 facets, pi / 8)
            Assert.Equal(16, TerrainSlopeDesign.FanDirections(0.5, 1, 16));
        }

        // the named Tolerance works: the facets fit (64 per turn, the bound) and the design certifies; a Tolerance a little
        // smaller needs a 65th facet (the name is close to the smallest one)
        [Fact]
        public void NamedTolerance_Fits_AndIsCloseToTheSmallest()
        {
            var d = Pit(0.01, 64);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            double named = d.FanTolerance;
            Assert.True(named > 0.01, named.ToString("R"));
            var ok = Pit(named, 64);
            Assert.True(ok.Compute(), ok.Status + " " + ok.Message);
            TerrainSlopeDesignTests.AssertAll(ok);
            Assert.Equal(2 * Math.PI / 64, ok.FacetAngle);
            Assert.Equal(0, ok.FanTolerance);
            var below = Pit(named * (1 - 1e-6), 64);
            Assert.False(below.Compute());
            Assert.Contains("cone facets", below.Message);
        }

        // the default bound, 512 facets per turn: a toe of 500 m reach at slope 1 (the facets' scale sMax 1.1 D0) fits from
        // about 2.3 cm, and 1 cm (781 facets) is refused
        [Fact]
        public void DefaultBound_At500mReach()
        {
            Assert.Equal(512, new TerrainSlopeDesign().MaxFanDirections);
            Assert.InRange(TerrainSlopeDesign.FanDirections(0.024, 1.1 * 500, 512), 480, 512);
            Assert.Equal(-1, TerrainSlopeDesign.FanDirections(0.01, 1.1 * 500, 512));
            Assert.InRange(TerrainSlopeDesign.FanDirections(0.01, 1.1 * 500, 4096), 770, 790);
        }

        // the bound is in [16, 4096]
        [Theory]
        [InlineData(15)]
        [InlineData(4097)]
        [InlineData(-1)]
        public void MaxFanDirections_OutOfRange_IsInvalidInput(int n)
        {
            var d = Pit(0.05, n);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            Assert.Contains("MaxFanDirections", d.Message);
        }

        // reuse: the refusal twice gives the same, and a success after it clears FanTolerance
        [Fact]
        public void Reuse_RefusedTwice_ThenSucceeds()
        {
            var d = Pit(0.01, 32);
            Assert.False(d.Compute());
            string message = d.Message; double named = d.FanTolerance;
            Assert.False(d.Compute());
            Assert.Equal(message, d.Message);
            Assert.Equal(named, d.FanTolerance);
            d.Tolerance = named;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeDesignTests.AssertAll(d);
            Assert.Equal(0, d.FanTolerance);
            Assert.Equal(2 * Math.PI / 32, d.FacetAngle);
        }

        // Direction Auto: the cut refuses first and passes its named Tolerance on; there the mixed design certifies
        [Fact]
        public void Auto_PassesTheNamedToleranceOn()
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-60, -60, 60, 60), 16, 16, (x, y) => 4 + 0.05 * x);
            var d = new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(TerrainSlopeFanDirectionsTests.SevenGon(), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5)) },
                Terrain = terrain, Direction = TerrainSlopeDesign.Directions.Auto, Tolerance = 0.002, MaxFanDirections = 16, BuildComposite = true
            };
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            Assert.Contains("cone facets", d.Message);
            Assert.True(d.FanTolerance > 0.002, d.FanTolerance.ToString("R"));
            Assert.Contains(d.FanTolerance.ToString("R"), d.Message);
            // (the named Tolerance counts the share a terrain reserves for the catch points, Tolerance / 4: its facets fit)
            d.Tolerance = d.FanTolerance;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeMixedTests.AssertMixed(d);
            Assert.Equal(2 * Math.PI / 16, d.FacetAngle);
        }

        // Direction Auto with a fill deeper than the cut: the cut refuses first, and the fill's facets need a larger Tolerance
        // than the cut's own (checked without building the fill); the Tolerance named fits both runs: at it no run refuses
        // its facets (the fill does not reach the terrain everywhere there, NoDaylight, a partial result)
        [Fact]
        public void Auto_DeeperFill_NamesTheFillsTolerance()
        {
            Func<TerrainSlopeDesign.Directions, double, TerrainSlopeDesign> make = (dir, tol) => new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(TerrainSlopeFanDirectionsTests.SevenGon(), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5)) },
                Terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-60, -60, 60, 60), 16, 16, (x, y) => x > 0 ? 2 + 0.3 * x : 2 + x),
                Direction = dir, Tolerance = tol, MaxFanDirections = 16
            };
            var d = make(TerrainSlopeDesign.Directions.Auto, 0.002);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            Assert.StartsWith("cut: ", d.Message);
            Assert.Contains("the facets fit from Tolerance " + d.FanTolerance.ToString("R"), d.Message);
            var cut = make(TerrainSlopeDesign.Directions.Cut, 0.002);
            Assert.False(cut.Compute());
            Assert.True(d.FanTolerance > cut.FanTolerance * 2, $"{d.FanTolerance} {cut.FanTolerance}");
            d.Tolerance = d.FanTolerance;
            d.Compute();
            Assert.True(d.Status == TerrainSlopeDesign.Statuses.Success || d.Status == TerrainSlopeDesign.Statuses.NoDaylight, d.Status + " " + d.Message);
            Assert.DoesNotContain("cone facets", d.Message);
            Assert.Equal(2 * Math.PI / 16, d.FacetAngle);
            // (each run's slope surface: the mixed one has both, overlapping in XY by design)
            TerrainSlopeDesignTests.AssertSurface(d.AutoCut);
            TerrainSlopeDesignTests.AssertSurface(d.AutoFill);
        }

        // review of #850: on a sloped terrain without HeightLimit the cap's estimate (CapEstimateRounds) grows with the
        // Tolerance by more than its shift, so a name from the build's search alone was refused again (10.998, then 12.005);
        // the name is the fixed point of the whole run up to the facets, and the design certifies there at once
        static TerrainSlopeDesign SlopedSquare(double ratio, int maxFan, double tol)
        {
            var toe = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            toe.CutStart[0] = SlopeSpec.Ratio(ratio); toe.CutEnd[0] = SlopeSpec.Ratio(ratio);
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-200, -200, 200, 200), 20, 20, (x, y) => 5 + 0.01 * x);
            return new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = tol, MaxFanDirections = maxFan, BuildComposite = true };
        }

        [Theory]
        [InlineData(16)]
        [InlineData(32)]
        public void SlopedTerrain_TheNameIsTheFixedPoint(int maxFan)
        {
            var d = SlopedSquare(10, maxFan, 0.01);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            double named = d.FanTolerance;
            Assert.True(named > 0.01, named.ToString("R"));
            // (the search runs the design again at other Tolerances: the caller's input is kept, and its runs are not counted)
            Assert.Equal(0.01, d.Tolerance);
            Assert.Equal(1, d.CapRounds);
            var ok = SlopedSquare(10, maxFan, named);
            Assert.True(ok.Compute(), ok.Status + " " + ok.Message);
            Assert.Equal(2 * Math.PI / maxFan, ok.FacetAngle);
            TerrainSlopeCatchLineTests.AssertCatchLines(ok, false);
            TerrainSlopeCatchLineTests.AssertComposite(ok);
        }

        // review of #850: slopes far apart (1:20 and 1:1) with a cap from the terrain: no Tolerance fits within the bound (the
        // facet share over the facets' scale tends to about 0.75 sMin / (1.1 sMax)); said so, with no name (not Infinity)
        [Fact]
        public void SlopesFarApart_NoToleranceFits()
        {
            var d = SlopedSquare(20, 16, 0.01);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            Assert.Contains("no Tolerance fits within MaxFanDirections", d.Message);
            Assert.DoesNotContain("Infinity", d.Message);
            Assert.DoesNotContain("∞", d.Message);
            Assert.Equal(0, d.FanTolerance);
            // (a larger bound fits)
            d.MaxFanDirections = 64;
            Assert.False(d.Compute());
            Assert.True(d.FanTolerance > 0 && MathUtil.IsFinite(d.FanTolerance), d.Message);
        }

        // the refusal and the named Tolerance in every frame (rotations about z; a translated frame in its local frame): the
        // 2^k frames bit for bit, the others within the rounding of the margins; the named Tolerance certifies in each, with
        // the facets at the bound
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities_NamedToleranceScales(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            Func<double, TerrainSlopeDesign> make = (tol) => new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(TerrainSlopeFanDirectionsTests.SevenGon().Select(p => s.Apply(p)).ToList(), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5)) },
                Direction = TerrainSlopeDesign.Directions.Cut, HeightLimit = s.Apply(new Vector3d(0, 0, 8)).z, Tolerance = tol,
                MaxFanDirections = 64, LocalFrame = s.Translation != Vector3d.Zero
            };
            var r = Pit(0.01, 64);
            Assert.False(r.Compute());
            var d = make(0.01 * s.Scale);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            Assert.Contains("cone facets", d.Message);
            if (s.IsExact)
                Assert.Equal(r.FanTolerance * s.Scale, d.FanTolerance);
            else
                Assert.True(Math.Abs(d.FanTolerance / s.Scale - r.FanTolerance) <= 1e-3 * r.FanTolerance, $"{d.FanTolerance / s.Scale} {r.FanTolerance}");
            var ok = make(d.FanTolerance);
            Assert.True(ok.Compute(), ok.Status + " " + ok.Message);
            Assert.Equal(2 * Math.PI / 64, ok.FacetAngle);
            if (ok.LocalFrame) {
                Vector2d o = ok.UsedOrigin;
                ok.Toes = ok.Toes.Select(t => SlopeToe.Constant(t.Points.Select(p => new Vector3d(p.x - o.x, p.y - o.y, p.z)).ToList(), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5))).ToList();
            }
            TerrainSlopeDesignTests.AssertAll(ok);
        }

        // RequiredCatchLineAccuracy makes the Tolerance smaller in rounds; a round whose Tolerance the bound refuses takes
        // the named one instead, if it is still smaller (before: the accuracy was reported as not reached, "a smaller
        // Tolerance failed: InvalidInput", after one round)
        // Slow: about 4 s (four designs on a 64 x 64 terrain, the last at 384 facets per turn)
        [Trait("Category", "Slow")]
        [Fact]
        public void AccuracyRounds_TakeTheNamedTolerance()
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-80, -80, 80, 80), 64, 64, (x, y) => 3 + 0.8 * x);
            var d = new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)) },
                Terrain = terrain, Tolerance = 0.05, HeightLimit = 80, RequiredCatchLineAccuracy = 0.3, TerrainVerticalUncertainty = 0.04, MaxFanDirections = 384
            };
            d.Compute();
            Assert.DoesNotContain("failed: InvalidInput", d.Message);
            Assert.True(d.UsedTolerance < 0.05, d.UsedTolerance.ToString("R"));
            // (the last round at the named Tolerance: the facets at the bound; the accuracy is still not reached, as the terrain's
            // vertical uncertainty alone exceeds it there)
            Assert.Equal(2 * Math.PI / 384, d.FacetAngle);
            Assert.Equal(TerrainSlopeDesign.Statuses.CatchLineAccuracyNotReached, d.Status);
        }
    }
}
