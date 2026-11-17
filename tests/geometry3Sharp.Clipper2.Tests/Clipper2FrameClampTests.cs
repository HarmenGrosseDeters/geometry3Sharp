using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using g3;
using g3.Clipper2;
using geometry3Sharp.Tests;
using Xunit;
using static geometry3Sharp.Clipper2.Tests.Oracle;
using ClipType = g3.Clipper2.ClipType;
using FillRule = g3.Clipper2.FillRule;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// ClipperFrame.ForBounds keeps the grid no finer than the doubles of the coordinates: far from the origin (a
    /// millimetre design at UTM coordinates) a grid of 2^40 units across the extent had grid points that rounded to the
    /// same or to swapped doubles, so slivers of the integer result came out as outer rings that are not counterclockwise
    /// or holes that are not clockwise (rotated lattice pairs at UTM with scale 1e-3: 2824 of 8000 results). The clamped
    /// grid maps back to doubles exactly.
    /// </summary>
    public class Clipper2FrameClampTests
    {
        static readonly ClipType[] Ops = { ClipType.Union, ClipType.Intersection, ClipType.Difference, ClipType.Xor };
        static readonly FillRule[] Rules = { FillRule.EvenOdd, FillRule.NonZero, FillRule.Positive, FillRule.Negative };

        static Polygon2d RandomRing(Random r, int range)
        {
            int n = 3 + r.Next(22);
            var v = new List<Vector2d>();
            for (int i = 0; i < n; ++i) v.Add(new Vector2d(r.Next(-range, range + 1), r.Next(-range, range + 1)));
            return new Polygon2d(v);
        }

        /// <summary> self-intersecting 3- to 24-gons on the lattice -2..2, the first pairs of seed 17 </summary>
        static IEnumerable<(Polygon2d a, Polygon2d b)> LatticePairs(int count)
        {
            var rand = new Random(17);
            for (int k = 0; k < count; ++k)
                yield return (RandomRing(rand, 2), RandomRing(rand, 2));
        }

        const int PairCount = 4;

        [Theory]
        [MemberData(nameof(Similarity.Names2D), MemberType = typeof(Similarity))]
        public void LatticePairs_AreWellFormedAndExact_UnderSimilarity(string name)
        {
            // every similarity of the plane, among them the rotated millimetre design at UTM coordinates (which failed the
            // orientation checks before the clamp); power-of-two scales also give the same result, scaled, bit for bit
            Similarity s = Similarity.ByName(name);
            Polygon2d Map(Polygon2d p) => new Polygon2d(p.Vertices.Select(s.Apply2d));
            // a lattice design of extent 2 at UTM coordinates: beyond 8192 times the extent, so its frame is clamped
            bool clamped = s.Translation.x != 0 || s.Translation.y != 0;
            bool exactScale = s.Angle2d == 0 && s.Translation.x == 0 && s.Translation.y == 0 && Clipper2DegenerateUnionTests.IsPowerOfTwo(s.Scale);
            foreach (var (a, b) in LatticePairs(PairCount))
                foreach (ClipType op in Ops)
                    foreach (FillRule rule in Rules) {
                        // the overlap samples only where the clamp acts (they cost most of the time)
                        var (error, slack, r) = Clipper2DegenerateUnionTests.Check(op, rule, Map(a), Map(b), clamped ? new Random(5) : null);
                        Assert.True(error <= slack, name + " " + op + " " + rule + ": area off by " + error + " grid units, more than the rounding " + slack);
                        AssertOnTheGrid(r, clamped);
                        if (exactScale) {
                            var (_, _, plain) = Clipper2DegenerateUnionTests.Check(op, rule, a, b);
                            Assert.Equal(plain.Polygons.Count, r.Polygons.Count);
                            for (int i = 0; i < plain.Polygons.Count; ++i)
                                foreach (var (rp, rm) in Rings(plain.Polygons[i]).Zip(Rings(r.Polygons[i]), (x, y) => (x, y)))
                                    Assert.Equal(rp.Vertices.Select(v => s.Scale * v), rm.Vertices);
                        }
                    }
        }

        static IEnumerable<Polygon2d> Rings(GeneralPolygon2d g) => new[] { g.Outer }.Concat(g.Holes);

        /// <summary>
        /// Where the grid is not finer than the doubles of the result and its origin is a grid point (a clamped frame, also
        /// a lattice input near the origin), every result vertex is a grid point exactly: it maps to the grid and back unchanged.
        /// </summary>
        static void AssertOnTheGrid(Clipper2Result r, bool clamped = false)
        {
            ClipperFrame f = r.Frame;
            var vertices = r.Polygons.SelectMany(Rings).SelectMany(p => p.Vertices).ToList();
            double far = vertices.Select(v => Math.Max(Math.Abs(v.x), Math.Abs(v.y)))
                .Concat(new[] { Math.Abs(f.Origin.x), Math.Abs(f.Origin.y) }).Max();
            bool originOnGrid = Math.Round(f.Origin.x * f.Scale) / f.Scale == f.Origin.x && Math.Round(f.Origin.y * f.Scale) / f.Scale == f.Origin.y;
            bool applies = originOnGrid && far * f.Scale <= Math.Pow(2, 53);
            if (clamped)
                Assert.True(applies, "clamped frame " + f + ": origin off the grid or grid finer than the doubles at " + far);
            if (!applies)
                return;
            foreach (Vector2d v in vertices)
                Assert.Equal(v, f.ToVector2d(f.ToPoint64(v)));
        }

        /// <summary> the frame of a rotated millimetre design at UTM coordinates is clamped, so AssertOnTheGrid checks it </summary>
        [Fact]
        public void RotatedUtmDesign_ResultIsOnTheGrid()
        {
            Similarity s = Similarity.RotatedZUtmMillimetre;
            var (a, b) = LatticePairs(1).First();
            var r = Clipper2Polygons.BooleanPaths(ClipType.Union, new[] { new Polygon2d(a.Vertices.Select(s.Apply2d)) },
                new[] { new Polygon2d(b.Vertices.Select(s.Apply2d)) }, new Clipper2Options { FillRule = FillRule.EvenOdd });
            Assert.True(r.Ok, r.ToString());
            Assert.Equal(Math.Pow(2, 30), r.Frame.Scale);
            Assert.Equal(r.Frame.Origin.x, Math.Round(r.Frame.Origin.x * r.Frame.Scale) / r.Frame.Scale);
            Assert.Equal(r.Frame.Origin.y, Math.Round(r.Frame.Origin.y * r.Frame.Scale) / r.Frame.Scale);
            AssertOnTheGrid(r, clamped: true);
        }

        [Fact]
        public void Clamp_ActsOnlyFarFromTheOrigin()
        {
            // half extent 1, default 40 bits: scale 2^40 while the coordinates stay within 2^13 (doubles there are 2^-40
            // apart), the clamp from beyond: 2^39 up to 2^14, and so on
            AxisAlignedBox2d Box(double c) => new AxisAlignedBox2d(new Vector2d(c - 1, -1), new Vector2d(c + 1, 1));
            Assert.True(ClipperFrame.ForBounds(Box(0), 0, ClipperFrame.DefaultPrecisionBits, out ClipperFrame near));
            Assert.Equal(Math.Pow(2, 40), near.Scale);
            Assert.True(ClipperFrame.ForBounds(Box(8190.5), 0, ClipperFrame.DefaultPrecisionBits, out ClipperFrame below));
            Assert.Equal(Math.Pow(2, 40), below.Scale);
            Assert.Equal(new Vector2d(8190.5, 0), below.Origin);
            // a reach of exactly 2^13 counts as beyond (the reach is rounded up)
            Assert.True(ClipperFrame.ForBounds(Box(8191), 0, ClipperFrame.DefaultPrecisionBits, out ClipperFrame at));
            Assert.Equal(Math.Pow(2, 39), at.Scale);
            // the bounds center off the clamped grid: the origin moves onto it, by at most half a grid unit
            // (bounds 8190.5 + 2^-39 to 8192.5 in x, half extent 1 - 2^-40 there and 1 in y: center 8191.5 + 2^-40, a double below 2^13 but
            // half a unit off the clamped grid of 2^-39)
            double c = 8191.5 + Math.Pow(2, -40);
            var offGrid = new AxisAlignedBox2d(new Vector2d(8190.5 + Math.Pow(2, -39), -1), new Vector2d(8192.5, 1));
            Assert.Equal(c, offGrid.Center.x);
            Assert.True(ClipperFrame.ForBounds(offGrid, 0, ClipperFrame.DefaultPrecisionBits, out ClipperFrame above));
            Assert.Equal(Math.Pow(2, 39), above.Scale);
            Assert.True(Math.Abs(above.Origin.x - c) <= 0.5 * above.Resolution);
            Assert.Equal(above.Origin.x, Math.Round(above.Origin.x * above.Scale) / above.Scale);
            // more precision bits reach the doubles sooner: 50 bits are clamped beyond 2^3, 49 bits only beyond 2^4
            Assert.True(ClipperFrame.ForBounds(Box(6.5), 0, 50, out ClipperFrame fine));
            Assert.Equal(Math.Pow(2, 50), fine.Scale);
            Assert.True(ClipperFrame.ForBounds(Box(7.5), 0, 50, out ClipperFrame clamped));
            Assert.Equal(Math.Pow(2, 49), clamped.Scale);
            Assert.True(ClipperFrame.ForBounds(Box(7.5), 0, 49, out ClipperFrame coarser));
            Assert.Equal(Math.Pow(2, 49), coarser.Scale);
            // UTM coordinates: the 10 km footprint keeps its grid, a 2 m design is clamped to the doubles of 5.7e6 (2^-30)
            Assert.True(ClipperFrame.ForBounds(new AxisAlignedBox2d(new Vector2d(632000, 5712000), new Vector2d(642000, 5713000)), 0, 40, out ClipperFrame footprint));
            Assert.Equal(Math.Pow(2, 27), footprint.Scale);
            Assert.True(ClipperFrame.ForBounds(new AxisAlignedBox2d(new Vector2d(632116, 5712042), new Vector2d(632118, 5712044)), 0, 40, out ClipperFrame design));
            Assert.Equal(Math.Pow(2, 30), design.Scale);
        }

        [Fact]
        public void Clamp_SnapsBothCoordinates()
        {
            // the smaller coordinate's center off the clamped grid of the larger one, its range crossing 2^20: the origin's
            // y moves onto the grid too, so the grid points above 2^20 (multiples of 2^-30) are doubles there
            var box = new AxisAlignedBox2d(new Vector2d(5712042, Math.Pow(2, 20) - 1.5 + Math.Pow(2, -32)), new Vector2d(5712044, Math.Pow(2, 20) + 0.5));
            Assert.Equal(Math.Pow(2, 20) - 0.5 + Math.Pow(2, -33), box.Center.y);
            Assert.True(ClipperFrame.ForBounds(box, 0, ClipperFrame.DefaultPrecisionBits, out ClipperFrame f));
            Assert.Equal(Math.Pow(2, 30), f.Scale);
            Assert.Equal(f.Origin.y, Math.Round(f.Origin.y * f.Scale) / f.Scale);
            var top = new Point64(0, (long)Math.Round((box.Max.y - f.Origin.y) * f.Scale) - 1);
            Assert.Equal(top, f.ToPoint64(f.ToVector2d(top)));
        }

        [Fact]
        public void Clamp_CoversTheMargin()
        {
            // bounds up to 2^23 and a margin of 0.75 grid units (a Minkowski pattern): the rounded reach 2^23 - 1 + 1 + 0.75u
            // is 2^23, but the true one passes it, where the doubles are 2u apart; the reach rounded up takes the next binade
            double u = Math.Pow(2, -30);
            var box = new AxisAlignedBox2d(new Vector2d(Math.Pow(2, 23) - 2, 0), new Vector2d(Math.Pow(2, 23), 2));
            Assert.True(ClipperFrame.ForBounds(box, 0.75 * u, ClipperFrame.DefaultPrecisionBits, out ClipperFrame f));
            Assert.Equal(Math.Pow(2, 29), f.Scale);
            var edge = new Point64((long)Math.Ceiling((1 + 0.75 * u) * f.Scale), 0);
            Assert.Equal(edge, f.ToPoint64(f.ToVector2d(edge)));
            // the Minkowski sum itself: every vertex on the grid
            var path = new Polygon2d(new[] { new Vector2d(box.Min.x, 0), new Vector2d(box.Max.x, 0), new Vector2d(box.Max.x, 2), new Vector2d(box.Min.x, 2) });
            var pattern = new Polygon2d(new[] { new Vector2d(0.75 * u, 0), new Vector2d(-0.75 * u, 0.75 * u), new Vector2d(-0.75 * u, -0.75 * u) });
            Clipper2Result r = Clipper2Polygons.MinkowskiSum(pattern, path, true);
            Assert.True(r.Ok, r.ToString());
            AssertOnTheGrid(r, clamped: true);
        }

        [Fact]
        public void Clamp_AtTheThreshold_GivesTheTranslatedResult()
        {
            // the same lattice pair (scaled to within ±0.5) just inside and just beyond the clamp: the grid at the origin has
            // 2^40 units across the half extent 2^e, the clamp acts once the coordinates exceed T = 2^(e + 13); both sides are
            // well-formed and exact on their grid, and give the region at the origin up to the grids
            var (a, b) = LatticePairs(1).First();
            Polygon2d Shift(Polygon2d p, double c) => new Polygon2d(p.Vertices.Select(v => new Vector2d(0.25 * v.x + c, 0.25 * v.y)));
            foreach (ClipType op in Ops) {
                var (_, _, origin) = Clipper2DegenerateUnionTests.Check(op, FillRule.EvenOdd, Shift(a, 0), Shift(b, 0));
                double threshold = Math.Pow(2, 53) / origin.Frame.Scale;
                foreach (double c in new[] { threshold - 1, threshold + 0.5 }) {
                    var (error, slack, r) = Clipper2DegenerateUnionTests.Check(op, FillRule.EvenOdd, Shift(a, c), Shift(b, c), new Random(5));
                    Assert.True(error <= slack, c + " " + op + ": area off by " + error + ", more than " + slack);
                    Assert.Equal(c < threshold ? origin.Frame.Scale : 0.5 * origin.Frame.Scale, r.Frame.Scale);
                    Assert.Equal(origin.Area, r.Area, 6);
                    AssertOnTheGrid(r);
                }
            }
        }

        [Fact]
        public void Clamp_KeepsPowerOfTwoScaling()
        {
            // a clamped frame scales with the input: inputs scaled by 2^k give the same integers and the scaled origin
            var rand = new Random(6);
            var box = new AxisAlignedBox2d(new Vector2d(632116.3, 5712042.1), new Vector2d(632117.9, 5712043.7));
            Assert.True(ClipperFrame.ForBounds(box, 0.01, 40, out ClipperFrame f1));
            Assert.Equal(Math.Pow(2, 30), f1.Scale);
            foreach (int k in new[] { -20, -3, 7, 40 }) {
                double s = Math.Pow(2, k);
                Assert.True(ClipperFrame.ForBounds(new AxisAlignedBox2d(box.Min * s, box.Max * s), 0.01 * s, 40, out ClipperFrame fk));
                Assert.Equal(f1.Scale / s, fk.Scale);
                Assert.Equal(f1.Origin * s, fk.Origin);
                foreach (Vector2d p in Samples(rand, box, 50))
                    Assert.Equal(f1.ToPoint64(p), fk.ToPoint64(p * s));
            }
        }
    }
}
