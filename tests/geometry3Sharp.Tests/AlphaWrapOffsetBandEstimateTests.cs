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
    /// The size estimate of the alpha wrap contains the offset band (issue #1593): the wrap goes around the rim of an open sheet or a thin
    /// plate, around the edges of a solid that the empty ball touches from outside, and over a sheet at the distance of the offset, with
    /// triangles of the offset's size, which no density of alpha has. MeshAlphaWrap.EstimatedTriangles against the raw wrap's triangle
    /// count on procedural sheets, plates, cubes and spheres (the cases of the issue), at relative alpha 5 to 96, so that each wrap takes a
    /// second at most.
    /// </summary>
    public class AlphaWrapOffsetBandEstimateTests
    {
        readonly ITestOutputHelper output;
        public AlphaWrapOffsetBandEstimateTests(ITestOutputHelper output) { this.output = output; }

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

        // the band and the rim are part of the estimate: positive, below it, and zero for a smooth closed solid
        [Fact]
        public void EstimatedRimTriangles_IsThePartOfTheEstimate()
        {
            MeshAlphaWrap sheet = Wrap(Sheet(8), 20, 600);
            Assert.True(sheet.EstimatedRimTriangles > 0);
            Assert.True(sheet.EstimatedRimTriangles < sheet.EstimatedTriangles);
            MeshAlphaWrap plate = Wrap(Plate(1e-4), 20, 600);
            Assert.True(plate.EstimatedRimTriangles > 0.5 * plate.EstimatedTriangles);
        }

        // closed solids without sharp edges keep the estimate they had, bit for bit: density * area / alpha^2, no band, no rim
        [Theory]
        [InlineData(20, 600)]
        [InlineData(24, 1200)]
        public void ClosedSphere_KeepsTheOneSidedEstimate(double relativeAlpha, double relativeOffset)
        {
            DMesh3 m = Sphere(8);
            MeshAlphaWrap w = Wrap(m, relativeAlpha, relativeOffset);
            Assert.Equal(0.0, w.EstimatedRimTriangles);
            Assert.Equal(MeshAlphaWrap.EstimateDensity * Invariance.Area(m) / w.UsedAlpha / w.UsedAlpha, w.EstimatedTriangles);
            Assert.InRange(w.Result.TriangleCount / w.EstimatedTriangles, 0.5, 1.3);
        }

        // a plate that is not thin (0.34 alpha and more: the wrap behaves as around a solid) is not a sheet: the rim term is the one of its sharp edges
        [Fact]
        public void ThickPlate_IsNotASheet()
        {
            DMesh3 m = Plate(0.2);
            MeshAlphaWrap w = Wrap(m, 24, 1200);
            AlphaWrapEstimate.EstimateTerms terms = AlphaWrapEstimate.Terms(Corners(m), w.UsedAlpha);
            Assert.Equal(0.0, terms.ThinArea);
            Assert.True(terms.ThickSharp > 7);
            Assert.InRange(w.Result.TriangleCount / w.EstimatedTriangles, 0.5, 1.3);
        }

        // EstimatedRimTriangles is exactly the band plus the rim of the terms (the formula written out here from AlphaWrapEstimate.Terms), and
        // EstimatedTriangles the bulk plus it: a dropped term of either fails the equality
        [Theory]
        [InlineData("sheet", 20, 600)]
        [InlineData("sheet", 96, 960)]
        [InlineData("plate", 20, 600)]
        [InlineData("two cubes", 5, 600)]
        [InlineData("cube", 20, 600)]
        public void EstimatedRimTriangles_IsTheBandPlusTheRimOfTheTerms(string shape, double relativeAlpha, double relativeOffset)
        {
            DMesh3 m = shape == "sheet" ? Sheet(16) : shape == "plate" ? Plate(1e-4) : shape == "cube" ? Box(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5), 6) : TwoCubes(1e-2);
            MeshAlphaWrap w = Wrap(m, relativeAlpha, relativeOffset);
            AlphaWrapEstimate.EstimateTerms t = AlphaWrapEstimate.Terms(Corners(m), w.UsedAlpha);
            double alpha = w.UsedAlpha, ratio = Math.Min(alpha / w.UsedOffset, MeshAlphaWrap.MaxOffsetRatio);
            double sheet = t.OpenArea + 0.5 * t.ThinArea;
            double band = MeshAlphaWrap.SheetOffsetDensity * sheet * Math.Max(0.0, ratio - MeshAlphaWrap.SheetOffsetRatio) / alpha / alpha;
            double rim = (MeshAlphaWrap.RimDensity * (t.OpenBoundary + t.ThinRim) + MeshAlphaWrap.SharpDensity * t.ThickSharp) * ratio / alpha;
            Assert.True(band + rim > 0);
            Assert.True(Math.Abs(w.EstimatedRimTriangles - (band + rim)) <= 1e-12 * (band + rim), $"{w.EstimatedRimTriangles} vs {band + rim}");
            double bulk = MeshAlphaWrap.EstimateDensity * (Invariance.Area(m) + t.OpenArea) / alpha / alpha;
            Assert.True(Math.Abs(w.EstimatedTriangles - (bulk + band + rim)) <= 1e-12 * w.EstimatedTriangles, $"{w.EstimatedTriangles} vs {bulk + band + rim}");
            // (the part that each term contributes: band for sheets, rim for all with edges)
            Assert.Equal(sheet > 0, band > 0);
            Assert.Equal(t.OpenBoundary + t.ThinRim + t.ThickSharp > 0, rim > 0);
        }

        // a surface whose triangles are not consistently oriented (every second one reversed): the sharp test is on the angle between the planes and
        // the thin test needs a volume, so neither sees a sheet or an edge in a smooth sphere: the estimate is the sphere's, and a budget just below
        // it coarsens by a hair, not 17 times (the volume of such a surface is about 0, and every edge looked like a 180 degree fold)
        [Fact]
        public void MixedOrientation_SmoothSphere_KeepsTheEstimate_AndTheBudget()
        {
            DMesh3 m = Sphere(8);
            foreach (int t in m.TriangleIndices().ToList())
                if (t % 2 == 1) m.ReverseTriOrientation(t);
            MeshAlphaWrap w = Wrap(m, 20, 600);
            Assert.Equal(0.0, w.EstimatedRimTriangles);
            Assert.Equal(MeshAlphaWrap.EstimateDensity * Invariance.Area(m) / w.UsedAlpha / w.UsedAlpha, w.EstimatedTriangles);
            AlphaWrapEstimate.EstimateTerms terms = AlphaWrapEstimate.Terms(Corners(m), w.UsedAlpha);
            Assert.Equal(0.0, terms.ThinArea);
            Assert.Equal(0.0, terms.ThickSharp);
            MeshAlphaWrap budget = Wrap(m, 20, 600, (int)w.EstimatedTriangles - 1);
            Assert.InRange(budget.AlphaCoarsening, 1.0, 1.01);
        }

        // a tiny (here subnormal) offset: the ratio alpha / offset is capped, the band of an input with no sheet is 0 (not 0 times infinity), and
        // the budget still applies (a NaN estimate compares false and silently switched it off)
        [Theory]
        [InlineData("sphere")]
        [InlineData("sheet")]
        public void TinyOffset_NoNaN_BudgetStillApplies(string shape)
        {
            DMesh3 m = shape == "sphere" ? Sphere(8) : Sheet(8);
            var w = new MeshAlphaWrap(m) { RelativeAlpha = 20, Offset = 1e-320, MaxEstimatedTriangles = 300, MaxSteinerPoints = 1 };
            w.Compute();       // (the estimate and the coarsening are set before the carving)
            Assert.False(double.IsNaN(w.EstimatedTriangles));
            Assert.False(double.IsNaN(w.EstimatedRimTriangles));
            Assert.True(w.EstimatedTriangles > 300);
            Assert.True(w.AlphaCoarsening > 1.0, $"coarsening {w.AlphaCoarsening}");
            var off = new MeshAlphaWrap(m) { RelativeAlpha = 20, Offset = 1e-320, MaxSteinerPoints = 1 };
            off.Compute();
            Assert.False(double.IsNaN(off.EstimatedTriangles));
            Assert.Equal(1.0, off.AlphaCoarsening);
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
            Assert.InRange(w.Result.TriangleCount / (double)budget, 0.3, 1.3);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
        }

        // the terms of the analysis on shapes with known sizes
        [Fact]
        public void Terms_OfSheetPlateCubeAndSphere()
        {
            double alpha = 0.05;
            // an open sheet: the area counted open, the boundary is its perimeter 4 (a flat 1 x 1 grid)
            DMesh3 flat = new DMesh3();
            for (int i = 0; i <= 8; ++i) for (int j = 0; j <= 8; ++j) flat.AppendVertex(new Vector3d(i / 8.0, j / 8.0, 0));
            for (int i = 0; i < 8; ++i) for (int j = 0; j < 8; ++j) { int a = i * 9 + j, b = a + 9; flat.AppendTriangle(a, b, b + 1); flat.AppendTriangle(a, b + 1, a + 1); }
            AlphaWrapEstimate.EstimateTerms t = AlphaWrapEstimate.Terms(Corners(flat), alpha);
            Assert.Equal(1.0, t.OpenArea, 12);
            Assert.Equal(4.0, t.OpenBoundary, 12);
            Assert.Equal(0.0, t.ThinArea);
            Assert.Equal(0.0, t.ThickSharp);
            // a thin plate (welded into a sheet at this alpha): thin, the rim at least the perimeter of the disc of the sheet's area
            t = AlphaWrapEstimate.Terms(Corners(Plate(1e-4)), alpha);
            Assert.Equal(0.0, t.OpenArea);
            Assert.Equal(2.0, t.ThinArea, 3);
            Assert.True(t.ThinRim >= 2 * Math.Sqrt(Math.PI * 1.0) - 1e-9, $"{t.ThinRim}");
            // a cube: 12 sharp edges of length 1, none thin
            t = AlphaWrapEstimate.Terms(Corners(Box(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5), 5)), alpha);
            Assert.Equal(12.0, t.ThickSharp, 9);
            Assert.Equal(0.0, t.ThinArea);
            // a smooth sphere: nothing
            t = AlphaWrapEstimate.Terms(Corners(Sphere(8)), alpha);
            Assert.Equal(0.0, t.ThickSharp);
            Assert.Equal(0.0, t.ThinArea);
            Assert.Equal(0.0, t.OpenArea);
            // no triangles, no alpha: zero terms
            Assert.Equal(0.0, AlphaWrapEstimate.Terms(new List<Vector3d>(), alpha).OpenArea);
            Assert.Equal(0.0, AlphaWrapEstimate.Terms(Corners(flat), 0).OpenArea);
        }

        // an offset that is not positive is rejected as before; the estimate is then without a rim (no division by it)
        [Fact]
        public void NoOffset_IsRejected_WithoutANaNEstimate()
        {
            var w = new MeshAlphaWrap(Sheet(4)) { RelativeAlpha = 20, RelativeOffset = 0, Offset = 0 };
            Assert.False(w.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidParameters, w.Status);
            Assert.False(double.IsNaN(w.EstimatedTriangles));
            Assert.Equal(0.0, w.EstimatedRimTriangles);
        }

        // reuse: Compute() twice, and for another input and back, gives the same estimate and result
        [Fact]
        public void Reuse_SameEstimateAndResult()
        {
            var w = new MeshAlphaWrap(Sheet(8)) { RelativeAlpha = 40, RelativeOffset = 800 };
            Assert.True(w.Compute(), w.Error);
            double e = w.EstimatedTriangles, rim = w.EstimatedRimTriangles; int tris = w.Result.TriangleCount;
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(e, w.EstimatedTriangles);
            Assert.Equal(rim, w.EstimatedRimTriangles);
            Assert.Equal(tris, w.Result.TriangleCount);
            Sphere3Generator_NormalizedCube g = new Sphere3Generator_NormalizedCube { Radius = 0.5, EdgeVertices = 6 };
            g.Generate();
            w.Mesh = g.MakeDMesh();
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(0.0, w.EstimatedRimTriangles);
            w.Mesh = Sheet(8);
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(e, w.EstimatedTriangles);
            Assert.Equal(tris, w.Result.TriangleCount);
        }

        // invariance over Similarity.All() at a fixed alpha and offset (scaled with the frame), on the sheet, the thin plate and the two cubes: the
        // estimate and its band bit for bit in the 2^k frames and within 1e-5 in the others (the weld grid and the thickness test follow the
        // frame's rounding), the same open / thin / sharp terms
        [Theory]
        [InlineData("sheet")]
        [InlineData("plate")]
        [InlineData("two cubes")]
        public void Estimate_Invariance(string shape)
        {
            DMesh3 input = shape == "sheet" ? Sheet(8) : shape == "plate" ? Plate(1e-4) : TwoCubes(1e-2);
            double diag = input.CachedBounds.DiagonalLength, alpha = diag / 40, offset = alpha / 30;
            double e0 = 0, rim0 = 0;
            foreach (Similarity s in Similarity.All()) {
                DMesh3 m = Invariance.Transformed(input, s);
                var w = new MeshAlphaWrap(m) { Alpha = alpha * s.Scale, Offset = offset * s.Scale, MaxSteinerPoints = 1 };
                w.Compute();      // (stops at once: the estimate is set before the carving)
                if (s.Name == Similarity.Identity.Name) { e0 = w.EstimatedTriangles; rim0 = w.EstimatedRimTriangles; }
                Assert.True(e0 > 0 && rim0 > 0, shape);
                if (s.IsExact) {
                    Assert.Equal(e0, w.EstimatedTriangles);
                    Assert.Equal(rim0, w.EstimatedRimTriangles);
                } else {
                    // (1e-5: the coordinates of the rotated millimetre UTM frame are rounded to 1e-10 of their size, 1.1e-6 measured on the cubes)
                    Assert.True(Math.Abs(w.EstimatedTriangles - e0) <= 1e-5 * e0, $"{shape} {s.Name}: {w.EstimatedTriangles} vs {e0}");
                    Assert.True(Math.Abs(w.EstimatedRimTriangles - rim0) <= 1e-5 * rim0, $"{shape} {s.Name}: {w.EstimatedRimTriangles} vs {rim0}");
                }
            }
        }

        // the analysis is deterministic: the same terms on repeated calls (sums in a fixed order)
        [Fact]
        public void Terms_Repeated_AreTheSame()
        {
            List<Vector3d> corners = Corners(TwoCubes(1e-2));
            AlphaWrapEstimate.EstimateTerms a = AlphaWrapEstimate.Terms(corners, 0.1);
            for (int i = 0; i < 5; ++i) {
                AlphaWrapEstimate.EstimateTerms b = AlphaWrapEstimate.Terms(corners, 0.1);
                Assert.Equal(a.OpenArea, b.OpenArea); Assert.Equal(a.OpenBoundary, b.OpenBoundary); Assert.Equal(a.ThinArea, b.ThinArea);
                Assert.Equal(a.ThinRim, b.ThinRim); Assert.Equal(a.ThickSharp, b.ThickSharp);
            }
        }

        static List<Vector3d> Corners(DMesh3 m)
        {
            var l = new List<Vector3d>();
            foreach (int t in m.TriangleIndices()) { Index3i tr = m.GetTriangle(t); l.Add(m.GetVertex(tr.a)); l.Add(m.GetVertex(tr.b)); l.Add(m.GetVertex(tr.c)); }
            return l;
        }
    }
}
