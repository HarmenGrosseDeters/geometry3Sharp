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
    /// TERRAIN.md 5.7: the volumes between the terrain and the slope over the region, on the overlay. Checked against the
    /// exact value of a cone envelope over flat terrain, against TerrainVolumeDifference(Terrain, Composite) (an
    /// independent integration of the composite), per piece, under Similarity.All() and on reuse.
    /// </summary>
    public class TerrainSlopeVolumeTests
    {
        readonly ITestOutputHelper output;
        public TerrainSlopeVolumeTests(ITestOutputHelper output) { this.output = output; }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SquarePit_FlatTerrain_MatchesTheCone(bool fill)
        {
            // a square toe of side 16 at z 0, slope 1:1 (s = 1), flat terrain h = 6 above (cut) or below (fill): the region
            // reaches out to d = h / s; volume = int_0^{h/s} (h - s d) (P0 + 2 pi d) dd = P0 h^2 / (2 s) + pi h^3 / (3 s^2),
            // plan area = P0 h / s + pi (h / s)^2, slope area = plan area * sqrt(1 + s^2). The facets lie at most Tolerance
            // below the exact cone (cut), so the volume is at most Tolerance * plan area more
            double h = 6, s = 1, tol = 0.02, P0 = 4 * 16;
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 40, 40, (x, y) => fill ? -h : h);
            var toe = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1 / s));
            if (fill) { toe.FillStart = toe.CutStart; toe.CutStart = null; }
            var d = new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = tol, BuildComposite = true,
                                               Direction = fill ? TerrainSlopeDesign.Directions.Fill : TerrainSlopeDesign.Directions.Cut };
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            double V = P0 * h * h / (2 * s) + Math.PI * h * h * h / (3 * s * s);
            double A = P0 * h / s + Math.PI * (h / s) * (h / s);
            output.WriteLine($"volume {d.Volume:R} (cone {V:R}), plan area {d.PlanArea:R} ({A:R}), slope area {d.SlopeArea:R}");
            Assert.InRange(d.Volume, V - 1e-6 * V, V + tol * A * 1.01);
            Assert.InRange(d.PlanArea, A * (1 - 1e-3), A + (tol / s) * P0 * 1.5);
            Assert.InRange(d.SlopeArea, d.PlanArea * (Math.Sqrt(2) - 1e-3), d.PlanArea * Math.Sqrt(2) * (1 + 1e-3));
            // the pieces sum up
            Assert.Equal(d.Volume, d.Pieces.Sum(p => p.Volume), 9);
            Assert.All(d.Pieces, p => Assert.True(p.Volume > 0 && p.PlanArea > 0));
            // the composite's own volume against the terrain (an independent integration)
            AssertVolumesMatchComposite(d, fill);
        }

        internal static void AssertVolumesMatchComposite(TerrainSlopeDesign d, bool fill)
        {
            var tvd = new TerrainVolumeDifference(d.Terrain, d.Composite);
            Assert.True(tvd.Compute());
            double other = fill ? tvd.FillVolume : tvd.CutVolume, wrong = fill ? tvd.CutVolume : tvd.FillVolume;
            // (the composite's catch points are snapped within MinFeatureSize: their effect is far below 1e-6)
            Assert.True(Math.Abs(other - d.Volume) <= 1e-6 * Math.Max(1, d.Volume), $"volume {d.Volume:R} vs composite {other:R}");
            Assert.True(wrong <= 1e-6 * Math.Max(1, d.Volume), $"the other direction {wrong:R}");
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void RandomTerrains_MatchTheComposite(int seed, bool utm)
        {
            int n = 0;
            for (int it = 0; it < 6; ++it) {
                var d = TerrainSlopeCatchLineTests.RandomTerrainCase(seed, utm, it);
                if (d.Compute() == false && d.Status != TerrainSlopeDesign.Statuses.NoDaylight) continue;
                if (d.Composite == null) continue;
                bool fill = d.Direction == TerrainSlopeDesign.Directions.Fill;
                output.WriteLine($"{it}: fill {fill} volume {d.Volume:R} plan {d.PlanArea:R}");
                Assert.True(d.Volume > 0);
                AssertVolumesMatchComposite(d, fill);
                Assert.Equal(d.Volume, d.Pieces.Sum(p => p.Volume), 6);
                n++;
            }
            Assert.True(n > 0);
        }

        [Fact]
        public void Volumes_AreReusable()
        {
            var d = TerrainSlopeCatchLineTests.RandomTerrainCase(2, true, 0);
            d.Compute();
            double v = d.Volume, a = d.PlanArea, sa = d.SlopeArea;
            var per = d.Pieces.Select(p => p.Volume).ToList();
            d.Compute();
            Assert.Equal(v, d.Volume); Assert.Equal(a, d.PlanArea); Assert.Equal(sa, d.SlopeArea);
            Assert.Equal(per, d.Pieces.Select(p => p.Volume).ToList());
            // a failing run leaves none
            d.Tolerance = -1;
            Assert.False(d.Compute());
            Assert.Equal(0, d.Volume);
        }
    }
}
