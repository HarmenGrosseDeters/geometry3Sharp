using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// 2.5D dual contouring of terrain (eg open-pit mines): one vertex per quadtree cell, placed by a QEF of
    /// the input planes, so break lines stay sharp; flat regions merge into large cells.
    /// </summary>
    public class TerrainDualContouringTests
    {
        // regular grid over [x0,x1] x [y0,y1] with n x n vertices, z = h(x,y)
        static DMesh3 Heightfield(Func<double, double, double> h, double x0, double y0, double x1, double y1, int n)
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

        static DMesh3 Soup(DMesh3 m)
        {
            DMesh3 soup = new DMesh3();
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                soup.AppendTriangle(soup.AppendVertex(m.GetVertex(t.a)), soup.AppendVertex(m.GetVertex(t.b)), soup.AppendVertex(m.GetVertex(t.c)));
            }
            return soup;
        }

        // open pit: flat bottom (rectangle 2a x 2b), benches of slope + berm, flat ground; rotated about z
        static double Pit(double x, double y, double angleDeg = 17)
        {
            double c = Math.Cos(angleDeg * MathUtil.Deg2Rad), s = Math.Sin(angleDeg * MathUtil.Deg2Rad);
            double u = c * x + s * y, v = -s * x + c * y;
            double r = Math.Max(Math.Abs(u) - 30, Math.Abs(v) - 15);    // distance out of the bottom rectangle
            double z = -40;
            const double slope = 8, berm = 6, height = 10;             // bench: 8 m wide face rising 10 m, 6 m berm
            for (int bench = 0; bench < 4; ++bench) {
                if (r <= 0) return z;
                double face = Math.Min(r, slope);
                z += height * face / slope;
                r -= slope;
                if (r <= 0) return z;
                r -= berm;
            }
            return z;
        }

        // height of a mesh at (x,y) by a vertical ray, NaN if missed
        static double HeightAt(DMeshAABBTree3 spatial, double x, double y)
        {
            AxisAlignedBox3d b = spatial.Mesh.CachedBounds;
            Ray3d ray = new Ray3d(new Vector3d(x, y, b.Max.z + 1), -Vector3d.AxisZ);
            int tid = spatial.FindNearestHitTriangle(ray);
            if (tid < 0) return double.NaN;
            IntrRay3Triangle3 intr = MeshQueries.TriangleIntersection(spatial.Mesh, tid, ray);
            return intr.Find() ? ray.PointAt(intr.RayParameter).z : double.NaN;
        }

        static void AssertCleanTerrain(DMesh3 m)
        {
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Empty(MeshIterators.BowtieVertices(m));
            foreach (int tid in m.TriangleIndices())
                Assert.True(m.GetTriNormal(tid).z > 0, $"triangle {tid} faces down");
            var comps = new MeshConnectedComponents(m);
            comps.FindConnectedT();
            Assert.Equal(1, comps.Count);
        }

        static double MinDistanceXY(DMesh3 m, Vector2d p)
        {
            return m.VertexIndices().Min(v => m.GetVertex(v).xy.Distance(p));
        }


        [Fact]
        public void TiltedPlane_FewTriangles_ExactHeights_CornersKept()
        {
            Func<double, double, double> h = (x, y) => 0.3 * x - 0.2 * y + 5;
            DMesh3 input = Heightfield(h, 0, 0, 100, 60, 121);
            var tdc = new TerrainDualContouring(input) { GridResolution = 64 };
            Assert.True(tdc.Compute());
            DMesh3 result = tdc.Result;

            AssertCleanTerrain(result);
            // the boundary ring stays at the finest resolution (to keep the outline), the inside merges
            var uniform = new TerrainDualContouring(input) { GridResolution = 64, Adaptive = false };
            Assert.True(uniform.Compute());
            Assert.True(result.TriangleCount < 0.25 * uniform.Result.TriangleCount, $"{result.TriangleCount} vs {uniform.Result.TriangleCount} triangles");
            foreach (int vid in result.VertexIndices()) {
                Vector3d v = result.GetVertex(vid);
                Assert.True(Math.Abs(v.z - h(v.x, v.y)) < 1e-6, $"vertex {v} off the plane");
            }
            foreach (Vector2d c in new[] { new Vector2d(0, 0), new Vector2d(100, 0), new Vector2d(100, 60), new Vector2d(0, 60) })
                Assert.True(MinDistanceXY(result, c) < 1e-6, $"corner {c} lost");
            var loops = new MeshBoundaryLoops(result);
            Assert.Single(loops.Loops);
        }

        [Fact]
        public void OpenPit_BreakLinesStaySharp()
        {
            DMesh3 input = Heightfield((x, y) => Pit(x, y), -100, -100, 100, 100, 401);
            var inputSpatial = new DMeshAABBTree3(input, true);

            Func<bool, (double rms, double max, int tris)> run = (useQEF) => {
                var tdc = new TerrainDualContouring(input) { GridResolution = 100, UseQEF = useQEF };
                Assert.True(tdc.Compute());
                DMesh3 result = tdc.Result;
                AssertCleanTerrain(result);
                var spatial = new DMeshAABBTree3(result, true);
                var rnd = new Random(3);
                double sum = 0, max = 0; int n = 0;
                for (int i = 0; i < 20000; ++i) {
                    double x = rnd.NextDouble() * 190 - 95, y = rnd.NextDouble() * 190 - 95;
                    double d = Math.Abs(HeightAt(spatial, x, y) - HeightAt(inputSpatial, x, y));
                    Assert.False(double.IsNaN(d));
                    sum += d * d; max = Math.Max(max, d); n++;
                }
                return (Math.Sqrt(sum / n), max, result.TriangleCount);
            };
            var dc = run(true);
            var plain = run(false);
            // cell size 2 m: averaged vertices round the 10 m benches off, QEF vertices sit on the break lines
            Assert.True(dc.rms < 0.3 * plain.rms, $"rms {dc.rms:F3} vs {plain.rms:F3} without QEF");
            Assert.True(dc.max < 0.5, $"max error {dc.max:F3} m");
            Assert.True(dc.tris < 0.05 * input.TriangleCount, $"{dc.tris} triangles");
        }

        [Fact]
        public void MessyInput_StillCleanTerrain()
        {
            DMesh3 grid = Heightfield((x, y) => Pit(x, y), -100, -100, 100, 100, 201);
            // a hole of about 6 m
            foreach (int tid in grid.TriangleIndices().ToArray())
                if (grid.GetTriCentroid(tid).xy.Distance(new Vector2d(60, 60)) < 3)
                    grid.RemoveTriangle(tid, true, false);
            DMesh3 input = Soup(grid);
            var rnd = new Random(5);
            foreach (int tid in input.TriangleIndices().ToArray()) {
                if (rnd.NextDouble() < 0.2)
                    input.ReverseTriOrientation(tid);
                if (rnd.NextDouble() < 0.1) {      // overlapping duplicate slightly above
                    Index3i t = input.GetTriangle(tid);
                    Vector3d up = 1e-4 * Vector3d.AxisZ;
                    input.AppendTriangle(input.AppendVertex(input.GetVertex(t.a) + up), input.AppendVertex(input.GetVertex(t.b) + up), input.AppendVertex(input.GetVertex(t.c) + up));
                }
            }

            var tdc = new TerrainDualContouring(input) { GridResolution = 100 };
            Assert.True(tdc.Compute());
            DMesh3 result = tdc.Result;
            AssertCleanTerrain(result);
            var loops = new MeshBoundaryLoops(result);
            Assert.Equal(2, loops.Loops.Count);    // outer ring and the hole
        }

        [Fact]
        public void LShapedFootprint_OuterRingAndCornersKept()
        {
            DMesh3 input = Heightfield((x, y) => 0.05 * x + 0.02 * y, 0, 0, 80, 80, 81);
            foreach (int tid in input.TriangleIndices().ToArray()) {
                Vector3d c = input.GetTriCentroid(tid);
                if (c.x > 40 && c.y > 40)
                    input.RemoveTriangle(tid, true, false);
            }
            MeshTransforms.Rotate(input, Vector3d.Zero, Quaterniond.AxisAngleD(Vector3d.AxisZ, 30));
            var inputLoops = new MeshBoundaryLoops(input);
            Assert.Single(inputLoops.Loops);

            var tdc = new TerrainDualContouring(input) { GridResolution = 64 };
            Assert.True(tdc.Compute());
            DMesh3 result = tdc.Result;
            AssertCleanTerrain(result);

            var rot = Quaterniond.AxisAngleD(Vector3d.AxisZ, 30);
            foreach (Vector2d c in new[] { new Vector2d(0, 0), new Vector2d(80, 0), new Vector2d(80, 40), new Vector2d(40, 40), new Vector2d(40, 80), new Vector2d(0, 80) }) {
                Vector2d rc = (rot * new Vector3d(c.x, c.y, 0)).xy;
                Assert.True(MinDistanceXY(result, rc) < 1e-6, $"corner {c} lost");
            }
            // every boundary vertex of the result lies on the input boundary
            var boundary = inputLoops.Loops[0];
            var polygon = new Polygon2d(boundary.Vertices.Select(v => input.GetVertex(v).xy));
            foreach (int vid in result.VertexIndices())
                if (result.IsBoundaryVertex(vid))
                    Assert.True(Math.Sqrt(polygon.DistanceSquared(result.GetVertex(vid).xy)) < 1e-6);
        }

        [Fact]
        public void UtmCoordinates_SameAccuracy()
        {
            Vector3d offset = new Vector3d(632000, 5712000, 100);
            DMesh3 input = Heightfield((x, y) => Pit(x, y) + offset.z, offset.x - 100, offset.y - 100, offset.x + 100, offset.y + 100, 201);
            // Pit is centred at the origin: evaluate it in local coordinates
            foreach (int vid in input.VertexIndices()) {
                Vector3d v = input.GetVertex(vid);
                input.SetVertex(vid, new Vector3d(v.x, v.y, Pit(v.x - offset.x, v.y - offset.y) + offset.z));
            }
            var tdc = new TerrainDualContouring(input) { GridResolution = 100 };
            Assert.True(tdc.Compute());
            DMesh3 result = tdc.Result;
            AssertCleanTerrain(result);
            var spatial = new DMeshAABBTree3(result, true);
            var inputSpatial = new DMeshAABBTree3(input, true);
            var rnd = new Random(9);
            double max = 0;
            for (int i = 0; i < 5000; ++i) {
                double x = offset.x + rnd.NextDouble() * 190 - 95, y = offset.y + rnd.NextDouble() * 190 - 95;
                max = Math.Max(max, Math.Abs(HeightAt(spatial, x, y) - HeightAt(inputSpatial, x, y)));
            }
            Assert.True(max < 0.5, $"max error {max:F3} m");
        }
    }
}
