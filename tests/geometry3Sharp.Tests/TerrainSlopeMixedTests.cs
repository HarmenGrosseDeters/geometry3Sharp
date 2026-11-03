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
    /// Direction Auto, mixed cut and fill (TERRAIN.md 5.5, TERRAIN_1C_PLAN.md section 10): the clamp
    /// max(S_fill, min(T, S_cut)) from two runs on the terrain and one composite of both.
    /// </summary>
    public class TerrainSlopeMixedTests
    {
        // a 7-gon toe at z 0 (r 11) in a plane tilted by g towards the angle rot (grid of 23 x 25 cells): cut 1:1.5, fill 1:2
        internal static TerrainSlopeDesign Make(double g, double c, double rot, TerrainSlopeDesign.Directions dir, Similarity s)
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-60, -60, 60, 60), 23, 25,
                (x, y) => g * (x * Math.Cos(rot) + y * Math.Sin(rot)) + c);
            foreach (int v in terrain.VertexIndices().ToList()) terrain.SetVertex(v, s.Apply(terrain.GetVertex(v)));
            var toe = new SlopeToe() {
                Points = TerrainSlopeCatchLineTests.Ngon(7, 11, 0, Vector2d.Zero, 0.2).Select(p => s.Apply(p)).ToList(), Closed = true,
                Side = SlopeToe.Sides.Outside
            };
            toe.CutStart = Enumerable.Repeat(SlopeSpec.Ratio(1.5), 7).ToArray(); toe.CutEnd = (SlopeSpec[])toe.CutStart.Clone();
            toe.FillStart = Enumerable.Repeat(SlopeSpec.Ratio(2), 7).ToArray(); toe.FillEnd = (SlopeSpec[])toe.FillStart.Clone();
            return new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = s.ApplyLength(0.05), BuildComposite = true, Direction = dir };
        }

        internal static void AssertMixed(TerrainSlopeDesign d)
        {
            TerrainSlopeCatchLineTests.AssertComposite(d);
            TerrainSlopeCatchLineTests.AssertCatchLines(d.AutoCut);
            TerrainSlopeCatchLineTests.AssertCatchLines(d.AutoFill, true);
            // the outputs of both runs, cut first
            Assert.Equal(d.AutoCut.Pieces.Count + d.AutoFill.Pieces.Count, d.Pieces.Count);
            Assert.All(d.Pieces.Take(d.AutoCut.Pieces.Count), p => Assert.Equal(TerrainSlopeDesign.Directions.Cut, p.Direction));
            Assert.All(d.Pieces.Skip(d.AutoCut.Pieces.Count), p => Assert.Equal(TerrainSlopeDesign.Directions.Fill, p.Direction));
            Assert.Equal(d.AutoCut.CatchLines.Count + d.AutoFill.CatchLines.Count, d.CatchLines.Count);
            Assert.Equal(d.AutoCut.Volume, d.CutVolume);
            Assert.Equal(d.AutoFill.Volume, d.FillVolume);
            Assert.Equal(d.AutoCut.PlanArea, d.CutArea);
            Assert.Equal(d.AutoFill.PlanArea, d.FillArea);
            Assert.Equal(d.CutVolume + d.FillVolume, d.Volume);
            Assert.Equal(d.AutoCut.SlopeSurface.TriangleCount + d.AutoFill.SlopeSurface.TriangleCount, d.SlopeSurface.TriangleCount);
            // each region triangle in its run's group: cut below the terrain (excavated), fill above it
            int nc = d.AutoCut.Pieces.Count;
            foreach (int t in d.SlopeRegion.TriangleIndices()) {
                Vector3d c = d.SlopeRegion.GetTriCentroid(t);
                double tz = TerrainSlopeDesignTests.HeightAt(d.Terrain, c.xy);
                bool fill = d.SlopeRegion.GetTriangleGroup(t) >= nc;
                double scale = 1e-9 * (1 + Math.Abs(tz));
                Assert.True(fill ? c.z >= tz - scale : c.z <= tz + scale, $"region triangle {t}: z {c.z}, terrain {tz}, fill {fill}");
            }
            // no crack inside: every boundary edge of the composite lies on the terrain's border, or on the toe (the slope
            // steps off the terrain there), or ends at a toe crossing, where the cut part (λ below the toe) and the fill
            // part (λ above) meet at one XY with a step along the catch segment that the terrain part does not share
            var toe = d.Toes[0];
            int n = toe.Points.Count;
            Func<Vector2d, bool> onToe = (q) => Enumerable.Range(0, n).Any(i =>
                new Segment2d(toe.Points[i].xy, toe.Points[(i + 1) % n].xy).DistanceSquared(q) <= 1e-18 * (1 + q.LengthSquared));
            var crossings = d.CatchLines.Where(l => l.Closed == false).SelectMany(l => new[] { l.Points.First().xy, l.Points.Last().xy }).ToList();
            Func<Vector2d, bool> atCrossing = (q) => crossings.Any(x => x.Distance(q) <= d.UsedMinFeatureSize);
            var border = d.Terrain.BoundaryEdgeIndices().Select(e => { Index2i v = d.Terrain.GetEdgeV(e); return new Segment2d(d.Terrain.GetVertex(v.a).xy, d.Terrain.GetVertex(v.b).xy); }).ToList();
            Func<Vector2d, bool> onBorder = (q) => border.Any(sg => sg.DistanceSquared(q) <= 1e-18 * (1 + q.LengthSquared));
            foreach (int e in d.Composite.BoundaryEdgeIndices()) {
                Index2i ev = d.Composite.GetEdgeV(e);
                Vector2d a = d.Composite.GetVertex(ev.a).xy, b = d.Composite.GetVertex(ev.b).xy;
                bool ok = (onBorder(a) && onBorder(b)) || (onToe(a) && onToe(b)) || atCrossing(a) || atCrossing(b);
                Assert.True(ok, $"composite boundary edge {a} {b} inside");
            }
        }

        [Theory]
        [InlineData(0.15, 0.3, 0.0)]
        [InlineData(0.15, 0.3, 0.37)]
        [InlineData(0.23, -1.1, 1.3)]
        [InlineData(0.07, 0.123, 2.1)]
        public void ToeCrossingTheTerrain_TheClamp(double g, double c, double rot)
        {
            var d = Make(g, c, rot, TerrainSlopeDesign.Directions.Auto, Similarity.Identity);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.Equal(TerrainSlopeDesign.Statuses.Success, d.Status);
            AssertMixed(d);
            Assert.True(d.CutVolume > 0 && d.FillVolume > 0);
            // the same runs as a cut run and a fill run alone
            foreach (var dir in new[] { TerrainSlopeDesign.Directions.Cut, TerrainSlopeDesign.Directions.Fill }) {
                var r = Make(g, c, rot, dir, Similarity.Identity);
                Assert.True(r.Compute(), r.Status + " " + r.Message);
                var sub = dir == TerrainSlopeDesign.Directions.Cut ? d.AutoCut : d.AutoFill;
                Assert.Equal(r.Volume, dir == TerrainSlopeDesign.Directions.Cut ? d.CutVolume : d.FillVolume);
                Assert.Equal("", Invariance.Difference(r.SlopeSurface, sub.SlopeSurface));
                Assert.Equal(r.CatchLines.Count, sub.CatchLines.Count);
                for (int i = 0; i < r.CatchLines.Count; ++i) Assert.Equal(r.CatchLines[i].Points, sub.CatchLines[i].Points);
            }
            // the composite is the clamp max(S_fill, min(T, S_cut)) of the two realized surfaces and the terrain, at
            // points away from the catch lines (there the composite moves catch points by up to MinFeatureSize)
            var catchPts = d.CatchLines.SelectMany(l => l.Points).Select(p => p.xy).ToList();
            int tested = 0;
            for (double x = -45; x <= 45; x += 3.7)
                for (double y = -45; y <= 45; y += 3.3) {
                    var q = new Vector2d(x, y);
                    if (catchPts.Min(p => p.Distance(q)) < 1) continue;
                    double t = TerrainSlopeDesignTests.HeightAt(d.Terrain, q);
                    double sc = TerrainSlopeDesignTests.HeightAt(d.AutoCut.SlopeSurface, q);
                    double sf = TerrainSlopeDesignTests.HeightAt(d.AutoFill.SlopeSurface, q);
                    double clamp = Math.Max(double.IsNaN(sf) ? double.NegativeInfinity : sf, Math.Min(t, double.IsNaN(sc) ? double.PositiveInfinity : sc));
                    double z = TerrainSlopeDesignTests.HeightAt(d.Composite, q);
                    Assert.True(Math.Abs(z - clamp) <= 1e-9 * (1 + Math.Abs(clamp)), $"at {q}: composite {z}, clamp {clamp} (T {t}, cut {sc}, fill {sf})");
                    tested++;
                }
            Assert.True(tested > 400, $"{tested}");
        }

        [Theory]
        [InlineData(5.0)]
        [InlineData(-5.0)]
        public void ToeBelowOrAboveTheTerrain_AutoIsCutOrFill(double terrainZ)
        {
            var dir = terrainZ > 0 ? TerrainSlopeDesign.Directions.Cut : TerrainSlopeDesign.Directions.Fill;
            var d = Make(0.01, terrainZ, 0, TerrainSlopeDesign.Directions.Auto, Similarity.Identity);
            var r = Make(0.01, terrainZ, 0, dir, Similarity.Identity);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.True(r.Compute(), r.Status + " " + r.Message);
            AssertMixed(d);
            // the other run has no region; the composite and the region are the same as of the one direction, bit for bit
            Assert.Equal("", Invariance.Difference(r.Composite, d.Composite));
            Assert.Equal("", Invariance.Difference(r.SlopeRegion, d.SlopeRegion));
            Assert.Equal(r.Volume, d.Volume);
            Assert.Equal(dir == TerrainSlopeDesign.Directions.Cut ? r.Volume : 0, d.CutVolume);
            Assert.Equal(dir == TerrainSlopeDesign.Directions.Fill ? r.Volume : 0, d.FillVolume);
            Assert.Equal(r.CatchLines.Count, d.CatchLines.Count);
        }

        // TERRAIN.md appendix A: a fill surface that passes above the toe would pass above the cut surface. A U-shaped
        // open toe, the far leg 8 m lower, 20 m away: fill 1:4 falls 5 m over 20 m, from the high leg it stays 3 m above
        // the low one; cut 2:1 rises 40 m, no undercut
        [Fact]
        public void FillAboveTheToe_IsToeOvertopped()
        {
            var terrain = TerrainSlopeCatchLineTests.Grid(new AxisAlignedBox2d(-60, -60, 120, 80), 23, 25, (x, y) => 4);
            var pts = new List<Vector3d> { new Vector3d(0, 20, 8), new Vector3d(40, 20, 8), new Vector3d(60, 10, 4), new Vector3d(40, 0, 0), new Vector3d(0, 0, 0) };
            var toe = new SlopeToe() {
                Points = pts, Closed = false, Side = SlopeToe.Sides.Left,
                CutStart = Enumerable.Repeat(SlopeSpec.Ratio(0.5), 4).ToArray(), CutEnd = Enumerable.Repeat(SlopeSpec.Ratio(0.5), 4).ToArray(),
                FillStart = Enumerable.Repeat(SlopeSpec.Ratio(4), 4).ToArray(), FillEnd = Enumerable.Repeat(SlopeSpec.Ratio(4), 4).ToArray()
            };
            Func<TerrainSlopeDesign.Directions, TerrainSlopeDesign> make = (dir) =>
                new TerrainSlopeDesign() { Toes = { toe }, Terrain = terrain, Tolerance = 0.01, BuildComposite = true, Direction = dir };
            var cut = make(TerrainSlopeDesign.Directions.Cut);
            Assert.True(cut.Compute(), cut.Status + " " + cut.Message);
            var d = make(TerrainSlopeDesign.Directions.Auto);
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.ToeOvertopped, d.Status);
            Assert.NotEqual(TerrainSlopeDesign.StatusKinds.None, d.StatusKind);
            Assert.NotEmpty(d.ToeProblems);
            Assert.Null(d.Composite);
            Assert.Empty(d.CatchLines);
        }

        // the replacement output and no composite: the same runs, the replacement is the full composite's changed part
        [Fact]
        public void Auto_Replacement_AndNoComposite()
        {
            var full = Make(0.23, -1.1, 1.3, TerrainSlopeDesign.Directions.Auto, Similarity.Identity);
            Assert.True(full.Compute(), full.Message);
            var rep = Make(0.23, -1.1, 1.3, TerrainSlopeDesign.Directions.Auto, Similarity.Identity);
            rep.CompositeKind = TerrainSlopeDesign.CompositeKinds.Replacement;
            Assert.True(rep.Compute(), rep.Message);
            Assert.Null(rep.Composite);
            Assert.NotNull(rep.Replacement);
            Assert.Equal(full.ReplacedTerrainTriangles, rep.ReplacedTerrainTriangles);
            Assert.Equal("", Invariance.Difference(full.SlopeRegion, rep.SlopeRegion));
            // the replacement plus the terrain triangles not replaced: the full composite's triangle count
            Assert.Equal(full.Composite.TriangleCount, rep.Replacement.TriangleCount + full.Terrain.TriangleCount - rep.ReplacedTerrainTriangles.Count);
            var none = Make(0.23, -1.1, 1.3, TerrainSlopeDesign.Directions.Auto, Similarity.Identity);
            none.BuildComposite = false;
            Assert.True(none.Compute(), none.Message);
            Assert.Null(none.Composite); Assert.Null(none.SlopeRegion);
            Assert.Equal(full.CutVolume, none.CutVolume);
            Assert.Equal(full.FillVolume, none.FillVolume);
            Assert.Equal(full.CatchLines.Count, none.CatchLines.Count);
        }

        [Fact]
        public void Auto_NeedsATerrain_AndNoHeightLimit()
        {
            var d = Make(0.15, 0.3, 0, TerrainSlopeDesign.Directions.Auto, Similarity.Identity);
            d.HeightLimit = 10;
            Assert.False(d.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, d.Status);
            var e = Make(0.15, 0.3, 0, TerrainSlopeDesign.Directions.Auto, Similarity.Identity);
            e.Terrain = null;
            Assert.False(e.Compute());
            Assert.Equal(TerrainSlopeDesign.Statuses.InvalidInput, e.Status);
        }

        [Fact]
        public void Reuse_AndInsertionOrder()
        {
            var d = Make(0.15, 0.3, 0.37, TerrainSlopeDesign.Directions.Auto, Similarity.Identity);
            Assert.True(d.Compute(), d.Message);
            DMesh3 first = d.Composite;
            Assert.True(d.Compute(), d.Message);
            Assert.Equal("", Invariance.Difference(first, d.Composite));
            var r = Make(0.15, 0.3, 0.37, TerrainSlopeDesign.Directions.Auto, Similarity.Identity);
            r.InsertionOrder = n => Enumerable.Range(0, n).Reverse().ToArray();
            Assert.True(r.Compute(), r.Message);
            Assert.Equal("", Invariance.Difference(first, r.Composite));
            Assert.Equal(d.CutVolume, r.CutVolume);
            Assert.Equal(d.FillVolume, r.FillVolume);
        }

        // (SlopeMargin 1e-4: MinFeatureSize, 2 (8 ulp + ...) / δ, is absolute, about 1.5 cm at UTM with the default δ, more
        // than the features of this toe at scale 1e-3; with 1e-4 it is 0.15 mm, the same in every frame)
        static TerrainSlopeDesign MakeForFrames(Similarity s)
        {
            var d = Make(0.15, 0.3, 0.37, TerrainSlopeDesign.Directions.Auto, s);
            d.SlopeMargin = 1e-4;
            return d;
        }

        static readonly Lazy<TerrainSlopeDesign> reference = new Lazy<TerrainSlopeDesign>(() => {
            var d = MakeForFrames(Similarity.Identity);
            Assert.True(d.Compute(), d.Message);
            return d;
        });

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            if (s.IsRotated) s = new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation);
            var d = MakeForFrames(s);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            AssertMixed(d);
            var r = reference.Value;
            Assert.Equal(r.CatchLines.Count, d.CatchLines.Count);
            if (s.IsExact) {
                Assert.Equal("", Invariance.Difference(r.Composite, Invariance.MappedBack(d.Composite, s)));
                Assert.Equal("", Invariance.Difference(r.SlopeRegion, Invariance.MappedBack(d.SlopeRegion, s)));
                Assert.Equal(r.CutVolume * s.Scale * s.Scale * s.Scale, d.CutVolume);
                Assert.Equal(r.FillVolume * s.Scale * s.Scale * s.Scale, d.FillVolume);
            } else {
                double k = s.Scale * s.Scale * s.Scale;
                Assert.True(Math.Abs(d.CutVolume / k - r.CutVolume) <= 2 * 0.05 * r.CutArea, $"cut {d.CutVolume / k} vs {r.CutVolume}");
                Assert.True(Math.Abs(d.FillVolume / k - r.FillVolume) <= 2 * 0.05 * r.FillArea, $"fill {d.FillVolume / k} vs {r.FillVolume}");
            }
        }
    }
}
