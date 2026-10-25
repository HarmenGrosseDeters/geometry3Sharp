using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Offset surfaces of a terrain at a perpendicular distance (safety layers, minimum cover).
    /// </summary>
    public class TerrainOffsetTests
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
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return mesh;
        }

        static double Distance(DMeshAABBTree3 s, Vector3d p) { int t = s.FindNearestTriangle(p); return Math.Sqrt(MeshQueries.TriDistanceSqr(s.Mesh, t, p)); }

        [Fact]
        public void TiltedPlane_OffsetIsParallelPlane()
        {
            Func<double, double, double> h = (x, y) => 0.3 * x - 0.2 * y;
            DMesh3 terrain = Grid(0, 0, 100, 100, 21, h);
            var off = new TerrainOffset(terrain, -5) { GridResolution = 50 };
            Assert.True(off.Compute());
            double shift = 5 * Math.Sqrt(1 + 0.3 * 0.3 + 0.2 * 0.2);     // perpendicular 5 m = vertical 5 * |n|/n_z
            foreach (int vid in off.Result.VertexIndices()) {
                Vector3d v = off.Result.GetVertex(vid);
                if (v.x < 10 || v.x > 90 || v.y < 10 || v.y > 90) continue;      // away from the terrain border
                Assert.True(Math.Abs(v.z - (h(v.x, v.y) - shift)) < 1e-6, $"{v}: {v.z} vs {h(v.x, v.y) - shift}");
            }
        }

        [Theory]
        [InlineData(5)]
        [InlineData(-5)]
        public void FlatSquare_FootprintIsKept(double d)
        {
            // the result ended where the cells did, up to a cell inside the terrain (x from 0.98 to 99.98,
            // y from 0.71 to 99.71 with 2 m cells); the terrain's boundary lines are kept now
            DMesh3 terrain = Grid(0, 0, 100, 100, 11, (x, y) => 0);
            var off = new TerrainOffset(terrain, d) { GridResolution = 50 };
            Assert.True(off.Compute());
            AxisAlignedBox3d b = off.Result.CachedBounds;
            Assert.Equal(0, b.Min.x, 6); Assert.Equal(0, b.Min.y, 6);
            Assert.Equal(100, b.Max.x, 6); Assert.Equal(100, b.Max.y, 6);
            foreach (int vid in off.Result.VertexIndices())     // (the bisection stops at 1e-6 (1 + |d|))
                Assert.True(Math.Abs(off.Result.GetVertex(vid).z - d) < 1e-5);
            double areaXY = off.Result.TriangleIndices().Sum(tid => Math.Abs(off.Result.GetTriNormal(tid).z) * off.Result.GetTriArea(tid));
            Assert.Equal(100 * 100, areaXY, 3);
        }

        [Fact]
        public void UtmCoordinates_SameResultAsAtTheOrigin()
        {
            // the same terrain at the origin and at UTM coordinates: the same offset surface (up to rounding)
            Vector3d utm = new Vector3d(632000, 5712000, 100);
            Func<double, double, double> h = (x, y) => Pit(x, y);
            DMesh3 local = Grid(-100, -100, 100, 100, 101, h);
            DMesh3 far = new DMesh3(local);
            MeshTransforms.Translate(far, utm);
            var a = new TerrainOffset(local, -5) { GridResolution = 50 };
            var b = new TerrainOffset(far, -5) { GridResolution = 50 };
            Assert.True(a.Compute() && b.Compute());
            var sa = new DMeshAABBTree3(a.Result, true);
            var sb = new DMeshAABBTree3(b.Result, true);
            var rnd = new Random(2);
            double maxDiff = 0;
            for (int i = 0; i < 2000; ++i) {
                double x = rnd.NextDouble() * 190 - 95, y = rnd.NextDouble() * 190 - 95;
                double za = HeightAt(sa, x, y), zb = HeightAt(sb, x + utm.x, y + utm.y) - utm.z;
                Assert.False(double.IsNaN(za) || double.IsNaN(zb), $"{x}, {y}");
                maxDiff = Math.Max(maxDiff, Math.Abs(za - zb));
            }
            Assert.True(maxDiff < 1e-3, $"{maxDiff} m between the origin and UTM results");
        }

        static double HeightAt(DMeshAABBTree3 s, double x, double y)
        {
            Ray3d ray = new Ray3d(new Vector3d(x, y, s.Mesh.CachedBounds.Max.z + 10), -Vector3d.AxisZ);
            int tid = s.FindNearestHitTriangle(ray);
            if (tid < 0) return double.NaN;
            var intr = MeshQueries.TriangleIntersection(s.Mesh, tid, ray);
            return intr.Find() ? ray.PointAt(intr.RayParameter).z : double.NaN;
        }

        [Theory]
        [InlineData(-5)]
        [InlineData(3)]
        public void OpenPit_OffsetAtDistance(double d)
        {
            DMesh3 terrain = Grid(-100, -100, 100, 100, 201, (x, y) => Pit(x, y));
            var off = new TerrainOffset(terrain, d) { GridResolution = 100 };
            Assert.True(off.Compute());
            DMesh3 result = off.Result;
            Assert.True(result.CheckValidity(false, FailMode.ReturnOnly));
            foreach (int tid in result.TriangleIndices())
                Assert.True(result.GetTriNormal(tid).z > 0);
            var ts = new DMeshAABBTree3(terrain, true);
            var rs = new DMeshAABBTree3(result, true);
            var rnd = new Random(1);
            int n = 0;
            for (int i = 0; i < 5000; ++i) {
                double x = rnd.NextDouble() * 160 - 80, y = rnd.NextDouble() * 160 - 80;
                Ray3d ray = new Ray3d(new Vector3d(x, y, 100), -Vector3d.AxisZ);
                int tid = rs.FindNearestHitTriangle(ray);
                Assert.True(tid >= 0);
                var intr = MeshQueries.TriangleIntersection(result, tid, ray);
                Assert.True(intr.Find());
                Vector3d p = ray.PointAt(intr.RayParameter);
                // on the right side of the terrain, at the distance. Around convex break lines the offset is
                // an arc of radius |d|, approximated by facets of the cell size: their chords lie up to
                // cell^2 / (8 |d|) inside it (2 m cells)
                Assert.True(Math.Sign(p.z - Pit(x, y)) == Math.Sign(d), $"{p} on the wrong side");
                double chord = 2.0 * 2.0 / (8 * Math.Abs(d));
                Assert.True(Math.Abs(Distance(ts, p) - Math.Abs(d)) < 0.02 * Math.Abs(d) + chord + 0.05, $"{p}: distance {Distance(ts, p)} instead of {Math.Abs(d)}");
                n++;
            }
            Assert.Equal(5000, n);
        }

        [Theory]
        [InlineData(0.1)]
        [InlineData(-0.1)]
        public void HighWall_SmallDistance_NoHoles(double d)
        {
            // a 20 m wall (one 1 m grid step): next to it the exit from the band is up to 20 m away
            DMesh3 terrain = Grid(0, 0, 100, 100, 101, (x, y) => x < 50 ? 0 : 20);
            var off = new TerrainOffset(terrain, d) { GridResolution = 100 };
            Assert.True(off.Compute());
            var loops = new MeshBoundaryLoops(off.Result);
            Assert.Single(loops.Loops);
            var rs = new DMeshAABBTree3(off.Result, true);
            for (double x = 2; x < 98; x += 0.7)
                for (double y = 5; y < 95; y += 9) {
                    Ray3d ray = new Ray3d(new Vector3d(x, y, 100), -Vector3d.AxisZ);
                    Assert.True(rs.FindNearestHitTriangle(ray) >= 0, $"hole at {x}, {y}");
                }
        }

        static double Pit(double x, double y)
        {
            double c = Math.Cos(17 * MathUtil.Deg2Rad), s = Math.Sin(17 * MathUtil.Deg2Rad);
            double u = c * x + s * y, v = -s * x + c * y;
            double r = Math.Max(Math.Abs(u) - 30, Math.Abs(v) - 15);
            double z = -40;
            for (int bench = 0; bench < 4; ++bench) {
                if (r <= 0) return z;
                double face = Math.Min(r, 8);
                z += 10 * face / 8;
                r -= 8;
                if (r <= 0) return z;
                r -= 6;
            }
            return z;
        }
    }
}
