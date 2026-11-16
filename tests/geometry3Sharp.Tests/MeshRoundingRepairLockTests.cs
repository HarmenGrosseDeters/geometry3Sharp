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

        [Fact]
        public void LockTransfer_ContractedEdgeWithTwoLockedNeighbours_KeepsTheLockOfBoth()
        {
            // a triangle fan: contracting (a, b) where both a-x and b-x are locked leaves b-x locked; a-x is gone
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1e-13, 0, 0)), x = m.AppendVertex(new Vector3d(0, 1, 0)), y = m.AppendVertex(new Vector3d(0, -1, 0));
            m.AppendTriangle(a, b, x); m.AppendTriangle(b, a, y);
            var locks = new HashSet<long>() { MeshRoundingRepair.LockedKey(a, x), MeshRoundingRepair.LockedKey(b, x), MeshRoundingRepair.LockedKey(a, b) };
            var repair = new MeshRoundingRepair(m) { LockedEdges = locks, AllowLockedContraction = true, RemoveTinyComponents = false };
            repair.Apply();
            foreach (long k in locks)
                Assert.True(m.FindEdge((int)(k >> 32), (int)(k & 0xFFFFFFFF)) >= 0, "a lock outlived its edge");
        }

        [Fact]
        public void FixedHard_ValenceThreeFixedVertex_IsNotRemoved()
        {
            // a vertex of valence 3 inside a triangle (a tiny cap fan): fixed hard, it stays
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(4, 0, 0)), c = m.AppendVertex(new Vector3d(0, 4, 0));
            int u = m.AppendVertex(new Vector3d(1, 1, 1e-14));
            m.AppendTriangle(a, b, u); m.AppendTriangle(b, c, u); m.AppendTriangle(c, a, u);
            var repair = new MeshRoundingRepair(m) { FixedVertexF = (v) => true, FixedHard = true, RemoveTinyComponents = false };
            repair.Apply();
            Assert.True(m.IsVertex(u));
            Assert.Equal(3, m.TriangleCount);
            Assert.Equal(0, repair.VertexRemovals);
        }

        [Fact]
        public void LockedEdges_TheTriangleOfAZip_IsLeftAlone()
        {
            // two caps on both sides of an edge whose apexes lie on it (the zip case): with the edge locked nothing changes
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(4, 0, 0));
            int c = m.AppendVertex(new Vector3d(1, 1e-13, 0)), d = m.AppendVertex(new Vector3d(3, -1e-13, 0));
            int x = m.AppendVertex(new Vector3d(2, 2, 0)), y = m.AppendVertex(new Vector3d(2, -2, 0));
            m.AppendTriangle(a, b, c); m.AppendTriangle(b, a, d);
            m.AppendTriangle(b, x, c); m.AppendTriangle(c, x, a); m.AppendTriangle(a, y, d); m.AppendTriangle(d, y, b);
            var before = AllEdges(m);
            var repair = new MeshRoundingRepair(m) { LockedEdges = AllEdges(m), RemoveTinyComponents = false };
            repair.Apply();
            Assert.Equal(0, repair.Zips + repair.StripZips + repair.Flips + repair.Collapses);
            Assert.Equal(before, AllEdges(m));
        }

        [Fact]
        public void LockedEdgeF_ARuleLockedEdgeIsNotLostByAContractionIntoAVertexOffTheLine()
        {
            // a on the line y = 0 between u and v, with x 1e-13 above it: contracting a into x would drop the rule-locked edges u-a and a-v
            var m = new DMesh3();
            int u = m.AppendVertex(new Vector3d(0, 0, 0)), a = m.AppendVertex(new Vector3d(2, 0, 0)), v = m.AppendVertex(new Vector3d(4, 0, 0));
            int x = m.AppendVertex(new Vector3d(2, 1e-13, 0)), y = m.AppendVertex(new Vector3d(2, -2, 0)), p = m.AppendVertex(new Vector3d(2, 2, 0));
            m.AppendTriangle(u, a, x); m.AppendTriangle(a, v, x); m.AppendTriangle(u, x, p); m.AppendTriangle(x, v, p);
            m.AppendTriangle(a, u, y); m.AppendTriangle(v, a, y);
            var repair = new MeshRoundingRepair(m) { LockedEdgeF = (s, t) => m.GetVertex(s).y == 0 && m.GetVertex(t).y == 0, AllowLockedContraction = true, RemoveTinyComponents = false };
            repair.Apply();
            Assert.True(m.IsVertex(a), "the vertex on the line was contracted into the one off it");
            Assert.True(m.FindEdge(u, a) >= 0 && m.FindEdge(a, v) >= 0);
        }
    }
}
