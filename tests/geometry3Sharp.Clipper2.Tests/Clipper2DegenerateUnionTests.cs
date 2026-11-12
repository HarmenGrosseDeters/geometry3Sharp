using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using g3;
using g3.Clipper2;
using geometry3Sharp.Tests;
using Xunit;
using static geometry3Sharp.Clipper2.Tests.Oracle;
using V = g3.Clipper2.Vendored;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// FINDINGS item 5: a boolean of self-intersecting rings with exact degeneracies (a vertex on another edge) returned
    /// a region that neither input fills. Cause, in the vendored Clipper2 2.0.0 (Clipper.Engine.cs, DoSplitOp): a hole
    /// of an output path that touches the path at a vertex becomes a crossing once a nearby intersection point is
    /// rounded to the grid; FixSelfIntersects splits there, and DoSplitOp dropped the split-off triangle because its area
    /// has the other sign than the path's (taken for a figure-eight twist), which filled the hole. The fix keeps such a
    /// triangle as a path of its own if it lies inside the rest of the path.
    /// The oracle is the exact area of the region by its definition (ExactRegionArea: winding numbers, rational slabs),
    /// on the grid integers the adapter feeds to Clipper2; Clipper2 rounds its intersection points to the grid, so its
    /// area may differ from the exact one by the rounding: less than the perimeter of the result in grid units.
    /// </summary>
    public class Clipper2DegenerateUnionTests
    {
        internal static bool Fill(FillRule rule, int w)
            => rule == FillRule.EvenOdd ? (w & 1) != 0 : rule == FillRule.NonZero ? w != 0 : rule == FillRule.Positive ? w > 0 : w < 0;

        internal static Func<int[], bool> Predicate(ClipType op, FillRule rule) => w => {
            bool a = Fill(rule, w[0]), b = Fill(rule, w[1]);
            return op == ClipType.Union ? a || b : op == ClipType.Intersection ? a && b : op == ClipType.Difference ? a && !b : a != b;
        };

        static (long x, long y)[] Grid(Path64 path) => path.Select(q => (q.X, q.Y)).ToArray();

        /// <summary>
        /// |result area - exact area| in grid units, and the allowed rounding, for BooleanPaths(op) of one ring each, on
        /// the grid the call used. The rounding: Clipper2 rounds intersection points to the grid (half a unit per
        /// coordinate), and the result's double vertices map back to the grid only up to their own rounding far from
        /// the origin (an ulp of the coordinate in grid units, eg 16 units at UTM coordinates on the default grid);
        /// each moves an edge by at most that much, so the area by at most perimeter times the sum, plus 1.
        /// </summary>
        static (double error, double slack, Clipper2Result result) Check(ClipType op, FillRule rule, Polygon2d a, Polygon2d b)
        {
            Clipper2Result r = Clipper2Polygons.BooleanPaths(op, new[] { a }, new[] { b }, new Clipper2Options { FillRule = rule });
            Assert.True(r.Ok, r.ToString());
            ClipperFrame f = r.Frame;
            double exact = ExactRegionArea.Area(new[] { new[] { Grid(f.ToPath64(a)) }, new[] { Grid(f.ToPath64(b)) } }, Predicate(op, rule));
            double area = 0, perimeter = 0;
            foreach (GeneralPolygon2d g in r.Polygons)
                foreach (Polygon2d ring in new[] { g.Outer }.Concat(g.Holes)) {
                    Path64 p = f.ToPath64(ring);
                    double ringArea = Math.Abs(Clipper.Area(p));
                    area += ring == g.Outer ? ringArea : -ringArea;
                    for (int i = 0; i < p.Count; ++i) {
                        Point64 s = p[i], t = p[(i + 1) % p.Count];
                        perimeter += Math.Sqrt((double)(t.X - s.X) * (t.X - s.X) + (double)(t.Y - s.Y) * (t.Y - s.Y));
                    }
                }
            double far = r.Polygons.SelectMany(g => new[] { g.Outer }.Concat(g.Holes)).SelectMany(q => q.Vertices)
                .Select(v => Math.Max(Math.Abs(v.x), Math.Abs(v.y))).DefaultIfEmpty(0).Max();
            double backRounding = Ulp(far) * f.Scale;
            return (Math.Abs(area - exact), perimeter * (1 + 2 * backRounding) + 1, r);
        }

        static Polygon2d Lattice(Random rand, int n, int range)
            => new Polygon2d(Enumerable.Range(0, n).Select(_ => new Vector2d(rand.Next(-range, range + 1), rand.Next(-range, range + 1))));

        // ---- the oracle ----

        [Fact]
        public void ExactRegionArea_KnownRegions()
        {
            (long, long)[] R(params long[] c) => Enumerable.Range(0, c.Length / 2).Select(i => (c[2 * i], c[2 * i + 1])).ToArray();
            var square = R(0, 0, 4, 0, 4, 4, 0, 4);
            var bowtie = R(0, 0, 2, 2, 2, 0, 0, 2);   // two lobes of winding -1 and +1, area 1 each
            var inner = R(1, 1, 1, 3, 3, 3, 3, 1);    // clockwise
            Func<int[], bool> any = w => w[0] != 0, pos = w => w[0] > 0, neg = w => w[0] < 0, odd = w => (w[0] & 1) != 0;
            Assert.Equal(16, ExactRegionArea.Area(new[] { new[] { square } }, any));
            Assert.Equal(16, ExactRegionArea.Area(new[] { new[] { square } }, pos));
            Assert.Equal(0, ExactRegionArea.Area(new[] { new[] { square } }, neg));
            Assert.Equal(2, ExactRegionArea.Area(new[] { new[] { bowtie } }, any));
            Assert.Equal(1, ExactRegionArea.Area(new[] { new[] { bowtie } }, pos));
            Assert.Equal(1, ExactRegionArea.Area(new[] { new[] { bowtie } }, neg));
            Assert.Equal(12, ExactRegionArea.Area(new[] { new[] { square, inner } }, any));
            Assert.Equal(12, ExactRegionArea.Area(new[] { new[] { square, inner } }, odd));
            var twice = R(0, 0, 4, 0, 4, 4, 0, 4, 0, 0, 4, 0, 4, 4, 0, 4);   // around twice: winding 2
            Assert.Equal(0, ExactRegionArea.Area(new[] { new[] { twice } }, odd));
            Assert.Equal(16, ExactRegionArea.Area(new[] { new[] { twice } }, w => w[0] == 2));
            // two operands, and a crossing at a rational point: triangles (0,0) (6,0) (0,6) and (0,0) (6,0) (6,6) meet in
            // (3,3); their union is 18 + 18 - 9
            var t1 = R(0, 0, 6, 0, 0, 6);
            var t2 = R(0, 0, 6, 0, 6, 6);
            Assert.Equal(27, ExactRegionArea.Area(new[] { new[] { t1 }, new[] { t2 } }, w => w[0] != 0 || w[1] != 0));
            Assert.Equal(9, ExactRegionArea.Area(new[] { new[] { t1 }, new[] { t2 } }, w => w[0] != 0 && w[1] != 0));
            // rational crossings of a self-intersecting ring against winding samples (Oracle.Winding, sharing no code)
            var star = R(0, 0, 7, 3, 1, 5, 5, -2, 4, 6);
            var ring = new Polygon2d(star.Select(p => new Vector2d(p.Item1, p.Item2)));
            int inside = 0, n = 400;
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j)
                    if (Winding(ring, new Vector2d(0 + 7.0 * (i + 0.5) / n, -2 + 8.0 * (j + 0.5) / n)) != 0) ++inside;
            double sampled = 56.0 * inside / (n * n), area = ExactRegionArea.Area(new[] { new[] { star } }, any);
            Assert.True(Math.Abs(sampled - area) < 0.2, sampled + " sampled, " + area + " exact");
        }

        // ---- the defect ----

        [Fact]
        public void TouchingHole_IsKept()
        {
            // the smallest case of the campaign: a vertex of A, (-2, 0), lies on an edge of B; the triangle
            // (-3.09, -2.73) (-2, 0) (-1.45, -0.44) of winding 0 in both is a hole of the union, which was filled
            Polygon2d a = Ring(2, -2, -2, 0, 3, -4, 1, 3, -4, -4);
            Polygon2d b = Ring(-1, 2, -4, 0, -3, -3, -1, 3);
            var (error, slack, r) = Check(ClipType.Union, FillRule.NonZero, a, b);
            Assert.True(error <= slack, "area off by " + error + " grid units, more than the rounding " + slack);
            CheckWellFormed(r);
            Assert.Equal(1, r.Polygons.Sum(g => g.Holes.Count));
            double exact = ExactRegionArea.Area(new[] { new[] { Grid(R64(a)) }, new[] { Grid(R64(b)) } }, w => w[0] != 0 || w[1] != 0);
            Assert.True(Math.Abs(exact - r.Area) < 1e-7, r.Area + " against " + exact);   // 23.6474; 24.4246 before the fix
            // a point of the hole is outside the result
            Assert.Equal(0, ResultCount(r.Polygons, new Vector2d(-2.05, -0.6)));
            Assert.Equal(1, ResultCount(r.Polygons, new Vector2d(-2.3, -0.6)));
        }

        static Path64 R64(Polygon2d p) => new Path64(p.Vertices.Select(v => new Point64((long)v.x, (long)v.y)));

        [Fact]
        public void TouchingHole_IsKept_InTheEngine()
        {
            // the same on the vendored engine directly, flat paths and PolyTree: the hole is a path of its own
            V.Path64 P(params long[] c) => new V.Path64(Enumerable.Range(0, c.Length / 2).Select(i => new V.Point64(c[2 * i] << 20, c[2 * i + 1] << 20)));
            var clipper = new V.Clipper64();
            clipper.AddSubject(P(2, -2, -2, 0, 3, -4, 1, 3, -4, -4));
            clipper.AddClip(P(-1, 2, -4, 0, -3, -3, -1, 3));
            var flat = new V.Paths64();
            Assert.True(clipper.Execute(V.ClipType.Union, V.FillRule.NonZero, flat));
            Assert.Equal(1, flat.Count(p => V.Clipper.Area(p) < 0));
            var tree = new V.PolyTree64();
            Assert.True(clipper.Execute(V.ClipType.Union, V.FillRule.NonZero, tree));
            double scale = Math.Pow(2, 40);
            Assert.True(Math.Abs(tree.Area() / scale - 23.647358369184996) < 1e-4, (tree.Area() / scale).ToString());   // 24.4246 before
            Assert.Equal(flat.Sum(p => V.Clipper.Area(p)), tree.Area());
        }

        [Fact]
        public void FindingsRepro_UnionIsExact()
        {
            // the repro of FINDINGS item 5 (coordinates k / 256), at the default precision of 40 bits, where it failed
            Polygon2d a = Ring(0, 0, 0.125, 0, 0.125, 0.125, 0, 0.125, 0.5703125, 0.5703125, 0.6953125, 0.5703125, 0.125, 0.125, 0, 0.125);
            Polygon2d b = Ring(0.33984375, 0.08984375, -0.58984375, -0.58984375, 0.22265625, 0.21484375, 0.48828125, -0.02734375, 0.21484375, 0.08984375, 0.78515625, 0.66015625);
            var (error, slack, r) = Check(ClipType.Union, FillRule.NonZero, a, b);
            Assert.True(error <= slack, "area off by " + error + " grid units, more than the rounding " + slack);
            CheckWellFormed(r);
            // the probe point of the finding: filled by neither input, so not by the union
            var p = new Vector2d(0.25463542, 0.20286458);
            Assert.Equal(0, Winding(a, p));
            Assert.Equal(0, Winding(b, p));
            Assert.Equal(0, ResultCount(r.Polygons, p));
        }

        [Theory]
        [InlineData(FillRule.NonZero)]
        [InlineData(FillRule.EvenOdd)]
        public void RandomDegenerateRings_UnionIsExact(FillRule rule)
        {
            // 16-gons on the lattice -4..4: many vertices on other edges and collinear overlaps; 600 pairs, about 2 s.
            // Before the fix 2 to 5 of each 600 were off (0.04 to 4 percent of the area)
            var rand = new Random(rule == FillRule.NonZero ? 41 : 42);
            var failures = new List<string>();
            for (int i = 0; i < 600; ++i) {
                Polygon2d a = Lattice(rand, 16, 4), b = Lattice(rand, 16, 4);
                var (error, slack, _) = Check(ClipType.Union, rule, a, b);
                if (error > slack)
                    failures.Add("pair " + i + ": off by " + error + " grid units");
            }
            Assert.True(failures.Count == 0, string.Join("; ", failures));
        }

        [Theory]
        [InlineData(ClipType.Intersection)]
        [InlineData(ClipType.Difference)]
        [InlineData(ClipType.Xor)]
        public void RandomDegenerateRings_OtherOperations(ClipType op)
        {
            // the fix acts in the output cleanup shared by all operations: 100 pairs of 12-gons, every fill rule
            var rand = new Random(43 + (int)op);
            var failures = new List<string>();
            for (int i = 0; i < 100; ++i) {
                Polygon2d a = Lattice(rand, 12, 4), b = Lattice(rand, 12, 4);
                foreach (FillRule rule in new[] { FillRule.EvenOdd, FillRule.NonZero, FillRule.Positive, FillRule.Negative }) {
                    var (error, slack, _) = Check(op, rule, a, b);
                    if (error > slack)
                        failures.Add(rule + " pair " + i + ": off by " + error + " grid units");
                }
            }
            Assert.True(failures.Count == 0, string.Join("; ", failures));
        }

        // ---- invariance ----

        [Theory]
        [MemberData(nameof(Similarity.Names2D), MemberType = typeof(Similarity))]
        public void TouchingHole_UnderSimilarity(string name)
        {
            // What is invariant: Clipper2 computes on the grid integers of the adapter's frame. A power-of-two scale gives
            // the same integers, so the same result, scaled, bit for bit. Any other similarity gives other integers (the
            // touch of the hole is then no longer exact in general), so the claim is the area: exact for the snapped input,
            // up to the grid rounding, and the area of the unmapped result up to that rounding.
            Similarity s = Similarity.ByName(name);
            Polygon2d a = Ring(2, -2, -2, 0, 3, -4, 1, 3, -4, -4);
            Polygon2d b = Ring(-1, 2, -4, 0, -3, -3, -1, 3);
            Polygon2d Map(Polygon2d p) => new Polygon2d(p.Vertices.Select(s.Apply2d));
            var (_, _, plain) = Check(ClipType.Union, FillRule.NonZero, a, b);
            var (error, slack, mapped) = Check(ClipType.Union, FillRule.NonZero, Map(a), Map(b));
            Assert.True(error <= slack, name + ": area off by " + error + " grid units, more than the rounding " + slack);
            CheckWellFormed(mapped);
            double rounding = slack / (mapped.Frame.Scale * mapped.Frame.Scale);   // in mapped units
            Assert.True(Math.Abs(mapped.Area - s.Scale * s.Scale * plain.Area) <= rounding + 1e-9 * s.Scale * s.Scale * plain.Area,
                name + ": area " + mapped.Area + " against " + s.Scale * s.Scale * plain.Area);
            if (s.Angle2d == 0 && s.Translation.x == 0 && s.Translation.y == 0 && IsPowerOfTwo(s.Scale)) {
                Assert.Equal(plain.Polygons.Count, mapped.Polygons.Count);
                for (int i = 0; i < plain.Polygons.Count; ++i) {
                    var p = plain.Polygons[i]; var m = mapped.Polygons[i];
                    Assert.Equal(p.Holes.Count, m.Holes.Count);
                    foreach (var (rp, rm) in new[] { p.Outer }.Concat(p.Holes).Zip(new[] { m.Outer }.Concat(m.Holes), (x, y) => (x, y)))
                        Assert.Equal(rp.Vertices.Select(v => s.Scale * v), rm.Vertices);
                }
            }
        }

        static double Ulp(double x) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1) - x;

        static bool IsPowerOfTwo(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            return x > 0 && (bits & 0xFFFFFFFFFFFFFL) == 0;
        }
    }
}
