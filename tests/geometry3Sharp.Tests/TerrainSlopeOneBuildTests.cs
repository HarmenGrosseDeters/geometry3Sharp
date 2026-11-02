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
    /// Terrain 1c, idea A (TERRAIN_1C_PLAN.md): the map is built once per margin and a larger toe lowering λ is only
    /// realized again. Lowering every plane by λ does not change any line of two planes, so the result must be bit for
    /// bit the one of a new build at the final λ (the reference, RebuildForEachLowering), on the stress set.
    /// </summary>
    public class TerrainSlopeOneBuildTests
    {
        readonly ITestOutputHelper output;
        public TerrainSlopeOneBuildTests(ITestOutputHelper output) { this.output = output; }

        static IEnumerable<Func<TerrainSlopeDesign>> Cases()
        {
            Func<int, double, double, double, double, bool, TerrainSlopeDesign> ngon = (n, r, phase, ratio, h, utm) => {
                var c = utm ? new Vector2d(5.6e6, 3.5e5) : Vector2d.Zero;
                double z = utm ? 100 : 0;
                return new TerrainSlopeDesign() {
                    Toes = { SlopeToe.Constant(TerrainSlopeCatchLineTests.Ngon(n, r, z, c, phase), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(ratio)) },
                    HeightLimit = z + h, Tolerance = 0.05
                };
            };
            // regular polygons (the merge cases among them), an uneven wavy toe, an open toe, fill
            yield return () => ngon(10, 9.642226746139222, 0.29190628476995334, 1.9346294006959672, 13.65915220256275, false);
            yield return () => ngon(16, 21.30606409921593, 0.07628677649250569, 1.4751108086086395, 21.30606409921593 * 0.6, false);
            yield return () => ngon(4, 23.804800668174774, 0.6008761141453293, 2.775734977692242, 23.804800668174774 * 0.6, false);
            yield return () => ngon(24, 20, 0.1, 1, 6, true);
            yield return () => {
                var pts = Enumerable.Range(0, 32).Select(i => {
                    double a = 2 * Math.PI * i / 32, rr = 30 * (1 + 0.15 * Math.Sin(5 * a));
                    return new Vector3d(5.6e6 + rr * Math.Cos(a), 3.5e5 + rr * Math.Sin(a), 80 + 2 * Math.Sin(3 * a));
                }).ToList();
                return new TerrainSlopeDesign() { Toes = { SlopeToe.Constant(pts, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)) }, HeightLimit = 95, Tolerance = 0.02 };
            };
            yield return () => new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(20, 3, 1), new Vector3d(35, -4, 0) }, false, SlopeToe.Sides.Left, SlopeSpec.Ratio(1.5)) },
                HeightLimit = 8, Tolerance = 0.02
            };
            yield return () => {
                var toe = SlopeToe.Constant(TerrainSlopeDesignTests.Square(8, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(2));
                toe.FillStart = toe.CutStart; toe.CutStart = null;
                return new TerrainSlopeDesign() { Toes = { toe }, HeightLimit = -6, Tolerance = 0.02, Direction = TerrainSlopeDesign.Directions.Fill };
            };
            // with terrains and the composite: the random wavy terrains of the catch-line stress test
            for (int seed = 0; seed < 4; ++seed)
                for (int it = 0; it < 3; ++it) {
                    int s0 = seed, i0 = it;
                    yield return () => TerrainSlopeCatchLineTests.RandomTerrainCase(s0, s0 >= 2, i0);
                }
        }

        [Fact]
        public void OneBuild_EqualsARebuildAtTheFinalLowering()
        {
            int saved = 0, raised = 0, n = 0;
            foreach (var make in Cases()) {
                var a = make();
                var b = make();
                b.RebuildForEachLowering = true;
                bool ra = a.Compute(), rb = b.Compute();
                n++;
                Assert.Equal(b.Status, a.Status);
                Assert.Equal(rb, ra);
                Assert.Equal(b.UsedToeLowering, a.UsedToeLowering);
                Assert.Equal(b.UsedSlopeMargin, a.UsedSlopeMargin);
                if (b.SlopeSurface != null) Assert.Equal("", Invariance.Difference(b.SlopeSurface, a.SlopeSurface));
                if (b.Composite != null) {
                    Assert.Equal("", Invariance.Difference(b.Composite, a.Composite));
                    Assert.Equal("", Invariance.Difference(b.SlopeRegion, a.SlopeRegion));
                }
                Assert.Equal(b.CatchLines.Count, a.CatchLines.Count);
                for (int i = 0; i < a.CatchLines.Count; ++i) Assert.Equal(b.CatchLines[i].Points, a.CatchLines[i].Points);
                Assert.True(a.Attempts <= b.Attempts);
                saved += b.Attempts - a.Attempts;
                if (a.Realizations > a.Attempts) raised++;
                output.WriteLine($"{n}: {a.Status} builds {a.Attempts} (reference {b.Attempts}), realizations {a.Realizations}, λ {a.UsedToeLowering:G3}");
            }
            output.WriteLine($"{n} cases, {saved} builds saved, {raised} with a raised lowering");
            // (the comparison has to exercise the re-realization)
            Assert.True(raised >= 3, $"{raised}");
        }
    }
}
