using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// PolyhedralGeodesicDistance.TracePath (docs/design/GEODESICS.md A.8): the exact shortest path by backtracing. Checked against the distance of the run (the
    /// length of the path must equal it to the tolerance of the class), against the geometry (every segment inside its triangle, the path bends only at
    /// vertices of angle >= 2 pi), against FlipOut (never shorter than the exact path), for invariance, reuse, determinism, the statuses, the guard and ties.
    /// </summary>
    public class PolyhedralGeodesicPathTests
    {
        // the tolerance the class docs quote for distances; the measured deviation of Length from Distance is 1.7e-15 relative on the families below
        const double TolRel = 5e-15, TolExt = 5e-16;
        static double Tol(double d, double ext) => TolRel * d + TolExt * ext;

        // ------------------------------------------------------------------ meshes (as in PolyhedralGeodesicDistanceTests)

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
        static double Extent(DMesh3 m) { var b = m.CachedBounds; return Math.Max(b.Max.x - b.Min.x, Math.Max(b.Max.y - b.Min.y, b.Max.z - b.Min.z)); }

        static DMesh3 Rotated(DMesh3 m, double scale)
        {
            var r = new DMesh3(m);
            foreach (int v in r.VertexIndices()) { Vector3d p = r.GetVertex(v); r.SetVertex(v, new Vector3d((0.6 * p.x - 0.8 * p.y) * scale + 0.3, (0.8 * p.x + 0.6 * p.y) * scale - 0.7, p.z * scale + 1.1)); }
            return r;
        }
        static DMesh3 Jittered(DMesh3 m, double amp, int seed)
        {
            var rnd = new Random(seed); var r = new DMesh3(m);
            foreach (int v in r.VertexIndices()) { Vector3d p = r.GetVertex(v); r.SetVertex(v, new Vector3d(p.x + amp * (rnd.NextDouble() - 0.5), p.y + amp * (rnd.NextDouble() - 0.5), p.z + amp * (rnd.NextDouble() - 0.5))); }
            return r;
        }
        static DMesh3 Tube()
        {
            int w = 6, h = 8, n = 4 * w; var m = new DMesh3();
            Func<int, Vector3d> ring = i => { int s = i / w, t = i % w; switch (s) { case 0: return new Vector3d(t, 0, 0); case 1: return new Vector3d(w, t, 0); case 2: return new Vector3d(w - t, w, 0); default: return new Vector3d(0, w - t, 0); } };
            for (int j = 0; j <= h; ++j) for (int i = 0; i < n; ++i) { Vector3d p = ring(i); m.AppendVertex(new Vector3d(p.x, p.y, j)); }
            for (int j = 0; j < h; ++j) for (int i = 0; i < n; ++i) {
                int a = j * n + i, b = j * n + (i + 1) % n, c = a + n, d = b + n;
                if (((i + j) & 1) == 0) { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); } else { m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c); }
            }
            return m;
        }
        static DMesh3 LShape(int n)
        {
            var full = Grid(0, 0, 8, 8, n, n, (x, y) => 0); var m = new DMesh3(full);
            foreach (int t in full.TriangleIndices()) { Vector3d ct = full.GetTriCentroid(t); if (ct.x > 4 && ct.y > 4) m.RemoveTriangle(t); }
            return m;
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (string f in new[] { "plane centre", "plane corner", "plane two sources", "plane rotated jittered", "pyramid", "pyramid rotated", "dome", "ridge", "saddle", "saddle corner",
                                         "terrain", "cube", "cube two sources", "sphere", "tube", "L-shape", "sliver" })
                yield return new object[] { f };
        }

        static (DMesh3 m, int[] src) Case(string f)
        {
            switch (f) {
                case "plane centre": return (Grid(1, 21, (x, y) => 0), new[] { 220 });
                case "plane corner": return (Grid(1, 15, (x, y) => 0), new[] { 0 });
                case "plane two sources": return (Grid(1, 15, (x, y) => 0), new[] { 0, 15 * 15 - 1 });
                case "plane rotated jittered": return (Rotated(Jittered(Grid(1, 21, (x, y) => 0), 0.01, 22), 1.0), new[] { 220, 5 });
                case "pyramid": return (Grid(1, 21, (x, y) => 1 - Math.Max(Math.Abs(x), Math.Abs(y))), new[] { 0 });
                case "pyramid rotated": return (Rotated(Grid(1, 15, (x, y) => 1 - Math.Max(Math.Abs(x), Math.Abs(y))), 1.37), new[] { 0 });
                case "dome": return (Grid(1, 21, (x, y) => -0.3 * (x * x + y * y)), new[] { 220 });
                case "ridge": return (Grid(1, 21, (x, y) => 4 * Math.Max(0, 0.5 - Math.Abs(x))), new[] { 10 * 21 + 3 });
                case "saddle": return (Grid(1, 21, (x, y) => 0.3 * (x * x - y * y)), new[] { 220 });
                case "saddle corner": return (Grid(1, 21, (x, y) => 0.3 * (x * x - y * y)), new[] { 0 });
                case "terrain": return (Grid(10, 21, Terrain), new[] { 10 * 21 + 10 });
                case "cube": return (new GridBox3Generator() { EdgeVertices = 6 }.Generate().MakeDMesh(), new[] { 0 });
                case "cube two sources": return (new GridBox3Generator() { EdgeVertices = 6 }.Generate().MakeDMesh(), new[] { 0, 100 });
                case "sphere": return (new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh(), new[] { 0 });
                case "tube": return (Tube(), new[] { 3 * 24 + 2 });
                case "L-shape": return (LShape(13), new[] { 1 * 13 + 10 });
                case "sliver": return (Grid(-1, -1e-4, 1, 1e-4, 21, 5, (x, y) => 0), new[] { 10, 20 });
            }
            throw new ArgumentException(f);
        }

        static PolyhedralGeodesicDistance Run(DMesh3 m, params int[] sources)
        {
            var g = new PolyhedralGeodesicDistance(m);
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(sources));
            return g;
        }

        // ------------------------------------------------------------------ geometry of a path

        static bool OnFace(DMesh3 m, int f, SurfacePathPoint p)
        {
            Index3i t = m.GetTriangle(f);
            if (p.Vertex >= 0) return t.a == p.Vertex || t.b == p.Vertex || t.c == p.Vertex;
            if (p.Edge >= 0) { Index2i et = m.GetEdgeT(p.Edge); return et.a == f || et.b == f; }
            return p.Face == f;
        }

        // the sum of the angles at v (2 pi on a flat interior vertex)
        static double AngleSum(DMesh3 m, int v)
        {
            double s = 0; Vector3d pv = m.GetVertex(v);
            foreach (int t in m.VtxTrianglesItr(v)) {
                Index3i tri = m.GetTriangle(t); var o = new List<int>(); for (int k = 0; k < 3; ++k) if (tri[k] != v) o.Add(tri[k]);
                Vector3d a = (m.GetVertex(o[0]) - pv).Normalized, b = (m.GetVertex(o[1]) - pv).Normalized;
                s += Math.Acos(Math.Max(-1, Math.Min(1, a.Dot(b))));
            }
            return s;
        }

        static void AssertValidPath(DMesh3 m, PolyhedralGeodesicDistance g, PolyhedralGeodesicPath r, int[] sources, double ext)
        {
            Assert.Equal(PolyhedralGeodesicPathStatus.Ok, r.Status);
            Assert.Contains(r.Source, sources);
            Assert.Equal(r.Source, r.Points[0].Vertex);
            Assert.Equal(r.Points.Count - 1, r.Faces.Count);
            double sum = 0; for (int i = 0; i + 1 < r.Points.Count; ++i) sum += r.Points[i].Position.Distance(r.Points[i + 1].Position);
            Assert.Equal(sum, r.Length);                            // Length is the sum of the segments, not a copy of Distance
            Assert.True(Math.Abs(r.Length - r.Distance) <= Tol(r.Distance, ext), $"length {r.Length:R} against the distance {r.Distance:R}");
            for (int i = 0; i < r.Faces.Count; ++i) {
                Assert.True(m.IsTriangle(r.Faces[i]));
                Assert.True(OnFace(m, r.Faces[i], r.Points[i]) && OnFace(m, r.Faces[i], r.Points[i + 1]), $"segment {i} is not inside its triangle {r.Faces[i]}");
            }
            // the path bends only at vertices that are not convex: an interior vertex has an angle sum >= 2 pi (or is on the boundary)
            for (int i = 1; i + 1 < r.Points.Count; ++i) {
                int v = r.Points[i].Vertex; if (v < 0) continue;
                if (m.IsBoundaryVertex(v)) continue;
                // a vertex of a straight stretch (flat) or a saddle; a convex vertex (sum < 2 pi) is never an interior point of a shortest path
                Assert.True(AngleSum(m, v) >= 2 * Math.PI - 1e-9, $"vertex {v} has the angle sum {AngleSum(m, v)}");
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryVertex_PathLengthEqualsTheDistance_SegmentsLieInTheirTriangles_BendsOnlyAtNonConvexVertices(string f)
        {
            var (m, src) = Case(f); var g = Run(m, src); double ext = Extent(m);
            foreach (int v in m.VertexIndices()) {
                var r = g.TracePath(v);
                AssertValidPath(m, g, r, src, ext);
                Assert.Equal(v, r.Points[r.Points.Count - 1].Vertex);
                Assert.Equal(g.NearestSource[v], r.Source);
                Assert.Equal(g.Distance[v], r.Distance);
                if (Array.IndexOf(src, v) >= 0) { Assert.Equal(0, r.Length); Assert.Single(r.Points); }
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void FacePoints_LengthEqualsTheirDistance_AndTheCornerBoundHolds(string f)
        {
            var (m, src) = Case(f); var g = Run(m, src); double ext = Extent(m); var rnd = new Random(7);
            foreach (int t in m.TriangleIndices().Where(t => t % 3 == 0)) {
                double a = rnd.NextDouble(), b = rnd.NextDouble() * (1 - a); var bary = new Vector3d(a, b, 1 - a - b);
                var r = g.TracePath(t, bary);
                AssertValidPath(m, g, r, src, ext);
                Index3i tri = m.GetTriangle(t);
                Vector3d p = a * m.GetVertex(tri.a) + b * m.GetVertex(tri.b) + (1 - a - b) * m.GetVertex(tri.c);
                Assert.Equal(t, r.Points[r.Points.Count - 1].Face);
                Assert.True((r.Points[r.Points.Count - 1].Position - p).Length <= 1e-12 * (1 + ext));
                // never longer than going to a corner and straight on, never shorter than the straight 3D line to the nearest source
                for (int k = 0; k < 3; ++k) Assert.True(r.Distance <= g.Distance[tri[k]] + p.Distance(m.GetVertex(tri[k])) + Tol(r.Distance, ext));
                double straight = src.Min(s => p.Distance(m.GetVertex(s)));
                Assert.True(r.Distance >= straight - Tol(straight, ext));
            }
        }

        [Fact]
        public void Plane_TheLengthIsTheStraightDistance_ToVerticesAndFacePoints()
        {
            var m = Grid(1, 21, (x, y) => 0); int src = 3 * 21 + 5; var g = Run(m, src); double ext = Extent(m); Vector3d s = m.GetVertex(src);
            foreach (int v in m.VertexIndices()) Assert.True(Math.Abs(g.TracePath(v).Length - s.Distance(m.GetVertex(v))) <= Tol(s.Distance(m.GetVertex(v)), ext));
            var rnd = new Random(3);
            foreach (int t in m.TriangleIndices().Where(t => t % 4 == 1)) {
                double a = rnd.NextDouble(), b = rnd.NextDouble() * (1 - a); Index3i tri = m.GetTriangle(t);
                Vector3d p = a * m.GetVertex(tri.a) + b * m.GetVertex(tri.b) + (1 - a - b) * m.GetVertex(tri.c);
                var r = g.TracePath(t, new Vector3d(a, b, 1 - a - b));
                Assert.True(Math.Abs(r.Length - s.Distance(p)) <= Tol(s.Distance(p), ext) && Math.Abs(r.Distance - s.Distance(p)) <= Tol(s.Distance(p), ext));
            }
        }

        [Fact]
        public void LongPaths_On101Plane_LengthIsWithin1e14OfTheDistance_AndTheStraightLine()
        {
            // paths of about 150 crossings: the error of the unfolding grows with the crossings (GEODESICS.md A.8): the class docs state 1e-14 relative
            var m = Grid(1, 101, (x, y) => 0); int src = 50 * 101 + 50; var g = Run(m, src); Vector3d s = m.GetVertex(src); int n = 0;
            foreach (int v in m.VertexIndices().Where(v => v % 53 == 7)) {
                var r = g.TracePath(v); double d = s.Distance(m.GetVertex(v));
                Assert.Equal(PolyhedralGeodesicPathStatus.Ok, r.Status);
                Assert.True(Math.Abs(r.Length - r.Distance) <= 1e-14 * r.Distance + 1e-15 * 2, $"vertex {v}: {r.Length:R} against {r.Distance:R}");
                Assert.True(Math.Abs(r.Length - d) <= 1e-14 * d + 1e-15 * 2);
                ++n;
            }
            Assert.True(n > 150);
        }

        [Fact]
        public void Cube_CornerToOppositeCorner_IsSqrtFiveSides_AndAvoidsTheCorners()
        {
            var m = new GridBox3Generator() { EdgeVertices = 6 }.Generate().MakeDMesh();
            AxisAlignedBox3d box = m.CachedBounds; double side = box.Width;
            int s = m.VertexIndices().OrderBy(v => m.GetVertex(v).Distance(box.Min)).First(), t = m.VertexIndices().OrderBy(v => m.GetVertex(v).Distance(box.Max)).First();
            var g = Run(m, s); var r = g.TracePath(t);
            Assert.Equal(PolyhedralGeodesicPathStatus.Ok, r.Status);
            Assert.True(Math.Abs(r.Length - Math.Sqrt(5) * side) <= 1e-12, $"{r.Length}");
            var corners = Enumerable.Range(0, 8).Select(k => m.VertexIndices().OrderBy(v => m.GetVertex(v).Distance(box.Corner(k))).First()).ToList();
            foreach (var p in r.Points.Skip(1).Take(r.Points.Count - 2)) Assert.DoesNotContain(p.Vertex, corners);
        }

        [Fact]
        public void SaddleSourceChain_PathGoesThroughPseudoSources_AndStillEqualsTheDistance()
        {
            // on the saddle every vertex is a pseudo-source: some shortest paths bend at vertices
            var m = Grid(1, 31, (x, y) => 0.3 * (x * x - y * y)); var g = Run(m, 0); double ext = Extent(m); int bent = 0;
            foreach (int v in m.VertexIndices()) {
                var r = g.TracePath(v); Assert.Equal(PolyhedralGeodesicPathStatus.Ok, r.Status);
                Assert.True(Math.Abs(r.Length - r.Distance) <= Tol(r.Distance, ext));
                bent += r.Points.Skip(1).Take(Math.Max(0, r.Points.Count - 2)).Count(p => p.Vertex >= 0 && !(m.GetVertex(p.Vertex).x == m.GetVertex(0).x));
            }
            Assert.True(bent > 100, $"{bent} interior vertices on the paths");
        }

        // ------------------------------------------------------------------ face points

        [Fact]
        public void FacePoint_AtACorner_IsTheVertexQuery_Exactly()
        {
            var m = Grid(10, 21, Terrain); var g = Run(m, 10 * 21 + 10);
            foreach (int t in m.TriangleIndices().Where(t => t % 11 == 0)) {
                Index3i tri = m.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    var bary = new Vector3d(k == 0 ? 1 : 0, k == 1 ? 1 : 0, k == 2 ? 1 : 0);
                    var a = g.TracePath(t, bary); var b = g.TracePath(tri[k]);
                    Assert.Equal(b.Status, a.Status); Assert.Equal(b.Distance, a.Distance); Assert.Equal(b.Length, a.Length); Assert.Equal(b.Source, a.Source);
                    Assert.Equal(b.Points.Select(p => p.Position).ToArray(), a.Points.Select(p => p.Position).ToArray());
                }
            }
        }

        [Fact]
        public void FacePoint_OnASharedEdge_AgreesWithTheNeighbouringTriangle()
        {
            foreach (string f in new[] { "terrain", "saddle", "cube", "plane rotated jittered" }) {
                var (m, src) = Case(f); var g = Run(m, src); double ext = Extent(m); int checkedEdges = 0;
                foreach (int e in m.EdgeIndices().Where(e => e % 5 == 2)) {
                    Index2i et = m.GetEdgeT(e); if (et.b < 0) continue;
                    foreach (double w in new[] { 0.5, 0.2 }) {
                        var res = new PolyhedralGeodesicPath[2];
                        for (int k = 0; k < 2; ++k) {
                            int t = k == 0 ? et.a : et.b; Index3i tri = m.GetTriangle(t); Index2i ev = m.GetEdgeV(e);
                            var bary = new double[3]; for (int c = 0; c < 3; ++c) bary[c] = tri[c] == ev.a ? 1 - w : (tri[c] == ev.b ? w : 0);
                            res[k] = g.TracePath(t, new Vector3d(bary[0], bary[1], bary[2]));
                            Assert.Equal(PolyhedralGeodesicPathStatus.Ok, res[k].Status);
                        }
                        Assert.True(Math.Abs(res[0].Distance - res[1].Distance) <= 4 * Tol(res[0].Distance, ext), $"{f} edge {e}: {res[0].Distance:R} against {res[1].Distance:R}");
                        Assert.True((res[0].Points[res[0].Points.Count - 1].Position - res[1].Points[res[1].Points.Count - 1].Position).Length <= 1e-12 * (1 + ext));
                        ++checkedEdges;
                    }
                }
                Assert.True(checkedEdges > 10);
            }
        }

        // ------------------------------------------------------------------ against FlipOut

        [Theory]
        [InlineData("plane corner")]
        [InlineData("saddle corner")]
        [InlineData("terrain")]
        [InlineData("cube")]
        [InlineData("pyramid")]
        public void FlipOut_IsNeverShorterThanTheExactPath(string f)
        {
            var (m, src) = Case(f); var g = Run(m, src[0]); double ext = Extent(m); int n = 0;
            foreach (int v in m.VertexIndices().Where(v => v % 17 == 5 && v != src[0])) {
                var exact = g.TracePath(v); var fo = new MeshGeodesicPath(m); fo.Compute(src[0], v);
                Assert.True(fo.Length >= exact.Length - Tol(exact.Length, ext) - 1e-9 * ext * 0, $"{f} vertex {v}: FlipOut {fo.Length:R} against the exact {exact.Length:R}");
                ++n;
            }
            Assert.True(n > 5);
        }

        [Fact]
        public void Plane_FlipOutAgreesWithTheExactPath_AndAPureEdgePathIsAFixedPointOfShorten()
        {
            var m = Grid(1, 15, (x, y) => 0); int src = 7 * 15 + 1; var g = Run(m, src); double ext = Extent(m);
            foreach (int v in m.VertexIndices().Where(v => v % 7 == 3 && v != src)) {
                var exact = g.TracePath(v); var fo = new MeshGeodesicPath(m); fo.Compute(src, v);
                Assert.True(Math.Abs(fo.Length - exact.Length) <= 1e-9 * (1 + exact.Length), $"vertex {v}");
            }
            // along the row of the source the exact path runs along mesh edges: Shorten leaves it unchanged
            int target = 7 * 15 + 12; var rowPath = g.TracePath(target);
            Assert.True(rowPath.Points.All(p => p.Vertex >= 0));
            var edgePath = rowPath.Points.Select(p => p.Vertex).ToList();
            var sh = new MeshGeodesicPath(m); sh.Shorten(edgePath);
            Assert.True(Math.Abs(sh.Length - rowPath.Length) <= Tol(rowPath.Length, ext) + 1e-12);
        }

        // ------------------------------------------------------------------ invariance, reuse, determinism

        static bool SameStructure(PolyhedralGeodesicPath a, PolyhedralGeodesicPath b)
            => a.Status == b.Status && a.Source == b.Source && a.Points.Count == b.Points.Count && a.Faces.SequenceEqual(b.Faces)
               && a.Points.Zip(b.Points, (p, q) => p.Vertex == q.Vertex && p.Edge == q.Edge && p.Face == q.Face).All(x => x);

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Paths_UnderSimilarities_ExactScalesGiveTheSameBits(string name)
        {
            Similarity sim = Similarity.ByName(name);
            var (m0, src) = Case("saddle corner"); var g0 = Run(m0, src); var m1 = Invariance.Transformed(m0, sim); var g1 = Run(m1, src);
            foreach (int v in m0.VertexIndices().Where(v => v % 3 == 0)) {
                var a = g0.TracePath(v); var b = g1.TracePath(v);
                Assert.Equal(PolyhedralGeodesicPathStatus.Ok, b.Status);
                if (sim.IsExact) {
                    Assert.True(SameStructure(a, b), $"{name} vertex {v}");
                    Assert.Equal(a.Length * sim.Scale, b.Length); Assert.Equal(a.Distance * sim.Scale, b.Distance);
                    for (int i = 0; i < a.Points.Count; ++i) Assert.Equal(a.Points[i].T, b.Points[i].T);
                } else {
                    Assert.True(Math.Abs(b.Length / sim.Scale - a.Length) <= 1e-8 * (1 + a.Length) + sim.Resolution(2) * 1e3, $"{name} vertex {v}");
                    Assert.True(Math.Abs(b.Length - b.Distance) <= 1e-8 * (1 + b.Distance));
                }
            }
            // a face point
            var fa = g0.TracePath(40, new Vector3d(0.2, 0.3, 0.5)); var fb = g1.TracePath(40, new Vector3d(0.2, 0.3, 0.5));
            if (sim.IsExact) { Assert.True(SameStructure(fa, fb)); Assert.Equal(fa.Distance * sim.Scale, fb.Distance); Assert.Equal(fa.Length * sim.Scale, fb.Length); }
        }

        [Fact]
        public void Reuse_OneInstance_SamePathsBitForBit_AndAnotherSource_AndFreshInstance()
        {
            var (m, src) = Case("terrain"); var g = new PolyhedralGeodesicDistance(m);
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(src));
            var first = m.VertexIndices().Select(v => g.TracePath(v)).ToList();
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(new[] { 5 }));
            Assert.Equal(5, g.TracePath(100).Source);
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(src));
            var fresh = Run(m, src);
            int i = 0;
            foreach (int v in m.VertexIndices()) {
                var a = first[i++]; var b = g.TracePath(v); var c = fresh.TracePath(v);
                Assert.True(SameStructure(a, b) && SameStructure(a, c));
                Assert.Equal(a.Length, b.Length); Assert.Equal(a.Length, c.Length);
                Assert.Equal(a.Points.Select(p => p.Position).ToArray(), b.Points.Select(p => p.Position).ToArray());
            }
        }

        [Fact]
        public void Deterministic_RepeatedTracesGiveTheSameBits()
        {
            var (m, src) = Case("saddle"); var g = Run(m, src);
            foreach (int v in m.VertexIndices().Where(v => v % 4 == 1)) {
                var a = g.TracePath(v); var b = g.TracePath(v);
                Assert.Equal(a.Length, b.Length); Assert.True(SameStructure(a, b));
                Assert.Equal(a.Points.Select(p => p.Position).ToArray(), b.Points.Select(p => p.Position).ToArray());
            }
        }

        // ------------------------------------------------------------------ statuses, the guard, ties

        [Fact]
        public void Statuses_NotComputed_Invalid_Unreached()
        {
            var m = Grid(1, 11, (x, y) => 0); var g = new PolyhedralGeodesicDistance(m);
            Assert.Equal(PolyhedralGeodesicPathStatus.NotComputed, g.TracePath(3).Status);
            // WindowLimit and Cancelled leave no path
            var lim = new PolyhedralGeodesicDistance(m) { MaxWindows = 10 }; Assert.Equal(PolyhedralGeodesicStatus.WindowLimit, lim.Compute(new[] { 60 }));
            var rl = lim.TracePath(3); Assert.Equal(PolyhedralGeodesicPathStatus.NotComputed, rl.Status); Assert.Empty(rl.Points);
            var big = Grid(1, 41, (x, y) => 0); var can = new PolyhedralGeodesicDistance(big) { CancelRequested = () => true }; Assert.Equal(PolyhedralGeodesicStatus.Cancelled, can.Compute(new[] { 60 }));
            Assert.Equal(PolyhedralGeodesicPathStatus.NotComputed, can.TracePath(3).Status);
            Assert.Equal(PolyhedralGeodesicStatus.NoSource, new PolyhedralGeodesicDistance(m).Compute(new int[0]));
            Assert.Equal(PolyhedralGeodesicPathStatus.Ok, Run(m, 60).TracePath(3).Status);
            var ok = Run(m, 60);
            Assert.Equal(PolyhedralGeodesicPathStatus.InvalidTarget, ok.TracePath(-1).Status);
            Assert.Equal(PolyhedralGeodesicPathStatus.InvalidTarget, ok.TracePath(m.MaxVertexID).Status);
            Assert.Equal(PolyhedralGeodesicPathStatus.InvalidTarget, ok.TracePath(-1, new Vector3d(1, 0, 0)).Status);
            Assert.Equal(PolyhedralGeodesicPathStatus.InvalidTarget, ok.TracePath(m.MaxTriangleID, new Vector3d(1, 0, 0)).Status);
            Assert.Equal(PolyhedralGeodesicPathStatus.InvalidTarget, ok.TracePath(0, new Vector3d(-0.1, 0.6, 0.5)).Status);
            Assert.Equal(PolyhedralGeodesicPathStatus.InvalidTarget, ok.TracePath(0, new Vector3d(0.5, 0.6, 0.5)).Status);
            Assert.Equal(PolyhedralGeodesicPathStatus.InvalidTarget, ok.TracePath(0, new Vector3d(double.NaN, 0.5, 0.5)).Status);
            // MaxDistance cuts off the far vertices: no distance, no path
            var cut = new PolyhedralGeodesicDistance(m) { MaxDistance = 0.3 }; Assert.Equal(PolyhedralGeodesicStatus.Ok, cut.Compute(new[] { 60 }));
            Assert.Equal(PolyhedralGeodesicPathStatus.Unreached, cut.TracePath(0).Status);
            Assert.Equal(PolyhedralGeodesicPathStatus.Ok, cut.TracePath(61).Status);
            Assert.Equal(PolyhedralGeodesicPathStatus.Unreached, cut.TracePath(0, new Vector3d(0.3, 0.3, 0.4)).Status);
            // a second component
            var two = new DMesh3(m); int off = two.MaxVertexID;
            var ids = new int[4]; ids[0] = two.AppendVertex(new Vector3d(5, 5, 0)); ids[1] = two.AppendVertex(new Vector3d(6, 5, 0)); ids[2] = two.AppendVertex(new Vector3d(5, 6, 0)); ids[3] = two.AppendVertex(new Vector3d(6, 6, 0));
            two.AppendTriangle(ids[0], ids[1], ids[2]); two.AppendTriangle(ids[1], ids[3], ids[2]);
            var g2 = Run(two, 60); Assert.Equal(PolyhedralGeodesicPathStatus.Unreached, g2.TracePath(ids[0]).Status);
        }

        [Fact]
        public void TraceFailed_WhenTheStepBudgetIsExhausted_PointsAreNotAPath_LengthStaysZero()
        {
            var (m, src) = Case("plane corner"); var g = Run(m, src); int far = 14 * 15 + 14;
            var ok = g.TracePath(far); Assert.Equal(PolyhedralGeodesicPathStatus.Ok, ok.Status);
            g.TraceStepCap = 1;
            var r = g.TracePath(far);
            Assert.Equal(PolyhedralGeodesicPathStatus.TraceFailed, r.Status);
            Assert.Equal(0, r.Length); Assert.Equal(g.Distance[far], r.Distance);
            Assert.True(r.Points.Count < ok.Points.Count);
            Assert.Equal(far, r.Points[0].Vertex);                  // target to source order: the first point is the target, not a source
            g.TraceStepCap = 0;
            Assert.Equal(PolyhedralGeodesicPathStatus.Ok, g.TracePath(far).Status);
            // the walk along edges (hops from pseudo-sources, here flat vertices of a row) is bounded by the same budget
            g.TraceStepCap = 3; int row = 14; Assert.Equal(PolyhedralGeodesicPathStatus.TraceFailed, g.TracePath(row).Status);
            g.TraceStepCap = 0; Assert.Equal(PolyhedralGeodesicPathStatus.Ok, g.TracePath(row).Status);
            g.TraceStepCap = 1;
            // a face point, too
            g.TraceStepCap = 1; Assert.Equal(PolyhedralGeodesicPathStatus.TraceFailed, g.TracePath(m.TriangleIndices().Last(), new Vector3d(0.3, 0.3, 0.4)).Status);
        }

        [Fact]
        public void TwoSourcesAtEqualDistance_TheLowestIdWins_RegardlessOfTheOrderGiven()
        {
            var m = Grid(1, 11, (x, y) => 0); int a = 5 * 11 + 1, b = 5 * 11 + 9, mid = 5 * 11 + 5;     // the middle vertex is exactly between them
            var g1 = Run(m, a, b); var g2 = Run(m, b, a);
            Assert.Equal(g1.Distance[mid], g2.Distance[mid]);
            foreach (var g in new[] { g1, g2, g1 }) {
                var r = g.TracePath(mid);
                Assert.Equal(PolyhedralGeodesicPathStatus.Ok, r.Status); Assert.Equal(a, r.Source); Assert.Equal(a, r.Points[0].Vertex);
                Assert.True(Math.Abs(r.Length - 0.8) <= 1e-14);
            }
            Assert.Equal(g1.TracePath(mid).Points.Select(p => p.Position).ToArray(), g2.TracePath(mid).Points.Select(p => p.Position).ToArray());
        }
    }
}
