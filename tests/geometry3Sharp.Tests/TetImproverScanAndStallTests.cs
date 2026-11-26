using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1154: the passes of TetMeshImprover that do not converge (SliverDihedralDegrees 20 to 30 run all ImprovePasses and RescuePasses). Two changes:
    /// the scan of every pass takes the quality of a tet whose vertices and versions are unchanged from a cache (the same output bit for bit, against the
    /// internal ReferenceHillClimb loop that computes every quality again), and the opt-in StallStopPasses ends a phase whose passes make no progress.
    /// Procedural inputs, stage 1 at alpha / 20 and a floor of 30 degrees (the tail of creeping passes is there), passes capped to keep a test short.
    /// </summary>
    public class TetImproverScanAndStallTests
    {
        public static IEnumerable<object[]> Names() => new[] { "cylinder", "box" }.Select(n => new object[] { n });

        static DMesh3 Scaled(DMesh3 m, double s)
        {
            var r = new DMesh3(m);
            foreach (int v in r.VertexIndices()) r.SetVertex(v, r.GetVertex(v) * s);
            return r;
        }

        // stage 1 of an input and a scale, once (the improver copies the mesh)
        static readonly Dictionary<(string, double), (DMesh3 input, MeshTetrahedralization tm)> stage1 = new Dictionary<(string, double), (DMesh3, MeshTetrahedralization)>();
        static (DMesh3 input, MeshTetrahedralization tm) Stage1(string name, double scale)
        {
            lock (stage1) {
                if (stage1.TryGetValue((name, scale), out var hit)) return hit;
                DMesh3 input = Scaled(TetMeshingStage2Tests.Input(name), scale);
                var tm = new MeshTetrahedralization(input) { RelativeAlpha = 20, RelativeOffset = 600, SliverDihedralDegrees = 30, Improve = false };
                tm.Compute();                     // the status is PointLimitReached at this floor (stage 1 stops at its point limit); the mesh is there
                Assert.NotNull(tm.Result);
                return stage1[(name, scale)] = (input, tm);
            }
        }

        // the improver as MeshTetrahedralization.Compute sets it up, on the stage-1 mesh of the input
        static TetMeshImprover Run(string name, double scale, Action<TetMeshImprover> cfg)
        {
            var (input, tm) = Stage1(name, scale);
            var imp = new TetMeshImprover(tm.Result) { TargetDegrees = 30, MaxPasses = 20, RescuePasses = 10, Contraction = tm.ContractEdges, Insertion = tm.SplitEdges };
            if (tm.BoundarySlideFactor > 0) {
                imp.InputTree = new DMeshAABBTree3(input, true);
                imp.BoundarySlideLimit = tm.BoundarySlideFactor * tm.UsedOffset;
                imp.TangentOnly = tm.BoundaryMovesTangentOnly;
            }
            cfg(imp);
            imp.Run();
            return imp;
        }

        // the runs the tests share (an improver run is the cost of a test; Result() is built once per run)
        static readonly Dictionary<(string, double, int, bool), (TetMeshImprover imp, TetMesh3 result)> runs = new Dictionary<(string, double, int, bool), (TetMeshImprover, TetMesh3)>();
        static (TetMeshImprover imp, TetMesh3 result) Shared(string name, double scale, int stall, bool reference)
        {
            lock (runs) {
                if (!runs.TryGetValue((name, scale, stall, reference), out var hit)) {
                    TetMeshImprover imp = Run(name, scale, i => { i.StallStopPasses = stall; i.ReferenceHillClimb = reference; });
                    runs[(name, scale, stall, reference)] = hit = (imp, imp.Result());
                }
                return hit;
            }
        }

        static void AssertSame(TetMesh3 a, TetMesh3 b)
        {
            Assert.Equal(a.Vertices.Count, b.Vertices.Count);
            for (int i = 0; i < a.Vertices.Count; ++i) Assert.Equal(a.Vertices[i], b.Vertices[i]);
            Assert.Equal(a.Tets, b.Tets);
        }

        [Theory]
        [MemberData(nameof(Names))]
        public void ScanCache_GivesTheReferenceLoopsOutputAndCounts_WithAFractionOfTheQualityEvaluations(string name)
        {
            var (reference, refResult) = Shared(name, 1, 0, true);
            var (cached, cachedResult) = Shared(name, 1, 0, false);
            AssertSame(refResult, cachedResult);
            Assert.Equal(reference.Counts.OrderBy(kv => kv.Key).ToList(), cached.Counts.OrderBy(kv => kv.Key).ToList());
            Assert.Null(TetMeshQuality.NotValidReason(cachedResult));
            // the premise: a tail of passes, each of which scanned every tet in the reference loop
            Assert.True(reference.Counts["passes"] >= 15, name + ": " + reference.Counts["passes"] + " passes");
            Assert.True(cached.ScanQualityComputed * 4 < reference.ScanQualityComputed, name + ": " + cached.ScanQualityComputed + " vs " + reference.ScanQualityComputed);
            Assert.Equal(reference.ScanQualityComputed, cached.ScanQualityComputed + cached.ScanQualityCached);
        }

        // the exact pass counts of the procedural runs (floor 30, alpha / 20, MaxPasses 20, RescuePasses 10): the off run (the caps, 30) and N = 2, 3. The pass that judges is counted.
        // (a counter that is not reset on progress, or a stop one pass later, or a missed criterion changes them)
        [Theory]
        [InlineData("cylinder", 0, 30, 0)]
        [InlineData("cylinder", 2, 26, 2)]
        [InlineData("cylinder", 3, 27, 1)]
        [InlineData("box", 0, 30, 0)]
        [InlineData("box", 2, 25, 1)]
        [InlineData("box", 3, 26, 1)]
        public void StallStop_ExactPassAndStopCounts(string name, int n, int passes, int stops)
        {
            var (imp, result) = Shared(name, 1, n, false);
            Assert.Equal(passes, imp.Counts["passes"]);
            Assert.Equal(stops, imp.Counts.TryGetValue("stall_stop", out long ss) ? ss : 0);
            Assert.Null(TetMeshQuality.NotValidReason(result));
            if (n == 0) Assert.False(imp.Counts.ContainsKey("stall_stop"));            // off: no key, the run as before
        }

        [Fact]
        public void StallRule_ScriptedSequences_JudgeEachCriterion()
        {
            Func<int, TetMeshImprover.StallRule> rule = n => new TetMeshImprover.StallRule(n, 0.01, 0.1);
            // the first observation only records; N passes in a row without progress stop (>=, not >)
            var r = rule(2);
            Assert.False(r.Observe(100, 5.0));
            Assert.False(r.Observe(100, 5.0));              // 1 stalled
            Assert.True(r.Observe(100, 5.0));               // 2 stalled
            r = rule(1);
            Assert.False(r.Observe(100, 5.0));
            Assert.True(r.Observe(100, 5.0));
            // the count criterion: a drop of more than 1 % is progress, 0.9 % is not
            r = rule(1);
            Assert.False(r.Observe(1000, 5.0));
            Assert.False(r.Observe(989, 5.0));              // 1.1 %: progress
            Assert.True(r.Observe(981, 5.0));               // 0.8 % of 989: none
            // the worst criterion: more than 0.1 degrees is progress, exactly 0.1 is not
            r = rule(1);
            Assert.False(r.Observe(100, 5.0));
            Assert.False(r.Observe(100, 5.0 + 0.2));
            Assert.True(r.Observe(100, 5.0 + 0.2 + 0.1));   // the same expression as the rule's: not more than 0.1
            r = rule(1);
            Assert.False(r.Observe(100, 5.0));
            Assert.True(r.Observe(100, 5.05));
            // progress resets the run: stalled, progress, stalled does not stop at N = 2, the next stalled pass does
            r = rule(2);
            Assert.False(r.Observe(100, 5.0));
            Assert.False(r.Observe(100, 5.0));              // 1 stalled
            Assert.False(r.Observe(90, 5.0));               // progress: run 0
            Assert.False(r.Observe(90, 5.0));               // 1 stalled
            Assert.True(r.Observe(90, 5.0));                // 2 stalled
            // no bad tets at all (worst = MaxValue): a drop to 0 is progress (the climb ends there anyway: a pass that changes nothing)
            r = rule(1);
            Assert.False(r.Observe(0, double.MaxValue));
            Assert.False(r.Observe(0, double.MaxValue));
        }

        [Theory]
        [InlineData("cylinder")]
        [InlineData("box")]
        public void StallStop_EndsThePhasesEarly_AndKeepsAValidMesh(string name)
        {
            var (plain, _) = Shared(name, 1, 0, false);
            var (stalled, result) = Shared(name, 1, 2, false);
            Assert.True(stalled.Counts["passes"] < plain.Counts["passes"], name + ": " + stalled.Counts["passes"] + " vs " + plain.Counts["passes"]);
            Assert.Null(TetMeshQuality.NotValidReason(result));
        }

        [Fact]
        public void StallStop_RepeatedRuns_GiveTheSameResult()
        {
            AssertSame(Shared("cylinder", 1, 2, false).result, Run("cylinder", 1, i => i.StallStopPasses = 2).Result());      // a fresh run against the shared one
        }

        [Theory]
        [InlineData(1.0 / 1024)]
        [InlineData(1024.0)]
        public void StallStop_SameResultUnderPow2Scales(double s)
        {
            var (a, ra) = Shared("cylinder", 1, 2, false);
            var (b, rb) = Shared("cylinder", s, 2, false);
            Assert.Equal(ra.Tets, rb.Tets);
            Assert.Equal(a.Counts.OrderBy(kv => kv.Key).ToList(), b.Counts.OrderBy(kv => kv.Key).ToList());
            for (int v = 0; v < ra.Vertices.Count; ++v) Assert.Equal(ra.Vertices[v] * s, rb.Vertices[v]);
        }

        [Fact]
        public void MeshTetrahedralization_StallStopPasses_IsOffByDefault_PassedOn_AndReusable()
        {
            Assert.Equal(0, new MeshTetrahedralization(TetMeshingStage2Tests.Input("cylinder")).StallStopPasses);
            var tm = new MeshTetrahedralization(TetMeshingStage2Tests.Input("cylinder")) {
                RelativeAlpha = 20, RelativeOffset = 600, SliverDihedralDegrees = 30, ImprovePasses = 20, RescuePasses = 10, StallStopPasses = 2 };
            tm.Compute(); Assert.NotNull(tm.Result);
            Assert.True(tm.Counts["improve_stall_stop"] >= 1);
            Assert.True(tm.Counts["improve_passes"] < 30, "the stop ended the phases before the caps (20 + 10 passes): " + tm.Counts["improve_passes"]);
            Assert.Null(TetMeshQuality.NotValidReason(tm.Result));
            TetMesh3 first = tm.Result;
            tm.Compute(); Assert.NotNull(tm.Result);        // reuse: the same instance again
            AssertSame(first, tm.Result);
        }
    }
}
