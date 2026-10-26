using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Geodesic paths and loops by edge flips (MeshGeodesicPath, FlipOut). Developable meshes (plane, cube surface,
    /// polygonal cylinder) have exact geodesics in closed form: the straight line in the unfolding.
    /// </summary>
    public class MeshGeodesicPathTests
    {
        // ---------------------------------------------------------------- meshes

        /// <summary> plane grid of nx x ny cells of size h, interior vertices jittered in the plane, random diagonals </summary>
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

        /// <summary>
        /// open polygonal cylinder: n slices around (radius 1), rings + 1 rings of height dz; quad (i, j) split by the
        /// diagonal (i, j) - (i + 1, j + 1). Unfolded it is a plane strip: slice i at x = i * chord.
        /// </summary>
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

        static double Chord(int n) { return 2 * Math.Sin(Math.PI / n); }

        /// <summary> L-shaped plane: [0, 2] x [0, 1] and [0, 1] x [0, 2], cells of 0.1; the reflex corner at (1, 1) </summary>
        static DMesh3 LShape(out Dictionary<(int, int), int> ids)
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
            ids = map;
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

        static int Nearest(DMesh3 m, Vector3d p)
        {
            return m.VertexIndices().OrderBy(v => m.GetVertex(v).Distance(p)).First();
        }

        // ---------------------------------------------------------------- checks

        /// <summary> the result is a finite polyline from the source to the target whose length is Length </summary>
        /// (pointResolution: rounding of the path points, which far from the origin limits the polyline length's accuracy)
        static void AssertValidPath(MeshGeodesicPath g, DMesh3 mesh, int source, int target, double pointResolution = 0)
        {
            Assert.True(g.Path.Count >= 2, "path has points");
            Assert.True(g.Path.All(p => p.IsFinite), "finite points");
            Assert.Equal(mesh.GetVertex(source), g.Path[0]);
            Assert.Equal(mesh.GetVertex(target), g.Path[g.Path.Count - 1]);
            Assert.Equal(source, g.PathVertices[0]);
            Assert.Equal(target, g.PathVertices[g.PathVertices.Count - 1]);
            double poly = 0;
            for (int k = 1; k < g.Path.Count; ++k) poly += g.Path[k].Distance(g.Path[k - 1]);
            Assert.True(Math.Abs(poly - g.Length) <= 1e-9 * g.Length + 4 * g.Path.Count * pointResolution,
                $"polyline length {poly} vs Length {g.Length}");
            Assert.True(g.Length <= g.InitialLength * (1 + 1e-15), "not longer than the edge path");
        }

        // ---------------------------------------------------------------- exact geodesics

        [Fact]
        public void Plane_GivesTheStraightLine()
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(12, 10, 0.1, 3, out ids);
            int s = ids[1, 2], t = ids[11, 9];
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Success, g.Compute(s, t));
            AssertValidPath(g, mesh, s, t);
            Vector3d a = mesh.GetVertex(s), b = mesh.GetVertex(t);
            Assert.True(Math.Abs(g.Length - a.Distance(b)) < 1e-12, $"{g.Length} vs {a.Distance(b)}");
            Assert.True(g.InitialLength > g.Length * 1.01, "the edge path was longer");
            var line = new Segment3d(a, b);
            foreach (Vector3d p in g.Path)
                Assert.True(line.DistanceSquared(p) < 1e-24, "on the straight line");
            Assert.Equal(new[] { s, t }, g.PathVertices);       // no vertex in between (jittered)
            Assert.True(g.Flips > 0);
        }

        [Fact]
        public void Cube_OppositeCorners_SqrtFive()
        {
            DMesh3 mesh = new GridBox3Generator() { EdgeVertices = 6 }.Generate().MakeDMesh();
            AxisAlignedBox3d box = mesh.CachedBounds;
            double side = box.Width;
            int s = Nearest(mesh, box.Min), t = Nearest(mesh, box.Max);
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Success, g.Compute(s, t));
            AssertValidPath(g, mesh, s, t);
            Assert.True(Math.Abs(g.Length - Math.Sqrt(5) * side) < 1e-12, $"{g.Length} vs {Math.Sqrt(5) * side}");
            // the cube corners (angle 3 pi / 2) are never on a geodesic
            var corners = Enumerable.Range(0, 8).Select(k => Nearest(mesh, box.Corner(k))).ToList();
            foreach (int v in g.PathVertices.Skip(1).Take(g.PathVertices.Count - 2))
                Assert.DoesNotContain(v, corners);
        }

        [Theory]
        [InlineData(5, 9)]
        [InlineData(11, 7)]     // the short way round is backwards (16 - 11 = 5 slices)
        [InlineData(3, 2)]
        public void Cylinder_GivesTheUnrolledStraightLine(int di, int dj)
        {
            int n = 16, rings = 10;
            double dz = 0.2;
            int[,] ids;
            DMesh3 mesh = Cylinder(n, rings, dz, out ids);
            int s = ids[0, 0], t = ids[di, dj];
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Success, g.Compute(s, t));
            AssertValidPath(g, mesh, s, t);
            double dx = Math.Min(di, n - di) * Chord(n), expected = Math.Sqrt(dx * dx + dj * dz * dj * dz);
            Assert.True(Math.Abs(g.Length - expected) < 1e-12, $"{g.Length} vs {expected}");
        }

        [Fact]
        public void LShape_BendsAtTheReflexBoundaryCorner()
        {
            Dictionary<(int, int), int> ids;
            DMesh3 mesh = LShape(out ids);
            int s = ids[(19, 9)], t = ids[(9, 19)], corner = ids[(10, 10)];
            double expected = 2 * Math.Sqrt(0.9 * 0.9 + 0.1 * 0.1);
            foreach (var pair in new[] { (s, t), (t, s) }) {
                var g = new MeshGeodesicPath(mesh);
                Assert.Equal(GeodesicPathStatus.Success, g.Compute(pair.Item1, pair.Item2));
                AssertValidPath(g, mesh, pair.Item1, pair.Item2);
                Assert.True(Math.Abs(g.Length - expected) < 1e-12, $"{g.Length} vs {expected}");
                Assert.Contains(corner, g.PathVertices);
            }
        }

        [Fact]
        public void LShape_PathAlongTheBoundaryInBothDirections()
        {
            // boundary edges have one halfedge: a path along the boundary runs against it one way
            Dictionary<(int, int), int> ids;
            DMesh3 mesh = LShape(out ids);
            var forward = new List<int>();
            for (int i = 2; i <= 18; ++i) forward.Add(ids[(i, 0)]);
            foreach (var path in new[] { forward, Enumerable.Reverse(forward).ToList() }) {
                var g = new MeshGeodesicPath(mesh);
                Assert.Equal(GeodesicPathStatus.Success, g.Shorten(path));
                AssertValidPath(g, mesh, path[0], path[path.Count - 1]);
                Assert.True(Math.Abs(g.Length - 1.6) < 1e-12, $"{g.Length}");
                Assert.Equal(0, g.Iterations);      // already straight
            }
        }

        [Fact]
        public void Waypoints_AreKept()
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(12, 10, 0.1, 5, out ids);
            int a = ids[1, 1], w = ids[6, 9], b = ids[11, 2];
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Success, g.ComputeThrough(new[] { a, w, b }));
            AssertValidPath(g, mesh, a, b);
            Assert.Contains(w, g.PathVertices);
            Vector3d pa = mesh.GetVertex(a), pw = mesh.GetVertex(w), pb = mesh.GetVertex(b);
            Assert.True(Math.Abs(g.Length - (pa.Distance(pw) + pw.Distance(pb))) < 1e-12);

            // closed: the triangle a, w, b
            Assert.Equal(GeodesicPathStatus.Success, g.ComputeThrough(new[] { a, w, b }, true));
            Assert.True(Math.Abs(g.Length - (pa.Distance(pw) + pw.Distance(pb) + pb.Distance(pa))) < 1e-12);
            Assert.Equal(new[] { a, w, b }, g.PathVertices);
            Assert.True(g.Path[0].Distance(g.Path[g.Path.Count - 1]) == 0, "closed polyline");
        }

        [Fact]
        public void Sphere_CloseToTheGreatCircle()
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 24 }.Generate().MakeDMesh();
            int s = Nearest(mesh, new Vector3d(1, 0.2, 0.1)), t = Nearest(mesh, new Vector3d(-0.3, 0.9, -0.4));
            Vector3d a = mesh.GetVertex(s).Normalized, b = mesh.GetVertex(t).Normalized;
            double arc = Math.Acos(MathUtil.Clamp(a.Dot(b), -1, 1));
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Success, g.Compute(s, t));
            AssertValidPath(g, mesh, s, t);
            Assert.True(Math.Abs(g.Length - arc) < 0.01 * arc, $"{g.Length} vs great circle {arc}");
            Assert.True(g.InitialLength > g.Length && g.Flips > 0, "the edge path was longer");
        }

        // ---------------------------------------------------------------- loops

        [Fact]
        public void Cylinder_ZigzagLoopTightensToTheRing()
        {
            int n = 16;
            int[,] ids;
            DMesh3 mesh = Cylinder(n, 10, 0.2, out ids);
            var loop = new List<int>();
            for (int i = 0; i <= 7; ++i) loop.Add(ids[i, 5]);
            for (int i = 8; i < n; ++i) loop.Add(ids[i, 6]);
            loop.Add(ids[0, 6]);
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Success, g.Shorten(loop, true));
            Assert.True(Math.Abs(g.Length - n * Chord(n)) < 1e-12, $"{g.Length} vs {n * Chord(n)}");
            Assert.True(g.InitialLength > g.Length + 0.1);
            Assert.True(g.Path[0].Distance(g.Path[g.Path.Count - 1]) == 0, "closed polyline");
            double z = g.Path[0].z;
            Assert.True(g.Path.All(p => Math.Abs(p.z - z) < 1e-12), "a horizontal ring");
        }

        [Fact]
        public void Plane_ContractibleLoopCollapses()
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(8, 8, 0.1, 7, out ids);
            var loop = new List<int>();
            for (int i = 2; i <= 6; ++i) loop.Add(ids[i, 2]);
            for (int j = 3; j <= 6; ++j) loop.Add(ids[6, j]);
            for (int i = 5; i >= 2; --i) loop.Add(ids[i, 6]);
            for (int j = 5; j >= 3; --j) loop.Add(ids[2, j]);
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Collapsed, g.Shorten(loop, true));
            Assert.Equal(0, g.Length);
            Assert.Single(g.Path);
            Assert.Single(g.PathVertices);
            Assert.Equal(mesh.GetVertex(g.PathVertices[0]), g.Path[0]);
        }

        // ---------------------------------------------------------------- invariance, reuse, input

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void BumpySphere_InvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 mesh = BumpySphere();
            int s = Nearest(mesh, new Vector3d(1, 0, 0)), t = Nearest(mesh, new Vector3d(-0.5, 0.7, -0.6));
            var g0 = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Success, g0.Compute(s, t));
            AssertValidPath(g0, mesh, s, t);
            Assert.True(g0.Flips > 10 && g0.InitialLength > g0.Length, "the path was shortened (exercised)");

            DMesh3 moved = Invariance.Transformed(mesh, sim);
            var g = new MeshGeodesicPath(moved);
            Assert.Equal(GeodesicPathStatus.Success, g.Compute(s, t));
            AssertValidPath(g, moved, s, t, sim.Scale * sim.Resolution(2.5));
            Assert.Equal(g0.PathVertices, g.PathVertices);
            if (sim.IsExact) {
                Assert.Equal(g0.Length, g.Length / sim.Scale);
                Assert.Equal(g0.Flips, g.Flips);
                Assert.Equal(g0.Path.Count, g.Path.Count);
                for (int k = 0; k < g0.Path.Count; ++k)
                    Assert.Equal(g0.Path[k], g.Path[k] / sim.Scale);
            } else {
                // the input itself is rounded: far from the origin the edge lengths are only accurate to Resolution
                double tol = 1e-9 * g0.Length + 64 * sim.Resolution(2.5);
                Assert.True(Math.Abs(g0.Length - g.Length / sim.Scale) < tol, $"{g0.Length} vs {g.Length / sim.Scale}");
            }
        }

        [Fact]
        public void Reuse_SameResultTwice_AndMeshUnchanged()
        {
            DMesh3 mesh = BumpySphere();
            var before = mesh.VertexIndices().Select(v => mesh.GetVertex(v)).ToList();
            int tcount = mesh.TriangleCount;
            int s = Nearest(mesh, new Vector3d(1, 0, 0)), t = Nearest(mesh, new Vector3d(-0.5, 0.7, -0.6));
            int u = Nearest(mesh, new Vector3d(0, 0, 1));
            var g = new MeshGeodesicPath(mesh);
            g.Compute(s, t);
            double len = g.Length;
            var verts = g.PathVertices.ToList();
            var path = g.Path.ToList();
            g.Compute(s, u);
            Assert.NotEqual(len, g.Length);
            g.Compute(s, t);
            Assert.Equal(len, g.Length);
            Assert.Equal(verts, g.PathVertices);
            Assert.Equal(path, g.Path);
            Assert.Equal(tcount, mesh.TriangleCount);
            Assert.Equal(before, mesh.VertexIndices().Select(v => mesh.GetVertex(v)).ToList());
        }

        [Fact]
        public void IterationLimit_GivesAValidShorterPath()
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(20, 20, 0.1, 11, out ids);
            int s = ids[0, 0], t = ids[20, 13];
            var g = new MeshGeodesicPath(mesh) { MaxIterations = 2 };
            Assert.Equal(GeodesicPathStatus.NotConverged, g.Compute(s, t));
            AssertValidPath(g, mesh, s, t);
            Assert.Equal(2, g.Iterations);
            double partial = g.Length;
            g.MaxIterations = 0;
            Assert.Equal(GeodesicPathStatus.Success, g.Compute(s, t));
            Assert.True(g.Length < partial);
        }

        [Fact]
        public void InvalidInputs_GiveStatusCodes()
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(4, 4, 0.1, 1, out ids);
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.InvalidInput, g.Compute(ids[0, 0], 9999));
            Assert.Empty(g.Path);
            Assert.Equal(GeodesicPathStatus.InvalidInput, g.Shorten(new[] { ids[0, 0], ids[3, 3] }));
            Assert.Equal(GeodesicPathStatus.InvalidInput, g.ComputeThrough(new[] { ids[1, 1] }, true));
            Assert.Equal(GeodesicPathStatus.InvalidInput, g.Shorten(new[] { ids[1, 1] }));

            // a single vertex: a path of length 0
            Assert.Equal(GeodesicPathStatus.Success, g.Compute(ids[2, 2], ids[2, 2]));
            Assert.Equal(0, g.Length);
            Assert.Single(g.Path);

            // two components
            var two = new DMesh3(mesh);
            int a = two.AppendVertex(new Vector3d(5, 0, 0)), b = two.AppendVertex(new Vector3d(6, 0, 0)), c = two.AppendVertex(new Vector3d(5, 1, 0));
            two.AppendTriangle(a, b, c);
            g = new MeshGeodesicPath(two);
            Assert.Equal(GeodesicPathStatus.NoPath, g.Compute(ids[0, 0], a));
            Assert.Empty(g.Path);

            // a bowtie vertex
            var bow = new DMesh3();
            int o = bow.AppendVertex(Vector3d.Zero);
            int p1 = bow.AppendVertex(new Vector3d(1, 0, 0)), p2 = bow.AppendVertex(new Vector3d(1, 1, 0));
            int q1 = bow.AppendVertex(new Vector3d(-1, 0, 0)), q2 = bow.AppendVertex(new Vector3d(-1, -1, 0));
            bow.AppendTriangle(o, p1, p2);
            bow.AppendTriangle(o, q1, q2);
            g = new MeshGeodesicPath(bow);
            Assert.Equal(GeodesicPathStatus.InvalidInput, g.Compute(p1, q1));
        }

        [Fact]
        public void Backtrack_AtAFixedVertex_KeepsItFixed()
        {
            // a -> w -> c -> w -> b with w fixed: the detour to c disappears, the two visits of w merge and stay fixed
            int[,] ids;
            DMesh3 mesh = JitteredPlane(6, 6, 0.1, 2, out ids);
            int a = ids[1, 1], w = ids[3, 4], c = ids[3, 5], b = ids[5, 1];
            // edge path: a ... w (along edges), c, w, ... b
            var d = new MeshGeodesicPath(mesh);
            List<int> legA = EdgePath(mesh, a, w), legB = EdgePath(mesh, w, b);
            var full = legA.Concat(new[] { c, w }).Concat(legB.Skip(1)).ToList();
            Assert.Equal(GeodesicPathStatus.Success, d.Shorten(full, false, new[] { w }));
            AssertValidPath(d, mesh, a, b);
            Assert.DoesNotContain(c, d.PathVertices);
            Assert.Single(d.PathVertices, w);
            Vector3d pa = mesh.GetVertex(a), pw = mesh.GetVertex(w), pb = mesh.GetVertex(b);
            Assert.True(Math.Abs(d.Length - (pa.Distance(pw) + pw.Distance(pb))) < 1e-12, $"{d.Length}");
        }

        /// <summary> a path along mesh edges (breadth first, fewest edges) </summary>
        static List<int> EdgePath(DMesh3 mesh, int a, int b)
        {
            var pred = new Dictionary<int, int>() { { a, -1 } };
            var queue = new Queue<int>();
            queue.Enqueue(a);
            while (queue.Count > 0) {
                int u = queue.Dequeue();
                if (u == b) break;
                foreach (int v in mesh.VtxVerticesItr(u))
                    if (pred.ContainsKey(v) == false) { pred[v] = u; queue.Enqueue(v); }
            }
            var path = new List<int>();
            for (int v = b; v != -1; v = pred[v]) path.Add(v);
            path.Reverse();
            return path;
        }

        [Fact]
        public void Cylinder_LoopTwiceAround_TightensToTheRingTwice()
        {
            // the loop overlaps itself once tight: the wedges then contain edges of the path, which must not be flipped
            int n = 16;
            int[,] ids;
            DMesh3 mesh = Cylinder(n, 10, 0.2, out ids);
            var loop = new List<int>();
            for (int i = 0; i < n; ++i) loop.Add(ids[i, 5]);
            for (int i = 0; i < n; ++i) loop.Add(ids[i, 6]);
            loop.Add(ids[0, 6]);
            var g = new MeshGeodesicPath(mesh);
            GeodesicPathStatus status = g.Shorten(loop, true);
            Assert.Equal(GeodesicPathStatus.Success, status);
            double poly = 0;
            for (int k = 1; k < g.Path.Count; ++k) poly += g.Path[k].Distance(g.Path[k - 1]);
            Assert.True(Math.Abs(poly - g.Length) <= 1e-9 * g.Length, $"polyline length {poly} vs Length {g.Length}");
            Assert.True(g.Path[0].Distance(g.Path[g.Path.Count - 1]) == 0, "closed polyline");
            Assert.True(g.Length < g.InitialLength);
            Assert.True(Math.Abs(g.Length - 2 * n * Chord(n)) < 1e-12, $"{g.Length} vs {2 * n * Chord(n)}");
        }

        /// <summary>
        /// open square pyramid with apex height H over the square [-1, 1]^2: a middle ring at half height, the base
        /// square as the boundary; the apex is a cone vertex with angle sum 8 atan(1 / sqrt(H^2 + 1))
        /// </summary>
        static DMesh3 Pyramid(double H, out int apex, out int[] ring)
        {
            var m = new DMesh3();
            apex = m.AppendVertex(new Vector3d(0, 0, H));
            var c = new[] { new Vector2d(1, 1), new Vector2d(-1, 1), new Vector2d(-1, -1), new Vector2d(1, -1) };
            ring = c.Select(p => m.AppendVertex(new Vector3d(0.5 * p.x, 0.5 * p.y, 0.5 * H))).ToArray();
            int[] baseRing = c.Select(p => m.AppendVertex(new Vector3d(p.x, p.y, 0))).ToArray();
            for (int k = 0; k < 4; ++k) {
                int k1 = (k + 1) % 4;
                m.AppendTriangle(apex, ring[k], ring[k1]);
                m.AppendTriangle(ring[k], baseRing[k], baseRing[k1]);
                m.AppendTriangle(ring[k], baseRing[k1], ring[k1]);
            }
            return m;
        }

        [Theory]
        [InlineData(0.5)]       // cone angle 334 degrees
        [InlineData(1.0)]       // 282 degrees
        [InlineData(3.0)]       // 140 degrees
        [InlineData(8.0)]       // 57 degrees
        public void Pyramid_LoopAroundTheApexCollapses(double H)
        {
            // a closed loop on a disk is contractible; with a cone angle below pi it collapses through loops of one segment
            int apex; int[] ring;
            DMesh3 mesh = Pyramid(H, out apex, out ring);
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Collapsed, g.Shorten(ring, true));
            Assert.Equal(0, g.Length);
            Assert.Single(g.Path);
            Assert.Equal(mesh.GetVertex(g.PathVertices[0]), g.Path[0]);
        }

        [Fact]
        public void ContractibleLoop_WithAFixedVertex_CollapsesToIt()
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(8, 8, 0.1, 7, out ids);
            var loop = new List<int>();
            for (int i = 2; i <= 6; ++i) loop.Add(ids[i, 2]);
            for (int j = 3; j <= 6; ++j) loop.Add(ids[6, j]);
            for (int i = 5; i >= 2; --i) loop.Add(ids[i, 6]);
            for (int j = 5; j >= 3; --j) loop.Add(ids[2, j]);
            int pin = ids[6, 4];
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Collapsed, g.Shorten(loop, true, new[] { pin }));
            Assert.Equal(new[] { pin }, g.PathVertices);
            Assert.Equal(mesh.GetVertex(pin), g.Path[0]);
        }

        [Fact]
        public void RepeatedWaypoint_GoesThereAndBack()
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(8, 8, 0.1, 4, out ids);
            int a = ids[1, 1], b = ids[6, 5], c = ids[7, 1];
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Success, g.ComputeThrough(new[] { a, b, a, c }));
            AssertValidPath(g, mesh, a, c);
            Vector3d pa = mesh.GetVertex(a), pb = mesh.GetVertex(b), pc = mesh.GetVertex(c);
            Assert.True(Math.Abs(g.Length - (2 * pa.Distance(pb) + pa.Distance(pc))) < 1e-12, $"{g.Length}");
            Assert.Equal(new[] { a, b, a, c }, g.PathVertices);
        }

        [Fact]
        public void DegenerateTriangle_IsMollified()
        {
            // a zero-area triangle (a, b, midpoint) on the boundary edge a-b: the surface stays the flat rectangle,
            // and the intrinsic mollification makes the triangle valid
            int[,] ids;
            DMesh3 mesh = JitteredPlane(8, 8, 0.1, 6, out ids);
            int a = ids[3, 0], b = ids[4, 0];
            int tid = mesh.GetEdgeT(mesh.FindEdge(a, b)).a;      // a boundary edge: one triangle
            Index3i tri = mesh.GetTriangle(tid);
            int k = tri.a == a ? 0 : tri.b == a ? 1 : 2;
            Assert.Equal(b, tri[(k + 1) % 3]);
            int top = tri[(k + 2) % 3];
            mesh.RemoveTriangle(tid);
            int mid = mesh.AppendVertex(0.5 * (mesh.GetVertex(a) + mesh.GetVertex(b)));
            mesh.AppendTriangle(a, mid, top);
            mesh.AppendTriangle(mid, b, top);
            mesh.AppendTriangle(mid, a, b);
            int s = ids[0, 2], t = ids[8, 1];
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Success, g.Compute(s, t));
            Assert.True(g.Triangulation.MollificationDelta > 0, "mollified (exercised)");
            // Length is in the mollified geometry: every edge is longer by the delta
            AssertValidPath(g, mesh, s, t, g.Triangulation.MollificationDelta);
            double straight = mesh.GetVertex(s).Distance(mesh.GetVertex(t));
            Assert.True(Math.Abs(g.Length - straight) < 1e-5 * straight, $"{g.Length} vs {straight}");
        }

        [Fact]
        public void FailedTrace_IsReported()
        {
            int[,] ids;
            DMesh3 mesh = JitteredPlane(6, 6, 0.1, 2, out ids);
            var g = new MeshGeodesicPath(mesh) { TraceTolerance = -1 };     // every trace counts as failed
            Assert.Equal(GeodesicPathStatus.TraceFailed, g.Compute(ids[0, 0], ids[6, 5]));
            Assert.Equal(mesh.GetVertex(ids[0, 0]).Distance(mesh.GetVertex(ids[6, 5])), g.Length, 12);
        }

        [Fact]
        public void BacktrackingEdgePath_IsRemoved()
        {
            // a -> b -> c -> b -> d: the detour to c and back disappears
            int[,] ids;
            DMesh3 mesh = JitteredPlane(6, 6, 0.1, 2, out ids);
            int a = ids[1, 1], b = ids[2, 1], c = ids[2, 2], d = ids[3, 1];
            var g = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Success, g.Shorten(new[] { a, b, c, b, d }));
            AssertValidPath(g, mesh, a, d);
            Assert.DoesNotContain(c, g.PathVertices);
            Assert.True(Math.Abs(g.Length - mesh.GetVertex(a).Distance(mesh.GetVertex(d))) < 1e-12);
        }
    }
}
