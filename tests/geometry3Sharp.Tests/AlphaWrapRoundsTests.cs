using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The round-based carving of MeshAlphaWrap (Alpha Wrap 3.0 lever (B), docs/design/ALPHA_WRAP_ROUNDS.md): the result
    /// does not depend on MaxThreads (1 is the serial reference of the round schedule) or on the run, keeps the wrap's
    /// guarantees, and RoundWindow = 0 is the serial loop before the rounds, bit for bit.
    /// </summary>
    public class AlphaWrapRoundsTests
    {
        // an input whose carving has rounds of many gates (queue / RoundQueueDivisor above the parallel threshold)
        internal static DMesh3 Input() { return AlphaWrapTests.Ellipsoid(5, 0.3, 0.45); }
        const double RelAlpha = 100, RelOffset = 1000;

        static string Fingerprint(MeshAlphaWrap w)
        {
            // the result bit for bit and the counts that are not timings
            var counts = w.Counts.Where(kv => !kv.Key.EndsWith("_ms")).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + "=" + kv.Value);
            return AlphaWrapManifoldStepTests.Hash(w.Result) + " " + string.Join(",", counts);
        }

        [Fact]
        public void Rounds_SameResultForEveryThreadCount_AndRun()
        {
            DMesh3 input = Input();
            var w1 = new MeshAlphaWrap(input) { RelativeAlpha = RelAlpha, RelativeOffset = RelOffset, MaxThreads = 1 };
            Assert.True(w1.Compute(), w1.Error);
            AlphaWrapTests.CheckWrap(w1, AlphaWrapTests.Vertices(input));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w1.Result));
            AlphaWrapTests.AssertSeparationBound(w1, "rounds");
            // the rounds ran, with many gates each, and with the cases the parallel phases must keep apart
            Assert.True(w1.Counts["rounds"] > 0);
            Assert.True(w1.Counts["round_kept"] > 50 * w1.Counts["rounds"] / 10, $"{w1.Counts["round_kept"]} kept in {w1.Counts["rounds"]} rounds");
            Assert.True(w1.Counts["round_deferred"] > 0);
            Assert.True(w1.Counts["round_shared_outside"] > 0, "no two kept carvings of a round with the same outside cell");
            string reference = Fingerprint(w1);
            foreach (int threads in new[] { 2, 4, 8 }) {
                var w = new MeshAlphaWrap(input) { RelativeAlpha = RelAlpha, RelativeOffset = RelOffset, MaxThreads = threads };
                Assert.True(w.Compute(), w.Error);
                Assert.Equal(reference, Fingerprint(w));
            }
        }

        // RoundWindow = 0 is the serial loop: master's result before the rounds, pinned (soup 17 of
        // AlphaWrapManifoldStepTests.ManifoldStep_BitIdenticalToBefore), and the rounds give another wrap of the same size
        [Fact]
        public void RoundWindowZero_IsTheSerialLoop()
        {
            var serial = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(17)) { RelativeAlpha = 20, RelativeOffset = 200, RoundWindow = 0 };
            Assert.True(serial.Compute(), serial.Error);
            Assert.Equal("614F77D53A96BA60", AlphaWrapManifoldStepTests.Hash(serial.Result));
            Assert.False(serial.Counts.ContainsKey("rounds"));

            var w0 = new MeshAlphaWrap(Input()) { RelativeAlpha = RelAlpha, RelativeOffset = RelOffset, RoundWindow = 0 };
            var wr = new MeshAlphaWrap(Input()) { RelativeAlpha = RelAlpha, RelativeOffset = RelOffset };
            Assert.True(w0.Compute(), w0.Error);
            Assert.True(wr.Compute(), wr.Error);
            Assert.NotEqual(AlphaWrapManifoldStepTests.Hash(w0.Result), AlphaWrapManifoldStepTests.Hash(wr.Result));
            // (the resolution does not change: triangle counts within a few percent)
            Assert.InRange(wr.Result.TriangleCount, 0.95 * w0.Result.TriangleCount, 1.05 * w0.Result.TriangleCount);
        }

        // the round-based carving pinned on the inputs of AlphaWrapManifoldStepTests.Pinned (several pinch rounds): a change of
        // the rounds' result shows here (hashes of this PR's first version; the serial carving keeps its pins there)
        [Theory]
        [InlineData(42, 60.0, 600.0, "1440DFD70A780C4E")]
        [InlineData(173, 20.0, 200.0, "C86486C02C2F7E20")]
        [InlineData(3, 20.0, 200.0, "86AF3AB6DE837E60")]
        [InlineData(17, 20.0, 200.0, "53686D484FA5D330")]
        [InlineData(1, 40.0, 400.0, "7766673AF63F80D2")]
        public void Rounds_Pinned(int seed, double relAlpha, double relOffset, string hash)
        {
            var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(seed)) { RelativeAlpha = relAlpha, RelativeOffset = relOffset };
            Assert.True(w.Compute(), w.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            AlphaWrapTests.AssertSeparationBound(w, "soup " + seed);
            Assert.True(w.Counts["rounds"] > 0);
            Assert.Equal(hash, AlphaWrapManifoldStepTests.Hash(w.Result));
        }

        // Compute twice on one instance: the same result
        [Fact]
        public void Rounds_Reuse_SameResult()
        {
            var w = new MeshAlphaWrap(Input()) { RelativeAlpha = RelAlpha, RelativeOffset = RelOffset, MaxThreads = 4 };
            Assert.True(w.Compute(), w.Error);
            string first = Fingerprint(w);
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(first, Fingerprint(w));
        }

        // invariance over Similarity.All(): the 2^k scales give the same wrap, scaled, bit for bit; every transform a clean
        // wrap with the separation bound
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Rounds_Invariance(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Input();
            var w0 = new MeshAlphaWrap(input) { RelativeAlpha = 60, RelativeOffset = 600, MaxThreads = 4 };
            Assert.True(w0.Compute(), w0.Error);
            Assert.True(w0.Counts["rounds"] > 0);
            var w = new MeshAlphaWrap(Invariance.Transformed(input, s)) { RelativeAlpha = 60, RelativeOffset = 600, MaxThreads = 4 };
            if (s.IsRotated) {
                w.Alpha = s.ApplyLength(w0.UsedAlpha);
                w.Offset = s.ApplyLength(w0.UsedOffset);
            }
            Assert.True(w.Compute(), w.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            AlphaWrapTests.AssertSeparationBound(w, s.Name);
            if (s.IsExact)
                Assert.Equal("", Invariance.GeometricDifference(w0.Result, Invariance.MappedBack(w.Result, s)));
        }

        // the triangulation after the rounds is a valid Delaunay triangulation (adjacency, orientation, empty spheres)
        [Fact]
        public void Rounds_TriangulationStaysDelaunay()
        {
            var w = new MeshAlphaWrap(AlphaWrapTests.Ellipsoid(2, 0.5, 0.6)) { RelativeAlpha = 30, RelativeOffset = 300, MaxThreads = 4,
                RoundQueueDivisor = 4, MakeManifold = false, RefineNonManifold = false, RefineMinAngleDegrees = 0 };
            w.Compute();
            Assert.True(w.Counts["rounds"] > 0 && w.Counts["round_kept"] > 2 * w.Counts["rounds"]);
            Assert.Null(w.Triangulation.Validate(true));
        }

        // MaxSteinerPoints reached in the middle of a round, after a rejected insertion of that round (which does not count):
        // the gates before it are kept, the limit is exact, the status PointLimitReached, the result a closed wrap around the
        // input, the same for every thread count. (The first limit from 400 on with that case.)
        [Fact]
        public void Rounds_PointLimitMidRound_AfterARejection()
        {
            DMesh3 input = Input();
            Func<int, int, MeshAlphaWrap> wrap = (limit, threads) => {
                int calls = 0;
                var w = new MeshAlphaWrap(input) { RelativeAlpha = RelAlpha, RelativeOffset = RelOffset, MaxThreads = threads, MaxSteinerPoints = limit,
                    RefineNonManifold = false, RefineMinAngleDegrees = 0, RejectInsertF = (rule) => rule == 2 && ++calls % 2 == 0 };
                Assert.False(w.Compute());
                return w;
            };
            int found = -1;
            for (int limit = 400; limit < 500 && found < 0; ++limit)
                if (wrap(limit, 1).Counts.ContainsKey("round_limit_mid_round")) found = limit;
            Assert.True(found > 0, "no limit in the middle of a round after a rejected insertion");
            string reference = null;
            foreach (int threads in new[] { 1, 4 }) {
                MeshAlphaWrap w = wrap(found, threads);
                Assert.Equal(MeshAlphaWrap.WrapStatus.PointLimitReached, w.Status);
                Assert.Equal(found, w.Counts["steiner_points"]);
                Assert.True(w.Counts["rejected_points"] > 0);
                Assert.Equal(1, w.Counts["round_limit_mid_round"]);
                Assert.True(w.Result.IsClosed());
                Assert.Empty(MeshIterators.BowtieVertices(w.Result));
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(w.Result));
                var spatial = new DMeshAABBTree3(w.Result, true);
                Assert.True(AlphaWrapTests.Vertices(input).All(p => spatial.WindingNumber(p) > 0.99));
                string f = Fingerprint(w);
                if (reference == null) reference = f;
                else Assert.Equal(reference, f);
            }
        }

        // cancelled between rounds: Cancelled, no result, for every thread count
        [Fact]
        public void Rounds_Cancel()
        {
            int calls = 0;
            var w = new MeshAlphaWrap(Input()) { RelativeAlpha = RelAlpha, RelativeOffset = RelOffset, MaxThreads = 4, CancelF = () => ++calls > 20 };
            Assert.False(w.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.Cancelled, w.Status);
            Assert.Null(w.Result);
        }
    }
}
