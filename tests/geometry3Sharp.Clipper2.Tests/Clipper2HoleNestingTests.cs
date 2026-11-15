using System;
using System.Linq;
using g3;
using g3.Clipper2;
using geometry3Sharp.Tests;
using Xunit;
using static geometry3Sharp.Clipper2.Tests.Oracle;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// ClipperFrame.ToGeneralPolygons where Clipper2's tree is right in area (its signed areas sum to the exact area) but
    /// nests a hole wrongly, so that the hole came out as a filled polygon of its own (2026-11-15; random 16- and 24-gons on
    /// -4..4 and -2..2 through the adapter, every clip type and fill rule: 34 of 160 000 results):
    /// - a negative path at the top level of the tree that touches its holder's edge, with a vertex rounded to the grid point
    ///   just outside it: `Contains` now takes a vertex within one grid unit of the holder's boundary as on it;
    /// - a negative path nested in a hole as an island: it is another hole of that hole's outer boundary, touching it;
    /// - and a path of zero grid area, which came out as an outer boundary that is not counterclockwise, is dropped.
    /// Shrunk cases; the oracle is the exact area of the region (ExactRegionArea, via Clipper2DegenerateUnionTests.Check).
    /// </summary>
    public class Clipper2HoleNestingTests
    {
        public static TheoryData<string, ClipType, FillRule, double[], double[], double> Cases => new TheoryData<string, ClipType, FillRule, double[], double[], double> {
            // a top-level hole one grid unit outside its holder: 4.0070 before (the sliver of 0.0070 filled instead of cut out)
            { "outside by a grid unit", ClipType.Difference, FillRule.Negative, new double[] { 4, -1, -4, 3, 4, 0 },
              new double[] { -2, -2, 3, 3, 2, 4, -1, -4, 4, -1, -2, 2, -2, 3 }, 3.9929824561403637 },
            // the same with 24-gons on -2..2: 6.3462 before
            { "outside by a grid unit, 24-gons", ClipType.Xor, FillRule.NonZero, new double[] { -2, 0, -1, -1, 1, -1 },
              new double[] { 2, -1, -2, -2, -2, 1, 2, -2, -1, 1, -2, -1, 2, 2, 0, -2, -2, 0 }, 6.108080808075389 },
            // 2.4950 before
            { "outside by a grid unit, union", ClipType.Union, FillRule.Negative, new double[] { 1, 2, -2, 0, 2, 1, 1, 2, 0, -2 },
              new double[] { -2, -2, 2, -1, 2, -2, -1, -1, 2, 1, 0, -2, 1, 2, 2, 1, -1, 2 }, 2.483300033299667 },
            // an island of negative area in a hole: 6.1236 before
            { "negative island in a hole", ClipType.Union, FillRule.EvenOdd, new double[] { 2, -1, 0, 1, 2, 1, -1, -2, 1, 1, 0, -1, 2, -1, 1, 1, 1, 0, 2, 2, -1, 2 },
              new double[] { 2, 0, -2, 1, 2, -1, 1, 1, 0, -1, 1, 0, 2, 2 }, 6.040277777775854 },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void HoleIsCutOut(string name, ClipType op, FillRule rule, double[] a, double[] b, double exact)
        {
            var (error, slack, r) = Clipper2DegenerateUnionTests.Check(op, rule, Ring(a), Ring(b), new Random(4));
            Assert.True(error <= slack, name + ": area off by " + error + " grid units, more than the rounding " + slack);
            Assert.True(Math.Abs(r.Area - exact) < 1e-6, name + ": " + r.Area);
            Assert.Contains(r.Polygons, g => g.Holes.Count > 0);
        }

        [Theory]
        [InlineData(FillRule.NonZero, 29.718143779428033)]   // 27.971 before
        [InlineData(FillRule.EvenOdd, 27.428708498686117)]
        [InlineData(FillRule.Negative, 20.490876165859717)]
        public void UnionOfTheReviewOf756_IsExact(FillRule rule, double area)
        {
            // the case the review of #756 found (FINDINGS item 5): right on a 2^20 grid, wrong on the adapter's (scale 2^38)
            Polygon2d a = Ring(-2, -2, 1, 1, -4, 3, -2, 4, 1, -3, -2, -1, -3, 0, 4, 1, -3, -3, 4, 4, -2, 1, -2, -2, -1, -2, 2, -4);
            Polygon2d b = Ring(0, 4, 3, 4, 1, -2, 4, 1, 0, -3, 3, 4, 0, -1, 2, 3, -3, -4, -4, -2, 3, 2, -1, 3, -4, 2, -1, 1);
            var (error, slack, r) = Clipper2DegenerateUnionTests.Check(ClipType.Union, rule, a, b, new Random(6));
            Assert.True(error <= slack, "area off by " + error + " grid units, more than the rounding " + slack);
            Assert.True(Math.Abs(r.Area - area) < 1e-6, r.Area.ToString());
        }

        [Theory]
        [InlineData(FillRule.NonZero)]
        [InlineData(FillRule.Positive)]
        public void ZeroAreaPath_IsDropped(FillRule rule)
        {
            // a pair of 24-gons on -2..2 whose Intersection has a path of zero grid area (three collinear points): it was
            // converted to an "outer boundary" that is not counterclockwise; it bounds nothing and is dropped
            Polygon2d a = Ring(1, 1, -2, 2, 1, -2, -2, -2, -1, 2, -2, 1, -2, 2, -2, -1, 1, 0, -2, 0, 1, -2, 0, -1, 0, 2, 2, -2, 1, 0,
                               -2, -2, -1, 0, -1, -2, 0, -2, 1, 0, 0, 1, 1, 1, -1, 1, -2, 0);
            Polygon2d b = Ring(-2, 2, 2, 2, 0, 0, 1, 1, -2, 2, -1, 2, -2, -2, -1, 2, 2, -2, -1, 1, 0, -2, 1, 1, 2, 1, 2, 2, -1, 1,
                               2, 2, 2, -1, 2, 1, 0, -2, 1, 1, 1, 1, 2, 1, 2, 1, -1, 0);
            var (error, slack, r) = Clipper2DegenerateUnionTests.Check(ClipType.Intersection, rule, a, b, new Random(5));
            Assert.True(error <= slack, "area off by " + error + " grid units, more than the rounding " + slack);
            Assert.All(r.Polygons, g => Assert.True(SignedArea(g.Outer.Vertices) > 0));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names2D), MemberType = typeof(Similarity))]
        public void HoleIsCutOut_UnderSimilarity(string name)
        {
            // as Clipper2MicroSelfIntersectionTests.RingFlippedByTheMicroFix_UnderSimilarity: power-of-two scales give the same
            // integers and so the same result bit for bit, any other similarity other integers, so the claim is the area.
            // Two cases are left out at the UTM offset with scale 1e-3: their results have slivers narrower than the rounding of
            // the grid points back to doubles there (an ulp of 5.7e6 is 2.6e5 grid units), which come out as rings of zero or
            // negative area in doubles (the limit in ToGeneralPolygons' summary), so the structural check of Check fails on them
            Similarity s = Similarity.ByName(name);
            Polygon2d Map(Polygon2d p) => new Polygon2d(p.Vertices.Select(s.Apply2d));
            bool slivers = s.Name == Similarity.RotatedZUtmMillimetre.Name;
            foreach (object[] c in Cases.Where(c => !slivers || ((string)c[0] != "outside by a grid unit, 24-gons" && (string)c[0] != "negative island in a hole"))) {
                var (op, rule, a, b) = ((ClipType)c[1], (FillRule)c[2], Ring((double[])c[3]), Ring((double[])c[4]));
                var (_, _, plain) = Clipper2DegenerateUnionTests.Check(op, rule, a, b);
                var (error, slack, mapped) = Clipper2DegenerateUnionTests.Check(op, rule, Map(a), Map(b));
                Assert.True(error <= slack, name + " " + c[0] + ": area off by " + error + " grid units, more than the rounding " + slack);
                double rounding = slack / (mapped.Frame.Scale * mapped.Frame.Scale);
                Assert.True(Math.Abs(mapped.Area - s.Scale * s.Scale * plain.Area) <= rounding + 1e-9 * s.Scale * s.Scale * plain.Area,
                    name + " " + c[0] + ": area " + mapped.Area + " against " + s.Scale * s.Scale * plain.Area);
                if (s.Angle2d == 0 && s.Translation.x == 0 && s.Translation.y == 0 && Clipper2DegenerateUnionTests.IsPowerOfTwo(s.Scale)) {
                    Assert.Equal(plain.Polygons.Count, mapped.Polygons.Count);
                    for (int i = 0; i < plain.Polygons.Count; ++i)
                        foreach (var (rp, rm) in new[] { plain.Polygons[i].Outer }.Concat(plain.Polygons[i].Holes)
                                     .Zip(new[] { mapped.Polygons[i].Outer }.Concat(mapped.Polygons[i].Holes), (x, y) => (x, y)))
                            Assert.Equal(rp.Vertices.Select(v => s.Scale * v), rm.Vertices);
                }
            }
        }
    }
}
