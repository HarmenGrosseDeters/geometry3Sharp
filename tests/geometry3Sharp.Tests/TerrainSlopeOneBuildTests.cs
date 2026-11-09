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
    /// bit the one of a new build at the final λ (the reference, RebuildForEachLowering), on the stress set. (This
    /// checks the loop: the map of the reference is the same by construction. The construction itself, planes through
    /// the specified toe instead of the lowered one, is checked by the oracles O1 / O2 of the other tests.)
    /// </summary>
    public class TerrainSlopeOneBuildTests
    {
        readonly ITestOutputHelper output;
        public TerrainSlopeOneBuildTests(ITestOutputHelper output) { this.output = output; }

        internal static IEnumerable<Func<TerrainSlopeDesign>> Cases()
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
                // the surface ends at H - λ (cut; H + λ for fill), never beyond H
                if (a.SlopeSurface != null && a.Terrain == null) {
                    bool fill = a.Direction == TerrainSlopeDesign.Directions.Fill;
                    double top = fill ? a.SlopeSurface.Vertices().Min(v => v.z) : a.SlopeSurface.Vertices().Max(v => v.z);
                    double end = fill ? a.HeightLimit + a.UsedToeLowering : a.HeightLimit - a.UsedToeLowering;
                    double slack = 4 * Math.Abs(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(Math.Abs(a.HeightLimit)) + 1) - Math.Abs(a.HeightLimit));
                    Assert.True(fill ? top >= end - slack : top <= end + slack, $"top {top:R}, H - λ {end:R}");
                    Assert.True(fill ? top >= a.HeightLimit : top <= a.HeightLimit);
                }
                // the statistics count the build, not only the realizations
                Assert.True(a.PredicateCalls > 1000, $"{a.PredicateCalls}");
                saved += b.Attempts - a.Attempts;
                if (a.Realizations > a.Attempts) raised++;
                output.WriteLine($"{n}: {a.Status} builds {a.Attempts} (reference {b.Attempts}), realizations {a.Realizations}, λ {a.UsedToeLowering:G3}");
            }
            output.WriteLine($"{n} cases, {saved} builds saved, {raised} with a raised lowering");
            // (the comparison has to exercise the re-realization)
            Assert.True(raised >= 3, $"{raised}");
        }

        [Fact]
        public void InsertionOrder_DoesNotChangeTheResult()
        {
            // ties go to the lower plane id and Simplify makes the canonical partition: the in-order insertion, the
            // reversed one and several seeded shuffles give the same result, bit for bit, on the stress set
            var orders = new List<Func<int, int[]>>() {
                n => Enumerable.Range(0, n).ToArray(),
                n => Enumerable.Range(0, n).Reverse().ToArray(),
                n => TerrainSlopeDesign.shuffled(n, 1),
                n => TerrainSlopeDesign.shuffled(n, 2),
                n => TerrainSlopeDesign.shuffled(n, 0xC0FFEE),
            };
            int cases = 0, withRays = 0, withUndercuts = 0;
            foreach (var make in Cases().Concat(UndercutCases())) {
                var a = make();
                a.CheckCanonical = true;
                bool ra = a.Compute();
                Assert.Equal("", a.CanonicalProblem);
                cases++;
                if (a.RayCount > 0) withRays++;
                if (a.UndercutCauses.Count > 0) withUndercuts++;
                foreach (var order in orders) {
                    var b = make();
                    b.InsertionOrder = order;
                    bool rb = b.Compute();
                    Assert.Equal(a.Status, b.Status);
                    Assert.Equal(ra, rb);
                    Assert.Equal(a.Attempts, b.Attempts);
                    Assert.Equal(a.UsedToeLowering, b.UsedToeLowering);
                    Assert.Equal(a.RayCount, b.RayCount);
                    Assert.Equal(a.ToeProblems, b.ToeProblems);
                    Assert.Equal(a.StatusKind, b.StatusKind);
                    Assert.Equal(a.UndercutCauses.Count, b.UndercutCauses.Count);
                    for (int i = 0; i < a.UndercutCauses.Count; ++i) {
                        Assert.Equal(a.UndercutCauses[i].Piece, b.UndercutCauses[i].Piece);
                        Assert.Equal(a.UndercutCauses[i].Cause, b.UndercutCauses[i].Cause);
                        Assert.Equal(a.UndercutCauses[i].At, b.UndercutCauses[i].At);
                    }
                    Assert.Equal(a.SlopeSurface == null, b.SlopeSurface == null);
                    if (a.SlopeSurface != null) {
                        Assert.Equal("", Invariance.Difference(a.SlopeSurface, b.SlopeSurface));
                        // (and the pieces of the triangles)
                        foreach (int t in a.SlopeSurface.TriangleIndices())
                            Assert.Equal(a.SlopeSurface.GetTriangleGroup(t), b.SlopeSurface.GetTriangleGroup(t));
                    }
                    Assert.Equal(a.Composite == null, b.Composite == null);
                    if (a.Composite != null) Assert.Equal("", Invariance.Difference(a.Composite, b.Composite));
                    Assert.Equal(a.CatchLines.Count, b.CatchLines.Count);
                    for (int i = 0; i < a.CatchLines.Count; ++i) Assert.Equal(a.CatchLines[i].Points, b.CatchLines[i].Points);
                }
                output.WriteLine($"{cases}: {a.Status}, rays {a.RayCount}, triangles {(a.SlopeSurface == null ? 0 : a.SlopeSurface.TriangleCount)}");
            }
            // (the rays have to be exercised: components that are not convex)
            Assert.True(withRays >= 3, $"{withRays}");
            // (and the undercut causes, which decide the StatusKind)
            Assert.True(withUndercuts >= 2, $"{withUndercuts}");
        }

        // toes that undercut themselves (TerrainSlopeDesignTests.TwoLevelToe_IsUndercut): a U-shaped open toe, and a closed
        // ring with the slope inside
        static IEnumerable<Func<TerrainSlopeDesign>> UndercutCases()
        {
            var u = new List<Vector3d> { new Vector3d(0, 20, 8), new Vector3d(40, 20, 8), new Vector3d(60, 10, 4), new Vector3d(40, 0, 0), new Vector3d(0, 0, 0) };
            yield return () => TerrainSlopeDesignTests.Design(20, 0.01, SlopeToe.Constant(u, false, SlopeToe.Sides.Left, SlopeSpec.Ratio(4)));
            var ring = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(40, 0, 0) };
            for (int i = 1; i < 12; ++i) {
                double a = -Math.PI / 2 + Math.PI * i / 12;
                ring.Add(new Vector3d(40 + 20 * Math.Cos(a), 10 + 10 * Math.Sin(a), 8.0 * i / 12));
            }
            ring.Add(new Vector3d(40, 20, 8)); ring.Add(new Vector3d(0, 20, 8));
            for (int i = 1; i < 12; ++i) {
                double a = Math.PI / 2 + Math.PI * i / 12;
                ring.Add(new Vector3d(20 * Math.Cos(a), 10 + 10 * Math.Sin(a), 8 - 8.0 * i / 12));
            }
            yield return () => TerrainSlopeDesignTests.Design(20, 0.01, SlopeToe.Constant(ring, true, SlopeToe.Sides.Inside, SlopeSpec.Ratio(4)));
        }
    }
}
