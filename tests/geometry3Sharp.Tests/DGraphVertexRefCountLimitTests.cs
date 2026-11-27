using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1695: a DGraph vertex count is a short (RefCountVector: 1 for the vertex and 1 for each edge), so a vertex has at
    /// most 32,766 edges. AppendEdge and CollapseEdge, the operations that give a vertex more edges, refuse before they
    /// change anything (InvalidID, MeshResult.Failed_HitValenceLimit); on master the count wrapped and the hub stopped
    /// being a vertex. SplitEdge keeps the counts of its ends. Master API only.
    /// </summary>
    public class DGraphVertexRefCountLimitTests
    {
        const int MaxEdges = short.MaxValue - 1;        // 32,766

        // a star: `leaves` leaves first, then the hub (the last id: FindEdge scans the edges of the smaller id, so the
        // build stays linear), with `spokes` edges hub-leaf
        static DGraph2 Star(int leaves, int spokes, out int hub)
        {
            var g = new DGraph2();
            for (int i = 0; i < leaves; ++i)
                g.AppendVertex(new Vector2d(Math.Cos(i), Math.Sin(i)));
            hub = g.AppendVertex(Vector2d.Zero);
            for (int i = 0; i < spokes; ++i)
                Assert.True(g.AppendEdge(i, hub) >= 0);
            return g;
        }

        static string Fingerprint(DGraph2 g, int v)
        {
            return g.VertexCount + "/" + g.EdgeCount + "/" + g.GetVtxEdgeCount(v) + "/" + g.IsVertex(v);
        }

        [Fact]
        public void AppendEdge_AtTheLimit_IsRefused_AndTheGraphUnchanged()
        {
            int hub;
            DGraph2 g = Star(MaxEdges + 2, MaxEdges, out hub);
            Assert.Equal(MaxEdges, g.GetVtxEdgeCount(hub));
            Assert.True(g.IsVertex(hub));
            string before = Fingerprint(g, hub);
            // the 32,767th edge at the hub, with the hub at either end
            Assert.Equal(DGraph.InvalidID, g.AppendEdge(MaxEdges, hub));
            Assert.Equal(DGraph.InvalidID, g.AppendEdge(hub, MaxEdges + 1));
            Assert.Equal(before, Fingerprint(g, hub));
            Assert.True(g.IsVertex(hub));
            // an edge between two leaves still works, and so does the hub's edge after one is removed
            Assert.True(g.AppendEdge(MaxEdges, MaxEdges + 1) >= 0);
            Assert.Equal(MeshResult.Ok, g.RemoveEdge(g.FindEdge(0, hub), false));
            Assert.True(g.AppendEdge(MaxEdges, hub) >= 0);
            Assert.Equal(MaxEdges, g.GetVtxEdgeCount(hub));
        }

        [Fact]
        public void AppendEdge_BelowTheLimit_Works()
        {
            int hub;
            DGraph2 g = Star(MaxEdges, MaxEdges - 1, out hub);
            Assert.True(g.AppendEdge(MaxEdges - 1, hub) >= 0);
            Assert.Equal(MaxEdges, g.GetVtxEdgeCount(hub));
            Assert.True(g.IsVertex(hub));
        }

        // a hub with `spokes` edges (one of them to a), and a with `others` more edges to new vertices (none of them at
        // the hub) and `shared` edges to leaves that the hub has an edge to too (removed by the collapse, not moved)
        static DGraph2 CollapseCase(int spokes, int others, int shared, out int hub, out int a)
        {
            var g = new DGraph2();
            for (int i = 0; i < spokes + others + 1; ++i)
                g.AppendVertex(new Vector2d(Math.Cos(i), Math.Sin(i)));
            a = spokes + others;            // (before the hub: its edges are few)
            hub = g.AppendVertex(Vector2d.Zero);
            for (int i = 0; i < spokes - 1; ++i)
                Assert.True(g.AppendEdge(i, hub) >= 0);
            Assert.True(g.AppendEdge(a, hub) >= 0);
            for (int i = 0; i < others; ++i)
                Assert.True(g.AppendEdge(spokes - 1 + i, a) >= 0);
            for (int i = 0; i < shared; ++i)
                Assert.True(g.AppendEdge(i, a) >= 0);
            return g;
        }

        [Fact]
        public void CollapseEdge_KeptVertexWouldPassTheLimit_IsRefused_AndTheGraphUnchanged()
        {
            // the hub keeps: 32,765 edges (ab among them) + the 2 edges of a moved = 32,767 at the peak: refused
            int hub, a;
            DGraph2 g = CollapseCase(MaxEdges - 1, 2, 0, out hub, out a);
            string before = Fingerprint(g, hub) + "|" + Fingerprint(g, a);
            Assert.Equal(MeshResult.Failed_HitValenceLimit, g.CollapseEdge(hub, a, out DGraph.EdgeCollapseInfo info));
            Assert.Equal(before, Fingerprint(g, hub) + "|" + Fingerprint(g, a));
            Assert.True(g.IsVertex(hub) && g.IsVertex(a));
        }

        [Fact]
        public void CollapseEdge_AtTheLimit_Works()
        {
            // 32,764 edges + 2 moved = 32,766 at the peak (32,765 after ab goes): allowed
            int hub, a;
            DGraph2 g = CollapseCase(MaxEdges - 2, 2, 0, out hub, out a);
            Assert.Equal(MeshResult.Ok, g.CollapseEdge(hub, a, out DGraph.EdgeCollapseInfo info));
            Assert.True(g.IsVertex(hub));
            Assert.False(g.IsVertex(a));
            Assert.Equal(MaxEdges - 1, g.GetVtxEdgeCount(hub));
        }

        [Fact]
        public void CollapseEdge_EdgesToSharedNeighbours_AreNotCounted()
        {
            // a full hub (32,766 edges): a's edges to leaves the hub already has are removed, not moved, so the hub's
            // count does not grow (32,766 - 1 after ab goes); a's one other edge would pass it: refused
            int hub, a;
            DGraph2 g = CollapseCase(MaxEdges, 0, 3, out hub, out a);
            Assert.Equal(MeshResult.Ok, g.CollapseEdge(hub, a, out DGraph.EdgeCollapseInfo info));
            Assert.True(g.IsVertex(hub));
            Assert.Equal(MaxEdges - 1, g.GetVtxEdgeCount(hub));
            DGraph2 g2 = CollapseCase(MaxEdges, 1, 3, out hub, out a);
            Assert.Equal(MeshResult.Failed_HitValenceLimit, g2.CollapseEdge(hub, a, out info));
            Assert.True(g2.IsVertex(hub) && g2.IsVertex(a));
        }

        [Fact]
        public void SplitEdge_OfAHubEdge_KeepsTheHubsCount()
        {
            int hub;
            DGraph2 g = Star(MaxEdges, MaxEdges, out hub);
            Assert.Equal(MeshResult.Ok, g.SplitEdge(g.FindEdge(0, hub), out DGraph.EdgeSplitInfo split));
            Assert.Equal(MaxEdges, g.GetVtxEdgeCount(hub));
            Assert.True(g.IsVertex(hub) && g.IsVertex(split.vNew));
        }
    }
}
