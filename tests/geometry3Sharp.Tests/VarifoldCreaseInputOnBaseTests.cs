using System.Reflection;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The sharp-feature pass on an unwelded soup (docs/design/VARIFOLD_CREASE_INPUT.md). Written against the API of the base
    /// (the switch is set by reflection), so it compiles there and shows the defect: on the base the pass finds no crease in a
    /// soup (every edge has one triangle) and recovers nothing.
    /// </summary>
    public class VarifoldCreaseInputOnBaseTests
    {
        [Fact]
        public void FeaturePass_UnweldedSoup_RecoversTheConcaveCrease()
        {
            DMesh3 soup = VarifoldCreaseInputTests.Unweld(AlphaWrapFeaturePassTests.LShape());
            var w = new MeshAlphaWrap(soup) { RelativeAlpha = 60, RelativeOffset = 600 };
            Assert.True(w.Compute(), w.Error);
            var pass = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), soup, w.UsedAlpha, w.UsedOffset);
            FieldInfo f = typeof(AlphaWrapFeaturePass).GetField("VarifoldCreases", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(pass, true);
            Assert.True(pass.Compute());
            Assert.True(pass.InputCreases > 0 && pass.CreaseSnaps > 0, $"creases {pass.InputCreases}, snaps {pass.CreaseSnaps}");
            double cov = AlphaWrapFeaturePassTests.ConcaveCoverage(pass.Mesh, w.UsedOffset, Similarity.Identity);
            Assert.True(cov > 0.8, $"concave crease coverage {cov}");
            AlphaWrapFeaturePassTests.AssertGuarantees(pass.Mesh, soup, w.UsedOffset, "unwelded soup");
        }
    }
}
