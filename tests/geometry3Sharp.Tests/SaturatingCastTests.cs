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

        [Theory]
        [InlineData(double.NaN, 0, 0L)]
        [InlineData(double.PositiveInfinity, int.MaxValue, long.MaxValue)]
        [InlineData(double.NegativeInfinity, int.MinValue, long.MinValue)]
        [InlineData(1e300, int.MaxValue, long.MaxValue)]
        [InlineData(-1e300, int.MinValue, long.MinValue)]
        [InlineData(2.9, 2, 2L)]
        [InlineData(-2.9, -2, -2L)]         // (truncation toward zero, as the cast)
        [InlineData(-0.0, 0, 0L)]
        [InlineData(3e9, int.MaxValue, 3000000000L)]
        public void ToIntAndLongSaturating_TruncateAndSaturate(double x, int i, long l)
        {
            Assert.Equal(i, MathUtil.ToIntSaturating(x));
            Assert.Equal(l, MathUtil.ToLongSaturating(x));
        }

        // the sites outside the exact scope (#804): infinite and NaN coordinates give the net10 result everywhere
        [Fact]
        public void TriangleBinsGrid_InfiniteBounds_TwoBins()
        {
            // (ROBUSTNESS_SWEEP R36: a +Inf vertex of MeshInsertPolygon; net48 threw from the bin list's size)
            var grid = new TriangleBinsGrid2d(new AxisAlignedBox2d(0, 0, double.PositiveInfinity, 1), 16);
            Vector2d a = new Vector2d(0, 0), b = new Vector2d(1, 0), c = new Vector2d(0, 1);
            grid.InsertTriangle(0, ref a, ref b, ref c);
            Assert.Equal(new AxisAlignedBox2d(0, 0, double.PositiveInfinity, 1), grid.Bounds);
        }

        [Fact]
        public void IntegerBoxContain_NonFinite_Saturates()
        {
            var box = new AxisAlignedBox3i(new Vector3i(1, 1, 1), new Vector3i(2, 2, 2));
            box.Contain(new Vector3d(double.NaN, double.PositiveInfinity, double.NegativeInfinity));
            Assert.Equal(new Vector3i(0, 1, int.MinValue), box.Min);
            Assert.Equal(new Vector3i(2, int.MaxValue, 2), box.Max);
            var box2 = new AxisAlignedBox2i(new Vector2i(1, 1), new Vector2i(2, 2));
            box2.Contain(new Vector3d(double.NaN, 1e300, 0));
            Assert.Equal(new Vector2i(0, 1), box2.Min);
            Assert.Equal(new Vector2i(2, int.MaxValue), box2.Max);
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
