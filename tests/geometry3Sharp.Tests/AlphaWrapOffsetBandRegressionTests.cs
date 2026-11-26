using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The regressions of issue #1593 that need nothing but the API of master (EstimatedTriangles, MaxEstimatedTriangles, UsedAlpha, UsedOffset,
    /// the result's triangle count, NotCleanReason), so that they compile and run against the library before the fix: the raw wrap's size against
    /// MeshAlphaWrap.EstimatedTriangles on procedural sheets, plates and cubes at small offsets, the budget on a sheet, and the estimate of a
    /// smooth sphere of mixed orientation. The tests of the new members (EstimatedRimTriangles, the terms) are in AlphaWrapOffsetBandEstimateTests.
    /// </summary>
    public class AlphaWrapOffsetBandRegressionTests
    {
        readonly ITestOutputHelper output;
        public AlphaWrapOffsetBandRegressionTests(ITestOutputHelper output) { this.output = output; }

        static DMesh3 Box(Vector3d center, Vector3d half, int n)
        {
            var g = new GridBox3Generator { Box = new Box3d(center, half), EdgeVertices = n };
            g.Generate();
            return g.MakeDMesh();
        }

        static DMesh3 Sphere(int n)
        {
            var g = new Sphere3Generator_NormalizedCube { Radius = 0.5, EdgeVertices = n };
            g.Generate();
            return g.MakeDMesh();
        }

        // the open curved sheet of the demo's "sheet" shape (a polynomial surface over [-0.5, 0.5]^2, 2 n^2 triangles)
        static DMesh3 Sheet(int n)
        {
            var m = new DMesh3();
            for (int i = 0; i <= n; ++i)
                for (int j = 0; j <= n; ++j) {
                    double x = -0.5 + (double)i / n, y = -0.5 + (double)j / n;
                    m.AppendVertex(new Vector3d(x, y, 0.3 * (x * x - y * y) + 0.6 * x * y * (x + y)));
                }
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    int a = i * (n + 1) + j, b = a + n + 1;
                    m.AppendTriangle(a, b, b + 1); m.AppendTriangle(a, b + 1, a + 1);
                }
            return m;
        }

        // a plate 1 x 1 x thickness
        static DMesh3 Plate(double thickness) => Box(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5 * thickness), 8);

        // two cubes of side 0.5 with a gap between them
        static DMesh3 TwoCubes(double gap)
        {
            DMesh3 a = Box(new Vector3d(-0.25 - gap / 2, 0, 0), new Vector3d(0.25, 0.25, 0.25), 2);
            new MeshEditor(a).AppendMesh(Box(new Vector3d(0.25 + gap / 2, 0, 0), new Vector3d(0.25, 0.25, 0.25), 2));
            return a;
        }

        static MeshAlphaWrap Wrap(DMesh3 m, double relativeAlpha, double relativeOffset, int budget = 0)
        {
            var w = new MeshAlphaWrap(m) { RelativeAlpha = relativeAlpha, RelativeOffset = relativeOffset, MaxEstimatedTriangles = budget };
            Assert.True(w.Compute(), w.Error);
            return w;
        }

        // result / estimate: a bound (at most 1.3) on the cases of the issue, 7.8 (sheet), 4.0 (plates), 6.7 (cubes) and 4.8 (small offset) on master
        // (measured after: 0.79, 0.84, 0.96, 0.80); a lower ratio is a coarser wrap than the size needs, 0.25 at the thinnest part of the
        // transition of ThinFactor (a plate of 0.01 at alpha 0.059)
        [Theory]
        [InlineData("sheet", 20, 600)]
        [InlineData("sheet", 96, 960)]
        [InlineData("sheet", 48, 2400)]
        [InlineData("plate 1e-4", 20, 600)]
        [InlineData("plate 1e-12", 20, 600)]
        [InlineData("plate 1e-4", 24, 1200)]
        [InlineData("two cubes", 5, 600)]
        [InlineData("two cubes", 20, 600)]
        public void Estimate_BoundsTheWrap_OnSheetsPlatesAndCubes(string shape, double relativeAlpha, double relativeOffset)
        {
            DMesh3 m = shape == "sheet" ? Sheet(16) : shape == "two cubes" ? TwoCubes(1e-2) : Plate(shape == "plate 1e-4" ? 1e-4 : 1e-12);
            MeshAlphaWrap w = Wrap(m, relativeAlpha, relativeOffset);
            double ratio = w.Result.TriangleCount / w.EstimatedTriangles;
            output.WriteLine($"{shape} alpha {relativeAlpha} offset {relativeOffset}: estimate {w.EstimatedTriangles:F0} result {w.Result.TriangleCount}, ratio {ratio:F2}");
            Assert.InRange(ratio, 0.4, 1.3);
        }

        // a smooth sphere of mixed orientation (every second triangle reversed) has the estimate of the oriented one and a budget just below it
        // coarsens by a hair, not 17 times (the thin-part test took its volume of about 0 for a plate, and every edge a 180 degree fold)
        [Fact]
        public void MixedOrientation_SmoothSphere_KeepsTheEstimate_AndTheBudget()
        {
            DMesh3 m = Sphere(8);
            foreach (int t in m.TriangleIndices().ToList())
                if (t % 2 == 1) m.ReverseTriOrientation(t);
            MeshAlphaWrap w = Wrap(m, 20, 600);
            Assert.Equal(MeshAlphaWrap.EstimateDensity * Invariance.Area(m) / w.UsedAlpha / w.UsedAlpha, w.EstimatedTriangles);
            MeshAlphaWrap budget = Wrap(m, 20, 600, (int)w.EstimatedTriangles - 1);
            Assert.InRange(budget.AlphaCoarsening, 1.0, 1.01);
        }

        // MaxEstimatedTriangles bounds the size (the issue: a sheet of 512 triangles with a budget of 2000 gave 4642, 2.3 times it)
        [Fact]
        public void Budget_BoundsTheSizeOfASheet()
        {
            DMesh3 m = Sheet(16);
            const int budget = 2000;
            MeshAlphaWrap w = Wrap(m, 20, 600, budget);
            output.WriteLine($"estimate {w.EstimatedTriangles:F0}, coarsening {w.AlphaCoarsening:F2}, result {w.Result.TriangleCount}");
            Assert.True(w.AlphaCoarsening > 1.5);
            Assert.InRange(w.Result.TriangleCount / (double)budget, 0.3, 1.0);       // (the budget bounds the size: 4674 = 2.3 times it on master)
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
        }
    }
}
