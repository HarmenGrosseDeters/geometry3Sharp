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
            Assert.DoesNotContain(m.VertexIndices(), v => m.GetVtxEdgeCount(v) == 0);    // no unused vertices
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

        // ground points of the pit: jittered grid, noise
        [Fact]
        public void IsolatedVertexAtTheOrigin_DoesNotChangeTheGrid()
        {
            Vector3d utm = new Vector3d(632000, 5712000, 100);
            DMesh3 plane = Heightfield((x, y) => 0.1 * x, 0, 0, 100, 100, 21);
            MeshTransforms.Translate(plane, utm);
            var a = new TerrainDualContouring(plane) { GridResolution = 50 };
            Assert.True(a.Compute());
            DMesh3 withOrphan = new DMesh3(plane);
            withOrphan.AppendVertex(Vector3d.Zero);      // eg a broken import
            var b = new TerrainDualContouring(withOrphan) { GridResolution = 50 };
            Assert.True(b.Compute());
            Assert.Equal(a.Result.TriangleCount, b.Result.TriangleCount);
            Assert.Equal(a.FinestCells, b.FinestCells);
        }

        [Fact]
        public void InvalidParameters_Fail()
        {
            DMesh3 plane = Heightfield((x, y) => 0.1 * x, 0, 0, 10, 10, 11);
            Assert.False(new TerrainDualContouring(plane) { SamplesPerCell = 0 }.Compute());
            Assert.False(new TerrainDualContouring(plane) { CellSize = 1e-6 }.Compute());      // 1e14 cells
            // larger than the terrain: no cell has four sampled neighbours
            var big = new TerrainDualContouring(plane) { CellSize = 100 };
            Assert.True(big.Compute() == false || big.Result.TriangleCount > 0);
        }

        [Fact]
        public void PointCloud_NonFinitePointsAreSkipped()
        {
            var points = PitPoints(1.0, 0.02, Vector3d.Zero);
            var tdc0 = new TerrainDualContouring(points) { CellSize = 2 };
            Assert.True(tdc0.Compute());
            points.Add(new Vector3d(double.NaN, 5, 5));
            points.Add(new Vector3d(5, double.PositiveInfinity, 5));
            var tdc = new TerrainDualContouring(points) { CellSize = 2 };
            Assert.True(tdc.Compute());
            Assert.Equal(tdc0.Result.TriangleCount, tdc.Result.TriangleCount);
        }

        static List<Vector3d> PitPoints(double spacing, double noise, Vector3d offset, int seed = 1)
        {
            var rnd = new Random(seed);
            var pts = new List<Vector3d>();
            for (double x = -100; x <= 100; x += spacing)
                for (double y = -100; y <= 100; y += spacing) {
                    double px = x + (rnd.NextDouble() - 0.5) * spacing, py = y + (rnd.NextDouble() - 0.5) * spacing;
                    pts.Add(offset + new Vector3d(px, py, Pit(px, py) + noise * (rnd.NextDouble() - 0.5)));
                }
            return pts;
        }

        static (double rms, double p99) ErrorToPit(DMesh3 result, Vector3d offset)
        {
            var spatial = new DMeshAABBTree3(result, true);
            var rnd = new Random(3);
            var errs = new List<double>();
            for (int i = 0; i < 20000; ++i) {
                double x = rnd.NextDouble() * 180 - 90, y = rnd.NextDouble() * 180 - 90;
                double z = HeightAt(spatial, offset.x + x, offset.y + y);
                Assert.False(double.IsNaN(z));
                errs.Add(Math.Abs(z - offset.z - Pit(x, y)));
            }
            errs.Sort();
            return (Math.Sqrt(errs.Average(e => e * e)), errs[(int)(0.99 * errs.Count)]);
        }

        [Fact]
        public void PointCloud_OpenPit_BreakLinesStaySharp()
        {
            List<Vector3d> points = PitPoints(0.5, 0.02, Vector3d.Zero);
            Func<bool, (double, double, int)> run = (qef) => {
                var tdc = new TerrainDualContouring(points) { GridResolution = 100, UseQEF = qef };
                Assert.True(tdc.Compute());
                AssertCleanTerrain(tdc.Result);
                var e = ErrorToPit(tdc.Result, Vector3d.Zero);
                return (e.Item1, e.Item2, tdc.Result.TriangleCount);
            };
            var dc = run(true);
            var plain = run(false);
            Assert.True(dc.Item1 < 0.5 * plain.Item1, $"rms {dc.Item1:F3} vs {plain.Item1:F3} without QEF");
            Assert.True(dc.Item2 < 0.5, $"p99 error {dc.Item2:F3} m");
            Assert.True(dc.Item3 < 0.1 * points.Count, $"{dc.Item3} triangles for {points.Count} points");
        }

        [Fact]
        public void PointCloud_UtmCoordinates()
        {
            Vector3d offset = new Vector3d(632000, 5712000, 100);
            var tdc = new TerrainDualContouring(PitPoints(0.5, 0.02, offset)) { GridResolution = 100 };
            Assert.True(tdc.Compute());
            AssertCleanTerrain(tdc.Result);
            Assert.True(ErrorToPit(tdc.Result, offset).Item1 < 0.1);
        }

        [Fact]
        public void PointCloud_GroundFilterThenTerrain()
        {
            // LiDAR survey of the pit with trucks on the floor: filter the ground, then build the terrain
            List<Vector3d> points = PitPoints(0.5, 0.02, Vector3d.Zero, 5);
            var rnd = new Random(9);
            for (int t = 0; t < 4; ++t) {
                double cx = -20 + 12 * t, cy = rnd.NextDouble() * 10 - 5;      // on the pit floor (|u|<30, |v|<15)
                for (double x = cx - 3; x <= cx + 3; x += 0.5)
                    for (double y = cy - 2; y <= cy + 2; y += 0.5)
                        points.Add(new Vector3d(x, y, Pit(x, y) + 4));
            }
            var csf = new ClothSimulationFilter(points) { ClothResolution = 1.0 };
            Assert.True(csf.Compute());
            var ground = points.Where((p, i) => csf.IsGround[i]).ToList();
            var tdc = new TerrainDualContouring(ground) { GridResolution = 100 };
            Assert.True(tdc.Compute());
            AssertCleanTerrain(tdc.Result);
            Assert.True(ErrorToPit(tdc.Result, Vector3d.Zero).Item2 < 0.5, "trucks left in the terrain");
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
        [Fact]
        public void MergedCells_NoOverlappingTrianglesInXY()
        {
            // rough polyhedral terrain (random planes, 17 cm noise): a merged cell's vertex sat far from
            // the corners its dual triangles fan around and covered a neighbour's vertex. 5 overlapping
            // triangle pairs here, 24 on 12 km^2 of a real open pit: not a height field, so
            // TerrainVolumeDifference refused the result
            var rnd = new Random(167);
            var planes2 = Enumerable.Range(0, 15).Select(_ => new Vector3d(rnd.NextDouble() * 1.6 - 0.8, rnd.NextDouble() * 1.6 - 0.8, rnd.NextDouble() * 40)).ToList();
            var planes = Enumerable.Range(0, 25).Select(_ => new Vector3d(rnd.NextDouble() * 1.2 - 0.6, rnd.NextDouble() * 1.2 - 0.6, rnd.NextDouble() * 40)).ToList();
            double noise = rnd.NextDouble() * 0.4;
            DMesh3 mesh = Heightfield((x, y) => planes.Max(p => p.x * (x - 60) + p.y * (y - 60) + p.z) + planes2.Min(p => p.x * (x - 60) + p.y * (y - 60) + p.z)
                + noise * (rnd.NextDouble() - 0.5), 0, 0, 120, 120, 121);

            var tdc = new TerrainDualContouring(mesh) { CellSize = 1, Tolerance = 0.2 };
            Assert.True(tdc.Compute());
            Assert.Equal(0, TerrainVolumeDifference.XYOverlaps(tdc.Result));
            Assert.True(tdc.SplitLeaves > 0);
            Assert.True(tdc.Result.TriangleCount < mesh.TriangleCount / 2);
            // usable for volumes against the input
            var vd = new TerrainVolumeDifference(mesh, tdc.Result);
            Assert.True(vd.Compute());
            Assert.True(Math.Abs(vd.NetVolume) < 0.05 * vd.OverlapArea, $"net {vd.NetVolume} over {vd.OverlapArea}");
        }
        [Fact]
        public void MaxError_SplitsMergedCellsWithFarSamples()
        {
            // Tolerance is an RMS: single samples of a merged cell can lie several times farther. With
            // MaxError the merged cells with a sample beyond it are split again
            var rnd = new Random(167);
            var planes2 = Enumerable.Range(0, 15).Select(_ => new Vector3d(rnd.NextDouble() * 1.6 - 0.8, rnd.NextDouble() * 1.6 - 0.8, rnd.NextDouble() * 40)).ToList();
            var planes = Enumerable.Range(0, 25).Select(_ => new Vector3d(rnd.NextDouble() * 1.2 - 0.6, rnd.NextDouble() * 1.2 - 0.6, rnd.NextDouble() * 40)).ToList();
            double noise = rnd.NextDouble() * 0.4;
            DMesh3 mesh = Heightfield((x, y) => planes.Max(p => p.x * (x - 60) + p.y * (y - 60) + p.z) + planes2.Min(p => p.x * (x - 60) + p.y * (y - 60) + p.z)
                + noise * (rnd.NextDouble() - 0.5), 0, 0, 120, 120, 121);

            var rms = new TerrainDualContouring(mesh) { CellSize = 1, Tolerance = 0.3, MaxError = 1e6 };
            Assert.True(rms.Compute());
            var max = new TerrainDualContouring(mesh) { CellSize = 1, Tolerance = 0.3, MaxError = 0.2 };
            Assert.True(max.Compute());
            Assert.True(rms.MaxSampleError > 0.3, $"RMS tolerance only: {rms.MaxSampleError}");
            Assert.True(max.SplitLeaves > rms.SplitLeaves);
            Assert.True(max.Result.TriangleCount > rms.Result.TriangleCount);
            // left above it: only samples in finest cells (which cannot be split)
            Assert.True(max.MaxErrorViolations < 0.01 * 121 * 121 * 16, $"{max.MaxErrorViolations} samples above, max {max.MaxSampleError}");
            Assert.Equal(0, TerrainVolumeDifference.XYOverlaps(max.Result));
        }
    }
}
