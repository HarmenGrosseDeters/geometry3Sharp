using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshAlphaWrap.PhaseMilliseconds "&lt;phase&gt;/&lt;part&gt;": the parts of each refinement phase (wrap, wrap.pinches,
    /// wrap.quality, wrap.pinches2) in that phase only, where wrap.intersection / .touch / .insert are the totals over all
    /// four (Alpha Wrap 3.0 phase 1: the earlier split overlapped). Timing only: the result does not change.
    /// </summary>
    public class AlphaWrapPhaseSplitTests
    {
        static readonly string[] Phases = { "wrap", "wrap.pinches", "wrap.quality", "wrap.pinches2" };
        static readonly string[] Parts = { "intersection", "touch", "insert", "carve", "trial" };

        static long Get(MeshAlphaWrap w, string key) => w.PhaseMilliseconds.TryGetValue(key, out long v) ? v : 0;

        [Fact]
        public void PhaseParts_AddUpToTheTotals_AndExplainThePhases()
        {
            // (a soup with pinches and quality points: all four refinement phases run)
            var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(42)) { RelativeAlpha = 60, RelativeOffset = 600 };
            Assert.True(w.Compute(), w.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            var ran = Phases.Where(p => w.PhaseMilliseconds.ContainsKey(p)).ToList();
            Assert.Equal(Phases, ran);
            Assert.True(w.Counts["pinch_rounds"] > 0 && w.Counts["quality_points"] > 0);
            // every part of every phase that ran, and none of a phase that did not
            foreach (string p in ran)
                foreach (string part in Parts)
                    Assert.True(w.PhaseMilliseconds.ContainsKey(p + "/" + part), p + "/" + part);
            Assert.DoesNotContain(w.PhaseMilliseconds.Keys, k => k.StartsWith("setup/") || k.StartsWith("extract/") || k.StartsWith("manifold/"));
            // the parts of the phases add up to the totals (each truncated to ms: at most 1 ms lost per phase)
            foreach (var (total, part) in new[] { ("wrap.intersection", "intersection"), ("wrap.touch", "touch"), ("wrap.insert", "insert") }) {
                long sum = ran.Sum(p => Get(w, p + "/" + part));
                Assert.True(sum <= Get(w, total) && sum >= Get(w, total) - ran.Count, $"{part}: {sum} vs {Get(w, total)}");
            }
            foreach (string p in ran) {
                long carve = Get(w, p + "/carve"), tests = Get(w, p + "/intersection") + Get(w, p + "/touch");
                // (the carving loop holds the tests; the phase holds the carving and the trials)
                Assert.True(tests <= carve + 3, $"{p}: tests {tests} > carve {carve}");
                Assert.True(carve + Get(w, p + "/trial") <= Get(w, p) + 2, $"{p}: carve + trial > phase {Get(w, p)}");
                Assert.True(Get(w, p + "/trial") == 0 || p.StartsWith("wrap.pinches"), $"{p}: trials outside the pinch passes");
            }
            // the carving explains the carving phase, the trials the pinch passes (both are most of their phase)
            Assert.True(Get(w, "wrap/carve") >= 0.8 * Get(w, "wrap") - 2, $"carve {Get(w, "wrap/carve")} of wrap {Get(w, "wrap")}");
            long pinch = Get(w, "wrap.pinches") + Get(w, "wrap.pinches2");
            long trial = Get(w, "wrap.pinches/trial") + Get(w, "wrap.pinches2/trial");
            long pinchCarve = Get(w, "wrap.pinches/carve") + Get(w, "wrap.pinches2/carve");
            Assert.True(trial + pinchCarve >= 0.5 * pinch - 4, $"pinch passes {pinch}: trials {trial}, carving {pinchCarve}");
        }

        static bool Same(DMesh3 a, DMesh3 b)
        {
            if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return false;
            var va = a.VertexIndices().ToArray(); var vb = b.VertexIndices().ToArray();
            for (int k = 0; k < va.Length; ++k) if (va[k] != vb[k] || a.GetVertex(va[k]) != b.GetVertex(vb[k])) return false;
            var ta = a.TriangleIndices().ToArray(); var tb = b.TriangleIndices().ToArray();
            for (int k = 0; k < ta.Length; ++k) if (ta[k] != tb[k] || a.GetTriangle(ta[k]) != b.GetTriangle(tb[k])) return false;
            return true;
        }

        // the wrap simplification's reduction parts (benchmarks): setup, the parallel rounds and the serial last pass, within the reduction
        [Fact]
        public void SimplifyWithin_ReportsTheReductionParts()
        {
            var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(7)) { RelativeAlpha = 20, RelativeOffset = 200 };
            Assert.True(w.Compute(), w.Error);
            var phases = new Dictionary<string, long>();
            DMesh3 s = MeshRobustRepair.SimplifyWithin(w.Result, 0.5 * w.UsedOffset, 0.5 * w.UsedOffset, true, () => false, out int subdivisions, phases);
            Assert.NotNull(s);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(s, true));
            foreach (string k in new[] { "reduce", "reduce/setup", "reduce/rounds", "reduce/last", "check", "certify" })
                Assert.True(phases.ContainsKey(k), k);
            Assert.True(phases["reduce/setup"] + phases["reduce/rounds"] + phases["reduce/last"] <= phases["reduce"] + 1);
            // (one serial Reducer run has no partition: no parts)
            var serial = new Dictionary<string, long>();
            Assert.NotNull(MeshRobustRepair.SimplifyWithin(w.Result, 0.5 * w.UsedOffset, 0.5 * w.UsedOffset, true, () => false, out subdivisions,
                MeshRobustRepair.WrapSimplifyMinQuality, true, 0, 0, serial));
            Assert.False(serial.ContainsKey("reduce/rounds"));
        }

        // the split is timing only: the same result as without anyone reading it, twice on one instance
        [Fact]
        public void PhaseSplit_Reuse_SameResultAndKeys()
        {
            var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup(7)) { RelativeAlpha = 20, RelativeOffset = 200 };
            Assert.True(w.Compute(), w.Error);
            DMesh3 first = new DMesh3(w.Result);
            var keys = new HashSet<string>(w.PhaseMilliseconds.Keys);
            Assert.True(w.Compute(), w.Error);
            Assert.True(Same(first, w.Result));
            Assert.Equal(keys, new HashSet<string>(w.PhaseMilliseconds.Keys));
        }
    }
}
