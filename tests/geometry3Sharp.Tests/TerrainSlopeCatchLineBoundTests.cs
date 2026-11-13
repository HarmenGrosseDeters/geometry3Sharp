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
