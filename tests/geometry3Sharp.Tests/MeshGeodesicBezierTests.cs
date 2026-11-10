using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Geodesic Bezier curves (MeshGeodesicBezier). Developable meshes (plane, polygonal cylinder) have the curve in
    /// closed form: the ordinary Bezier curve of the unfolded control points. The sphere has the spherical de Casteljau
    /// construction (slerp) as a reference up to the discretization error.
    /// </summary>
    public class MeshGeodesicBezierTests
    {
        static DMesh3 JitteredPlane(int nx, int ny, double h, int seed, out int[,] ids)
        {
            var rand = new Random(seed);
            var m = new DMesh3();
            ids = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i) {
                    bool inner = i > 0 && j > 0 && i < nx && j < ny;
                    double dx = inner ? 0.3 * h * (rand.NextDouble() - 0.5) : 0;
                    double dy = inner ? 0.3 * h * (rand.NextDouble() - 0.5) : 0;
                    ids[i, j] = m.AppendVertex(new Vector3d(i * h + dx, j * h + dy, 0));
                }
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    int a = ids[i, j], b = ids[i + 1, j], c = ids[i + 1, j + 1], d = ids[i, j + 1];
                    if (rand.Next(2) == 0) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
                    else { m.AppendTriangle(a, b, d); m.AppendTriangle(b, c, d); }
                }
            return m;
        }

        static DMesh3 Cylinder(int n, int rings, double dz, out int[,] ids)
        {
            var m = new DMesh3();
            ids = new int[n, rings + 1];
            for (int j = 0; j <= rings; ++j)
                for (int i = 0; i < n; ++i) {
                    double a = 2 * Math.PI * i / n;
                    ids[i, j] = m.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), j * dz));
                }
            for (int j = 0; j < rings; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = ids[i, j], b = ids[(i + 1) % n, j], c = ids[(i + 1) % n, j + 1], d = ids[i, j + 1];
                    m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
                }
            return m;
        }

        static DMesh3 BumpySphere()
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid);
                m.SetVertex(vid, p * (1 + 0.2 * Math.Sin(3 * p.x + 1) * Math.Cos(2 * p.y - 0.5) + 0.05 * p.z));
            }
            return m;
        }

        static int Nearest(DMesh3 m, Vector3d p) => m.VertexIndices().OrderBy(v => m.GetVertex(v).Distance(p)).First();

        // ordinary de Casteljau
        static Vector2d Bezier(IList<Vector2d> c, double t)
        {
            var cur = c.ToArray();
            for (int level = cur.Length - 1; level >= 1; --level)
                for (int i = 0; i < level; ++i) cur[i] = (1 - t) * cur[i] + t * cur[i + 1];
            return cur[0];
        }

        static double PolylineLength(IList<Vector3d> p)
        {
            double l = 0;
            for (int k = 1; k < p.Count; ++k) l += p[k].Distance(p[k - 1]);
            return l;
        }

        static void AssertValid(MeshGeodesicBezier b, DMesh3 mesh, IList<int> control, int segments)
        {
            Assert.Equal(GeodesicPathStatus.Success, b.Status);
            Assert.Equal(segments + 1, b.Points.Count);
            Assert.Equal(segments + 1, b.Parameters.Count);
            Assert.True(b.Points.All(p => p.IsFinite) && b.Path.All(p => p.IsFinite));
            Assert.Equal(mesh.GetVertex(control[0]), b.Points[0]);
            Assert.Equal(mesh.GetVertex(control[control.Count - 1]), b.Points[segments]);
            Assert.Equal(b.Points[0], b.Path[0]);
            Assert.Equal(b.Points[segments], b.Path[b.Path.Count - 1]);
            Assert.True(Math.Abs(PolylineLength(b.Path) - b.Length) <= 1e-7 * b.Length, "Length is the length of Path");
        }

        // ---------------------------------------------------------------- closed forms

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void Plane_IsTheOrdinaryBezierCurve(int count)
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(14, 12, 0.1, 5, out ids);
            var cells = new (int, int)[] { (1, 2), (4, 10), (9, 9), (12, 3), (6, 1) }.Take(count).ToArray();
            int[] control = cells.Select(c => ids[c.Item1, c.Item2]).ToArray();
            var b = new MeshGeodesicBezier(mesh);
            Assert.Equal(GeodesicPathStatus.Success, b.Compute(control, 16));
            AssertValid(b, mesh, control, 16);
            var c2 = control.Select(v => new Vector2d(mesh.GetVertex(v).x, mesh.GetVertex(v).y)).ToArray();
            for (int i = 0; i <= 16; ++i) {
                Vector2d e = Bezier(c2, b.Parameters[i]);
                Assert.True(Math.Abs(b.Points[i].x - e.x) < 1e-11 && Math.Abs(b.Points[i].y - e.y) < 1e-11 && Math.Abs(b.Points[i].z) < 1e-12,
                    $"count {count}, t = {b.Parameters[i]}: {b.Points[i]} vs {e}");
            }
            // the geodesics between the points are straight lines: Path is the polyline through Points
            Assert.True(Math.Abs(b.Length - PolylineLength(b.Points)) < 1e-11);
        }

        // along the boundary the path runs on boundary halfedges in both directions (a reversed one is split from its head)
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Plane_AlongTheBoundary_InBothDirections(bool reverse)
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(14, 12, 0.1, 5, out ids);
            int[] control = { ids[1, 0], ids[6, 0], ids[13, 0] };
            if (reverse) Array.Reverse(control);
            var c2 = control.Select(v => new Vector2d(mesh.GetVertex(v).x, mesh.GetVertex(v).y)).ToArray();
            var b = new MeshGeodesicBezier(mesh);
            var t = new[] { 0.0, 0.13, 0.37, 0.5, 0.81, 1.0 };
            Assert.Equal(GeodesicPathStatus.Success, b.Compute(control, t));
            for (int i = 0; i < t.Length; ++i) {
                Vector2d e = Bezier(c2, t[i]);
                Assert.True(Math.Abs(b.Points[i].x - e.x) < 1e-11 && Math.Abs(b.Points[i].y) < 1e-11, $"t = {t[i]}: {b.Points[i]} vs {e}");
            }
        }

        [Fact]
        public void Cylinder_IsTheBezierCurveOfTheUnfoldedControlPoints()
        {
            int n = 40, rings = 20;
            double dz = 0.08, chord = 2 * Math.Sin(Math.PI / n);
            int[,] ids;
            DMesh3 mesh = Cylinder(n, rings, dz, out ids);
            var cells = new (int, int)[] { (3, 2), (9, 15), (17, 4), (24, 12) };
            int[] control = cells.Select(c => ids[c.Item1, c.Item2]).ToArray();
            var unfolded = cells.Select(c => new Vector2d(c.Item1 * chord, c.Item2 * dz)).ToArray();
            var b = new MeshGeodesicBezier(mesh);
            Assert.Equal(GeodesicPathStatus.Success, b.Compute(control, 24));
            AssertValid(b, mesh, control, 24);
            // each geodesic is a straight line in the unfolding: the length of Path is the length of the polyline through
            // the unfolded curve points
            double expected = 0;
            Vector2d prev = Bezier(unfolded, 0);
            for (int i = 1; i <= 24; ++i) {
                Vector2d p = Bezier(unfolded, b.Parameters[i]);
                expected += prev.Distance(p);
                prev = p;
            }
            Assert.True(Math.Abs(b.Length - expected) < 1e-7 * expected, $"{b.Length} vs {expected}");
            // the points are on the cylinder surface (inside the radius of the polygon, at least the apothem)
            double apothem = Math.Cos(Math.PI / n);
            foreach (Vector3d p in b.Points) {
                double r = Math.Sqrt(p.x * p.x + p.y * p.y);
                Assert.InRange(r, apothem - 1e-12, 1 + 1e-12);
            }
        }

        static Vector3d Slerp(Vector3d a, Vector3d b, double t)
        {
            double w = Math.Acos(MathUtil.Clamp(a.Dot(b), -1, 1));
            if (w < 1e-12) return a;
            return (Math.Sin((1 - t) * w) * a + Math.Sin(t * w) * b) / Math.Sin(w);
        }

        [Fact]
        public void Sphere_IsCloseToTheSphericalBezierCurve()
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 24 }.Generate().MakeDMesh();
            int[] control = { Nearest(mesh, new Vector3d(1, 0.1, 0)), Nearest(mesh, new Vector3d(0.5, 1, 0.8)),
                              Nearest(mesh, new Vector3d(-0.4, 1, -0.2)), Nearest(mesh, new Vector3d(-1, -0.2, 0.3)) };
            var b = new MeshGeodesicBezier(mesh);
            Assert.Equal(GeodesicPathStatus.Success, b.Compute(control, 12));
            AssertValid(b, mesh, control, 12);
            double h = mesh.EdgeIndices().Average(e => { Index2i ev = mesh.GetEdgeV(e); return mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)); });
            Vector3d[] c = control.Select(v => mesh.GetVertex(v).Normalized).ToArray();
            double worst = 0;
            for (int i = 0; i <= 12; ++i) {
                double t = b.Parameters[i];
                Vector3d[] cur = (Vector3d[])c.Clone();
                for (int level = cur.Length - 1; level >= 1; --level)
                    for (int k = 0; k < level; ++k) cur[k] = Slerp(cur[k], cur[k + 1], t).Normalized;
                worst = Math.Max(worst, b.Points[i].Normalized.Distance(cur[0]));
            }
            Assert.True(worst < 0.5 * h, $"worst deviation {worst} vs edge length {h}");
        }

        // ---------------------------------------------------------------- properties

        [Fact]
        public void Reversed_GivesTheSameCurve_OnThePlane()
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(14, 12, 0.1, 5, out ids);
            int[] control = { ids[1, 2], ids[4, 10], ids[9, 9], ids[12, 3] };
            var f = new MeshGeodesicBezier(mesh);
            var r = new MeshGeodesicBezier(mesh);
            Assert.Equal(GeodesicPathStatus.Success, f.Compute(control, 8));
            Assert.Equal(GeodesicPathStatus.Success, r.Compute(control.Reverse().ToArray(), 8));
            for (int i = 0; i <= 8; ++i)
                Assert.True(f.Points[i].Distance(r.Points[8 - i]) < 1e-11, $"i = {i}");
        }

        // on a curved mesh a discrete geodesic past a saddle vertex is not unique (both sides are locally shortest), and
        // the start of the search decides: the curves agree to a small part of an edge length, not to rounding
        [Fact]
        public void Reversed_GivesTheSameCurve_OnABumpySphereUpToAFractionOfAnEdge()
        {
            DMesh3 mesh = BumpySphere();
            int[] control = { Nearest(mesh, new Vector3d(1, 0, 0)), Nearest(mesh, new Vector3d(0.3, 1, 0.4)),
                              Nearest(mesh, new Vector3d(-0.6, 0.5, 0.9)), Nearest(mesh, new Vector3d(-0.5, 0.7, -0.6)) };
            double h = mesh.EdgeIndices().Average(e => { Index2i ev = mesh.GetEdgeV(e); return mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)); });
            var f = new MeshGeodesicBezier(mesh);
            var r = new MeshGeodesicBezier(mesh);
            Assert.Equal(GeodesicPathStatus.Success, f.Compute(control, 8));
            Assert.Equal(GeodesicPathStatus.Success, r.Compute(control.Reverse().ToArray(), 8));
            for (int i = 0; i <= 8; ++i)
                Assert.True(f.Points[i].Distance(r.Points[8 - i]) < 0.02 * h, $"i = {i}: {f.Points[i]} vs {r.Points[8 - i]}");
        }

        [Fact]
        public void Degree1_IsTheGeodesicAtTheFraction()
        {
            DMesh3 mesh = BumpySphere();
            int s = Nearest(mesh, new Vector3d(1, 0, 0)), t = Nearest(mesh, new Vector3d(-0.5, 0.7, -0.6));
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Success, g.Compute(s, t));
            var b = new MeshGeodesicBezier(mesh);
            Assert.Equal(GeodesicPathStatus.Success, b.Compute(new[] { s, t }, new[] { 0.0, 0.25, 0.8, 1.0 }));
            Assert.True(Math.Abs(b.Length - g.Length) < 1e-9 * g.Length, "Path is the geodesic");
            double[] f = { 0, 0.25, 0.8, 1 };
            for (int i = 0; i < f.Length; ++i) {
                double want = f[i] * g.Length, walked = 0;
                // arc length of the geodesic's polyline up to the point
                int k = 0;
                while (k + 1 < g.Path.Count && walked + g.Path[k].Distance(g.Path[k + 1]) < want - 1e-12) { walked += g.Path[k].Distance(g.Path[k + 1]); k++; }
                double along = k + 1 < g.Path.Count ? (want - walked) / g.Path[k].Distance(g.Path[k + 1]) : 0;
                Vector3d expect = k + 1 < g.Path.Count ? g.Path[k] + along * (g.Path[k + 1] - g.Path[k]) : g.Path[k];
                Assert.True(b.Points[i].Distance(expect) < 1e-9, $"f = {f[i]}: {b.Points[i]} vs {expect}");
            }
        }

        [Fact]
        public void LShape_CurveStaysInsideTheDomain()
        {
            var m = new DMesh3();
            var map = new Dictionary<(int, int), int>();
            Func<int, int, int> v = (i, j) => {
                int id;
                if (map.TryGetValue((i, j), out id) == false) map[(i, j)] = id = m.AppendVertex(new Vector3d(0.1 * i, 0.1 * j, 0));
                return id;
            };
            for (int j = 0; j < 20; ++j)
                for (int i = 0; i < 20; ++i) {
                    if (i >= 10 && j >= 10) continue;
                    int a = v(i, j), b = v(i + 1, j), c = v(i + 1, j + 1), d = v(i, j + 1);
                    if ((i + j) % 2 == 0) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
                    else { m.AppendTriangle(a, b, d); m.AppendTriangle(b, c, d); }
                }
            // the straight Bezier segments would cut the reflex corner at (1, 1); the geodesics bend around it
            int[] control = { map[(20, 2)], map[(15, 6)], map[(4, 16)], map[(2, 20)] };
            var bz = new MeshGeodesicBezier(m);
            Assert.Equal(GeodesicPathStatus.Success, bz.Compute(control, 20));
            AssertValid(bz, m, control, 20);
            foreach (Vector3d p in bz.Path)
                Assert.True(p.x <= 1 + 1e-9 || p.y <= 1 + 1e-9, $"{p} is outside the L");
            Assert.True(bz.Path.Any(p => p.Distance(new Vector3d(1, 1, 0)) < 1e-9), "bends at the reflex corner");
        }

        [Fact]
        public void Sphere_PointsAndPathLieOnTheSurface()
        {
            DMesh3 mesh = BumpySphere();
            int[] control = { Nearest(mesh, new Vector3d(1, 0, 0)), Nearest(mesh, new Vector3d(0.3, 1, 0.4)),
                              Nearest(mesh, new Vector3d(-0.6, 0.5, 0.9)), Nearest(mesh, new Vector3d(-0.5, 0.7, -0.6)) };
            var b = new MeshGeodesicBezier(mesh);
            Assert.Equal(GeodesicPathStatus.Success, b.Compute(control, 10));
            AssertValid(b, mesh, control, 10);
            var tree = new DMeshAABBTree3(mesh, true);
            foreach (Vector3d p in b.Path) {
                double d2;
                tree.FindNearestTriangle(p, out d2);
                Assert.True(d2 < 1e-16, $"{p} is {Math.Sqrt(d2)} off the surface");
            }
            Assert.True(b.Triangulation.InputVertexCount < b.Triangulation.VertexCount, "points were inserted");
            string err;
            Assert.True(b.Triangulation.CheckConsistency(out err), err);
        }

        // ---------------------------------------------------------------- invariance, reuse, input

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void BumpySphere_InvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 mesh = BumpySphere();
            int[] control = { Nearest(mesh, new Vector3d(1, 0, 0)), Nearest(mesh, new Vector3d(0.3, 1, 0.4)),
                              Nearest(mesh, new Vector3d(-0.6, 0.5, 0.9)), Nearest(mesh, new Vector3d(-0.5, 0.7, -0.6)) };
            var b0 = new MeshGeodesicBezier(mesh);
            Assert.Equal(GeodesicPathStatus.Success, b0.Compute(control, 8));
            DMesh3 moved = Invariance.Transformed(mesh, sim);
            var b = new MeshGeodesicBezier(moved);
            Assert.Equal(GeodesicPathStatus.Success, b.Compute(control, 8));
            Assert.Equal(b0.Path.Count, b.Path.Count);
            if (sim.IsExact) {
                Assert.Equal(b0.Length, b.Length / sim.Scale);
                for (int k = 0; k < b0.Points.Count; ++k) Assert.Equal(b0.Points[k], b.Points[k] / sim.Scale);
                for (int k = 0; k < b0.Path.Count; ++k) Assert.Equal(b0.Path[k], b.Path[k] / sim.Scale);
            } else {
                double tol = 1e-8 + 256 * sim.Resolution(2.5);
                Assert.True(Math.Abs(b0.Length - b.Length / sim.Scale) < tol, $"{b0.Length} vs {b.Length / sim.Scale}");
                for (int k = 0; k < b0.Points.Count; ++k)
                    Assert.True(b0.Points[k].Distance(sim.Inverse(b.Points[k])) < tol, $"point {k}");
            }
        }

        [Fact]
        public void Reuse_SameResultTwice_AndMeshUnchanged()
        {
            DMesh3 mesh = BumpySphere();
            var before = mesh.VertexIndices().Select(v => mesh.GetVertex(v)).ToList();
            int tcount = mesh.TriangleCount;
            int[] c1 = { Nearest(mesh, new Vector3d(1, 0, 0)), Nearest(mesh, new Vector3d(0.3, 1, 0.4)), Nearest(mesh, new Vector3d(-0.5, 0.7, -0.6)) };
            int[] c2 = { Nearest(mesh, new Vector3d(0, 0, 1)), Nearest(mesh, new Vector3d(1, 1, -0.4)), Nearest(mesh, new Vector3d(-1, 0.2, -0.5)) };
            var b = new MeshGeodesicBezier(mesh);
            b.Compute(c1, 6);
            var points = b.Points.ToList(); var path = b.Path.ToList(); double len = b.Length;
            b.Compute(c2, 4);
            Assert.Equal(5, b.Points.Count);
            Assert.NotEqual(len, b.Length);
            b.Compute(c1, 6);
            Assert.Equal(points, b.Points);
            Assert.Equal(path, b.Path);
            Assert.Equal(len, b.Length);
            Assert.Equal(tcount, mesh.TriangleCount);
            Assert.Equal(before, mesh.VertexIndices().Select(v => mesh.GetVertex(v)).ToList());
        }

        [Fact]
        public void InvalidInputs_GiveStatusCodesAndEmptyResults()
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(4, 4, 0.1, 1, out ids);
            var b = new MeshGeodesicBezier(mesh);
            int a = ids[0, 0], c = ids[4, 4];
            Assert.Equal(GeodesicPathStatus.Success, b.Compute(new[] { a, c }, 4));
            Assert.Equal(GeodesicPathStatus.InvalidInput, b.Compute(new[] { a }, 4));
            Assert.Equal(GeodesicPathStatus.InvalidInput, b.Compute(new[] { a, c }, 0));
            Assert.Equal(GeodesicPathStatus.InvalidInput, b.Compute(new[] { a, 9999 }, 4));
            Assert.Equal(GeodesicPathStatus.InvalidInput, b.Compute(new[] { a, c }, new[] { 0.5, 0.2 }));
            Assert.Equal(GeodesicPathStatus.InvalidInput, b.Compute(new[] { a, c }, new[] { 0.5, 1.5 }));
            Assert.Equal(GeodesicPathStatus.InvalidInput, b.Compute(new[] { a, c }, new[] { double.NaN }));
            Assert.Empty(b.Points); Assert.Empty(b.Path); Assert.Equal(0, b.Length);
            // a bowtie vertex
            var bow = new DMesh3();
            int o = bow.AppendVertex(Vector3d.Zero);
            int p1 = bow.AppendVertex(new Vector3d(1, 0, 0)), p2 = bow.AppendVertex(new Vector3d(1, 1, 0));
            int q1 = bow.AppendVertex(new Vector3d(-1, 0, 0)), q2 = bow.AppendVertex(new Vector3d(-1, -1, 0));
            bow.AppendTriangle(o, p1, p2);
            bow.AppendTriangle(o, q1, q2);
            Assert.Equal(GeodesicPathStatus.InvalidInput, new MeshGeodesicBezier(bow).Compute(new[] { p1, q1 }, 2));

            // two components
            var two = new DMesh3(mesh);
            int u = two.AppendVertex(new Vector3d(5, 0, 0)), v = two.AppendVertex(new Vector3d(6, 0, 0)), w = two.AppendVertex(new Vector3d(5, 1, 0));
            two.AppendTriangle(u, v, w);
            var bt = new MeshGeodesicBezier(two);
            Assert.Equal(GeodesicPathStatus.NoPath, bt.Compute(new[] { ids[0, 0], u }, 2));
            Assert.Empty(bt.Points);
        }

        [Fact]
        public void RepeatedAndCoincidentControlPoints_GiveAPoint()
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(6, 6, 0.1, 2, out ids);
            var b = new MeshGeodesicBezier(mesh);
            Assert.Equal(GeodesicPathStatus.Success, b.Compute(new[] { ids[2, 3], ids[2, 3], ids[2, 3] }, 3));
            Assert.All(b.Points, p => Assert.Equal(mesh.GetVertex(ids[2, 3]), p));
            Assert.Equal(0, b.Length);
            Assert.Single(b.Path);
        }
    }
}
