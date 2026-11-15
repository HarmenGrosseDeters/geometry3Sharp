using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue 804: a cast of a double to an integer type is unspecified for NaN and out-of-range values in C#; .NET 9 and
    /// later saturate (NaN gives 0), .NET Framework gives the "integer indefinite" value (int.MinValue, long.MinValue).
    /// The sites of the exact scope that can see such a value give the same integer on every runtime; these tests pin
    /// it. On .NET 9 and later the plain casts give these results already, so the tests decide on .NET Framework (the
    /// net48 test job: there they fail with the plain casts).
    /// </summary>
    public class SaturatingCastTests
    {
        [Theory]
        [InlineData(double.NaN, 0L)]
        [InlineData(double.PositiveInfinity, long.MaxValue)]
        [InlineData(double.NegativeInfinity, long.MinValue)]
        [InlineData(1e300, long.MaxValue)]
        [InlineData(-1e300, long.MinValue)]
        [InlineData(9223372036854775808.0, long.MaxValue)]          // 2^63
        [InlineData(-9223372036854775808.0, long.MinValue)]         // -2^63, in range
        [InlineData(9223372036854774784.0, 9223372036854774784L)]   // the largest double below 2^63
        [InlineData(-9223372036854777856.0, long.MinValue)]         // the next double below -2^63
        [InlineData(1.5, 1L)]
        [InlineData(-1.5, -2L)]
        [InlineData(-0.0, 0L)]
        [InlineData(-1e-300, -1L)]
        public void FloorToLongSaturating_TheSameOnEveryRuntime(double x, long expected)
        {
            Assert.Equal(expected, MathUtil.FloorToLongSaturating(x));
        }

        [Theory]
        [InlineData(double.NaN, 0)]
        [InlineData(double.PositiveInfinity, int.MaxValue)]
        [InlineData(double.NegativeInfinity, int.MinValue)]
        [InlineData(1e10, int.MaxValue)]
        [InlineData(-1e10, int.MinValue)]
        [InlineData(2147483647.5, int.MaxValue)]
        [InlineData(-2147483648.5, int.MinValue)]
        [InlineData(-2.5, -3)]
        public void FloorToIntSaturating_TheSameOnEveryRuntime(double x, int expected)
        {
            Assert.Equal(expected, MathUtil.FloorToIntSaturating(x));
        }

        // the cell of ExactMeshBoolean's crossing grid: clamped to [0, count - 1], NaN in cell 0
        [Theory]
        [InlineData(double.NaN, 0)]
        [InlineData(double.NegativeInfinity, 0)]
        [InlineData(double.PositiveInfinity, 9)]
        [InlineData(1e300, 9)]
        [InlineData(-1e300, 0)]
        [InlineData(-0.5, 0)]
        [InlineData(0.0, 0)]
        [InlineData(3.7, 3)]
        [InlineData(9.99, 9)]
        public void CrossingGridCell_TheSameOnEveryRuntime(double v, int expected)
        {
            Assert.Equal(expected, ExactMeshBoolean.CrossingGrid.cell(v, 0, 1, 10));
            // (an infinite cell width, which the grid's constructor puts in place of a zero or NaN width: cell 0)
            Assert.Equal(0, ExactMeshBoolean.CrossingGrid.cell(v, 0, double.PositiveInfinity, 10));
        }

        [Fact]
        public void CrossingGridCell_NaNOrigin_CellZero()
        {
            Assert.Equal(0, ExactMeshBoolean.CrossingGrid.cell(1, double.NaN, 1, 10));
            Assert.Equal(0, ExactMeshBoolean.CrossingGrid.cell(double.PositiveInfinity, double.PositiveInfinity, 1, 10));
        }

        // the reach bin of the slope design's terrain maxima: 0..bins, NaN in bin 0
        [Theory]
        [InlineData(double.NaN, 0)]
        [InlineData(-1.0, 0)]
        [InlineData(0.0, 0)]
        [InlineData(-0.0, 0)]
        [InlineData(0.25, 1)]
        [InlineData(1.0, 4)]
        [InlineData(1.01, 5)]
        [InlineData(1e300, 16)]
        [InlineData(double.PositiveInfinity, 16)]
        public void TerrainReachBin_TheSameOnEveryRuntime(double d, int expected)
        {
            Assert.Equal(expected, TerrainSlopeDesign.BinOf(d, 0.25, 16));
        }
    }
}
