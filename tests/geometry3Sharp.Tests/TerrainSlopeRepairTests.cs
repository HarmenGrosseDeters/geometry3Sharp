using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The repairs of the realization of the slope surface (Terrain 1c): needle flips within their deviation share, and
    /// the stitching of dropped triangles (faces narrower than the rounding) without cracks.
    /// </summary>
    public class TerrainSlopeRepairTests
    {
        // a needle (a, b, c), 1e-9 thin, on the long edge ab of a fat triangle (b, a, d); c is 'zc' above the line ab
        static DMesh3 Needle(double zc)
        {
            var m = new DMesh3(false, false, false, true);
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(10, 0, 0));
            int c = m.AppendVertex(new Vector3d(5, 1e-9, zc)), d = m.AppendVertex(new Vector3d(5, -3, 0));
            m.AppendTriangle(a, b, c, 1);
            m.AppendTriangle(b, a, d, 2);
            return m;
        }

        [Fact]
        public void NeedleFlip_WithinItsShare()
        {
            // the flip changes the surface at the crossing of the diagonals by about zc: made within the share, not beyond
            var m = Needle(1e-3);
            Assert.Equal(1, SlopeMap.FlipNeedles(m, 1e-6, 2e-3));
            Assert.All(m.TriangleIndices(), t => Assert.Equal(2, m.GetTriangleGroup(t)));    // the thicker one's piece
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                Assert.True(ExactPredicates.Orient2d(m.GetVertex(tv.a).xy, m.GetVertex(tv.b).xy, m.GetVertex(tv.c).xy) > 0);
            }
            var m2 = Needle(1e-3);
            Assert.Equal(0, SlopeMap.FlipNeedles(m2, 1e-6, 5e-4));
            // and not a fat triangle
            var m3 = Needle(1e-3);
            Assert.Equal(0, SlopeMap.FlipNeedles(m3, 1e-12, 1));
        }

        [Fact]
        public void Stitch_SplitsTheLongEdgeAtTheMiddleCorner()
        {
            // the triangle (e0, m, e1) with m exactly on e0 e1 was dropped; the other side (e1, e0, x) is split at m
            var mesh = new DMesh3(false, false, false, true);
            int e0 = mesh.AppendVertex(new Vector3d(0, 0, 0)), e1 = mesh.AppendVertex(new Vector3d(4, 0, 0)), mid = mesh.AppendVertex(new Vector3d(1, 0, 0));
            int x = mesh.AppendVertex(new Vector3d(2, -2, 0)), y = mesh.AppendVertex(new Vector3d(0.5, 1, 0)), z = mesh.AppendVertex(new Vector3d(2.5, 1, 0));
            mesh.AppendTriangle(e1, e0, x, 7);
            mesh.AppendTriangle(e0, mid, y, 3); mesh.AppendTriangle(mid, z, y, 3); mesh.AppendTriangle(mid, e1, z, 3);
            Assert.Equal(1, mesh.EdgeIndices().Count(e => mesh.IsBoundaryEdge(e) && mesh.GetEdgeV(e).a + mesh.GetEdgeV(e).b == e0 + e1
                                                                                  && (mesh.GetEdgeV(e).a == e0 || mesh.GetEdgeV(e).b == e0)));
            SlopeMap.stitch(mesh, new List<Index3i> { new Index3i(e0, mid, e1) });
            // no edge e0 e1 left, the two halves meet the upper side at m: the only boundary is the outer one
            Assert.True(mesh.FindEdge(e0, e1) < 0);
            Assert.True(mesh.FindEdge(mid, x) >= 0);
            Assert.False(mesh.IsBoundaryEdge(mesh.FindEdge(e0, mid)));
            Assert.False(mesh.IsBoundaryEdge(mesh.FindEdge(mid, e1)));
            Assert.Equal(5, mesh.TriangleCount);
            Assert.Equal(7, mesh.GetTriangleGroup(mesh.GetEdgeT(mesh.FindEdge(mid, x)).a));
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                Assert.True(ExactPredicates.Orient2d(mesh.GetVertex(tv.a).xy, mesh.GetVertex(tv.b).xy, mesh.GetVertex(tv.c).xy) > 0);
            }
        }
    }
}
