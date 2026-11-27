using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The raw-size budget of the alpha wrap (issue #694, Alpha Wrap 3.0 lever 2): MeshAlphaWrap.MaxEstimatedTriangles coarsens alpha
    /// and offset before the refinement when the size estimated from the input's area is above it; MeshRobustRepair passes it on
    /// (WrapMaxEstimatedTriangles) and reports the factor (AlphaWrapCoarsening). Off by default.
    /// </summary>
    public class AlphaWrapBudgetTests
    {
        // the input of issue #694: a closed sphere of 260 triangles flattened onto z = 0 (zero volume; the robust repair wraps it)
        static DMesh3 Flat()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1);
            foreach (int v in m.VertexIndices().ToList()) { Vector3d p = m.GetVertex(v); m.SetVertex(v, new Vector3d(p.x, p.y, 0)); }
            return m;
        }

        // the alpha at which the estimate is the budget, from the terms of the input (the oracle of the coarsening, written out here: the bulk
        // and the band go with 1 / alpha^2 and the rim with 1 / alpha, alpha and offset scaled together, ratio alpha / offset = ratio):
        // c^2 budget - rim alpha c - (density area + band) = 0 with c the coarsened alpha
        static double CoarseAlpha(DMesh3 m, double relativeAlpha, double ratio, double budget)
        {
            var corners = new List<Vector3d>();
            foreach (int t in m.TriangleIndices()) { Index3i tr = m.GetTriangle(t); corners.Add(m.GetVertex(tr.a)); corners.Add(m.GetVertex(tr.b)); corners.Add(m.GetVertex(tr.c)); }
            double alpha = m.GetBounds().DiagonalLength / relativeAlpha;
            AlphaWrapEstimate.EstimateTerms terms = AlphaWrapEstimate.Terms(corners, alpha);
            double sheet = terms.OpenArea + 0.5 * terms.ThinArea;
            double band = MeshAlphaWrap.SheetOffsetDensity * sheet * Math.Max(0.0, ratio - MeshAlphaWrap.SheetOffsetRatio);
            double rimAlpha = (MeshAlphaWrap.RimDensity * (terms.OpenBoundary + terms.ThinRim) + MeshAlphaWrap.SharpDensity * terms.ThickSharp) * ratio;
            double bulk = MeshAlphaWrap.EstimateDensity * (Invariance.Area(m) + terms.OpenArea) + band;
            return (rimAlpha + Math.Sqrt(rimAlpha * rimAlpha + 4 * budget * bulk)) / (2 * budget);
        }

        static MeshAlphaWrap Wrap(DMesh3 m, double relativeAlpha, int budget, double cap = 0)
        {
            var w = new MeshAlphaWrap(m) { RelativeAlpha = relativeAlpha, RelativeOffset = 10 * relativeAlpha, MaxEstimatedTriangles = budget, MaxAlphaCoarsening = cap };
            Assert.True(w.Compute(), w.Error);
            return w;
        }

        // the estimate is EstimateDensity * area / alpha^2 plus the offset band (EstimatedRimTriangles, issue #1593: the flattened sphere is a
        // plate of zero thickness, a sheet with a rim), computed also with the budget off; off, or a budget above the estimate, changes nothing
        [Fact]
        public void Estimate_IsTheAreaOverAlphaSquared_ABudgetAboveItChangesNothing()
        {
            DMesh3 flat = Flat();
            double diag = flat.CachedBounds.DiagonalLength, alpha = diag / 50;
            MeshAlphaWrap off = Wrap(flat, 50, 0);
            double expected = MeshAlphaWrap.EstimateDensity * Invariance.Area(flat) / (alpha * alpha) + off.EstimatedRimTriangles;
            Assert.True(off.EstimatedRimTriangles > 0);
            Assert.True(Math.Abs(off.EstimatedTriangles - expected) <= 1e-12 * expected, $"{off.EstimatedTriangles} vs {expected}");
            Assert.Equal(1.0, off.AlphaCoarsening);
            Assert.Equal(alpha, off.UsedAlpha);
            MeshAlphaWrap above = Wrap(flat, 50, (int)Math.Ceiling(expected));
            Assert.Equal(1.0, above.AlphaCoarsening);
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(off.Result), AlphaWrapManifoldStepTests.Hash(above.Result));
            // (the estimate against the measured size of this input: the bulk 3.0 to 3.3 times area / alpha^2, EstimateDensity's comment, and
            // the band and rim of the sheet-like input on top: 0.76 of it at alpha / offset 10)
            Assert.InRange(off.Result.TriangleCount / expected, 0.6, 1.3);
        }

        // the default (budget off) is bit for bit the wrap of master df429d6f, before the budget (hash computed there with the same
        // settings and the old API; with the serial carving, RoundWindow = 0, which that master had: the round-based carving of #774
        // gives another wrap)
        [Fact]
        public void Default_Off_BitIdenticalToMaster()
        {
            var w = new MeshAlphaWrap(Flat()) { RelativeAlpha = 50, RelativeOffset = 500, RoundWindow = 0 };
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(0, w.MaxEstimatedTriangles);
            Assert.Equal(1.0, w.AlphaCoarsening);
            Assert.Equal(6420, w.Result.TriangleCount);
            Assert.Equal("01DA0B9EE68DD6CC", AlphaWrapManifoldStepTests.Hash(w.Result));
            Assert.Equal(0, new MeshRobustRepair(Flat()).WrapMaxEstimatedTriangles);
        }

        // above the budget: alpha and offset coarsened by the same factor, sqrt(estimate / budget); the result is the complete wrap at
        // that alpha (bit for bit the wrap with UsedAlpha and UsedOffset given), clean, of about the budget's size
        [Fact]
        public void Budget_Coarsens_ToTheCompleteWrapAtTheLargerAlpha()
        {
            DMesh3 flat = Flat();
            double diag = flat.CachedBounds.DiagonalLength;
            const int budget = 6000;
            MeshAlphaWrap w = Wrap(flat, 100, budget);
            double coarse = CoarseAlpha(flat, 100, 10, budget), factor = coarse / (diag / 100);
            Assert.True(factor > 1.5, $"factor {factor}");
            Assert.True(Math.Abs(w.AlphaCoarsening - factor) <= 1e-12 * factor, $"{w.AlphaCoarsening} vs {factor}");
            Assert.Equal(w.AlphaCoarsening, w.UsedAlpha / (diag / 100));
            Assert.Equal(diag / 1000 * w.AlphaCoarsening, w.UsedOffset);
            Assert.Equal(MeshAlphaWrap.WrapStatus.Success, w.Status);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            Assert.InRange(w.Result.TriangleCount, budget / 2, budget * 2);
            var given = new MeshAlphaWrap(flat) { Alpha = w.UsedAlpha, Offset = w.UsedOffset };
            Assert.True(given.Compute(), given.Error);
            Assert.Equal(1.0, given.AlphaCoarsening);
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(given.Result), AlphaWrapManifoldStepTests.Hash(w.Result));
        }

        // an alpha whose square underflows (unit input, alpha 1e-170): the estimate is infinite, and the budget still applies (before the
        // fix the coarsening was skipped and the wrap ran until MaxSteinerPoints)
        [Fact]
        public void Budget_TinyAlpha_InfiniteEstimate_Coarsened()
        {
            DMesh3 flat = Flat();
            const int budget = 2000;
            var w = new MeshAlphaWrap(flat) { Alpha = 1e-170, Offset = 1e-171, MaxEstimatedTriangles = budget, MaxSteinerPoints = 100000 };
            Assert.True(w.Compute(), w.Error);
            Assert.True(double.IsPositiveInfinity(w.EstimatedTriangles), $"estimate {w.EstimatedTriangles}");
            double expected = Math.Sqrt(MeshAlphaWrap.EstimateDensity * Invariance.Area(flat) / budget);
            // (the band and the rim of the flat sheet make it coarser than the bulk alone: alpha / offset is 10 here, band 0.6 * (10 - 8) of the sheet)
            Assert.InRange(w.UsedAlpha / expected, 1.0, 1.4);
            Assert.True(Math.Abs(w.UsedOffset - 0.1 * w.UsedAlpha) <= 1e-12 * w.UsedAlpha, $"{w.UsedOffset}");
            Assert.Equal(MeshAlphaWrap.WrapStatus.Success, w.Status);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            Assert.InRange(w.Result.TriangleCount, budget / 2, budget * 2);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 3)]
        [InlineData(3, 1)]
        public void Budget_OverflowingFactor_KeepsFiniteOffset(int alphaMultiple, int offsetMultiple)
        {
            DMesh3 box = DegenerateFamilies.Box(Vector3d.Zero, Vector3d.One);
            var w = new MeshAlphaWrap(box) {
                Alpha = alphaMultiple * double.Epsilon, Offset = offsetMultiple * double.Epsilon,
                MaxEstimatedTriangles = 2000, MaxSteinerPoints = 100000, MaxThreads = 1
            };
            Assert.True(w.Compute(), w.Error);
            Assert.True(double.IsPositiveInfinity(w.AlphaCoarsening));
            Assert.Equal(offsetMultiple * w.UsedAlpha / alphaMultiple, w.UsedOffset);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            string hash = AlphaWrapManifoldStepTests.Hash(w.Result);

            var replay = new MeshAlphaWrap(box) { Alpha = w.UsedAlpha, Offset = w.UsedOffset };
            Assert.True(replay.Compute(), replay.Error);
            Assert.Equal(hash, AlphaWrapManifoldStepTests.Hash(replay.Result));
            w.MaxThreads = 2;
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(hash, AlphaWrapManifoldStepTests.Hash(w.Result));
        }

        [Fact]
        public void Budget_OverflowingFactor_RejectsUnrepresentableOffset()
        {
            DMesh3 box = DegenerateFamilies.Box(Vector3d.Zero, Vector3d.One);
            var w = new MeshAlphaWrap(box) {
                Alpha = double.Epsilon, Offset = double.MaxValue, MaxEstimatedTriangles = 2000
            };
            Assert.False(w.Compute());
            Assert.True(double.IsPositiveInfinity(w.AlphaCoarsening));
            Assert.True(double.IsPositiveInfinity(w.UsedOffset));
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidParameters, w.Status);
        }

        [Fact]
        public void Budget_OverflowingFactorAndRatio_KeepsFiniteOffset()
        {
            DMesh3 box = DegenerateFamilies.Box(Vector3d.Zero, Vector3d.One);
            var w = new MeshAlphaWrap(box) {
                Alpha = double.Epsilon, Offset = ExactPoint3.ScaleB(1.0, -47), MaxEstimatedTriangles = 2000
            };
            // Both ratios overflow. Only the later expansion of the box exceeds the coordinate range.
            Assert.True(double.IsPositiveInfinity(w.Offset / w.Alpha));
            Assert.False(w.Compute());
            Assert.True(double.IsPositiveInfinity(w.AlphaCoarsening));
            Assert.True(MathUtil.IsFinite(w.UsedOffset));
            Assert.Equal(ExactPoint3.ScaleB(w.UsedAlpha, 1027), w.UsedOffset);
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidInput, w.Status);
        }

        [Fact]
        public void Budget_OverflowingFactor_RejectsNonFiniteOrZeroOffset_AndReuse()
        {
            DMesh3 box = DegenerateFamilies.Box(Vector3d.Zero, Vector3d.One);
            var w = new MeshAlphaWrap(box) {
                Alpha = double.Epsilon, Offset = double.PositiveInfinity, MaxEstimatedTriangles = 2000, MaxThreads = 1
            };
            Assert.False(w.Compute());
            Assert.True(double.IsPositiveInfinity(w.AlphaCoarsening));
            Assert.True(double.IsPositiveInfinity(w.UsedOffset));
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidParameters, w.Status);

            w.Offset = 0;
            w.RelativeOffset = 0;
            Assert.False(w.Compute());
            Assert.True(double.IsPositiveInfinity(w.AlphaCoarsening));
            Assert.Equal(0.0, w.UsedOffset);
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidParameters, w.Status);

            // Reuse the same instance after overflowing-factor rejections: must match a fresh instance bit for bit.
            w.Offset = 2 * double.Epsilon;
            Assert.True(w.Compute(), w.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            var fresh = new MeshAlphaWrap(box) {
                Alpha = double.Epsilon, Offset = 2 * double.Epsilon, MaxEstimatedTriangles = 2000, MaxThreads = 1
            };
            Assert.True(fresh.Compute(), fresh.Error);
            Assert.Equal(fresh.UsedAlpha, w.UsedAlpha);
            Assert.Equal(fresh.UsedOffset, w.UsedOffset);
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(fresh.Result), AlphaWrapManifoldStepTests.Hash(w.Result));

            // Then reuse with budget off and ordinary parameters: no stale Infinity or coarsened values.
            w.Alpha = w.UsedAlpha;
            w.Offset = w.UsedOffset;
            w.MaxEstimatedTriangles = 0;
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(1.0, w.AlphaCoarsening);
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(fresh.Result), AlphaWrapManifoldStepTests.Hash(w.Result));
        }

        [Fact]
        public void Budget_OverflowingFactor_UsesFiniteCap()
        {
            DMesh3 box = DegenerateFamilies.Box(Vector3d.Zero, Vector3d.One);
            var w = new MeshAlphaWrap(box) {
                Alpha = double.Epsilon, Offset = 2, MaxEstimatedTriangles = 2000, MaxAlphaCoarsening = double.MaxValue
            };
            Assert.False(w.Compute());
            Assert.Equal(double.MaxValue, w.AlphaCoarsening);
            Assert.Equal(double.Epsilon * double.MaxValue, w.UsedAlpha);
            Assert.True(double.IsPositiveInfinity(w.UsedOffset));
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidParameters, w.Status);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Budget_OverflowingFactor_Invariance(string frame)
        {
            DMesh3 box = DegenerateFamilies.Box(Vector3d.Zero, Vector3d.One);
            double tiny = ExactPoint3.ScaleB(1.0, -1058);
            Similarity s = Similarity.ByName(frame);
            DMesh3 input = Invariance.Transformed(box, s);
            // 2 * a is exact for subnormal a, so Offset / Alpha == 2 bit for bit in every frame.
            double a = tiny * s.Scale;
            var transformed = new MeshAlphaWrap(input) {
                Alpha = a, Offset = 2 * a, MaxEstimatedTriangles = 2000
            };
            Assert.True(transformed.Compute(), transformed.Error);
            Assert.True(double.IsPositiveInfinity(transformed.AlphaCoarsening));
            Assert.Equal(2 * transformed.UsedAlpha, transformed.UsedOffset);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(transformed.Result));

            var origin = new MeshAlphaWrap(box) { Alpha = tiny, Offset = 2 * tiny, MaxEstimatedTriangles = 2000 };
            Assert.True(origin.Compute(), origin.Error);
            if (s.IsExact) {
                Assert.Equal(s.Scale * origin.UsedAlpha, transformed.UsedAlpha);
                Assert.Equal(s.Scale * origin.UsedOffset, transformed.UsedOffset);
                Assert.Equal("", Invariance.Difference(Invariance.Transformed(origin.Result, s), transformed.Result));
            } else {
                if (s == Similarity.Utm) {
                    Assert.Equal(origin.UsedAlpha, transformed.UsedAlpha);
                    Assert.Equal(origin.UsedOffset, transformed.UsedOffset);
                } else {
                    Assert.True(Math.Abs(transformed.UsedAlpha / s.Scale - origin.UsedAlpha) <= 1e-6 * origin.UsedAlpha,
                        $"{s.Name}: {transformed.UsedAlpha / s.Scale} vs {origin.UsedAlpha}");
                    Assert.True(Math.Abs(transformed.UsedOffset / s.Scale - origin.UsedOffset) <= 1e-6 * origin.UsedOffset,
                        $"{s.Name}: {transformed.UsedOffset / s.Scale} vs {origin.UsedOffset}");
                }
                var replay = new MeshAlphaWrap(input) { Alpha = transformed.UsedAlpha, Offset = transformed.UsedOffset };
                Assert.True(replay.Compute(), replay.Error);
                Assert.Equal("", Invariance.Difference(replay.Result, transformed.Result));
            }
        }

        // reuse: Compute() twice on one instance gives the same result; the outputs are recomputed (budget off again: not coarsened;
        // an empty input: no estimate)
        [Fact]
        public void Budget_Reuse()
        {
            DMesh3 flat = Flat();
            var w = new MeshAlphaWrap(flat) { RelativeAlpha = 100, RelativeOffset = 1000, MaxEstimatedTriangles = 6000 };
            Assert.True(w.Compute(), w.Error);
            string h = AlphaWrapManifoldStepTests.Hash(w.Result);
            double factor = w.AlphaCoarsening, alpha = w.UsedAlpha;
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(w.Result));
            Assert.Equal(factor, w.AlphaCoarsening);
            Assert.Equal(alpha, w.UsedAlpha);
            w.RelativeAlpha = 50; w.RelativeOffset = 500; w.MaxEstimatedTriangles = 0;
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(1.0, w.AlphaCoarsening);
            Assert.Equal(flat.CachedBounds.DiagonalLength / 50, w.UsedAlpha);
            w.Mesh = new DMesh3();
            Assert.False(w.Compute());
            Assert.Equal(MeshAlphaWrap.WrapStatus.EmptyInput, w.Status);
            Assert.Equal(0.0, w.EstimatedTriangles);
            Assert.Equal(1.0, w.AlphaCoarsening);
        }

        // invariance over Similarity.All(): the coarsened alpha does not depend on the frame (the 2^k scales: the estimate and the factor
        // bit for bit, and the result scaled bit for bit); every wrap is clean
        [Fact]
        public void Budget_Invariance()
        {
            DMesh3 input = Flat();
            MeshAlphaWrap w0 = Wrap(input, 50, 1500);
            Assert.True(w0.AlphaCoarsening > 1.5, $"factor {w0.AlphaCoarsening}");
            foreach (Similarity s in Similarity.All()) {
                DMesh3 m = Invariance.Transformed(input, s);
                MeshAlphaWrap w = Wrap(m, 50, 1500);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
                // (the relative alpha follows the axis-aligned box, which a rotation changes; the coarsened alpha does not depend on it:
                // alpha * sqrt(density * area / (alpha^2 * budget)) = sqrt(density * area / budget); 1e-6: the input's coordinates rounded
                // in the frame, 2.5e-8 measured in the rotated millimetre UTM frame)
                double expected = CoarseAlpha(input, 50, 10, 1500);
                Assert.True(Math.Abs(w.UsedAlpha / s.Scale - expected) <= 1e-6 * expected, $"{s.Name}: {w.UsedAlpha / s.Scale} vs {expected}");
                if (s.IsExact) {
                    Assert.Equal(w0.EstimatedTriangles, w.EstimatedTriangles);
                    Assert.Equal(w0.AlphaCoarsening, w.AlphaCoarsening);
                    Assert.Equal(w0.Result.TriangleCount, w.Result.TriangleCount);
                    Assert.True(w0.Result.VertexIndices().All(v => w.Result.GetVertex(v) == s.Apply(w0.Result.GetVertex(v))), s.Name);
                }
            }
        }

        // MeshRobustRepair: the budget reaches the wrap fallback, the factor is reported, and reset by the next Apply()
        [Fact]
        public void RobustRepair_Budget_CoarsensTheWrapAndReportsIt()
        {
            const int budget = 8000;
            // (a coarse solidify: it fails on zero volume at any resolution, and the test is about the wrap)
            var r = new MeshRobustRepair(Flat()) { WrapMaxEstimatedTriangles = budget, SolidifyResolution = 16 };
            Assert.True(r.Apply(), r.ResultReason);
            Assert.True(r.UsedAlphaWrap, r.ResultReason);
            Assert.True(r.AlphaWrapCoarsening > 2, $"factor {r.AlphaWrapCoarsening}");
            Assert.Equal(r.AlphaWrap.AlphaCoarsening, r.AlphaWrapCoarsening);
            // (unbudgeted: 211,614 raw triangles, issue #694)
            Assert.InRange(r.AlphaWrapTriangles, budget / 2, budget * 2);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            // the next Apply() on an input that needs no wrap: not coarsened
            r.Mesh = Defects.Sphere(Vector3d.Zero, 1);
            Assert.True(r.Apply(), r.ResultReason);
            Assert.False(r.UsedAlphaWrap);
            Assert.Equal(1.0, r.AlphaWrapCoarsening);
        }

        // the cap on the coarsening (MaxAlphaCoarsening): the factor is at most the cap, alpha and offset are the requested ones times
        // it, the result is the complete clean wrap at that alpha (bit for bit the wrap with UsedAlpha and UsedOffset given), above the
        // budget; a cap that the budget does not reach, 0 (the default) and a value below 1 (counts as 1) are as documented
        [Fact]
        public void Cap_LimitsTheFactor_TheWrapIsTheCompleteWrapAtThatAlpha()
        {
            DMesh3 flat = Flat();
            double diag = flat.CachedBounds.DiagonalLength;
            const int budget = 1000;
            MeshAlphaWrap free = Wrap(flat, 50, budget);
            Assert.True(free.AlphaCoarsening > 2.0, $"factor {free.AlphaCoarsening}");
            Assert.Equal(0.0, free.MaxAlphaCoarsening);
            MeshAlphaWrap capped = Wrap(flat, 50, budget, 1.25);
            Assert.Equal(1.25, capped.AlphaCoarsening);
            Assert.Equal(diag / 50 * 1.25, capped.UsedAlpha);
            Assert.Equal(diag / 500 * 1.25, capped.UsedOffset);
            Assert.Equal(free.EstimatedTriangles, capped.EstimatedTriangles);
            Assert.Equal(MeshAlphaWrap.WrapStatus.Success, capped.Status);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(capped.Result));
            Assert.True(capped.Result.TriangleCount > 2 * budget, $"{capped.Result.TriangleCount}: the cap leaves the result above the budget");
            Assert.True(capped.Result.TriangleCount > free.Result.TriangleCount);
            var given = new MeshAlphaWrap(flat) { Alpha = capped.UsedAlpha, Offset = capped.UsedOffset };
            Assert.True(given.Compute(), given.Error);
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(given.Result), AlphaWrapManifoldStepTests.Hash(capped.Result));
            // (a cap above the factor the budget asks for, and 0: the free result)
            string h = AlphaWrapManifoldStepTests.Hash(free.Result);
            MeshAlphaWrap high = Wrap(flat, 50, budget, free.AlphaCoarsening * 2);
            Assert.Equal(free.AlphaCoarsening, high.AlphaCoarsening);
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(high.Result));
            // (a cap just at the factor, and the next value below it)
            MeshAlphaWrap atFactor = Wrap(flat, 50, budget, free.AlphaCoarsening);
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(atFactor.Result));
            Assert.Equal(free.UsedAlpha, atFactor.UsedAlpha);   // (the uncapped alpha bit for bit, not requested alpha * factor)
            Assert.Equal(free.UsedOffset, atFactor.UsedOffset);
            Assert.Equal(free.AlphaCoarsening * 0.999, Wrap(flat, 50, budget, free.AlphaCoarsening * 0.999).AlphaCoarsening);
            // (a value below 1 counts as 1: not coarsened, the wrap of the requested alpha)
            MeshAlphaWrap one = Wrap(flat, 50, budget, 0.5);
            Assert.Equal(1.0, one.AlphaCoarsening);
            Assert.Equal(diag / 50, one.UsedAlpha);
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(Wrap(flat, 50, 0).Result), AlphaWrapManifoldStepTests.Hash(one.Result));
            // (the cap does nothing without a budget)
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(Wrap(flat, 50, 0).Result), AlphaWrapManifoldStepTests.Hash(Wrap(flat, 50, 0, 1.25).Result));
        }

        // reuse: Compute() twice with the cap gives the same result; the factor follows the cap that is set at each call
        [Fact]
        public void Cap_Reuse()
        {
            DMesh3 flat = Flat();
            var w = new MeshAlphaWrap(flat) { RelativeAlpha = 50, RelativeOffset = 500, MaxEstimatedTriangles = 1000, MaxAlphaCoarsening = 1.25 };
            Assert.True(w.Compute(), w.Error);
            string h = AlphaWrapManifoldStepTests.Hash(w.Result);
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(w.Result));
            Assert.Equal(1.25, w.AlphaCoarsening);
            w.MaxAlphaCoarsening = 0;
            Assert.True(w.Compute(), w.Error);
            Assert.True(w.AlphaCoarsening > 2.0, $"{w.AlphaCoarsening}");
            w.MaxAlphaCoarsening = 1.5;
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(1.5, w.AlphaCoarsening);
        }

        // invariance over Similarity.All() with the cap active: the factor is the cap in every frame (the requested alpha follows the
        // box, the factor does not); at the 2^k scales the result is the scaled result bit for bit; every wrap is clean
        [Fact]
        public void Cap_Invariance()
        {
            DMesh3 input = Flat();
            MeshAlphaWrap w0 = Wrap(input, 50, 1000, 1.25);
            foreach (Similarity s in Similarity.All()) {
                MeshAlphaWrap w = Wrap(Invariance.Transformed(input, s), 50, 1000, 1.25);
                Assert.Equal(1.25, w.AlphaCoarsening);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
                if (s.IsExact) {
                    Assert.Equal(w0.Result.TriangleCount, w.Result.TriangleCount);
                    Assert.True(w0.Result.VertexIndices().All(v => w.Result.GetVertex(v) == s.Apply(w0.Result.GetVertex(v))), s.Name);
                }
            }
        }

        // MeshRobustRepair passes the cap on (WrapMaxAlphaCoarsening, default 0)
        [Fact]
        public void RobustRepair_Cap_ReachesTheWrap()
        {
            Assert.Equal(0.0, new MeshRobustRepair(Flat()).WrapMaxAlphaCoarsening);
            var r = new MeshRobustRepair(Flat()) { WrapMaxEstimatedTriangles = 1000, WrapMaxAlphaCoarsening = 1.5, SolidifyResolution = 16 };
            Assert.True(r.Apply(), r.ResultReason);
            Assert.True(r.UsedAlphaWrap, r.ResultReason);
            Assert.Equal(1.5, r.AlphaWrapCoarsening);
            Assert.Equal(1.5, r.AlphaWrap.MaxAlphaCoarsening);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
        }
    }
}
