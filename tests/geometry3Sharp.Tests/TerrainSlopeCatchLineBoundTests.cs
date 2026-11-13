// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The proven horizontal bound of the catch line (docs/design/TERRAIN.md 5.5, "The proven bound",
    /// CatchLine.ProvenUncertainty): analytic cases where the band's width is known, the terrain's vertical uncertainty,
    /// and an oracle that samples the band directly (point location in the realized slope surface and the terrain, no
    /// code of the bound): every sampled point of the band is within the bound of some segment, or in a reported box.
    /// </summary>
    public class TerrainSlopeCatchLineBoundTests
    {
        readonly ITestOutputHelper output;
        public TerrainSlopeCatchLineBoundTests(ITestOutputHelper output) { this.output = output; }

        // a square pit at z 0 (side 16), slope 1:ratio, terrain z = h + g x
        static TerrainSlopeDesign Pit(double ratio, double h, double g, double tol = 0.05, int n = 64)
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-80, -80, 80, 80), n, n, (x, y) => h + g * x);
            var toe = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(ratio));
            return new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = tol, HeightLimit = 80 };
        }

        // the height of a mesh at p (the first triangle that holds p in XY, closed), NaN if none
        static double HeightAt(DMesh3 m, Vector2d p)
        {
            foreach (int t in m.TriangleIndices()) {
                Index3i v = m.GetTriangle(t);
                Vector3d a = m.GetVertex(v.a), b = m.GetVertex(v.b), c = m.GetVertex(v.c);
                double d = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (d == 0) continue;
                double l1 = ((p.x - a.x) * (c.y - a.y) - (p.y - a.y) * (c.x - a.x)) / d;
                double l2 = ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x)) / d;
                if (l1 < -1e-12 || l2 < -1e-12 || l1 + l2 > 1 + 1e-12) continue;
                return a.z + l1 * (b.z - a.z) + l2 * (c.z - a.z);
            }
            return double.NaN;
        }

        static double DistanceToSegment(Vector2d p, Vector2d a, Vector2d b)
        {
            Vector2d u = b - a;
            double t = u.LengthSquared > 0 ? Math.Max(0, Math.Min(1, (p - a).Dot(u) / u.LengthSquared)) : 0;
            return p.Distance(a + t * u);
        }

        // every sampled point of the band {-Tol - εT <= D̃ <= εT} (strictly inside by a margin, D̃ = S̃ - T̃ from point location,
        // mirrored for fill)
        // is within ProvenUncertainty of some segment, or in a box of CatchLineBandWithoutLine; returns the points sampled
        static int AssertBandCovered(TerrainSlopeDesign d, int samples, double margin)
        {
            var box = AxisAlignedBox2d.Empty;
            foreach (Vector3d v in d.SlopeSurface.Vertices()) box.Contain(v.xy);
            var rnd = new Random(7);
            double eT = d.TerrainVerticalUncertainty, tol = d.UsedTolerance;
            int inBand = 0;
            // (half uniform over the slope surface's box, half near the computed line, up to 3 times the largest bound away)
            var segs = d.CatchLines.SelectMany(l => Enumerable.Range(0, l.ProvenUncertainty.Count).Select(i => new[] { l.Points[i].xy, l.Points[(i + 1) % l.Points.Count].xy })).ToList();
            double reach = 3 * d.CatchLines.SelectMany(l => l.ProvenUncertainty).DefaultIfEmpty(0).Max();
            for (int k = 0; k < samples; ++k) {
                Vector2d p;
                if (k % 2 == 0 || segs.Count == 0)
                    p = new Vector2d(box.Min.x + rnd.NextDouble() * box.Width, box.Min.y + rnd.NextDouble() * box.Height);
                else {
                    Vector2d[] sg = segs[rnd.Next(segs.Count)];
                    double t = rnd.NextDouble(), a = 2 * Math.PI * rnd.NextDouble(), r = reach * rnd.NextDouble();
                    p = sg[0] + t * (sg[1] - sg[0]) + new Vector2d(r * Math.Cos(a), r * Math.Sin(a));
                }
                double zs = HeightAt(d.SlopeSurface, p), zt = HeightAt(d.Terrain, p);
                if (double.IsNaN(zs) || double.IsNaN(zt)) continue;
                // (cut S̃ - T̃, fill T̃ - S̃: the mirrored difference, as the overlay's)
                double D = d.Direction == TerrainSlopeDesign.Directions.Fill ? zt - zs : zs - zt;
                if (!(D > -tol - eT + margin && D < eT - margin)) continue;
                inBand++;
                bool covered = d.CatchLineBandWithoutLine.Any(b => b.Contains(p));
                for (int li = 0; li < d.CatchLines.Count && covered == false; ++li) {
                    CatchLine l = d.CatchLines[li];
                    for (int i = 0; i < l.ProvenUncertainty.Count && covered == false; ++i)
                        if (DistanceToSegment(p, l.Points[i].xy, l.Points[(i + 1) % l.Points.Count].xy) <= l.ProvenUncertainty[i]) covered = true;
                }
                Assert.True(covered, $"band point {p} (D̃ {D:G3}) is not within the proven bound of any segment");
            }
            return inBand;
        }

        [Fact]
        public void FlatTerrain_TheBandIsToleranceOverTheSlope()
        {
            // flat terrain, slope 1:1: |grad D̃| about 1, the band D̃ in [-Tol, 0] lies Tol inside the line (the slope side):
            // the bound is at least Tol (the band's far edge, where the true line may be), and within 2 Tol (one segment per
            // band piece: a piece's far corner along the line is farther from that segment than from the line, 1.52 Tol here)
            var d = Pit(1, 6, 0);
            Assert.True(d.Compute(), d.Message);
            var l = Assert.Single(d.CatchLines);
            Assert.Equal(l.Gradient.Count, l.ProvenUncertainty.Count);
            double umax = l.ProvenUncertainty.Max();
            output.WriteLine($"proven {l.ProvenUncertainty.Min():G4} .. {umax:G4}, estimate {l.Uncertainty.Max():G4}, MinFeatureSize {d.UsedMinFeatureSize:G3}");
            Assert.True(umax >= d.Tolerance * (1 - 1e-6), $"{umax}");
            Assert.True(umax <= 2 * d.Tolerance + d.UsedMinFeatureSize, $"{umax}");
            // (every segment's bound at least the snap's move, MinFeatureSize)
            Assert.All(l.ProvenUncertainty, u => Assert.True(u >= d.UsedMinFeatureSize));
            Assert.Empty(d.CatchLineBandWithoutLine);
            Assert.True(AssertBandCovered(d, 4000, 1e-9) > 100);
        }

        [Fact]
        public void TerrainVerticalUncertainty_WidensTheBandOnBothSides()
        {
            // εT = Tol: the band [-2 Tol, Tol], up to 2 Tol from the line on the slope side
            var d = Pit(1, 6, 0);
            d.TerrainVerticalUncertainty = d.Tolerance;
            Assert.True(d.Compute(), d.Message);
            double umax = d.CatchLines.SelectMany(l => l.ProvenUncertainty).Max();
            Assert.True(umax >= 2 * d.Tolerance * (1 - 1e-6) && umax <= 4 * d.Tolerance + d.UsedMinFeatureSize, $"{umax}");
            Assert.True(AssertBandCovered(d, 4000, 1e-9) > 100);
        }

        [Fact]
        public void NearlyParallel_TheBoundGrowsAsOneOverTheGradient()
        {
            // the terrain rises with x at 0.8, the slope at 1: on the +x side |grad D̃| = 0.2, the band 0.05 / 0.2 = 0.25 wide
            var d = Pit(1, 3, 0.8);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            var l = Assert.Single(d.CatchLines);
            double umax = l.ProvenUncertainty.Max();
            output.WriteLine($"proven max {umax:G4}, estimate max {l.Uncertainty.Max():G4}");
            Assert.True(umax >= 0.25 * (1 - 1e-6) && umax <= 0.25 * 2 + d.UsedMinFeatureSize, $"{umax}");      // (1.81 x here)
            Assert.True(AssertBandCovered(d, 4000, 1e-9) > 100);
        }

        // the random wavy terrains of the catch-line stress tests: the band sampled against the bound
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        public void RandomTerrains_TheBandIsCovered(int seed, int it)
        {
            var d = TerrainSlopeCatchLineTests.RandomTerrainCase(seed, seed >= 2, it);
            if (d.Compute() == false) return;
            int n = AssertBandCovered(d, 3000, 1e-9);
            Assert.True(n > 50, $"{n}");
            output.WriteLine($"{n} band points, {d.CatchLineBandWithoutLine.Count} boxes without a line");
        }

        [Trait("Category", "Slow")]   // (the design in up to four rounds: the refinement of RequiredCatchLineAccuracy)
        [Fact]
        public void RequiredAccuracy_UsesTheProvenBound()
        {
            // nearly parallel on +x: the proven bound at Tolerance 0.05 is at least 0.25 there. Required 0.15: a smaller
            // Tolerance until the proven bound meets it
            var d = Pit(1, 3, 0.8);
            d.RequiredCatchLineAccuracy = 0.15;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.True(d.UsedTolerance < 0.05, $"{d.UsedTolerance}");
            Assert.All(d.CatchLines.SelectMany(l => l.ProvenUncertainty), u => Assert.True(u <= 0.15, $"{u}"));
        }

        // the behaviour change (2026-11-13): the estimate reaches 0.3 m on the nearly parallel side with the terrain's own
        // 0.04 m (its floor 0.04 / 0.2 = 0.2 m, the estimate met at a smaller Tolerance, which succeeded before), but the proven
        // bound cannot (the band's own width over the gradient, assigned per piece, stays above 0.3 m): CatchLineAccuracyNotReached
        // instead of a Success that claimed an accuracy the result did not have
        [Trait("Category", "Slow")]   // (the design three times: the refinement rounds of RequiredCatchLineAccuracy)
        [Fact]
        public void RequiredAccuracy_TheEstimateWouldPass_TheProvenBoundDoesNot()
        {
            var d = Pit(1, 3, 0.8);
            d.RequiredCatchLineAccuracy = 0.3; d.TerrainVerticalUncertainty = 0.04;
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.CatchLineAccuracyNotReached, d.Status);
            Assert.Equal(TerrainSlopeDesign.StatusKinds.NotCertified, d.StatusKind);
            // (the estimate alone would have been met: every segment's estimate at most 0.3; the proven bound is not)
            Assert.All(d.CatchLines.SelectMany(l => l.Uncertainty), u => Assert.True(u <= 0.3, $"{u}"));
            Assert.Contains(d.CatchLines.SelectMany(l => l.ProvenUncertainty), u => u > 0.3);
        }

        // numerics review F1: the band's slack holds the realization error of a stored height, 2 s e_xy + e_eval (TERRAIN.md
        // 5.4), on both sides. At UTM e_xy alone is about 1e-9 m, against 1e-13 m of 8 ulps of the heights
        [Fact]
        public void BandSlack_HoldsTheRealizationErrorAtUtm()
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(5.6e6 - 60, 3.5e5 - 60, 5.6e6 + 60, 3.5e5 + 60), 24, 24, (x, y) => 126);
            var toe = SlopeToe.Constant(TerrainSlopeCatchLineTests.Ngon(8, 10, 120, new Vector2d(5.6e6, 3.5e5), 0.1), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var d = new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = 0.05 };
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            // (a lower bound of the design's e_xy: the ulp of the largest output coordinate, which is below its maxXY)
            double m = d.SlopeSurface.Vertices().Max(v => Math.Max(Math.Abs(v.x), Math.Abs(v.y)));
            double exy = ExactInterval.Up(m) - m;
            Assert.True(d.CatchLineBandSlack >= 2 * 1 * exy * (1 - 1e-6), $"slack {d.CatchLineBandSlack} below 2 s e_xy {2 * exy}");
            Assert.True(d.CatchLines.SelectMany(l => l.ProvenUncertainty).Max() >= d.Tolerance * (1 - 1e-6));
        }

        // numerics review F2: a face whose plane the intervals do not decide (a terrain sliver, nz within its rounding) is
        // not dropped: its part of the band is all of it, the boxes of all its vertices
        [Fact]
        public void UndecidedPlane_TheWholeFaceCounts()
        {
            // (a sliver at mixed magnitudes, found by search: Orient2d decides it, the interval normal does not)
            var rnd = new Random(11);
            Vector3d b0 = default(Vector3d), b1 = default(Vector3d), b2 = default(Vector3d);
            bool found = false;
            for (int k = 0; k < 100000 && found == false; ++k) {
                b0 = new Vector3d(0.1 + rnd.NextDouble(), 0.3 + rnd.NextDouble(), 1.7);
                b1 = new Vector3d(1e8 + 0.3 + rnd.NextDouble(), 0.3 + 1e-8 * rnd.NextDouble(), 2.9);
                double t = 0.5 + 0.25 * rnd.NextDouble();
                b2 = new Vector3d(b0.x + t * (b1.x - b0.x), b0.y + t * (b1.y - b0.y), 2.3);
                int o2 = ExactPredicates.Orient2d(b0.xy, b1.xy, b2.xy);
                if (o2 == 0) continue;
                if (o2 < 0) { var x = b1; b1 = b2; b2 = x; }
                found = SlopeCatchLineBound.PlaneDecided(b0, b1, b2) == false;
            }
            Assert.True(found);
            var terrain = new DMesh3(false, false, false, true);
            terrain.AppendTriangle(terrain.AppendVertex(b0), terrain.AppendVertex(b1), terrain.AppendVertex(b2));
            var slope = new DMesh3(false, false, false, true);
            int s0 = slope.AppendVertex(new Vector3d(-10, -10, 2)), s1 = slope.AppendVertex(new Vector3d(3e8, -10, 2)), s2 = slope.AppendVertex(new Vector3d(-10, 3e8, 2));
            slope.SetTriangleGroup(slope.AppendTriangle(s0, s1, s2), 0);
            var o = new SlopeTerrainOverlay(slope, terrain, false, new Dictionary<int, int>(), null);
            Assert.True(o.Compute(), o.Problem);
            var f = Assert.Single(o.Faces);
            List<Vector2d> cs = SlopeCatchLineBound.Piece(o, 0, -1, 1);
            Assert.NotNull(cs);
            var box = AxisAlignedBox2d.Empty;
            foreach (Vector2d c in cs) box.Contain(c);
            foreach (Vector3d v in new[] { b0, b1, b2 })
                Assert.True(v.x >= box.Min.x && v.x <= box.Max.x && v.y >= box.Min.y && v.y <= box.Max.y, $"{v.xy} outside {box}");
        }

        // numerics review F3: Direction Auto with a composite moves a toe crossing's catch points by up to the partner snap's
        // radius, 1e-4 of the toes' size, after its own snap: the proven bound holds both
        [Fact]
        public void Auto_TheBoundHoldsThePartnerSnap()
        {
            var d = TerrainSlopeMixedTests.Make(0.3, 0, 0.4, TerrainSlopeDesign.Directions.Auto, Similarity.Identity);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            var toeBox = AxisAlignedBox2d.Empty;
            foreach (var p in d.Toes[0].Points) toeBox.Contain(p.xy);
            double snap = 1e-4 * toeBox.DiagonalLength;
            Assert.True(snap > d.UsedMinFeatureSize);
            Assert.NotEmpty(d.CatchLines);
            // (the partner snap moves a point already moved by its own snap: both add)
            Assert.All(d.CatchLines.SelectMany(l => l.ProvenUncertainty), u => Assert.True(u >= snap + d.UsedMinFeatureSize, $"{u} below the snaps {snap} + {d.UsedMinFeatureSize}"));
        }

        // the side of the band (review M4, M5): one slope triangle at z = 0 over one terrain triangle z = 0.1 x. Cut:
        // D̃ = S̃ - T̃ = -0.1 x, the band [lo, hi] = [-0.05, 0.01] is x in [-0.1, 0.5]; fill (mirrored): D̃ = T̃ - S̃ = 0.1 x, x in
        // [-0.5, 0.1]. And the band's limits: Tolerance and εT on the low side, εT on the high side, the slack on both
        [Theory]
        [InlineData(false, -0.1, 0.5)]
        [InlineData(true, -0.5, 0.1)]
        public void Piece_IsOnTheRightSide(bool fill, double x0, double x1)
        {
            var slope = new DMesh3(false, false, false, true);
            slope.SetTriangleGroup(slope.AppendTriangle(slope.AppendVertex(new Vector3d(-10, -10, 0)), slope.AppendVertex(new Vector3d(10, -10, 0)), slope.AppendVertex(new Vector3d(0, 10, 0))), 0);
            var terrain = new DMesh3(false, false, false, true);
            terrain.AppendTriangle(terrain.AppendVertex(new Vector3d(-20, -20, -2)), terrain.AppendVertex(new Vector3d(20, -20, 2)), terrain.AppendVertex(new Vector3d(0, 20, 0)));
            var o = new SlopeTerrainOverlay(slope, terrain, fill, new Dictionary<int, int>(), null);
            Assert.True(o.Compute(), o.Problem);
            var f = Assert.Single(o.Faces);
            List<Vector2d> cs = SlopeCatchLineBound.Piece(o, 0, -0.05, 0.01);
            double mn = cs.Min(c => c.x), mx = cs.Max(c => c.x);
            Assert.InRange(mn, x0 - 1e-9, x0 + 1e-9);
            Assert.InRange(mx, x1 - 1e-9, x1 + 1e-9);
            double lo, hi;
            SlopeCatchLineBound.Band(0.05, 0.01, 0.001, out lo, out hi);
            Assert.True(lo <= -0.061 && lo > -0.0611 && hi >= 0.011 && hi < 0.0111, $"{lo} {hi}");
        }

        // review finding 1: a hill behind the pit that the cone passes under (a component of D̃ < 0 not connected to the toe,
        // TERRAIN.md 5.5) is not the catch line: its band ring is cut off by faces with D̃ above the band, where D > 0. Before
        // the fix it was reported without a line, and RequiredCatchLineAccuracy failed where master succeeds
        [Fact]
        public void HillBehindThePit_IsNotACatchLine()
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-80, -80, 80, 80), 64, 64,
                (x, y) => 6 + 40 * Math.Max(0, 1 - Math.Sqrt((x - 40) * (x - 40) + y * y) / 8));
            var toe = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var d = new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = 0.05, HeightLimit = 80, RequiredCatchLineAccuracy = 2 };
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Empty(d.CatchLineBandWithoutLine);
            Assert.Equal(0.05, d.UsedTolerance);
        }

        // numerics re-check G1: the reach starts at the toe too. The square toe at z 0, its east side εT / 2 above the terrain
        // (z = -εT / 2 for x >= 7.5, a grid line), the rest well above it (the terrain falls 0.5 per unit to the west): no computed cut, so no
        // region, but the true terrain may be εT higher, and then a cut starts at the east side. That band is reported (a
        // box at x = 8); the reach from the region alone missed it
        [Fact]
        public void ToeJustAboveTheTerrain_TheBandAtTheToeIsReported()
        {
            double eT = 0.02;
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-80, -80, 80, 80), 64, 64, (x, y) => -eT / 2 - 0.5 * Math.Max(0, 7.5 - x));
            var toe = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            var d = new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = 0.05, HeightLimit = 80, TerrainVerticalUncertainty = eT };
            d.Compute();
            output.WriteLine($"{d.Status} {d.Message}: {d.CatchLines.Count} lines, {d.CatchLineBandWithoutLine.Count} boxes");
            Assert.Contains(d.CatchLineBandWithoutLine, b => b.Max.x >= 8 - 1e-9 && b.Min.x <= 8 + 1e-9 && b.Min.y < 8 && b.Max.y > -8);
            // (nothing reported far from that side: the west is 8 below the toe)
            Assert.DoesNotContain(d.CatchLineBandWithoutLine, b => b.Max.x < 0);
        }

        [Fact]
        public void DistanceUp_IsAnUpperBound()
        {
            var rnd = new Random(3);
            for (int k = 0; k < 2000; ++k) {
                Func<double> r = () => (rnd.NextDouble() - 0.5) * Math.Pow(10, rnd.Next(-3, 7));
                var p = new Vector2d(r(), r()); var a = new Vector2d(r(), r()); var b = k % 7 == 0 ? a : new Vector2d(r(), r());
                double d = DistanceToSegment(p, a, b), u = SlopeCatchLineBound.DistanceUp(p, a, b);
                double scale = Math.Max(p.Length, Math.Max(a.Length, b.Length));
                Assert.True(u >= d - 1e-12 * scale, $"{u} below {d}");
                Assert.True(u <= d + 1e-9 * scale, $"{u} far above {d}");
            }
        }

        [Fact]
        public void Div_RoundsOutwards()
        {
            var q = SlopeCatchLineBound.Div(new ExactInterval(1, 1), new ExactInterval(3, 3));
            Assert.True(q.Lo < 1.0 / 3 && q.Hi > 1.0 / 3);
            var q2 = SlopeCatchLineBound.Div(new ExactInterval(-2, 1), new ExactInterval(-4, -1));
            Assert.True(q2.Lo <= -1 && q2.Hi >= 2);
        }
    }
}
