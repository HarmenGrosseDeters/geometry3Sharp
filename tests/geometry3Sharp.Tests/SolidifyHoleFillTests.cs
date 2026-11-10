using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The non-adaptive dual contouring path of MeshWindingNumberSolidify fills the holes dual contouring leaves (non-manifold quads) with the fill checked
    /// exactly (WindingContouring.FinishHoles). Intersections the fill adds are known (FillIntersections) and make StopIfSelfIntersecting stop without a second
    /// check of the whole contour; the stop is the existing path: Compute() is true with StoppedOnSelfIntersections and the raw contour, which
    /// FinishPostProcess completes.
    /// </summary>
    public class SolidifyHoleFillTests
    {
        static MeshWindingNumberSolidify Run(DMesh3 m, int resolution, bool stop = true)
        {
            var so = new MeshWindingNumberSolidify(m) { GridResolution = resolution, Adaptive = false, StopIfSelfIntersecting = stop, KeepRawContour = true };
            Assert.True(so.Compute());
            return so;
        }

        // with StopIfSelfIntersecting, Compute() never hands back a contour with self-intersections as if it were finished: a stop has intersections
        // in the raw contour, a non-stop has none; intersections added by the fill are a stop
        [Fact]
        public void FillIntersections_AreAStop_NoStopMeansNone()
        {
            int stopped = 0, free = 0, holes = 0, fillIntersecting = 0;
            for (int seed = 1; seed <= 24; ++seed)
                foreach (int res in new[] { 24, 40 }) {
                    var so = Run(MeshRobustRepairTests.ThinPlates(seed), res);
                    if (so.NonManifoldTriangles > 0) holes++;
                    bool raw = MeshRepairMetrics.HasSelfIntersections(so.RawContour);
                    Assert.Equal(so.StoppedOnSelfIntersections, raw);
                    if (so.FillIntersections > 0) {
                        fillIntersecting++;
                        Assert.True(so.StoppedOnSelfIntersections, $"seed {seed}, res {res}: the fill added {so.FillIntersections} intersecting pairs");
                    }
                    if (so.StoppedOnSelfIntersections) stopped++; else free++;
                }
            Assert.True(holes >= 10, $"{holes} runs with holes to fill");
            Assert.True(stopped >= 1 && free >= 1, $"{stopped} stopped, {free} free");
        }

        // finishing a stopped contour gives what the run without stopping gives
        [Fact]
        public void FinishPostProcess_SameAsTheRunWithoutStopping()
        {
            for (int seed = 1; seed <= 6; ++seed) {
                var stopped = Run(MeshRobustRepairTests.ThinPlates(seed), 40);
                if (!stopped.StoppedOnSelfIntersections) continue;
                var full = Run(MeshRobustRepairTests.ThinPlates(seed), 40, stop: false);
                stopped.FinishPostProcess();
                Assert.Equal("", Invariance.Difference(full.Result, stopped.Result));
            }
        }
    }
}
