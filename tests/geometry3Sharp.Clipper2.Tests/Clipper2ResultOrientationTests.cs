using System.Linq;
using Clipper2Lib;
using g3;
using g3.Clipper2;
using Xunit;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// Results are documented as outer rings counterclockwise and holes clockwise. Clipper2 reports a lobe of negative winding (NonZero)
    /// as a clockwise path even where it is an outer boundary; found by the MeshFuzzPilot (a self-intersecting raw ring).
    /// </summary>
    public class Clipper2ResultOrientationTests
    {
        static Polygon2d Ring(params double[] xy)
        {
            var v = new System.Collections.Generic.List<Vector2d>();
            for (int i = 0; i < xy.Length; i += 2) v.Add(new Vector2d(xy[i], xy[i + 1]));
            return new Polygon2d(v);
        }

        static void AssertOriented(Clipper2Result r)
        {
            Assert.True(r.Ok, r.Error);
            Assert.NotEmpty(r.Polygons);
            foreach (GeneralPolygon2d g in r.Polygons) {
                Assert.True(g.Outer.SignedArea > 0, "outer ring of area " + g.Outer.SignedArea);
                foreach (var h in g.Holes) Assert.True(h.SignedArea < 0, "hole of area " + h.SignedArea);
            }
        }

        // a ring found by the fuzz pilot: self-intersecting, with a small lobe of negative winding
        static readonly Polygon2d Tangle = Ring(-1.04296875, -1.0546875, -0.65625, -0.56640625, -0.65625, -0.50390625, -0.71875, -0.50390625,
            -1.1640625, -0.77734375, -0.796875, -0.25, -0.65625, -0.50390625, -0.2890625, -0.546875, -0.61328125, -0.94140625, -0.625, -0.53515625,
            -0.71875, -0.56640625, -0.6875, -0.47265625, -0.6875, -0.53515625, -0.625, -0.53515625, -0.625, -0.47265625, -0.6875, -0.47265625);

        // the other ring of the pilot's input: a point, repeated
        static readonly Polygon2d Speck = Ring(-2.4185786671844023E-85, -2.2187038711453535E-199, -0.953125, -0.8046875, -0.953125, -0.8046875,
            -0.953125, -0.8046875, -0.953125, -0.8046875, -0.953125, -0.8046875);

        [Fact]
        public void BooleanPaths_SelfIntersectingRing_ResultsAreOriented()
        {
            AssertOriented(Clipper2Polygons.BooleanPaths(ClipType.Union, new[] { Speck }, new[] { Tangle }));
        }

        [Fact]
        public void OffsetAndSimplify_OfTheTangledResult_AreOriented()
        {
            var union = Clipper2Polygons.BooleanPaths(ClipType.Union, new[] { Speck }, new[] { Tangle });
            AssertOriented(union);
            AssertOriented(Clipper2Polygons.Offset(union.Polygons, 0.02));
            AssertOriented(Clipper2Polygons.Offset(union.Polygons, -0.01));
            AssertOriented(Clipper2Polygons.Simplify(union.Polygons, 0.01));
        }

        [Fact]
        public void BooleanPaths_ClockwiseLobeNextToCounterclockwiseOne_BothOuterCounterclockwise()
        {
            // a figure of eight: the lobes have opposite winding, both are filled by NonZero
            var r = Clipper2Polygons.BooleanPaths(ClipType.Union, new[] { Ring(0, 0, 4, 4, 4, 0, 0, 4) }, new Polygon2d[0]);
            AssertOriented(r);
            Assert.Equal(2, r.Polygons.Count);
            Assert.Equal(8, r.Area, 9);
        }
    }
}
