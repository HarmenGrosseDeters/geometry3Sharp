using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The regression of #805 (the prefilter of SlopeMap.InsertPiece and the error of the approximate corners), end to end
    /// through TerrainSlopeDesign's public API only, so that it compiles against the library before the fix and fails there
    /// (the unit tests of the prefilter's decision are in TerrainSlopeDesignTests)
    /// </summary>
    public class TerrainSlopePrefilterRegressionTests
    {
        static List<Vector3d> TerrainSlopeDesignTestsNgon(int n, double r, double z, double phase) =>
            Enumerable.Range(0, n).Select(i => new Vector3d(r * Math.Cos(phase + 2 * Math.PI * i / n), r * Math.Sin(phase + 2 * Math.PI * i / n), z)).ToList();

        // regular polygons whose realization merged positions within 64 ulps (found by a sweep over random regular polygons;
        // they were the fixture of the merge's lowering): the prefilter of InsertPiece skipped faces smaller than the error of
        // their approximate corners (at an apex where many planes meet) by that approximate distance, which left clusters of
        // map vertices and is not the same at every scale. Now, in every frame, the design is certified and nothing is merged
        // (none in 1500 random regular polygons: the exact envelope has no such clusters), and the 2^k frames give the same
        // surface bit for bit. The merge's lowering stays tested on the wavy toe (TerrainSlopeBandMergeTests)
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void RegularPolygons_NoMergeAfterPrefilterFix_InEveryFrame(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            foreach (var c in new[] { (16, 27.33760196358785, 0.4471245046924448, 2.280296943513815), (22, 12.984759201288576, 0.5293125866583142, 3.0107992275202644) }) {
                Func<Similarity, TerrainSlopeDesign> make = (f) => {
                    var toe = SlopeToe.Constant(TerrainSlopeDesignTestsNgon(c.Item1, c.Item2, 0, c.Item3).Select(p => f.Apply(p)).ToList(), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(c.Item4));
                    return new TerrainSlopeDesign() { Toes = { toe }, HeightLimit = f.Apply(new Vector3d(0, 0, c.Item2 * 0.6)).z, Tolerance = f.ApplyLength(0.05) };
                };
                var d = make(s);
                Assert.True(d.Compute(), d.Status + " " + d.Message);
                TerrainSlopeDesignTests.AssertSurface(d);
                TerrainSlopeDesignTests.AssertO1(d);
                Assert.Equal(0, d.MergedPositions);
                if (s.IsExact) {
                    var r = make(Similarity.Identity);
                    Assert.True(r.Compute());
                    Assert.Equal("", Invariance.Difference(r.SlopeSurface, Invariance.MappedBack(d.SlopeSurface, s)));
                }
            }
        }
    }
}
