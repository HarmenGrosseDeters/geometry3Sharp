// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MinFeatureSize follows from the coordinates' resolution (docs/design/TERRAIN.md 5.4, 5.8.1): it scales with the
    /// input, so a toe of a few cm near the origin gives the result of the same toe in metres, scaled bit for bit.
    /// </summary>
    public class TerrainSlopeFeatureSizeTests
    {
        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = TerrainSlopeToeCrossingTests.Make(0.07, 0.123, 2.1, false, Similarity.Identity);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            return d;
        });

        // the 7-gon pit of r 11 m with a toe crossing (TerrainSlopeToeCrossingTests), with the default SlopeMargin, at
        // 2^-k: r 34 cm down to 1.3 mm
        [Theory]
        [InlineData(5)]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(13)]
        public void ToeOfAFewCentimetres_IsTheMetreResultScaled(int k)
        {
            double scale = Math.Pow(2, -k);
            var s = new Similarity($"scale 2^-{k}", scale, Matrix3d.Identity, Vector3d.Zero);
            var d = TerrainSlopeToeCrossingTests.Make(0.07, 0.123, 2.1, false, s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeCatchLineTests.AssertCatchLines(d, false);
            TerrainSlopeCatchLineTests.AssertComposite(d);
            var r = reference.Value;
            Assert.Equal(r.UsedMinFeatureSize * scale, d.UsedMinFeatureSize);
            Assert.Equal(r.UsedToeLowering * scale, d.UsedToeLowering);
            Assert.Equal("", Invariance.Difference(r.SlopeSurface, Invariance.MappedBack(d.SlopeSurface, s)));
            Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, s)));
            Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, s)));
            Assert.Equal(r.Volume * scale * scale * scale, d.Volume);
        }
    }
}
