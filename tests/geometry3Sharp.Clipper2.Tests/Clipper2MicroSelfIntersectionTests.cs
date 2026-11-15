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
    

        // ---- the micro fix moves the path by at most one grid unit (2026-11-15) ----

        /// <summary>
        /// The raw engine on the inputs times 2^shift against the exact area of the region: (error, rounding slack, area), in grid
        /// units squared; the slack is the result's perimeter plus one, the error a rounded intersection point can cause.
        /// </summary>
        static (double error, double slack, double area) RawEngine(ClipType op, FillRule rule, double[] a, double[] b, int shift = 20)
        {
            (long x, long y)[] Grid(double[] c) => Enumerable.Range(0, c.Length / 2).Select(i => ((long)c[2 * i] << shift, (long)c[2 * i + 1] << shift)).ToArray();
            V.Path64 Path((long x, long y)[] r) => new V.Path64(r.Select(p => new V.Point64(p.x, p.y)));
            var ga = Grid(a); var gb = Grid(b);
            var clipper = new V.Clipper64();
            clipper.AddSubject(Path(ga));
            clipper.AddClip(Path(gb));
            var result = new V.Paths64();
            Assert.True(clipper.Execute((V.ClipType)(int)op, (V.FillRule)(int)rule, result));
            double area = 0, perimeter = 0;
            foreach (V.Path64 p in result) {
                area += V.Clipper.Area(p);
                for (int i = 0; i < p.Count; ++i) {
                    V.Point64 s = p[i], t = p[(i + 1) % p.Count];
                    perimeter += Math.Sqrt((double)(t.X - s.X) * (t.X - s.X) + (double)(t.Y - s.Y) * (t.Y - s.Y));
                }
            }
            double exact = ExactRegionArea.Area(new[] { new[] { ga }, new[] { gb } }, Clipper2DegenerateUnionTests.Predicate(op, rule));
            return (Math.Abs(area - exact), perimeter + 1, area / Math.Pow(2, 2 * shift));
        }

        // Shrunk from the remaining wrong results of #756 (random 3- to 24-gons on -2..2 and -4..4): the branch's edge
        // (op2.prev, op2) crosses the next two edges far from the copied vertex, and the insertion cut the triangle
        // (op2.prev, copy, op2) off the region. Exact areas from ExactRegionArea; "before" is master's engine.
        public static TheoryData<string, ClipType, FillRule, double[], double[], double> FarCopies => new TheoryData<string, ClipType, FillRule, double[], double[], double> {
            // 0.0709 before
            { "16-gons of #756", ClipType.Intersection, FillRule.NonZero, new double[] { 0, 4, -2, -1, 4, 2, -4, 2, -1, -1, -4, -2, 1, -1 },
              new double[] { 1, -4, -2, -3, -2, 0, 0, -2, -1, -1 }, 0.5708683473389355 },
            // 0.8333 before
            { "16-gons on -4..4", ClipType.Difference, FillRule.Negative, new double[] { -4, -1, 3, -1, -2, 0, 2, 0, -2, 4, 4, -2 },
              new double[] { 4, -1, -3, 3, -1, 4 }, 5.5 },
            // 20.2233 before
            { "16- and 9-gon on -4..4", ClipType.Xor, FillRule.Positive, new double[] { 4, 4, -3, 2, 2, -3 },
              new double[] { -4, -2, 2, 0, -1, 3, 4, 4, -3, 2, 0, -3, -2, 4 }, 22.223316871615733 },
            // 3.9421 before
            { "24-gons on -2..2", ClipType.Difference, FillRule.Positive, new double[] { 1, -2, -1, 1, 1, -1, -1, 0, -1, -2, 2, 2, -1, 1, 0, -2 },
              new double[] { 1, -2, -1, 1, -2, -2, 2, -1, -1, 0, 2, -1, -1, -1, -2, 2 }, 3.9637784815803387 },
            // found after the port of f774daf (6- and 16-gon on -4..4 shrunk): Xor EvenOdd, 16.9927 without the distance test
            { "after the rounding port", ClipType.Xor, FillRule.EvenOdd, new double[] { -4, 0, -4, -4, -2, 4, 2, 4, -1, -2, -4, -1, -4, 4, 2, 3 },
              new double[] { -3, 0, -2, 1, 3, 1 }, 17.206986346986344 },
            // 0.6470 before
            { "24- and 22-gon on -2..2", ClipType.Difference, FillRule.Negative, new double[] { 2, 1, 2, 0, -2, -2 },
              new double[] { 2, 0, -2, -2, 1, -1, -1, 0, -1, 2, 1, -1, -2, 1, 1, 0, 0, -1, -1, 2 }, 0.8612745098039215 },
        };

        [Theory]
        [MemberData(nameof(FarCopies))]
        public void FarCopy_IsSplitInstead(string name, ClipType op, FillRule rule, double[] a, double[] b, double exact)
        {
            var (error, slack, area) = RawEngine(op, rule, a, b);
            Assert.True(error <= slack, name + ": raw engine off by " + error + " grid units, more than the rounding " + slack);
            Assert.True(Math.Abs(area - exact) < 1e-5, name + ": " + area);
            // through the adapter (its own grid)
            var (e, s, r) = Clipper2DegenerateUnionTests.Check(op, rule, Ring(a), Ring(b), new Random(3));
            Assert.True(e <= s, name + ": adapter off by " + e + " grid units, more than the rounding " + s);
            Assert.True(Math.Abs(r.Area - exact) < 1e-6, name + ": adapter " + r.Area);
        }

        [Fact]
        public void CopyBetweenOneAndTwoUnitsFromTheEdge_IsSplit()
        {
            // pins the bound of one grid unit (the far copies above are about 2^20 units away): on a lattice of step 2 grid
            // units the copied vertex of this Union NonZero lies between one and 1.41 units from the edge's line; with the
            // bound at 1.41 units or more the insertion is taken and the area is 24.375 against an exact 25.65 (in lattice
            // units), with 1 it is 25.375 (re-derived after the port of the PolyClipping/Clipper2 rounding, f774daf)
            double[] a = { -4, 4, -3, -4, -2, -4 }, b = { 2, -2, -4, 3, 4, 2, -3, -4, -3, -1, -1, 3 };
            var (error, slack, area) = RawEngine(ClipType.Union, FillRule.NonZero, a, b, 1);
            Assert.True(error <= slack, "off by " + error + ", more than the rounding " + slack);
            Assert.Equal(25.375, area);
        }

        [Fact]
        public void OrientationGuard_OnAGridOfFewUnits()
        {
            // With the distance test the orientation guard no longer acts at the adapter's magnitudes, only where the
            // vertices are a few grid units apart (since the port of f774daf: 7 of 2.4 million random results on lattices of
            // step 1 to 3 grid units and at 2^20 and 2^50 changed, all within the rounding). This one, a lattice of step 3
            // grid units, Xor NonZero, exact 54.95 grid units squared: 55 with the guard, 52.5 without it
            double[] a = { 2, 2, -1, 1, -1, 2, 2, -2 }, b = { -2, 0, 1, 2, 0, -1, -1, 1, 2, 2, 2, -2, 2, 2, 0, -1 };
            double[] Times3(double[] c) => c.Select(x => 3 * x).ToArray();
            var (error, slack, area) = RawEngine(ClipType.Xor, FillRule.NonZero, Times3(a), Times3(b), 0);
            Assert.True(error <= slack, "off by " + error + ", more than the rounding " + slack);
            Assert.Equal(55, area);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names2D), MemberType = typeof(Similarity))]
        public void FarCopy_UnderSimilarity(string name)
        {
            // as RingFlippedByTheMicroFix_UnderSimilarity, for the first case above
            Similarity s = Similarity.ByName(name);
            Polygon2d a = Ring(0, 4, -2, -1, 4, 2, -4, 2, -1, -1, -4, -2, 1, -1), b = Ring(1, -4, -2, -3, -2, 0, 0, -2, -1, -1);
            Polygon2d Map(Polygon2d p) => new Polygon2d(p.Vertices.Select(s.Apply2d));
            var (_, _, plain) = Clipper2DegenerateUnionTests.Check(ClipType.Intersection, FillRule.NonZero, a, b);
            var (error, slack, mapped) = Clipper2DegenerateUnionTests.Check(ClipType.Intersection, FillRule.NonZero, Map(a), Map(b));
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
