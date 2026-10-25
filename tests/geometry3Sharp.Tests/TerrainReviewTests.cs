using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Defects and limits found in the quality review of the terrain classes (safety lines, point cloud
    /// contouring, volumes, ground filter).
    /// </summary>
    public class TerrainReviewTests
    {
        static DMesh3 Grid(Func<double, double, double> h, double x0, double y0, double x1, double y1, int nx, int ny = -1)
        {
            if (ny < 0) ny = nx;
            DMesh3 mesh = new DMesh3();
            int[,] vid = new int[nx, ny];
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    double x = x0 + (x1 - x0) * i / (nx - 1), y = y0 + (y1 - y0) * j / (ny - 1);
                    vid[i, j] = mesh.AppendVertex(new Vector3d(x, y, h(x, y)));
                }
            for (int j = 0; j + 1 < ny; ++j)
                for (int i = 0; i + 1 < nx; ++i) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return mesh;
        }

        static double R(double x, double y)
        {
            double c = Math.Cos(17 * MathUtil.Deg2Rad), s = Math.Sin(17 * MathUtil.Deg2Rad);
            double u = c * x + s * y, v = -s * x + c * y;
            return Math.Max(Math.Abs(u) - 30, Math.Abs(v) - 15);
        }
        static double Pit(double x, double y, double face = 8, double berm = 6)
        {
            double r = R(x, y), z = -40;
            for (int bench = 0; bench < 4; ++bench) {
                if (r <= 0) return z;
                z += 10 * Math.Min(r, face) / face;
                r -= face;
                if (r <= 0) return z;
                r -= berm;
            }
            return z;
        }

        static double HeightAt(DMeshAABBTree3 spatial, double x, double y)
        {
            AxisAlignedBox3d b = spatial.Mesh.CachedBounds;
            Ray3d ray = new Ray3d(new Vector3d(x, y, b.Max.z + 1), -Vector3d.AxisZ);
            int tid = spatial.FindNearestHitTriangle(ray);
            if (tid < 0) return double.NaN;
            IntrRay3Triangle3 intr = MeshQueries.TriangleIntersection(spatial.Mesh, tid, ray);
            return intr.Find() ? ray.PointAt(intr.RayParameter).z : double.NaN;
        }

        // the iso line had junctions (inner corners) where the chaining dropped edges: gaps in the line
        [Fact]
        public void SafetyLinesHorizontal_NoGapsInTheLine()
        {
            var pit = Grid((x, y) => Pit(x, y, 2.68), -100, -100, 100, 100, 201);
            var src = new List<Vector3d> { new Vector3d(-60, -40, 0), new Vector3d(60, -40, 0), new Vector3d(60, 40, 0) };
            double D = 7.5;
            var sl = new TerrainSafetyLines(pit, new[] { src }, D) { Tolerance = 0.01 };
            Assert.True(sl.Compute());
            var ls = sl.Lines.SelectMany(l => Enumerable.Range(0, l.Count - 1).Select(i => new Segment2d(l[i].xy, l[i + 1].xy))).ToList();
            double worst = 0;
            for (double a = -90; a <= 0; a += 0.25) {
                var q = new Vector2d(60, -40) + D * new Vector2d(Math.Cos(a * MathUtil.Deg2Rad), Math.Sin(a * MathUtil.Deg2Rad));
                worst = Math.Max(worst, ls.Min(s => Math.Sqrt(s.DistanceSquared(q))));
            }
            Assert.True(worst < 0.02, $"gap {worst:F3} m");
            // and every iso edge is in exactly one line
            int edges = sl.Lines.Sum(l => l.Count - 1);
            Assert.True(edges > 0);
        }

        // merged leaves next to the edge of a point cloud lost the triangles to the boundary
        [Fact]
        public void PointCloudDC_FlatSquare_NoLostArea()
        {
            var pts = new List<Vector3d>();
            for (double x = 0; x <= 100; x += 0.3)
                for (double y = 0; y <= 100; y += 0.3)
                    pts.Add(new Vector3d(x, y, 0.01 * x));
            var tdc = new TerrainDualContouring(pts) { CellSize = 1.0 };
            Assert.True(tdc.Compute());
            var sp = new DMeshAABBTree3(tdc.Result, true);
            int miss = 0, n = 0;
            for (double x = 2; x < 98; x += 0.5)
                for (double y = 2; y < 98; y += 0.5) {
                    n++;
                    if (double.IsNaN(HeightAt(sp, x, y))) miss++;
                }
            Assert.True(miss == 0, $"{100.0 * miss / n:F1}% not covered");
            Assert.True(tdc.LeafCells < 0.5 * tdc.FinestCells, "still adaptive inside");
        }

        // the same with a gap in the cloud (eg water without returns)
        [Fact]
        public void PointCloudDC_GapInTheCloud_AreaKeptAroundIt()
        {
            var pts = new List<Vector3d>();
            for (double x = 0; x <= 100; x += 0.3)
                for (double y = 0; y <= 100; y += 0.3)
                    if (Math.Abs(x - 50) > 6 || Math.Abs(y - 50) > 6) pts.Add(new Vector3d(x, y, 0.01 * x));
            var tdc = new TerrainDualContouring(pts) { CellSize = 1.0 };
            Assert.True(tdc.Compute());
            var sp = new DMeshAABBTree3(tdc.Result, true);
            int miss = 0, n = 0;
            for (double x = 2; x < 98; x += 0.5)
                for (double y = 2; y < 98; y += 0.5) {
                    if (Math.Abs(x - 50) < 8 && Math.Abs(y - 50) < 8) continue;
                    n++;
                    if (double.IsNaN(HeightAt(sp, x, y))) miss++;
                }
            Assert.True(miss == 0, $"{100.0 * miss / n:F1}% not covered");
            Assert.True(double.IsNaN(HeightAt(sp, 50, 50)), "the gap is not filled");
        }

        // two survey files merged with an overlap: the overlap would be counted twice
        [Fact]
        public void Volume_OverlappingTriangles_AreRejected()
        {
            DMesh3 before = Grid((x, y) => 0, 0, 0, 10, 10, 11);
            DMesh3 after = Grid((x, y) => -2, 0, 0, 10, 10, 11);
            MeshEditor.Append(after, Grid((x, y) => -2, 5, 0, 10, 10, 6));     // a second file, half overlapping
            var v = new TerrainVolumeDifference(before, after);
            Assert.False(v.Compute());
            Assert.True(v.AfterOverlaps > 0);
            Assert.Equal(0, v.BeforeOverlaps);
            // a clean pair passes, neighbours sharing edges (and soups) are not overlaps
            var ok = new TerrainVolumeDifference(before, Grid((x, y) => -2, 0, 0, 10, 10, 7));
            Assert.True(ok.Compute());
            Assert.Equal(200, ok.CutVolume, 6);
            Assert.Equal(0, TerrainVolumeDifference.XYOverlaps(Grid((x, y) => Math.Sin(x) * y, -50, -50, 50, 50, 41)));
        }

        // along the surface, the heat distance overestimates near the crest, so the line was closer to the
        // crest than the true distance (the unsafe side)
        [Theory]
        [InlineData(15.0)]
        [InlineData(5.0)]
        public void SafetyLineAlongSurface_NotCloserThanTheTrueDistance(double D)
        {
            // crest at x = 0, a 75 degree face 10 m high, then a berm
            double fw = 10 / Math.Tan(75 * MathUtil.Deg2Rad);
            Func<double, double, double> h = (x, y) => x <= 0 ? 0 : (x >= fw ? -10 : -10 * x / fw);
            DMesh3 t = new DMesh3();
            double[] xs = Enumerable.Range(0, 41).Select(i => -10 + 0.5 * i).Where(x => x < 0).Concat(new[] { 0.0, fw / 2, fw })
                .Concat(Enumerable.Range(0, 60).Select(i => fw + 0.5 * (i + 1))).ToArray();
            int ny = 81;
            int[,] vid = new int[xs.Length, ny];
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < xs.Length; ++i) vid[i, j] = t.AppendVertex(new Vector3d(xs[i], -20 + 0.5 * j, h(xs[i], 0)));
            for (int j = 0; j + 1 < ny; ++j)
                for (int i = 0; i + 1 < xs.Length; ++i) {
                    t.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    t.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            var crest = new List<Vector3d> { new Vector3d(0, -20, 0), new Vector3d(0, 20, 0) };
            var sl = new TerrainSafetyLines(t, new[] { crest }, D) { Mode = TerrainSafetyLines.Modes.AlongSurface };
            Assert.True(sl.Compute());
            double faceLen = Math.Sqrt(fw * fw + 100);
            double exact = (D <= faceLen) ? D * fw / faceLen : fw + (D - faceLen);
            int n = 0;
            foreach (var line in sl.Lines)
                foreach (Vector3d p in line) {
                    if (p.x < 0.5 || Math.Abs(p.y) > 12) continue;
                    Assert.True(p.x >= exact - 0.02, $"{p}: {exact - p.x:F3} m closer to the crest than {D} m along the surface");
                    Assert.True(p.x <= exact + 0.05 * D + 0.5, $"{p}: too far ({p.x - exact:F3} m)");
                    n++;
                }
            Assert.True(n > 20);
        }
    }
}
