using System;
using System.Linq;
using Xunit;
using V = g3.Clipper2.Vendored;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// Fixes of the C++ Clipper2 (PolyClipping/Clipper2, BSL-1.0, after the C# 2.0.0 sources were archived) ported to the
    /// vendored C# sources; each test is the upstream test of that commit, translated (Clipper2Lib/README.md lists them).
    /// </summary>
    public class Clipper2UpstreamFixesTests
    {
        static V.Path64 P(params long[] c) => new V.Path64(Enumerable.Range(0, c.Length / 2).Select(i => new V.Point64(c[2 * i], c[2 * i + 1])));

        static V.Paths64 Run(V.ClipType op, V.FillRule rule, V.Paths64 subject, V.Paths64 clip)
        {
            var c = new V.Clipper64();
            c.AddSubject(subject);
            if (clip != null) c.AddClip(clip);
            var result = new V.Paths64();
            Assert.True(c.Execute(op, rule, result));
            return result;
        }

        static double Area(V.Paths64 paths) => paths.Sum(p => V.Clipper.Area(p));

        [Fact]
        public void DifferenceRoundsIntersections()
        {
            // f774daf (PolyClipping/Clipper2#13): the exact difference has an area of 3.27; truncating its intersection points
            // reversed the only contour to an area of -3.5
            var solution = Run(V.ClipType.Difference, V.FillRule.NonZero, new V.Paths64 { P(0, 10, 5, 2, 7, 1) }, new V.Paths64 { P(3, 6, 6, 1, 11, 11) });
            Assert.Single(solution);
            Assert.True(V.Clipper.IsPositive(solution[0]));
            Assert.InRange(Area(solution), 2.27, 4.27);
        }

        [Fact]
        public void RectClipOfASharedEdge_RoundsTheSameWay()
        {
            // f774daf: the shared edge crosses the top of the rect at exactly x = 7.5, and the two triangles give it in opposite
            // directions, so both must round that crossing to the same point
            var rect = new V.Rect64(-10, 115, 30, 150);
            var adjacent = new V.Paths64 { P(0, 100, 11, 122, 0, 144), P(11, 122, 0, 100, 11, 78) };
            var separate = V.Clipper.RectClip(rect, adjacent);
            var merged = V.Clipper.RectClip(rect, V.Clipper.Union(adjacent, V.FillRule.NonZero));
            Assert.Equal(Area(merged), Area(separate));
        }

        [Fact]
        public void XorOfDivergingSharedEdges_IsPositiveAndSymmetric()
        {
            // ec5f2d4 (PolyClipping/Clipper2#13): the paths share edges that diverge at 33889,18931 by less than half a unit for
            // several scanbeams; the region between them came out reversed
            long[] common = {
                97542,3449, 97548,169199, 97559,169465, 97626,170263, 97663,170547, 97747,170984, 97915,171671,
                97999,171956, 98156,172387, 98420,173010, 98536,173254, 98766,173674, 99129,174259, 99290,174494, 99585,174873,
                100292,175675, 133417,208800, 133586,209021, 133442,209203, 129002,213655, 128782,213824, 128600,213682,
                7772,92854, 7604,92634, 7752,92446, 13507,86686, 13738,86509, 14008,86472, 26760,86465, 27036,86453,
                27811,86385, 28080,86349, 28535,86262, 29186,86101, 29446,86025, 29917,85856, 30530,85596, 30783,85475,
                31229,85228, 31842,84841, 32182,84600, 32810,84072, 33032,83862, 33342,83530, 33958,82797, 34128,82575,
                34408,82159, 34675,81711, 34805,81471, 35000,81054, 35259,80418, 35352,80157, 35483,79709, 35634,79071,
                35688,78786, 35750,78289, 35820,77190, 35816,24593, 35807,24353, 35739,23457, 35684,23032, 35633,22770,
                35468,22076, 35370,21745, 35176,21200, 35042,20881, 34747,20272, 34566,19941, 34037,19135, 33889,18931 };
            var subject = P(new long[] { 97513, 3213 }.Concat(common).Concat(new long[] { 33621, 18600, 32723, 17588, 32698, 3608, 32710, 3376, 32737, 3177, 97246, 3177 }).ToArray());
            var clip = P(new long[] { 97512, 3212 }.Concat(common).Concat(new long[] { 33622, 18601, 33118, 18033, 33040, 17609, 33017, 3570, 33032, 3337, 33058, 3177, 97246, 3177 }).ToArray());
            var solution = Run(V.ClipType.Xor, V.FillRule.EvenOdd, new V.Paths64 { subject }, new V.Paths64 { clip });
            var swapped = Run(V.ClipType.Xor, V.FillRule.EvenOdd, new V.Paths64 { clip }, new V.Paths64 { subject });
            Assert.NotEmpty(solution);
            Assert.NotEmpty(swapped);
            Assert.All(solution, p => Assert.True(V.Clipper.IsPositive(p)));
            Assert.All(swapped, p => Assert.True(V.Clipper.IsPositive(p)));
            Assert.Equal(Area(solution), Area(swapped));
        }

        [Fact]
        public void EdgesLeavingASharedVertex_AreOrderedToTheRight()
        {
            // ec5f2d4, the loop over the right neighbours (the upstream test above needs only the left one): Xor Positive on
            // the unit grid, shrunk from random 3- to 16-gons; exact 2.78, 1 with the order restored, empty without it
            var r = Run(V.ClipType.Xor, V.FillRule.Positive, Lattice(1, 2, 0, 0, -2, 0, 2, 2, -1, 2, 0, 1, 0, 1, -2), Lattice(1, 0, 2, -1, -1, 1, 1));
            Assert.Equal(1, Area(r));
        }

        [Fact]
        public void UnionKeepsTheIntersectionInTheScanbeam()
        {
            // the upstream test of 4a3bf64 (the clip edge leaving -28785,-61 starts almost on the subject's flat edge, so
            // their intersection is computed just below the scanbeam, and clamping it used to leave it 6 units before that
            // vertex). 4a3bf64 itself is not ported: with the rounding of f774daf the C# engine does not reach that clamp
            // on this input or on 3.2 million random results tried (Clipper2Lib/README.md); the test passes with the other
            // fixes (it fails on master) and stays as a regression test
            var solution = Run(V.ClipType.Union, V.FillRule.EvenOdd, new V.Paths64 { P(-69425, 73, -33569, -35, -24000, -87) },
                new V.Paths64 { P(-3862, 183, -33569, -35, -28785, -61, -26393, -75, -24000, -87) });
            Assert.Single(solution);
            Assert.Equal(4, solution[0].Count);
            Assert.Equal(2230933, Area(solution));
        }

        [Fact]
        public void PolyTreeOfCyclicSplits_Terminates()
        {
            // f97d65d: emptied OutRecs whose splits form a cycle recursed forever in CheckSplitOwner upstream (C++); the C#
            // sources do not reach the cycle on this input, the guard is ported as is
            var c = new V.Clipper64 { PreserveCollinear = false };
            c.AddClip(new V.Paths64 { P(3, 0, 3, 5, 0, 3, 5, 3, 0, 5, 5, 3, 0, 3, 0, 0, 0, 3, 5, 3, 0, 0, 3, 3, 2, 3) });
            Assert.True(c.Execute(V.ClipType.Xor, V.FillRule.EvenOdd, new V.PolyTree64(), new V.Paths64()));
        }

        static V.Paths64 Lattice(long step, params long[] c) => new V.Paths64 { P(c.Select(x => x * step).ToArray()) };

        [Fact]
        public void IntersectionsAtOnePoint_AreSortedStably()
        {
            // f774daf: the order of intersections at the same point changes the output; List.Sort (introsort) left it to the
            // implementation. Two Intersection NonZero cases on a lattice of step 2 grid units (shrunk from random 3- to
            // 16-gons), where the unstable order gave an empty result: exact 0.44 and 1.45 lattice units squared
            var r1 = Run(V.ClipType.Intersection, V.FillRule.NonZero, Lattice(2, -2, 2, 0, 2, -2, -1, 0, 0, -1, 1, -1, 0, 0, 0),
                Lattice(2, 1, 0, -2, 1, -2, 2, -1, 2, -2, 0, 1, 2, -2, -2, -1, -2, 0, 2));
            Assert.Equal(-0.125 * 4, Area(r1));
            var r2 = Run(V.ClipType.Intersection, V.FillRule.NonZero, Lattice(2, 1, 2, -2, 2, 2, -2, -1, 2, 1, -1, -2, -2, 2, 1, 2, 2, 1, -1, 2, 1, -2, -1),
                Lattice(2, -2, -1, -1, 1, -2, 1, 2, -1));
            Assert.Equal(0.25 * 4, Area(r2));
        }

        [Fact]
        public void UnionOnTheUnitGrid_RoundsIntersections()
        {
            // f774daf on the unit grid (shrunk from random 3- to 16-gons): Union Positive, exact 5.60; truncating the
            // intersection points gave an empty result, rounding gives 4
            var r = Run(V.ClipType.Union, V.FillRule.Positive, Lattice(1, -3, -1, 3, 0, -1, 0), Lattice(1, 0, -4, 1, 0, 0, 4));
            Assert.Equal(4, Area(r));
        }

        [Fact]
        public void IntersectionPoint_DoesNotDependOnTheSegmentsDirection()
        {
            // f774daf: both segments are evaluated from their bottom end, so a shared edge rounds to the same point whichever
            // way round it is given (here the crossing lies at x = 7.5 on the line y = 122 + ...: a tie)
            var a = new V.Point64(0, 100); var b = new V.Point64(11, 122); var c = new V.Point64(-10, 115); var d = new V.Point64(30, 115);
            foreach (var (s1, s2) in new[] { (a, b), (b, a) })
                foreach (var (t1, t2) in new[] { (c, d), (d, c) }) {
                    Assert.True(V.InternalClipper.GetLineIntersectPt(s1, s2, t1, t2, out V.Point64 ip));
                    Assert.True(V.InternalClipper.GetLineIntersectPt(a, b, c, d, out V.Point64 reference));
                    Assert.Equal(reference, ip);
                }
            // a tie is rounded to even, as C++'s nearbyint: (0,0)-(13,2) crosses y = 1 at x = 6.5, which gives 6 (away from
            // zero it would be 7)
            Assert.True(V.InternalClipper.GetLineIntersectPt(new V.Point64(0, 0), new V.Point64(13, 2), new V.Point64(-10, 1), new V.Point64(20, 1), out V.Point64 tie));
            Assert.Equal(new V.Point64(6, 1), tie);
            // the order of the second segment's ends matters in doubles at the adapter's magnitudes (found by a search)
            var e1 = new V.Point64(-732766039893, 267966786172); var e2 = new V.Point64(597744236310, 270007010941);
            var f1 = new V.Point64(233684498028, -105137564208); var f2 = new V.Point64(20054427145, -354628352677);
            Assert.True(V.InternalClipper.GetLineIntersectPt(e1, e2, f1, f2, out V.Point64 ipA));
            Assert.True(V.InternalClipper.GetLineIntersectPt(e1, e2, f2, f1, out V.Point64 ipB));
            Assert.Equal(ipA, ipB);
            // and over random segments of a lattice: the same point for every order of ends and of the two segments' ends
            var rand = new Random(7);
            int checkedCount = 0;
            for (int n = 0; n < 20000; ++n) {
                V.Point64 R() => new V.Point64(rand.Next(-50, 51) * 3, rand.Next(-50, 51) * 7);
                var (p1, p2, q1, q2) = (R(), R(), R(), R());
                if (!V.InternalClipper.GetLineIntersectPt(p1, p2, q1, q2, out V.Point64 ip0)) continue;
                Assert.True(V.InternalClipper.GetLineIntersectPt(p2, p1, q1, q2, out V.Point64 ip1));
                Assert.True(V.InternalClipper.GetLineIntersectPt(p1, p2, q2, q1, out V.Point64 ip2));
                Assert.Equal(ip0, ip1);
                Assert.Equal(ip0, ip2);
                ++checkedCount;
            }
            Assert.True(checkedCount > 10000);
        }
    }
}
