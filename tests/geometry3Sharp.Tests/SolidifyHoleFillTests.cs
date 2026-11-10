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
        readonly Xunit.Abstractions.ITestOutputHelper log;
        public SolidifyHoleFillTests(Xunit.Abstractions.ITestOutputHelper output) { log = output; }

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
            for (int seed = 1; seed <= 8; ++seed)
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
            Assert.True(holes >= 5, $"{holes} runs with holes to fill");
            Assert.True(stopped >= 1 && free >= 1, $"{stopped} stopped, {free} free");
            log?.WriteLine($"{holes} runs with holes, {fillIntersecting} with intersections added by the fill, {stopped} stopped, {free} free");
        }

        // finishing a stopped contour gives what the run without stopping gives
        [Fact]
        public void FinishPostProcess_SameAsTheRunWithoutStopping()
        {
            int finished = 0;
            for (int seed = 1; seed <= 6; ++seed) {
                var stopped = Run(MeshRobustRepairTests.ThinPlates(seed), 40);
                if (!stopped.StoppedOnSelfIntersections) continue;
                var full = Run(MeshRobustRepairTests.ThinPlates(seed), 40, stop: false);
                stopped.FinishPostProcess();
                Assert.Equal("", Invariance.Difference(full.Result, stopped.Result));
                finished++;
            }
            Assert.True(finished >= 3, $"{finished} stopped runs finished");
        }

        // the seams and the count follow the scale of the mesh (the solidify computes on a copy scaled to an extent of 1 .. 2), the result too
        [Fact]
        public void Similarities_SameContourAndFillCount()
        {
            DMesh3 plates = MeshRobustRepairTests.ThinPlates(4);
            var r0 = Run(new DMesh3(plates), 24);
            Assert.NotNull(r0.RawContour);
            foreach (Similarity sim in Similarity.All()) {
                if (!sim.IsExact) continue;
                var r = Run(Invariance.Transformed(plates, sim), 24);
                Assert.Equal(r0.FillIntersections, r.FillIntersections);
                Assert.Equal(r0.StoppedOnSelfIntersections, r.StoppedOnSelfIntersections);
                Assert.NotNull(r.RawContour);
                Assert.Equal("", Invariance.Difference(r0.RawContour, Invariance.MappedBack(r.RawContour, sim)));
                Assert.Equal("", Invariance.Difference(r0.Result, Invariance.MappedBack(r.Result, sim)));
            }
        }

        // one instance, several runs: the count and the raw contour of a run do not leak into the next (a grid run, an adaptive run, a grid run)
        [Fact]
        public void Reuse_NothingLeaksBetweenRuns()
        {
            DMesh3 plates = MeshRobustRepairTests.ThinPlates(3);
            var so = new MeshWindingNumberSolidify(plates) { GridResolution = 24, Adaptive = false, StopIfSelfIntersecting = true, KeepRawContour = true };
            Assert.True(so.Compute());
            int fill = so.FillIntersections;
            DMesh3 first = new DMesh3(so.Result);
            so.Adaptive = true;
            Assert.True(so.Compute());
            Assert.Equal(0, so.FillIntersections);
            so.Adaptive = false;
            Assert.True(so.Compute());
            Assert.Equal(fill, so.FillIntersections);
            Assert.Equal("", Invariance.Difference(first, so.Result));
        }
    }
}
