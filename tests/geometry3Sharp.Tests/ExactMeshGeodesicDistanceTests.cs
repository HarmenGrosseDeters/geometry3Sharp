using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshGeodesicDistance, the certified test oracle of the polyhedral geodesic distance
    /// (docs/design/GEODESICS.md, option A): [Lower, Upper] must contain the exact polyhedral distance. Checked against
    /// closed forms (planes, developable surfaces, a convex sphere bracket), against properties every distance has,
    /// under similarities, and for reuse and determinism.
    /// </summary>
    public class ExactMeshGeodesicDistanceTests
    {
        // ------------------------------------------------------------------ meshes

        static DMesh3 Grid(double x0, double y0, double x1, double y1, int nx, int ny, Func<double, double, double> h, bool alternate = true)
        {
            var m = new DMesh3();
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    double x = x0 + (x1 - x0) * i / (nx - 1), y = y0 + (y1 - y0) * j / (ny - 1);
                    m.AppendVertex(new Vector3d(x, y, h(x, y)));
                }
            for (int j = 0; j < ny - 1; ++j)
                for (int i = 0; i < nx - 1; ++i) {
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    if (!alternate || ((i + j) & 1) == 0) { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); }
                    else { m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c); }
                }
            return m;
        }

        static DMesh3 Grid(double half, int n, Func<double, double, double> h) => Grid(-half, -half, half, half, n, n, h);

        static double Terrain(double x, double y) => 3 * Math.Sin(x / 7 + 1) * Math.Sin(y / 5) + 0.8 * Math.Sin(x * 1.3) * Math.Cos(y * 1.1);

        // ridge with 75 degree faces along y: developable, its unfolding along x is the profile's arc length
        const double RidgeHalf = 2.5;
        static readonly double RidgeSlope = Math.Tan(75 * Math.PI / 180);
        static double Ridge(double x) => RidgeSlope * Math.Max(0, RidgeHalf - Math.Abs(x));
        static double RidgeArc(double x)
        {
            // arc length of the profile from 0 to x (odd in x): flat beyond RidgeHalf, slope RidgeSlope inside
            double s = Math.Sqrt(1 + RidgeSlope * RidgeSlope), a = Math.Abs(x);
            double u = a <= RidgeHalf ? a * s : RidgeHalf * s + (a - RidgeHalf);
            return Math.Sign(x) * u;
        }

        static ExactMeshGeodesicDistance Run(DMesh3 m, params int[] sources)
        {
            var g = new ExactMeshGeodesicDistance(m);
            Assert.Equal(ExactMeshGeodesicDistance.Result.Ok, g.Compute(sources));
            return g;
        }

        // Lower <= exact <= Upper with a tolerance for the exact value's own rounding, and the enclosure narrow
        static void AssertEncloses(DMesh3 m, ExactMeshGeodesicDistance g, Func<int, double> exact, double maxRelWidth)
        {
            foreach (int v in m.VertexIndices()) {
                double d = exact(v), slack = 1e-12 * (1 + d);
                Assert.True(g.Lower[v] <= d + slack, $"vertex {v}: Lower {g.Lower[v]:R} above exact {d:R}");
                Assert.True(g.Upper[v] >= d - slack, $"vertex {v}: Upper {g.Upper[v]:R} below exact {d:R}");
                Assert.True(g.Upper[v] - g.Lower[v] <= maxRelWidth * (1 + d), $"vertex {v}: width {g.Upper[v] - g.Lower[v]:E2}");
            }
        }

        static double[] EdgeDijkstra(DMesh3 m, int s)
        {
            var d = new double[m.MaxVertexID]; for (int i = 0; i < d.Length; ++i) d[i] = double.PositiveInfinity;
            d[s] = 0; var q = new SortedSet<(double, int)> { (0, s) };
            while (q.Count > 0) {
                var (k, v) = q.Min; q.Remove(q.Min);
                if (k > d[v]) continue;
                foreach (int u in m.VtxVerticesItr(v)) { double nd = d[v] + m.GetVertex(v).Distance(m.GetVertex(u)); if (nd < d[u]) { d[u] = nd; q.Add((nd, u)); } }
            }
            return d;
        }

        // ------------------------------------------------------------------ closed forms

        [Fact]
        public void Plane_EnclosesTheEuclideanDistance()
        {
            DMesh3 m = Grid(10, 21, (x, y) => 0);
            int src = 10 * 21 + 10;
            var g = Run(m, src);
            AssertEncloses(m, g, v => m.GetVertex(v).Distance(m.GetVertex(src)), 0.08);
            // the lower bound is tight: each emission of a pseudo-source lowers sigma by 1e-9 relative (A5), and on a plane
            // every vertex is a flat pseudo-source, so the margins add up along a chain of a few emissions
            foreach (int v in m.VertexIndices()) { double d = m.GetVertex(v).Distance(m.GetVertex(src)); Assert.True(d - g.Lower[v] <= 1e-7 * (1 + d)); }
        }

        [Fact]
        public void ObtusePlane_EnclosesTheEuclideanDistance()
        {
            // rows 0.25 apart, shifted by half a column: every triangle has an angle of about 127 degrees
            var m = new DMesh3(); int nx = 12, ny = 12;
            for (int j = 0; j < ny; ++j) for (int i = 0; i < nx; ++i) m.AppendVertex(new Vector3d(i + 0.5 * (j & 1), 0.25 * j, 0));
            for (int j = 0; j < ny - 1; ++j)
                for (int i = 0; i < nx - 1; ++i) {
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    if ((j & 1) == 0) { m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c); }
                    else { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); }
                }
            int src = 6 * nx + 5;
            var g = Run(m, src);
            AssertEncloses(m, g, v => m.GetVertex(v).Distance(m.GetVertex(src)), 0.1);
        }

        [Fact]
        public void SplitSquare_EveryVertexBounded()
        {
            // review M1: the opposite edges of both faces are boundary edges
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(1, 1, 0)); m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendTriangle(0, 1, 2); m.AppendTriangle(0, 2, 3);
            var g = Run(m, 0);
            AssertEncloses(m, g, v => m.GetVertex(v).Length, 1e-12);
        }

        [Fact]
        public void Prism_EnclosesTheUnrolledDistance()
        {
            // an open 16-gon prism: developable; the distance is the straight line in the unrolled strip, over the wraps
            int n = 16, rows = 13; double R = 3, H = 12, chord = 2 * R * Math.Sin(Math.PI / n);
            var m = new DMesh3();
            for (int j = 0; j < rows; ++j) for (int i = 0; i < n; ++i) { double a = 2 * Math.PI * i / n; m.AppendVertex(new Vector3d(R * Math.Cos(a), R * Math.Sin(a), H * j / (rows - 1))); }
            for (int j = 0; j < rows - 1; ++j)
                for (int i = 0; i < n; ++i) { int a = j * n + i, b = j * n + (i + 1) % n, c = a + n, d = b + n; m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); }
            int src = 6 * n;
            var g = Run(m, src);
            AssertEncloses(m, g, v => {
                int i = v % n, j = v / n; double dz = H * (j - 6) / (rows - 1), best = double.MaxValue;
                for (int k = -1; k <= 1; ++k) { double ux = (i + k * n) * chord; best = Math.Min(best, Math.Sqrt(ux * ux + dz * dz)); }
                return best;
            }, 0.08);
        }

        [Fact]
        public void AlignedRidge_EnclosesTheUnfoldedDistance()
        {
            // the exactly aligned ridge of gate 3: rays run through rows of flat fold vertices, which are pseudo-sources (S1)
            int n = 21; double h = 0.25, half = (n - 1) * h / 2;
            DMesh3 m = Grid(half, n, (x, y) => Ridge(x));
            int src = (n / 2) * n + 3;
            var g = Run(m, src);
            Vector3d s = m.GetVertex(src);
            AssertEncloses(m, g, v => { Vector3d p = m.GetVertex(v); double du = RidgeArc(p.x) - RidgeArc(s.x), dy = p.y - s.y; return Math.Sqrt(du * du + dy * dy); }, 0.2);
        }

        [Fact]
        public void LShapedDomain_BendsAtTheReflexCorner()
        {
            // the unit grid on [0,8]^2 without (4,8]x(4,8]: from (7,1) to (1,7) the path bends at the reflex corner (4,4)
            var full = Grid(0, 0, 8, 8, 9, 9, (x, y) => 0);
            var m = new DMesh3(full);
            foreach (int t in full.TriangleIndices()) { Vector3d c = full.GetTriCentroid(t); if (c.x > 4 && c.y > 4) m.RemoveTriangle(t); }
            int src = 1 * 9 + 7;
            var g = Run(m, src);
            Vector3d s = m.GetVertex(src), corner = new Vector3d(4, 4, 0);
            AssertEncloses(m, g, v => {
                Vector3d p = m.GetVertex(v);
                // s = (7, 1) lies below y = 4: the segment to p enters the removed quadrant x > 4, y > 4 iff it crosses y = 4 at x > 4
                bool behind = p.y > 4 && s.x + (p.x - s.x) * (4 - s.y) / (p.y - s.y) > 4;
                return behind ? s.Distance(corner) + corner.Distance(p) : s.Distance(p);
            }, 0.1);
        }

        [Fact]
        public void ConvexSphere_LiesBetweenChordAndArc()
        {
            // a latitude-longitude sphere: its quads are planar, so it is a convex polyhedron with vertices on the sphere;
            // the nearest-point projection argument of GEODESICS.md 7.1 gives chord <= d <= great-circle arc
            int rings = 9, seg = 14; var m = new DMesh3();
            int north = m.AppendVertex(new Vector3d(0, 0, 1));
            for (int r = 1; r < rings; ++r) for (int k = 0; k < seg; ++k) {
                double th = Math.PI * r / rings, ph = 2 * Math.PI * k / seg;
                m.AppendVertex(new Vector3d(Math.Sin(th) * Math.Cos(ph), Math.Sin(th) * Math.Sin(ph), Math.Cos(th)));
            }
            int south = m.AppendVertex(new Vector3d(0, 0, -1));
            int V(int r, int k) => 1 + (r - 1) * seg + (k % seg);
            for (int k = 0; k < seg; ++k) { m.AppendTriangle(north, V(1, k), V(1, k + 1)); m.AppendTriangle(south, V(rings - 1, k + 1), V(rings - 1, k)); }
            for (int r = 1; r < rings - 1; ++r) for (int k = 0; k < seg; ++k) { m.AppendTriangle(V(r, k), V(r + 1, k), V(r + 1, k + 1)); m.AppendTriangle(V(r, k), V(r + 1, k + 1), V(r, k + 1)); }
            var g = Run(m, north);
            Vector3d s = m.GetVertex(north);
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                double chord = s.Distance(p), arc = Math.Acos(Math.Max(-1, Math.Min(1, s.Dot(p))));
                Assert.True(g.Lower[v] <= arc + 1e-12, $"vertex {v}: Lower above the arc");
                Assert.True(g.Upper[v] >= chord - 1e-12, $"vertex {v}: Upper below the chord");
                Assert.True(g.Lower[v] >= chord - 1e-12 || g.Upper[v] >= chord, $"vertex {v}");
            }
        }

        // ------------------------------------------------------------------ properties

        [Fact]
        public void Terrain_Properties_ThreeDDistance_EdgePaths_FlipOut_TriangleInequality_Symmetry()
        {
            DMesh3 m = Grid(8, 17, Terrain);
            int a = 8 * 17 + 8, b = 2 * 17 + 3, c = 14 * 17 + 12;
            var ga = Run(m, a); var gb = Run(m, b); var gc = Run(m, c);
            double[] dij = EdgeDijkstra(m, a);
            foreach (int v in m.VertexIndices()) {
                Assert.True(ga.Lower[v] <= ga.Upper[v]);
                Assert.True(ga.Lower[v] >= m.GetVertex(a).Distance(m.GetVertex(v)) - 1e-12 * (1 + dij[v]), "Lower below the 3D distance is allowed only by rounding");
                Assert.True(ga.Upper[v] <= dij[v] + 1e-12 * (1 + dij[v]), "Upper above an edge path");
                Assert.True(ga.Upper[v] - ga.Lower[v] <= 1e-6 * (1 + dij[v]), $"terrain enclosure narrow at {v}");
            }
            // Upper is a fixpoint of the edge paths: Upper(v) <= Upper(u) + |uv| on every edge (an edge path is a real path)
            foreach (int e in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(e); double l = m.GetVertex(ev.a).Distance(m.GetVertex(ev.b));
                Assert.True(ga.Upper[ev.a] <= ga.Upper[ev.b] + l * (1 + 1e-12) + 1e-12, $"edge {e}");
                Assert.True(ga.Upper[ev.b] <= ga.Upper[ev.a] + l * (1 + 1e-12) + 1e-12, $"edge {e}");
            }
            // symmetry and the triangle inequality, within the enclosures
            Assert.True(ga.Lower[b] <= gb.Upper[a] && gb.Lower[a] <= ga.Upper[b]);
            Assert.True(ga.Lower[c] <= ga.Upper[b] + gb.Upper[c]);
            // a FlipOut geodesic is a real path
            var path = new MeshGeodesicPath(m);
            Assert.Equal(GeodesicPathStatus.Success, path.Compute(a, c));
            Assert.True(ga.Lower[c] <= path.Length * (1 + 1e-9));
        }

        [Fact]
        public void MultipleSources_TheMinimumOfSingleSources()
        {
            DMesh3 m = Grid(6, 13, Terrain);
            int a = 6 * 13 + 2, b = 3 * 13 + 10;
            var ga = Run(m, a); var gb = Run(m, b); var gab = Run(m, a, b);
            foreach (int v in m.VertexIndices()) {
                Assert.True(gab.Lower[v] <= Math.Min(ga.Upper[v], gb.Upper[v]) + 1e-12);
                Assert.True(gab.Upper[v] >= Math.Min(ga.Lower[v], gb.Lower[v]) - 1e-12);
            }
        }

        // ------------------------------------------------------------------ vertex classification (A5, review S1)

        static int Kind(DMesh3 m, int v) => new ExactMeshGeodesicDistance(m).VertexKind(v);

        static DMesh3 Fan(Vector3d[] ring)
        {
            var m = new DMesh3(); int p = m.AppendVertex(Vector3d.Zero);
            foreach (var r in ring) m.AppendVertex(r);
            for (int i = 0; i < ring.Length; ++i) m.AppendTriangle(p, 1 + i, 1 + (i + 1) % ring.Length);
            return m;
        }

        static Vector3d[] Ring(int k, Func<int, double> z) => Enumerable.Range(0, k).Select(i => new Vector3d(Math.Cos(2 * Math.PI * i / k), Math.Sin(2 * Math.PI * i / k), z(i))).ToArray();

        [Fact]
        public void Classification_ConvexOnlyIfCertain_FlatAndSaddleAndBoundaryArePseudoSources()
        {
            Assert.Equal(0, Kind(Fan(Ring(6, i => -0.5)), 0));                          // a convex cone: no shortest path passes
            Assert.Equal(0, Kind(Fan(Ring(7, i => -1e-6)), 0));                         // nearly flat, but certainly convex
            Assert.Equal(1, Kind(Fan(Ring(7, i => 0)), 0));                             // exactly flat: a pseudo-source (S1)
            Assert.Equal(1, Kind(Fan(Ring(6, i => (i & 1) == 0 ? 0.7 : -0.7)), 0));      // a saddle
            // a coplanar fan of eight 90-degree sectors wraps twice (4 pi): a saddle, not flat
            Assert.Equal(1, Kind(Fan(Enumerable.Range(0, 8).Select(i => (1 + 0.1 * i) * new Vector3d(Math.Cos(i * Math.PI / 2), Math.Sin(i * Math.PI / 2), 0)).ToArray()), 0));
            DMesh3 g = Grid(2, 5, (x, y) => 0);
            Assert.Equal(1, Kind(g, 0));                                                  // a boundary vertex
        }

        // ------------------------------------------------------------------ invariance, reuse, determinism, status

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Terrain_UnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 m0 = Grid(5, 11, Terrain);
            int src = 5 * 11 + 5;
            var g0 = Run(m0, src);
            var g = Run(Invariance.Transformed(m0, sim), src);
            foreach (int v in m0.VertexIndices()) {
                if (sim.IsExact) {
                    Assert.Equal(g0.Lower[v] * sim.Scale, g.Lower[v]);
                    Assert.Equal(g0.Upper[v] * sim.Scale, g.Upper[v]);
                } else {
                    // the transformed mesh is a slightly different surface: the enclosures overlap up to its rounding
                    double res = sim.Resolution(20) * 1e3;
                    Assert.True(g.Lower[v] / sim.Scale <= g0.Upper[v] + res && g0.Lower[v] <= g.Upper[v] / sim.Scale + res, $"{name}: vertex {v}");
                }
            }
            if (sim.IsExact) Assert.Equal(g0.Windows, g.Windows);
        }

        [Fact]
        public void RotatedObtusePlane_EnclosuresOverlap()
        {
            DMesh3 m0 = Grid(0, 0, 8, 2, 17, 9, (x, y) => 0, alternate: false);
            var rot = new Similarity("rotated", 1, Matrix3d.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 37), new Vector3d(0.3, -0.2, 0.1));
            var g0 = Run(m0, 4 * 17 + 3); var g = Run(Invariance.Transformed(m0, rot), 4 * 17 + 3);
            foreach (int v in m0.VertexIndices()) Assert.True(g.Lower[v] <= g0.Upper[v] + 1e-12 && g0.Lower[v] <= g.Upper[v] + 1e-12);
        }

        [Fact]
        public void Reuse_SameBits()
        {
            DMesh3 m = Grid(5, 11, Terrain);
            var g = new ExactMeshGeodesicDistance(m);
            g.Compute(new[] { 60 }); double[] lo1 = (double[])g.Lower.Clone(), up1 = (double[])g.Upper.Clone();
            g.Compute(new[] { 5 });
            g.Compute(new[] { 60 });
            Assert.Equal(lo1, g.Lower); Assert.Equal(up1, g.Upper);
        }

        [Fact]
        public void AlignedRidge41_ReachesTheWindowLimit_WithNaNArrays()
        {
            // the measured reach (GEODESICS.md): the exactly aligned 41 x 41 ridge exceeds the oracle's window budget; the
            // limit must end with WindowLimit and NaN arrays, never with values that look valid
            int n = 41; double h = 0.25, half = (n - 1) * h / 2;
            DMesh3 m = Grid(half, n, (x, y) => Ridge(x));
            var g = new ExactMeshGeodesicDistance(m) { MaxWindows = 60000 };
            Assert.Equal(ExactMeshGeodesicDistance.Result.WindowLimit, g.Compute(new[] { (n / 2) * n + 3 }));
            Assert.True(g.Lower.All(double.IsNaN) && g.Upper.All(double.IsNaN));
        }

        [Fact]
        public void Deterministic_RepeatedRuns_SameBits()
        {
            DMesh3 m = Grid(5, 11, (x, y) => Ridge(x * 0.5));
            var a = Run(m, 3); var b = Run(m, 3);
            Assert.Equal(a.Lower, b.Lower); Assert.Equal(a.Upper, b.Upper); Assert.Equal(a.Windows, b.Windows);
        }

        [Fact]
        public void Status_NoSource_NonManifold_Degenerate_WindowLimit_ArraysAreNaN()
        {
            DMesh3 m = Grid(3, 7, Terrain);
            var g = new ExactMeshGeodesicDistance(m);
            Assert.Equal(ExactMeshGeodesicDistance.Result.NoSource, g.Compute(new int[0]));
            Assert.True(g.Lower.All(double.IsNaN) && g.Upper.All(double.IsNaN));
            g.MaxWindows = 5;
            Assert.Equal(ExactMeshGeodesicDistance.Result.WindowLimit, g.Compute(new[] { 24 }));
            Assert.True(g.Lower.All(double.IsNaN) && g.Upper.All(double.IsNaN), "a window limit must not leave values that look valid");
            // a zero-area triangle
            var d = new DMesh3(); d.AppendVertex(Vector3d.Zero); d.AppendVertex(new Vector3d(1, 0, 0)); d.AppendVertex(new Vector3d(2, 0, 0)); d.AppendTriangle(0, 1, 2);
            var gd = new ExactMeshGeodesicDistance(d);
            Assert.Equal(ExactMeshGeodesicDistance.Result.DegenerateTriangles, gd.Compute(new[] { 0 }));
            Assert.True(gd.Lower.All(double.IsNaN));
            // a bowtie vertex
            var bt = new DMesh3();
            bt.AppendVertex(Vector3d.Zero); bt.AppendVertex(new Vector3d(1, 0, 0)); bt.AppendVertex(new Vector3d(0, 1, 0)); bt.AppendVertex(new Vector3d(-1, 0, 0)); bt.AppendVertex(new Vector3d(0, -1, 0));
            bt.AppendTriangle(0, 1, 2); bt.AppendTriangle(0, 3, 4);
            Assert.Equal(ExactMeshGeodesicDistance.Result.NonManifoldInput, new ExactMeshGeodesicDistance(bt).Compute(new[] { 1 }));
        }
    }
}
