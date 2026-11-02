using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // (its own file: it needs MeshAlphaWrap.ForceProjectionFallback, so the other new alpha wrap tests still compile
    // against the library of the base branch in the "New tests on base" job)
    public class AlphaWrapFallbackTests
    {
        [Fact]
        public void Wrap_ProjectionFallback_KeepsTheGuaranteesAndIsCounted()
        {
            // the fallback of rule 2 (the projection of the clamped end of the dual edge, the old placement) is taken
            // for every rule 2 point here: the wrap keeps its guarantees, every use is counted, and on this input the
            // old placement gives near-coincident Steiner points again (what the projection of the own circumcenter fixes)
            DMesh3 m = AlphaWrapTests.Ellipsoid(5, 0.3, 0.45);
            var w = new MeshAlphaWrap(m) { RelativeAlpha = 150, RelativeOffset = 1500, ForceProjectionFallback = true };
            Assert.True(w.Compute(), w.Error);
            AlphaWrapTests.CheckWrap(w, AlphaWrapTests.Vertices(m));
            Assert.Equal(w.Counts["steiner_projection"] + w.Counts["rejected_rule2"], w.Counts["projection_fallbacks"]);
            Assert.True(w.Counts["projection_fallbacks"] > 0);
            Assert.True(AlphaWrapTests.MinSteinerSeparation(w.Triangulation) < 0.01 * w.UsedOffset);
        }
    }
}
