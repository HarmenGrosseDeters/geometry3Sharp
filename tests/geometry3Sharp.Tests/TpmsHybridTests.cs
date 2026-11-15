using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The two-type hybrid of TpmsLattice (docs/design/TPMS_LATTICE.md section 9): h = w (g_A - c_A) + (1 - w) (g_B - c_B) with the sigmoid
    /// weight of a single plane. The field here is an independent copy; the library is only asked for its levels (tables) and reports.
    /// </summary>
    public class TpmsHybridTests
    {
        const double L = 1.0;

        // independent copies of the gyroid and the I-WP (sin / cos squared forms)
        static double F(TpmsType type, Vector3d q)
        {
            double a = 2 * Math.PI * q.x / L, b = 2 * Math.PI * q.y / L, c = 2 * Math.PI * q.z / L;
            double ca = Math.Cos(a), cb = Math.Cos(b), cc = Math.Cos(c), sa = Math.Sin(a), sb = Math.Sin(b), sc = Math.Sin(c);
            if (type == TpmsType.Gyroid) return sa * cb + sb * cc + sc * ca;
            return 2 * (ca * cb + cb * cc + cc * ca) - (2 * ca * ca - 1 + 2 * cb * cb - 1 + 2 * cc * cc - 1);
        }

        static double G(TpmsType type, TpmsStyle style, Vector3d q)
        {
            double f = F(type, q);
            return style == TpmsStyle.Sheet ? Math.Abs(f) : (style == TpmsStyle.SkeletalLow ? f : -f);
        }

        static double LevelOf(TpmsType type, TpmsStyle style, double fraction)
            => TpmsLattice.LevelForFraction(TpmsLattice.QuantileTable(type, style), fraction);

        // the weight of the plane x = offset written out (w = 1 / (1 + exp(-4 eta / width)), no overflow for the sampled range)
        static double W(double x, double offset, double width)
        {
            double z = 4 * (x - offset) / width;
            return z >= 0 ? 1 / (1 + Math.Exp(-z)) : Math.Exp(z) / (1 + Math.Exp(z));
        }

        // the exact hybrid h of the test, in phase units (not normalised)
        static double H(TpmsType a, TpmsType b, TpmsStyle style, double ca, double cb, double offset, double width, Vector3d q)
        {
            double w = W(q.x, offset, width);
            return w * (G(a, style, q) - ca) + (1 - w) * (G(b, style, q) - cb);
        }

        static TpmsLattice Make(TpmsType a, TpmsType b, TpmsStyle style, double fraction, double offset, double width, int divisions = 14, double sx = 4, double sy = 2)
        {
            return new TpmsLattice {
                Type = a, SecondType = b, Blend = TpmsLattice.SigmoidBlend(0, offset, width), Style = style, CellSize = L, Divisions = divisions,
                VolumeFraction = fraction, Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(sx, sy, sy))
            };
        }

        static void AssertOk(TpmsLattice lattice)
        {
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
            Assert.True(Invariance.AllFinite(lattice.Mesh));
        }

        [Fact]
        public void Weight_OfOneIsThePureFirstType_AndOfZeroThePureSecondType()
        {
            // w = 1 everywhere with a zero gradient bound: the bound and the field of the larger type are the pure ones, so the mesh is the pure mesh bit for bit.
            // Also the sliver guard must be the pure one: it uses the larger of the two GradientRatios, which is the I-WP's (pinned here, so a change of the ratios fails loudly)
            Assert.True(TpmsLattice.GradientRatio(TpmsType.IWP) >= TpmsLattice.GradientRatio(TpmsType.Gyroid));
            foreach (TpmsStyle style in new[] { TpmsStyle.Sheet, TpmsStyle.SkeletalLow, TpmsStyle.SkeletalHigh }) {
                var pure = new TpmsLattice { Type = TpmsType.IWP, Style = style, CellSize = L, Divisions = 14, VolumeFraction = 0.35, Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 2)) };
                pure.Compute();
                AssertOk(pure);
                var one = new TpmsLattice { Type = TpmsType.IWP, SecondType = TpmsType.Gyroid, Blend = new TpmsBlendWeight(p => 1.0, 0), Style = style, CellSize = L, Divisions = 14, VolumeFraction = 0.35, Box = pure.Box };
                one.Compute();
                AssertOk(one);
                Assert.Equal("", Invariance.Difference(pure.Mesh, one.Mesh));
                var zero = new TpmsLattice { Type = TpmsType.Gyroid, SecondType = TpmsType.IWP, Blend = new TpmsBlendWeight(p => 0.0, 0), Style = style, CellSize = L, Divisions = 14, VolumeFraction = 0.35, Box = pure.Box };
                zero.Compute();
                AssertOk(zero);
                Assert.Equal("", Invariance.Difference(pure.Mesh, zero.Mesh));
                Assert.Equal(pure.Level, zero.SecondLevel);
                Assert.Equal(pure.Level, one.Level);
                // the two pure types differ (the test would pass for a hybrid that ignores the weight otherwise)
                var gyroid = new TpmsLattice { Type = TpmsType.Gyroid, Style = style, CellSize = L, Divisions = 14, VolumeFraction = 0.35, Box = pure.Box };
                gyroid.Compute();
                Assert.NotEqual("", Invariance.Difference(pure.Mesh, gyroid.Mesh));
            }
        }

        [Theory]
        [InlineData(TpmsStyle.Sheet, 0.25)]
        [InlineData(TpmsStyle.SkeletalLow, 1.0)]
        [InlineData(TpmsStyle.SkeletalHigh, 0.5)]
        public void Hybrid_IsCleanAndEachSideHasItsTargetFraction(TpmsStyle style, double width)
        {
            // gyroid for x > 2 (eta > 0), I-WP for x < 2, both at 0.3: away from the transition each half has the fraction of the pure lattice
            var lattice = Make(TpmsType.Gyroid, TpmsType.IWP, style, 0.3, 2.0, width);
            lattice.Compute();
            AssertOk(lattice);
            var tree = new DMeshAABBTree3(lattice.Mesh, true);
            var rand = new Random(41);
            int nLow = 0, low = 0, nHigh = 0, high = 0;
            for (int i = 0; i < 9000; ++i) {
                var q = new Vector3d(0.3 + 3.4 * rand.NextDouble(), 0.3 + 1.4 * rand.NextDouble(), 0.3 + 1.4 * rand.NextDouble());
                bool inside = tree.IsInside(q);
                if (q.x < 1.2) { ++nLow; if (inside) ++low; }
                if (q.x > 2.8) { ++nHigh; if (inside) ++high; }
            }
            Assert.InRange((double)low / nLow, 0.3 - 0.06, 0.3 + 0.06);
            Assert.InRange((double)high / nHigh, 0.3 - 0.06, 0.3 + 0.06);
            Assert.InRange(lattice.RealizedVolumeFraction, 0.2, 0.4);
            // both halves really are different lattices: a mesh of the pure gyroid has the same fraction but another surface
            Assert.Equal(LevelOf(TpmsType.Gyroid, style, 0.3), lattice.Level);
            Assert.Equal(LevelOf(TpmsType.IWP, style, 0.3), lattice.SecondLevel);
        }

        [Fact]
        public void Hybrid_MatchesAnIndependentFieldAwayFromTheSurface()
        {
            // a winding-number oracle: points of the independent exact hybrid well inside / outside (by a margin of 3/4 cube) are
            // inside / outside the mesh, over the transition (distance estimate: first order, a heuristic oracle with a margin of 3/4 cube)
            const double offset = 2, width = 0.6;
            foreach (var (style, fraction, divisions) in new[] { (TpmsStyle.Sheet, 0.5, 20), (TpmsStyle.SkeletalLow, 0.3, 16), (TpmsStyle.SkeletalHigh, 0.4, 16) }) {
                var lattice = Make(TpmsType.Gyroid, TpmsType.IWP, style, fraction, offset, width, divisions);
                lattice.Compute();
                AssertOk(lattice);
                double ca = LevelOf(TpmsType.Gyroid, style, fraction), cb = LevelOf(TpmsType.IWP, style, fraction), h = L / divisions;
                var tree = new DMeshAABBTree3(lattice.Mesh, true);
                var rand = new Random(5);
                int inside = 0, outside = 0;
                const double margin = 0.75;   // cubes: the mesh surface is within a fraction of a cube of the exact one
                for (int i = 0; i < 60000 && inside + outside < 2400; ++i) {
                    var q = new Vector3d(1 + 2 * rand.NextDouble(), 2 * h + (2 - 4 * h) * rand.NextDouble(), 2 * h + (2 - 4 * h) * rand.NextDouble());
                    // first-order distance to the exact surface (value over the central-difference gradient; the proven bound is too loose for a distance)
                    double Hf(Vector3d p) => H(TpmsType.Gyroid, TpmsType.IWP, style, ca, cb, offset, width, p);
                    const double e = 1e-5;
                    double gx = (Hf(q + new Vector3d(e, 0, 0)) - Hf(q - new Vector3d(e, 0, 0))) / (2 * e);
                    double gy = (Hf(q + new Vector3d(0, e, 0)) - Hf(q - new Vector3d(0, e, 0))) / (2 * e);
                    double gz = (Hf(q + new Vector3d(0, 0, e)) - Hf(q - new Vector3d(0, 0, e))) / (2 * e);
                    double f = Hf(q) / Math.Max(Math.Sqrt(gx * gx + gy * gy + gz * gz), 1e-9);
                    if (f < -margin * h) { ++inside; Assert.True(tree.IsInside(q), "an interior point " + q + " is outside"); }
                    else if (f > margin * h) { ++outside; Assert.False(tree.IsInside(q), "an exterior point " + q + " is inside"); }
                }
                Assert.True(inside > 150 && outside > 150, style + ": " + inside + " / " + outside);
            }
        }

        [Fact]
        public void LipschitzBound_IsTheDerivedOne_AndBoundsTheSampledGradient()
        {
            // Lh = (2 pi / L) max(G_A, G_B) + (M_A + |c_A| + M_B + |c_B|) Wmax with the closed-form constants written here (gyroid 2 sqrt 3 / 3, I-WP 6 sqrt 3 / 9)
            foreach (double width in new[] { 0.5, 0.02 }) {
                var lattice = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.Sheet, 0.3, 2.0, width, 14);
                lattice.Compute();
                AssertOk(lattice);
                double ca = LevelOf(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3), cb = LevelOf(TpmsType.IWP, TpmsStyle.Sheet, 0.3);
                double expected = 2 * Math.PI * 6 * Math.Sqrt(3) + (3 + Math.Abs(ca) + 9 + Math.Abs(cb)) / width;
                Assert.Equal(expected, lattice.LipschitzBoundUsed, 9);
                // the sampled gradient of the exact hybrid (central differences) stays below the bound: a bound without the weight term fails for the steep weight
                double max = 0;
                var rand = new Random(3);
                const double e = 1e-6;
                for (int i = 0; i < 40000; ++i) {
                    var q = new Vector3d(2 + (rand.NextDouble() - 0.5) * 6 * width, rand.NextDouble(), rand.NextDouble());
                    double Hf(Vector3d p) => H(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.Sheet, ca, cb, 2.0, width, p);
                    double gx = (Hf(q + new Vector3d(e, 0, 0)) - Hf(q - new Vector3d(e, 0, 0))) / (2 * e);
                    double gy = (Hf(q + new Vector3d(0, e, 0)) - Hf(q - new Vector3d(0, e, 0))) / (2 * e);
                    double gz = (Hf(q + new Vector3d(0, 0, e)) - Hf(q - new Vector3d(0, 0, e))) / (2 * e);
                    max = Math.Max(max, Math.Sqrt(gx * gx + gy * gy + gz * gz));
                }
                Assert.True(max <= lattice.LipschitzBoundUsed, "sampled gradient " + max + " above the bound " + lattice.LipschitzBoundUsed);
                if (width < 0.1) Assert.True(max > 2 * 2 * Math.PI * 6 * Math.Sqrt(3) * 0.2, "the steep weight must dominate the sampled gradient: " + max);
            }
        }

        [Fact]
        public void Enclosure_ContainsTheExactHybrid_WithASteepWeight()
        {
            // a steep weight makes the weight term of the bound matter (width 0.1: Wmax 10); dyadic grid, offsets and widths
            foreach (TpmsStyle style in new[] { TpmsStyle.Sheet, TpmsStyle.SkeletalLow }) {
                var lattice = Make(TpmsType.Gyroid, TpmsType.IWP, style, 0.3, 2.0, 0.125, 16);
                lattice.Enclosure = true;
                lattice.Compute();
                AssertOk(lattice);
                Assert.True(lattice.IsoValueUsed > Math.Sqrt(3) * (L / 16) / 2);
                double ca = LevelOf(TpmsType.Gyroid, style, 0.3), cb = LevelOf(TpmsType.IWP, style, 0.3), h = L / 16;
                var tree = new DMeshAABBTree3(lattice.Mesh, true);
                var rand = new Random(8);
                int tested = 0;
                for (int i = 0; i < 400000 && tested < 3000; ++i) {
                    var q = new Vector3d(1.5 + rand.NextDouble(), 0.25 + 1.5 * rand.NextDouble(), 0.25 + 1.5 * rand.NextDouble());
                    double g = H(TpmsType.Gyroid, TpmsType.IWP, style, ca, cb, 2.0, 0.125, q) / lattice.LipschitzBoundUsed;
                    if (g > 0 || g < -0.1 * h) continue;
                    ++tested;
                    Assert.True(tree.IsInside(q), "a point " + q + " of the exact hybrid is outside the enclosure (g " + g + ")");
                }
                Assert.True(tested >= 750, "only " + tested + " near-surface points");
                var plain = Make(TpmsType.Gyroid, TpmsType.IWP, style, 0.3, 2.0, 0.125, 16);
                plain.Compute();
                Assert.True(lattice.RealizedVolumeFraction > plain.RealizedVolumeFraction, "the enclosure is larger than the target");
            }
        }

        [Fact]
        public void Enclosure_LevelPadUsesTheLargerGradientRatioOfTheTwoTypes()
        {
            // the sliver guard of a hybrid is sized by the larger of the two per-type ratios (a larger guard only grows the solid) and scaled by the phase part of the
            // Lipschitz bound over the whole bound (the weight term does not make the lattice's own gradient larger); the level pays 2 Snap
            double ratio = Math.Max(TpmsLattice.GradientRatio(TpmsType.Gyroid), TpmsLattice.GradientRatio(TpmsType.IWP));
            Assert.NotEqual(TpmsLattice.GradientRatio(TpmsType.Gyroid), TpmsLattice.GradientRatio(TpmsType.IWP));
            foreach (var (a, b) in new[] { (TpmsType.Gyroid, TpmsType.IWP), (TpmsType.IWP, TpmsType.Gyroid) }) {
                var lattice = Make(a, b, TpmsStyle.SkeletalLow, 0.3, 1.0, 0.5, 16, 2, 2);
                lattice.Enclosure = true;
                lattice.Compute();
                AssertOk(lattice);
                double h = L / 16, scale = 2 * Math.PI * 6 * Math.Sqrt(3) / lattice.LipschitzBoundUsed;
                Assert.InRange(scale, 0.5, 0.999);   // the weight term is part of the bound
                Assert.True(lattice.IsoValueUsed >= Math.Sqrt(3) * h / 2 + 2 * h / 256 * ratio * scale + 4 * Math.Pow(2, -26), a + ": level " + lattice.IsoValueUsed);
                Assert.True(lattice.IsoValueUsed <= Math.Sqrt(3) * h / 2 + 2 * h / 256 * ratio * scale + 4 * Math.Pow(2, -22), a + ": level " + lattice.IsoValueUsed);
            }
        }

        [Fact]
        public void Enclosure_LevelIsTheHybridFormula_NotTheSingleTypeOne()
        {
            // IsoValueUsed = sqrt(3) h / 2 + 2 Snap + s (2^-26 + 4 eNorm) recomputed from the derivation for a steep weight (width 1/128: Wmax 128), where the hybrid error term
            // differs from the single type one (a mutant that calls EnclosureError(Type, ...) instead moves the level by more than 1e-15)
            const double h = 1.0 / 16, width = 1.0 / 128, s = 4;   // box 2 x 2 x 2 with 16 cubes per period: the grid reaches 37 h < 4
            var lattice = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.SkeletalLow, 0.3, 1.0, width, 16, 2, 2);
            lattice.Enclosure = true;
            lattice.Compute();
            AssertOk(lattice);
            double ca = LevelOf(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.3), cb = LevelOf(TpmsType.IWP, TpmsStyle.SkeletalLow, 0.3);
            double msum = TpmsLattice.TermSum(TpmsType.Gyroid) + Math.Abs(ca) + TpmsLattice.TermSum(TpmsType.IWP) + Math.Abs(cb), wmax = 1 / width;
            double lw = 2 * Math.PI * 6 * Math.Sqrt(3) + msum * wmax;
            Assert.Equal(lw, lattice.LipschitzBoundUsed, 9);
            double ratio = Math.Max(TpmsLattice.GradientRatio(TpmsType.Gyroid), TpmsLattice.GradientRatio(TpmsType.IWP));
            double snap = h / 256 * Math.Max(ratio, 1.0 / 16) * (2 * Math.PI * 6 * Math.Sqrt(3) / lw);
            double eNorm = TpmsLattice.EnclosureErrorBlend(TpmsType.Gyroid, TpmsType.IWP, lw, s, false, 0, 1, h, msum, wmax);
            double expected = Math.Sqrt(3) * h / 2 + 2 * snap + s * (Math.Pow(2, -26) + 4 * eNorm);
            Assert.Equal(expected, lattice.IsoValueUsed, 15);
            double single = Math.Sqrt(3) * h / 2 + 2 * snap + s * (Math.Pow(2, -26) + 4 * TpmsLattice.EnclosureError(TpmsType.Gyroid, lw, s, false, 0, 1, h));
            Assert.True(Math.Abs(expected - single) > 2e-15, "the test must tell the two error terms apart: " + (expected - single));
        }

        [Fact]
        public void Enclosure_KeepsATinyExactIslandAtTheWorstPlaceOfTheWeightTerm()
        {
            // The worst case of the weight term, as Recipe_LevelEnclosesAnExactSolidAtTheWorstPlaces does for the single point: a weight that is a steep cone around a cube centre c0
            // (w = 1 - |p - c0| / rho, rho much smaller than a cube, gradient bound 1 / rho) turns a point where the gyroid is below its level (a > 0) inside a cavity of the I-WP (b > 0)
            // into an exact solid {h <= 0}, an island of radius rho (1 - b / (a + b)), about h / 2000. All eight nodes of the cube see w = 0, so h = b there, the same as the
            // single point case. The island is contained only if Lh includes the weight term (h rises with slope (a + b) / rho towards the nodes); a bound without it, or the lying
            // bound 0, loses the island: the guarantee rests on the bound.
            const double h = 1.0 / 16, rho = 2.6e-4;
            var c0 = new Vector3d(14 * h, 14 * h, 14 * h);      // a cube centre of the first (dyadic, half-cube shifted) grid, inside the 2 x 2 x 2 box
            double ca = LevelOf(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.3), cb = LevelOf(TpmsType.IWP, TpmsStyle.SkeletalLow, 0.3);
            double a = F(TpmsType.Gyroid, c0) - ca, b = F(TpmsType.IWP, c0) - cb;
            Assert.True(a < -0.2, "the gyroid must be below its level at c0: " + a);
            // the nodes of the cube (and a margin) must be high for the bound without the weight term: b / Lh_phase above the level pad
            double lhPhase = 2 * Math.PI * 6 * Math.Sqrt(3), minB = double.MaxValue;
            foreach (double dx in new[] { -0.5, 0.5 }) foreach (double dy in new[] { -0.5, 0.5 }) foreach (double dz in new[] { -0.5, 0.5 })
                minB = Math.Min(minB, F(TpmsType.IWP, c0 + new Vector3d(dx, dy, dz) * h) - cb);
            Assert.True(minB / lhPhase > 1.1 * (Math.Sqrt(3) * h / 2 + 0.002), "the nodes must be above the level pad of the bound without the weight term: " + minB);
            Func<Vector3d, double> cone = p => Math.Max(0.0, 1 - (p - c0).Length / rho);
            double radius = rho * (1 - b / (b - a));   // w* = b / (a + b) with a < 0 here (a is g_A - c_A), so b - a = a_abs + b
            Assert.InRange(radius, h / 4000, h / 500);
            foreach (var (bound, expectInside) in new[] { (1 / rho, true), (0.0, false) }) {
                var lattice = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.SkeletalLow, 0.3, 0, 1, 16, 2, 2);
                lattice.Blend = new TpmsBlendWeight(cone, bound);
                lattice.Enclosure = true;
                lattice.Compute();
                AssertOk(lattice);
                var tree = new DMeshAABBTree3(lattice.Mesh, true);
                Assert.Equal(expectInside, tree.IsInside(c0));
                if (expectInside) Assert.True(lattice.LipschitzBoundUsed > 500 * lhPhase, "the weight term must dominate the bound: " + lattice.LipschitzBoundUsed);
            }
        }

        [Fact]
        public void EnclosureError_OfTheHybridFollowsTheDerivation()
        {
            // the hybrid terms: 64 u max(TermSum) + (6 u combination + 8 u + 2 u s Wmax weight) msum, over (Lw s), plus the phase part of the single type; the values are
            // of the order 1e-17, so the comparison is relative
            double u = Math.Pow(2, -53), lw = 80, s = 4, msum = 25, wmax = 8;
            double expected = (64 * u * 9 + (6 * u + 8 * u + 2 * u * s * wmax) * msum) / (lw * s) + 4 * u;
            double actual = TpmsLattice.EnclosureErrorBlend(TpmsType.Gyroid, TpmsType.IWP, lw, s, false, 0, 1, 0.1, msum, wmax);
            Assert.InRange(actual / expected, 1 - 1e-13, 1 + 1e-13);
            // without a weight term and without the combination (msum 0) it is the single type error of the larger term sum
            double single = TpmsLattice.EnclosureError(TpmsType.IWP, lw, s, false, 0, 1, 0.1);
            Assert.InRange(TpmsLattice.EnclosureErrorBlend(TpmsType.Gyroid, TpmsType.IWP, lw, s, false, 0, 1, 0.1, 0, 0) / single, 1 - 1e-13, 1 + 1e-13);
            // each term counts: the combination (msum), the weight value (8 u) and the coordinate rounding (Wmax)
            double e0 = TpmsLattice.EnclosureErrorBlend(TpmsType.Gyroid, TpmsType.IWP, lw, s, false, 0, 1, 0.1, msum, 0);
            Assert.True(TpmsLattice.EnclosureErrorBlend(TpmsType.Gyroid, TpmsType.IWP, lw, s, false, 0, 1, 0.1, msum, 16) > TpmsLattice.EnclosureErrorBlend(TpmsType.Gyroid, TpmsType.IWP, lw, s, false, 0, 1, 0.1, msum, 8));
            Assert.InRange((e0 - single) / (14 * u * msum / (lw * s)), 1 - 1e-6, 1 + 1e-6);
        }

        [Theory]
        [InlineData(TpmsStyle.Sheet, false)]
        [InlineData(TpmsStyle.SkeletalHigh, false)]
        [InlineData(TpmsStyle.SkeletalLow, true)]
        public void Compute_IsInvariantUnderSimilarities(TpmsStyle style, bool enclosure)
        {
            // offset and width scale with the lattice (length units), the plane lives in the lattice frame: the 2^k scales give the mesh bit for bit.
            // Enclosure needs a dyadic grid, so only the power-of-two scales (and rotations / translations of the frame) run with it.
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All()) {
                double lg = Math.Log(s.Scale, 2);
                if (enclosure && Math.Abs(lg - Math.Round(lg)) > 1e-12) continue;
                var lattice = Make(TpmsType.Gyroid, TpmsType.IWP, style, 0.3, 0.75, 0.5, enclosure ? 16 : 12, 1.5, 1.5);   // 16: a dyadic cube for Enclosure
                lattice.CellSize = s.Scale * L;
                lattice.Blend = TpmsLattice.SigmoidBlend(0, 0.75 * s.Scale, 0.5 * s.Scale);
                lattice.Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1.5 * s.Scale, 1.5 * s.Scale, 1.5 * s.Scale));
                lattice.Origin = s.Apply(Vector3d.Zero);
                lattice.Rotation = s.Rotation;
                lattice.Enclosure = enclosure;
                lattice.Compute();
                AssertOk(lattice);
                if (reference == null) { reference = lattice; continue; }
                Assert.Equal(reference.GridShiftsUsed, lattice.GridShiftsUsed);
                Assert.Equal(reference.Mesh.TriangleCount, lattice.Mesh.TriangleCount);
                Assert.Equal(reference.RealizedVolumeFraction, lattice.RealizedVolumeFraction, 9);
                DMesh3 back = Invariance.MappedBack(lattice.Mesh, s);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference.Mesh, back));
                else {
                    foreach (int vid in back.VertexIndices())
                        Assert.True((back.GetVertex(vid) - reference.Mesh.GetVertex(vid)).Length < 1e-6 * L, s.Name);
                }
            }
            Assert.NotNull(reference);
        }

        [Fact]
        public void Compute_IsInvariantUnderPowerOfTwoScales_WithAGradeAndAnAxisTwoBlend()
        {
            // the grade (a library-derived Jacobian bound) and a blend across z: every length parameter scales with the lattice, the mesh is the same bit for bit
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                var lattice = new TpmsLattice {
                    Type = TpmsType.IWP, SecondType = TpmsType.Gyroid, Blend = TpmsLattice.SigmoidBlend(2, 1.0 * s.Scale, 0.25 * s.Scale), Style = TpmsStyle.SkeletalLow,
                    CellSize = s.Scale * L, Divisions = 14, VolumeFraction = 0.35,
                    Box = new AxisAlignedBox3d(new Vector3d(-1, -1, 0) * s.Scale, new Vector3d(1, 1, 2) * s.Scale),
                    Grade = TpmsLattice.LinearGrade(2, 0, 1 * s.Scale, 2 * s.Scale, 1.25 * s.Scale, 1.5 * s.Scale),
                    Origin = s.Apply(Vector3d.Zero), Rotation = s.Rotation
                };
                lattice.Compute();
                AssertOk(lattice);
                if (reference == null) { reference = lattice; continue; }
                Assert.Equal(reference.Mesh.TriangleCount, lattice.Mesh.TriangleCount);
                Assert.Equal("", Invariance.Difference(reference.Mesh, Invariance.MappedBack(lattice.Mesh, s)));
            }
            Assert.NotNull(reference);
        }

        [Fact]
        public void Compute_IsReusable_AndParallelEqualsSerial()
        {
            var a = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.SkeletalLow, 0.4, 1.0, 0.5, 12, 2, 2);
            a.ParallelCompute = false;
            a.Compute();
            AssertOk(a);
            DMesh3 first = new DMesh3(a.Mesh, true);
            a.Compute();
            Assert.Equal("", Invariance.Difference(first, a.Mesh));
            double level = a.SecondLevel;
            a.SecondType = TpmsType.Gyroid; a.Blend = TpmsLattice.SigmoidBlend(0, 1.0, 0.5);
            a.Compute();
            Assert.NotEqual(level, a.SecondLevel);
            a.SecondType = TpmsType.IWP;
            a.Compute();
            Assert.Equal("", Invariance.Difference(first, a.Mesh));
            for (int run = 0; run < 3; ++run) {
                var b = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.SkeletalLow, 0.4, 1.0, 0.5, 12, 2, 2);
                b.ParallelCompute = true;
                b.Compute();
                Assert.Equal("", Invariance.Difference(first, b.Mesh));
            }
            // the hybrid switched off again gives the pure lattice of the first type
            a.SecondType = null; a.Blend = null;
            a.Compute();
            AssertOk(a);
            Assert.Equal(0.0, a.SecondLevel);
        }

        [Fact]
        public void SigmoidBlend_IsStableSymmetricAndHasTheStatedWidthAndSlope()
        {
            var blend = TpmsLattice.SigmoidBlend(1, 0.5, 0.25);
            Func<double, double> w = y => blend.Weight(new Vector3d(0.3, y, -7));
            Assert.Equal(0.5, w(0.5), 15);
            Assert.Equal(1.0, w(0.5 + 800 * 0.25));    // no overflow: tanh saturates
            Assert.Equal(0.0, w(0.5 - 800 * 0.25));
            Assert.False(double.IsNaN(w(1e6)) || double.IsNaN(w(-1e6)));
            for (double eta = 0.01; eta < 1; eta += 0.07) Assert.Equal(1.0, w(0.5 + eta) + w(0.5 - eta), 14);
            // 10 % to 90 % over 1.0986 width (2 atanh(0.8) / k with k = 4 / width); slope 1 / width at the plane; bound 1 / width
            double zone = 0.25 * Math.Log(9) / 4;
            Assert.Equal(0.9, w(0.5 + zone), 10);
            Assert.Equal(0.1, w(0.5 - zone), 10);
            Assert.Equal(1 / 0.25, blend.GradientBound, 15);
            double slope = (w(0.5 + 1e-7) - w(0.5 - 1e-7)) / 2e-7;
            Assert.Equal(1 / 0.25, slope, 5);
            double max = 0;
            for (double y = 0; y < 1; y += 1e-4) max = Math.Max(max, Math.Abs(w(y + 1e-7) - w(y - 1e-7)) / 2e-7);
            Assert.True(max <= blend.GradientBound * (1 + 1e-6));
            // the axis selects the coordinate
            Assert.Equal(blend.Weight(new Vector3d(9, 0.7, 9)), blend.Weight(new Vector3d(-3, 0.7, 5)));
            Assert.NotEqual(blend.Weight(new Vector3d(9, 0.7, 9)), blend.Weight(new Vector3d(9, 0.2, 9)));
            Assert.Throws<ArgumentOutOfRangeException>(() => TpmsLattice.SigmoidBlend(3, 0, 1));
            Assert.Throws<ArgumentException>(() => TpmsLattice.SigmoidBlend(0, 0, 0));
            Assert.Throws<ArgumentException>(() => TpmsLattice.SigmoidBlend(0, double.NaN, 1));
            Assert.Throws<ArgumentException>(() => TpmsLattice.SigmoidBlend(0, 0, double.PositiveInfinity));
            // a power-of-two scale of offset, width and position gives the same weight bit for bit
            var big = TpmsLattice.SigmoidBlend(1, 0.5 * 8, 0.25 * 8);
            for (double y = -0.3; y < 1.2; y += 0.0937) Assert.Equal(w(y), big.Weight(new Vector3d(0, y * 8, 0)));
        }

        [Fact]
        public void Options_AreChecked()
        {
            void Expect(Action<TpmsLattice> change, TpmsStatus status)
            {
                var l = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.Sheet, 0.3, 1.0, 0.5, 12, 2, 2);
                change(l);
                l.Compute();
                Assert.Equal(status, l.Status);
                Assert.Null(l.Mesh);
            }
            Expect(l => l.Blend = null, TpmsStatus.InvalidOptions);                                   // SecondType without Blend
            Expect(l => l.SecondType = null, TpmsStatus.InvalidOptions);                              // Blend without SecondType
            Expect(l => l.SecondType = (TpmsType)99, TpmsStatus.InvalidOptions);
            Expect(l => l.VolumeFractionAt = p => 0.3, TpmsStatus.InvalidOptions);
            Expect(l => { l.Enclosure = true; l.Blend = new TpmsBlendWeight(p => 0.5); }, TpmsStatus.InvalidOptions);   // no gradient bound
            var unbounded = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.Sheet, 0.3, 1.0, 0.5, 12, 2, 2);
            unbounded.Enclosure = true; unbounded.Blend = new TpmsBlendWeight(p => 0.5);
            unbounded.Compute();
            Assert.Contains("gradient bound", unbounded.Message);
            Expect(l => { l.Enclosure = true; l.Blend = new TpmsBlendWeight(p => 0.5, double.PositiveInfinity); }, TpmsStatus.InvalidOptions);
            Expect(l => { l.Enclosure = true; l.DomainMesh = new DMesh3(); }, TpmsStatus.InvalidOptions);   // Enclosure needs a box
            Assert.Throws<ArgumentNullException>(() => new TpmsBlendWeight(null));
            // a delegate weight without a bound works without Enclosure; values outside [0, 1] are clamped (w = 1 then)
            var ok = Make(TpmsType.IWP, TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 1.0, 0.5, 12, 2, 2);
            ok.Blend = new TpmsBlendWeight(p => 7.0);
            ok.Compute();
            AssertOk(ok);
            var pure = new TpmsLattice { Type = TpmsType.IWP, Style = TpmsStyle.Sheet, CellSize = L, Divisions = 12, VolumeFraction = 0.3, Box = ok.Box };
            pure.Compute();
            // clamped to 1 but the bound differs from the pure one (the weight term of an unknown bound is left out, the phase part is the larger type's): same mesh
            Assert.Equal("", Invariance.Difference(pure.Mesh, ok.Mesh));
            var nan = Make(TpmsType.IWP, TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 1.0, 0.5, 12, 2, 2);
            nan.Blend = new TpmsBlendWeight(p => double.NaN);
            nan.Compute();
            Assert.NotEqual(TpmsStatus.Ok, nan.Status);
        }

        [Fact]
        public void Options_RefuseABoundThatCannotBeNormalisedAndFailOnNaNWeights()
        {
            TpmsLattice With(TpmsBlendWeight blend)
            {
                var l = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.Sheet, 0.3, 1.0, 0.5, 12, 2, 2);
                l.Blend = blend;
                l.Compute();
                return l;
            }
            // without Enclosure too: a negative bound flips the sign of the field, an infinite one zeroes it (a solid box would be reported as Ok)
            foreach (double bound in new[] { -1000.0, double.NegativeInfinity, double.PositiveInfinity, 1e308 }) {
                var l = With(new TpmsBlendWeight(p => 0.5, bound));
                Assert.Equal(TpmsStatus.InvalidOptions, l.Status);
                Assert.Null(l.Mesh);
                if (bound != 1e308) Assert.Contains("finite and not negative", l.Message);
            }
            Assert.Equal(TpmsStatus.InvalidOptions, With(TpmsLattice.SigmoidBlend(0, 1, 1e-320)).Status);   // 1 / width is infinite
            Assert.Equal(TpmsStatus.Ok, With(new TpmsBlendWeight(p => 0.5, 0)).Status);
            Assert.Equal(TpmsStatus.Ok, With(new TpmsBlendWeight(p => 0.5)).Status);                         // unknown bound: fine without Enclosure
            // NaN weights, everywhere or only in part of the domain: no silent holes
            var all = With(new TpmsBlendWeight(p => double.NaN, 1));
            Assert.Equal(TpmsStatus.ContouringFailed, all.Status);
            Assert.Contains("NaN", all.Message);
            var part = With(new TpmsBlendWeight(p => p.x > 1.5 ? double.NaN : 0.5, 1));
            Assert.Equal(TpmsStatus.ContouringFailed, part.Status);
            Assert.Null(part.Mesh);
            // the outputs of an earlier success do not survive a failing Compute on the same instance
            var again = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.Sheet, 0.3, 1.0, 0.5, 12, 2, 2);
            again.Compute();
            Assert.True(again.SecondLevel != 0);
            again.Divisions = 2;
            again.Compute();
            Assert.Equal(TpmsStatus.InvalidOptions, again.Status);
            Assert.Equal(0.0, again.SecondLevel);
        }

        [Fact]
        public void SteepBlend_DoesNotThickenThePureSides()
        {
            // a weight much steeper than a cube (width 0.004, cube 1/16): the Lipschitz bound is dominated by the weight term, and the sliver guard must not follow it
            // (an unscaled guard of 2^-8 cube times that bound is about one field unit and would fill the pure halves)
            var lattice = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.SkeletalLow, 0.3, 2.0, 0.004, 16);
            lattice.Compute();
            AssertOk(lattice);
            Assert.True(lattice.LipschitzBoundUsed > 5 * 2 * Math.PI * 6 * Math.Sqrt(3), "the weight term must dominate: " + lattice.LipschitzBoundUsed);
            var tree = new DMeshAABBTree3(lattice.Mesh, true);
            var rand = new Random(17);
            int nLow = 0, low = 0, nHigh = 0, high = 0;
            for (int i = 0; i < 9000; ++i) {
                var q = new Vector3d(0.3 + 3.4 * rand.NextDouble(), 0.3 + 1.4 * rand.NextDouble(), 0.3 + 1.4 * rand.NextDouble());
                bool inside = tree.IsInside(q);
                if (q.x < 1.7) { ++nLow; if (inside) ++low; }
                if (q.x > 2.3) { ++nHigh; if (inside) ++high; }
            }
            Assert.InRange((double)low / nLow, 0.3 - 0.06, 0.3 + 0.06);
            Assert.InRange((double)high / nHigh, 0.3 - 0.06, 0.3 + 0.06);
        }

        [Fact]
        public void Enclosure_ContainsTheExactHybrid_ForSkeletalHighAndAGrade()
        {
            var high = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.SkeletalHigh, 0.3, 2.0, 0.125, 16);
            high.Enclosure = true;
            high.Compute();
            AssertOk(high);
            var graded = Make(TpmsType.IWP, TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.35, 0.0, 0.5, 16, 2, 2);
            graded.Box = new AxisAlignedBox3d(new Vector3d(-1, -1, 0), new Vector3d(1, 1, 2));
            graded.Grade = TpmsLattice.LinearGrade(2, 0, 1, 2, 1.25, 1.5);
            graded.Blend = TpmsLattice.SigmoidBlend(2, 1.0, 0.25);
            graded.Enclosure = true;
            graded.Compute();
            AssertOk(graded);
            var plain = new TpmsLattice { Type = graded.Type, SecondType = graded.SecondType, Blend = graded.Blend, Style = graded.Style, CellSize = L, Divisions = 16, VolumeFraction = 0.35, Box = graded.Box, Grade = graded.Grade };
            plain.Compute();
            AssertOk(plain);
            Assert.True(graded.RealizedVolumeFraction > plain.RealizedVolumeFraction, "the enclosure is larger than the target");
            Assert.True(graded.LipschitzBoundUsed > 2 * Math.PI * 6 * Math.Sqrt(3));
        }

        [Fact]
        public void WallThickness_UsesTheAreaRatioOfEachType()
        {
            var lattice = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.Sheet, 0.3, 2.0, 0.4, 24);
            lattice.VolumeFraction = 0.5;   // ignored: the wall thickness decides
            lattice.WallThickness = 0.06;
            lattice.Compute();
            AssertOk(lattice);
            Assert.Equal(LevelOf(TpmsType.Gyroid, TpmsStyle.Sheet, TpmsLattice.AreaRatio(TpmsType.Gyroid) * 0.06), lattice.Level, 12);
            Assert.Equal(LevelOf(TpmsType.IWP, TpmsStyle.Sheet, TpmsLattice.AreaRatio(TpmsType.IWP) * 0.06), lattice.SecondLevel, 12);
            Assert.NotEqual(lattice.Level, lattice.SecondLevel);
            var thick = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.Sheet, 0.3, 2.0, 0.4, 24);
            thick.WallThickness = 20;
            thick.Compute();
            Assert.Equal(TpmsStatus.InvalidOptions, thick.Status);
        }

        [Fact]
        public void WorksWithAGradedCellSizeAndAMeshDomain()
        {
            var graded = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.SkeletalLow, 0.4, 1.0, 0.5, 16, 2, 2);
            graded.Box = new AxisAlignedBox3d(new Vector3d(0, -1, -1), new Vector3d(2, 1, 1));
            graded.Grade = TpmsLattice.LinearGrade(0, 0, 1, 2, 1.5, 1.5);
            graded.Compute();
            AssertOk(graded);
            Assert.True(graded.MinLocalCellSize > 0);
            var sphere = Make(TpmsType.Gyroid, TpmsType.IWP, TpmsStyle.SkeletalLow, 0.4, 0.0, 0.5, 16);
            sphere.DomainMesh = new Sphere3Generator_NormalizedCube { Radius = 1.2, EdgeVertices = 16 }.Generate().MakeDMesh();
            sphere.Compute();
            AssertOk(sphere);
            double maxRadius = 0;
            foreach (Vector3d v in sphere.Mesh.Vertices()) maxRadius = Math.Max(maxRadius, v.Length);
            Assert.InRange(maxRadius, 0.9, 1.3);
        }
    }
}
