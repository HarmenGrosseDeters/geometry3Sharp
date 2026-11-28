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

        static List<Vector3d> ScaleAndShift(List<Vector3d> corners, int pow2Exp, Vector3d shift)
        {
            var res = new List<Vector3d>(corners.Count);
            foreach (Vector3d p in corners)
                res.Add(new Vector3d(
                    ExactPoint3.ScaleB(p.x, pow2Exp) + shift.x,
                    ExactPoint3.ScaleB(p.y, pow2Exp) + shift.y,
                    ExactPoint3.ScaleB(p.z, pow2Exp) + shift.z));
            return res;
        }

        [Fact]
        public void Terms_ExtremeScales_2p350_And_2m350_PreservesClassificationAndLengths()
        {
            List<Vector3d> cube = Corners(Box(Vector3d.Zero, new Vector3d(0.5, 0.5, 0.5), 4));
            List<Vector3d> plate = Corners(Plate(1e-4));
            List<Vector3d> sphere = Corners(Sphere(8));
            List<Vector3d> openSquare = new List<Vector3d> {
                new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0),
                new Vector3d(0, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0)
            };
            double alpha0 = 0.05;

            AlphaWrapEstimate.EstimateTerms refCube = AlphaWrapEstimate.Terms(cube, alpha0);
            AlphaWrapEstimate.EstimateTerms refPlate = AlphaWrapEstimate.Terms(plate, alpha0);
            Assert.Equal(0.0, refCube.ThinArea);
            Assert.Equal(12.0, refCube.ThickSharp, 9);
            Assert.True(refPlate.ThinArea > 1.9 && refPlate.ThinRim >= 2 * Math.Sqrt(Math.PI) - 1e-6);

            foreach (int exp in new[] { 350, -350 }) {
                double a = ExactPoint3.ScaleB(alpha0, exp);

                // Closed cube: s^3 = 2^(+-1050) overflows/underflows on master, causing NaN volume, Inf cross.Length -> normal=0, or ThinArea false positive
                List<Vector3d> scaledCube = ScaleAndShift(cube, exp, Vector3d.Zero);
                AlphaWrapEstimate.EstimateTerms tc = AlphaWrapEstimate.Terms(scaledCube, a);
                AlphaWrapEstimate.EstimateTerms tc2 = AlphaWrapEstimate.Terms(scaledCube, a);
                Assert.Equal(tc, tc2); // determinism on normalized path
                Assert.Equal(0.0, tc.OpenArea);
                Assert.Equal(0.0, tc.ThinArea);
                Assert.Equal(0.0, tc.ThinRim);
                Assert.True(Math.Abs(tc.ThickSharp - ExactPoint3.ScaleB(refCube.ThickSharp, exp)) <= ExactPoint3.ScaleB(1e-9, exp),
                    $"exp {exp}: cube ThickSharp {tc.ThickSharp}");

                // Thin plate: must remain thin with scaled ThinArea and ThinRim
                AlphaWrapEstimate.EstimateTerms tp = AlphaWrapEstimate.Terms(ScaleAndShift(plate, exp, Vector3d.Zero), a);
                Assert.Equal(0.0, tp.OpenArea);
                Assert.True(Math.Abs(tp.ThinArea - ExactPoint3.ScaleB(refPlate.ThinArea, 2 * exp)) <= ExactPoint3.ScaleB(1e-6, 2 * exp),
                    $"exp {exp}: plate ThinArea {tp.ThinArea}");
                Assert.True(Math.Abs(tp.ThinRim - ExactPoint3.ScaleB(refPlate.ThinRim, exp)) <= ExactPoint3.ScaleB(1e-6, exp),
                    $"exp {exp}: plate ThinRim {tp.ThinRim}");

                // Smooth closed sphere: must stay smooth and thick (not sharp at 2^350, not thin at 2^-350)
                AlphaWrapEstimate.EstimateTerms ts = AlphaWrapEstimate.Terms(ScaleAndShift(sphere, exp, Vector3d.Zero), a);
                Assert.Equal(0.0, ts.OpenArea);
                Assert.Equal(0.0, ts.ThinArea);
                Assert.Equal(0.0, ts.ThickSharp);

                // Direct OpenArea call at 2^+-350: verifies ScaleB(area, 2 * scaleExp)
                double oa = AlphaWrapEstimate.OpenArea(ScaleAndShift(openSquare, exp, Vector3d.Zero), a);
                Assert.True(Math.Abs(oa - ExactPoint3.ScaleB(1.0, 2 * exp)) <= ExactPoint3.ScaleB(1e-12, 2 * exp),
                    $"exp {exp}: OpenArea {oa}");
            }

            // pre == 1 branch: hi - lo overflows double ([-1e308, +1e308]), half-scaled span stays finite
            double big = 1e308;
            var hugeSpan = new List<Vector3d> {
                new Vector3d(-big, -big, 0), new Vector3d(big, -big, 0), new Vector3d(big, big, 0),
                new Vector3d(-big, -big, 0), new Vector3d(big, big, 0), new Vector3d(-big, big, 0)
            };
            double hugeOpening = AlphaWrapEstimate.LargestOpening(hugeSpan, 1e305);
            Assert.True(double.IsInfinity(hugeOpening) || hugeOpening > 1e308);
            // Narrower axis-aligned open square spanning [-1e308, +1e308] on x and [-1e307, +1e307] on y:
            // hi.x - lo.x = 2e308 overflows double -> pre == 1, while perimeter and opening along x stay finite? No, 2e308 overflows;
            // use [-9e307, +9e307] so hi - lo = 1.8e308 overflows (pre == 1), while a 0.25x sub-square stays finite on outputs.
            double b9 = 9e307;
            var pre1Tris = new List<Vector3d> {
                new Vector3d(-b9, 0, 0), new Vector3d(-b9 + 2e307, 0, 0), new Vector3d(-b9 + 2e307, 2e307, 0),
                new Vector3d(-b9, 0, 0), new Vector3d(-b9 + 2e307, 2e307, 0), new Vector3d(-b9, 2e307, 0),
                // far point forcing hi.x - lo.x = 1.8e308 > double.MaxValue -> pre = 1
                new Vector3d(b9, 0, 0), new Vector3d(b9, 0, 0), new Vector3d(b9, 0, 0)
            };
            double pre1Opening = AlphaWrapEstimate.LargestOpening(pre1Tris, 1e305);
            Assert.True(MathUtil.IsFinite(pre1Opening) && Math.Abs(pre1Opening - Math.Sqrt(2.0) * 2e307) <= 1e295,
                $"pre==1 LargestOpening {pre1Opening}");
            AlphaWrapEstimate.EstimateTerms pre1Terms = AlphaWrapEstimate.Terms(pre1Tris, 1e305);
            Assert.True(MathUtil.IsFinite(pre1Terms.OpenBoundary) && Math.Abs(pre1Terms.OpenBoundary - 8e307) <= 1e295,
                $"pre==1 OpenBoundary {pre1Terms.OpenBoundary}");

            // Non-finite and zero-span inputs return zero cleanly
            var nanTris = new List<Vector3d> { new Vector3d(double.NaN, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) };
            var infTris = new List<Vector3d> { new Vector3d(double.PositiveInfinity, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) };
            var zeroSpan = new List<Vector3d> { new Vector3d(5, 5, 5), new Vector3d(5, 5, 5), new Vector3d(5, 5, 5) };
            foreach (var bad in new[] { nanTris, infTris, zeroSpan }) {
                Assert.Equal(0.0, AlphaWrapEstimate.OpenArea(bad, 0.05));
                Assert.Equal(0.0, AlphaWrapEstimate.LargestOpening(bad, 0.05));
                Assert.Equal(default(AlphaWrapEstimate.EstimateTerms), AlphaWrapEstimate.Terms(bad, 0.05));
            }
            Assert.Equal(0.0, AlphaWrapEstimate.OpenArea(openSquare, double.PositiveInfinity));
            Assert.Equal(0.0, AlphaWrapEstimate.OpenArea(openSquare, double.NaN));
        }

        [Fact]
        public void Analyze_UtmWithTinyAlpha_AndExtremeOpening_DoesNotCollapseVertices()
        {
            DMesh3 flat = new DMesh3();
            for (int i = 0; i <= 4; ++i)
                for (int j = 0; j <= 4; ++j)
                    flat.AppendVertex(new Vector3d(i / 4.0, j / 4.0, 0));
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j) {
                    int a = i * 5 + j, b = a + 5;
                    flat.AppendTriangle(a, b, b + 1);
                    flat.AppendTriangle(a, b + 1, a + 1);
                }
            List<Vector3d> unitSheet = Corners(flat);

            // 1. UTM offset (5e6) with tiny alpha (1e-12): raw v/cell = 4e19 > 9e18 collapses all vertices on master
            List<Vector3d> utmSheet = ScaleAndShift(unitSheet, 0, new Vector3d(5e6, 5e6, 0));
            AlphaWrapEstimate.EstimateTerms tUtm = AlphaWrapEstimate.Terms(utmSheet, 1e-12);
            Assert.Equal(1.0, tUtm.OpenArea, 9);
            Assert.Equal(1.0, AlphaWrapEstimate.OpenArea(utmSheet, 1e-12), 9);
            Assert.Equal(4.0, tUtm.OpenBoundary, 9);
            Assert.Equal(Math.Sqrt(2.0), AlphaWrapEstimate.LargestOpening(utmSheet, 1e-12), 9);

            // 1b. SafeGridLimit = 2^52 fast-path boundary: v/cell = 6e17 (in [2^53, 1e18]) loses unit integer spacing in binary64
            double side30 = ExactPoint3.ScaleB(1.0, -30);
            var utmSmall = new List<Vector3d> {
                new Vector3d(7_500_000.0, 7_500_000.0, 0),
                new Vector3d(7_500_000.0 + side30, 7_500_000.0, 0),
                new Vector3d(7_500_000.0 + side30, 7_500_000.0 + side30, 0),
                new Vector3d(7_500_000.0, 7_500_000.0, 0),
                new Vector3d(7_500_000.0 + side30, 7_500_000.0 + side30, 0),
                new Vector3d(7_500_000.0, 7_500_000.0 + side30, 0)
            };
            double utmSmallOpening = AlphaWrapEstimate.LargestOpening(utmSmall, 1e-10);
            Assert.True(Math.Abs(utmSmallOpening - Math.Sqrt(2.0) * side30) <= 1e-16, $"utmSmall opening {utmSmallOpening}");
            Assert.True(Math.Abs(AlphaWrapEstimate.OpenArea(utmSmall, 1e-10) - side30 * side30) <= 1e-26);

            // 1c. SafeGridLimit = 2^52 normalized-path boundary: 2 / workCell = 1e18 (> 2^52) triggers exactKeys instead of ulp>1 collapse
            double eps52 = ExactPoint3.ScaleB(1.0, -52);
            var normEdge = new List<Vector3d> {
                // Anchor lo at (0,0,0) so (1.5, 1.5, 0) lands at 1.5 in normalized [0, 2)
                new Vector3d(0, 0, 0), new Vector3d(0, 0, 0), new Vector3d(0, 0, 0),
                new Vector3d(1.5, 1.5, 0), new Vector3d(1.5 + eps52, 1.5, 0), new Vector3d(1.5 + eps52, 1.5 + eps52, 0),
                new Vector3d(1.5, 1.5, 0), new Vector3d(1.5 + eps52, 1.5 + eps52, 0), new Vector3d(1.5, 1.5 + eps52, 0)
            };
            List<Vector3d> normEdge350 = ScaleAndShift(normEdge, 350, Vector3d.Zero);
            double normOpening = AlphaWrapEstimate.LargestOpening(normEdge350, ExactPoint3.ScaleB(1.6e-17, 350));
            Assert.True(Math.Abs(normOpening - ExactPoint3.ScaleB(Math.Sqrt(2.0) * eps52, 350)) <= ExactPoint3.ScaleB(1e-20, 350),
                $"normEdge350 opening {normOpening}");

            // 2. Tiny alpha relative to unit span (span/cell = 8e22 > 2^52): exercises exact-bit welding fallback & determinism
            AlphaWrapEstimate.EstimateTerms tExact = AlphaWrapEstimate.Terms(unitSheet, 1e-22);
            AlphaWrapEstimate.EstimateTerms tExact2 = AlphaWrapEstimate.Terms(unitSheet, 1e-22);
            Assert.Equal(tExact, tExact2);
            Assert.Equal(1.0, tExact.OpenArea, 9);
            Assert.Equal(1.0, AlphaWrapEstimate.OpenArea(unitSheet, 1e-22), 9);
            Assert.Equal(4.0, tExact.OpenBoundary, 9);
            Assert.Equal(Math.Sqrt(2.0), AlphaWrapEstimate.LargestOpening(unitSheet, 1e-22), 9);

            // 2b. -0.0 vs +0.0 welding via NormBits under exactKeys: shared diagonal edge must weld cleanly
            double negZero = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000UL));
            var negZeroSquare = new List<Vector3d> {
                new Vector3d(0.0, 0.0, 0.0), new Vector3d(1.0, 0.0, 0.0), new Vector3d(1.0, 1.0, 0.0),
                new Vector3d(negZero, negZero, negZero), new Vector3d(1.0, 1.0, 0.0), new Vector3d(negZero, 1.0, 0.0)
            };
            AlphaWrapEstimate.EstimateTerms tNegZero = AlphaWrapEstimate.Terms(negZeroSquare, 1e-22);
            Assert.Equal(1.0, tNegZero.OpenArea, 9);
            Assert.Equal(4.0, tNegZero.OpenBoundary, 9);

            // 3. Extreme length scales (2^600 and 2^-600): LargestOpening and OpenBoundary stay finite and scaled
            foreach (int exp in new[] { 600, -600 }) {
                List<Vector3d> scaled = ScaleAndShift(unitSheet, exp, Vector3d.Zero);
                double a = ExactPoint3.ScaleB(0.05, exp);
                double opening = AlphaWrapEstimate.LargestOpening(scaled, a);
                double expectedOpening = ExactPoint3.ScaleB(Math.Sqrt(2.0), exp);
                Assert.True(Math.Abs(opening - expectedOpening) <= ExactPoint3.ScaleB(1e-12, exp), $"exp {exp}: opening {opening}");
                AlphaWrapEstimate.EstimateTerms t = AlphaWrapEstimate.Terms(scaled, a);
                Assert.True(Math.Abs(t.OpenBoundary - ExactPoint3.ScaleB(4.0, exp)) <= ExactPoint3.ScaleB(1e-12, exp), $"exp {exp}: OpenBoundary {t.OpenBoundary}");
            }

            // 4. Extreme span / alpha (> 2^1072) where workAlpha underflows to 0: zero-volume closed plate must still classify as thin
            var zeroVolPlate = new List<Vector3d> {
                // top face (+z normal) and coincident bottom face (-z normal) forming a closed zero-volume 2-sided square
                new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0),
                new Vector3d(0, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0),
                new Vector3d(0, 0, 0), new Vector3d(1, 1, 0), new Vector3d(1, 0, 0),
                new Vector3d(0, 0, 0), new Vector3d(0, 1, 0), new Vector3d(1, 1, 0)
            };
            AlphaWrapEstimate.EstimateTerms tUnderflow = AlphaWrapEstimate.Terms(ScaleAndShift(zeroVolPlate, 600, Vector3d.Zero), ExactPoint3.ScaleB(1.0, -475));
            Assert.Equal(0.0, tUnderflow.OpenArea);
            Assert.Equal(0.0, tUnderflow.ThickSharp);
            Assert.True(tUnderflow.ThinRim > 0, $"workAlpha underflow ThinRim {tUnderflow.ThinRim}");
        }
    }
}
