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
    }
}
