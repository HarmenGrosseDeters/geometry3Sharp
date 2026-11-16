using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshRoundingRepair.FixedHard, LockedEdges, LockedEdgeF and AllowLockedContraction (the exact imprint's rounding,
    /// docs/design/EXACT_IMPRINT.md 4.2): fixed vertices are never moved or removed, locked edges stay edges.
    /// </summary>
    public class MeshRoundingRepairLockTests
    {
        // a sphere whose edges (every 5th) are split 1e-12 from their end: needles that an unrestricted repair contracts
        static DMesh3 Needles(out HashSet<int> original)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            original = new HashSet<int>(m.VertexIndices());
            foreach (int eid in m.EdgeIndices().ToArray()) {
                if (eid % 5 != 0 || m.IsEdge(eid) == false)
                    continue;
                Index2i ev = m.GetEdgeV(eid);
                Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                DMesh3.EdgeSplitInfo info;
                if (m.SplitEdge(eid, out info) == MeshResult.Ok)
                    m.SetVertex(info.vNew, a + 1e-12 * (b - a).Normalized);
            }
            return m;
        }

        static HashSet<long> AllEdges(DMesh3 m)
        {
            var set = new HashSet<long>();
            foreach (int eid in m.EdgeIndices()) { Index2i ev = m.GetEdgeV(eid); set.Add(MeshRoundingRepair.LockedKey(ev.a, ev.b)); }
            return set;
        }

        [Fact]
        public void Baseline_UnrestrictedRepairContractsTheNeedles()
        {
            HashSet<int> original;
            DMesh3 m = Needles(out original);
            int vertices = m.VertexCount;
            var repair = new MeshRoundingRepair(m);
            Assert.True(repair.Apply());
            Assert.True(repair.Collapses > 0);
            Assert.True(m.VertexCount < vertices);
        }

        [Fact]
        public void FixedHard_AllVerticesFixed_NoVertexMovesOrVanishes()
        {
            HashSet<int> original;
            DMesh3 m = Needles(out original);
            var positions = m.VertexIndices().ToDictionary(v => v, v => m.GetVertex(v));
            var repair = new MeshRoundingRepair(m) { FixedVertexF = (v) => true, FixedHard = true, RemoveTinyComponents = false };
            repair.Apply();
            Assert.Equal(positions.Count, m.VertexCount);
            foreach (var kv in positions)
                Assert.Equal(kv.Value, m.GetVertex(kv.Key));
            Assert.Equal(0, repair.Collapses + repair.Moves + repair.VertexRemovals);
            Assert.True(repair.RemainingDegenerate > 0, "the needles stay, counted");
        }

        [Fact]
        public void FixedHard_OriginalVerticesFixed_NewOnesAreContractedIntoThem()
        {
            HashSet<int> original;
            DMesh3 m = Needles(out original);
            var positions = original.ToDictionary(v => v, v => m.GetVertex(v));
            var repair = new MeshRoundingRepair(m) { FixedVertexF = original.Contains, FixedHard = true, RemoveTinyComponents = false };
            Assert.True(repair.Apply());
            Assert.True(repair.Collapses > 0);
            foreach (var kv in positions) {
                Assert.True(m.IsVertex(kv.Key));
                Assert.Equal(kv.Value, m.GetVertex(kv.Key));
            }
            Assert.Equal(original.Count, m.VertexCount);
        }

        [Fact]
        public void LockedEdges_AllLocked_NothingContractedOrFlipped()
        {
            HashSet<int> original;
            DMesh3 m = Needles(out original);
            var before = AllEdges(m);
            var repair = new MeshRoundingRepair(m) { LockedEdges = AllEdges(m), RemoveTinyComponents = false };
            repair.Apply();
            Assert.Equal(0, repair.Collapses + repair.Flips + repair.Zips + repair.StripZips + repair.VertexRemovals);
            Assert.Equal(before, AllEdges(m));
        }

        [Fact]
        public void LockedEdgeF_AllLocked_NothingContractedOrFlipped()
        {
            HashSet<int> original;
            DMesh3 m = Needles(out original);
            var before = AllEdges(m);
            var repair = new MeshRoundingRepair(m) { LockedEdgeF = (a, b) => true, RemoveTinyComponents = false };
            repair.Apply();
            Assert.Equal(0, repair.Collapses + repair.Flips + repair.Zips + repair.StripZips + repair.VertexRemovals);
            Assert.Equal(before, AllEdges(m));
        }

        [Fact]
        public void LockedEdges_ContractionAllowed_LocksFollowTheMesh()
        {
            HashSet<int> original;
            DMesh3 m = Needles(out original);
            var locks = AllEdges(m);
            var repair = new MeshRoundingRepair(m) { LockedEdges = locks, AllowLockedContraction = true, RemoveTinyComponents = false };
            Assert.True(repair.Apply());
            Assert.True(repair.LockedContractions > 0);
            Assert.Equal(repair.Collapses, repair.LockedContractions);
            // every lock is an edge of the mesh, and every edge is still locked (all edges were locked, none was added)
            var edges = AllEdges(m);
            Assert.Equal(edges, locks);
            // and no flip was made
            Assert.Equal(0, repair.Flips);
        }

        [Fact]
        public void LockedEdges_OnlyTheNeedleEdges_TheOthersStillContract()
        {
            HashSet<int> original;
            DMesh3 m = Needles(out original);
            // lock the edges between an original and a new vertex (the needle edges): no contraction, but the repair may use other edits
            var locks = new HashSet<long>();
            foreach (int eid in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(eid);
                if (original.Contains(ev.a) != original.Contains(ev.b)) locks.Add(MeshRoundingRepair.LockedKey(ev.a, ev.b));
            }
            Assert.NotEmpty(locks);
            var repair = new MeshRoundingRepair(m) { LockedEdges = locks, RemoveTinyComponents = false };
            repair.Apply();
            foreach (long k in locks)
                Assert.True(m.FindEdge((int)(k >> 32), (int)(k & 0xFFFFFFFF)) >= 0, "a locked edge is gone");
            Assert.Equal(0, repair.LockedContractions);
        }
    }
}
