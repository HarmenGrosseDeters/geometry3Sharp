// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A sliver of the composite's clipped slope region (docs/design/TERRAIN.md §11, the clipped-region item, face 2): a
    /// clipped triangle far thinner than MinFeatureSize at the catch line, whose support error is more than λ's bound
    /// covers, so the margin δ is raised. The current behaviour, pinned until the vertex-edge separation of the
    /// RoundToGrid item (Halperin and Packer 2002) repairs it: success in a second attempt.
    /// </summary>
    public class TerrainSlopeClippedRegionTests
    {
        // a wavy 32-gon (r 100 m) on waves of amplitude 30 m near the origin (TerrainSlopeBandRepairTests.WavyOnWaves)
        static TerrainSlopeDesign Make(Similarity s)
        {
            return TerrainSlopeBandRepairTests.WavyOnWaves(s, new Vector3d(0, 0, -340), n: 32, r: 100, amp: 30, sp: 8);
        }

        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(Similarity.Identity);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            return d;
        });

        // near the origin and in its 2^k scalings the first attempt fails on a clipped sliver (an altitude far below
        // MinFeatureSize), δ is raised by 8 and the second attempt succeeds. At UTM, rotated and at scale 1e3 or 1e-3 the
        // sliver rounds away (one attempt). In "rotated, scale 1e-3, utm" the design (20 cm across) is near that frame's
        // MinFeatureSize (1.9 mm): 16 triangles below it fail the first attempt, with no room for the band merge (its
        // radius is r), another cause (TERRAIN.md 5.8.1). Every frame succeeds with a valid composite; the 2^k frames are
        // bit for bit
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void WavyToeNearTheOrigin_ClippedSliverRaisesTheMargin(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var d = Make(s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeCatchLineTests.AssertCatchLines(d, false);
            TerrainSlopeCatchLineTests.AssertComposite(d);
            var r = reference.Value;
            Assert.Equal(r.CatchLines.Count, d.CatchLines.Count);
            if (s.IsExact) {
                Assert.Equal(2, d.Attempts);
                Assert.Equal(8 * 1e-6, d.UsedSlopeMargin, 15);
                // (the first attempt's failing triangle is a sliver of the clipped region, below the final MinFeatureSize)
                Assert.True(d.AttemptProblemAltitudes.Count >= 1 && d.AttemptProblemAltitudes[0].Min() < d.UsedMinFeatureSize,
                            string.Join(" ", d.AttemptProblemAltitudes.Select(a => string.Join(",", a))) + " " + d.UsedMinFeatureSize);
                Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, s)));
                Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, s)));
            } else {
                Assert.Equal(s.Name.StartsWith("rotated, scale 1e-3, utm") ? 2 : 1, d.Attempts);
            }
        }
    }
}
