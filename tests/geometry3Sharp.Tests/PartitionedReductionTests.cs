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

        // an open noisy sheet over two long triangles (z = -h) that span every slab: they are frozen in every round and
        // share no vertex with a region, so a region sees them only through the box overlap. Collapses of the sheet within
        // the error would cross them; the result must have no self-intersection
        internal static DMesh3 SheetOverObstacle(int seed, double h)
        {
            var m = new DMesh3();
            var rnd = new Random(seed);
            int nx = 80, ny = 8;
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i)
                    m.AppendVertex(new Vector3d(0.5 * i, 0.5 * j, 0.2 * rnd.NextDouble()));
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
                }
            int p0 = m.AppendVertex(new Vector3d(-1, -1, -h)), p1 = m.AppendVertex(new Vector3d(0.5 * nx + 1, -1, -h));
            int p2 = m.AppendVertex(new Vector3d(0.5 * nx + 1, 0.5 * ny + 1, -h)), p3 = m.AppendVertex(new Vector3d(-1, 0.5 * ny + 1, -h));
            m.AppendTriangle(p0, p1, p2); m.AppendTriangle(p0, p2, p3);
            return m;
        }

        [Theory]
        [InlineData(1, 0.01)]
        [InlineData(2, 0.005)]
        [InlineData(3, 0.002)]
        public void FrozenObstacle_WithoutSharedVertex_IsSeen(int seed, double h)
        {
            DMesh3 input = SheetOverObstacle(seed, h);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(input));
            var pr = new PartitionedReduction() { Configure = Configure };
            DMesh3 r = pr.Reduce(input, 0.3 * 0.3);
            Assert.True(pr.FrozenTriangles > 0 && pr.RegionCollapses > 0, $"{pr.FrozenTriangles} frozen, {pr.RegionCollapses} collapses");
            Assert.True(r.TriangleCount < input.TriangleCount / 2, $"{r.TriangleCount}");
            Assert.True(r.CheckValidity(true, FailMode.ReturnOnly));
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(r));
            // (the same without the obstacle crosses z = -h: else the test shows nothing)
            DMesh3 bare = new DMesh3(input);
            bare.RemoveTriangle(bare.MaxTriangleID - 1); bare.RemoveTriangle(bare.MaxTriangleID - 2);
            DMesh3 rb = new PartitionedReduction() { Configure = Configure }.Reduce(bare, 0.3 * 0.3);
            Assert.True(rb.VertexIndices().Any(v => rb.GetVertex(v).z < -h), "the sheet does not reach the obstacle");
        }

        // a box refined on its faces, long along x: many vertices share the x of the end faces, so quantile cuts coincide
        // (inverted slabs that own nothing). Clean and the same for every thread count
        [Fact]
        public void CoincidingCuts_CleanAndDeterministic()
        {
            var gen = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(4, 0.5, 0.5)), EdgeVertices = 24 };
            DMesh3 box = gen.Generate().MakeDMesh();
            MeshTransforms.Translate(box, new Vector3d(0.25, 0.125, 0.0625));
            DMesh3 reference = null;
            foreach (int threads in new[] { 1, 4, 8 }) {
                var pr = new PartitionedReduction() { MaxThreads = threads, Configure = Configure };
                DMesh3 r = pr.Reduce(box, 1e-6);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(r, true));
                Assert.True(r.TriangleCount < box.TriangleCount / 4, $"{r.TriangleCount}");
                if (reference == null) reference = r;
                else Assert.Equal("", Invariance.Difference(reference, r));
            }
        }

        // the slabs cannot be kept apart (no gap): one serial run, the same as Slabs 1 without rounds
        [Fact]
        public void NoGap_FallsBackToSerial()
        {
            MeshAlphaWrap w = Wrap(173, 40);
            double tol = 0.5 * w.UsedOffset;
            var pr = new PartitionedReduction() { Configure = Configure, GapEdgeLengths = 0 };
            DMesh3 r = pr.Reduce(w.Result, tol * tol);
            Assert.Equal(1, pr.SerialFallbacks);
            DMesh3 serial = new PartitionedReduction() { Slabs = 1, Rounds = 0, Configure = Configure }.Reduce(w.Result, tol * tol);
            Assert.Equal("", Invariance.Difference(serial, r));
            var ok = new PartitionedReduction() { Configure = Configure };
            ok.Reduce(w.Result, tol * tol);
            Assert.Equal(0, ok.SerialFallbacks);
        }

        // an empty mesh: no exception; SimplifyWithin answers as the serial path
        [Fact]
        public void EmptyMesh()
        {
            Assert.Equal(0, new PartitionedReduction() { Configure = Configure }.Reduce(new DMesh3(), 1).TriangleCount);
            int sub;
            DMesh3 part = MeshRobustRepair.SimplifyWithin(new DMesh3(), 1, 1, true, () => false, out sub, MeshRobustRepair.WrapSimplifySlabs, 0);
            DMesh3 serial = MeshRobustRepair.SimplifyWithin(new DMesh3(), 1, 1, true, () => false, out sub, 0, 0);
            Assert.Equal(serial == null, part == null);
        }
    }
}
