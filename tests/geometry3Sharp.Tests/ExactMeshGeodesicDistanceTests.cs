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

        // Lower <= exact <= Upper against an enclosure of the exact value (no slack), and the enclosure narrow
        static void AssertEncloses(DMesh3 m, ExactMeshGeodesicDistance g, Func<int, ExactInterval> exact, double maxRelWidth)
        {
            foreach (int v in m.VertexIndices()) {
                ExactInterval d = exact(v);
                Assert.True(g.Lower[v] <= d.Hi, $"vertex {v}: Lower {g.Lower[v]:R} above exact [{d.Lo:R}, {d.Hi:R}]");
                Assert.True(g.Upper[v] >= d.Lo, $"vertex {v}: Upper {g.Upper[v]:R} below exact [{d.Lo:R}, {d.Hi:R}]");
                Assert.True(g.Upper[v] - g.Lower[v] <= maxRelWidth * (1 + d.Hi), $"vertex {v}: width {g.Upper[v] - g.Lower[v]:E2}");
            }
        }

        // a closed form evaluated in double for a surface built with sin, cos or tan: the built mesh is that surface only
        // up to the rounding of its coordinates (a relative 1e-15 of the size), so the closed form gets a slack of 1e-12 of
        // the distance plus 1e-12 of the mesh size (1000 times that rounding) on both sides
        static void AssertEncloses(DMesh3 m, ExactMeshGeodesicDistance g, Func<int, double> closedForm, double size, double maxRelWidth)
            => AssertEncloses(m, g, v => { double d = closedForm(v), slack = 1e-12 * (d + size); return new ExactInterval(d - slack, d + slack); }, maxRelWidth);

        static ExactInterval I(double x) => ExactInterval.Of(x);

        // the exact Euclidean distance of two stored points, enclosed
        static ExactInterval Dist(Vector3d a, Vector3d b)
            => ExactInterval.Sqrt(ExactInterval.Square(I(a.x) - I(b.x)) + ExactInterval.Square(I(a.y) - I(b.y)) + ExactInterval.Square(I(a.z) - I(b.z)));

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
            AssertEncloses(m, g, v => Dist(m.GetVertex(v), m.GetVertex(src)), 0.08);
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
            AssertEncloses(m, g, v => Dist(m.GetVertex(v), m.GetVertex(src)), 0.1);
        }

        [Fact]
        public void SplitSquare_EveryVertexBounded()
        {
            // review M1: the opposite edges of both faces are boundary edges
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(1, 1, 0)); m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendTriangle(0, 1, 2); m.AppendTriangle(0, 2, 3);
            var g = Run(m, 0);
            AssertEncloses(m, g, v => Dist(m.GetVertex(v), Vector3d.Zero), 1e-12);
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
            }, 2 * H, 0.08);
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
            AssertEncloses(m, g, v => { Vector3d p = m.GetVertex(v); double du = RidgeArc(p.x) - RidgeArc(s.x), dy = p.y - s.y; return Math.Sqrt(du * du + dy * dy); }, 2 * half, 0.2);
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
                return behind ? Dist(s, corner) + Dist(corner, p) : Dist(s, p);
            }, 0.1);
        }

        // the measured width of the sphere's enclosures, with a margin (a regression bound)
        const double SphereWidth = 1e-11;      // measured 3.5e-13

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
                Assert.True(g.Upper[v] - g.Lower[v] <= SphereWidth * (1 + arc), $"vertex {v}: width {g.Upper[v] - g.Lower[v]:E2}");
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
            var fresh = Run(m, 60);
            Assert.Equal(fresh.Lower, g.Lower); Assert.Equal(fresh.Upper, g.Upper); Assert.Equal(fresh.Windows, g.Windows);
        }

        // reviewed (2026-11-15): a source within about its disk radius of an edge line, behind a sliver next to an obtuse
        // face. The child clip t1 = xc.Hi lies past the ray parallel to the next edge, so the end values of Mu are no
        // bound of the crossings (Mu is not monotone over the clip): Lower[x] came out above the straight distance.
        // The mirror covers the right child. In the plane the chord is a lower bound of the geodesic distance.
        [Theory]
        [InlineData(1e-7, false)] [InlineData(1e-8, false)] [InlineData(1e-10, false)] [InlineData(1e-12, false)] [InlineData(1e-14, false)]
        [InlineData(1e-7, true)] [InlineData(1e-8, true)] [InlineData(1e-10, true)] [InlineData(1e-12, true)] [InlineData(1e-14, true)]
        public void SourceNearAnEdgeLine_LowerStaysBelowTheChord(double h, bool mirror)
        {
            Func<double, double> X = x => mirror ? 1 - x : x;
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(X(0), 0, 0)), b = m.AppendVertex(new Vector3d(X(1), 0, 0));
            int c = m.AppendVertex(new Vector3d(X(-0.43), 0.0094, 0)), v = m.AppendVertex(new Vector3d(X(0.0716), -h, 0));
            int x = m.AppendVertex(new Vector3d(X(-0.4), 0.00766, 0));
            foreach (var t in new[] { new Index3i(a, v, b), new Index3i(a, b, c), new Index3i(a, c, x) })
                Assert.True(m.AppendTriangle(mirror ? new Index3i(t.a, t.c, t.b) : t) >= 0);
            var g = Run(m, v);
            foreach (int u in m.VertexIndices()) {
                ExactInterval d = Dist(m.GetVertex(v), m.GetVertex(u));
                Assert.True(g.Lower[u] <= d.Hi, $"vertex {u}: Lower {g.Lower[u]:R} above the chord {d.Hi:R}");
                Assert.True(g.Upper[u] >= d.Lo, $"vertex {u}: Upper {g.Upper[u]:R} below the chord {d.Lo:R}");
            }
        }

        // reviewed: an adversarial family around the finding above. Slivers of height 1e-6 to 1e-15 (or a few ulps) between
        // an edge and an obtuse face, with the source on the far side of the sliver; x lies beyond the edge ac (no fold)
        // and the straight segment from the source to x crosses ab and then ac inside both, so the planar chord is the
        // geodesic distance of x (cases near the ends are skipped) and an enclosure of it bounds Lower and Upper. The old
        // clip fails a share of the cases.
        [Fact]
        public void Slivers_NextToObtuseFaces_EncloseTheStraightDistance()
        {
            var rnd = new Random(7);
            int asserted = 0;
            for (int it = 0; it < 400; ++it) {
                bool mirror = (it & 1) == 1;
                Func<double, double> X = x => mirror ? 1 - x : x;
                double h = (it % 3) == 0 ? (1 + rnd.Next(16)) * Math.Pow(2, -52 - rnd.Next(4)) : Math.Pow(10, -6 - 9 * rnd.NextDouble());
                double cy = 0.002 + 0.02 * rnd.NextDouble(), cx = -0.1 - 0.6 * rnd.NextDouble();
                double xx = cx * (0.3 + 0.7 * rnd.NextDouble()), xy = cy * xx / cx * (0.3 + 0.65 * rnd.NextDouble());
                double vx = 0.02 + 0.9 * rnd.NextDouble();
                // the segment v -> x crosses the line through a and c at the parameter k of a -> c
                double dx = xx - vx, dy = xy + h, den = cx * dy - cy * dx;
                double k = (vx * dy - (-h) * dx) / den;
                if (!(k > 1e-6 && k < 1 - 1e-6)) continue;
                var m = new DMesh3();
                int a = m.AppendVertex(new Vector3d(X(0), 0, 0)), b = m.AppendVertex(new Vector3d(X(1), 0, 0));
                int c = m.AppendVertex(new Vector3d(X(cx), cy, 0)), v = m.AppendVertex(new Vector3d(X(vx), -h, 0));
                int x = m.AppendVertex(new Vector3d(X(xx), xy, 0));
                foreach (var t in new[] { new Index3i(a, v, b), new Index3i(a, b, c), new Index3i(a, c, x) })
                    Assert.True(m.AppendTriangle(mirror ? new Index3i(t.a, t.c, t.b) : t) >= 0);
                var g = new ExactMeshGeodesicDistance(m);
                if (g.Compute(new[] { v }) != ExactMeshGeodesicDistance.Result.Ok) continue;
                ++asserted;
                ExactInterval d = Dist(m.GetVertex(v), m.GetVertex(x));
                Assert.True(g.Lower[x] <= d.Hi, $"case {it} (h {h:R}): Lower {g.Lower[x]:R} above the straight distance {d.Hi:R}");
                Assert.True(g.Upper[x] >= d.Lo, $"case {it} (h {h:R}): Upper {g.Upper[x]:R} below the straight distance {d.Lo:R}");
                foreach (int u in m.VertexIndices())
                    Assert.True(g.Upper[u] >= Dist(m.GetVertex(v), m.GetVertex(u)).Lo, $"case {it}: vertex {u} Upper below the chord");
            }
            Assert.True(asserted > 150, $"only {asserted} of 400 cases asserted");
        }

        [Fact]
        public void Status_IsReset_WhenComputeThrows()
        {
            DMesh3 m = Grid(2, 5, (x, y) => 0);
            var g = Run(m, 12);
            Assert.Throws<InvalidOperationException>(() => g.Compute(Throwing()));
            Assert.Equal(ExactMeshGeodesicDistance.Result.NotComputed, g.Status);
            Assert.True(double.IsNaN(g.Lower[12]) && double.IsNaN(g.Upper[12]));
        }

        static IEnumerable<int> Throwing()
        {
            yield return 12;
            throw new InvalidOperationException("source enumeration fails");
        }

        [Fact]
        public void ForcedCompaction_OfDeadWindows_ChangesNothing()
        {
            // the compaction of dead windows is speed only; the test meshes never reach its threshold of 8 (terrain
            // 41^2: at most 7 on one side), so force it on every Add and compare bits, windows and merges
            foreach (var (m, s) in new[] { (Grid(10, 21, Terrain), 0), (Grid(5, 11, Terrain), 60) }) {
                var plain = Run(m, s);
                Assert.Equal(0, plain.Compactions);
                var forced = new ExactMeshGeodesicDistance(m) { ForceCompaction = true };
                Assert.Equal(ExactMeshGeodesicDistance.Result.Ok, forced.Compute(new[] { s }));
                Assert.True(forced.Compactions > 0);
                Assert.Equal(plain.Lower, forced.Lower); Assert.Equal(plain.Upper, forced.Upper);
                Assert.Equal(plain.Windows, forced.Windows); Assert.Equal(plain.Merges, forced.Merges);
                forced.Compute(new[] { s });
                Assert.True(forced.Compactions > 0); Assert.Equal(plain.Lower, forced.Lower);
            }
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

        [Fact]
        public void UnreferencedVertex_IsUnreachable_AndTheRestIsComputed()
        {
            // review: a vertex without triangles made the classification throw
            DMesh3 m = Grid(2, 5, (x, y) => 0);
            int lone = m.AppendVertex(new Vector3d(7, 7, 0));
            var g = new ExactMeshGeodesicDistance(m);
            Assert.Equal(ExactMeshGeodesicDistance.Result.NotComputed, g.Status);
            Assert.Equal(ExactMeshGeodesicDistance.Result.Ok, g.Compute(new[] { 12 }));
            Assert.True(double.IsNaN(g.Lower[lone]) && double.IsNaN(g.Upper[lone]));
            foreach (int v in m.VertexIndices()) {
                if (v == lone) continue;
                ExactInterval d = Dist(m.GetVertex(v), m.GetVertex(12));
                Assert.True(g.Lower[v] <= d.Hi && g.Upper[v] >= d.Lo && g.Upper[v] - g.Lower[v] <= 0.08 * (1 + d.Hi), $"vertex {v}");
            }
            Assert.Equal(0, g.Lower[12]);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Plane_UnderSimilarities_Merges_AndExactScalesGiveTheSameBits(string name)
        {
            // review: the merge thresholds once had absolute terms (1 + |c|), so a 2^k scaling changed which windows merged
            Similarity sim = Similarity.ByName(name);
            DMesh3 m0 = Grid(4, 9, (x, y) => 0);
            int src = 4 * 9 + 4;
            var g0 = Run(m0, src);
            var g = Run(Invariance.Transformed(m0, sim), src);
            Assert.True(g0.Merges > 0 && g.Merges > 0, "the plane exercises the merging (A6)");
            foreach (int v in m0.VertexIndices()) {
                if (sim.IsExact) {
                    Assert.Equal(g0.Lower[v] * sim.Scale, g.Lower[v]);
                    Assert.Equal(g0.Upper[v] * sim.Scale, g.Upper[v]);
                } else {
                    double res = sim.Resolution(8) * 1e3;
                    Assert.True(g.Lower[v] / sim.Scale <= g0.Upper[v] + res && g0.Lower[v] <= g.Upper[v] / sim.Scale + res, $"{name}: vertex {v}");
                }
            }
            if (sim.IsExact) { Assert.Equal(g0.Windows, g.Windows); Assert.Equal(g0.Merges, g.Merges); }
        }

        [Theory]
        [InlineData(-40)]
        [InlineData(40)]
        public void Plane_FarPowerOfTwoScales_GiveTheSameBits(int k)
        {
            // the scales of Similarity.All stay within 2^10; absolute terms in a threshold (review: 1 + |c| in A6) show far
            // from 1. The scaled mesh is exact (powers of two), so every decision and every bit must scale with it
            DMesh3 m0 = Grid(4, 9, (x, y) => 0);
            double f = k > 0 ? (double)(1L << 40) : 1.0 / (1L << 40);
            var m = new DMesh3(m0); foreach (int v in m.VertexIndices()) m.SetVertex(v, m0.GetVertex(v) * f);
            var g0 = Run(m0, 40); var g = Run(m, 40);
            Assert.True(g0.Merges > 0);
            Assert.Equal(g0.Merges, g.Merges); Assert.Equal(g0.Windows, g.Windows);
            foreach (int v in m0.VertexIndices()) { Assert.Equal(g0.Lower[v] * f, g.Lower[v]); Assert.Equal(g0.Upper[v] * f, g.Upper[v]); }
        }

        // ------------------------------------------------------------------ the bounds of the steps, against exact references

        static ExactDyadic D(double x) => ExactDyadic.Of(x);

        // |(px, py) - (cx, cy)|^2 <= r^2, exactly
        static bool InDisk(double px, double py, double cx, double cy, double r)
        {
            ExactDyadic dx = D(px) - D(cx), dy = D(py) - D(cy);
            return ExactDyadic.Compare(dx * dx + dy * dy, D(r) * D(r)) <= 0;
        }

        static IEnumerable<ExactMeshGeodesicDistance.P2> AdversarialBoxes(Random rnd, double scale)
        {
            // narrow, wide, very unequal in x and y, a point, and the half widths far below the coordinates
            double[] widths = { 0, 1e-16, 1e-9, 1e-4, 3e-3 };
            for (int k = 0; k < 40; ++k) {
                double x = (rnd.NextDouble() * 2 - 1) * scale, y = (rnd.NextDouble() * 2 - 1) * scale;
                double hx = widths[rnd.Next(widths.Length)] * scale, hy = widths[rnd.Next(widths.Length)] * scale;
                yield return new ExactMeshGeodesicDistance.P2(new ExactInterval(x - hx, x + hx), new ExactInterval(y - hy, y + hy));
            }
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1e-150)]      // squares of the half widths underflow (review: hx*hx + hy*hy lost them)
        [InlineData(1e-300)]      // the half widths are subnormal
        [InlineData(1e150)]
        public void DiskOfBox_ContainsEveryCorner(double scale)
        {
            var rnd = new Random(11);
            foreach (var box in AdversarialBoxes(rnd, scale)) {
                double r = ExactMeshGeodesicDistance.DiskOfBox(box, out double cx, out double cy);
                foreach (double x in new[] { box.X.Lo, box.X.Hi })
                    foreach (double y in new[] { box.Y.Lo, box.Y.Hi })
                        Assert.True(InDisk(x, y, cx, cy, r), $"corner ({x:R}, {y:R}) outside the disk ({cx:R}, {cy:R}, {r:R})");
            }
        }

        // the image of c under the rigid map that takes P to the origin and Q onto the +x axis, compared with the disk
        // (cx, cy, r) exactly: with u = c - P, d = Q - P, A = u . d, B = d x u, N = |d|^2, the image is (A, B) / sqrt(N),
        // and |image - C|^2 <= r^2 is A^2 + B^2 + N (|C|^2 - r^2) <= 2 sqrt(N) (A cx + B cy), decided without roots
        static bool ImageInDisk(double cxw, double cyw, double Px, double Py, double Qx, double Qy, double cx, double cy, double r, bool reflect)
        {
            ExactDyadic ux = D(cxw) - D(Px), uy = D(cyw) - D(Py), dx = D(Qx) - D(Px), dy = D(Qy) - D(Py);
            ExactDyadic A = ux * dx + uy * dy, B = dx * uy - dy * ux, N = dx * dx + dy * dy;
            if (reflect) B = -B;
            ExactDyadic K = A * A + B * B + N * (D(cx) * D(cx) + D(cy) * D(cy) - D(r) * D(r));
            ExactDyadic M = A * D(cx) + B * D(cy);
            ExactDyadic four = D(4);
            if (M.Sign >= 0) return K.Sign <= 0 || ExactDyadic.Compare(K * K, four * N * M * M) <= 0;
            return K.Sign <= 0 && ExactDyadic.Compare(K * K, four * N * M * M) >= 0;
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1e-150)]
        [InlineData(1e-154)]      // the squares of the coordinates are subnormal, those of the half widths underflow
        [InlineData(1e-300)]
        [InlineData(1e150)]
        [InlineData(1e300)]       // the squares overflow
        public void TransformDisk_ContainsTheImageOfEveryPoint_ForEveryEdgeInTheBoxes(double scale)
        {
            var rnd = new Random(12);
            var boxes = AdversarialBoxes(rnd, scale).ToArray();
            int checkedCases = 0;
            for (int k = 0; k + 2 < boxes.Length; k += 3) {
                var PA = boxes[k]; var PB = boxes[k + 1]; var C = boxes[k + 2];
                double wr = rnd.Next(3) == 0 ? 0 : (rnd.NextDouble() * 1e-3 + 1e-12) * scale;
                double wcx = 0.5 * C.X.Lo + 0.5 * C.X.Hi, wcy = 0.5 * C.Y.Lo + 0.5 * C.Y.Hi;
                if (!ExactMeshGeodesicDistance.TransformDisk(wcx, wcy, wr, PA, PB, out double cx, out double cy, out double r)) continue;
                Assert.True(cy <= 0);
                // true edge ends: the corners and the centres of the boxes; points: the disk's centre and points on a smaller circle
                var ends = new List<(double, double)>();
                foreach (var b in new[] { PA, PB }) {
                    var pts = new List<(double, double)> { (0.5 * b.X.Lo + 0.5 * b.X.Hi, 0.5 * b.Y.Lo + 0.5 * b.Y.Hi) };
                    foreach (double x in new[] { b.X.Lo, b.X.Hi }) foreach (double y in new[] { b.Y.Lo, b.Y.Hi }) pts.Add((x, y));
                    ends.Add(pts[rnd.Next(pts.Count)]); ends.Add(pts[0]); ends.Add(pts[1 + rnd.Next(4)]);
                }
                for (int t = 0; t < 8; ++t) {
                    double a = rnd.NextDouble() * 2 * Math.PI, rr = wr * 0.999;
                    double px = t == 0 ? wcx : wcx + rr * Math.Cos(a), py = t == 0 ? wcy : wcy + rr * Math.Sin(a);
                    if (!InDisk(px, py, wcx, wcy, wr)) continue;
                    for (int i = 0; i < 3; ++i) {
                        var (Px, Py) = ends[i]; var (Qx, Qy) = ends[3 + i];
                        Assert.True(ImageInDisk(px, py, Px, Py, Qx, Qy, cx, cy, r, false) || ImageInDisk(px, py, Px, Py, Qx, Qy, cx, cy, r, true),
                            $"scale {scale}: the image of ({px:R}, {py:R}) leaves the disk ({cx:R}, {cy:R}, {r:R})");
                        ++checkedCases;
                    }
                }
            }
            Assert.True(checkedCases > 50, $"scale {scale}: only {checkedCases} cases");
        }

        [Fact]
        public void Hypot_IsAccurate_AtEveryScale()
        {
            var rnd = new Random(13);
            foreach (double scale in new[] { 1.0, 1e-154, 1e-300, 1e-320, 1e300, double.MaxValue / 4 })
                for (int k = 0; k < 200; ++k) {
                    double x = (rnd.NextDouble() * 2 - 1) * scale, y = (rnd.NextDouble() * 2 - 1) * scale * (k % 3 == 0 ? 1e-9 : 1);
                    double h = ExactMeshGeodesicDistance.Hypot(x, y);
                    // exactly: (h (1 - 4e-16))^2 <= x^2 + y^2 <= (h (1 + 4e-16))^2, or within half the smallest spacing
                    ExactDyadic s2 = D(x) * D(x) + D(y) * D(y), tol = D(double.Epsilon);
                    ExactDyadic lo = D(h) - D(h) * D(4e-16) - tol, hi = D(h) + D(h) * D(4e-16) + tol;
                    Assert.True((lo.Sign <= 0 || ExactDyadic.Compare(lo * lo, s2) <= 0) && ExactDyadic.Compare(hi * hi, s2) >= 0, $"hypot({x:R}, {y:R}) = {h:R}");
                }
            Assert.Equal(0, ExactMeshGeodesicDistance.Hypot(0, 0));
            Assert.Equal(5, ExactMeshGeodesicDistance.Hypot(-3, 4));
        }

        [Fact]
        public void CornerAngle_EnclosesKnownAngles_AndTheHardAtan2Cases()
        {
            Vector3d O = Vector3d.Zero, X = new Vector3d(1, 0, 0);
            void Contains(ExactInterval a, double below, double above, string what)
            {
                Assert.True(a.Lo <= below && a.Hi >= above, $"{what}: [{a.Lo:R}, {a.Hi:R}]");
                Assert.True(a.Hi - a.Lo <= 64 * (ExactInterval.Up(Math.Max(Math.Abs(a.Hi), double.Epsilon)) - Math.Abs(a.Hi)) + 1e-300, $"{what}: tight, [{a.Lo:R}, {a.Hi:R}], {(a.Hi - a.Lo) / (ExactInterval.Up(Math.Abs(a.Hi)) - Math.Abs(a.Hi))} spacings");
            }
            // Math.PI is below pi: pi/4 and pi/2 lie in (Math.PI/4, Up(Math.PI/4)) and the same for pi/2 (exact scalings)
            Contains(ExactMeshGeodesicDistance.CornerAngle(O, X, new Vector3d(0, 1, 0)), Math.PI / 2, ExactInterval.Up(Math.PI / 2), "pi/2");
            Contains(ExactMeshGeodesicDistance.CornerAngle(O, X, new Vector3d(1, 1, 0)), Math.PI / 4, ExactInterval.Up(Math.PI / 4), "pi/4");
            // 3 pi/4 lies within a spacing of the rounded 0.75 Math.PI
            double t = 0.75 * Math.PI;
            Contains(ExactMeshGeodesicDistance.CornerAngle(O, X, new Vector3d(-1, 1, 0)), ExactInterval.Down(t), ExactInterval.Up(t), "3pi/4");
            // skew in 3D: the angle between (1, 1, 0) and (0, 1, 1) is pi/3
            Contains(ExactMeshGeodesicDistance.CornerAngle(O, new Vector3d(1, 1, 0), new Vector3d(0, 1, 1)), ExactInterval.Down(Math.PI / 3), ExactInterval.Up(Math.PI / 3), "pi/3");
            // nearly straight: pi - 1e-17 lies above Math.PI (which is pi - 1.2e-16)
            Contains(ExactMeshGeodesicDistance.CornerAngle(O, X, new Vector3d(-1, 1e-17, 0)), Math.PI, ExactInterval.Up(Math.PI), "pi - 1e-17");
            // a tiny angle: atan(1e-150) lies just below 1e-150
            Contains(ExactMeshGeodesicDistance.CornerAngle(O, X, new Vector3d(1, 1e-150, 0)), ExactInterval.Down(1e-150), 1e-150, "1e-150");
            // smaller, the square of the cross product underflows: no certain lower end above 0, so the enclosure may be the
            // whole [0, 2 pi], but it must still contain the angle
            ExactInterval tiny = ExactMeshGeodesicDistance.CornerAngle(O, X, new Vector3d(1, 1e-200, 0));
            Assert.True(tiny.Lo <= ExactInterval.Down(1e-200) && tiny.Hi >= 1e-200);
            // a corner whose sides overlap (zero cross product): no angle is certain, the whole [0, 2 pi]
            ExactInterval z = ExactMeshGeodesicDistance.CornerAngle(O, X, new Vector3d(2, 0, 0));
            Assert.True(z.Lo <= 0 && z.Hi >= 2 * Math.PI);
        }

        // ------------------------------------------------------------------ the edge path pass and the pruning (gate 3.0 shard geodesic-1)

        // a shallow convex dome: its interior vertices are certainly convex, so they never emit and get their upper bounds
        // only from windows, whose widening leaves some of them above an edge path through a neighbour (the shortest path
        // passes close to a convex vertex). The edge path pass after the propagation must lower them: then Upper is exactly a
        // fixpoint of the edge paths, Upper(u) <= (Upper(v) + |uv|).Hi in the library's own interval arithmetic, on every edge.
        [Theory]
        [InlineData(1e-4, 24)]      // the centre: about 16 vertices lowered by the edge path pass, chains of two included
        [InlineData(1e-6, 0)]       // a corner: about 9
        public void ShallowDome_UpperIsExactlyAFixpointOfTheEdgePaths(double eps, int src)
        {
            DMesh3 m = Grid(-1, -1, 1, 1, 7, 7, (x, y) => -eps * (x * x + y * y));
            Assert.Equal(0, Kind(m, 3 * 7 + 3));        // the pass matters only next to convex vertices, which never emit
            var g = Run(m, src);
            Vector3d s = m.GetVertex(src);
            double[] dij = EdgeDijkstra(m, src);
            foreach (int v in m.VertexIndices()) {
                // convex: the 3D distance is a lower bound of the geodesic one, an edge path an upper bound
                Assert.True(g.Lower[v] <= dij[v] * (1 + 1e-12), $"vertex {v}: Lower above an edge path");
                Assert.True(g.Upper[v] >= Dist(s, m.GetVertex(v)).Lo, $"vertex {v}: Upper below the 3D distance");
                Assert.True(g.Upper[v] <= dij[v] * (1 + 1e-12), $"vertex {v}: Upper above an edge path");
                // (a pseudo-source re-emits 1e-9 below its lower bound, Emit)
                Assert.True(g.Lower[v] >= Dist(s, m.GetVertex(v)).Lo * (1 - 1e-8), $"vertex {v}: Lower {g.Lower[v]:R} below the 3D distance");
            }
            foreach (int e in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(e);
                ExactInterval l = Dist(m.GetVertex(ev.a), m.GetVertex(ev.b));
                Assert.True(g.Upper[ev.a] <= (I(g.Upper[ev.b]) + l).Hi, $"edge {e}: Upper[{ev.a}] = {g.Upper[ev.a]:R} above the path through {ev.b}");
                Assert.True(g.Upper[ev.b] <= (I(g.Upper[ev.a]) + l).Hi, $"edge {e}: Upper[{ev.b}] = {g.Upper[ev.b]:R} above the path through {ev.a}");
            }
        }

        // Dominated (A4) through reflection (Window is private): the parts of [l, r] where window a is certainly worse than
        // window b, found by bisection to the given depth. Two point origins at height -1 below x = 0 and x = 4, sigma 0:
        // a is worse exactly on (2, 4]. Depth 6 halves [0, 4] six times (pieces of 1/16): the pieces from about 2.2 on.
        static List<(double l, double r)> Dominated(double ax, double bx, double l, double r, int depth)
        {
            var g = new ExactMeshGeodesicDistance(new DMesh3());
            Type wt = typeof(ExactMeshGeodesicDistance).GetNestedType("Window", System.Reflection.BindingFlags.NonPublic);
            object a = Activator.CreateInstance(wt, true), b = Activator.CreateInstance(wt, true);
            var point = new Func<double, ExactMeshGeodesicDistance.P2>(x => new ExactMeshGeodesicDistance.P2(I(x), I(-1)));
            var rem = new List<(double l, double r)>();
            System.Reflection.MethodInfo dom = typeof(ExactMeshGeodesicDistance).GetMethod("Dominated",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            dom.Invoke(g, new object[] { a, point(ax), b, point(bx), l, r, depth, rem });
            return rem;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Dominated_FindsTheDominatedPieces_ByBisection(bool mirror)
        {
            // mirror: a at 4, b at 0, a worse on [0, 2)
            double ax = mirror ? 4 : 0, bx = mirror ? 0 : 4;
            var rem = Dominated(ax, bx, 0, 4, 6);
            foreach (var (l, r) in rem) {
                Assert.True(0 <= l && l < r && r <= 4, $"piece [{l}, {r}] outside [0, 4]");
                // certainly worse: the piece lies on a's far side of the bisector x = 2
                Assert.True(mirror ? r <= 2 : l >= 2, $"piece [{l}, {r}] not dominated");
            }
            // the pieces cover all of [2.25, 4] (or [0, 1.75]): every half was searched, to depth 6
            double covered = 0; foreach (var (l, r) in rem) covered += r - l;
            for (double x = 2.25; x <= 4; x += 1.0 / 64) {
                double t = mirror ? 4 - x : x;
                Assert.True(rem.Exists(p => p.l <= t && t <= p.r), $"{t} not covered (pieces {string.Join(", ", rem)})");
            }
            Assert.True(covered >= 1.75 && covered < 2);
            // depth 0: only the whole interval, which is not dominated
            Assert.Empty(Dominated(ax, bx, 0, 4, 0));
            // the whole interval dominated: one piece at once
            Assert.Equal(new[] { (2.5, 4.0) }, Dominated(0, 4, 2.5, 4, 0).ToArray());
        }
    }
}
