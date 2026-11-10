using System.Collections.Generic;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Fixed examples of the QualityMesh2 property campaign (QualityMesh2PropertyCampaign.CsCheck.cs; QA.md 2.4): every failing seed of a
    /// campaign becomes a case here, shrunk, and runs all properties of <see cref="QualityMesh2Properties.Check"/> on every target.
    /// </summary>
    public class QualityMesh2PropertyExamplesTests
    {
        static QualityMesh2PropertyCase Soup(int[] x, int[] y, int[][] segments, int denominator, int exponent, double angle, bool constrained, bool conforming)
            => new QualityMesh2PropertyCase { Family = "soup", X = x, Y = y, Rings = new int[0][], Holes = new bool[0], Segments = segments,
                Denominator = denominator, Exponent = exponent, SnapLog2 = null, Angle = angle, Constrained = constrained, Conforming = conforming };

        public static IEnumerable<object[]> InputAngleBelow60() => new[] {
            // finding 1 (campaign seed 0000000005vE1, shrunk): two segments of four points, the input angle 26.6 degrees at (2, 0) is
            // above the bound of 20; before the pair threshold became 60 degrees: 166 triangles, one below the bound, not excused
            new object[] { "four points, 20 degrees conforming", Soup(new[] { 0, 4, 2, 2 }, new[] { 4, 3, 3, 0 }, new[] { new[] { 0, 1 }, new[] { 2, 3 } }, 1, -1, 20, false, true) },
            // seed 000000000hnE1 (33 degrees) and 000000000dGE1 (30 degrees), shrunk by hand to their first violating form
            new object[] { "three segments, 33 degrees", Soup(new[] { 1, 3, 8, 7, 11, 1 }, new[] { 6, 3, 2, 8, 12, 0 }, new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4, 5 } }, 2, -2, 33, false, false) },
            new object[] { "five segments, 30 degrees", Soup(new[] { 10, 1, 12, 0, 4, 5, 0, 6, 2, 6 }, new[] { 3, 6, 6, 6, 5, 5, 0, 7, 1, 6 },
                new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4, 5 }, new[] { 6, 7 }, new[] { 8, 9 } }, 2, 2, 30, false, false) },
        };

        [Theory]
        [MemberData(nameof(InputAngleBelow60))]
        public void InputAngleBelow60_IsExcused(string name, QualityMesh2PropertyCase c)
            => Assert.True("" == QualityMesh2Properties.Check(c, new[] { -11, 13 }), name);

        static QualityMesh2PropertyCase Grid(string family, int[] x, int[] y, int[][] rings, int[][] segments, int denominator, int exponent, int snap, double angle, bool constrained, bool conforming)
            => new QualityMesh2PropertyCase { Family = family, X = x, Y = y, Rings = rings, Holes = new bool[rings.Length], Segments = segments,
                Denominator = denominator, Exponent = exponent, SnapLog2 = snap, Angle = angle, Constrained = constrained, Conforming = conforming };

        public static IEnumerable<object[]> GridRerouting() => new[] {
            // SegmentMissing, fixed in the oracle (the iterated image, #526): conforming mode on a sliver polygon at pixel 1 lattice unit; the chain of (0,0)-(95,-3) is not
            // found by the oracle (the constrained mode of the same input is valid, and so is pixel 2)
            new object[] { "wedge, conforming, chain of a ring edge", Grid("wedge", new[] { 0, 95, 34, 250, 180 }, new[] { 0, -3, -1, 0, 2 }, new[] { new[] { 0, 1, 2, 3, 4 } }, new int[0][], 2, 1, 0, 30, false, true) },
            // ChainSkipsHotPixel and VertexUnused on a hole ring's spike tip, fixed in the oracle (the lobe, T5)
            new object[] { "polygon with a hole, input vertex unused", new QualityMesh2PropertyCase { Family = "polygon", X = new[] { 19, -19, -7, -12, -11, -12, -12, -3, -2 }, Y = new[] { -14, 9, -28, -2, -4, -3, -4, -14, -12 },
                Rings = new[] { new[] { 0, 1, 2 }, new[] { 3, 4, 5, 6, 7, 8 } }, Holes = new[] { false, true }, Segments = new int[0][], Denominator = 0, Exponent = 0, SnapLog2 = 0, Angle = 20, Constrained = false, Conforming = true } },
            // TrianglesOnDegenerate, fixed by #512: three collinear input points (2,4), (3,3), (0,6) at pixel 2; snapping used to give a triangle
            new object[] { "zero-area input is empty after snapping", Grid("soup", new[] { 2, 3, 0 }, new[] { 4, 3, 6 }, new int[0][], new[] { new[] { 0, 1 }, new[] { 0, 2 } }, 2, -6, 1, 0, true, false) },
            // finding 2 (grid), fixed by oracle rule O-G (#499): the oracle's AreaMismatch on inputs whose hull or ring edge is rerouted
            // through a hot pixel; M2 (no refinement) showed the same, so it was the oracle's tolerance, not the mesher
            new object[] { "soup: the hull edge (1,4)-(5,5) passes the pixel of the input point (3,4)", Grid("soup", new[] { 5, 3, 6, 1 }, new[] { 5, 4, 4, 4 }, new int[0][], new[] { new[] { 0, 1 }, new[] { 2, 3 } }, 1, -2, 0, 30, false, true) },
            new object[] { "wedge 252/253, pixel 1/2 unit", Grid("wedge", new[] { 0, 252, 253, 64 }, new[] { 0, -4, -4, 2 }, new[] { new[] { 0, 1, 2, 3 } }, new int[0][], 2, -4, -1, 0, true, false) },
            new object[] { "wedge, pixel 1/4 unit, 30 degrees", Grid("wedge", new[] { 0, 111, 119, 237 }, new[] { 0, -1, 2, 4 }, new[] { new[] { 0, 1, 2, 3 } }, new int[0][], 1, 2, -2, 30, false, true) },
            // AngleBelowBound, fixed in the oracle (T5): five segments at pixel 1/8 lattice unit, bound 15; the hull edge (0,8)-(16,40) px passes the input
            // point (8,24), and the apex of its 4.8 degree angle with the first segment lies on the other piece. The oracle took the pieces
            // as separate sources on the grid; the mesher takes the whole hull edge (OriginOf)
            new object[] { "soup: a hull edge through an input point, the small angle's apex on its other piece", Grid("soup", new[] { 0, 2, 6, 4, 6, 1, 4, 6, 0, 4 }, new[] { 0, 5, 4, 4, 2, 3, 3, 3, 1, 0 }, new int[0][], new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4, 5 }, new[] { 6, 7 }, new[] { 8, 9 } }, 2, -1, -3, 15, false, false) },
            // VertexUnused / SegmentMissing, fixed in the oracle (T5, owner decision B 2026-11-10): the ring (0,0), (162,-3), (84,0), (279,3), (103,3) at
            // pixel 4 units has a spike 0 -> 1 -> 2 about 20 pixels long and under a pixel wide; the input's rounding collapses it, the mesh has no
            // triangle at its tip, and the full image (with refinement vertices as hot pixels) had a lobe of 5.5 pixels squared there. The reference is the rounded input
            new object[] { "wedge: a spike the input's rounding collapses, its tip without a triangle", Grid("wedge", new[] { 0, 162, 84, 279, 103 }, new[] { 0, -3, 0, 3, 3 }, new[] { new[] { 0, 1, 2, 3, 4 } }, new int[0][], 1, -1, 2, 15, true, false) },
            new object[] { "polygon with a ring edge across pixels", Grid("polygon", new[] { 26, 20, 6, 1, -26, -35 }, new[] { -8, 35, 18, 26, 17, 21 }, new[] { new[] { 0, 1, 2, 3, 4, 5 } }, new int[0][], 2, 2, -1, 20, true, false) },
        };

        [Theory]
        [MemberData(nameof(GridRerouting))]
        public void GridRerouting_IsWithinTheTube(string name, QualityMesh2PropertyCase c)
            => Assert.True("" == QualityMesh2Properties.Check(c, new[] { -11, 13 }), name);

        [Theory]
        [MemberData(nameof(GridRerouting))]
        public void GridRerouting_IsNoOpenClass(string name, QualityMesh2PropertyCase c)
        {
            QualityMesh2Properties.Check(c, new[] { -11 });
            Assert.True(QualityMesh2Properties.LastOpenClass == null, name);
        }
    }
}
