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
    /// Known failures, pinned strictly: each test asserts today's wrong outcome, so a fix makes it fail and it is turned
    /// into the test of the fix (docs/design/TERRAIN.md 5.4, "Repair of the band [64·e_xy, MinFeatureSize)").
    /// </summary>
    public class TerrainSlopeKnownFailureTests
    {
        // a wavy 48-gon (r 150 m, ± 15 %, toe z ± 4 m) at c, ratio 1.5, on a wave terrain h above the toe (amplitude amp,
        // grid spacing sp), as TerrainBenchmark's 'slope toe=wavy terrain=wave'
        internal static TerrainSlopeDesign WavyOnWaves(Vector3d c, int n = 48, double r = 150, double h = 60, double amp = 20, double sp = 8, double ratio = 1.5)
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
            return new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(pts, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(ratio)) },
                Terrain = terrain, Tolerance = 0.05, BuildComposite = true
            };
        }

        // Known failure: near the origin cap needles along the crease x ≈ 0 (altitudes 1e-12 to 3e-10 m, edges up to
        // 88 m) fall between the needle flips' threshold (64 e_xy, 7e-12 m) and MinFeatureSize (3e-6 m), which no repair
        // covers; a larger δ makes new ones. At UTM the same needles are below 64 e_xy (6e-8 m) and are flipped or merged.
        // When the repair of TERRAIN.md 5.4 lands, the first assertion fails: assert Success there, the same as at UTM.
        [Fact]
        public void WavyToeNearTheOrigin_FailsWhereUtmSucceeds()
        {
            var origin = WavyOnWaves(new Vector3d(0, 0, -340));
            Assert.False(origin.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.SlopeViolation, origin.Status);
            Assert.NotEmpty(origin.ProblemTriangles);

            var utm = WavyOnWaves(new Vector3d(327000.25, 5642000.5, -340));
            Assert.True(utm.Compute(), utm.Status + " " + utm.Message);
            Assert.Equal(1, utm.Attempts);
            TerrainSlopeCatchLineTests.AssertCatchLines(utm, false);
            TerrainSlopeCatchLineTests.AssertComposite(utm);
        }
    }
}
