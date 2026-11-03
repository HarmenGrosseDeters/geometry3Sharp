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

        // Known failure: near the origin the first attempt has cap needles along the crease x ≈ 0 (altitudes 4e-12 m,
        // edges of 37 m), just above the needle flips' threshold (64 e_xy, 3.6e-12 m) and far below MinFeatureSize
        // (2.3e-6 m), which no repair covers; one fails the slope, one the support (by 2.84 m: its plane at the toe ends).
        // λ cannot take that, δ is raised, and every further attempt fails again, until the margins exceed Tolerance. At
        // UTM the crease points round to one position on the coarser grid: no needles, success in 1 attempt. When the
        // repair of TERRAIN.md 5.4 lands, the first assertion fails: assert Success there, the same as at UTM.
        [Fact]
        public void WavyToeNearTheOrigin_FailsWhereUtmSucceeds()
        {
            var origin = WavyOnWaves(new Vector3d(0, 0, -340));
            Assert.False(origin.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.SlopeViolation, origin.Status);
            // (the cause, not only the status: the margin raised to its end, and needles of the band among the failing
            // triangles of the last attempt: at least r = MergeRadius(e_xy) = 64 e_xy thick, so not one that today's
            // flips would take, and thinner than its MinFeatureSize, with edges a million times longer. e_xy is not
            // exposed; MinFeatureSize δ / 2 = 8 e_xy + 4 e_eval / s_min >= 8 e_xy gives r <= 4 MinFeatureSize δ, used as
            // the lower end, conservative. The first attempt's needles, 4e-12 m, are documented above; a diagnostic
            // of attempt 0 comes with the implementation)
            Assert.Equal(3, origin.Attempts);
            Assert.NotEmpty(origin.ProblemTriangles);
            DMesh3 last = origin.LastRealized;
            double r = 4 * origin.UsedMinFeatureSize * origin.UsedSlopeMargin;
            int needles = 0;
            foreach (int t in origin.ProblemTriangles) {
                Assert.True(last.IsTriangle(t), $"problem triangle {t} is not a triangle of LastRealized");
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                last.GetTriVertices(t, ref a, ref b, ref c);
                double l = Math.Max(a.xy.Distance(b.xy), Math.Max(b.xy.Distance(c.xy), c.xy.Distance(a.xy)));
                double h = Math.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) / l;
                if (h >= r && h < origin.UsedMinFeatureSize && l > 1e6 * h) needles++;
            }
            Assert.True(needles > 0, "no needle of the band [64 e_xy, MinFeatureSize) among the failing triangles: the failure has another cause now");

            var utm = WavyOnWaves(new Vector3d(327000.25, 5642000.5, -340));
            Assert.True(utm.Compute(), utm.Status + " " + utm.Message);
            Assert.Equal(1, utm.Attempts);
            TerrainSlopeCatchLineTests.AssertCatchLines(utm, false);
            TerrainSlopeCatchLineTests.AssertComposite(utm);
        }
    }
}
