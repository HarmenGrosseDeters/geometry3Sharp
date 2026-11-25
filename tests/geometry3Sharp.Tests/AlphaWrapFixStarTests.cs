using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The manifold step of MeshAlphaWrap (fix_star) at stars with many cells, issue #1607: with a point limit far below what the
    /// alpha needs, the box corners have stars of tens of thousands of cells, and the step recomputed the whole star for every cell it
    /// moved inside (quadratic: 10k to 80k points took 1.4 to 51.7 s). Now one pass (join_inside_components), the same moves in the same
    /// order: the result must be bit for bit that of the old step, which ReferenceFixStar keeps.
    /// </summary>
    public class AlphaWrapFixStarTests
    {
        static DMesh3 Sphere(int n) => MeshRepairIssueCasesTests.Sphere(Vector3d.Zero, 1, n);

        static MeshAlphaWrap Limit(DMesh3 input, int points, bool reference)
        {
            var w = new MeshAlphaWrap(input) { RelativeAlpha = 1e5, RelativeOffset = 1e6, MaxSteinerPoints = points, ReferenceFixStar = reference };
            w.Compute();
            return w;
        }

        // random triangle soups (a thin slab and a long box), a point limit and an alpha too fine for them: stars at the box corners with
        // several inside components and exterior and enclosed outside components
        static DMesh3 Soup(int seed)
        {
            var rnd = new Random(seed);
            var m = new DMesh3();
            int np = 8 + rnd.Next(30);
            double sx = Math.Pow(10, rnd.Next(-1, 2));
            for (int i = 0; i < np; ++i) m.AppendVertex(new Vector3d(rnd.NextDouble() * sx, rnd.NextDouble(), rnd.NextDouble() * 0.3));
            for (int i = 0; i + 2 < np; ++i) m.AppendTriangle(i, i + 1, i + 2);
            return m;
        }

        static void AssertSameResult(DMesh3 a, DMesh3 b, string what)
        {
            Assert.True((a == null) == (b == null), what);
            if (a == null) return;
            Assert.Equal(a.VertexCount, b.VertexCount);
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            Assert.True(a.VertexIndices().All(v => a.GetVertex(v) == b.GetVertex(v)), what + ": vertices");
            Assert.True(a.TriangleIndices().All(t => a.GetTriangle(t) == b.GetTriangle(t)), what + ": triangles");
        }

        [Fact]
        public void PointLimit_ResultIsTheOldStepsResult()
        {
            foreach (int points in new[] { 1000, 4000 }) {
                MeshAlphaWrap now = Limit(Sphere(6), points, false), old = Limit(Sphere(6), points, true);
                Assert.Equal(MeshAlphaWrap.WrapStatus.PointLimitReached, now.Status);
                AssertSameResult(now.Result, old.Result, $"sphere, {points} points");
                Assert.Equal(old.Counts["manifold_flips"], now.Counts["manifold_flips"]);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(now.Result));
            }
        }

        [Fact]
        public void Soups_ResultIsTheOldStepsResult()
        {
            int compared = 0, pointLimited = 0, onePass = 0;
            for (int seed = 0; seed < 10; ++seed) {
                var rnd = new Random(100 + seed);
                double ra = Math.Pow(10, 1.5 + 3.5 * rnd.NextDouble());
                double ro = ra * (1 + 60 * rnd.NextDouble());
                int points = 800 + rnd.Next(2500);
                DMesh3 input = Soup(seed);
                var now = new MeshAlphaWrap(input) { RelativeAlpha = ra, RelativeOffset = ro, MaxSteinerPoints = points };
                var old = new MeshAlphaWrap(input) { RelativeAlpha = ra, RelativeOffset = ro, MaxSteinerPoints = points, ReferenceFixStar = true };
                now.Compute();
                old.Compute();
                Assert.Equal(old.Status, now.Status);
                AssertSameResult(now.Result, old.Result, $"soup {seed}");
                compared++;
                if (now.Status == MeshAlphaWrap.WrapStatus.PointLimitReached) pointLimited++;
                if (now.Counts.TryGetValue("manifold_star_work", out long wn) && old.Counts.TryGetValue("manifold_star_work", out long wo) && wn < wo) onePass++;
                if (now.Result != null) Assert.Equal("", MeshRobustRepair.NotCleanReason(now.Result));
            }
            // (the cases have to reach the new step, or the test compares the old one with itself)
            Assert.True(pointLimited >= 6, $"{pointLimited} of {compared} reached the limit");
            Assert.True(onePass >= 3, $"{onePass} of {compared} took the one-pass step");
        }

        // the work of the step goes with the cells it moves, not with their product with the size of the star: 20000 points gave
        // 38 million visited cells for 40,000 moves before, below 1 million now (a time bound would depend on the runner)
        [Fact]
        public void PointLimit_WorkIsLinearInTheMoves()
        {
            MeshAlphaWrap w = Limit(Sphere(6), 20000, false);
            Assert.Equal(MeshAlphaWrap.WrapStatus.PointLimitReached, w.Status);
            long flips = w.Counts["manifold_flips"], work = w.Counts["manifold_star_work"];
            Assert.True(flips > 10000, $"{flips} moves");
            Assert.True(work < 30 * flips, $"{work} cells visited for {flips} moves");
        }

        // the order of the candidates is that of preferred_flip, and the first cell of the star on a tie (as a scan of the cells in order)
        [Fact]
        public void CandidateOrder_IsPreferredFlipThenTheLocalIndex()
        {
            var c = new Func<bool, bool, double, int, MeshAlphaWrap.JoinCandidate>((corner, joins, vol, k) =>
                new MeshAlphaWrap.JoinCandidate { Corner = corner, Joins = joins, Volume = vol, K = k });
            Assert.True(MeshAlphaWrap.before(c(false, false, 5, 9), c(true, true, 1, 0)));     // no corner first
            Assert.True(MeshAlphaWrap.before(c(true, true, 5, 9), c(true, false, 1, 0)));      // then joining
            Assert.True(MeshAlphaWrap.before(c(true, true, 1, 9), c(true, true, 5, 0)));       // then the smaller volume
            Assert.True(MeshAlphaWrap.before(c(true, true, 2, 3), c(true, true, 2, 4)));       // then the first cell
            Assert.False(MeshAlphaWrap.before(c(true, true, 2, 4), c(true, true, 2, 3)));
            Assert.False(MeshAlphaWrap.before(c(true, true, 2, 3), c(true, true, 2, 3)));
            for (int i = 0; i < 16; ++i) {
                bool ca = (i & 1) != 0, cb = (i & 2) != 0, ja = (i & 4) != 0, jb = (i & 8) != 0;
                Assert.Equal(MeshAlphaWrap.preferred_flip(ca, ja, 1, cb, jb, 2), MeshAlphaWrap.before(c(ca, ja, 1, 1), c(cb, jb, 2, 0)));
            }
        }

        // the soups of the sweep behind the two tests below (400 seeds): the input and the wrap's parameters of one seed
        static MeshAlphaWrap Sweep(int seed, bool reference)
        {
            var rnd = new Random(1000 + seed);
            var m = new DMesh3();
            int np = 6 + rnd.Next(60);
            double sx = Math.Pow(10, rnd.Next(-1, 2));
            for (int i = 0; i < np; ++i) m.AppendVertex(new Vector3d(rnd.NextDouble() * sx, rnd.NextDouble(), rnd.NextDouble() * (rnd.Next(2) == 0 ? 0.3 : 1)));
            for (int i = 0; i + 2 < np; ++i) m.AppendTriangle(i, i + 1, i + 2);
            double ra = Math.Pow(10, 1.0 + 4.5 * rnd.NextDouble()), ro = ra * (1 + 80 * rnd.NextDouble());
            int points = 300 + rnd.Next(4000);
            var w = new MeshAlphaWrap(m) { RelativeAlpha = ra, RelativeOffset = ro, MaxSteinerPoints = points, ReferenceFixStar = reference };
            w.Compute();
            return w;
        }

        // seeds of the sweep where a candidate's key got worse by a merge of inside components before it was taken: without the check of
        // a popped key (which moves such a cell back into the heap) the step takes another cell first and the result differs (5 of the 9 that differ of the 400
        // seeds; the check was not exercised by the cases above)
        [Fact]
        public void Sweep_StaleCandidateKeys_AreCheckedWhenPopped()
        {
            foreach (int seed in new[] { 0, 61, 105, 134, 274 }) {
                MeshAlphaWrap now = Sweep(seed, false), old = Sweep(seed, true);
                Assert.Equal(old.Status, now.Status);
                AssertSameResult(now.Result, old.Result, $"sweep seed {seed}");
                if (now.Result != null) Assert.Equal("", MeshRobustRepair.NotCleanReason(now.Result));
            }
        }

        // seeds where the old step visited 1.3 to 2.9 million cells for 1800 to 6600 moves (stars of thousands of cells, cut-off pieces and
        // the fallback to the general step): the new one visits a quarter or less. A regression of the forest's re-attachment or of the searches
        // after a move (which count in manifold_star_work) back to a search of the whole star per move would not stay below that
        [Fact]
        public void Sweep_WorkIsBelowAQuarterOfTheOldSteps()
        {
            foreach (int seed in new[] { 187, 320, 365 }) {
                MeshAlphaWrap now = Sweep(seed, false), old = Sweep(seed, true);
                AssertSameResult(now.Result, old.Result, $"sweep seed {seed}");
                long wn = now.Counts["manifold_star_work"], wo = old.Counts["manifold_star_work"];
                Assert.True(wo > 1000000, $"seed {seed}: the old step visited {wo} cells");
                Assert.True(4 * wn < wo, $"seed {seed}: {wn} cells visited, the old step {wo}");
            }
        }

        [Fact]
        public void PointLimit_Reuse_ComputeTwiceGivesTheSameResult()
        {
            var w = new MeshAlphaWrap(Sphere(6)) { RelativeAlpha = 1e5, RelativeOffset = 1e6, MaxSteinerPoints = 3000 };
            w.Compute();
            DMesh3 first = w.Result;
            long work = w.Counts["manifold_star_work"];
            w.Compute();
            Assert.Equal(MeshAlphaWrap.WrapStatus.PointLimitReached, w.Status);
            AssertSameResult(first, w.Result, "second Compute");
            Assert.Equal(work, w.Counts["manifold_star_work"]);
        }

        [Fact]
        public void PointLimit_ThreadCountDoesNotChangeTheResult()
        {
            MeshAlphaWrap serial = null, parallel = null;
            foreach (int threads in new[] { 1, 4 }) {
                var w = new MeshAlphaWrap(Sphere(6)) { RelativeAlpha = 1e5, RelativeOffset = 1e6, MaxSteinerPoints = 3000, MaxThreads = threads };
                w.Compute();
                if (threads == 1) serial = w; else parallel = w;
            }
            AssertSameResult(serial.Result, parallel.Result, "1 and 4 threads");
            Assert.Equal(serial.Counts["manifold_flips"], parallel.Counts["manifold_flips"]);
        }

        [Fact]
        public void PointLimit_Invariance()
        {
            DMesh3 input = Sphere(6);
            MeshAlphaWrap w0 = Limit(input, 3000, false);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w0.Result));
            foreach (Similarity s in Similarity.All()) {
                MeshAlphaWrap w = Limit(Invariance.Transformed(input, s), 3000, false);
                Assert.Equal(MeshAlphaWrap.WrapStatus.PointLimitReached, w.Status);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
                if (s.IsExact) {
                    Assert.Equal(w0.Result.TriangleCount, w.Result.TriangleCount);
                    Assert.Equal(w0.Counts["manifold_flips"], w.Counts["manifold_flips"]);
                    Assert.True(w0.Result.VertexIndices().All(v => w.Result.GetVertex(v) == s.Apply(w0.Result.GetVertex(v))), s.Name);
                }
            }
        }
    }
}
