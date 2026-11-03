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
    /// The repair of the band [64·e_xy, MinFeatureSize) (docs/design/TERRAIN.md 5.4): needles from near-quadruple points
    /// (two triple points closer than MinFeatureSize whose plane sets are not nested), which no collapse may remove and
    /// the merge below 64 e_xy does not take, are merged up to the band radius.
    /// </summary>
    public class TerrainSlopeBandRepairTests
    {
        // a wavy 48-gon (r 150 m, ± 15 %, toe z ± 4 m) at c, ratio 1.5, on a wave terrain h above the toe (amplitude amp,
        // grid spacing sp), as TerrainBenchmark's 'slope toe=wavy terrain=wave'
        internal static TerrainSlopeDesign WavyOnWaves(Vector3d c, int n = 48, double r = 150, double h = 60, double amp = 20, double sp = 8, double ratio = 1.5)
        {
            return WavyOnWaves(Similarity.Identity, c, n, r, h, amp, sp, ratio);
        }

        // the same in the frame s (the inputs built at c, then mapped)
        internal static TerrainSlopeDesign WavyOnWaves(Similarity s, Vector3d c, int n = 48, double r = 150, double h = 60, double amp = 20, double sp = 8, double ratio = 1.5)
        {
            var pts = new List<Vector3d>();
            for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * i / n, rr = r * (1 + 0.15 * Math.Sin(5 * a));
                pts.Add(c + new Vector3d(rr * Math.Cos(a), rr * Math.Sin(a), 4 * Math.Sin(3 * a)));
            }
            double reach = (h + amp) * ratio * 1.2 + 2 * sp;
            var box = new AxisAlignedBox2d(pts[0].xy, pts[0].xy);
            foreach (var p in pts) box.Contain(p.xy);
            box.Expand(reach);
            int nx = (int)Math.Ceiling(box.Width / sp), ny = (int)Math.Ceiling(box.Height / sp);
            var terrain = new DMesh3();
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i) {
                    double x = box.Min.x + box.Width * i / nx, y = box.Min.y + box.Height * j / ny;
                    terrain.AppendVertex(new Vector3d(x, y, c.z + h + amp * Math.Sin(0.7 * (x - c.x) / r) * Math.Cos(0.9 * (y - c.y) / r)));
                }
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    int v = j * (nx + 1) + i;
                    terrain.AppendTriangle(v, v + 1, v + nx + 2); terrain.AppendTriangle(v, v + nx + 2, v + nx + 1);
                }
            foreach (int v in terrain.VertexIndices().ToList()) terrain.SetVertex(v, s.Apply(terrain.GetVertex(v)));
            return new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(pts.Select(p => s.Apply(p)).ToList(), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(ratio)) },
                Terrain = terrain, Tolerance = s.ApplyLength(0.05), BuildComposite = true
            };
        }

        // Near the origin the first attempt had cap needles along the crease x ≈ 0 (altitudes 4e-12 m, edges of 37 m) from
        // two triple points 8.5e-12 m apart with plane sets {67, 69, 77} and {69, 75, 77}: above r = 64 e_xy (3.6e-12 m),
        // far below MinFeatureSize (2.3e-6 m). One failed the slope, one the support (by 2.84 m), δ was raised, and every
        // attempt failed again (SlopeViolation after 3). At UTM the triple points round to one position: no needles.
        // The band merge makes them one vertex near the origin too (its diagnostics: TerrainSlopeBandMergeTests)
        [Fact]
        public void WavyToeNearTheOrigin_SucceedsAsAtUtm()
        {
            var origin = WavyOnWaves(new Vector3d(0, 0, -340));
            Assert.True(origin.Compute(), origin.Status + " " + origin.Message);
            Assert.Equal(1, origin.Attempts);
            TerrainSlopeCatchLineTests.AssertCatchLines(origin, false);
            TerrainSlopeCatchLineTests.AssertComposite(origin);

            var utm = WavyOnWaves(new Vector3d(327000.25, 5642000.5, -340));
            Assert.True(utm.Compute(), utm.Status + " " + utm.Message);
            Assert.Equal(1, utm.Attempts);
            TerrainSlopeCatchLineTests.AssertCatchLines(utm, false);
            TerrainSlopeCatchLineTests.AssertComposite(utm);
            Assert.Equal(utm.CatchLines.Count, origin.CatchLines.Count);
        }

        internal static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = WavyOnWaves(new Vector3d(0, 0, -340));
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            return d;
        });

        // the same status in every frame (near the origin, scaled, rotated, at UTM: band merges in all but UTM), the 2^k
        // frames bit for bit
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void WavyToe_Similarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var d = WavyOnWaves(s, new Vector3d(0, 0, -340));
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeCatchLineTests.AssertCatchLines(d, false);
            TerrainSlopeCatchLineTests.AssertComposite(d);
            var r = reference.Value;
            Assert.Equal(r.CatchLines.Count, d.CatchLines.Count);
            if (s.IsExact) {
                Assert.Equal("", Invariance.Difference(r.SlopeSurface, Invariance.MappedBack(d.SlopeSurface, s)));
                // (a composite node on the terrain's grid line x = 0 is moved off the slope side by SlopeComposite's nudge:
                // from exactly 0 the step is the ulp of the edge's coordinate scale, so it scales with 2^k)
                Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, s)));
                Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, s)));
            }
        }

        [Fact]
        public void Reuse()
        {
            var d = WavyOnWaves(new Vector3d(0, 0, -340));
            Assert.True(d.Compute(), d.Message);
            var c1 = d.Composite;
            Assert.True(d.Compute(), d.Message);
            Assert.Equal("", Invariance.Difference(c1, d.Composite));
        }
    }
}
