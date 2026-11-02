using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // (its own file: these use MeshAlphaWrap.RefineNonManifold and preferred_flip, so the other new alpha wrap tests
    // still compile against the library of the base branch in the "New tests on base" job)
    public class AlphaWrapNonManifoldTests
    {
        [Fact]
        public void Wrap_RefineNonManifoldOff_ManifoldStepAlone()
        {
            // with the refinement off the manifold step alone makes the surface manifold, as before: closed, manifold, no
            // self-intersections; its moved cells give triangles outside the angle bound on this input (9), and the result
            // even reaches beyond offset + 2 alpha from the input (0.2041 against 0.2032, so CheckWrap's band fails);
            // with it on, neither
            DMesh3 m = AlphaWrapTests.RandomSoup(42);
            var off = new MeshAlphaWrap(m) { RelativeAlpha = 20, RelativeOffset = 200, RefineNonManifold = false };
            Assert.True(off.Compute(), off.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(off.Result));
            Assert.False(off.Counts.ContainsKey("pinch_points"));
            Assert.True(off.Counts["manifold_flips"] > 0);
            Assert.ThrowsAny<Exception>(() => AlphaWrapTests.AssertAngleBoundOnAllTriangles(off, "off"));

            var on = new MeshAlphaWrap(m) { RelativeAlpha = 20, RelativeOffset = 200 };
            Assert.True(on.RefineNonManifold);
            Assert.True(on.Compute(), on.Error);
            Assert.True(on.Counts["pinch_points"] > 0 && on.Counts["pinch_rounds"] > 0);
            Assert.True(on.Counts["manifold_flips"] < off.Counts["manifold_flips"]);
            AlphaWrapTests.AssertAngleBoundOnAllTriangles(on, "on");
        }

        [Fact]
        public void Wrap_RefineNonManifold_PointLimitAndCancel()
        {
            // the extra points count against the point limit: one point above what the wrap needs without the refinement ends
            // in the first round at non-manifold spots, with PointLimitReached and its guarantees (closed, manifold, enclosing)
            DMesh3 m = AlphaWrapTests.RandomSoup(42);
            var plain = new MeshAlphaWrap(m) { RelativeAlpha = 20, RelativeOffset = 200, RefineNonManifold = false };
            Assert.True(plain.Compute(), plain.Error);
            long needed = plain.Counts["steiner_points"];
            var limited = new MeshAlphaWrap(m) { RelativeAlpha = 20, RelativeOffset = 200, MaxSteinerPoints = (int)needed + 1 };
            Assert.False(limited.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.PointLimitReached, limited.Status);
            Assert.Equal(needed + 1, limited.Counts["steiner_points"]);
            Assert.Equal(1, limited.Counts["pinch_points"]);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(limited.Result));

            // cancelled once the first refinement has ended: in the first round of the refinement at non-manifold spots,
            // before it reports its counts
            var cancelled = new MeshAlphaWrap(m) { RelativeAlpha = 20, RelativeOffset = 200 };
            cancelled.CancelF = () => cancelled.PhaseMilliseconds.ContainsKey("wrap");
            Assert.False(cancelled.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.Cancelled, cancelled.Status);
            Assert.False(cancelled.Counts.ContainsKey("pinch_rounds"));
        }

        [Fact]
        public void ManifoldStep_CandidateOrder_ScaleFree()
        {
            // no box corner first, then joining inside components, then the smaller volume; volumes far above 2^53
            // (coordinates of a few hundred thousand) are still ordered (the old score volume / (1 + volume) tied at 1)
            Assert.True(MeshAlphaWrap.preferred_flip(false, false, 5, true, true, 1));
            Assert.True(MeshAlphaWrap.preferred_flip(false, true, 5, false, false, 1));
            Assert.True(MeshAlphaWrap.preferred_flip(false, true, 1e17, false, true, 2e17));
            Assert.False(MeshAlphaWrap.preferred_flip(false, true, 2e17, false, true, 1e17));
            Assert.False(MeshAlphaWrap.preferred_flip(false, true, 1e17, false, true, 1e17));
            Assert.True(MeshAlphaWrap.preferred_flip(false, true, 1e-300, false, true, 1.5e-300));
        }
    }
}
