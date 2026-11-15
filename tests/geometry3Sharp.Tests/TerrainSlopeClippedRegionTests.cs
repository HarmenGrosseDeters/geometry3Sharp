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
    public class TerrainSlopeClippedRegionTests : IClassFixture<TerrainSlopeClippedRegionTests.ReferenceFixture>
    {
        // (the shared reference is built by a class fixture, before the first test: its cost, about 5 s, is no test's, whichever row runs first)
        public sealed class ReferenceFixture { public ReferenceFixture() { var d = TerrainSlopeClippedRegionTests.reference.Value; } }


        // a wavy 48-gon (r 150 m) on waves of amplitude 30 m near the origin (TerrainSlopeBandRepairTests.WavyOnWaves)
        // (first the 32-gon of r 100 m; since the composite's snap reserves Tolerance / 4, its finer facets have the sliver
        // in this one)
        static TerrainSlopeDesign Make(Similarity s)
        {
            return TerrainSlopeBandRepairTests.WavyOnWaves(s, new Vector3d(0, 0, -340), amp: 30);
        }

        internal static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Make(Similarity.Identity);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            return d;
        });

        // near the origin and in its 2^k scalings a clipped sliver (an altitude far below MinFeatureSize, cut from a realized
        // needle) fails the region check. Until #567 that raised δ by 8 and the second attempt succeeded; now the failing
        // triangle names its realized parent, the band merge collapses the parent's short edge on the next realization, and
        // the first margin succeeds (TERRAIN.md 5.5). At UTM, rotated and at scale 1e3 or 1e-3 the sliver rounds away (one
        // attempt). In "rotated, scale 1e-3, utm" the design (30 cm across) is near that frame's MinFeatureSize (2.3 mm at the
        // first margin): triangles of the clipped region fail the first two attempts, with no room for the band merge (its
        // radius is r), another cause (TERRAIN.md 5.8.1). Every frame succeeds with a valid composite; the 2^k frames are bit
        // for bit
        // The frame "rotated, scale 1e-3, utm" (a design of centimetres at UTM coordinates: MinFeatureSize 1.5 cm, three builds by design, TERRAIN.md
        // 5.8.1, #696) costs 2-3 times the other frames and runs in the Nightly test WavyToeNearTheOrigin_ClippedSliverIsMerged_ExtremeFrame: the run is the check
        // Slow: 2.2-5.0 s a row alone (two designs of 48 vertices on waves, three Computes of the frame, bit for bit in the 2^k frames)
        [Trait("Category", "Slow")]
        [Theory]
        [MemberData(nameof(Similarity.NamesExceptExtremeFrame), MemberType = typeof(Similarity))]
        public void WavyToeNearTheOrigin_ClippedSliverIsMerged(string name) => ClippedSliverIsMerged(name);

        [Trait("Category", "Nightly")]
        [Fact]
        public void WavyToeNearTheOrigin_ClippedSliverIsMerged_ExtremeFrame() => ClippedSliverIsMerged(Similarity.RotatedUtmMillimetre.Name);

        static void ClippedSliverIsMerged(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var d = Make(s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeCatchLineTests.AssertCatchLines(d, false);
            TerrainSlopeCatchLineTests.AssertComposite(d);
            TerrainSlopeCatchLineTests.AssertRegionNeverSteeper(d);
            var r = reference.Value;
            Assert.Equal(r.CatchLines.Count, d.CatchLines.Count);
            if (s.IsExact) {
                Assert.Equal(1, d.Attempts);
                Assert.Equal(1e-6, d.UsedSlopeMargin, 15);
                // (by the merge of the realized needle the sliver was cut from)
                Assert.True(d.RegionNeedles > 0);
                Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, s)));
                Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, s)));
            } else {
                Assert.Equal(s.Name.StartsWith("rotated, scale 1e-3, utm") ? 3 : 1, d.Attempts);
            }
        }

        // the 32-gon (r 100 m) this test had first: success with valid catch lines and composite in every frame but
        // "rotated, scale 1e-3, utm", where the design (20 cm across) is near the frame's MinFeatureSize and the composite
        // fails honestly (CompositeFailed); the local frame succeeds there
        [Theory]
        // (near the origin and at UTM only, to keep the suite's time: the other frames are the retuned tests')
        [InlineData("identity")]
        [InlineData("utm")]
        public void WavyToe32_Succeeds(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var d = TerrainSlopeBandRepairTests.WavyOnWaves(s, new Vector3d(0, 0, -340), n: 32, r: 100, amp: 30, sp: 8);
            if (name == Similarity.RotatedUtmMillimetre.Name) {
                if (d.Compute() == false) Assert.Equal(TerrainSlopeDesign.Statuses.CompositeFailed, d.Status);
                d = TerrainSlopeBandRepairTests.WavyOnWaves(s, new Vector3d(0, 0, -340), n: 32, r: 100, amp: 30, sp: 8);
                d.LocalFrame = true;
            }
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            if (d.LocalFrame) TerrainSlopeLocalFrameTests.InputsToLocal(d);
            TerrainSlopeCatchLineTests.AssertCatchLines(d, false);
            TerrainSlopeCatchLineTests.AssertComposite(d);
        }
    }
}
