using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The final stage of the imprint's repair (#1034, docs/design/IMPRINT_SLIVER_CLEANUP.md): the parts of split target edges
    /// follow a contraction (MeshRoundingRepair.RuleLockedEdges, RuleLocksFollow) only where the repair leaves a defect without it.
    /// Uses the internal switch ExactMeshImprint.SliverCleanup.
    /// </summary>
    public class ExactMeshImprintSliverStageTests
    {
        static ExactMeshImprint Run(DMesh3 target, DMesh3 cutter, bool cleanup)
        {
            var imprint = new ExactMeshImprint() { Target = target, Cutter = cutter, SliverCleanup = cleanup };
            Assert.True(imprint.Compute(), imprint.Error);
            return imprint;
        }

        static bool Same(DMesh3 a, DMesh3 b) =>
            a.MaxVertexID == b.MaxVertexID && a.MaxTriangleID == b.MaxTriangleID
            && a.VertexIndices().All(v => b.IsVertex(v) && a.GetVertex(v) == b.GetVertex(v))
            && a.TriangleIndices().All(t => b.IsTriangle(t) && a.GetTriangle(t) == b.GetTriangle(t));

        [Theory]
        [MemberData(nameof(ExactMeshImprintSliverTests.Cases), MemberType = typeof(ExactMeshImprintSliverTests))]
        public void WithoutTheStage_SliversAreLeft_WithItTheResultIsClean(string kind, int axis, int ulps)
        {
            DMesh3 a = ExactMeshImprintSliverTests.Shape(kind), b = ExactMeshImprintSliverTests.Shifted(a, axis, ulps);
            foreach (var (t, c) in new[] { (a, b), (b, a) }) {
                ExactMeshImprint old = Run(t, c, false);
                Assert.False(old.RoundingClean);
                Assert.Equal("degenerate triangles", MeshRobustRepair.NotCleanReason(old.Result));
                Assert.False(old.Rounding.FollowStageRan);
                ExactMeshImprint now = Run(t, c, true);
                Assert.True(now.Rounding.FollowStageRan);
                ExactMeshImprintSliverTests.AssertCleanAndCarried(t, now);
                // the stage contracts parts of split target edges (counted as before the change)
                Assert.True(now.Counts["target_edge_parts_contracted"] > old.Counts["target_edge_parts_contracted"]);
            }
        }

        // a capped cone frustum with five slices (snapped to a grid of 2^-24 like ExactMeshImprintSliverTests.Shape: the same input on every runtime)
        static DMesh3 FiveSliceCylinder()
        {
            DMesh3 m = new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = 0.6f, Height = 2f, Slices = 5 }.Generate().MakeDMesh();
            const double grid = 1.0 / (1 << 24);
            foreach (int v in m.VertexIndices().ToArray()) {
                Vector3d q = m.GetVertex(v);
                m.SetVertex(v, new Vector3d(Math.Round(q.x / grid) * grid, Math.Round(q.y / grid) * grid, Math.Round(q.z / grid) * grid));
            }
            return m;
        }

        [Theory]
        [InlineData(3, true)]
        [InlineData(-3, false)]
        public void TheStage_SearchesAgain_AndTheListsOfDefectsAreRebuiltAfterItsMoves(int ulps, bool targetIsTheUnshifted)
        {
            // the copy shifted by 3 ulp along z: the stage's contractions leave a defect that the position search of the stage fixes;
            // the defect lists are then rebuilt and the passes run again (without that, the result is not clean)
            DMesh3 a = FiveSliceCylinder(), b = ExactMeshImprintSliverTests.Shifted(a, 2, ulps);
            DMesh3 target = targetIsTheUnshifted ? a : b, cutter = targetIsTheUnshifted ? b : a;
            ExactMeshImprint old = Run(target, cutter, false);
            Assert.False(old.RoundingClean);
            Assert.False(old.Rounding.FollowStageRan);
            ExactMeshImprint now = Run(target, cutter, true);
            Assert.True(now.Rounding.FollowStageRan);
            Assert.True(now.Rounding.PositionSearchMoves > 0);
            ExactMeshImprintSliverTests.AssertCleanAndCarried(target, now);
            Assert.Equal(0, now.Rounding.RemainingDegenerate);
            Assert.Equal(0, now.Rounding.RemainingIntersections);
        }

        // inputs whose result is clean without the stage: the same result bit for bit, and the stage does not run
        public static IEnumerable<object[]> CleanToday()
        {
            yield return new object[] { "cubes" };
            yield return new object[] { "spheres" };
            yield return new object[] { "box-shift-x" };
            yield return new object[] { "sphere-shift" };
            yield return new object[] { "cylinder-shift-y" };
        }

        static void CleanTodayPair(string name, out DMesh3 target, out DMesh3 cutter)
        {
            switch (name) {
                case "cubes":
                    ExactMeshImprintTests.Pair(Similarity.Identity, out target, out cutter);
                    return;
                case "spheres":
                    target = ExactMeshImprintTests.Sphere(Vector3d.Zero, 1, 12);
                    cutter = ExactMeshImprintTests.Sphere(new Vector3d(0.6, 0.2, 0.1), 0.8, 12);
                    return;
                case "box-shift-x":
                    target = ExactMeshImprintSliverTests.Shape("box");
                    cutter = ExactMeshImprintSliverTests.Shifted(target, 0, 1);
                    return;
                case "sphere-shift":
                    target = ExactMeshImprintTests.Sphere(Vector3d.Zero, 1, 5);
                    cutter = ExactMeshImprintSliverTests.Shifted(target, 1, 1);
                    return;
                default:
                    target = ExactMeshImprintSliverTests.Shape("cylinder");
                    cutter = ExactMeshImprintSliverTests.Shifted(target, 1, 1);
                    return;
            }
        }

        [Theory]
        [MemberData(nameof(CleanToday))]
        public void CleanWithoutTheStage_IsUnchanged(string name)
        {
            CleanTodayPair(name, out DMesh3 target, out DMesh3 cutter);
            ExactMeshImprint old = Run(target, cutter, false), now = Run(target, cutter, true);
            Assert.True(old.RoundingClean);
            Assert.False(now.Rounding.FollowStageRan);
            Assert.True(Same(old.Result, now.Result));
            Assert.Equal(old.CurveEdges.OrderBy(x => x), now.CurveEdges.OrderBy(x => x));
            Assert.Equal(old.SourceTriangle, now.SourceTriangle);
            Assert.Equal(old.Counts.OrderBy(kv => kv.Key), now.Counts.OrderBy(kv => kv.Key));
        }

        // before the stage the lock set answers as the old rule did (one LockedEdgeF over the fixed set of parts): the repair without
        // the stage equals the repair with the old locks bit for bit, on inputs it leaves unclean and on inputs it cleans
        public static IEnumerable<object[]> AllPairs()
        {
            foreach (object[] c in ExactMeshImprintSliverTests.Cases()) yield return new object[] { (string)c[0] + "," + c[1] + "," + c[2] };
            foreach (object[] c in CleanToday()) yield return c;
        }

        [Theory]
        [MemberData(nameof(AllPairs))]
        public void BeforeTheStage_TheLockSetEqualsTheOldRule_BitForBit(string name)
        {
            DMesh3 target, cutter;
            string[] f = name.Split(',');
            if (f.Length == 3) {
                target = ExactMeshImprintSliverTests.Shape(f[0]);
                cutter = ExactMeshImprintSliverTests.Shifted(target, int.Parse(f[1]), int.Parse(f[2]));
            } else
                CleanTodayPair(name, out target, out cutter);
            foreach (var (t, c) in new[] { (target, cutter), (cutter, target) }) {
                ExactMeshImprint now = Run(t, c, false);
                var legacy = new ExactMeshImprint() { Target = t, Cutter = c, LegacyPartLocks = true };
                Assert.True(legacy.Compute(), legacy.Error);
                Assert.Null(legacy.Rounding.RuleLockedEdges);
                Assert.True(Same(legacy.Result, now.Result));
                Assert.Equal(legacy.CurveEdges.OrderBy(x => x), now.CurveEdges.OrderBy(x => x));
                Assert.Equal(legacy.SourceTriangle, now.SourceTriangle);
                Assert.Equal(legacy.Counts.OrderBy(kv => kv.Key), now.Counts.OrderBy(kv => kv.Key));
                Assert.Equal(legacy.RoundingClean, now.RoundingClean);
                Assert.Equal(legacy.Rounding.RemainingDegenerate, now.Rounding.RemainingDegenerate);
            }
        }

        [Fact]
        public void ExactMeshBoolean_DoesNotSetTheRuleLocks_TheStageCannotRun()
        {
            // the documented unclean case of the boolean (RoundingExactPairsTests: FoldedCluster stays unclean): defects are left,
            // so the stage would run if the boolean enabled it; it sets no RuleLockedEdges, so its outputs are those of before #1034
            DMesh3 m = SafeRoundingDegenerateInputTests.Make(SafeRoundingDegenerateInputTests.Family.FoldedCluster, 36, 1);
            var b = new ExactMeshBoolean(m, new DMesh3(), ExactMeshBoolean.Operations.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.False(b.RoundingClean);
            Assert.True(b.Rounding.RemainingDegenerate + b.Rounding.RemainingIntersections > 0);
            Assert.Null(b.Rounding.RuleLockedEdges);
            Assert.False(b.Rounding.RuleLocksFollow);
            Assert.False(b.Rounding.FollowStageRan);
            Assert.Equal(0, b.Rounding.RuleLocksReroutedThroughFixed + b.Rounding.RuleLocksReroutedThroughOther);
        }

        [Fact]
        public void Counts_ReportTheStage_TheReroutes_AndTheDriftWithinTheBound()
        {
            // the cylinder shifted by -1 ulp along x: curve vertices on split edges next to vertices off those edges, so chains of
            // parts pass through them after the stage (through target vertices and through new ones)
            DMesh3 a = ExactMeshImprintSliverTests.Shape("cylinder"), b = ExactMeshImprintSliverTests.Shifted(a, 0, -1);
            ExactMeshImprint now = Run(a, b, true);
            Assert.Equal(1, now.Counts["sliver_cleanup_stage"]);
            // (measured: two reroutes through target vertices, one through a new vertex; the input is libm-free, the same on every runtime)
            Assert.Equal(2, now.Counts["split_edge_reroutes_through_target_vertex"]);
            Assert.Equal(1, now.Counts["split_edge_reroutes_through_new_vertex"]);
            Assert.Equal(now.Rounding.RuleLocksReroutedThroughFixed, now.Counts["split_edge_reroutes_through_target_vertex"]);
            Assert.Equal(now.Rounding.RuleLocksReroutedThroughOther, now.Counts["split_edge_reroutes_through_new_vertex"]);
            Assert.True(now.Rounding.MaxDrift <= now.Rounding.MaxLength);
            ExactMeshImprint old = Run(a, b, false);
            Assert.Equal(0, old.Counts["sliver_cleanup_stage"]);
            Assert.Equal(0, old.Counts["split_edge_reroutes_through_target_vertex"] + old.Counts["split_edge_reroutes_through_new_vertex"]);
        }

        [Fact]
        public void KnownLimit_ALinkConditionLeavesOneSliver_TheResultStaysClosedAndCarriesTheCurve()
        {
            // the cylinder shifted by -1 ulp along z, cut into the shifted copy: one sliver is left. Its short edge (1.4e-11, a curve
            // edge and a part of a split edge) cannot be contracted: the contraction breaks the link condition (DMesh3.CollapseEdge
            // refuses it), and the other edits at the sliver add a degenerate triangle or an intersection, or improve nothing
            // (docs/design/IMPRINT_SLIVER_CLEANUP.md, section 5). Not clean, as before the stage; the rest of the guarantees hold
            DMesh3 a = ExactMeshImprintSliverTests.Shape("cylinder"), b = ExactMeshImprintSliverTests.Shifted(a, 2, -1);
            ExactMeshImprint now = Run(b, a, true);
            Assert.True(now.Rounding.FollowStageRan);
            Assert.False(now.RoundingClean);
            Assert.Equal(1, now.Rounding.RemainingDegenerate);
            Assert.Equal(0, now.Rounding.RemainingIntersections);
            Assert.False(now.Rounding.EvaluationLimitReached);
            Assert.True(now.Result.IsClosed());
            foreach (int v in b.VertexIndices())
                Assert.Equal(b.GetVertex(v), now.Result.GetVertex(v));
            Assert.NotEmpty(now.CurveEdges);
            foreach (long k in now.CurveEdges)
                Assert.True(now.Result.FindEdge((int)(k >> 32), (int)(k & 0xFFFFFFFF)) >= 0);
            Assert.True(now.Rounding.MaxDrift <= now.Rounding.MaxLength);
        }

        // ---- MeshRoundingRepair alone ----

        // two triangles on edge (p, q), split at v 1e-14 from p: the triangles (p, v, r) and (v, p, s) are degenerate; the
        // parts (p, v), (v, q) are rule-locked and p, q, r, s fixed. With MaxDisplacement 1e-13 no move gets v far enough
        // from p; the contraction of v into p is the repair, and it needs the lock of (v, q) to move to (p, q)
        static MeshRoundingRepair SplitEdge(bool follow, out int p, out int q, out int v)
        {
            var m = new DMesh3();
            p = m.AppendVertex(new Vector3d(0, 0, 0));
            q = m.AppendVertex(new Vector3d(1, 0, 0));
            int r = m.AppendVertex(new Vector3d(0.5, 1, 0)), s = m.AppendVertex(new Vector3d(0.5, -1, 0));
            v = m.AppendVertex(new Vector3d(1e-14, 0, 0));
            m.AppendTriangle(p, v, r); m.AppendTriangle(v, q, r);
            m.AppendTriangle(v, p, s); m.AppendTriangle(q, v, s);
            int fixedCount = 4;
            var parts = new HashSet<long>() { MeshRoundingRepair.LockedKey(p, v), MeshRoundingRepair.LockedKey(v, q) };
            var repair = new MeshRoundingRepair(m) {
                FixedVertexF = (x) => x < fixedCount, FixedHard = true, RemoveTinyComponents = false,
                AllowLockedContraction = true, MaxDisplacement = 1e-13,
                RuleLockedEdges = parts, RuleLocksFollow = follow
            };
            return repair;
        }

        [Fact]
        public void RuleLockedEdges_Following_DoesNotLoseALockOfLockedEdgeF()
        {
            // (v, q) locked by the fixed rule instead of the set: that lock cannot follow, so v is not contracted, also in the stage
            MeshRoundingRepair repair = SplitEdge(true, out int p, out int q, out int v);
            repair.RuleLockedEdges.Remove(MeshRoundingRepair.LockedKey(v, q));
            int vv = v, qq = q;
            repair.LockedEdgeF = (x, y) => MeshRoundingRepair.LockedKey(x, y) == MeshRoundingRepair.LockedKey(vv, qq);
            Assert.False(repair.Apply());
            Assert.True(repair.FollowStageRan);
            Assert.True(repair.Mesh.IsVertex(v));
            Assert.Equal(2, repair.RemainingDegenerate);
        }

        [Fact]
        public void RuleLockedEdges_WithoutFollowing_TheSliverStays()
        {
            MeshRoundingRepair repair = SplitEdge(false, out int p, out int q, out int v);
            Assert.False(repair.Apply());
            Assert.Equal(2, repair.RemainingDegenerate);
            Assert.False(repair.FollowStageRan);
            Assert.True(repair.Mesh.IsVertex(v));
            // the set is not changed outside the stage
            Assert.Equal(2, repair.RuleLockedEdges.Count);
        }

        [Fact]
        public void RuleLockedEdges_Following_ContractsIntoTheEnd_AndTheLockMoves()
        {
            MeshRoundingRepair repair = SplitEdge(true, out int p, out int q, out int v);
            Assert.True(repair.Apply());
            Assert.True(repair.FollowStageRan);
            Assert.False(repair.Mesh.IsVertex(v));
            Assert.Equal(2, repair.Mesh.TriangleCount);
            Assert.Equal(new Vector3d(0, 0, 0), repair.Mesh.GetVertex(p));
            // the contracted part is gone, the other one now ends at p
            Assert.Equal(new HashSet<long>() { MeshRoundingRepair.LockedKey(p, q) }, repair.RuleLockedEdges);
            Assert.Equal(1, repair.RuleLockedContractions);
            Assert.Equal(0, repair.LockedContractions);
            // (a contraction along a part, into the edge's own end: no reroute)
            Assert.Equal(0, repair.RuleLocksReroutedThroughFixed + repair.RuleLocksReroutedThroughOther);
            // (a locked edge is not flipped: the edge p-q stays)
            Assert.True(repair.Mesh.FindEdge(p, q) >= 0);
        }
    }
}
