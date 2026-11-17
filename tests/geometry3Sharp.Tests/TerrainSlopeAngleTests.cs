using System;
using g3;
using gs;
using g3.CoreMathPort;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The slope angle of the terrain slope design is the correctly rounded CoreMath.Acos (owner 2026-11-17; it was the composed
    /// AcosByAtan2, one ulp off in 12.4 % of the arguments and not monotone in the last ulp)
    /// </summary>
    public class TerrainSlopeAngleTests
    {
        static double Next(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + (x >= 0 ? 1 : -1));

        [Fact]
        public void SlopeAngle_IsCoreMathAcos_BitForBit_AndClamps()
        {
            ulong s = 7;
            for (int i = 0; i < 5000; ++i) {
                s = s * 6364136223846793005UL + 1442695040888963407UL;
                double r = ((s >> 11) * (1.0 / 9007199254740992.0)) * 2 - 1;
                Assert.Equal(BitConverter.DoubleToInt64Bits(CoreMath.Acos(r)), BitConverter.DoubleToInt64Bits(TerrainSlopeDesign.SlopeAngle(r)));
            }
            Assert.Equal(0.0, TerrainSlopeDesign.SlopeAngle(1.5));
            Assert.Equal(Math.PI, TerrainSlopeDesign.SlopeAngle(-7));
        }

        [Fact]
        public void FanAngle_IsCoreMathAcosOverThreeQuarters_AndSlopeAngleKeepsNaN()
        {
            double c = 0.23144537602828275;
            Assert.Equal(BitConverter.DoubleToInt64Bits(CoreMath.Acos(c) / 0.75), BitConverter.DoubleToInt64Bits(TerrainSlopeDesign.FanAngle(c)));
            Assert.NotEqual(BitConverter.DoubleToInt64Bits(CoreMath.AcosByAtan2(c) / 0.75), BitConverter.DoubleToInt64Bits(TerrainSlopeDesign.FanAngle(c)));
            Assert.True(double.IsNaN(TerrainSlopeDesign.SlopeAngle(double.NaN)));
        }

        [Fact]
        public void SlopeAngle_IsMonotone_AtTheDocumentedNeighbours()
        {
            // 0.23144537602828275 and its next double: AcosByAtan2 gave 1.3372331895174001 then 1.3372331895174003 (a rise)
            double c0 = 0.23144537602828275, c1 = Next(c0);
            Assert.True(TerrainSlopeDesign.SlopeAngle(c1) <= TerrainSlopeDesign.SlopeAngle(c0));
            Assert.Equal(1.3372331895174003, TerrainSlopeDesign.SlopeAngle(c0));     // (the composed version's ...001 was one ulp low)
            Assert.True(TerrainSlopeDesign.SlopeAngle(c1) <= 1.3372331895174003);
        }
    }
}
