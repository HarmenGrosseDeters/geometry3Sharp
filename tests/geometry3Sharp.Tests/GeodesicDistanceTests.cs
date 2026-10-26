using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Geodesic distances along a surface (heat method), eg distances along the terrain for haul roads.
    /// </summary>
    public class GeodesicDistanceTests
    {
        static DMesh3 Grid(double x0, double y0, double x1, double y1, int n, Func<double, double, double> h)
        {
            DMesh3 mesh = new DMesh3();
            int[,] vid = new int[n, n];
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    double x = x0 + (x1 - x0) * i / (n - 1), y = y0 + (y1 - y0) * j / (n - 1);
                    vid[i, j] = mesh.AppendVertex(new Vector3d(x, y, h(x, y)));
                }
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    // alternate diagonals, so the grid has no preferred direction
                    if ((i + j) % 2 == 0) {
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                    } else {
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i, j + 1]);
                        mesh.AppendTriangle(vid[i + 1, j], vid[i + 1, j + 1], vid[i, j + 1]);
                    }
                }
            return mesh;
        }

        static int NearestVertex(DMesh3 m, Vector3d p) { return m.VertexIndices().OrderBy(v => m.GetVertex(v).DistanceSquared(p)).First(); }

        [Fact]
        public void Plane_DistanceIsEuclidean()
        {
            DMesh3 mesh = Grid(-50, -50, 50, 50, 101, (x, y) => 0);
            int src = NearestVertex(mesh, Vector3d.Zero);
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(new[] { src }));
            foreach (int vid in mesh.VertexIndices()) {
                double exact = mesh.GetVertex(vid).Length;
                // about 2% of the distance plus half an edge length (1 m here); smoothed near the source
                Assert.True(Math.Abs(geo.Distance[vid] - exact) < 0.02 * exact + 0.5, $"{mesh.GetVertex(vid)}: {geo.Distance[vid]} vs {exact}");
            }
        }

        static double MeanEdgeLength(DMesh3 mesh)
        {
            double min, max, avg;
            MeshQueries.EdgeLengthStats(mesh, out min, out max, out avg);
            return avg;
        }

        /// <summary> the documented bound near open boundaries: 2% of the distance plus one mean edge length </summary>
        static void AssertBoundaryBound(DMesh3 mesh, Vector3d source, string label)
        {
            int src = NearestVertex(mesh, source);
            Vector3d s = mesh.GetVertex(src);
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(new[] { src }));
            double h = MeanEdgeLength(mesh);
            foreach (int vid in mesh.VertexIndices()) {
                double exact = mesh.GetVertex(vid).Distance(s);    // convex planar domain: straight lines
                Assert.True(Math.Abs(geo.Distance[vid] - exact) < 0.02 * exact + h,
                    $"{label}: {mesh.GetVertex(vid)}: {geo.Distance[vid]} vs {exact}");
            }
        }

        [Fact]
        public void OpenBoundary_WithinTwoPercentPlusOneEdge()
        {
            // square with alternating diagonals, and with all diagonals in one direction (the worst case),
            // sources in the middle, near a corner, on an edge and in a corner
            DMesh3 alternating = Grid(0, 0, 10, 10, 41, (x, y) => 0);
            DMesh3 oneWay = new DMesh3();
            for (int j = 0; j <= 40; ++j)
                for (int i = 0; i <= 40; ++i)
                    oneWay.AppendVertex(new Vector3d(0.25 * i, 0.25 * j, 0));
            for (int j = 0; j < 40; ++j)
                for (int i = 0; i < 40; ++i) {
                    int a = j * 41 + i;
                    oneWay.AppendTriangle(a, a + 1, a + 42);
                    oneWay.AppendTriangle(a, a + 42, a + 41);
                }
            foreach (DMesh3 mesh in new[] { alternating, oneWay })
                foreach (Vector3d src in new[] { new Vector3d(5, 5, 0), new Vector3d(1, 1, 0), new Vector3d(5, 0, 0), Vector3d.Zero })
                    AssertBoundaryBound(mesh, src, $"square {(mesh == oneWay ? "one-way" : "alternating")} from {src}");

            // irregular triangulation of a disc of radius 5, sources in the middle, off center and on the boundary
            DMesh3 disc = new TrivialDiscGenerator() { Radius = 5, Slices = 64 }.Generate().MakeDMesh();
            var remesher = new Remesher(disc);
            remesher.SetTargetEdgeLength(0.25);
            MeshConstraintUtil.PreserveBoundaryLoops(remesher);
            for (int k = 0; k < 30; ++k)
                remesher.BasicRemeshPass();
            AxisAlignedBox3d b = disc.CachedBounds;
            Vector3d c = b.Center;
            Vector3d r = (b.Max.x - c.x) * Vector3d.AxisX;
            foreach (Vector3d src in new[] { c, c + 0.6 * r, c + r })
                AssertBoundaryBound(disc, src, $"disc from {src}");
        }

        [Fact]
        public void Cylinder_DistanceIsArcLength()
        {
            // half cylinder of radius 20 over x, unrolled width pi*20; distance from a line along x
            double R = 20;
            DMesh3 mesh = Grid(0, 0, 60, Math.PI * R, 81, (x, y) => 0);
            foreach (int vid in mesh.VertexIndices().ToArray()) {
                Vector3d v = mesh.GetVertex(vid);
                double a = v.y / R;
                mesh.SetVertex(vid, new Vector3d(v.x, R * Math.Sin(a), R - R * Math.Cos(a)));
            }
            // sources: the line y = 0 (arc start)
            var src = mesh.VertexIndices().Where(v => mesh.GetVertex(v).y == 0 && mesh.GetVertex(v).z == 0).ToArray();
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(src));
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                double arc = R * Math.Atan2(v.y, R - v.z);
                if (v.x < 5 || v.x > 55) continue;     // away from the cylinder ends
                double h = 60.0 / 80;
                Assert.True(Math.Abs(geo.Distance[vid] - arc) < 0.02 * arc + 0.5 * h, $"{v}: {geo.Distance[vid]} vs arc {arc}");
            }
        }

        [Fact]
        public void HillyTerrain_BetweenStraightLineAndEdgePaths()
        {
            Func<double, double, double> h = (x, y) => 8 * Math.Sin(x / 15.0) * Math.Cos(y / 11.0);
            DMesh3 mesh = Grid(-50, -50, 50, 50, 101, h);
            int src = NearestVertex(mesh, new Vector3d(-40, -40, 0));
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(new[] { src }));
            var dijkstra = DijkstraGraphDistance.MeshVertices(mesh);
            dijkstra.AddSeed(src, 0);
            dijkstra.Compute();
            Vector3d s = mesh.GetVertex(src);
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                double straight = v.Distance(s), edges = dijkstra.GetDistance(vid);
                Assert.True(geo.Distance[vid] >= 0.98 * straight - 0.5, $"{v}: {geo.Distance[vid]} below the straight line {straight}");
                Assert.True(geo.Distance[vid] <= 1.01 * edges + 0.5, $"{v}: {geo.Distance[vid]} above the edge path {edges}");
            }
        }

        [Fact]
        public void MultipleSources_NearestOne()
        {
            DMesh3 mesh = Grid(-50, -50, 50, 50, 81, (x, y) => 0);
            int a = NearestVertex(mesh, new Vector3d(-30, 0, 0)), b = NearestVertex(mesh, new Vector3d(30, 0, 0));
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(new[] { a, b }));
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                double exact = Math.Min(v.Distance(mesh.GetVertex(a)), v.Distance(mesh.GetVertex(b)));
                Assert.True(Math.Abs(geo.Distance[vid] - exact) < 0.02 * exact + 0.5 * 100.0 / 80, $"{v}: {geo.Distance[vid]} vs {exact}");
            }
        }

        static double Length(List<Vector3d> path)
        {
            double l = 0;
            for (int i = 1; i < path.Count; ++i) l += path[i].Distance(path[i - 1]);
            return l;
        }

        [Fact]
        public void Path_Plane_IsStraight()
        {
            DMesh3 mesh = Grid(-50, -50, 50, 50, 101, (x, y) => 0);
            int src = NearestVertex(mesh, Vector3d.Zero);
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(new[] { src }));
            Vector3d start = new Vector3d(40, 25, 0);
            List<Vector3d> path = geo.TracePath(start);
            Assert.True(path.Count >= 2);
            Assert.True(path[0].Distance(start) < 1e-9);
            Assert.True(path[path.Count - 1].Distance(mesh.GetVertex(src)) < 1e-9, "path does not end at the source");
            double straight = start.Length;
            Assert.True(Math.Abs(Length(path) - straight) < 0.01 * straight, $"length {Length(path)} vs {straight}");
            // close to the straight line
            var seg = new Segment3d(start, Vector3d.Zero);
            Assert.True(path.Max(p => seg.DistanceSquared(p)) < 1.0, "path leaves the straight line");
        }

        [Fact]
        public void Path_HillyTerrain_OnSurfaceAndShortest()
        {
            Func<double, double, double> h = (x, y) => 8 * Math.Sin(x / 15.0) * Math.Cos(y / 11.0);
            DMesh3 mesh = Grid(-50, -50, 50, 50, 101, h);
            int src = NearestVertex(mesh, new Vector3d(-40, -40, 0));
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(new[] { src }));
            var spatial = new DMeshAABBTree3(mesh, true);
            var dijkstra = DijkstraGraphDistance.MeshVertices(mesh);
            dijkstra.AddSeed(src, 0);
            dijkstra.Compute();
            foreach (Vector3d target in new[] { new Vector3d(40, 40, 0), new Vector3d(45, -30, 0), new Vector3d(-35, 42, 0) }) {
                int tv = NearestVertex(mesh, target);
                List<Vector3d> path = geo.TracePath(tv);
                Assert.True(path[path.Count - 1].Distance(mesh.GetVertex(src)) < 1e-9);
                foreach (Vector3d p in path)
                    Assert.True(Math.Sqrt(MeshQueries.TriDistanceSqr(mesh, spatial.FindNearestTriangle(p), p)) < 1e-6, "path point off the surface");
                double len = Length(path);
                Assert.True(len >= mesh.GetVertex(tv).Distance(mesh.GetVertex(src)) - 1e-6, "shorter than the straight line");
                Assert.True(len <= dijkstra.GetDistance(tv) * 1.001, $"longer than the edge path: {len} vs {dijkstra.GetDistance(tv)}");
                Assert.True(Math.Abs(len - geo.Distance[tv]) < 0.03 * geo.Distance[tv] + 1, $"length {len} vs distance {geo.Distance[tv]}");
            }
        }

        static DMesh3 TwoGrids()
        {
            DMesh3 small = Grid(0, 0, 9, 9, 10, (x, y) => 0);
            DMesh3 large = Grid(1000, 0, 1039, 39, 40, (x, y) => 0);
            MeshEditor.Append(large, small);
            return large;
        }

        [Fact]
        public void TwoComponents_EachFromItsOwnSource()
        {
            DMesh3 mesh = TwoGrids();
            int a = NearestVertex(mesh, new Vector3d(0, 0, 0)), b = NearestVertex(mesh, new Vector3d(1000, 0, 0));
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(new[] { a, b }));
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                double exact = v.Distance(mesh.GetVertex(v.x < 500 ? a : b));
                Assert.True(Math.Abs(geo.Distance[vid] - exact) < 0.02 * exact + 0.5, $"{v}: {geo.Distance[vid]} vs {exact}");
            }
        }

        [Fact]
        public void ComponentWithoutSource_IsNaN_AndPathIsTheStartPoint()
        {
            DMesh3 mesh = TwoGrids();
            int b = NearestVertex(mesh, new Vector3d(1000, 0, 0));
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(new[] { b }));
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                if (v.x < 500) Assert.True(double.IsNaN(geo.Distance[vid]), $"{v}: {geo.Distance[vid]}");
                else Assert.False(double.IsNaN(geo.Distance[vid]));
            }
            var path = geo.TracePath(new Vector3d(5, 5, 0));
            Assert.Single(path);
        }

        [Fact]
        public void IsolatedVertices_AreIgnored()
        {
            DMesh3 mesh = Grid(-20, -20, 20, 20, 41, (x, y) => 0);
            int iso = mesh.AppendVertex(new Vector3d(500, 500, 0));
            int src = NearestVertex(mesh, Vector3d.Zero);
            var geo = new MeshGeodesicDistance(mesh);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.True(geo.Compute(new[] { src }));
            // (stalled: 20000 iterations, minutes; the bound leaves room for a loaded machine)
            Assert.True(sw.ElapsedMilliseconds < 30000, $"{sw.ElapsedMilliseconds} ms");
            Assert.True(double.IsNaN(geo.Distance[iso]));
            foreach (int vid in mesh.VertexIndices()) {
                if (vid == iso) continue;
                double exact = mesh.GetVertex(vid).Length;
                Assert.True(Math.Abs(geo.Distance[vid] - exact) < 0.02 * exact + 0.5);
            }
            // an isolated source does not disturb the others
            Assert.True(geo.Compute(new[] { src, iso }));
            Assert.Equal(0, geo.Distance[iso]);
            Assert.True(Math.Abs(geo.Distance[NearestVertex(mesh, new Vector3d(10, 0, 0))] - 10) < 0.7);
        }

        [Fact]
        public void LongStrip_NoUnderflowFarFromTheSource()
        {
            // the heat decays like exp(-d / edge): 1500 edge lengths are far below the double range
            DMesh3 mesh = new DMesh3();
            int n = 1501;
            int[,] vid = new int[n, 3];
            for (int j = 0; j < 3; ++j)
                for (int i = 0; i < n; ++i) vid[i, j] = mesh.AppendVertex(new Vector3d(i, j, 0));
            for (int j = 0; j < 2; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(new[] { vid[0, 1] }));
            foreach (int x in new[] { 100, 700, 1200, 1500 })
                Assert.True(Math.Abs(geo.Distance[vid[x, 1]] - x) < 0.02 * x + 0.5, $"x = {x}: {geo.Distance[vid[x, 1]]}");
            var path = geo.TracePath(vid[1500, 1]);
            Assert.True(path[path.Count - 1].Distance(mesh.GetVertex(vid[0, 1])) < 1e-9);
        }

        [Fact]
        public void Paths_MultipleSources_EndAtASource()
        {
            DMesh3 mesh = Grid(-50, -50, 50, 50, 81, (x, y) => 0);
            int a = NearestVertex(mesh, new Vector3d(-30, 0, 0)), b = NearestVertex(mesh, new Vector3d(30, 10, 0));
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(new[] { a, b }));
            foreach (int vid in mesh.VertexIndices()) {
                if (vid % 10 != 0) continue;
                var path = geo.TracePath(vid);
                Vector3d end = path[path.Count - 1];
                Assert.True(end.Distance(mesh.GetVertex(a)) < 1e-9 || end.Distance(mesh.GetVertex(b)) < 1e-9, $"path from {mesh.GetVertex(vid)} ends at {end}");
            }
        }

        [Fact]
        public void InvalidUse_ClearErrors()
        {
            DMesh3 mesh = Grid(0, 0, 10, 10, 11, (x, y) => 0);
            var geo = new MeshGeodesicDistance(mesh);
            Assert.Throws<InvalidOperationException>(() => geo.TracePath(0));
            Assert.False(geo.Compute(new int[0]));
            Assert.False(geo.Compute(new[] { -1, 100000 }));
            Assert.Throws<InvalidOperationException>(() => geo.TracePath(new Vector3d(5, 5, 0)));
            Assert.True(geo.Compute(new[] { 0 }));
            Assert.Throws<ArgumentException>(() => geo.TracePath(100000));
            // the mesh changed: distances are stale
            mesh.SetVertex(5, mesh.GetVertex(5) + Vector3d.AxisZ);
            Assert.Throws<InvalidOperationException>(() => geo.TracePath(3));
            Assert.True(geo.Compute(new[] { 0 }));
            var path = geo.TracePath(5);
            Assert.True(path[path.Count - 1].Distance(mesh.GetVertex(0)) < 1e-9);
        }

        [Fact]
        public void UtmCoordinates_SameDistances()
        {
            DMesh3 local = Grid(-50, -50, 50, 50, 61, (x, y) => 3 * Math.Sin(x / 10));
            DMesh3 utm = new DMesh3(local);
            MeshTransforms.Translate(utm, new Vector3d(632000, 5712000, 100));
            int src = NearestVertex(local, Vector3d.Zero);
            var a = new MeshGeodesicDistance(local); Assert.True(a.Compute(new[] { src }));
            var b = new MeshGeodesicDistance(utm); Assert.True(b.Compute(new[] { src }));
            foreach (int vid in local.VertexIndices())
                Assert.True(Math.Abs(a.Distance[vid] - b.Distance[vid]) < 1e-3, $"{a.Distance[vid]} vs {b.Distance[vid]}");
        }
        // the grid with a row of caps: on the row y = 0 each edge is split, its new vertex moved down by eps
        // and the edge to it from above flipped, so the vertex forms a triangle with the two ends of the
        // edge it came from (angle ~180 degrees). Remeshing with constrained feature lines leaves such
        // caps along break lines.
        static DMesh3 GridWithCaps(int n, double eps, out List<int> capVertices)
        {
            DMesh3 mesh = Grid(-(n - 1) / 2, -(n - 1) / 2, (n - 1) / 2, (n - 1) / 2, n, (x, y) => 0.3 * Math.Sin(0.2 * x));
            capVertices = new List<int>();
            for (int i = 5; i < n - 6; ++i) {
                int a = NearestVertexFast(mesh, n, i, (n - 1) / 2), b = NearestVertexFast(mesh, n, i + 1, (n - 1) / 2);
                DMesh3.EdgeSplitInfo split;
                Assert.Equal(MeshResult.Ok, mesh.SplitEdge(mesh.FindEdge(a, b), out split));
                mesh.SetVertex(split.vNew, mesh.GetVertex(split.vNew) - new Vector3d(0, eps, 0));
                int up = mesh.VtxVerticesItr(split.vNew).First(v => mesh.GetVertex(v).y > 0.5);
                DMesh3.EdgeFlipInfo flip;
                Assert.Equal(MeshResult.Ok, mesh.FlipEdge(mesh.FindEdge(split.vNew, up), out flip));
                capVertices.Add(split.vNew);
            }
            return mesh;
        }
        static int NearestVertexFast(DMesh3 m, int n, int i, int j) { return i + n * j; }

        [Fact]
        public void CapsAlongAFeatureLine_ConvergeWithTheSameDistances()
        {
            // the huge cotangent weights of the caps made the heat and Poisson solves stall (no result
            // after MaxIterations); on the intrinsic Delaunay triangulation the caps are flipped away
            int n = 161;
            List<int> caps;
            DMesh3 mesh = GridWithCaps(n, 1e-6, out caps);
            DMesh3 clean = Grid(-(n - 1) / 2, -(n - 1) / 2, (n - 1) / 2, (n - 1) / 2, n, (x, y) => 0.3 * Math.Sin(0.2 * x));
            foreach (int[] src in new[] { new[] { n / 2 + n * 10 }, new[] { NearestVertexFast(mesh, n, 5, (n - 1) / 2), NearestVertexFast(mesh, n, n - 6, (n - 1) / 2) } }) {
                var geo = new MeshGeodesicDistance(mesh);
                Assert.True(geo.Compute(src));
                Assert.True(geo.IntrinsicFlips > 0);
                var reference = new MeshGeodesicDistance(clean);
                Assert.True(reference.Compute(src));
                Assert.Equal(0, reference.IntrinsicFlips);
                // the grid vertices keep their ids; same surface, so about the same distances
                foreach (int vid in clean.VertexIndices())
                    Assert.True(Math.Abs(geo.Distance[vid] - reference.Distance[vid]) < 0.01 * reference.Distance[vid] + 0.3,
                        $"{clean.GetVertex(vid)}: {geo.Distance[vid]} vs {reference.Distance[vid]}");
                foreach (int vid in caps)
                    Assert.False(double.IsNaN(geo.Distance[vid]));
            }
        }

        [Fact]
        public void DelaunayMesh_IntrinsicSameAsExtrinsic()
        {
            // a plane grid is Delaunay: no flips, the same distances
            DMesh3 plane = Grid(-30, -30, 30, 30, 61, (x, y) => 0);
            int src = NearestVertex(plane, Vector3d.Zero);
            var a = new MeshGeodesicDistance(plane); Assert.True(a.Compute(new[] { src }));
            var b = new MeshGeodesicDistance(plane) { IntrinsicDelaunay = false }; Assert.True(b.Compute(new[] { src }));
            Assert.Equal(0, a.IntrinsicFlips);
            foreach (int vid in plane.VertexIndices())
                Assert.True(Math.Abs(a.Distance[vid] - b.Distance[vid]) < 1e-9, $"{a.Distance[vid]} vs {b.Distance[vid]}");
            // on a curved surface some edges are flipped: both within the accuracy of the method
            DMesh3 hills = Grid(-30, -30, 30, 30, 61, (x, y) => 3 * Math.Sin(x / 10) * Math.Cos(y / 7));
            a = new MeshGeodesicDistance(hills); Assert.True(a.Compute(new[] { src }));
            b = new MeshGeodesicDistance(hills) { IntrinsicDelaunay = false }; Assert.True(b.Compute(new[] { src }));
            foreach (int vid in hills.VertexIndices())
                Assert.True(Math.Abs(a.Distance[vid] - b.Distance[vid]) < 0.02 * b.Distance[vid] + 0.5, $"{a.Distance[vid]} vs {b.Distance[vid]}");
        }
        [Fact]
        public void LineAndPointSources_NoOffsetAtThePointSource()
        {
            // a line of sources (eg a crest) and a single far point source: phi of the Poisson step differs
            // between them; shifting it by the minimum put the surroundings of the point source 3.5 m too far
            // at 15 m. Now phi = 0 at every source; what is left (up to 1.8 m) comes from the heat step, where the
            // line's stronger heat moves the seam between the two towards the point (by about sqrt(t))
            DMesh3 mesh = Grid(-50, -50, 50, 50, 101, (x, y) => 0);
            var src = new List<int>();
            for (int i = -45; i <= 45; ++i) src.Add(NearestVertex(mesh, new Vector3d(i, -40, 0)));
            Vector3d point = new Vector3d(20, 30, 0);
            src.Add(NearestVertex(mesh, point));
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(src));
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                double exact = Math.Min(p.Distance(point), Math.Abs(p.y + 40) + Math.Max(0, Math.Abs(p.x) - 45));
                if (p.Distance(point) > 15) continue;   // around the point source
                Assert.True(Math.Abs(geo.Distance[vid] - exact) < 0.02 * exact + 2.0, $"{p}: {geo.Distance[vid]} vs {exact}");
            }
        }
        [Fact]
        public void SingleSource_RoughTerrain_NotPinned()
        {
            // one source on rough terrain (waves plus 30 cm noise, as a 1 m DGM): phi is not pinned there.
            // Pinned (Dirichlet at the one vertex) the distance came out 11% short a few metres from the
            // source here (7% at 1 km^2 of a real open pit); about 6% (the smoothing near the source) now
            int n = 501, h = (n - 1) / 2;
            var rnd = new Random(1);
            DMesh3 mesh = new DMesh3();
            int[,] vid = new int[n, n];
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    double x = i - h, y = j - h;
                    vid[i, j] = mesh.AppendVertex(new Vector3d(x, y, 5 * Math.Sin(x / 17) * Math.Cos(y / 23) + 0.3 * (rnd.NextDouble() - 0.5)));
                }
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            int src = vid[h, h];
            var geo = new MeshGeodesicDistance(mesh);
            Assert.True(geo.Compute(new[] { src }));
            foreach (int vid2 in mesh.VertexIndices()) {
                // the straight distance is a lower bound of the distance along the surface
                double e = mesh.GetVertex(vid2).Distance(mesh.GetVertex(src));
                if (e < 5) continue;
                Assert.True(geo.Distance[vid2] > 0.92 * e, $"{mesh.GetVertex(vid2)}: {geo.Distance[vid2]} vs straight {e}");
            }
        }

        static DMesh3 Strip(int len, out int[,] vid)
        {
            DMesh3 mesh = new DMesh3();
            vid = new int[len + 1, 3];
            for (int j = 0; j < 3; ++j)
                for (int i = 0; i <= len; ++i) vid[i, j] = mesh.AppendVertex(new Vector3d(i, j, 0));
            for (int j = 0; j < 2; ++j)
                for (int i = 0; i < len; ++i) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return mesh;
        }

        [Fact]
        public void VeryLongStrip_DirectSolvesInTheLogDomain()
        {
            // 25000 edge lengths: the heat underflows after ~700 and the Jacobi / CG iterations did not reach the
            // far end (Compute false after 36 s; at 5000 edge lengths distances wrong by 100)
            int len = 25000;
            int[,] vid;
            DMesh3 mesh = Strip(len, out vid);
            var geo = new MeshGeodesicDistance(mesh);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.True(geo.Compute(new[] { vid[0, 1] }));
            foreach (int x in new[] { 100, 1000, 5000, 12000, 25000 })
                Assert.True(Math.Abs(geo.Distance[vid[x, 1]] - x) < 0.02 * x + 0.5, $"x = {x}: {geo.Distance[vid[x, 1]]}");
            // and from the middle, a second source set reusing the factorizations
            Assert.True(geo.Compute(new[] { vid[len / 2, 1] }));
            Assert.True(Math.Abs(geo.Distance[vid[0, 1]] - len / 2) < 0.02 * len / 2 + 0.5);
            Assert.Equal(1, geo.PrecomputeCount);
        }

        [Fact]
        public void NonManifoldMesh_FallsBackToTheMeshTriangles()
        {
            // two grids sharing one corner vertex (a bowtie): no intrinsic triangulation, the mesh triangles are used
            DMesh3 mesh = Grid(0, 0, 10, 10, 11, (x, y) => 0);
            int corner = NearestVertex(mesh, new Vector3d(10, 10, 0));
            int[,] map = new int[11, 11];
            for (int j = 0; j < 11; ++j)
                for (int i = 0; i < 11; ++i)
                    map[i, j] = (i == 0 && j == 0) ? corner : mesh.AppendVertex(new Vector3d(10 + i, 10 + j, 0));
            for (int j = 0; j < 10; ++j)
                for (int i = 0; i < 10; ++i) {
                    mesh.AppendTriangle(map[i, j], map[i + 1, j], map[i + 1, j + 1]);
                    mesh.AppendTriangle(map[i, j], map[i + 1, j + 1], map[i, j + 1]);
                }
            Assert.Throws<ArgumentException>(() => new IntrinsicTriangulation(mesh));
            var geo = new MeshGeodesicDistance(mesh) { IntrinsicRefine = true };
            int src = NearestVertex(mesh, Vector3d.Zero);
            Assert.True(geo.Compute(new[] { src }));
            Assert.Equal(0, geo.IntrinsicFlips);
            Assert.Equal(IntrinsicRefinementStatus.NonManifoldInput, geo.Refinement.Status);
            double d = geo.Distance[NearestVertex(mesh, new Vector3d(20, 20, 0))];
            Assert.True(Math.Abs(d - 20 * Math.Sqrt(2)) < 0.05 * 20 * Math.Sqrt(2) + 1, $"{d}");
        }
    }
}
