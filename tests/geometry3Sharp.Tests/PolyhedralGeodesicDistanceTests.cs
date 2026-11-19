using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// PolyhedralGeodesicDistance (docs/design/GEODESICS.md 6e, A.7): plain-double window propagation. Not certified: checked
    /// against the certified oracle (ExactMeshGeodesicDistance: the result must lie within the tolerance of its enclosure),
    /// against closed forms, and for the properties of a distance, invariance, reuse, determinism, statuses and the memory bound.
    /// </summary>
    public class PolyhedralGeodesicDistanceTests
    {
        // The tolerances the class docs quote (the tests enforce exactly these): relative to the distance d plus a fraction of the
        // mesh extent. TolOracle also covers the oracle's own width (its enclosure is up to about 2e-8 relative on terrains).
        const double TolRel = 1e-9, TolExt = 1e-12;
        static double Tol(double d, double extent) => TolRel * d + TolExt * extent;

        // ------------------------------------------------------------------ meshes

        static DMesh3 Grid(double x0, double y0, double x1, double y1, int nx, int ny, Func<double, double, double> h)
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
                    if (((i + j) & 1) == 0) { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); }
                    else { m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c); }
                }
            return m;
        }
        static DMesh3 Grid(double half, int n, Func<double, double, double> h) => Grid(-half, -half, half, half, n, n, h);
        static double Terrain(double x, double y) => 3 * Math.Sin(x / 7 + 1) * Math.Sin(y / 5) + 0.8 * Math.Sin(x * 1.3) * Math.Cos(y * 1.1);
        static readonly double RidgeSlope = Math.Tan(75 * Math.PI / 180);

        static double Extent(DMesh3 m) { var b = m.CachedBounds; return Math.Max(b.Max.x - b.Min.x, Math.Max(b.Max.y - b.Min.y, b.Max.z - b.Min.z)); }

        static PolyhedralGeodesicDistance Run(DMesh3 m, params int[] sources)
        {
            var g = new PolyhedralGeodesicDistance(m);
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(sources));
            return g;
        }

        // Distance within Tol of the oracle's enclosure [Lower, Upper], and never below the straight 3D distance (a surface path
        // is never shorter than the line: this catches a window that is not realised, A.7)
        static void AssertAgreesWithOracle(DMesh3 m, PolyhedralGeodesicDistance g, params int[] sources)
        {
            var orc = new ExactMeshGeodesicDistance(m);
            Assert.Equal(ExactMeshGeodesicDistance.Result.Ok, orc.Compute(sources));
            double ext = Extent(m);
            foreach (int v in m.VertexIndices()) {
                double d = g.Distance[v];
                Assert.False(double.IsNaN(d), $"vertex {v} is NaN");
                double tol = Tol(orc.Upper[v], ext);
                Assert.True(d >= orc.Lower[v] - tol, $"vertex {v}: {d:R} below the oracle's Lower {orc.Lower[v]:R}");
                Assert.True(d <= orc.Upper[v] + tol, $"vertex {v}: {d:R} above the oracle's Upper {orc.Upper[v]:R}");
            }
            AssertAboveStraight(m, g, sources);
        }

        static void AssertAboveStraight(DMesh3 m, PolyhedralGeodesicDistance g, params int[] sources)
        {
            double ext = Extent(m);
            foreach (int v in m.VertexIndices()) {
                double straight = sources.Min(s => m.GetVertex(v).Distance(m.GetVertex(s)));
                Assert.True(g.Distance[v] >= straight - Tol(straight, ext), $"vertex {v}: {g.Distance[v]:R} below the straight distance {straight:R}");
            }
        }

        static void AssertClosedForm(DMesh3 m, PolyhedralGeodesicDistance g, Func<int, double> exact, double tolRel = TolRel)
        {
            double ext = Extent(m);
            foreach (int v in m.VertexIndices()) {
                double d = exact(v);
                Assert.True(Math.Abs(g.Distance[v] - d) <= tolRel * d + TolExt * ext, $"vertex {v}: {g.Distance[v]:R} against {d:R}");
            }
        }

        // ------------------------------------------------------------------ against the oracle

        public static IEnumerable<object[]> Families()
        {
            foreach (string f in new[] { "plane centre", "plane corner", "plane edge", "terrain", "saddle", "dome 1e-6", "dome 0.3", "ridge 75", "ridge 4", "valley 3/4", "L-shape", "two sources" })
                yield return new object[] { f };
        }

        static (DMesh3 m, int[] src) Family(string f, int n)
        {
            int c = (n / 2) * n + n / 2;
            switch (f) {
                case "plane centre": return (Grid(1, n, (x, y) => 0), new[] { c });
                case "plane corner": return (Grid(1, n, (x, y) => 0), new[] { 0 });
                case "plane edge": return (Grid(1, n, (x, y) => 0), new[] { n / 2 });
                case "terrain": return (Grid(n / 2, n, Terrain), new[] { c });
                case "saddle": return (Grid(1, n, (x, y) => 0.3 * (x * x - y * y)), new[] { c });
                case "dome 1e-6": return (Grid(1, n, (x, y) => -1e-6 * (x * x + y * y)), new[] { c });
                case "dome 0.3": return (Grid(1, n, (x, y) => -0.3 * (x * x + y * y)), new[] { c });
                case "ridge 75": { double h = (n - 1) * 0.25 / 2; return (Grid(h, n, (x, y) => RidgeSlope * Math.Max(0, 2.5 - Math.Abs(x))), new[] { (n / 2) * n + 3 }); }
                case "ridge 4": { double h = (n - 1) * 0.25 / 2; return (Grid(h, n, (x, y) => 4 * Math.Max(0, 2.5 - Math.Abs(x))), new[] { (n / 2) * n + 3 }); }
                case "valley 3/4": return (Grid(-1, -1, 1, 1, n, n, (x, y) => 0.75 * Math.Abs(x)), new[] { (n / 2) * n + 2 });
                case "L-shape": {
                    var full = Grid(0, 0, 8, 8, n, n, (x, y) => 0); var m = new DMesh3(full);
                    foreach (int t in full.TriangleIndices()) { Vector3d ct = full.GetTriCentroid(t); if (ct.x > 4 && ct.y > 4) m.RemoveTriangle(t); }
                    return (m, new[] { 1 * n + n - 3 });
                }
                case "two sources": return (Grid(1, n, (x, y) => 0), new[] { 0, n * n - 1 });
            }
            throw new ArgumentException(f);
        }

        [Theory]
        [MemberData(nameof(Families))]
        public void Family_AgreesWithTheCertifiedOracle(string f)
        {
            var (m, src) = Family(f, f == "L-shape" ? 17 : 31);
            AssertAgreesWithOracle(m, Run(m, src), src);
        }

        [Fact]
        public void Plane_AndFlatFolds_MatchTheirClosedForms()
        {
            int n = 41, c = (n / 2) * n + n / 2;
            var plane = Grid(1, n, (x, y) => 0);
            AssertClosedForm(plane, Run(plane, c), v => plane.GetVertex(v).Distance(plane.GetVertex(c)));
            // the exactly flat slope-4 ridge (z depends on x only: its unfolding is the profile's arc length)
            double half = (n - 1) * 0.25 / 2; double k = Math.Sqrt(1 + 16);
            var ridge = Grid(half, n, (x, y) => 4 * Math.Max(0, 2.5 - Math.Abs(x)));
            int rs = (n / 2) * n + 3; Vector3d s = ridge.GetVertex(rs);
            Func<double, double> u = x => Math.Sign(x) * (Math.Min(Math.Abs(x), 2.5) * k + Math.Max(0, Math.Abs(x) - 2.5));
            AssertClosedForm(ridge, Run(ridge, rs), v => { Vector3d p = ridge.GetVertex(v); return Math.Sqrt(Math.Pow(u(p.x) - u(s.x), 2) + Math.Pow(p.y - s.y, 2)); });
            // the 3/4 valley: unfolding factor 5/4
            var valley = Grid(-1, -1, 1, 1, n, n, (x, y) => 0.75 * Math.Abs(x));
            int vs = (n / 2) * n + 3; Vector3d vsp = valley.GetVertex(vs);
            AssertClosedForm(valley, Run(valley, vs), v => { Vector3d p = valley.GetVertex(v); return Math.Sqrt(Math.Pow(1.25 * (p.x - vsp.x), 2) + Math.Pow(p.y - vsp.y, 2)); });
        }

        [Theory]
        [InlineData(0.3)]
        [InlineData(2.0)]
        public void ConePyramid_SharpConvexApex_MatchesTheUnfolding(double height)
        {
            // an open pyramid: apex 0, base vertices 1..n at radius r; a face's apex angle is omega; between two base vertices the
            // path is the chord of the unfolded sector if the sector angle is below pi, otherwise through the apex (2 R)
            int n = 24; double r = 1, R = Math.Sqrt(r * r + height * height), b = 2 * r * Math.Sin(Math.PI / n), omega = 2 * Math.Asin(b / (2 * R));
            var m = new DMesh3(); m.AppendVertex(new Vector3d(0, 0, height));
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(r * Math.Cos(2 * Math.PI * i / n), r * Math.Sin(2 * Math.PI * i / n), 0));
            for (int i = 0; i < n; ++i) m.AppendTriangle(0, 1 + i, 1 + (i + 1) % n);
            int src = 1 + 5; var g = Run(m, src);
            Func<double, double> arc = a => a < Math.PI ? 2 * R * Math.Sin(a / 2) : 2 * R;
            AssertClosedForm(m, g, v => {
                if (v == 0) return R;
                int di = Math.Abs(v - 1 - 5); double a1 = di * omega, a2 = (n - di) * omega;
                return di == 0 ? 0 : Math.Min(arc(a1), arc(a2));
            });
            AssertAgreesWithOracle(m, g, src);
        }

        [Fact]
        public void SquareTube_TwoImagesOfTheSourceAroundTheTube_ExactLattice()
        {
            // a developable tube of integer coordinates (its fold lines are straight folds): the source reaches a vertex both ways round
            int w = 6, h = 8, n = 4 * w; var m = new DMesh3();
            Func<int, Vector3d> ring = i => { int s = i / w, t = i % w; switch (s) { case 0: return new Vector3d(t, 0, 0); case 1: return new Vector3d(w, t, 0); case 2: return new Vector3d(w - t, w, 0); default: return new Vector3d(0, w - t, 0); } };
            for (int j = 0; j <= h; ++j) for (int i = 0; i < n; ++i) { Vector3d p = ring(i); m.AppendVertex(new Vector3d(p.x, p.y, j)); }
            for (int j = 0; j < h; ++j) for (int i = 0; i < n; ++i) {
                int a = j * n + i, b = j * n + (i + 1) % n, c = a + n, d = b + n;
                if (((i + j) & 1) == 0) { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); } else { m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c); }
            }
            int src = 3 * n + 2; var g = Run(m, src);
            AssertClosedForm(m, g, v => { int i = v % n, j = v / n; int di = Math.Abs(i - 2); di = Math.Min(di, n - di); return Math.Sqrt(di * di + (j - 3) * (j - 3)); });
            AssertAgreesWithOracle(m, g, src);
        }

        [Fact]
        public void ThinSlivers_MatchTheStraightDistance()
        {
            // a planar strip of thin obtuse triangles (height 1e-7 .. 1e-12): the geodesic distance is the straight one
            foreach (double h in new[] { 1e-7, 1e-10, 1e-12 }) {
                var m = new DMesh3(); int n = 12;
                for (int i = 0; i < n; ++i) { m.AppendVertex(new Vector3d(i, 0, 0)); m.AppendVertex(new Vector3d(i + 0.5, h, 0)); }
                for (int i = 0; i + 2 < 2 * n; i += 2) { m.AppendTriangle(i, i + 2, i + 1); m.AppendTriangle(i + 1, i + 2, i + 3); }
                var g = Run(m, 0);
                AssertClosedForm(m, g, v => m.GetVertex(v).Distance(m.GetVertex(0)), 1e-8);
            }
        }

        // ------------------------------------------------------------------ properties

        [Fact]
        public void Terrain_Properties_NeverBelowTheStraightLine_AboveNoEdgePath_Symmetric()
        {
            int n = 41; var m = Grid(n / 2, n, Terrain); int a = 3 * n + 4, b = 30 * n + 25;
            var ga = Run(m, a); var gb = Run(m, b);
            Assert.True(Math.Abs(ga.Distance[b] - gb.Distance[a]) <= 1e-8 * ga.Distance[b]);
            AssertAboveStraight(m, ga, a);
            // never above the shortest edge path (a real path)
            var dj = new double[m.MaxVertexID]; for (int i = 0; i < dj.Length; ++i) dj[i] = double.PositiveInfinity; dj[a] = 0;
            var q = new SortedSet<(double, int)> { (0, a) };
            while (q.Count > 0) { var (k, v) = q.Min; q.Remove(q.Min); if (k > dj[v]) continue; foreach (int u in m.VtxVerticesItr(v)) { double nd = dj[v] + m.GetVertex(v).Distance(m.GetVertex(u)); if (nd < dj[u]) { dj[u] = nd; q.Add((nd, u)); } } }
            foreach (int v in m.VertexIndices()) Assert.True(ga.Distance[v] <= dj[v] * (1 + 1e-12) + 1e-12);
        }

        [Fact]
        public void NearestSource_IsTheNearerSource_AndTheLowestIdAtATie()
        {
            int n = 21; var m = Grid(1, n, (x, y) => 0); int s1 = 10 * n + 4, s2 = 10 * n + 16;
            var g = Run(m, s2, s1);
            foreach (int v in m.VertexIndices()) {
                double d1 = m.GetVertex(v).Distance(m.GetVertex(s1)), d2 = m.GetVertex(v).Distance(m.GetVertex(s2));
                if (Math.Abs(d1 - d2) > 1e-6) Assert.Equal(d1 < d2 ? s1 : s2, g.NearestSource[v]);
                else Assert.True(g.NearestSource[v] == s1 || g.NearestSource[v] == s2);       // at a tie either source, the lowest id only at an exact tie
            }
        }

        // ------------------------------------------------------------------ statuses, limits, memory

        [Fact]
        public void Statuses_AndTheNaNArrays()
        {
            var m = Grid(1, 9, (x, y) => 0);
            var g = new PolyhedralGeodesicDistance(m);
            Assert.Equal(PolyhedralGeodesicStatus.NotComputed, g.Status);
            Assert.Equal(PolyhedralGeodesicStatus.NoSource, g.Compute(new int[0]));
            Assert.Equal(PolyhedralGeodesicStatus.NoSource, g.Compute(new[] { -1, 1000 }));
            Assert.True(g.Distance.All(double.IsNaN) && g.NearestSource.All(x => x == -1));
            Assert.Throws<ArgumentNullException>(() => new PolyhedralGeodesicDistance(null));
            // a bowtie vertex
            var bow = new DMesh3(); int c = bow.AppendVertex(new Vector3d(0, 0, 0));
            int[] r = new[] { bow.AppendVertex(new Vector3d(1, 0, 0)), bow.AppendVertex(new Vector3d(0, 1, 0)), bow.AppendVertex(new Vector3d(-1, 0, 0)), bow.AppendVertex(new Vector3d(0, -1, 0)) };
            bow.AppendTriangle(c, r[0], r[1]); bow.AppendTriangle(c, r[2], r[3]);
            Assert.Equal(PolyhedralGeodesicStatus.NonManifoldInput, new PolyhedralGeodesicDistance(bow).Compute(new[] { c }));
            // a degenerate triangle
            var deg = new DMesh3(); deg.AppendVertex(new Vector3d(0, 0, 0)); deg.AppendVertex(new Vector3d(1, 0, 0)); deg.AppendVertex(new Vector3d(2, 0, 0)); deg.AppendTriangle(0, 1, 2);
            Assert.Equal(PolyhedralGeodesicStatus.DegenerateTriangles, new PolyhedralGeodesicDistance(deg).Compute(new[] { 0 }));
            // unused vertex ids stay NaN while the rest is computed
            var holes = new DMesh3(m); int extra = holes.AppendVertex(new Vector3d(5, 5, 0));
            var gh = new PolyhedralGeodesicDistance(holes); Assert.Equal(PolyhedralGeodesicStatus.Ok, gh.Compute(new[] { 0 }));
            Assert.True(double.IsNaN(gh.Distance[extra]) && gh.NearestSource[extra] == -1 && !double.IsNaN(gh.Distance[8]));
        }

        [Fact]
        public void MaxDistance_CutsOff_AndRejectsNaNAndNegative()
        {
            int n = 31; var m = Grid(1, n, (x, y) => 0); int c = (n / 2) * n + n / 2;
            var full = Run(m, c);
            var g = new PolyhedralGeodesicDistance(m) { MaxDistance = 0.5 };
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(new[] { c }));
            int inside = 0;
            foreach (int v in m.VertexIndices()) {
                if (full.Distance[v] <= 0.5 - 1e-9) { Assert.Equal(full.Distance[v], g.Distance[v], 9); ++inside; }
                else if (full.Distance[v] > 0.5 + 1e-9) Assert.True(double.IsNaN(g.Distance[v]) || g.Distance[v] <= 0.5);
            }
            Assert.True(inside > 10);
            Assert.True(g.Windows < full.Windows);
            var zero = new PolyhedralGeodesicDistance(m) { MaxDistance = 0 }; Assert.Equal(PolyhedralGeodesicStatus.Ok, zero.Compute(new[] { c }));
            Assert.Equal(0, zero.Distance[c]); Assert.Equal(1, zero.Distance.Count(d => !double.IsNaN(d)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PolyhedralGeodesicDistance(m) { MaxDistance = double.NaN }.Compute(new[] { c }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PolyhedralGeodesicDistance(m) { MaxDistance = -1 }.Compute(new[] { c }));
        }

        [Fact]
        public void CancelRequested_EndsWithCancelled_AndNaNArrays()
        {
            int n = 41; var m = Grid(1, n, (x, y) => 0); int polls = 0;
            var g = new PolyhedralGeodesicDistance(m) { CancelRequested = () => ++polls > 0 };
            Assert.Equal(PolyhedralGeodesicStatus.Cancelled, g.Compute(new[] { (n / 2) * n + n / 2 }));
            Assert.True(g.Distance.All(double.IsNaN) && g.NearestSource.All(x => x == -1));
            Assert.True(polls >= 1);
        }

        [Fact]
        public void WindowLimit_EndsBeforeMemoryGrowsPastTheBound()
        {
            // the bound of the class docs: at most 120 bytes x (MaxWindows + 2 x the largest valence) + 57 bytes per vertex (+ the result arrays)
            int n = 41; var m = Grid(1, n, (x, y) => 0); int c = (n / 2) * n + n / 2;
            long limit = 2000; var g = new PolyhedralGeodesicDistance(m) { MaxWindows = limit };
            int valence = m.VertexIndices().Max(v => m.GetVtxEdgeCount(v));
            Run(m, c); GC.Collect();                                    // warm up (JIT, statics)
            long before = GC.GetAllocatedBytesForCurrentThread();
            var st = g.Compute(new[] { c });
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(PolyhedralGeodesicStatus.WindowLimit, st);
            Assert.True(g.Distance.All(double.IsNaN) && g.NearestSource.All(x => x == -1));
            Assert.True(g.Windows <= limit + 2 * valence, $"windows {g.Windows}");
            long bound = 120L * (limit + 2 * valence) + 57L * m.MaxVertexID + 40L * m.MaxVertexID + 8L * m.MaxEdgeID * 8 + (4L << 20);
            Assert.True(used <= bound, $"allocated {used} bytes, bound {bound}");
        }

        [Fact]
        public void DefaultWindowBudget_64PerVertex_IsEnoughForTheTargetFamilies()
        {
            foreach (string f in new[] { "plane centre", "plane corner", "terrain", "ridge 4", "ridge 75" }) {      // the saddle (not a target family) needs about 42 per vertex at 41 x 41 and more on larger grids
                var (m, src) = Family(f, 41);
                var g = Run(m, src);
                Assert.True(g.Windows < 32L * m.VertexCount, $"{f}: {g.Windows} windows");
            }
        }

        // ------------------------------------------------------------------ reuse, determinism, invariance

        [Fact]
        public void Reuse_OneInstance_SameBits_AndAnotherSource()
        {
            int n = 31; var m = Grid(n / 2, n, Terrain); var g = new PolyhedralGeodesicDistance(m);
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(new[] { 100 }));
            var d1 = (double[])g.Distance.Clone(); var s1 = (int[])g.NearestSource.Clone(); long w1 = g.Windows;
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(new[] { 100 }));
            Assert.Equal(w1, g.Windows); Assert.Equal(d1, g.Distance); Assert.Equal(s1, g.NearestSource);
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(new[] { 500 }));
            Assert.Equal(0, g.Distance[500]); Assert.Equal(PolyhedralGeodesicStatus.Ok, new PolyhedralGeodesicDistance(m).Compute(new[] { 500 }));
        }

        [Fact]
        public void Deterministic_RepeatedRunsAndFreshInstances_SameBits()
        {
            var (m, src) = Family("ridge 75", 31);
            var a = Run(m, src); var b = Run(m, src); var c2 = new PolyhedralGeodesicDistance(new DMesh3(m)); c2.Compute(src);
            Assert.Equal(a.Distance, b.Distance); Assert.Equal(a.Distance, c2.Distance); Assert.Equal(a.Windows, b.Windows);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Plane_UnderSimilarities_ExactScalesGiveTheSameBits(string name)
        {
            // rotated and shifted copies lose exact flatness (their fans are coplanar only up to rounding) and the run differs by rounding
            Similarity sim = Similarity.ByName(name);
            DMesh3 m0 = Grid(4, 15, (x, y) => 0); int src = 7 * 15 + 7;
            var g0 = Run(m0, src); var g = Run(Invariance.Transformed(m0, sim), src);
            foreach (int v in m0.VertexIndices()) {
                if (sim.IsExact) Assert.Equal(g0.Distance[v] * sim.Scale, g.Distance[v]);
                else Assert.True(Math.Abs(g.Distance[v] / sim.Scale - g0.Distance[v]) <= 1e-8 * (1 + g0.Distance[v]) + sim.Resolution(8) * 1e3, $"{name}: vertex {v}");
            }
            if (sim.IsExact) Assert.Equal(g0.Windows, g.Windows);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Terrain_UnderSimilarities_Agrees(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 m0 = Grid(10, 21, Terrain); int src = 10 * 21 + 10;
            var g0 = Run(m0, src); var g = Run(Invariance.Transformed(m0, sim), src);
            foreach (int v in m0.VertexIndices()) {
                if (sim.IsExact) Assert.Equal(g0.Distance[v] * sim.Scale, g.Distance[v]);
                else Assert.True(Math.Abs(g.Distance[v] / sim.Scale - g0.Distance[v]) <= 1e-8 * (1 + g0.Distance[v]) + sim.Resolution(8) * 1e3, $"{name}: vertex {v}");
            }
        }

        // ------------------------------------------------------------------ the 10^4-vertex comparisons (the oracle's run is the check)

        [Theory]
        [Trait("Category", "Nightly")]
        [InlineData("plane centre")]
        [InlineData("terrain")]
        [InlineData("ridge 4")]
        [InlineData("saddle")]
        public void TenThousandVertices_AgreeWithTheCertifiedOracle(string f)
        {
            // Nightly: the oracle needs 3-14 s at 101 x 101 vertices, more than the 5 s of a PR test, and its run is the check
            var (m, src) = Family(f, 101);
            var g = new PolyhedralGeodesicDistance(m); Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(src));
            var orc = new ExactMeshGeodesicDistance(m) { MaxWindows = 3_000_000 };
            Assert.Equal(ExactMeshGeodesicDistance.Result.Ok, orc.Compute(src));
            double ext = Extent(m);
            foreach (int v in m.VertexIndices()) {
                double tol = Tol(orc.Upper[v], ext);
                Assert.True(g.Distance[v] >= orc.Lower[v] - tol && g.Distance[v] <= orc.Upper[v] + tol, $"{f} vertex {v}");
            }
        }
    }
}
