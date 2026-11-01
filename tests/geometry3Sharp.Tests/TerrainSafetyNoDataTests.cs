using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The NoData guard of TerrainSafetyLines (ROADMAP, terrain 1a; ROBUSTNESS_SWEEP S26): a single vertex at a nodata
    /// value next to a source refined until memory ran out. Outliers near the sources are refused with a status, and the
    /// refinement limit counts the band's 3D area.
    /// </summary>
    public class TerrainSafetyNoDataTests
    {
        // n x n cells of 1 m, two triangles each
        static DMesh3 Grid(int n, Func<int, int, double> h)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) m.AppendVertex(new Vector3d(i, j, h(i, j)));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = i + (n + 1) * j, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
                }
            return m;
        }

        static List<List<Vector3d>> Crest() =>
            new List<List<Vector3d>> { new List<Vector3d> { new Vector3d(20, 5, 0), new Vector3d(20, 35, 0) } };

        const int SpikeId = 22 + 41 * 20;     // vertex (22, 20), 2 m from the source line

        [Theory]
        [InlineData(TerrainSafetyLines.Modes.Horizontal)]
        [InlineData(TerrainSafetyLines.Modes.Guaranteed)]
        [InlineData(TerrainSafetyLines.Modes.AlongSurface)]
        public void NoDataSpike_NearSource_IsRefusedFast(TerrainSafetyLines.Modes mode)
        {
            // the scenario that ran out of memory: z = -9999 at one vertex next to the crest
            DMesh3 m = Grid(40, (i, j) => (i == 22 && j == 20) ? -9999 : 0);
            Assert.Equal(-9999, m.GetVertex(SpikeId).z);
            var sw = Stopwatch.StartNew();
            var sl = new TerrainSafetyLines(m, Crest(), 5) { Mode = mode };
            Assert.False(sl.Compute());
            Assert.True(sw.ElapsedMilliseconds < 5000, sw.ElapsedMilliseconds + " ms");
            Assert.Equal(TerrainSafetyLines.Statuses.OutlierVertex, sl.Status);
            Assert.Equal(new[] { SpikeId }, sl.OutlierVertices);
            Assert.Null(sl.RefinedTerrain);
            Assert.Empty(sl.Lines);
            // named as nodata: its own status
            sl.NoDataValue = -9999;
            Assert.False(sl.Compute());
            Assert.Equal(TerrainSafetyLines.Statuses.NoDataVertex, sl.Status);
            Assert.Equal(new[] { SpikeId }, sl.OutlierVertices);
        }

        [Fact]
        public void Outliers_FarFromTheSources_DoNotMatter_AndRealSlopesPass()
        {
            // a spike 15 m from the crest (reach 5 m) and a wall of slope 50 near it
            DMesh3 m = Grid(40, (i, j) => (i == 37 && j == 20) ? -9999 : (i >= 23 && i <= 24 ? 50 * (i - 23) : (i > 24 ? 50 : 0)));
            var sl = new TerrainSafetyLines(m, Crest(), 5) { NoDataValue = -9999 };
            Assert.True(sl.Compute());
            Assert.Equal(TerrainSafetyLines.Statuses.Success, sl.Status);
            Assert.Empty(sl.OutlierVertices);
            Assert.NotEmpty(sl.Lines);
            // the same wall steeper than the limit is an outlier (its upper or lower end)
            sl.MaxTerrainSlope = 40;
            Assert.False(sl.Compute());
            Assert.Equal(TerrainSafetyLines.Statuses.OutlierVertex, sl.Status);
            Assert.NotEmpty(sl.OutlierVertices);
            Assert.All(sl.OutlierVertices, v => Assert.InRange(m.GetVertex(v).x, 23, 24));
            // no check
            sl.MaxTerrainSlope = double.PositiveInfinity;
            Assert.True(sl.Compute());
            // invalid limits
            foreach (double bad in new[] { 0.0, -1, double.NaN }) {
                sl.MaxTerrainSlope = bad;
                Assert.False(sl.Compute());
                Assert.Equal(TerrainSafetyLines.Statuses.InvalidInput, sl.Status);
            }
        }

        // the limit counts the 3D area of the band: on a plane of slope 20 the estimate is about sqrt(401) = 20 times the
        // flat one, and the real count of refined triangles within 10 times of it (it was 187 times with the horizontal band)
        [Fact]
        public void SteepTerrain_EstimateCountsThe3DArea()
        {
            DMesh3 flat = Grid(40, (i, j) => 0), steep = Grid(40, (i, j) => 20.0 * i);
            var f = new TerrainSafetyLines(flat, Crest(), 5);
            var st = new TerrainSafetyLines(steep, Crest(), 5);
            double edge = Math.Min(Math.Sqrt(8 * 5 * 0.01), 5);
            double ef = f.expected_refined_triangles(5, edge), es = st.expected_refined_triangles(5, edge);
            Assert.InRange(es / ef, 19.9, 20.1);
            Assert.True(st.Compute());
            double real = st.RefinedTerrain.TriangleCount;
            Assert.InRange(real / es, 1, 10);
        }

        [Fact]
        public void Status_OfTheOtherFailures()
        {
            DMesh3 m = Grid(10, (i, j) => 0);
            var sl = new TerrainSafetyLines(m, new List<List<Vector3d>> { new List<Vector3d> { new Vector3d(5, 2, 0), new Vector3d(5, 8, 0) } }, 2);
            Assert.True(sl.Compute());
            Assert.Equal(TerrainSafetyLines.Statuses.Success, sl.Status);
            sl.SafetyDistance = double.NaN;
            Assert.False(sl.Compute());
            Assert.Equal(TerrainSafetyLines.Statuses.InvalidInput, sl.Status);
            // a tolerance so small that the refinement exceeds MaxRefinedTriangles
            sl.SafetyDistance = 2;
            sl.Tolerance = 1e-12;
            Assert.False(sl.Compute());
            Assert.Equal(TerrainSafetyLines.Statuses.TooLarge, sl.Status);
        }
    }
}
