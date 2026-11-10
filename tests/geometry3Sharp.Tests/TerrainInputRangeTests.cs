using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> the supported input range of TerrainVolumeDifference and TerrainOffset (docs/QA.md 2.5a): outside, Compute() is false; inside, correct </summary>
    public class TerrainInputRangeTests
    {
        static DMesh3 Terrain(double s, double dz)
        {
            var m = new DMesh3(); int n = 9;
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i)
                    m.AppendVertex(new Vector3d(s * i, s * j, s * (0.3 * Math.Sin(i * 0.9) * Math.Cos(j * 0.7) + dz)));
            for (int j = 0; j < n - 1; ++j)
                for (int i = 0; i < n - 1; ++i) {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
                }
            return m;
        }

        // before: Success with zero volumes where the extent is below 2^-25 or above 2^260
        [Theory]
        [InlineData(-1000)]
        [InlineData(-300)]
        [InlineData(-40)]
        [InlineData(-30)]
        [InlineData(260)]
        [InlineData(500)]
        [InlineData(1000)]
        public void VolumeDifference_OutsideRange_False(int e)
        {
            double s = Math.Pow(2, e);
            var t = new TerrainVolumeDifference(Terrain(s, 0), Terrain(s, 0.1));
            Assert.False(t.Compute());
        }

        [Theory]
        [InlineData(-5)]
        [InlineData(0)]
        [InlineData(50)]
        [InlineData(190)]
        public void VolumeDifference_InsideRange_Exact_Pow2(int e)
        {
            double s = Math.Pow(2, e);
            var t0 = new TerrainVolumeDifference(Terrain(1, 0), Terrain(1, 0.1));
            Assert.True(t0.Compute());
            var t = new TerrainVolumeDifference(Terrain(s, 0), Terrain(s, 0.1));
            Assert.True(t.Compute());
            Assert.Equal(t0.FillVolume, t.FillVolume / (s * s * s));
            Assert.Equal(t0.CutVolume, t.CutVolume / (s * s * s));
            Assert.InRange(t.FillVolume / (s * s * s), 6.399999, 6.400001);     // dz 0.1 over the area 64
        }

        [Fact]
        public void VolumeDifference_NotFinite_False()
        {
            DMesh3 bad = Terrain(1, 0);
            bad.SetVertex(3, new Vector3d(double.NaN, 0, 0));
            Assert.False(new TerrainVolumeDifference(bad, Terrain(1, 0.1)).Compute());
        }

        [Theory]
        [InlineData(-1000)]
        [InlineData(-30)]
        [InlineData(260)]
        [InlineData(300)]
        [InlineData(700)]
        public void Offset_OutsideRange_False_NoResult(int e)
        {
            double s = Math.Pow(2, e);
            var o = new TerrainOffset(Terrain(s, 0), 0.1 * s) { GridResolution = 64 };
            Assert.False(o.Compute());
            Assert.Null(o.Result);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(100)]
        [InlineData(190)]
        public void Offset_InsideRange_AtTheDistance(int e)
        {
            double s = Math.Pow(2, e);
            var o = new TerrainOffset(Terrain(s, 0), 0.1 * s) { GridResolution = 64 };
            Assert.True(o.Compute());
            Assert.True(Invariance.AllFinite(o.Result));
            Assert.InRange(o.Result.GetBounds().Center.z / s, 0.05, 0.15);
        }
    }
}
