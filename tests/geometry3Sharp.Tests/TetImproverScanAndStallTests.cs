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
            TetMeshImprover reference = Run(name, 1, i => i.ReferenceHillClimb = true);
            TetMeshImprover cached = Run(name, 1, i => { });
            AssertSame(reference.Result(), cached.Result());
            Assert.Equal(reference.Counts.OrderBy(kv => kv.Key).ToList(), cached.Counts.OrderBy(kv => kv.Key).ToList());
            Assert.Null(TetMeshQuality.NotValidReason(cached.Result()));
            // the premise: a tail of passes, each of which scanned every tet in the reference loop
            Assert.True(reference.Counts["passes"] >= 15, name + ": " + reference.Counts["passes"] + " passes");
            Assert.True(cached.ScanQualityComputed * 4 < reference.ScanQualityComputed, name + ": " + cached.ScanQualityComputed + " vs " + reference.ScanQualityComputed);
            Assert.Equal(reference.ScanQualityComputed, cached.ScanQualityComputed + cached.ScanQualityCached);
        }

        [Theory]
        [InlineData("cylinder")]
        [InlineData("box")]
        public void StallStop_EndsThePhasesEarly_AndKeepsAValidMesh_WhileOffIsTheDefault(string name)
        {
            TetMeshImprover plain = Run(name, 1, i => { });
            Assert.False(plain.Counts.ContainsKey("stall_stop"));                    // off: no key, the run as before
            TetMeshImprover stalled = Run(name, 1, i => i.StallStopPasses = 2);
            Assert.True(stalled.Counts.ContainsKey("stall_stop") && stalled.Counts["stall_stop"] >= 1);
            Assert.True(stalled.Counts["passes"] < plain.Counts["passes"], name + ": " + stalled.Counts["passes"] + " vs " + plain.Counts["passes"]);
            Assert.Null(TetMeshQuality.NotValidReason(stalled.Result()));
        }

        [Fact]
        public void StallStop_RepeatedRuns_GiveTheSameResult()
        {
            AssertSame(Run("cylinder", 1, i => i.StallStopPasses = 2).Result(), Run("cylinder", 1, i => i.StallStopPasses = 2).Result());
        }

        [Theory]
        [InlineData(1.0 / 1024)]
        [InlineData(1024.0)]
        public void StallStop_SameResultUnderPow2Scales(double s)
        {
            TetMeshImprover a = Run("cylinder", 1, i => i.StallStopPasses = 2), b = Run("cylinder", s, i => i.StallStopPasses = 2);
            TetMesh3 ra = a.Result(), rb = b.Result();          // (Result() builds the mesh each call)
            Assert.Equal(ra.Tets, rb.Tets);
            Assert.Equal(a.Counts.OrderBy(kv => kv.Key).ToList(), b.Counts.OrderBy(kv => kv.Key).ToList());
            for (int v = 0; v < ra.Vertices.Count; ++v) Assert.Equal(ra.Vertices[v] * s, rb.Vertices[v]);
        }

        [Fact]
        public void MeshTetrahedralization_StallStopPasses_IsOffByDefault_PassedOn_AndReusable()
        {
            Assert.Equal(0, new MeshTetrahedralization(TetMeshingStage2Tests.Input("cylinder")).StallStopPasses);
            Func<int, MeshTetrahedralization> make = stall => new MeshTetrahedralization(TetMeshingStage2Tests.Input("cylinder")) {
                RelativeAlpha = 20, RelativeOffset = 600, SliverDihedralDegrees = 30, ImprovePasses = 20, RescuePasses = 10, StallStopPasses = stall };
            MeshTetrahedralization off = make(0);
            off.Compute(); Assert.NotNull(off.Result);
            Assert.False(off.Counts.ContainsKey("improve_stall_stop"));
            MeshTetrahedralization on = make(2);
            on.Compute(); Assert.NotNull(on.Result);
            Assert.True(on.Counts["improve_stall_stop"] >= 1);
            Assert.True(on.Counts["improve_passes"] < off.Counts["improve_passes"]);
            Assert.Null(TetMeshQuality.NotValidReason(on.Result));
            TetMesh3 first = on.Result;
            on.Compute(); Assert.NotNull(on.Result);        // reuse: the same instance again
            AssertSame(first, on.Result);
        }
    }
}
