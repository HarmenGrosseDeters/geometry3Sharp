using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using g3;
using g3.Clipper2;
using Xunit;
using static geometry3Sharp.Clipper2.Tests.Oracle;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// Booleans, clipping of polylines, Minkowski sums and simplification, checked against the oracles of
    /// Oracle.cs: exact areas, point sampling against the definition of each operation, orientation and nesting.
    /// </summary>
    public class Clipper2PolygonsTests
    {
        static readonly ClipType[] Ops = { ClipType.Union, ClipType.Intersection, ClipType.Difference, ClipType.Xor };

        static bool Expected(ClipType op, bool a, bool b)
        {
            switch (op) {
                case ClipType.Union: return a || b;
                case ClipType.Intersection: return a && b;
                case ClipType.Difference: return a && !b;
                default: return a != b;
            }
        }

        static bool Filled(FillRule rule, int w)
        {
            switch (rule) {
                case FillRule.EvenOdd: return (w & 1) != 0;
                case FillRule.NonZero: return w != 0;
                case FillRule.Positive: return w > 0;
                default: return w < 0;
            }
        }

        [Fact]
        public void Squares_ExactAreas()
        {
            var a = new[] { Rect(0, 0, 2, 2) };
            var b = new[] { Rect(1, 1, 3, 3) };
            Assert.Equal(7, Clipper2Polygons.Union(a, b).Area, 12);
            Assert.Equal(1, Clipper2Polygons.Intersection(a, b).Area, 12);
            Assert.Equal(3, Clipper2Polygons.Difference(a, b).Area, 12);
            Assert.Equal(6, Clipper2Polygons.Xor(a, b).Area, 12);
            Clipper2Result u = Clipper2Polygons.Union(a, b);
            CheckWellFormed(u);
            Assert.Single(u.Polygons);
            Assert.Equal(8, u.Polygons[0].Outer.VertexCount);   // the L-shaped outline, no collinear vertices
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void RandomPolygons_MatchDefinitionAtSamples(int seed)
        {
            var rand = new Random(seed);
            var a = new List<GeneralPolygon2d> { StarWithHole(rand, new Vector2d(0, 0), 23, 10), new GeneralPolygon2d(Star(rand, new Vector2d(9, 3), 17, 2, 6)) };
            var b = new List<GeneralPolygon2d> { StarWithHole(rand, new Vector2d(6, -2), 31, 9) };
            // a clockwise input polygon: oriented by the adapter
            Polygon2d cw = Star(rand, new Vector2d(-6, 6), 11, 2, 5); cw.Reverse();
            b.Add(new GeneralPolygon2d(cw));
            var box = new AxisAlignedBox2d(new Vector2d(-16, -16), new Vector2d(18, 16));
            Vector2d[] samples = Samples(rand, box, 4000).ToArray();
            foreach (ClipType op in Ops) {
                Clipper2Result r = Clipper2Polygons.Boolean(op, a, b);
                CheckWellFormed(r, samples);
                int tested = 0;
                foreach (Vector2d p in samples) {
                    if (BoundaryDistance(a, p) < 1e-6 || BoundaryDistance(b, p) < 1e-6) continue;   // grid rounding
                    bool inA = a.Any(g => Winding(g, p) > 0), inB = b.Any(g => Winding(g, p) > 0);
                    Assert.True(Expected(op, inA, inB) == (ResultCount(r.Polygons, p) == 1), op + " wrong at " + p);
                    ++tested;
                }
                Assert.True(tested > 3900);
            }
        }

        [Fact]
        public void AreaIdentities_Hold()
        {
            var rand = new Random(7);
            for (int k = 0; k < 10; ++k) {
                var a = new[] { StarWithHole(rand, new Vector2d(rand.NextDouble(), 0), 19, 5) };
                var b = new[] { StarWithHole(rand, new Vector2d(3, rand.NextDouble()), 13, 4) };
                double u = Clipper2Polygons.Union(a, b).Area, i = Clipper2Polygons.Intersection(a, b).Area;
                double d = Clipper2Polygons.Difference(a, b).Area, x = Clipper2Polygons.Xor(a, b).Area;
                double areaA = Area(a), areaB = Area(b);
                double tol = 1e-9 * (areaA + areaB);
                Assert.True(Math.Abs(u + i - areaA - areaB) < tol);
                Assert.True(Math.Abs(d - (areaA - i)) < tol);
                Assert.True(Math.Abs(x - (u - i)) < tol);
                Assert.True(i > 0 && d > 0);
            }
        }

        [Theory]
        [InlineData(FillRule.EvenOdd)]
        [InlineData(FillRule.NonZero)]
        [InlineData(FillRule.Positive)]
        [InlineData(FillRule.Negative)]
        public void RawPaths_FillRules_MatchWindingNumbers(FillRule rule)
        {
            var rand = new Random(11);
            // self-intersecting rings (a pentagram, winding 2 in the middle) and overlapping rings of both orientations
            Polygon2d star = new Polygon2d(Enumerable.Range(0, 5).Select(i => new Vector2d(5 * Math.Cos(i * 4 * Math.PI / 5), 5 * Math.Sin(i * 4 * Math.PI / 5))));
            Polygon2d cw = Star(rand, new Vector2d(2, 1), 13, 2, 4); cw.Reverse();
            Polygon2d ccw = Star(rand, new Vector2d(-2, -1), 9, 2, 4);
            var subject = new[] { star, cw, ccw };
            var clip = new[] { Ring(-1, -6, 7, -6, 7, 2, -1, 2) };
            var options = new Clipper2Options { FillRule = rule };
            var box = new AxisAlignedBox2d(new Vector2d(-7, -7), new Vector2d(8, 7));
            Vector2d[] samples = Samples(rand, box, 3000).ToArray();
            foreach (ClipType op in Ops) {
                Clipper2Result r = Clipper2Polygons.BooleanPaths(op, subject, clip, options);
                CheckWellFormed(r, samples);
                foreach (Vector2d p in samples) {
                    if (subject.Concat(clip).Min(ring => RingDistance(ring.Vertices, p)) < 1e-6) continue;
                    bool inA = Filled(rule, subject.Sum(ring => Winding(ring, p))), inB = Filled(rule, clip.Sum(ring => Winding(ring, p)));
                    Assert.True(Expected(op, inA, inB) == (ResultCount(r.Polygons, p) == 1), rule + " " + op + " wrong at " + p);
                }
            }
        }

        [Fact]
        public void HolesAndIslands_AreNested()
        {
            // a square ring with an island in its hole: two polygons, the ring with one hole and the island
            var outer = new[] { Rect(0, 0, 10, 10) };
            var cut = new[] { Rect(2, 2, 8, 8) };
            Clipper2Result ring = Clipper2Polygons.Difference(outer, cut);
            Clipper2Result r = Clipper2Polygons.Union(ring.Polygons, new[] { Rect(4, 4, 6, 6) });
            CheckWellFormed(r);
            Assert.Equal(2, r.Polygons.Count);
            GeneralPolygon2d big = r.Polygons.OrderByDescending(g => g.Outer.Area).First();
            GeneralPolygon2d island = r.Polygons.OrderBy(g => g.Outer.Area).First();
            Assert.Single(big.Holes);
            Assert.Empty(island.Holes);
            Assert.Equal(36, big.Holes[0].Area, 12);
            Assert.Equal(4, island.Area, 12);
            Assert.Equal(100 - 36 + 4, r.Area, 12);
            // the island lies inside the hole
            Assert.All(island.Outer.Vertices, v => Assert.NotEqual(0, Winding(big.Holes[0], v)));
            Assert.Same(r.Polygons[0], big);   // enclosing polygon first
        }

        [Fact]
        public void InputHoles_OfEitherOrientation_AreHoles()
        {
            // the hole given counterclockwise like its outer ring (AddHole without the orientation check)
            var g = new GeneralPolygon2d(Rect(0, 0, 10, 10).Outer);
            g.AddHole(Rect(3, 3, 7, 7).Outer, true, false);
            var flipped = new GeneralPolygon2d(new Polygon2d(Rect(0, 0, 10, 10).Outer.Vertices.Reverse().ToList()));
            flipped.AddHole(Rect(3, 3, 7, 7).Outer, true, true);
            foreach (GeneralPolygon2d input in new[] { g, flipped }) {
                Clipper2Result r = Clipper2Polygons.Union(new[] { input });
                CheckWellFormed(r);
                Assert.Single(r.Polygons);
                Assert.Single(r.Polygons[0].Holes);
                Assert.Equal(84, r.Area, 12);
            }
        }

        [Fact]
        public void ClipPolyLines_KeepsInsideAndOutsidePieces()
        {
            var square = new[] { Rect(0, 0, 4, 4) };
            var line = new PolyLine2d(new[] { new Vector2d(-2, 1), new Vector2d(6, 1), new Vector2d(6, 3), new Vector2d(2, 3) });
            Clipper2Result inside = Clipper2Polygons.ClipPolyLines(ClipType.Intersection, new[] { line }, square);
            Clipper2Result outside = Clipper2Polygons.ClipPolyLines(ClipType.Difference, new[] { line }, square);
            Assert.True(inside.Ok && outside.Ok);
            Assert.Empty(inside.Polygons);
            Assert.Equal(2, inside.PolyLines.Count);
            Assert.Equal(4 + 2, inside.PolyLines.Sum(l => l.ArcLength), 12);
            Assert.Equal(2 + 2 + 2 + 2, outside.PolyLines.Sum(l => l.ArcLength), 12);
            foreach (PolyLine2d piece in inside.PolyLines)
                foreach (Segment2d s in piece.SegmentItr())
                    Assert.True(Winding(square[0], s.Center) != 0);
        }

        [Fact]
        public void MinkowskiSum_OfSquares_IsLargerSquare()
        {
            Polygon2d pattern = Ring(-1, -1, 1, -1, 1, 1, -1, 1);   // 2 x 2 around the origin
            Polygon2d path = Rect(10, 10, 14, 14).Outer;
            Clipper2Result closed = Clipper2Polygons.MinkowskiSum(pattern, path, true);
            CheckWellFormed(closed);
            Assert.Single(closed.Polygons);
            Assert.Equal(36, closed.Area, 12);   // 6 x 6
            Assert.Equal(new AxisAlignedBox2d(new Vector2d(9, 9), new Vector2d(15, 15)), closed.Polygons[0].Bounds);
            // open path around the square (first vertex repeated): the outline swept by the pattern, with a 2 x 2 hole
            var outline = new Polygon2d(path.Vertices.Concat(new[] { path[0] }));
            Clipper2Result open = Clipper2Polygons.MinkowskiSum(pattern, outline, false);
            CheckWellFormed(open);
            Assert.Equal(36 - 4, open.Area, 12);
            Assert.Single(open.Polygons[0].Holes);
            // an open path of one segment: a 6 x 2 rectangle
            Clipper2Result segment = Clipper2Polygons.MinkowskiSum(pattern, Ring(0, 0, 4, 0), false);
            Assert.Equal(12, segment.Area, 12);
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(3, false)]
        public void MinkowskiSum_WithDisc_MatchesDistanceAtSamples(int seed, bool closed)
        {
            // a disc pattern (64-gon of radius r, inscribed): the sum is the set of points within r of the path,
            // up to the gap between the 64-gon and the circle (r (1 - cos(pi/64)))
            var rand = new Random(seed);
            double r = 0.8;
            var disc = new Polygon2d(Enumerable.Range(0, 64).Select(i => new Vector2d(r * Math.Cos(i * Math.PI / 32), r * Math.Sin(i * Math.PI / 32))));
            Polygon2d path = Star(rand, new Vector2d(1, 2), 17, 2, 5);
            Clipper2Result sum = Clipper2Polygons.MinkowskiSum(disc, path, closed);
            var box = new AxisAlignedBox2d(new Vector2d(-6, -5), new Vector2d(8, 9));
            Vector2d[] samples = Samples(rand, box, 4000).ToArray();
            CheckWellFormed(sum, samples);
            double gap = r * (1 - Math.Cos(Math.PI / 64)) + 1e-9;
            foreach (Vector2d p in samples) {
                double dist = RingDistance(path.Vertices, p, closed);
                if (closed && Winding(path, p) != 0) dist = 0;
                if (dist > r - gap && dist < r + 1e-9) continue;
                Assert.True((dist < r) == (ResultCount(sum.Polygons, p) == 1), "wrong at " + p + " (distance " + dist + ")");
            }
        }

        [Fact]
        public void MinkowskiDifference_MirrorsThePattern()
        {
            Polygon2d pattern = Ring(0, 0, 2, 0, 2, 1, 0, 1);   // in the +x +y quadrant
            Polygon2d path = Rect(0, 0, 1, 1).Outer;
            Clipper2Result sum = Clipper2Polygons.MinkowskiSum(pattern, path, true);
            Clipper2Result diff = Clipper2Polygons.MinkowskiDifference(pattern, path, true);
            Assert.Equal(new AxisAlignedBox2d(new Vector2d(0, 0), new Vector2d(3, 2)), sum.Polygons[0].Bounds);
            Assert.Equal(new AxisAlignedBox2d(new Vector2d(-2, -1), new Vector2d(1, 1)), diff.Polygons[0].Bounds);
            Assert.Equal(6, diff.Area, 12);
        }

        [Fact]
        public void Simplify_RemovesNoiseWithinEpsilon()
        {
            // a square with noise vertices along its edges, below epsilon
            var rand = new Random(5);
            var v = new List<Vector2d>();
            Vector2d[] corners = { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10) };
            for (int c = 0; c < 4; ++c)
                for (int i = 0; i < 20; ++i) {
                    Vector2d a = corners[c], b = corners[(c + 1) % 4];
                    Vector2d n = new Vector2d(-(b - a).y, (b - a).x).Normalized;
                    v.Add(Vector2d.Lerp(a, b, i / 20.0) + (i == 0 ? 0 : 0.01 * (rand.NextDouble() - 0.5)) * n);
                }
            var noisy = new[] { new GeneralPolygon2d(new Polygon2d(v)) };
            Clipper2Result r = Clipper2Polygons.Simplify(noisy, 0.05);
            CheckWellFormed(r);
            Assert.Single(r.Polygons);
            Assert.Equal(4, r.Polygons[0].Outer.VertexCount);
            Assert.True(Math.Abs(r.Area - 100) < 0.05 * 40);
            Clipper2Result lines = Clipper2Polygons.SimplifyPolyLines(new[] { new PolyLine2d(v) }, 0.05);
            Assert.True(lines.Ok);
            Assert.Single(lines.PolyLines);
            Assert.True(lines.PolyLines[0].VertexCount < 10);
            Assert.True((v[0] - lines.PolyLines[0].Start).Length <= lines.Frame.Resolution);   // ends kept
            Assert.True((v[v.Count - 1] - lines.PolyLines[0].End).Length <= lines.Frame.Resolution);
        }
    }
}
