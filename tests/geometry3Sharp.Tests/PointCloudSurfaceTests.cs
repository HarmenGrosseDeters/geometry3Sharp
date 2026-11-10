using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// PointCloudSurface (step C1): the gate of the owner decision (2026-11-10), on procedural torus samplings with known surface:
    /// (a) the mean distance to the true surface (mean of both directions) is smaller than that of the alpha wrap,
    /// (b) the largest distance is within one cube size of the wrap's, (c) a hole is detected and the wrap is used there,
    /// (d) the winding number is evaluated only near the data; plus closed / manifold / finite, invariance, reuse, determinism.
    /// </summary>
    public class PointCloudSurfaceTests
    {
        static double TorusDistance(Vector3d p)
        {
            double q = Math.Sqrt(p.x * p.x + p.y * p.y) - PointClouds.TorusR;
            return Math.Abs(Math.Sqrt(q * q + p.z * p.z) - PointClouds.Torusr);
        }

        /// <summary> mean of the two directional means, and the larger of the two directional maxima, of mesh and the true torus </summary>
        internal static void Deviation(DMesh3 mesh, out double mean, out double max)
        {
            double sum = 0, maxA = 0;
            foreach (int vid in mesh.VertexIndices()) {
                double d = TorusDistance(mesh.GetVertex(vid));
                sum += d; maxA = Math.Max(maxA, d);
            }
            double meanA = sum / mesh.VertexCount;
            var tree = new DMeshAABBTree3(mesh, true);
            var rnd = new Random(3);
            double sumB = 0, maxB = 0;
            int count = 1500;
            for (int i = 0; i < count; ++i) {
                double u = rnd.NextDouble() * 2 * Math.PI, v = rnd.NextDouble() * 2 * Math.PI;
                var p = new Vector3d((PointClouds.TorusR + PointClouds.Torusr * Math.Cos(v)) * Math.Cos(u),
                    (PointClouds.TorusR + PointClouds.Torusr * Math.Cos(v)) * Math.Sin(u), PointClouds.Torusr * Math.Sin(v));
                int tid = tree.FindNearestTriangle(p);
                Triangle3d tri = new Triangle3d();
                mesh.GetTriVertices(tid, ref tri.V0, ref tri.V1, ref tri.V2);
                double d = Math.Sqrt(new DistPoint3Triangle3(p, tri).GetSquared());
                sumB += d; maxB = Math.Max(maxB, d);
            }
            mean = 0.5 * (meanA + sumB / count);
            max = Math.Max(maxA, maxB);
        }

        public static IEnumerable<object[]> Scenes() => new[] {
            new object[] { "clean", 0.0, false, false },
            new object[] { "noise", 0.08, false, false },
            new object[] { "hole", 0.0, true, false },
            new object[] { "non-uniform", 0.0, false, true },
            new object[] { "noise+hole+non-uniform", 0.08, true, true },
        };

        [Theory]
        [MemberData(nameof(Scenes))]
        public void Gate_MeanBetterThanTheWrap_MaxWithinOneCube(string name, double sigma, bool hole, bool nonUniform)
        {
            var pts = PointClouds.Torus(2500, 7, out _, sigma, hole, nonUniform);
            var surface = new PointCloudSurface(pts);
            Assert.True(surface.Compute(), $"{name}: {surface.Status} {surface.Error}");
            Assert.Equal(PointCloudSurface.SurfaceStatus.Success, surface.Status);
            ResultChecks.AssertClosed(surface.Result, name);
            Deviation(surface.Result, out double mean, out double max);
            Deviation(surface.NormalsStage.Wrap, out double wrapMean, out double wrapMax);
            Assert.True(mean < wrapMean, $"{name}: mean {mean:F4} against the wrap's {wrapMean:F4}");
            Assert.True(max <= wrapMax + surface.UsedCubeSize, $"{name}: max {max:F3} against the wrap's {wrapMax:F3} + cube {surface.UsedCubeSize:F3}");
            // (d) the winding number is evaluated near the data only: not at every node of the grid (the nodes are asked for up to 4 times)
            Assert.True(surface.BandNodes < 2.5 * surface.GridNodes, $"{name}: {surface.BandNodes} evaluations for {surface.GridNodes} nodes");
        }

        [Fact]
        public void Hole_IsDetected_AndTheWrapDecidesThere()
        {
            var clean = new PointCloudSurface(PointClouds.Torus(2500, 7, out _)) { CubeSize = 0.2 };
            Assert.True(clean.Compute());
            var holed = new PointCloudSurface(PointClouds.Torus(2500, 7, out _, 0, true)) { CubeSize = 0.2 };
            Assert.True(holed.Compute());
            Assert.True(clean.WrapAreaFraction < 0.01, $"clean: {clean.WrapAreaFraction:P2} of the surface from the wrap");
            Assert.True(holed.WrapAreaFraction > 0.01 && holed.WrapAreaFraction > 5 * clean.WrapAreaFraction,
                $"hole: {holed.WrapAreaFraction:P2} of the surface from the wrap");
        }

        [Fact]
        public void NoisyData_BubblesAreRemoved_CountedAndReported()
        {
            var pts = PointClouds.Torus(2500, 7, out _, 0.08);
            var surface = new PointCloudSurface(pts) { CubeSize = 0.2 };
            Assert.True(surface.Compute(), surface.Status + surface.Error);
            Assert.True(surface.ComponentsRemoved > 0, "no bubble on noisy data");
            Assert.InRange(surface.RemovedAreaFraction, 1e-6, 0.15);
            // without the cleanup the result is not one component and says so (Result is kept for diagnostics)
            var raw = new PointCloudSurface(pts) { MinComponentAreaFraction = 0, CubeSize = 0.2 };
            Assert.False(raw.Compute());
            Assert.Equal(PointCloudSurface.SurfaceStatus.NotClean, raw.Status);
            Assert.Contains("components", raw.Error);
            Assert.Equal(0, raw.ComponentsRemoved);
            Assert.NotNull(raw.Result);
        }

        [Fact]
        public void FailureStatuses()
        {
            var rnd = new Random(2);
            var sheet = Enumerable.Range(0, 1500).Select(i => new Vector3d(4 * rnd.NextDouble(), 4 * rnd.NextDouble(), 0)).ToList();
            var open = new PointCloudSurface(sheet);
            Assert.False(open.Compute());
            Assert.Equal(PointCloudSurface.SurfaceStatus.NormalsFailed, open.Status);
            Assert.Null(open.Result);

            var tiny = new PointCloudSurface(PointClouds.Torus(2500, 7, out _)) { MaxGridNodes = 1000 };
            Assert.False(tiny.Compute());
            Assert.Equal(PointCloudSurface.SurfaceStatus.GridTooLarge, tiny.Status);

            var nan = PointClouds.Torus(200, 7, out _);
            nan[3] = new Vector3d(double.NaN, 0, 0);
            var bad = new PointCloudSurface(nan);
            Assert.False(bad.Compute());
            Assert.Equal(PointCloudSurface.SurfaceStatus.NormalsFailed, bad.Status);
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Invariant_OverSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            var pts = PointClouds.Torus(1600, 11, out _, 0.02);
            var reference = new PointCloudSurface(pts) { CubeSize = 0.2 };
            Assert.True(reference.Compute(), reference.Status + reference.Error);
            var moved = new PointCloudSurface(pts.Select(s.Apply).ToList()) { CubeSize = 0.2 * s.Scale };
            Assert.True(moved.Compute(), name + ": " + moved.Status + " " + moved.Error);
            ResultChecks.AssertClosed(moved.Result, name);
            if (s.IsExact) {
                // a power of two scale is exact: the same mesh, scaled, bit for bit
                Assert.Equal(reference.Result.VertexCount, moved.Result.VertexCount);
                Assert.Equal(reference.Result.TriangleCount, moved.Result.TriangleCount);
                foreach (int vid in reference.Result.VertexIndices())
                    Assert.Equal(s.Apply(reference.Result.GetVertex(vid)), moved.Result.GetVertex(vid));
            } else {
                // the same surface up to a small part of the cube (the grid is anchored to the data, not to the axes)
                var tree = new DMeshAABBTree3(Invariance.Transformed(reference.Result, s), true);
                double worst = 0;
                foreach (int vid in moved.Result.VertexIndices()) {
                    int tid = tree.FindNearestTriangle(moved.Result.GetVertex(vid));
                    Triangle3d tri = new Triangle3d();
                    tree.Mesh.GetTriVertices(tid, ref tri.V0, ref tri.V1, ref tri.V2);
                    worst = Math.Max(worst, Math.Sqrt(new DistPoint3Triangle3(moved.Result.GetVertex(vid), tri).GetSquared()));
                }
                // (the axis-aligned box of a rotated cloud differs, so the lattice does: two grid surfaces of the same field differ by less than a cube)
                Assert.True(worst < 1.0 * moved.UsedCubeSize, $"{name}: worst distance {worst} for cube {moved.UsedCubeSize}");
            }
        }

        static void AssertSameMesh(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a.VertexCount, b.VertexCount);
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            foreach (int vid in a.VertexIndices()) Assert.Equal(a.GetVertex(vid), b.GetVertex(vid));
            foreach (int tid in a.TriangleIndices()) Assert.Equal(a.GetTriangle(tid), b.GetTriangle(tid));
        }

        [Fact]
        public void Reuse_ComputeTwice()
        {
            var surface = new PointCloudSurface(PointClouds.Torus(1600, 5, out _, 0.03)) { CubeSize = 0.2 };
            Assert.True(surface.Compute());
            var first = new DMesh3(surface.Result);
            int removed = surface.ComponentsRemoved;
            Assert.True(surface.Compute());
            AssertSameMesh(first, surface.Result);
            Assert.Equal(removed, surface.ComponentsRemoved);
        }

        [Fact]
        public void Determinism_ParallelEqualsSerial()
        {
            var pts = PointClouds.Torus(1600, 5, out _, 0.03, true, true);
            var parallel = new PointCloudSurface(pts) { Parallel = true, CubeSize = 0.2 };
            Assert.True(parallel.Compute(), parallel.Status + parallel.Error);
            var serial = new PointCloudSurface(pts) { Parallel = false, CubeSize = 0.2 };
            Assert.True(serial.Compute());
            AssertSameMesh(parallel.Result, serial.Result);
            gParallel.SerialOnThisThread = true;
            try {
                var forced = new PointCloudSurface(pts) { Parallel = true, CubeSize = 0.2 };
                Assert.True(forced.Compute());
                AssertSameMesh(parallel.Result, forced.Result);
            } finally { gParallel.SerialOnThisThread = false; }
            var again = new PointCloudSurface(pts) { CubeSize = 0.2 };
            Assert.True(again.Compute());
            AssertSameMesh(parallel.Result, again.Result);
        }

        [Fact]
        public void DuplicatedPoints_GiveTheSurfaceOfTheUniquePoints()
        {
            var pts = PointClouds.Torus(1600, 5, out _, 0.0);
            var single = new PointCloudSurface(pts) { CubeSize = 0.2 };
            Assert.True(single.Compute());
            var twice = new PointCloudSurface(pts.Concat(pts).ToList()) { CubeSize = 0.2 };
            Assert.True(twice.Compute(), twice.Status + twice.Error);
            AssertSameMesh(single.Result, twice.Result);
        }
    }
}
