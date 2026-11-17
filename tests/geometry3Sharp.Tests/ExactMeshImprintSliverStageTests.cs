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
            // the cylinder: a curve vertex on a split edge 1e-16 from a target vertex that is not on the edge, so the edge's
            // chain of parts passes through that target vertex after the stage
            DMesh3 a = ExactMeshImprintSliverTests.Shape("cylinder"), b = ExactMeshImprintSliverTests.Shifted(a, 0, 1);
            ExactMeshImprint now = Run(b, a, true);
            Assert.Equal(1, now.Counts["sliver_cleanup_stage"]);
            // (measured: one reroute through a target vertex, three through new vertices; deterministic)
            Assert.Equal(1, now.Counts["split_edge_reroutes_through_target_vertex"]);
            Assert.Equal(3, now.Counts["split_edge_reroutes_through_new_vertex"]);
            Assert.Equal(now.Rounding.RuleLocksReroutedThroughFixed, now.Counts["split_edge_reroutes_through_target_vertex"]);
            Assert.Equal(now.Rounding.RuleLocksReroutedThroughOther, now.Counts["split_edge_reroutes_through_new_vertex"]);
            Assert.True(now.Rounding.MaxDrift <= now.Rounding.MaxLength);
            ExactMeshImprint old = Run(b, a, false);
            Assert.Equal(0, old.Counts["sliver_cleanup_stage"]);
            Assert.Equal(0, old.Counts["split_edge_reroutes_through_target_vertex"] + old.Counts["split_edge_reroutes_through_new_vertex"]);
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
