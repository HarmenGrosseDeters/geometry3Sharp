using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Exact cut and fill volumes between two terrain surfaces (TINs), eg the survey before and after
    /// excavation. Both surfaces are piecewise linear, so the volumes are exact up to rounding.
    /// </summary>
    public class TerrainVolumeTests
    {
        // grid over [x0,x1] x [y0,y1] with n x n vertices, z = h(x,y); flip = other diagonal
        static DMesh3 Grid(double x0, double y0, double x1, double y1, int n, Func<double, double, double> h, bool flip = false)
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
                    if (flip) {
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i, j + 1]);
                        mesh.AppendTriangle(vid[i + 1, j], vid[i + 1, j + 1], vid[i, j + 1]);
                    } else {
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                        mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                    }
                }
            return mesh;
        }

        static TerrainVolumeDifference Run(DMesh3 before, DMesh3 after)
        {
            var v = new TerrainVolumeDifference(before, after);
            Assert.True(v.Compute());
            return v;
        }

        [Fact]
        public void ParallelSurfaces_DifferentTriangulations()
        {
            DMesh3 before = Grid(0, 0, 10, 10, 11, (x, y) => 0.1 * x + 0.05 * y);
            DMesh3 after = Grid(0, 0, 10, 10, 7, (x, y) => 0.1 * x + 0.05 * y + 2, true);
            var v = Run(before, after);
            Assert.Equal(200, v.FillVolume, 6);
            Assert.Equal(0, v.CutVolume, 6);
            Assert.Equal(100, v.OverlapArea, 6);
        }

        [Fact]
        public void CrossingSurfaces_CutAndFillSplitAtTheZeroLine()
        {
            // before z = 0, after z = x over [-1,1]^2: fill = cut = 1
            DMesh3 before = Grid(-1, -1, 1, 1, 2, (x, y) => 0);
            DMesh3 after = Grid(-1, -1, 1, 1, 5, (x, y) => x, true);
            var v = Run(before, after);
            Assert.Equal(1, v.FillVolume, 9);
            Assert.Equal(1, v.CutVolume, 9);
            Assert.Equal(0, v.NetVolume, 9);
        }

        [Fact]
        public void OpenPit_CutVolumeExact()
        {
            // after: an excavated pit (below the original ground everywhere); before: flat ground, coarse TIN
            DMesh3 after = Grid(-100, -100, 100, 100, 201, (x, y) => Pit(x, y));
            DMesh3 before = Grid(-100, -100, 100, 100, 3, (x, y) => 0);
            double expected = after.TriangleIndices().Sum(t => {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                after.GetTriVertices(t, ref a, ref b, ref c);
                double area = 0.5 * Math.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                return area * -(a.z + b.z + c.z) / 3;      // exact integral of the linear depth
            });
            var v = Run(before, after);
            Assert.InRange(v.CutVolume, expected * (1 - 1e-9), expected * (1 + 1e-9));
            Assert.Equal(0, v.FillVolume, 6);
        }

        [Fact]
        public void PartialOverlap_OnlyCommonFootprintCounts()
        {
            DMesh3 before = Grid(0, 0, 10, 10, 6, (x, y) => 0);
            DMesh3 after = Grid(5, 0, 15, 10, 4, (x, y) => 1);
            var v = Run(before, after);
            Assert.Equal(50, v.OverlapArea, 6);
            Assert.Equal(50, v.FillVolume, 6);
        }

        [Fact]
        public void UtmCoordinates_SameResult()
        {
            Vector3d off = new Vector3d(632000, 5712000, 100);
            DMesh3 before = Grid(off.x - 1, off.y - 1, off.x + 1, off.y + 1, 2, (x, y) => off.z);
            DMesh3 after = Grid(off.x - 1, off.y - 1, off.x + 1, off.y + 1, 5, (x, y) => off.z + (x - off.x), true);
            var v = Run(before, after);
            Assert.Equal(1, v.FillVolume, 7);
            Assert.Equal(1, v.CutVolume, 7);
        }

        [Fact]
        public void TriangleSoupAndMixedOrientation()
        {
            DMesh3 before = Grid(0, 0, 10, 10, 11, (x, y) => 0);
            DMesh3 after = Grid(0, 0, 10, 10, 9, (x, y) => -3);
            // unweld and flip some triangles of after
            DMesh3 soup = new DMesh3();
            int k = 0;
            foreach (int tid in after.TriangleIndices()) {
                Index3i t = after.GetTriangle(tid);
                int a = soup.AppendVertex(after.GetVertex(t.a)), b = soup.AppendVertex(after.GetVertex(t.b)), c = soup.AppendVertex(after.GetVertex(t.c));
                if (k++ % 3 == 0) soup.AppendTriangle(a, c, b); else soup.AppendTriangle(a, b, c);
            }
            var v = Run(before, soup);
            Assert.Equal(300, v.CutVolume, 6);
        }

        // open pit as in TerrainDualContouringTests: flat bottom, four benches (10 m face over 8 m, 6 m berm)
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
