using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Geometric quality of the dual contouring results: sharp features off the grid axes, triangle
    /// count, self-intersections (from the quality review).
    /// </summary>
    public class ContouringQualityTests
    {
        static DMesh3 Box(Vector3d min, Vector3d max)
        {
            return new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();
        }

        static double Volume(DMesh3 m)
        {
            Vector3d c = m.CachedBounds.Center;
            return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), (v) => m.GetVertex(v) - c).x;
        }

        static DMesh3 RotatedBox(out Vector3d[] corners)
        {
            DMesh3 box = Box(Vector3d.Zero, new Vector3d(10, 6, 4));
            var q = Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 27);
            MeshTransforms.Rotate(box, new Vector3d(5, 3, 2), q);
            corners = new Vector3d[8];
            for (int k = 0; k < 8; ++k)
                corners[k] = new Vector3d(5, 3, 2) + q * (new Vector3d((k & 1) * 10, ((k >> 1) & 1) * 6, ((k >> 2) & 1) * 4) - new Vector3d(5, 3, 2));
            return box;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RotatedBox_SharpCornersAndExactVolume(bool adaptive)
        {
            Vector3d[] corners;
            DMesh3 box = RotatedBox(out corners);
            double cell = 0.25;
            var s = new MeshWindingNumberSolidify(box) { CellSize = cell, Adaptive = adaptive };
            Assert.True(s.Compute());
            DMesh3 r = s.Result;
            foreach (Vector3d c in corners) {
                double d = r.VertexIndices().Min(v => r.GetVertex(v).Distance(c));
                Assert.True(d < 0.02 * cell, $"corner {c} missed by {d / cell:F3} cells");
            }
            Assert.InRange(Volume(r), 240 * 0.999, 240 * 1.001);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(r));
            // the vertices on the input surface (edges and corners reproduced): all but the few moved to
            // resolve folds at the edges (were 0.12 cells off at about 200 vertices with clamping)
            var tree = new DMeshAABBTree3(box, true);
            var dev = r.VertexIndices().Select(v => Math.Sqrt(MeshQueries.TriDistanceSqr(box, tree.FindNearestTriangle(r.GetVertex(v)), r.GetVertex(v)))).ToList();
            Assert.True(dev.Max() < 0.2 * cell, $"vertex {dev.Max() / cell:F3} cells off the input");
            Assert.True(dev.Count(d => d > 0.02 * cell) <= 20, $"{dev.Count(d => d > 0.02 * cell)} of {dev.Count} vertices off the input");
            Assert.True(r.TriangleCount < 1000, $"{r.TriangleCount} triangles");
        }

        [Theory]
        [InlineData(0.05)]
        [InlineData(0.2)]
        [InlineData(1.0)]
        public void PostProcessReduction_NoNewSelfIntersections(double tol)
        {
            // a thin wavy slab, finely tessellated: reduction can push the two sides through each other;
            // the post-processing then reduces again with every collapse checked
            DMesh3 slab = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(5, 5, 0.02)), EdgeVertices = 40 }.Generate().MakeDMesh();
            foreach (int vid in slab.VertexIndices().ToArray()) {
                Vector3d v = slab.GetVertex(vid);
                slab.SetVertex(vid, new Vector3d(v.x, v.y, v.z + 0.3 * Math.Sin(1.3 * v.x) * Math.Cos(1.1 * v.y)));
            }
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(slab));
            DMesh3 result = WindingContouring.PostProcess(new DMesh3(slab), false, tol, 1.0, () => false, (name) => { });
            Assert.True(result.TriangleCount < slab.TriangleCount);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(result));
        }
    
        // open, non-orientable: the winding number jumps across the strip, sheets of the contour pass close
        // to each other in few cells (no sharp features)
        static DMesh3 MoebiusStrip(int n = 200, int w = 6)
        {
            DMesh3 m = new DMesh3();
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < w; ++j) {
                    double u = 2 * Math.PI * i / n, v = -0.4 + 0.8 * j / (w - 1), r = 1 + v * Math.Cos(u / 2);
                    m.AppendVertex(new Vector3d(r * Math.Cos(u), r * Math.Sin(u), v * Math.Sin(u / 2)));
                }
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < w - 1; ++j) {
                    int i1 = (i + 1) % n;
                    int a = i * w + j, b = i * w + j + 1, c = i1 * w + j, d = i1 * w + j + 1;
                    if (i1 == 0) { c = w - 1 - j; d = w - 2 - j; }     // the half twist
                    m.AppendTriangle(a, c, d);
                    m.AppendTriangle(a, d, b);
                }
            return m;
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.05)]
        public void NonOrientableInput_NoSelfIntersections(double reduceTol)
        {
            // (233 intersecting pairs when only triangles around feature vertices were checked)
            var solid = new MeshWindingNumberSolidify(MoebiusStrip()) { GridResolution = 64, Adaptive = true, ReduceTolerance = reduceTol };
            Assert.True(solid.Compute());
            Assert.True(solid.Result.TriangleCount > 1000);
            Assert.True(solid.Result.IsClosed());
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(solid.Result));
        }

        // thin plates (about a cell thick at resolution 64), randomly rotated, overlapping: at ambiguous faces
        // both cells often have a single patch through both surface segments
        static DMesh3 RandomPlates(int seed)
        {
            var rnd = new Random(seed);
            DMesh3 m = new DMesh3();
            for (int i = 0; i < 12; ++i) {
                var c = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                var e = new Vector3d(0.3 + 0.2 * rnd.NextDouble(), 0.02 + 0.03 * rnd.NextDouble(), 0.3);
                var b = new TrivialBox3Generator() { Box = new Box3d(c, e) }.Generate().MakeDMesh();
                var axis = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
                MeshTransforms.Rotate(b, c, Quaterniond.AxisAngleD(axis, 360 * rnd.NextDouble()));
                MeshEditor.Append(m, b);
            }
            return m;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(8)]
        public void AmbiguousFaces_RefinedInsteadOfLeftOut(int seed)
        {
            // the polygons that would create non-manifold edges were left out and the holes filled (4-8
            // non-manifold triangles, up to 36 self-intersections); refining around them separates the surfaces
            var solid = new MeshWindingNumberSolidify(RandomPlates(seed)) { GridResolution = 64, Adaptive = true, ReduceTolerance = 0 };
            Assert.True(solid.Compute());
            Assert.True(solid.NonManifoldRefinements > 0);
            Assert.Equal(0, solid.NonManifoldTriangles);
            Assert.True(solid.Result.IsClosed());
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(solid.Result));
        }
}
}
