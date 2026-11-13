using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The trial passes of MeshAlphaWrap's pinch refinement and manifold step in parallel (Alpha Wrap 3.0 lever (B), second
    /// part: the stars, the pocket search, the vertex checks of the manifold step): the same result bit for bit as in one
    /// thread, for every thread count, on inputs whose refinement runs several pinch rounds with flips and pockets.
    /// </summary>
    public class AlphaWrapParallelTrialsTests
    {
        static string Fingerprint(MeshAlphaWrap w)
        {
            var counts = w.Counts.Where(kv => !kv.Key.EndsWith("_ms")).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + "=" + kv.Value);
            return AlphaWrapManifoldStepTests.Hash(w.Result) + " " + string.Join(",", counts);
        }

        // the serial carving's pins (master's results, AlphaWrapManifoldStepTests.Pinned) with every trial pass in parallel
        // (threshold 0, so that the small inputs take the parallel paths): unchanged
        [Theory]
        [MemberData(nameof(AlphaWrapManifoldStepTests.Pinned), MemberType = typeof(AlphaWrapManifoldStepTests))]
        public void ParallelTrials_MastersPinsUnchanged(int seed, double relAlpha, double relOffset, string hash)
        {
            foreach (int threads in new[] { 2, 4, 8 }) {
                var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(seed)) { RelativeAlpha = relAlpha, RelativeOffset = relOffset,
                    RoundWindow = 0, MaxThreads = threads, MinParallelTrialCells = 0 };
                Assert.True(w.Compute(), w.Error);
                Assert.True(w.Counts["pinch_rounds"] >= 1);
                Assert.Equal(hash, AlphaWrapManifoldStepTests.Hash(w.Result));
            }
        }

        // the default (round-based carving, threshold as shipped) on an input large enough for the parallel passes: the same
        // result and counts with the passes in one thread, for 1, 2, 4 and 8 threads; flips, pockets and pinch rounds occur
        [Fact]
        public void ParallelTrials_SameAsSerial_EveryThreadCount()
        {
            DMesh3 input = AlphaWrapTests.RandomSoup(42);
            var serial = new MeshAlphaWrap(input) { RelativeAlpha = 120, RelativeOffset = 1200, MaxThreads = 4, ParallelTrials = false };
            Assert.True(serial.Compute(), serial.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(serial.Result));
            Assert.True(serial.Counts["pinch_rounds"] >= 1 && serial.Counts["manifold_flips"] > 0);
            Assert.True(serial.Counts["cells"] >= 4 * 4096, $"{serial.Counts["cells"]} cells: below the parallel threshold");
            string reference = Fingerprint(serial);
            foreach (int threads in new[] { 1, 2, 4, 8, 4 }) {
                var w = new MeshAlphaWrap(input) { RelativeAlpha = 120, RelativeOffset = 1200, MaxThreads = threads };
                Assert.True(w.Compute(), w.Error);
                Assert.Equal(reference, Fingerprint(w));
            }
        }

        // the pocket search on random labellings of a wrap's triangulation (many enclosed outside regions): the parallel
        // search moves the same cells inside, in the same order, as the serial one, for every thread count
        [Fact]
        public void ParallelTrials_Pockets_SameAsSerial()
        {
            var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(42)) { RelativeAlpha = 60, RelativeOffset = 600 };
            Assert.True(w.Compute(), w.Error);
            foreach (double share in new[] { 0.3, 0.5, 0.7 }) {
                Func<int, bool> outside = c => (uint)(c * 2654435761u) % 1000 < share * 1000;
                List<int> serial = w.PocketsForTests(outside, false, 1);
                Assert.True(serial.Count > 100, $"{serial.Count} pocket cells at {share}");
                foreach (int threads in new[] { 1, 2, 4, 8 })
                    Assert.Equal(serial, w.PocketsForTests(outside, true, threads));
            }
        }

        // the stars built in parallel (by vertex ranges) equal the serial ones, cell order included (the manifold step's
        // components and choices follow that order)
        [Fact]
        public void ParallelTrials_Stars_SameAsSerial()
        {
            var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(42)) { RelativeAlpha = 60, RelativeOffset = 600 };
            Assert.True(w.Compute(), w.Error);
            var (s0, c0) = w.StarsForTests(false, 1);
            foreach (int threads in new[] { 2, 3, 4, 8 }) {
                var (s1, c1) = w.StarsForTests(true, threads);
                Assert.Equal(s0, s1);
                Assert.Equal(c0, c1);
            }
        }

        // the precheck of the manifold step's vertex check against an independent oracle, on random labellings (odd facet
        // counts around edges, several cycles, box corners with result facets): a vertex is unchanged by the check exactly if
        // it has no result facet, or it is not on the box, every edge v-x is on two result facets and those form one cycle
        [Fact]
        public void ParallelTrials_Precheck_AsOracle()
        {
            var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(42)) { RelativeAlpha = 40, RelativeOffset = 400 };
            Assert.True(w.Compute(), w.Error);
            Delaunay3 dt = w.Triangulation;
            int trivial = 0, notTrivial = 0, corners = 0;
            foreach (double share in new[] { 0.02, 0.5, 0.97 }) {
                Func<int, bool> outside = c => (uint)(c * 2654435761u) % 1000 < share * 1000;
                var got = new HashSet<int>(w.UnchangedByCheckForTests(outside, 4));
                for (int v = 0; v < dt.VertexCount; ++v) {
                    bool expected = Oracle(dt, outside, v, out bool hasFacets);
                    Assert.True(expected == got.Contains(v), $"vertex {v} at {share}: oracle {expected}");
                    if (expected && hasFacets) trivial++;
                    if (!expected) notTrivial++;
                    if (v < 8 && hasFacets) corners++;
                }
            }
            Assert.True(trivial > 100 && notTrivial > 100 && corners > 0, $"{trivial} / {notTrivial} / {corners}");
        }

        static bool Oracle(Delaunay3 dt, Func<int, bool> outside, int v, out bool hasFacets)
        {
            // result facets through v: facets between an inside cell and an outside cell (or the box) of v's star
            var star = Enumerable.Range(0, dt.MaxCellID).Where(c => dt.IsCell(c) && dt.VertexIndexIn(c, v) >= 0).ToList();
            bool hull = false;
            var facets = new List<(int, int)>();        // the two other vertices of each result facet through v
            foreach (int c in star) {
                for (int i = 0; i < 4; ++i) {
                    if (dt.CellVertex(c, i) == v) continue;
                    int n = dt.Neighbour(c, i);
                    if (n < 0) hull = true;
                    bool cin = !outside(c), nout = n < 0 || outside(n);
                    if (!(cin && nout)) continue;
                    var xs = Enumerable.Range(0, 4).Where(j => j != i && dt.CellVertex(c, j) != v).Select(j => dt.CellVertex(c, j)).ToArray();
                    facets.Add((xs[0], xs[1]));
                }
            }
            hasFacets = facets.Count > 0;
            if (!hasFacets) return true;
            if (hull) return false;
            var degree = new Dictionary<int, int>();
            foreach (var (a, b) in facets) foreach (int x in new[] { a, b }) degree[x] = (degree.TryGetValue(x, out int d) ? d : 0) + 1;
            if (degree.Values.Any(d => d != 2)) return false;
            // one cycle: the link edges connect all link vertices
            var seen = new HashSet<int> { facets[0].Item1 };
            bool grown = true;
            while (grown) {
                grown = false;
                foreach (var (a, b) in facets)
                    if (seen.Contains(a) != seen.Contains(b)) { seen.Add(a); seen.Add(b); grown = true; }
            }
            return seen.Count == degree.Count;
        }
    }
}
