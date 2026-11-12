using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QA 2.0 T11 (terrain): CsCheck campaign for TerrainSlopeDesign on random wavy terrains (cut, fill and Auto, a closed polygonal toe,
    /// ratios 1 to 3, jittered and flipped grids). After a Compute() with the status Success or NoDaylight (the other statuses are the
    /// documented limits of a design, listed in Acceptable): never steeper (MaxSlopeRatio at most 1, the exact check of the library),
    /// the catch-line oracles (cut and fill: on the terrain, between the cones and Tolerance below them, simple lines) and the
    /// composite oracles of TerrainSlopeCatchLineTests (counter-clockwise, no XY overlap, one vertex per position, the terrain's
    /// footprint), the volumes consistent (the library's cut and fill volumes against TerrainVolumeDifference of terrain and
    /// composite), a second Compute() on the same instance and a new instance equal bit for bit, and the design scaled by 2^k
    /// equal bit for bit (scaled).
    /// 5 samples in CI; G3_PROPERTY_SAMPLES=n, G3_PROPERTY_CAMPAIGN=terrainslope with G3_PROPERTY_REPLAY=seed to replay.
    /// </summary>
    [Trait("Category", CampaignRunner.Category)]
    public class TerrainSlopeCampaignTests
    {
        readonly ITestOutputHelper log;
        public TerrainSlopeCampaignTests(ITestOutputHelper log) { this.log = log; }

        public sealed class Scene
        {
            public int Dir;             // 0 cut, 1 fill, 2 auto
            public int Toe;             // polygon vertices, 3 .. 10
            public int Radius;          // toe radius in 0.5 units, 10 .. 28
            public int Ratio;           // slope 1 : (Ratio / 8), 8 .. 24
            public int Amp, Wave, Height;   // terrain: amplitude in 0.5, wave number in 0.01, height above the toe in 0.5
            public int Jitter;          // 0 or 1 (0.3 random z)
            public int Nx, Ny;          // grid cells, 12 .. 28
            public bool Flip;
            public int Seed;
            public int Tilt;            // Auto: the terrain tilts through the toe plane, 0 .. 2 (x 0.4 per unit)
            public int Side;            // 0 Outside, 1 Inside
            public int Holes;           // 0 .. 3 triangles removed at the terrain corner far from the toe (no-data cells)
            public override string ToString() => $"{new[] { "cut", "fill", "auto" }[Dir]}: toe {Toe}-gon r {Radius / 2.0}, 1:{Ratio / 8.0}, terrain amp {Amp / 2.0} wave {Wave / 100.0} height {Height / 2.0} jitter {Jitter} tilt {Tilt} grid {Nx}x{Ny}{(Flip ? " flipped" : "")}, side {(Side == 0 ? "outside" : "inside")}, holes {Holes}, seed {Seed}";
        }

        /// <summary> the design, all lengths multiplied by 2^shift (exact) </summary>
        static TerrainSlopeDesign Make(Scene s, int shift, bool utm = false)
        {
            double f = Math.ScaleB(1.0, shift);
            var jr = new Random(s.Seed);
            double amp = s.Amp / 2.0, kk = s.Wave / 100.0, h = s.Height / 2.0, sign = s.Dir == 1 ? -1 : 1;
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-80, -80, 80, 80), s.Nx, s.Ny,
                (x, y) => sign * (h + amp * Math.Sin(kk * x) * Math.Cos(0.7 * kk * y)) + (s.Dir == 2 ? s.Tilt * 0.1 * x : 0) + (s.Jitter == 0 ? 0 : 0.3 * (jr.NextDouble() - 0.5)), s.Flip);
            for (int k = 0; k < s.Holes; ++k) terrain.RemoveTriangle(k);
            Vector3d move = utm ? new Vector3d(5.6e6 + 0.25, 3.5e5 + 0.125, 120) : Vector3d.Zero;
            foreach (int v in terrain.VertexIndices().ToList()) terrain.SetVertex(v, f * terrain.GetVertex(v) + move);
            var toe = SlopeToe.Constant(TerrainSlopeCatchLineTests.Ngon(s.Toe, s.Radius / 2.0, 0, Vector2d.Zero, 0.1).Select(p => f * p + move).ToList(), true,
                s.Side == 0 ? SlopeToe.Sides.Outside : SlopeToe.Sides.Inside, SlopeSpec.Ratio(s.Ratio / 8.0));
            if (s.Dir == 1) { toe.FillStart = toe.CutStart; toe.CutStart = null; }
            else if (s.Dir == 2) { toe.FillStart = toe.CutStart; }
            return new TerrainSlopeDesign() {
                Toes = { toe }, Terrain = terrain, Tolerance = f * 0.05, BuildComposite = true,
                Direction = s.Dir == 0 ? TerrainSlopeDesign.Directions.Cut : s.Dir == 1 ? TerrainSlopeDesign.Directions.Fill : TerrainSlopeDesign.Directions.Auto
            };
        }

        static bool Acceptable(TerrainSlopeDesign.Statuses st) =>
            st == TerrainSlopeDesign.Statuses.Success || st == TerrainSlopeDesign.Statuses.NoDaylight;

        static long Bits(double v) => BitConverter.DoubleToInt64Bits(v);

        static string SameMesh(DMesh3 a, DMesh3 b, double scale)
        {
            if (a == null || b == null) return a == b ? "" : "one mesh is null";
            if (a.VertexCount != b.VertexCount || a.TriangleCount != b.TriangleCount) return $"{a.VertexCount}/{a.TriangleCount} vs {b.VertexCount}/{b.TriangleCount} vertices/triangles";
            var va = a.VertexIndices().ToList(); var vb = b.VertexIndices().ToList();
            for (int i = 0; i < va.Count; ++i) {
                Vector3d p = a.GetVertex(va[i]), q = b.GetVertex(vb[i]);
                if (Bits(p.x * scale) != Bits(q.x) || Bits(p.y * scale) != Bits(q.y) || Bits(p.z * scale) != Bits(q.z)) return $"vertex {i} differs: {p} * {scale} vs {q}";
            }
            var ta = a.TriangleIndices().ToList(); var tb = b.TriangleIndices().ToList();
            for (int i = 0; i < ta.Count; ++i) if (a.GetTriangle(ta[i]) != b.GetTriangle(tb[i])) return $"triangle {i} differs";
            return "";
        }

        static string SameDesign(TerrainSlopeDesign a, TerrainSlopeDesign b, double scale)
        {
            if (a.Status != b.Status) return $"status {a.Status} vs {b.Status}";
            if (a.CatchLines.Count != b.CatchLines.Count) return $"{a.CatchLines.Count} vs {b.CatchLines.Count} catch lines";
            for (int l = 0; l < a.CatchLines.Count; ++l) {
                var pa = a.CatchLines[l].Points; var pb = b.CatchLines[l].Points;
                if (pa.Count != pb.Count) return $"catch line {l}: {pa.Count} vs {pb.Count} points";
                for (int i = 0; i < pa.Count; ++i)
                    if (Bits(pa[i].x * scale) != Bits(pb[i].x) || Bits(pa[i].y * scale) != Bits(pb[i].y) || Bits(pa[i].z * scale) != Bits(pb[i].z)) return $"catch line {l} point {i} differs";
            }
            string d = SameMesh(a.Composite, b.Composite, scale);
            if (d != "") return "composite: " + d;
            d = SameMesh(a.SlopeRegion, b.SlopeRegion, scale);
            return d == "" ? "" : "slope region: " + d;
        }

        static int accepted, rejected, volumeChecked;

        static string Property(Scene s, int[] shifts)
        {
            TerrainSlopeDesign d = Make(s, 0);
            try { d.Compute(); }
            catch (Exception e) { return "Compute threw " + e.GetType().Name + ": " + e.Message; }
            if (!Acceptable(d.Status)) {
                System.Threading.Interlocked.Increment(ref rejected);
                // (the documented limits of a design are no failure; an internal one is)
                if (d.Status == TerrainSlopeDesign.Statuses.InvalidInput || d.Status == TerrainSlopeDesign.Statuses.NotImplemented || d.Status == TerrainSlopeDesign.Statuses.Failed || d.Status == TerrainSlopeDesign.Statuses.SlopeViolation ||
                    d.Status == TerrainSlopeDesign.Statuses.CompositeFailed || d.Status == TerrainSlopeDesign.Statuses.NotHeightField)
                    return "status " + d.Status + ": " + d.Message;
                return "";
            }
            System.Threading.Interlocked.Increment(ref accepted);
            string r = Oracles(s, d);
            if (r != "") return r;
            // reuse: the same instance again, a new instance
            TerrainSlopeDesign first = Make(s, 0); first.Compute();
            string diff = SameDesign(first, d, 1);
            if (diff != "") return "a new instance differs: " + diff;
            d.Compute();
            diff = SameDesign(first, d, 1);
            if (diff != "") return "a second Compute on the same instance differs: " + diff;
            // translated to UTM coordinates (not bit for bit: the same status and catch lines, the volumes close)
            TerrainSlopeDesign ut = Make(s, 0, true);
            ut.Compute();
            if (ut.Status != first.Status) return $"translated to UTM: status {ut.Status} instead of {first.Status} ({ut.Message})";
            if (ut.CatchLines.Count != first.CatchLines.Count) return $"translated to UTM: {ut.CatchLines.Count} catch lines instead of {first.CatchLines.Count}";
            double vtol = 1e-3 * (Math.Abs(first.CutVolume) + Math.Abs(first.FillVolume)) + first.Tolerance * (first.PlanArea + first.CutArea + first.FillArea);
            if (Math.Abs(ut.CutVolume - first.CutVolume) > vtol || Math.Abs(ut.FillVolume - first.FillVolume) > vtol)
                return $"translated to UTM: volumes cut {ut.CutVolume}, fill {ut.FillVolume} instead of {first.CutVolume}, {first.FillVolume}";
            foreach (int shift in shifts) {
                TerrainSlopeDesign sd = Make(s, shift);
                sd.Compute();
                diff = SameDesign(first, sd, Math.ScaleB(1.0, shift));
                if (diff != "") return "scaled by 2^" + shift + " differs: " + diff;
            }
            return "";
        }

        static string Oracles(Scene s, TerrainSlopeDesign d)
        {
            try {
                if (d.Status == TerrainSlopeDesign.Statuses.Success && d.MaxSlopeRatio > 1 + 1e-9) return $"MaxSlopeRatio {d.MaxSlopeRatio} above 1 (steeper than the spec)";
                // (cut or fill: the catch-line oracle; Auto: when all pieces have one direction, else only the composite oracles)
                bool allFill = d.Pieces.Count > 0 && d.Pieces.All(p => p.Direction == TerrainSlopeDesign.Directions.Fill);
                bool allCut = d.Pieces.Count > 0 && d.Pieces.All(p => p.Direction == TerrainSlopeDesign.Directions.Cut);
                if (s.Dir != 2 || allFill || allCut) TerrainSlopeCatchLineTests.AssertCatchLines(d, s.Dir == 1 || allFill);
                TerrainSlopeCatchLineTests.AssertComposite(d, s.Dir == 1);
                if (d.Composite != null) {
                    var tvd = new TerrainVolumeDifference(d.Terrain, d.Composite);
                    if (tvd.Compute()) {
                        System.Threading.Interlocked.Increment(ref volumeChecked);
                        // the cut and fill volumes of the design against the volume difference of terrain and composite, up to the
                        // tolerance over the plan area of the slope (the composite is within Tolerance of the envelope)
                        double slack = d.Tolerance * (d.CutArea + d.FillArea) * 1.2 + 1e-9 * (Math.Abs(d.CutVolume) + Math.Abs(d.FillVolume) + 1);
                        if (Math.Abs(tvd.CutVolume - d.CutVolume) > slack) return $"cut volume {d.CutVolume} of the design, {tvd.CutVolume} of terrain against composite (slack {slack})";
                        if (Math.Abs(tvd.FillVolume - d.FillVolume) > slack) return $"fill volume {d.FillVolume} of the design, {tvd.FillVolume} of terrain against composite (slack {slack})";
                    }
                }
            } catch (Xunit.Sdk.XunitException e) {
                return "oracle: " + e.Message.Split('\n')[0];
            }
            return "";
        }

        static readonly Gen<Scene> GenScene =
            Gen.Select(Gen.Select(Gen.Int[0, 2], Gen.Int[3, 10], Gen.Int[10, 28], Gen.Int[8, 24], (a, b, c, d) => (a, b, c, d)),
                       Gen.Select(Gen.Int[2, 10], Gen.Int[5, 15], Gen.Int[12, 24], Gen.Int[0, 1], (a, w, h, j) => (a, w, h, j)),
                       Gen.Select(Gen.Int[12, 28], Gen.Int[12, 28], Gen.Bool, Gen.Int[0, 100000], Gen.Int[0, 1], Gen.Int[0, 3], (nx, ny, fl, sd, si, ho) => (nx, ny, fl, sd, si, ho)),
                       Gen.Int[0, 2],
                (x, y, z, t) => new Scene { Tilt = t, Side = z.Item5, Holes = z.Item6, Dir = x.Item1, Toe = x.Item2, Radius = x.Item3, Ratio = x.Item4, Amp = y.Item1, Wave = y.Item2, Height = y.Item3, Jitter = y.Item4, Nx = z.Item1, Ny = z.Item2, Flip = z.Item3, Seed = z.Item4 });

        static readonly int[] Shifts = { -9, 11 };

        /// <summary>
        /// every triangle of the slope region (the clipped slope triangles of the composite) by the exact oracle of the
        /// library, independently of the design's own check: not steeper than its piece's spec, its plane on or below the
        /// piece's toe ends (fill mirrored), counter-clockwise, finite
        /// </summary>
        internal static void AssertRegionNeverSteeper(TerrainSlopeDesign d)
        {
            Assert.NotNull(d.SlopeRegion);
            Assert.True(d.SlopeRegion.TriangleCount > 0);
            foreach (int t in d.SlopeRegion.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                d.SlopeRegion.GetTriVertices(t, ref a, ref b, ref c);
                Assert.True(MathUtil.IsFinite(a.x + a.y + a.z + b.x + b.y + b.z + c.x + c.y + c.z), $"region triangle {t} is not finite");
                SlopePiece pc = d.Pieces[d.SlopeRegion.GetTriangleGroup(t)];
                Vector3d p0 = pc.P0, p1 = pc.P1;
                if (pc.Direction == TerrainSlopeDesign.Directions.Fill) { a.z = -a.z; b.z = -b.z; c.z = -c.z; p0.z = -p0.z; p1.z = -p1.z; }
                Assert.True(ExactPredicates.Orient2d(a.xy, b.xy, c.xy) > 0, $"region triangle {t} is not counter-clockwise");
                Assert.True(Slope.IsNotSteeper(a, b, c, pc.Spec), $"region triangle {t} is steeper than its spec");
                Assert.True(Slope.IsPlaneOnOrBelow(a, b, c, p0) && Slope.IsPlaneOnOrBelow(a, b, c, p1), $"region triangle {t} rises above its toe segment's ends");
            }
        }

        static void AssertCertified(Scene s)
        {
            TerrainSlopeDesign d = Make(s, 0);
            Assert.True(d.Compute(), d.Status + ": " + d.Message);
            Assert.Equal(TerrainSlopeDesign.Statuses.Success, d.Status);
            Assert.True(d.MaxSlopeRatio <= 1, $"MaxSlopeRatio {d.MaxSlopeRatio}");
            AssertRegionNeverSteeper(d);
            Assert.NotNull(d.Composite);
            // (the campaign's property: the oracles of the catch lines, the composite and the volumes, a second Compute and a
            // new instance bit for bit, the design at UTM, and scaled by 2^-9 and 2^11 bit for bit)
            Assert.Equal("", Property(s, Shifts));
        }

        /// <summary>
        /// The first safe-failure class of #567, fixed: a closed toe with the slope on its inside, found by the campaign (seed
        /// lyr6; fill, 4-gon r 13, 1:1, jitter, grid 24x18, seed 70671). The four planes of the square meet in two points 9e-12
        /// apart, so the realized surface has a needle from a toe corner to the apex; it passes the exact check, but the
        /// catch line crosses it, and its clipped part has two catch points 7e-12 apart, whose z, rounded again, tilt its
        /// plane by e_z / 7e-12. That failed the check on every margin (4 attempts, then SlopeViolation). Now the region's
        /// failing triangle names its realized parent, and the band merge collapses the parent's short edge on the next
        /// realization (TERRAIN.md 5.5): certified at the first margin.
        /// </summary>
        [Fact]
        public void InsideOfAToe_NeedleAtTheApex_IsCertified()
        {
            var s = new Scene { Dir = 1, Toe = 4, Radius = 26, Ratio = 8, Amp = 8, Wave = 11, Height = 18, Jitter = 1, Nx = 24, Ny = 18, Flip = false, Seed = 70671, Side = 1, Holes = 0 };
            AssertCertified(s);
            TerrainSlopeDesign d = Make(s, 0);
            d.Compute();
            // (by the merge of the region's needle, at the first margin, not by a larger one: its edge recorded and found
            // by the band merge)
            Assert.True(d.RegionNeedles > 0 && d.RegionNeedleHits > 0, $"{d.RegionNeedles} edges, {d.RegionNeedleHits} hits");
            Assert.Equal(d.SlopeMargin, d.UsedSlopeMargin);
            // the reference path, a new build for every lowering (TerrainSlopeOneBuildTests), keeps the margin's region
            // needles: the same result bit for bit, and it ends (a time limit, not a hang)
            TerrainSlopeDesign r = Make(s, 0);
            r.RebuildForEachLowering = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            r.CancelF = () => sw.Elapsed.TotalSeconds > 60;
            Assert.True(r.Compute(), r.Status + ": " + r.Message);
            Assert.Equal(d.UsedToeLowering, r.UsedToeLowering);
            Assert.Equal("", SameDesign(d, r, 1));
            Assert.True(d.Attempts <= r.Attempts);
        }

        // the edge key of the region needles: the order of its ends and the sign of a zero do not matter
        [Fact]
        public void RegionNeedleEdgeKey_IgnoresOrderAndSignOfZero()
        {
            var p = new Vector2d(0.0, 1.5); var q = new Vector2d(-2.0, 0.25);
            var a = new TerrainSlopeDesign.XYEdge(p, q); var b = new TerrainSlopeDesign.XYEdge(q, p);
            Assert.Equal(a, b); Assert.Equal(a.GetHashCode(), b.GetHashCode());
            var n = new TerrainSlopeDesign.XYEdge(new Vector2d(-0.0, 1.5), q);
            Assert.Equal(a, n); Assert.Equal(a.GetHashCode(), n.GetHashCode());
            Assert.NotEqual(a, new TerrainSlopeDesign.XYEdge(p, new Vector2d(-2.0, 0.5)));
        }

        /// <summary>
        /// The second safe-failure class of #567, fixed: Direction Auto on a terrain that tilts through the toe plane (seed
        /// mjr6; auto, 8-gon r 11, 1:2.5, amplitude 5, wave 0.13, height 6, tilt 2, grid 28x13, seed 90309). The terrain
        /// triangle at a toe corner has its two other corners on the two toe segments there, catch points moved by 1e-11,
        /// so it overlaps each run's slope in a sliver along each segment. The certificate's step rule asked for one capsule
        /// around one boundary edge holding the run's whole overlap, which no capsule does at a corner, so it compared the
        /// slope across the step (0.23 where 0.0127 fits). The rule is now per slope triangle, whose overlap is convex and
        /// lies along one segment (TERRAIN.md 5.5; TerrainSlopeSnapBoundTests.StepsAtACornerOfTheToe).
        /// </summary>
        [Fact]
        public void AutoAtAToeCorner_StepSlivers_IsCertified()
        {
            AssertCertified(new Scene { Dir = 2, Toe = 8, Radius = 22, Ratio = 20, Amp = 10, Wave = 13, Height = 12, Jitter = 1, Nx = 28, Ny = 13, Flip = false, Seed = 90309, Side = 0, Holes = 2, Tilt = 2 });
        }

        [Fact]
        public void Campaign()
        {
            accepted = rejected = volumeChecked = 0;
            CampaignRunner.Run(log, nameof(TerrainSlopeCampaignTests), "terrainslope", GenScene, s => Property(s, Shifts), CampaignRunner.Samples(5), 411u);
            // (not vacuous: most designs end with a result)
            if (CampaignRunner.Samples(5) >= 5 && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("G3_PROPERTY_REPLAY"))) Assert.True(accepted > 2 * rejected && volumeChecked > 0, $"{accepted} designs with a result, {rejected} with another status, {volumeChecked} volume checks");
            log.WriteLine($"terrainslope: {accepted} designs with a result, {rejected} with another status, {volumeChecked} volume checks");
        }
    }
}
