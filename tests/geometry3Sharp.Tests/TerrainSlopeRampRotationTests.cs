using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1539: the around-the-corner ramp (TerrainSlopeRampTests.AroundCorner) rotated about z by 127, 205 and 271 degrees.
    /// The map's faces along the road's end edge are slivers a few ulps wide. The rounding turns some of their triangles
    /// over (clockwise) or lays them over a neighbour's (an edge run in the same direction by two triangles; DMesh3 takes
    /// that), and the next triangle on that edge is refused: "SlopeMap: non-manifold realization". After the two realizations
    /// of the design (with and without the merge) the strict one drops such slivers and stitches the neighbours.
    /// </summary>
    public class TerrainSlopeRampRotationTests
    {
        // ---- append_faces on its own: a sliver the rounding laid over a neighbour ----

        // vertices of the mesh: the long edge e0 e1 on y = 0 (coordinates up to 4: ulp 8.9e-16, a sliver is up to 3.6e-15 high), the apex x below it, m1 just below the edge (a sliver (e1, e0, m1)
        // over x's triangle: counter-clockwise, running along e1 -> e0 as the triangle (e1, e0, x) does), m2 just above it
        static DMesh3 Setup(out int e0, out int e1, out int x, out int m1, out int m2)
        {
            var mesh = new DMesh3(false, false, false, true);
            e0 = mesh.AppendVertex(new Vector3d(0, 0, 0)); e1 = mesh.AppendVertex(new Vector3d(4, 0, 0)); x = mesh.AppendVertex(new Vector3d(2, -2, 0));
            m1 = mesh.AppendVertex(new Vector3d(1, -1e-15, 0)); m2 = mesh.AppendVertex(new Vector3d(2.5, 1e-15, 0));
            return mesh;
        }

        static int Append(DMesh3 mesh, List<Index3i> tris, bool strict)
        {
            // (a face per triangle, its corners the mesh's vertex ids)
            var faces = tris.Select((t, i) => new KeyValuePair<int, List<int>>(i, new List<int> { t.a, t.b, t.c })).ToList();
            return SlopeMap.append_faces(mesh, faces, (v) => new List<Index3i> { new Index3i(v[0], v[1], v[2]) }, (v) => v, strict);
        }

        [Fact]
        public void AppendFaces_Strict_DropsASliverOverANeighbour_AndStitchesIt()
        {
            int e0, e1, x, m1, m2;
            var mesh = Setup(out e0, out e1, out x, out m1, out m2);
            // x's triangle, the sliver over it (m1 below the edge), the sliver on the other side (m2 above it, a legal neighbour)
            var tris = new List<Index3i> { new Index3i(e1, e0, x), new Index3i(e1, e0, m1), new Index3i(e0, e1, m2) };
            // as before: the third triangle on the edge is refused
            var plain = Setup(out e0, out e1, out x, out m1, out m2);
            Assert.Throws<SlopeMap.NonManifoldRealization>(() => Append(plain, tris, false));
            // strict: the sliver is dropped (the other side of the edge keeps its triangle), every triangle counter-clockwise, manifold
            Assert.Equal(1, Append(mesh, tris, true));
            Assert.Equal(2, mesh.TriangleCount);
            Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));
            Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(mesh));
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                Assert.Equal(1, ExactPredicates.Orient2d(mesh.GetVertex(tv.a).xy, mesh.GetVertex(tv.b).xy, mesh.GetVertex(tv.c).xy));
            }
        }

        [Fact]
        public void AppendFaces_Strict_DropsATurnedOverSliver_NotTheDefault()
        {
            int e0, e1, x, m1, m2;
            var mesh = Setup(out e0, out e1, out x, out m1, out m2);
            // (e0, e1, m1): m1 is below the edge, clockwise: the default appends it (it lies over x's triangle), strict drops it
            var tris = new List<Index3i> { new Index3i(e0, e1, m1) };
            Assert.Equal(0, Append(mesh, tris, false));
            Assert.Equal(1, mesh.TriangleCount);
            var strict = Setup(out e0, out e1, out x, out m1, out m2);
            Assert.Equal(1, Append(strict, tris, true));
            Assert.Equal(0, strict.TriangleCount);
        }

        [Fact]
        public void AppendFaces_Strict_DoesNotDropATriangleThatIsNotASliver()
        {
            int e0, e1, x, m1, m2;
            var mesh = Setup(out e0, out e1, out x, out m1, out m2);
            // m3 is 0.5 below the edge: (e1, e0, m3) lies over x's triangle and is no sliver, and (e0, e1, m3) is clockwise and no sliver
            int m3 = mesh.AppendVertex(new Vector3d(1, -0.5, 0));
            Assert.Throws<SlopeMap.NonManifoldRealization>(() => Append(mesh, new List<Index3i> { new Index3i(e1, e0, x), new Index3i(e1, e0, m3) }, true));
            var mesh2 = Setup(out e0, out e1, out x, out m1, out m2);
            m3 = mesh2.AppendVertex(new Vector3d(1, -0.5, 0));
            Assert.Throws<SlopeMap.NonManifoldRealization>(() => Append(mesh2, new List<Index3i> { new Index3i(e0, e1, m3) }, true));
            // the bound grows with the merge radius: 1e-3 of radius lets a 1e-4 sliver go
            var mesh3 = Setup(out e0, out e1, out x, out m1, out m2);
            int m4 = mesh3.AppendVertex(new Vector3d(1, -1e-4, 0));
            Assert.Throws<SlopeMap.NonManifoldRealization>(() => Append(mesh3, new List<Index3i> { new Index3i(e0, e1, m4) }, true));
            Assert.Equal(1, SlopeMap.append_faces(mesh3, new List<KeyValuePair<int, List<int>>> { new KeyValuePair<int, List<int>>(0, new List<int> { e0, e1, m4 }) },
                (v) => new List<Index3i> { new Index3i(v[0], v[1], v[2]) }, (v) => v, true, 1e-3));
        }

        // the bound 2 (ulp(M) + radius) + 16 u L (u = 2^-53) of is_sliver, with the true height just below and just above it
        [Fact]
        public void IsSliver_DecidesAtItsBound()
        {
            double big = Math.Pow(2, 22), h = Math.Pow(2, -29);       // M = 2^22 + 4: ulp 2^-30, 2 ulp = 2^-29; 16 u L = 2^-47
            Func<double, double, double, double, bool> sliver = (x0, y0, hh, r) =>
                SlopeMap.is_sliver(new Vector2d(x0, y0), new Vector2d(x0 + 4, y0), new Vector2d(x0 + 2, y0 - hh), r);
            Assert.True(sliver(big, 0, h, 0));
            Assert.True(sliver(big, 0, h + Math.Pow(2, -50), 0));      // inside the margin for the evaluation
            Assert.False(sliver(big, 0, h + Math.Pow(2, -40), 0));
            Assert.False(sliver(big, 0, 2 * h, 0));
            // M = 4, ulp 2^-50 (2 ulp = 2^-49), 16 u L = 2^-47: the bound is 1.25 * 2^-47
            Assert.True(sliver(0, 0, Math.Pow(2, -47), 0));
            Assert.False(sliver(0, 0, Math.Pow(2, -45), 0));
            // with a merge radius: 2 radius + 2 ulp + 2^-47
            Assert.True(sliver(0, 0, 1.9e-6, 1e-6));
            Assert.False(sliver(0, 0, 2.1e-6, 1e-6));
            // a point is a sliver
            Assert.True(SlopeMap.is_sliver(new Vector2d(1, 1), new Vector2d(1, 1), new Vector2d(1, 1), 0));
        }

        // the sliver bound decides at the drop: at 96 degrees the strict realization meets a triangle 0.073 high that turned over
        // or lies over a neighbour, which is no rounding artefact (before: it was dropped, and the design failed in the checks after)
        [Fact]
        public void AroundCorner_96_FailsAtTheDropOfARealTriangle()
        {
            var d = TerrainSlopeRampRotationRegressionTests.Make(96);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.Failed, d.Status);
            Assert.Equal("SlopeMap: non-manifold realization", d.Message);
        }
    }
}
