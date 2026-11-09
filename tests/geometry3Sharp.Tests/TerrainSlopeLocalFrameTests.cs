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
    /// TerrainSlopeDesign.LocalFrame (docs/design/TERRAIN.md 5.8.1): the design computed on the inputs translated in XY
    /// to a local origin, the outputs in that local frame with UsedOrigin.
    /// </summary>
    public class TerrainSlopeLocalFrameTests
    {
        // the inputs of d translated by -UsedOrigin (exact: LocalFrame checked it), so that the checks of the outputs,
        // which read Terrain and Toes, see the local frame the outputs are in
        internal static void InputsToLocal(TerrainSlopeDesign d)
        {
            Vector2d o = d.UsedOrigin;
            var toes = d.Toes.Select(t => new SlopeToe() {
                Points = t.Points.Select(p => new Vector3d(p.x - o.x, p.y - o.y, p.z)).ToList(), Closed = t.Closed, Side = t.Side,
                CutStart = t.CutStart, CutEnd = t.CutEnd, FillStart = t.FillStart, FillEnd = t.FillEnd
            }).ToList();
            var terrain = new DMesh3(d.Terrain, false, false, false, false);
            foreach (int v in terrain.VertexIndices()) {
                Vector3d p = terrain.GetVertex(v);
                Assert.Equal(p.x, (p.x - o.x) + o.x); Assert.Equal(p.y, (p.y - o.y) + o.y);
                terrain.SetVertex(v, new Vector3d(p.x - o.x, p.y - o.y, p.z));
            }
            d.Toes = toes; d.Terrain = terrain;
        }

        // the terrain bit for bit as the copy (-0.0 and +0.0 differ; Invariance.Difference compares with ==)
        static void AssertBitwise(DMesh3 copy, DMesh3 terrain)
        {
            Assert.Equal(copy.MaxVertexID, terrain.MaxVertexID);
            foreach (int v in copy.VertexIndices()) {
                Vector3d a = copy.GetVertex(v), b = terrain.GetVertex(v);
                Assert.True(BitConverter.DoubleToInt64Bits(a.x) == BitConverter.DoubleToInt64Bits(b.x) &&
                            BitConverter.DoubleToInt64Bits(a.y) == BitConverter.DoubleToInt64Bits(b.y) &&
                            BitConverter.DoubleToInt64Bits(a.z) == BitConverter.DoubleToInt64Bits(b.z), $"vertex {v}: {a} {b}");
            }
            Assert.Equal("", Invariance.Difference(copy, terrain));
        }

        // the 7-gon pit of r 11 m with a toe crossing (TerrainSlopeToeCrossingTests), with the default SlopeMargin
        static TerrainSlopeDesign Pit(Similarity s, bool local)
        {
            var d = TerrainSlopeToeCrossingTests.Make(0.07, 0.123, 2.1, false, s);
            d.LocalFrame = local;
            return d;
        }

        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = Pit(Similarity.Identity, true);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            return d;
        });

        // every frame succeeds with local outputs; the 2^k frames give the identity's result scaled, origin included,
        // bit for bit. The other frames pick another origin, so another local input
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Pit_Similarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var d = Pit(s, true);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            var r = reference.Value;
            // (far from the origin every coordinate's difference to the origin is a double; near it, coordinates smaller
            // than the origin with all their mantissa bits are not, and the run falls back to the input's frame, which
            // is local already: bit for bit the default run)
            if (s.Translation != Vector3d.Zero) Assert.False(d.LocalFrameFallback);
            if (d.LocalFrameFallback) {
                Assert.Equal(Vector2d.Zero, d.UsedOrigin);
                var here = Pit(s, false);
                Assert.True(here.Compute(), here.Message);
                Assert.Equal("", Invariance.Difference(here.SlopeRegion, d.SlopeRegion));
                Assert.Equal("", Invariance.Difference(here.Composite, d.Composite));
            }
            Assert.Equal(r.CatchLines.Count, d.CatchLines.Count);
            if (s.IsExact) {
                Assert.Equal(r.UsedOrigin * s.Scale, d.UsedOrigin);
                Assert.Equal(r.UsedMinFeatureSize * s.Scale, d.UsedMinFeatureSize);
                // (the outputs are local: the similarity's scale maps them, its translation is in UsedOrigin)
                var scaleOnly = new Similarity("scale", s.Scale, Matrix3d.Identity, Vector3d.Zero);
                Assert.Equal("", Invariance.Difference(r.SlopeSurface, Invariance.MappedBack(d.SlopeSurface, scaleOnly)));
                Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, scaleOnly)));
                Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, scaleOnly)));
            }
            InputsToLocal(d);
            TerrainSlopeCatchLineTests.AssertCatchLines(d, false);
            TerrainSlopeCatchLineTests.AssertComposite(d);
        }

        // the local outputs are, bit for bit, a default run on the inputs translated by -UsedOrigin, and its derived
        // sizes are the local ones: at UTM MinFeatureSize goes from 1.5 cm in the input frame to 4e-7 m
        [Fact]
        public void AtUtm_IsTheDefaultRunOnTheTranslatedInput()
        {
            var d = Pit(Similarity.Utm, true);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.True(d.UsedOrigin.Length > 1e6);
            var here = Pit(Similarity.Utm, false);
            here.Compute();
            Assert.True(d.UsedMinFeatureSize < 1e-3 * here.UsedMinFeatureSize, $"{d.UsedMinFeatureSize} {here.UsedMinFeatureSize}");

            var plain = Pit(Similarity.Utm, false);
            plain.UsedOrigin = d.UsedOrigin;
            InputsToLocal(plain);
            Assert.True(plain.Compute(), plain.Status + " " + plain.Message);
            Assert.Equal(plain.UsedMinFeatureSize, d.UsedMinFeatureSize);
            Assert.Equal(plain.UsedToeLowering, d.UsedToeLowering);
            Assert.Equal(plain.Volume, d.Volume);
            Assert.Equal("", Invariance.Difference(plain.SlopeSurface, d.SlopeSurface));
            Assert.Equal("", Invariance.Difference(plain.SlopeRegion, d.SlopeRegion));
            Assert.Equal("", Invariance.Difference(plain.Composite, d.Composite));
            Assert.Equal(plain.CatchLines.Count, d.CatchLines.Count);
            for (int i = 0; i < d.CatchLines.Count; ++i)
                Assert.Equal(plain.CatchLines[i].Points, d.CatchLines[i].Points);
        }

        // the inputs are restored: the toes are copied, the terrain translated in place and back, bit for bit
        [Fact]
        public void InputsUnchanged()
        {
            var d = Pit(Similarity.Utm, true);
            var toes = d.Toes;
            var points = d.Toes.Select(t => t.Points.ToList()).ToList();
            var terrain = d.Terrain;
            var copy = new DMesh3(d.Terrain);
            Assert.True(d.Compute(), d.Message);
            Assert.Same(toes, d.Toes);
            Assert.Same(terrain, d.Terrain);
            for (int i = 0; i < points.Count; ++i) Assert.Equal(points[i], d.Toes[i].Points);
            AssertBitwise(copy, d.Terrain);
        }

        // the terrain's timestamps do not change: a spatial index the caller holds on it stays valid and finds the same
        [Fact]
        public void CallersSpatialIndexStaysValid()
        {
            var d = Pit(Similarity.Utm, true);
            int ts = d.Terrain.Timestamp, shape = d.Terrain.ShapeTimestamp;
            var tree = new DMeshAABBTree3(d.Terrain, true);
            Vector3d q = d.Terrain.GetVertex(17) + new Vector3d(0.1, 0.2, 5);
            int before = tree.FindNearestTriangle(q);
            Assert.True(d.Compute(), d.Message);
            Assert.Equal(ts, d.Terrain.Timestamp);
            Assert.Equal(shape, d.Terrain.ShapeTimestamp);
            Assert.Equal(before, tree.FindNearestTriangle(q));
        }

        // a terrain coordinate -0.0 would come back as +0.0 (-0 - o + o): a fallback, the terrain unchanged bit for bit
        // (at UTM, where nothing else falls back: -0 - o is exact)
        [Fact]
        public void NegativeZeroInTheTerrain_FallsBack()
        {
            var d = Pit(Similarity.Utm, true);
            int v = d.Terrain.VertexIndices().First();
            Vector3d p = d.Terrain.GetVertex(v);
            d.Terrain.SetVertex(v, new Vector3d(-0.0, p.y, p.z));
            var copy = new DMesh3(d.Terrain);
            d.Compute();
            Assert.True(d.LocalFrameFallback);
            AssertBitwise(copy, d.Terrain);
        }

        // the terrain, translated in place, is restored bit for bit also when the run throws (here from CancelF, an
        // exception the design does not catch)
        [Fact]
        public void TerrainRestoredWhenTheRunThrows()
        {
            var d = Pit(Similarity.Utm, true);
            var copy = new DMesh3(d.Terrain);
            int calls = 0;
            d.CancelF = () => { if (++calls == 3) throw new NotSupportedException("probe"); return false; };
            Assert.Throws<NotSupportedException>(() => d.Compute());
            AssertBitwise(copy, d.Terrain);
            Assert.NotEqual(Vector2d.Zero, d.UsedOrigin);
        }

        // the wavy 48-gon at UTM (TerrainSlopeBandRepairTests): its local frame is near the origin, the case that failed
        // before #350 where UTM succeeded (SlopeViolation from needles of near-quadruple points, now merged:
        // TerrainSlopeBandRepairTests). It succeeds locally, with local outputs that pass the checks. (Its local input is
        // not the repro's: the origin is the toe box's centre, about 13 m from the polygon's centre)
        [Trait("Category", "Slow")]
        [Fact]
        public void WavyToeAtUtm_SucceedsLocally()
        {
            var d = TerrainSlopeBandRepairTests.WavyOnWaves(new Vector3d(327000.25, 5642000.5, -340));
            d.LocalFrame = true;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.False(d.LocalFrameFallback);
            // (the centre of the toe's box: the wavy 48-gon is not symmetric in y)
            Assert.True(Math.Abs(d.UsedOrigin.x - 327000.25) < 50 && Math.Abs(d.UsedOrigin.y - 5642000.5) < 50, d.UsedOrigin.ToString());
            Assert.True(d.Composite.GetBounds().Max.xy.Length < 1000);
            InputsToLocal(d);
            TerrainSlopeCatchLineTests.AssertCatchLines(d, false);
            TerrainSlopeCatchLineTests.AssertComposite(d);
        }

        // an input coordinate whose difference to the origin is not a double: the design runs in the input's frame
        // (UsedOrigin (0, 0), LocalFrameFallback), bit for bit the default run
        [Fact]
        public void InexactTranslation_FallsBackToTheInputFrame()
        {
            Func<bool, TerrainSlopeDesign> make = (local) => {
                var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-40, -40, 40, 40), 16, 16, (x, y) => 5 + 0.03 * x);
                // (a toe corner at x = 0.1: its last bits are below the ulp of the origin's magnitude, near 5)
                var toe = TerrainSlopeDesignTests.Square(10, 0, new Vector2d(5.1, 5.3)).ToList();
                return new TerrainSlopeDesign() {
                    Toes = { SlopeToe.Constant(toe, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)) },
                    Terrain = terrain, Tolerance = 0.05, BuildComposite = true, LocalFrame = local
                };
            };
            var d = make(true);
            bool ok = d.Compute();
            Assert.True(d.LocalFrameFallback);
            Assert.Equal(Vector2d.Zero, d.UsedOrigin);
            var here = make(false);
            Assert.Equal(here.Compute(), ok);
            Assert.Equal(here.Status, d.Status);
            Assert.Equal("", Invariance.Difference(here.SlopeRegion, d.SlopeRegion));
            Assert.Equal("", Invariance.Difference(here.Composite, d.Composite));
        }

        // a mixed cut and fill (Direction.Auto, two runs) at UTM, as a full composite and as the replacement, on a
        // terrain that is not compact (the triangles at its first vertex removed): bit for bit the default run on the
        // translated input, the terrain ids (ReplacedTerrainTriangles, the vertex maps) those of the caller's terrain
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MixedAtUtm_ReplacementAndIdsOfTheCallersTerrain(bool replacement)
        {
            Func<bool, TerrainSlopeDesign> make = (local) => {
                var d = TerrainSlopeMixedTests.Make(0.23, -1.1, 1.3, TerrainSlopeDesign.Directions.Auto, Similarity.Utm);
                foreach (int t in d.Terrain.VtxTrianglesItr(0).ToList()) d.Terrain.RemoveTriangle(t, true, false);
                Assert.False(d.Terrain.IsCompact);
                if (replacement) d.CompositeKind = TerrainSlopeDesign.CompositeKinds.Replacement;
                d.LocalFrame = local;
                return d;
            };
            var d = make(true);
            var before = new DMesh3(d.Terrain, false, false, false, false);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.False(d.LocalFrameFallback);
            Assert.NotNull(d.AutoCut); Assert.NotNull(d.AutoFill);
            // (the non-compact terrain restored bit for bit after the two runs of Auto)
            AssertBitwise(before, d.Terrain);
            var plain = make(false);
            plain.UsedOrigin = d.UsedOrigin;
            InputsToLocal(plain);
            Assert.True(plain.Compute(), plain.Status + " " + plain.Message);
            Assert.Equal(plain.Pieces.Count, d.Pieces.Count);
            Assert.Equal("", Invariance.Difference(plain.SlopeRegion, d.SlopeRegion));
            Assert.Equal(plain.ReplacedTerrainTriangles, d.ReplacedTerrainTriangles);
            Assert.Equal(plain.CompositeTerrainVertices, d.CompositeTerrainVertices);
            Assert.Equal(plain.TerrainCompositeVertices, d.TerrainCompositeVertices);
            if (replacement) {
                Assert.Null(d.Composite);
                Assert.Equal("", Invariance.Difference(plain.Replacement, d.Replacement));
                // (the replaced triangles are triangles of the caller's terrain, the vertex map into its vertices)
                Assert.All(d.ReplacedTerrainTriangles, t => Assert.True(d.Terrain.IsTriangle(t)));
                Assert.All(d.CompositeTerrainVertices.Where(v => v >= 0), v => Assert.True(d.Terrain.IsVertex(v)));
            } else {
                Assert.Equal("", Invariance.Difference(plain.Composite, d.Composite));
            }
        }

        // at UTM, one stray terrain vertex near (0, 0) with fine bits: its difference to the origin is not a double, and
        // the run falls back to the input frame, as the default run
        [Fact]
        public void StrayVertexAtUtm_FallsBack()
        {
            Func<bool, TerrainSlopeDesign> make = (local) => {
                var d = Pit(Similarity.Utm, local);
                d.Terrain.AppendVertex(new Vector3d(1.2345678901234567, 0.1, 100));
                return d;
            };
            var d = make(true);
            bool ok = d.Compute();
            Assert.True(d.LocalFrameFallback);
            Assert.Equal(Vector2d.Zero, d.UsedOrigin);
            var here = make(false);
            Assert.Equal(here.Compute(), ok);
            Assert.Equal(here.Status, d.Status);
            Assert.Equal("", Invariance.Difference(here.SlopeRegion, d.SlopeRegion));
        }

        // no toe point or a non-finite coordinate: the input frame's run reports it, no origin, no fallback
        [Fact]
        public void InvalidInput_AsTheDefaultRun()
        {
            foreach (int c in new[] { 0, 1 }) {
                Func<bool, TerrainSlopeDesign> make = (local) => {
                    var d = Pit(Similarity.Utm, local);
                    if (c == 0) d.Toes.Clear();
                    else d.Toes[0].Points[2] = new Vector3d(double.NaN, d.Toes[0].Points[2].y, 0);
                    return d;
                };
                var d = make(true);
                Assert.False(d.Compute());
                var here = make(false);
                here.Compute();
                Assert.Equal(here.Status, d.Status);
                Assert.Equal(Vector2d.Zero, d.UsedOrigin);
                Assert.False(d.LocalFrameFallback);
            }
        }

        // without LocalFrame: UsedOrigin (0, 0), no fallback
        [Fact]
        public void Default_InTheInputFrame()
        {
            var d = Pit(Similarity.Utm, false);
            d.Compute();
            Assert.Equal(Vector2d.Zero, d.UsedOrigin);
            Assert.False(d.LocalFrameFallback);
        }

        [Fact]
        public void Reuse()
        {
            var d = Pit(Similarity.Utm, true);
            Assert.True(d.Compute(), d.Message);
            var o = d.UsedOrigin;
            var c1 = d.Composite;
            Assert.True(d.Compute(), d.Message);
            Assert.Equal(o, d.UsedOrigin);
            Assert.Equal("", Invariance.Difference(c1, d.Composite));
            // (and switched off on the same instance: the input frame again)
            d.LocalFrame = false;
            d.Compute();
            Assert.Equal(Vector2d.Zero, d.UsedOrigin);
        }
    }
}
