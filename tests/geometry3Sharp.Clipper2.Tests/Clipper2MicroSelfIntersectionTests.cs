using System;
using System.Linq;
using g3;
using g3.Clipper2;
using geometry3Sharp.Tests;
using Xunit;
using static geometry3Sharp.Clipper2.Tests.Oracle;
using V = g3.Clipper2.Vendored;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// #756: FixSelfIntersects' "micro self-intersection" branch of the vendored Clipper2 2.0.0 inserts a copy of the vertex
    /// two ahead. Where that flips the ring's orientation it reshaped the path instead of removing a twist (an Intersection
    /// of 0.590 came out as one ring of area -0.076); the insertion is then undone and the crossing goes to DoSplitOp
    /// (owner decision 2026-11-13; upstream's removal of the branch, 4da1564, is not adopted). The oracle is the exact area
    /// of the region (ExactRegionArea, via Clipper2DegenerateUnionTests.Check), within the grid rounding.
    /// </summary>
    public class Clipper2MicroSelfIntersectionTests
    {
        // the #756 case, shrunk from a pair of 16-gons on the lattice -4..4
        static readonly Polygon2d FlipA = Ring(0, 4, -2, -1, 4, 2, -4, 2, -1, -1, -4, -2, 1, -1);
        static readonly Polygon2d FlipB = Ring(-2, -3, -2, 0, 0, -2);

        [Fact]
        public void RingFlippedByTheMicroFix_IsSplitInstead()
        {
            var (error, slack, r) = Clipper2DegenerateUnionTests.Check(ClipType.Intersection, FillRule.NonZero, FlipA, FlipB, new Random(1));
            Assert.True(error <= slack, "area off by " + error + " grid units, more than the rounding " + slack);
            Assert.True(Math.Abs(r.Area - 0.5904761904761904) < 1e-6, r.Area.ToString());   // -0.0762 before the fix
            // the raw engine: the flat result is one positive ring
            V.Path64 P(Polygon2d p) => new V.Path64(p.Vertices.Select(v => new V.Point64((long)v.x << 20, (long)v.y << 20)));
            var clipper = new V.Clipper64();
            clipper.AddSubject(P(FlipA));
            clipper.AddClip(P(FlipB));
            var flat = new V.Paths64();
            Assert.True(clipper.Execute(V.ClipType.Intersection, V.FillRule.NonZero, flat));
            Assert.All(flat, p => Assert.True(V.Clipper.Area(p) > 0));
            Assert.True(Math.Abs(flat.Sum(p => V.Clipper.Area(p)) / Math.Pow(2, 40) - 0.5904761904761904) < 1e-4);
        }

        [Fact]
        public void XorOf24gons_IsExact()
        {
            // a pair of 24-gons on -2..2 from the campaign of #756: Xor NonZero 6.4559 before the fix, exact 6.7892
            Polygon2d a = Ring(1, 2, -1, -2, 2, -1, -2, -1, 0, 0, 0, 2, 1, 0, 0, -2, 1, -2, -1, 2, 1, -2, 2, 1, -1, -2, 1, 2,
                               0, -2, 0, 0, 0, 2, -1, -1, 0, -1, 2, 0, 0, 0, 1, 1, 0, 2, 0, -1);
            Polygon2d b = Ring(-2, -2, 1, 2, -2, 0, -1, 1, 2, 1, 1, 1, 0, -1, 1, 0, -2, 2, -1, 1, -2, -2, 0, 0, -2, 1, -1, -2,
                               0, -1, -2, 0, 0, 0, 2, -1, -1, 1, -1, -2, 1, 1, 0, 2, 0, -2, -2, -1);
            var (error, slack, r) = Clipper2DegenerateUnionTests.Check(ClipType.Xor, FillRule.NonZero, a, b, new Random(2));
            Assert.True(error <= slack, "area off by " + error + " grid units, more than the rounding " + slack);
            Assert.True(Math.Abs(r.Area - 6.789199689199688) < 1e-6, r.Area.ToString());
        }

        [Theory]
        [MemberData(nameof(Similarity.Names2D), MemberType = typeof(Similarity))]
        public void RingFlippedByTheMicroFix_UnderSimilarity(string name)
        {
            // What is invariant (as in Clipper2DegenerateUnionTests.TouchingHole_UnderSimilarity): Clipper2 computes on the
            // adapter's grid integers; a power-of-two scale gives the same integers, so the same result bit for bit; any
            // other similarity gives other integers, so the claim is the area: exact for the snapped input up to the rounding
            Similarity s = Similarity.ByName(name);
            Polygon2d Map(Polygon2d p) => new Polygon2d(p.Vertices.Select(s.Apply2d));
            var (_, _, plain) = Clipper2DegenerateUnionTests.Check(ClipType.Intersection, FillRule.NonZero, FlipA, FlipB);
            var (error, slack, mapped) = Clipper2DegenerateUnionTests.Check(ClipType.Intersection, FillRule.NonZero, Map(FlipA), Map(FlipB));
            Assert.True(error <= slack, name + ": area off by " + error + " grid units, more than the rounding " + slack);
            double rounding = slack / (mapped.Frame.Scale * mapped.Frame.Scale);
            Assert.True(Math.Abs(mapped.Area - s.Scale * s.Scale * plain.Area) <= rounding + 1e-9 * s.Scale * s.Scale * plain.Area,
                name + ": area " + mapped.Area + " against " + s.Scale * s.Scale * plain.Area);
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
