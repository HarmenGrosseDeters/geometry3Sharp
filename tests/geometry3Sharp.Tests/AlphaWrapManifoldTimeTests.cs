using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue #1607 with the API of the library before the fix (no counters, no switch): the manifold step at the stars of the box corners
    /// took 12 s for 40000 Steiner points on the point-limit sphere (quadratic in the points: 1.9 s at 20000, 50 s at 80000), 0.17 s now.
    /// The bound is on the phase time (PhaseMilliseconds["manifold"]), 17 times the time now.
    /// </summary>
    public class AlphaWrapManifoldTimeTests
    {
        [Fact]
        public void PointLimit_ManifoldPhase_IsFastAtLargeBoxCornerStars()
        {
            DMesh3 input = MeshRepairIssueCasesTests.Sphere(Vector3d.Zero, 1, 6);
            var w = new MeshAlphaWrap(input) { RelativeAlpha = 1e5, RelativeOffset = 1e6, MaxSteinerPoints = 40000 };
            w.Compute();
            Assert.Equal(MeshAlphaWrap.WrapStatus.PointLimitReached, w.Status);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            Assert.True(w.Counts["manifold_flips"] > 50000, $"{w.Counts["manifold_flips"]} moves");
            long ms = w.PhaseMilliseconds["manifold"];
            Assert.True(ms < 3000, $"the manifold step took {ms} ms");
        }
    }
}
