// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The composite's catch points move within MinFeatureSize (snapped onto slope vertices, merged, snapped to the
    /// partner's at a toe crossing), and the terrain triangles at them follow: the surface then changes near them by up
    /// to the terrain's and the slope's gradients times the move, which the tolerance budget did not hold (docs/design/
    /// TERRAIN.md 5.4). Below MinFeatureSize (Mixed's 7-gon of radius 11 m scaled by 1e-3, at UTM) that was up to 72 x
    /// Tolerance on a Success. A composite now reserves Tolerance / 4 for the move and certifies it exactly; what it
    /// cannot certify raises the margin, then fails with SlopeViolation (NotCertified).
    /// </summary>
    public class TerrainSlopeSnapTests
    {
        static Similarity frame(string name)
        {
            Similarity s = Similarity.ByName(name);
            return s.IsRotated ? new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation) : s;
        }

        // the ideal surface of TerrainSlopeMixedTests.Make: the cones of the closed toe (outside; cut 1:1.5 up, fill 1:2
        // down from the toe's height), clamped: max(S_fill, min(T, S_cut)), min(T, S_cut) in cut, max(T, S_fill) in fill
        internal static double Ideal(TerrainSlopeDesign d, Vector2d q, out bool outside)
        {
            List<Vector3d> toe = d.Toes[0].Points;
            int m = toe.Count;
            outside = new Polygon2d(toe.Select(p => p.xy)).Contains(q) == false;
            double dist = Enumerable.Range(0, m).Min(k => Math.Sqrt(new Segment2d(toe[k].xy, toe[(k + 1) % m].xy).DistanceSquared(q)));
            double T = TerrainSlopeDesignTests.HeightAt(d.Terrain, q);
            double cut = toe[0].z + dist / 1.5, fill = toe[0].z - dist / 2;
            switch (d.Direction) {
                case TerrainSlopeDesign.Directions.Cut: return Math.Min(T, cut);
                case TerrainSlopeDesign.Directions.Fill: return Math.Max(T, fill);
                default: return Math.Max(fill, Math.Min(T, cut));
            }
        }

        /// <summary> the largest |z - ideal| / Tolerance of the composite on a 61 x 61 grid over 3 x the toe's box, outside the toe </summary>
        internal static double WorstDeviation(TerrainSlopeDesign d)
        {
            var tb = AxisAlignedBox2d.Empty;
            foreach (Vector3d p in d.Toes[0].Points) tb.Contain(p.xy);
            double worst = 0;
            for (int i = 0; i <= 60; ++i)
                for (int j = 0; j <= 60; ++j) {
                    var q = new Vector2d(tb.Min.x - tb.Width + 3 * tb.Width * i / 60.0, tb.Min.y - tb.Height + 3 * tb.Height * j / 60.0);
                    bool outside;
                    double ideal = Ideal(d, q, out outside);
                    double z = TerrainSlopeDesignTests.HeightAt(d.Composite, q);
                    if (outside == false || double.IsNaN(ideal) || double.IsNaN(z)) continue;
                    worst = Math.Max(worst, Math.Abs(z - ideal) / d.Tolerance);
                }
            return worst;
        }

        // a Success is within Tolerance of the ideal surface; what is not certified fails with SlopeViolation (below
        // MinFeatureSize also the composite may fail, honestly: CompositeFailed)
        static void AssertWithinToleranceOrNotCertified(TerrainSlopeDesign d, string what, bool belowMinFeature = false)
        {
            if (belowMinFeature && d.Status == TerrainSlopeDesign.Statuses.CompositeFailed) return;
            if (d.Status == TerrainSlopeDesign.Statuses.SlopeViolation) {
                Assert.Equal(TerrainSlopeDesign.StatusKinds.NotCertified, d.StatusKind);
                Assert.Contains("not certified", d.Message);
                return;
            }
            Assert.True(d.Status == TerrainSlopeDesign.Statuses.Success, what + ": " + d.Status + " " + d.Message);
            double w = WorstDeviation(d);
            Assert.True(w <= 1, $"{what}: |z - ideal| up to {w:G3} x Tolerance");
            if (d.Direction == TerrainSlopeDesign.Directions.Auto) TerrainSlopeMixedTests.AssertMixed(d);
            else TerrainSlopeCatchLineTests.AssertComposite(d, d.Direction == TerrainSlopeDesign.Directions.Fill);
        }

        // terrain tilt g, offset c, tilt direction rot of TerrainSlopeMixedTests.Make
        public static IEnumerable<object[]> Cases()
        {
            foreach (object[] f in Similarity.Names())
                foreach (var dir in new[] { TerrainSlopeDesign.Directions.Cut, TerrainSlopeDesign.Directions.Fill, TerrainSlopeDesign.Directions.Auto })
                    yield return new object[] { f[0], dir };
        }

        static readonly double[][] configs = {
            new[] { 0.07, -1.1, 0.0 }, new[] { 0.07, 0.123, 2.1 }, new[] { 0.15, 0.3, 0.37 }, new[] { 0.15, 1.7, 1.3 },
            new[] { 0.23, -1.1, 0.0 }, new[] { 0.23, 0.123, 3.9 },
            // (mixed at UTM: a fill region triangle at a moved catch point on terrain triangles that only the fill run's
            // overlay has)
            new[] { 0.23, -1.1, 0.37 },
            // (at UTM without the reserved share the certificate fails the first attempt of each run here)
            new[] { 0.23, 0.3, 2.1 }
        };

        // the oracle: every frame of Similarity.All (rotated about z), cut, fill and mixed
        [Trait("Category", "Slow")]
        [Theory]
        [MemberData(nameof(Cases))]
        public void WithinToleranceOfTheIdealSurface(string name, TerrainSlopeDesign.Directions dir)
        {
            Similarity s = frame(name);
            int successes = 0;
            foreach (double[] c in configs) {
                var d = TerrainSlopeMixedTests.Make(c[0], c[1], c[2], dir, s);
                d.Compute();
                AssertWithinToleranceOrNotCertified(d, $"g {c[0]} c {c[1]} rot {c[2]}", name == Similarity.RotatedUtmMillimetre.Name);
                if (d.Status == TerrainSlopeDesign.Statuses.Success) successes++;
                // (at UTM the reserved share holds the move: certified in the first attempt of each run; without it the
                // certificate fails there and δ is raised)
                if (name == Similarity.Utm.Name) Assert.Equal(dir == TerrainSlopeDesign.Directions.Auto ? 2 : 1, d.Attempts);
            }
            // (every design is certified where the toe is far above MinFeatureSize; below it, some need a larger margin)
            if (name != Similarity.RotatedUtmMillimetre.Name) Assert.Equal(configs.Length, successes);
        }

        // the repro: the 7-gon of radius 11 mm at UTM (Mixed's design scaled by 1e-3, rotated); on master a Success up to
        // 72 x Tolerance off the ideal surface, in cut and fill
        [Theory]
        [InlineData(TerrainSlopeDesign.Directions.Cut, 0.23, -1.1, 0.0)]
        [InlineData(TerrainSlopeDesign.Directions.Fill, 0.07, 0.123, 2.1)]
        [InlineData(TerrainSlopeDesign.Directions.Auto, 0.15, 0.3, 0.37)]
        public void SmallToeAtUtm(TerrainSlopeDesign.Directions dir, double g, double c, double rot)
        {
            var d = TerrainSlopeMixedTests.Make(g, c, rot, dir, frame("rotated, scale 1e-3, utm"));
            d.Compute();
            AssertWithinToleranceOrNotCertified(d, "small toe", true);
        }

        // the 2^k frames give the identity's result scaled, bit for bit (the certificate's verdict too)
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities(string name)
        {
            Similarity s = frame(name);
            if (s.IsExact == false) return;
            foreach (var dir in new[] { TerrainSlopeDesign.Directions.Cut, TerrainSlopeDesign.Directions.Auto }) {
                var r = TerrainSlopeMixedTests.Make(0.23, -1.1, 0.0, dir, Similarity.Identity);
                var d = TerrainSlopeMixedTests.Make(0.23, -1.1, 0.0, dir, s);
                r.Compute(); d.Compute();
                Assert.Equal(r.Status, d.Status);
                Assert.Equal(r.Attempts, d.Attempts);
                if (r.Status != TerrainSlopeDesign.Statuses.Success) continue;
                Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, s)));
                Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, s)));
            }
        }

        [Fact]
        public void Reuse()
        {
            var d = TerrainSlopeMixedTests.Make(0.23, -1.1, 0.0, TerrainSlopeDesign.Directions.Cut, frame("utm"));
            Assert.True(d.Compute(), d.Message);
            var c1 = new DMesh3(d.Composite);
            int a1 = d.Attempts;
            Assert.True(d.Compute(), d.Message);
            Assert.Equal("", Invariance.Difference(c1, d.Composite));
            Assert.Equal(a1, d.Attempts);
        }
    }
}
