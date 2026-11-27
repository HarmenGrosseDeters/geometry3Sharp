using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1684: a vertex reference count of DMesh3 is a short (RefCountVector, part of the 1.0.324 API): 1 for the vertex and 1 for each triangle at it, so
    /// at most 32,766 triangles. Without a check the count wrapped, FlipEdge returned Ok on a mesh in which the vertex was no longer a vertex. Every
    /// operation that gives a vertex another triangle refuses (a failure MeshResult, InvalidID from AppendTriangle, an exception from
    /// RebuildFromEdgeRefcounts) and leaves the mesh as it is. This file uses only the public API: it compiles against the base branch.
    /// </summary>
    public class DMesh3VertexRefCountLimitTests
    {
        const int Max = 32766;          // triangles at one vertex

        // A fan of n triangles (rim i, rim i + 1, c) around the vertex c, which gets the LAST vertex id (find_edge scans the edges of the smaller id: a first id
        // would make the building of the fan quadratic, 3 s). Open: the rim vertices 0 .. n; closed: the rim vertices 0 .. n - 1 and the last triangle (n - 1, 0, c).
        // The triangles are counter-clockwise seen from +z.
        static Index3i Rot(int a, int b, int c, int k) { return k == 0 ? new Index3i(a, b, c) : (k == 1 ? new Index3i(c, a, b) : new Index3i(b, c, a)); }

        static DMesh3 Fan(int n, bool closed, out int c, int rot = 0)
        {
            var m = new DMesh3();
            int rim = closed ? n : n + 1;
            for (int i = 0; i < rim; ++i) {
                double a = 2 * Math.PI * i / rim;
                m.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), 0));
            }
            c = m.AppendVertex(Vector3d.Zero);
            for (int i = 0; i < n; ++i)
                Assert.True(m.AppendTriangle(Rot(i, closed && i == n - 1 ? 0 : i + 1, c, rot)) >= 0);
            Assert.Equal(n, m.GetVtxTriangleCount(c));
            return m;
        }

        // a triangle on the rim edge (0, 1) of an open fan, on its other side: the edge becomes interior (the apex of the new triangle is a new vertex)
        static int AddNeighbourOfRimEdge(DMesh3 m, out int apex)
        {
            apex = m.AppendVertex(new Vector3d(0.5, -1.5, 0));
            return m.AppendTriangle(1, 0, apex);
        }

        static long Fingerprint(DMesh3 m)
        {
            long h = 17;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                h = h * 31 + t; h = h * 31 + tv.a; h = h * 31 + tv.b; h = h * 31 + tv.c;
            }
            return h * 31 + m.VertexCount * 7 + m.TriangleCount * 3 + m.EdgeCount + m.MaxVertexID * 5 + m.MaxTriangleID * 11 + m.MaxEdgeID;
        }

        // (CheckValidity is quadratic in the valence of the full vertex: 20 s; the fingerprint of every triangle, the counts and the vertex are checked instead)
        static void AssertUnchanged(DMesh3 m, long before, int c, int centerTriangles)
        {
            Assert.Equal(before, Fingerprint(m));
            Assert.True(m.IsVertex(c));
            Assert.Equal(centerTriangles, m.GetVtxTriangleCount(c));
            Assert.Equal(centerTriangles, m.GetVtxEdgeCount(c) - (m.IsBoundaryVertex(c) ? 1 : 0));
        }

        // the same fan of an open rim, with the triangle on its rim edge (0, 1) appended FIRST: the fan triangle is the second triangle of that edge
        static DMesh3 FanWithNeighbourFirst(int n, out int c, out int apex)
        {
            var m = new DMesh3();
            for (int i = 0; i <= n; ++i) m.AppendVertex(new Vector3d(Math.Cos(i * 1e-3), Math.Sin(i * 1e-3), 0));
            apex = m.AppendVertex(new Vector3d(0.5, -1.5, 0));
            c = m.AppendVertex(Vector3d.Zero);
            Assert.True(m.AppendTriangle(1, 0, apex) >= 0);
            for (int i = 0; i < n; ++i) Assert.True(m.AppendTriangle(i, i + 1, c) >= 0);
            Assert.Equal(n, m.GetVtxTriangleCount(c));
            return m;
        }

        // ---- FlipEdge (the repro of the issue) ----

        [Fact]
        public void FlipEdge_ThatWouldGiveAVertexItsTriangle32767_IsRefused()
        {
            DMesh3 m = Fan(Max, false, out int c);
            Assert.True(AddNeighbourOfRimEdge(m, out int apex) >= 0);      // the rim edge (0,1) is interior now and flippable: c is the apex of its first triangle
            long before = Fingerprint(m);
            MeshResult r = m.FlipEdge(0, 1, out DMesh3.EdgeFlipInfo info);
            Assert.Equal(MeshResult.Failed_HitValenceLimit, r);
            AssertUnchanged(m, before, c, Max);
        }

        [Fact]
        public void FlipEdge_ToTheLastFreeCount_Works()
        {
            DMesh3 m = Fan(Max - 1, false, out int c);                      // the vertex has one more to give
            Assert.True(AddNeighbourOfRimEdge(m, out int apex) >= 0);
            Assert.Equal(MeshResult.Ok, m.FlipEdge(0, 1, out DMesh3.EdgeFlipInfo info));
            Assert.Equal(Max, m.GetVtxTriangleCount(c));
            Assert.True(m.IsVertex(c));
            Assert.True(m.FindEdge(c, apex) >= 0);
        }

        [Fact]
        public void FlipEdge_TheOtherApexAtTheLimit_IsRefused()
        {
            DMesh3 m = FanWithNeighbourFirst(Max, out int c, out int apex);  // the full vertex is the apex d of the second triangle
            long before = Fingerprint(m);
            Assert.Equal(MeshResult.Failed_HitValenceLimit, m.FlipEdge(0, 1, out DMesh3.EdgeFlipInfo info));
            AssertUnchanged(m, before, c, Max);
        }

        // ---- AppendTriangle / InsertTriangle ----

        [Fact]
        public void AppendTriangle_ThatWouldGiveAVertexItsTriangle32767_ReturnsInvalidID()
        {
            DMesh3 m = Fan(Max, true, out int c);
            int p = m.AppendVertex(new Vector3d(5, 5, 0)), q = m.AppendVertex(new Vector3d(6, 5, 0));
            long before = Fingerprint(m);
            for (int k = 0; k < 3; ++k)                          // the full vertex at each corner
                Assert.Equal(DMesh3.InvalidID, m.AppendTriangle(Rot(p, q, c, k)));
            AssertUnchanged(m, before, c, Max);
            // another vertex is not affected
            Assert.True(m.AppendTriangle(p, q, m.AppendVertex(new Vector3d(5, 6, 0))) >= 0);
        }

        [Fact]
        public void AppendTriangle_ToTheLastFreeCount_Works()
        {
            DMesh3 m = Fan(Max - 1, true, out int c);
            int p = m.AppendVertex(new Vector3d(5, 5, 0)), q = m.AppendVertex(new Vector3d(6, 5, 0));
            Assert.True(m.AppendTriangle(p, q, c) >= 0);
            Assert.Equal(Max, m.GetVtxTriangleCount(c));
            Assert.True(m.IsVertex(c));
        }

        [Fact]
        public void InsertTriangle_ThatWouldGiveAVertexItsTriangle32767_IsRefused()
        {
            DMesh3 m = Fan(Max, true, out int c);
            int p = m.AppendVertex(new Vector3d(5, 5, 0)), q = m.AppendVertex(new Vector3d(6, 5, 0));
            long before = Fingerprint(m);
            for (int k = 0; k < 3; ++k)
                Assert.Equal(MeshResult.Failed_HitValenceLimit, m.InsertTriangle(m.MaxTriangleID, Rot(p, q, c, k)));
            AssertUnchanged(m, before, c, Max);
        }

        // ---- SplitEdge (the guards are older: pinned here, with the cases of both branches) ----

        [Fact]
        public void SplitEdge_OnTheBoundary_ThatWouldOverfillTheApex_IsRefused()
        {
            DMesh3 m = Fan(Max, false, out int c);
            long before = Fingerprint(m);
            Assert.Equal(MeshResult.Failed_HitValenceLimit, m.SplitEdge(0, 1, out DMesh3.EdgeSplitInfo info));
            AssertUnchanged(m, before, c, Max);
        }

        [Fact]
        public void SplitEdge_Interior_ThatWouldOverfillAnApex_IsRefused()
        {
            // the apex c of the first triangle
            DMesh3 m = Fan(Max, false, out int c);
            Assert.True(AddNeighbourOfRimEdge(m, out int apex) >= 0);
            long before = Fingerprint(m);
            Assert.Equal(MeshResult.Failed_HitValenceLimit, m.SplitEdge(0, 1, out DMesh3.EdgeSplitInfo info));
            AssertUnchanged(m, before, c, Max);
        }

        [Fact]
        public void SplitEdge_Interior_ThatWouldOverfillTheOtherApex_IsRefused()
        {
            DMesh3 n = FanWithNeighbourFirst(Max, out int c, out int apex);
            long before = Fingerprint(n);
            Assert.Equal(MeshResult.Failed_HitValenceLimit, n.SplitEdge(0, 1, out DMesh3.EdgeSplitInfo info));
            AssertUnchanged(n, before, c, Max);
        }

        // ---- PokeTriangle / SetTriangle ----

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void PokeTriangle_AtAFullVertex_IsRefused_AndLeavesNoNewVertex(int corner)
        {
            DMesh3 m = Fan(Max, true, out int c, corner);              // the full vertex at corner 'corner' of the triangle
            long before = Fingerprint(m);
            Assert.Equal(MeshResult.Failed_HitValenceLimit, m.PokeTriangle(0, out DMesh3.PokeTriangleInfo info));
            AssertUnchanged(m, before, c, Max);
        }

        [Fact]
        public void PokeTriangle_AtTheLastFreeCount_Works()
        {
            DMesh3 m = Fan(Max - 1, true, out int c);
            Assert.Equal(MeshResult.Ok, m.PokeTriangle(0, out DMesh3.PokeTriangleInfo info));
            Assert.Equal(Max, m.GetVtxTriangleCount(c));
            Assert.True(m.IsVertex(c));
        }

        [Fact]
        public void SetTriangle_ThatWouldGiveAFullVertexAnotherCorner_IsRefused()
        {
            DMesh3 m = Fan(Max, true, out int c);
            int x = m.AppendVertex(new Vector3d(5, 5, 0)), y = m.AppendVertex(new Vector3d(6, 5, 0)), z = m.AppendVertex(new Vector3d(5, 6, 0));
            int t = m.AppendTriangle(x, y, z);
            Assert.True(t >= 0);
            long before = Fingerprint(m);
            for (int k = 0; k < 3; ++k)
                Assert.Equal(MeshResult.Failed_HitValenceLimit, m.SetTriangle(t, Rot(x, y, c, k)));
            AssertUnchanged(m, before, c, Max);
        }

        // ---- CollapseEdge / MergeEdges ----

        [Fact]
        public void CollapseEdge_ThatMovesTrianglesToAFullVertex_IsRefused()
        {
            DMesh3 m = Fan(Max, true, out int c);
            // the rim vertex 0 gets four more triangles on its own (a second fan at the vertex)
            int[] p = new int[5];
            for (int i = 0; i < 5; ++i) p[i] = m.AppendVertex(new Vector3d(3 + i, 3, 0));
            for (int i = 0; i < 4; ++i) Assert.True(m.AppendTriangle(0, p[i], p[i + 1]) >= 0);
            long before = Fingerprint(m);
            Assert.Equal(MeshResult.Failed_HitValenceLimit, m.CollapseEdge(c, 0, out DMesh3.EdgeCollapseInfo info));
            AssertUnchanged(m, before, c, Max);
        }

        [Fact]
        public void MergeEdges_ThatMovesTrianglesToAFullVertex_IsRefused()
        {
            DMesh3 m = Fan(Max, false, out int c);                // open: c has the limit, and its edge (c, 0) is a boundary edge
            // a triangle (q, p, s) whose boundary edge q -> p has to be joined with the boundary edge c -> 0 of the first fan triangle (0, 1, c): p lies where c is, q where 0 is
            // (opposite directions, as MergeEdges needs), so c takes over the triangle of p
            int p = m.AppendVertex(m.GetVertex(c)), q = m.AppendVertex(m.GetVertex(0)), s = m.AppendVertex(new Vector3d(0.5, -1.5, 0));
            Assert.True(m.AppendTriangle(q, p, s) >= 0);
            int eKeep = m.FindEdge(0, c), eDiscard = m.FindEdge(p, q);
            Assert.True(eKeep >= 0 && eDiscard >= 0);
            Assert.True(m.IsBoundaryEdge(eKeep) && m.IsBoundaryEdge(eDiscard));
            long before = Fingerprint(m);
            Assert.Equal(MeshResult.Failed_HitValenceLimit, m.MergeEdges(eKeep, eDiscard, out DMesh3.MergeEdgesInfo info));
            AssertUnchanged(m, before, c, Max);
        }

        // ---- RebuildFromEdgeRefcounts (the deserializer): no result, so it throws ----

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void RebuildFromEdgeRefcounts_ForAVertexWithTooManyTriangles_Throws(int corner)
        {
            DMesh3 m = Fan(Max, true, out int c);
            // a separate triangle u, with its corner 'corner' and the two edges at it rewritten to c in the buffers: the 32,767th triangle at c
            int[] u = { m.AppendVertex(new Vector3d(5, 5, 0)), m.AppendVertex(new Vector3d(6, 5, 0)), m.AppendVertex(new Vector3d(5, 6, 0)) };
            int t = m.AppendTriangle(u[0], u[1], u[2]);
            Assert.True(t >= 0);
            int k = corner, next = u[(k + 1) % 3], prev = u[(k + 2) % 3];
            int e1 = m.FindEdge(u[k], next), e2 = m.FindEdge(prev, u[k]);
            m.TrianglesBuffer[3 * t + k] = c;
            m.EdgesBuffer[4 * e1] = Math.Min(next, c); m.EdgesBuffer[4 * e1 + 1] = Math.Max(next, c);
            m.EdgesBuffer[4 * e2] = Math.Min(prev, c); m.EdgesBuffer[4 * e2 + 1] = Math.Max(prev, c);
            Assert.Throws<InvalidOperationException>(() => m.RebuildFromEdgeRefcounts());
        }

        [Fact]
        public void RebuildFromEdgeRefcounts_AtTheLimit_Works()
        {
            DMesh3 m = Fan(Max, true, out int c);
            long before = Fingerprint(m);
            m.RebuildFromEdgeRefcounts();
            Assert.Equal(before, Fingerprint(m));
            Assert.Equal(Max, m.GetVtxTriangleCount(c));
            Assert.True(m.IsVertex(c));
        }

        [Fact]
        public void MergeEdges_ThatMovesTrianglesToAFullSecondVertex_IsRefused()
        {
            DMesh3 m = Fan(Max, false, out int c);                // open: the last triangle (n - 1, n, c) has the boundary edge n -> c; c has the limit and is its end b
            // a triangle (q, p, s) whose boundary edge q -> p has to be joined with it: p lies where vertex n is, q where c is: c takes over the triangle of q
            int p = m.AppendVertex(m.GetVertex(Max)), q = m.AppendVertex(m.GetVertex(c)), s = m.AppendVertex(new Vector3d(0.5, -1.5, 0));
            Assert.True(m.AppendTriangle(q, p, s) >= 0);
            int eKeep = m.FindEdge(Max, c), eDiscard = m.FindEdge(p, q);
            Assert.True(eKeep >= 0 && eDiscard >= 0);
            Assert.True(m.IsBoundaryEdge(eKeep) && m.IsBoundaryEdge(eDiscard));
            long before = Fingerprint(m);
            Assert.Equal(MeshResult.Failed_HitValenceLimit, m.MergeEdges(eKeep, eDiscard, out DMesh3.MergeEdgesInfo info));
            AssertUnchanged(m, before, c, Max);
        }

        // ---- NTMesh3 ----

        [Fact]
        public void NTMesh3_AppendTriangle_ThatWouldGiveAVertexItsTriangle32767_ReturnsInvalidID()
        {
            var m = new NTMesh3();
            for (int i = 0; i <= Max; ++i) m.AppendVertex(new Vector3d(Math.Cos(i * 1e-3), Math.Sin(i * 1e-3), 0));
            int c = m.AppendVertex(Vector3d.Zero);
            for (int i = 0; i < Max; ++i) Assert.True(m.AppendTriangle(i, i + 1, c) >= 0);
            int p = m.AppendVertex(new Vector3d(5, 5, 0)), q = m.AppendVertex(new Vector3d(6, 5, 0));
            int triangles = m.TriangleCount;
            for (int k = 0; k < 3; ++k)                          // the full vertex at each corner
                Assert.Equal(NTMesh3.InvalidID, m.AppendTriangle(Rot(p, q, c, k)));
            Assert.Equal(triangles, m.TriangleCount);
            Assert.True(m.IsVertex(c));
        }
    }
}
