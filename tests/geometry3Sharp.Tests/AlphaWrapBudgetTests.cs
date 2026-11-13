using System;
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

        static MeshAlphaWrap Wrap(DMesh3 m, double relativeAlpha, int budget)
        {
            var w = new MeshAlphaWrap(m) { RelativeAlpha = relativeAlpha, RelativeOffset = 10 * relativeAlpha, MaxEstimatedTriangles = budget };
            Assert.True(w.Compute(), w.Error);
            return w;
        }

        // the estimate is EstimateDensity * area / alpha^2, computed also with the budget off; off, or a budget above the estimate,
        // changes nothing
        [Fact]
        public void Estimate_IsTheAreaOverAlphaSquared_ABudgetAboveItChangesNothing()
        {
            DMesh3 flat = Flat();
            double diag = flat.CachedBounds.DiagonalLength, alpha = diag / 50;
            MeshAlphaWrap off = Wrap(flat, 50, 0);
            double expected = MeshAlphaWrap.EstimateDensity * Invariance.Area(flat) / (alpha * alpha);
            Assert.True(Math.Abs(off.EstimatedTriangles - expected) <= 1e-12 * expected, $"{off.EstimatedTriangles} vs {expected}");
            Assert.Equal(1.0, off.AlphaCoarsening);
            Assert.Equal(alpha, off.UsedAlpha);
            MeshAlphaWrap above = Wrap(flat, 50, (int)Math.Ceiling(expected));
            Assert.Equal(1.0, above.AlphaCoarsening);
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(off.Result), AlphaWrapManifoldStepTests.Hash(above.Result));
            // (the estimate against the measured size of this input: 3.0 to 3.3 times area / alpha^2, EstimateDensity's comment)
            Assert.InRange(off.Result.TriangleCount / expected, 0.8, 1.3);
        }

        // the default (budget off) is bit for bit the wrap of master df429d6f, before the budget (hash computed there with the same
        // settings and the old API)
        [Fact]
        public void Default_Off_BitIdenticalToMaster()
        {
            var w = new MeshAlphaWrap(Flat()) { RelativeAlpha = 50, RelativeOffset = 500 };
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
            double factor = Math.Sqrt(w.EstimatedTriangles / budget);
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
            Assert.True(Math.Abs(w.UsedAlpha - expected) <= 1e-12 * expected, $"{w.UsedAlpha} vs {expected}");
            Assert.True(Math.Abs(w.UsedOffset - 0.1 * expected) <= 1e-12 * expected, $"{w.UsedOffset}");
            Assert.Equal(MeshAlphaWrap.WrapStatus.Success, w.Status);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            Assert.InRange(w.Result.TriangleCount, budget / 2, budget * 2);
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
                double expected = Math.Sqrt(MeshAlphaWrap.EstimateDensity * Invariance.Area(input) / 1500);
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
    }
}
