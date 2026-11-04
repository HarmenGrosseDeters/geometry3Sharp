using System;
using System.Collections.Generic;
using g3;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The refiner's insertions locate their point from the triangle the refiner already found (DelaunayKernel2.LocateHint): the
    /// walk takes a few steps, not O(sqrt n) from the last insertion, which the worst-first order puts anywhere in the mesh.
    /// </summary>
    public class QualityMesh2LocateTests
    {
        readonly ITestOutputHelper output;
        public QualityMesh2LocateTests(ITestOutputHelper output) { this.output = output; }

        /// <summary> a square with clusters of short segments in its corners and centre: refinement grades from each cluster, and the
        /// worst-first queue jumps between them </summary>
        static ConstrainedMesh2Input Clusters()
        {
            var input = new ConstrainedMesh2Input();
            void Add(Vector2d p) => input.Points.Add(p);
            Add(new Vector2d(0, 0)); Add(new Vector2d(1000, 0)); Add(new Vector2d(1000, 1000)); Add(new Vector2d(0, 1000));
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }));
            var rnd = new Random(5);
            foreach (var c in new[] { new Vector2d(50, 50), new Vector2d(950, 60), new Vector2d(500, 500), new Vector2d(70, 930), new Vector2d(940, 940) })
                for (int i = 0; i < 6; ++i) {
                    int a = input.Points.Count;
                    var p = c + new Vector2d(rnd.NextDouble() * 20, rnd.NextDouble() * 20);
                    Add(p); Add(p + new Vector2d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5) * 4);
                    input.Segments.Add(new Mesh2Segment(a, a + 1));
                }
            return input;
        }

        [Theory]
        [InlineData(20.0, 0.0)]
        [InlineData(30.0, 0.0)]
        [InlineData(30.0, 0.01)]
        public void RefinerInsertions_WalkAFewSteps(double angle, double snap)
        {
            long steps0 = QualityMesh2.Refiner.WalkStepsForTests, n0 = QualityMesh2.Refiner.LocationsForTests;
            var result = new QualityMesh2 { MinAngle = angle, MaxArea = 40, SnapTolerance = snap }.Compute(Clusters());
            long steps = QualityMesh2.Refiner.WalkStepsForTests - steps0, n = QualityMesh2.Refiner.LocationsForTests - n0;
            Assert.True(result.Mesh != null && result.Mesh.Triangles.Count > 20000, "triangles " + result.Mesh?.Triangles.Count);
            output.WriteLine(n + " locations, " + steps + " steps, " + (steps / (double)n).ToString("F2") + " per location");
            // the triangle found holds the point: 0.01 steps per location here; from the last insertion instead 15 to 22 (85 on the
            // Herne tiles)
            Assert.True(n > 10000 && steps < n, n + " locations, " + steps + " walk steps");
        }
    }
}
