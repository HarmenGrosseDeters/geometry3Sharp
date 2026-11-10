using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Fixed examples of the T3 campaign (seeds of Robust(clean)) for far parts: the repair stage's cleanups and tolerances relative to
    /// the diagonal of all triangles collapsed or eroded a part (RepairCampaignFindings F1b/F1c). Each scene is the printed case of
    /// its seed; CleanSceneProperties is the campaign's property (parts kept within 1e-3 of their volume, Apply() false only with
    /// ScaleDisparity, scale invariance bit for bit, determinism).
    /// </summary>
    public class RepairCampaignFarPartExamples
    {
        static RepairCampaignTests.Piece P(int kind, int exp, int rel, int axis, int gap, int far, int seed) =>
            new RepairCampaignTests.Piece { Kind = kind, Exp = exp, Rel = rel, Axis = axis, Gap = gap, Far = far, Thin = 3, Defect = 0, Seed = seed };

        static string Run(int shift, params RepairCampaignTests.Piece[] pieces) =>
            RepairCampaignTests.CleanSceneProperties(new RepairCampaignTests.Scene { Pieces = pieces, Chain = true }, shift);

        // kinds: 0 tet, 1 box, 2 sphere; rel: 0 far, 2 gap
        [Fact]
        public void F1c_TetraNextToHugeSphere_ErodedWithTrue_Seed_gvB1() =>
            Assert.Equal("", Run(6, P(0, -6, 0, 4, 2, 0, 792), P(0, 8, 2, 3, 14, 0, 176), P(2, 6, 0, 0, 2, 15, 734)));

        [Fact]
        public void F1c_SmallSphereNextToBox_ErodedWithTrue_Seed_geB1() =>
            Assert.Equal("", Run(-4, P(0, -2, 2, 5, 30, 0, 35), P(2, -6, 0, 4, 2, 1, 900), P(1, -5, 0, 0, 2, 16, 202)));

        [Fact]
        public void F1b_FalseReturnsAPartAtAFractionOfItsVolume_Seed_5KB1() =>
            Assert.Equal("", Run(10, P(2, 1, 2, 1, 17, 0, 152), P(0, 8, 0, 3, 2, 22, 934)));

        [Fact]
        public void F1b_SmallSphereBetweenBigOnes_Seed_qlB1() =>
            Assert.Equal("", Run(-3, P(2, 6, 2, 1, 24, 0, 14), P(2, -1, 2, 2, 13, 0, 807), P(2, 6, 0, 0, 2, 20, 866), P(2, 7, 0, 0, 2, 7, 121)));
    }
}
