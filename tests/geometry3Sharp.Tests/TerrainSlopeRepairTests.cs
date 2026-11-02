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

        [Fact]
        public void Realize_DropsAFaceNarrowerThanTheRounding_NoGapNoOverlap()
        {
            // At UTM magnitude (ulp(y) = 2^-30) two creases through a, ab and a line rising one ulp per 1000 m, bound a
            // sliver face S = (a, b, c) whose apex c (on the vertical x = x0 + 5) is 0.005 ulps above ab: exactly
            // counter-clockwise, collinear once rounded. Below it G = (a, d, b), above it K = (c, b, e, a).
            double x0 = 328375.25, y0 = 5642875.5, ulp = Math.Pow(2, -30);
            Assert.Equal(y0 + ulp, BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(y0) + 1));
            var K = new ExactSlopeKernel();
            int a = K.PointVertex(new Vector2d(x0, y0)), b = K.PointVertex(new Vector2d(x0 + 10, y0));
            int d = K.PointVertex(new Vector2d(x0 + 5, y0 - 5)), e = K.PointVertex(new Vector2d(x0 + 5, y0 + 5));
            int rising = K.PointLine(new Vector2d(x0, y0), new Vector2d(x0 + 1000, y0 + ulp));
            int vertical = K.PointLine(new Vector2d(x0 + 5, y0 - 1), new Vector2d(x0 + 5, y0 + 1));
            int c = K.Crossing(rising, vertical);
            Assert.True(K.Orient(a, b, c) > 0);
            Assert.Equal(new Vector2d(x0 + 5, y0), K.Rounded(c));

            var mesh = new DMesh3(false, false, false, true);
            var ids = new Dictionary<Vector2d, int>();
            Func<int, int> outVertex = (v) => {
                Vector2d r = K.Rounded(v);
                int o;
                if (ids.TryGetValue(r, out o) == false) { o = mesh.AppendVertex(new Vector3d(r.x, r.y, 0)); ids[r] = o; }
                return o;
            };
            // a fan from the first corner, at the exact corners (as the map triangulates: no triangle across collinear ones)
            Func<List<int>, List<Index3i>> fan = (v) => {
                var tris = new List<Index3i>();
                for (int i = 1; i + 1 < v.Count; ++i)
                    if (K.Orient(v[0], v[i], v[i + 1]) != 0) tris.Add(new Index3i(v[0], v[i], v[i + 1]));
                return tris;
            };
            var faces = new List<KeyValuePair<int, List<int>>> {
                new KeyValuePair<int, List<int>>(1, new List<int> { a, d, b }),
                new KeyValuePair<int, List<int>>(2, new List<int> { a, b, c }),
                new KeyValuePair<int, List<int>>(3, new List<int> { c, b, e, a }),
            };
            Assert.Equal(1, SlopeMap.append_faces(mesh, faces, fan, outVertex));

            // no overlap: every triangle strictly counter-clockwise (a kept sliver is a zero-area fold over G and K);
            // no gap: the areas sum to the outer quadrilateral's 50 m^2 (exact here: offsets from a are multiples of
            // 2^-30 below 16), and the only boundary is its four edges (a crack would leave ab or ac, cb open)
            double area2 = 0;
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                Vector2d p = mesh.GetVertex(tv.a).xy, q = mesh.GetVertex(tv.b).xy, r = mesh.GetVertex(tv.c).xy;
                Assert.True(ExactPredicates.Orient2d(p, q, r) > 0);
                area2 += (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
            }
            Assert.Equal(100.0, area2);
            var outer = new HashSet<Index2i> {
                ordered(outVertex(a), outVertex(d)), ordered(outVertex(d), outVertex(b)),
                ordered(outVertex(b), outVertex(e)), ordered(outVertex(e), outVertex(a)) };
            var boundary = mesh.EdgeIndices().Where(mesh.IsBoundaryEdge).Select(x => ordered(mesh.GetEdgeV(x).a, mesh.GetEdgeV(x).b)).ToList();
            Assert.Equal(4, boundary.Count);
            Assert.True(outer.SetEquals(boundary));
            // G was split at c (two triangles of group 1), K kept its two
            Assert.Equal(2, mesh.TriangleIndices().Count(x => mesh.GetTriangleGroup(x) == 1));
            Assert.Equal(4, mesh.TriangleCount);
        }

        static Index2i ordered(int u, int v) { return u < v ? new Index2i(u, v) : new Index2i(v, u); }
    }
}
