using System.Collections.Generic;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Fixed examples of the QualityMesh2 property campaign (QualityMesh2PropertyTests.CsCheck.cs; QA.md 2.4): every failing seed of a
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
            => Assert.Equal("", QualityMesh2Properties.Check(c, new[] { -11, 13 }));
    }
}
