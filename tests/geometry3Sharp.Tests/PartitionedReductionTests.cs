using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// PartitionedReduction (Alpha Wrap 2.0 option A): the wrap simplification on fixed slabs, the regions in parallel.
    /// The result must not depend on the thread count or the run, must keep the guarantees of SimplifyWithin, and must
    /// carry what pinned vertices absorb.
    /// </summary>
    public class PartitionedReductionTests
    {
        static void Configure(Reducer r)
        {
            r.PreventSelfIntersections = true; r.AreaWeightedQuadrics = false;
            r.MinCollapseTriangleQuality = 0.05; r.EdgeFlipTolerance = Math.Cos(60 * MathUtil.Deg2Rad);
        }

        static MeshAlphaWrap Wrap(int seed, double relAlpha)
        {
            var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(seed)) { RelativeAlpha = relAlpha, RelativeOffset = 10 * relAlpha };
            Assert.True(w.Compute(), w.Error);
            return w;
        }

        static double Hausdorff(DMesh3 a, DMesh3 b, double tol)
        {
            var hd = new MeshHausdorffDistance(a, b) { Symmetric = false, Tolerance = 0.01 * tol };
            Assert.True(hd.Compute());
            return hd.UpperBound;
        }

        // the same result for 1, 2, 4 and 8 threads and for repeated runs: the partition depends only on the mesh and the
        // fixed slab count, never on the cores
        [Theory]
        [InlineData(42, 40.0)]
        [InlineData(173, 60.0)]
        [InlineData(7, 60.0)]
        public void SameResult_AnyThreadCount_AndRepeated(int seed, double relAlpha)
        {
            MeshAlphaWrap w = Wrap(seed, relAlpha);
            double tol = 0.5 * w.UsedOffset;
            DMesh3 reference = null;
            foreach (int threads in new[] { 1, 2, 4, 8, 4, 1 }) {
                var pr = new PartitionedReduction() { MaxThreads = threads, Configure = Configure };
                Assert.Equal(MeshRobustRepair.WrapSimplifySlabs, pr.Slabs);
                DMesh3 r = pr.Reduce(w.Result, tol * tol);
                if (reference == null) reference = r;
                else Assert.Equal("", Invariance.Difference(reference, r));
            }
            // the regions do work (on these small wraps the frozen bands are a large part; on Thingi10K wraps about 10 %)
            var check = new PartitionedReduction() { Configure = Configure };
            check.Reduce(w.Result, tol * tol);
            Assert.True(check.RegionCollapses > 0 && check.RegionTriangles > 0,
                $"{check.RegionCollapses} collapses, {check.RegionTriangles} region / {check.FrozenTriangles} frozen triangles");
        }

        // SimplifyWithin with the slabs: clean, certified within the distance (or refused), the input unchanged; close to one
        // serial run (triangles within 10 %)
        [Theory]
        [InlineData(42, 20.0)]
        [InlineData(173, 40.0)]
        [InlineData(11, 60.0)]
        [InlineData(3, 60.0)]
        [InlineData(23, 40.0)]
        public void SimplifyWithin_Guarantees(int seed, double relAlpha)
        {
            MeshAlphaWrap w = Wrap(seed, relAlpha);
            double tol = 0.5 * w.UsedOffset;
            DMesh3 copy = new DMesh3(w.Result);
            int sub;
            DMesh3 part = MeshRobustRepair.SimplifyWithin(w.Result, tol, tol, true, () => false, out sub, MeshRobustRepair.WrapSimplifySlabs, 0);
            DMesh3 serial = MeshRobustRepair.SimplifyWithin(w.Result, tol, tol, true, () => false, out sub, 0, 0);
            Assert.Equal("", Invariance.Difference(copy, w.Result));
            Assert.Equal(serial == null, part == null);
            if (part == null) return;
            Assert.Equal("", MeshRobustRepair.NotCleanReason(part, true));
            Assert.True(Hausdorff(part, w.Result, tol) <= tol);
            Assert.True(part.TriangleCount < w.Result.TriangleCount);
            Assert.True(Math.Abs(part.TriangleCount - serial.TriangleCount) <= 0.1 * serial.TriangleCount,
                $"{part.TriangleCount} vs serial {serial.TriangleCount}");
        }

        // the default of SimplifyWithin is the partition
        [Fact]
        public void SimplifyWithin_DefaultIsPartitioned()
        {
            MeshAlphaWrap w = Wrap(42, 40);
            double tol = 0.5 * w.UsedOffset;
            int sub;
            DMesh3 dflt = MeshRobustRepair.SimplifyWithin(w.Result, tol, tol, true, () => false, out sub);
            DMesh3 part = MeshRobustRepair.SimplifyWithin(w.Result, tol, tol, true, () => false, out sub, MeshRobustRepair.WrapSimplifySlabs, 1);
            DMesh3 serial = MeshRobustRepair.SimplifyWithin(w.Result, tol, tol, true, () => false, out sub, 0, 0);
            Assert.Equal("", Invariance.Difference(part, dflt));
            Assert.NotEqual("", Invariance.Difference(serial, dflt));
            Assert.Equal(8, MeshRobustRepair.WrapSimplifySlabs);
        }

        // one slab and no parallel round: exactly one serial run (the quadrics and fold-over references are seeded right)
        [Fact]
        public void OneSlabNoRound_SameAsSerial()
        {
            MeshAlphaWrap w = Wrap(173, 40);
            double tol = 0.5 * w.UsedOffset;
            DMesh3 serial = new DMesh3(w.Result);
            var r = new Reducer(serial);
            Configure(r);
            r.ReduceToMaxQuadricError(tol * tol);
            DMesh3 part = new PartitionedReduction() { Slabs = 1, Rounds = 0, Configure = Configure }.Reduce(w.Result, tol * tol);
            Assert.Equal("", Invariance.Difference(new DMesh3(serial, true), part));
        }

        // a free vertex collapsed onto a pinned one adds its quadric to it; losing that (CarryPinnedVertexData false, the
        // first prototype) let later collapses go further: on these wraps the distance to the wrap rose from 0.79 / 0.53 to
        // 0.96 / 0.82 of the tolerance (and on Thingi10K wraps beyond it: 26 of 100 failed the certification)
        [Theory]
        [InlineData(42, 60.0, 0.85)]
        [InlineData(3, 60.0, 0.65)]
        public void PinnedVertices_CarryTheirQuadric(int seed, double relAlpha, double bound)
        {
            MeshAlphaWrap w = Wrap(seed, relAlpha);
            double tol = 0.5 * w.UsedOffset;
            DMesh3 carried = new PartitionedReduction() { Configure = Configure }.Reduce(w.Result, tol * tol);
            DMesh3 lost = new PartitionedReduction() { Configure = Configure, CarryPinnedVertexData = false }.Reduce(w.Result, tol * tol);
            double dc = Hausdorff(carried, w.Result, tol) / tol, dl = Hausdorff(lost, w.Result, tol) / tol;
            Assert.True(dc <= bound, $"carried: {dc}");
            Assert.True(dl > dc + 0.1, $"lost {dl} vs carried {dc}");
        }

        // cancelled: SimplifyWithin returns null, no exception
        [Fact]
        public void Cancelled()
        {
            MeshAlphaWrap w = Wrap(42, 40);
            double tol = 0.5 * w.UsedOffset;
            int sub;
            Assert.Null(MeshRobustRepair.SimplifyWithin(w.Result, tol, tol, true, () => true, out sub, MeshRobustRepair.WrapSimplifySlabs, 0));
        }

        // under every similarity: clean and certified; the 2^k scales give the same result bit for bit
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void InvariantUnderSimilarity(string name)
        {
            Similarity sim = Similarity.ByName(name);
            MeshAlphaWrap w = Wrap(173, 40);
            double tol = 0.5 * w.UsedOffset;
            DMesh3 reference = new PartitionedReduction() { Configure = Configure }.Reduce(w.Result, tol * tol);
            DMesh3 input = Invariance.Transformed(w.Result, sim);
            double stol = sim.ApplyLength(tol);
            DMesh3 r = new PartitionedReduction() { Configure = Configure }.Reduce(input, stol * stol);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r, true));
            if (sim.IsExact)
                Assert.Equal("", Invariance.Difference(reference, Invariance.MappedBack(r, sim)));
            else
                Assert.True(Math.Abs(r.TriangleCount - reference.TriangleCount) <= 0.1 * reference.TriangleCount);
        }
    }
}
