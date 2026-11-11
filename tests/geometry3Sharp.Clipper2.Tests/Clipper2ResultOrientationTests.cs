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

        /// <summary>
        /// Clipper2's tree nests a filled triangle that touches a self-touching outer ring as a hole of it (found by the MeshFuzzPilot's
        /// libFuzzer run): the result lost its area. The flat paths of the same call (both of positive area) are the reference.
        /// </summary>
        [Fact]
        public void BooleanPaths_RegionTouchingASelfTouchingOuterRing_IsNotSubtracted()
        {
            const double c = 0.1562500000001421;
            Polygon2d a = Ring(c, c, 0.6562500000001421, c, 0.6562500000001421, 0.6562500000001421, c, 0.6562500000001421, c, c, 0.6562500000001421, c,
                0.6562500000001421, 0.6562500000001421, c, 0.6562500000001421, 0.4062500000001421, 0.9062500000001421, 0.9062500000001421, 0.4062500000001421,
                0.9062500000001421, 0.9062500000001421, 0.4062500000001421, 0.9062500000001421, 0.4062500000001421, 0.4062500000001421,
                0.9062500000001421, 0.4062500000001421, 3.709988353690383E-251, 8.487998269837145E-79, 0.4062500000001421, 0.7187500000001421);
            Polygon2d b = Ring(-0.29687499999992895, -0.29687499999992895, 0.703125000000071, -0.29687499999992895, -0.29687499999992895, 0.703125000000071);
            Clipper2Result r = Clipper2Polygons.BooleanPaths(ClipType.Union, new[] { a }, new[] { b });
            AssertOriented(r);
            Paths64 flat = Clipper.Union(new Paths64 { r.Frame.ToPath64(a) }, new Paths64 { r.Frame.ToPath64(b) }, FillRule.NonZero);
            double expected = flat.Sum(p => Clipper.Area(p)) / (r.Frame.Scale * r.Frame.Scale);
            Assert.Equal(expected, r.Area, 9);
        }

        /// <summary>
        /// A clockwise boundary inside a counterclockwise one is a hole, although Clipper2's tree reports it at the top level (found by
        /// the MeshFuzzPilot's libFuzzer run: the lobe was reoriented into an outer boundary and its area added). The signed area of the
        /// flat paths of the same call (outers positive, holes negative) is the reference.
        /// </summary>
        [Fact]
        public void BooleanPaths_ClockwiseBoundaryInsideACounterclockwiseOne_IsAHole()
        {
            Polygon2d a = Ring(
                -7.275957614183426E-12, 0, 0.8740516812193827, 0.8509521484375066, -6.366462912410498E-12, 9.094947017729282E-13,
                -1.0970779840135947E-11, -2.7853275241795927E-12, -1.0970779840135947E-11, -3.694822225952521E-12, -6.366462912410498E-12,
                0, -6.366462912410498E-12, 9.094947017729282E-13, -1.0970779840135947E-11, -2.7853275241795927E-12,
                -1.0516032489249483E-11, -3.240074875066057E-12, -5.9117155615240335E-12, 4.547473508864641E-13, -7.901235221652314E-12,
                -6.252776074688882E-13, -8.810729923425242E-12, -6.252776074688882E-13, -6.821210263296962E-12, 4.547473508864641E-13,
                -7.901235221652314E-12, 5.7411853049416095E-12, -5.9117155615240335E-12, 1.3642420526593924E-12, -6.821210263296962E-12,
                1.3642420526593924E-12);
            Polygon2d b = Ring(
                -0.12938040939579404, -0.15655517578309414, 0.468275840604206, -0.15655517578309414, -0.06688040939579404,
                -0.09405517578309414, 0.386244590604206, 0.4098510742169059, 0.12452584060420596, 0.09735107421690586, 0.18702584060420596,
                0.09735107421690586, -0.06688040939579404, -0.09405517578309414, 0.12452584060420596, 0.5075073242169059,
                0.503432090604206, 0.12469482421690586, 0.22218209060420596, 0.12860107421690586, 0.378432090604206, 0.3512573242169059,
                0.15577584060420596, 0.19110107421690586, 0.15577584060420596, 0.12860107421690586, 0.21827584060420596,
                0.12860107421690586, 0.21827584060420596, 0.19110107421690586, 0.15577584060420596, 0.19110107421690586);
            Clipper2Result r = Clipper2Polygons.BooleanPaths(ClipType.Union, new[] { a }, new[] { b });
            AssertOriented(r);
            Paths64 flat = Clipper.Union(new Paths64 { r.Frame.ToPath64(a) }, new Paths64 { r.Frame.ToPath64(b) }, FillRule.NonZero);
            Assert.Equal(flat.Sum(p => Clipper.Area(p)) / (r.Frame.Scale * r.Frame.Scale), r.Area, 9);
            Assert.Contains(r.Polygons, g => g.Holes.Count > 0);
        }

        static Polygon2d Square(double x0, double y0, double x1, double y1, bool ccw) =>
            ccw ? Ring(x0, y0, x1, y0, x1, y1, x0, y1) : Ring(x0, y0, x0, y1, x1, y1, x1, y0);

        [Fact]
        public void BooleanPaths_HoleTouchingItsOuterRingAtAVertex_IsAHole()
        {
            var r = Clipper2Polygons.BooleanPaths(ClipType.Union, new[] { Square(0, 0, 10, 10, true), Ring(5, 0, 3, 5, 7, 5) }, new Polygon2d[0]);
            AssertOriented(r);
            Assert.Single(r.Polygons);
            Assert.Single(r.Polygons[0].Holes);
            Assert.Equal(90, r.Area, 9);
        }

        [Fact]
        public void BooleanPaths_IslandInsideAHole_IsAPolygonOfItsOwn()
        {
            var r = Clipper2Polygons.BooleanPaths(ClipType.Union,
                new[] { Square(0, 0, 30, 30, true), Square(5, 5, 25, 25, false), Square(10, 10, 20, 20, true) }, new Polygon2d[0]);
            AssertOriented(r);
            Assert.Equal(2, r.Polygons.Count);
            Assert.Single(r.Polygons[0].Holes);
            Assert.Empty(r.Polygons[1].Holes);
            Assert.Equal(900 - 400 + 100, r.Area, 9);
        }

        [Fact]
        public void BooleanPaths_EvenOdd_NestedSameOrientationRings_AlternateHoleAndIsland()
        {
            var r = Clipper2Polygons.BooleanPaths(ClipType.Union,
                new[] { Square(0, 0, 30, 30, true), Square(5, 5, 25, 25, true), Square(10, 10, 20, 20, true) }, new Polygon2d[0],
                new Clipper2Options { FillRule = FillRule.EvenOdd });
            AssertOriented(r);
            Assert.Equal(2, r.Polygons.Count);
            Assert.Equal(900 - 400 + 100, r.Area, 9);
        }

        // ---- hand-built trees: each rule and clause of ClipperFrame.ToGeneralPolygons on its own (the frame maps the grid to itself)

        static Path64 Grid(params long[] xy)
        {
            var path = new Path64();
            for (int i = 0; i < xy.Length; i += 2) path.Add(new Point64(xy[i], xy[i + 1]));
            return path;
        }

        static Path64 Sq(long a, long b, bool ccw) => ccw ? Grid(a, a, b, a, b, b, a, b) : Grid(a, a, a, b, b, b, b, a);

        static double Net(System.Collections.Generic.List<GeneralPolygon2d> polygons) =>
            polygons.Sum(g => g.Outer.SignedArea + g.Holes.Sum(h => h.SignedArea));

        static System.Collections.Generic.List<GeneralPolygon2d> Convert(PolyTree64 tree) =>
            new ClipperFrame(new Vector2d(0, 0), 1).ToGeneralPolygons(tree);

        [Fact]
        public void Tree_NegativeTopLevelPathInsideAHoleHolder_DoesNotThrow_AndIsAnOuterRing()
        {
            // k clockwise at the top level, j counterclockwise nested in it as a hole, i clockwise at the top level inside j: i's holder j
            // is itself a hole of k, so i is an outer ring (a reviewer's synthetic tree: KeyNotFoundException before)
            var tree = new PolyTree64();
            PolyPath64 k = (PolyPath64)tree.AddChild(Sq(0, 3000, false));
            k.AddChild(Sq(500, 2500, true));
            tree.AddChild(Sq(1000, 2000, false));
            var polygons = Convert(tree);
            Assert.Equal(2, polygons.Count);
            Assert.All(polygons, g => Assert.True(g.Outer.SignedArea > 0));
            Assert.Equal(9e6 - 4e6 + 1e6, Net(polygons), 6);
        }

        [Fact]
        public void Tree_NegativeIslandInsideAHole_IsAFilledIsland()
        {
            // p counterclockwise, h its clockwise hole, i clockwise inside h as the tree's island: winding p + h = 0, then i = -1: filled
            var tree = new PolyTree64();
            PolyPath64 p = (PolyPath64)tree.AddChild(Sq(0, 4000, true));
            PolyPath64 h = (PolyPath64)p.AddChild(Sq(500, 3500, false));
            h.AddChild(Sq(1000, 3000, false));
            var polygons = Convert(tree);
            Assert.Equal(2, polygons.Count);
            Assert.Single(polygons[0].Holes);
            Assert.Empty(polygons[1].Holes);
            Assert.Equal(16e6 - 9e6 + 4e6, Net(polygons), 6);
        }

        [Fact]
        public void Tree_LobeSpanningANotchWithItsCornersOnTheBoundary_IsNotAHole()
        {
            // a U-shaped outer ring (area 700) and a clockwise triangle (area 100) in its notch, every vertex on the U's boundary
            var tree = new PolyTree64();
            tree.AddChild(Grid(0, 0, 30, 0, 30, 30, 20, 30, 20, 10, 10, 10, 10, 30, 0, 30));
            tree.AddChild(Grid(10, 30, 20, 30, 15, 10));
            var polygons = Convert(tree);
            Assert.Equal(2, polygons.Count);
            Assert.All(polygons, g => Assert.Empty(g.Holes));
            Assert.Equal(800, Net(polygons), 6);
        }

        [Fact]
        public void Tree_NegativeTopLevelPath_IsAHoleOfTheSmallestPositivePathThatHoldsIt()
        {
            // P1 with its hole H and the island P2 in it (the tree's nesting); a clockwise lobe N at the top level inside P2 (and P1)
            var tree = new PolyTree64();
            PolyPath64 p1 = (PolyPath64)tree.AddChild(Sq(0, 1000, true));
            PolyPath64 h = (PolyPath64)p1.AddChild(Sq(100, 900, false));
            h.AddChild(Sq(200, 800, true));
            tree.AddChild(Sq(300, 400, false));
            var polygons = Convert(tree);
            Assert.Equal(2, polygons.Count);
            Assert.Single(polygons[0].Holes);        // P1 comes first and keeps H only
            Assert.Single(polygons[1].Holes);        // P2 holds N
            Assert.Equal(1e6 - 640000 + 360000 - 10000, Net(polygons), 6);
        }

        [Fact]
        public void Tree_HoleOfTheSameOrientationAsItsParent_IsARegionOfItsOwn_AlsoWhenTheParentIsClockwise()
        {
            var tree = new PolyTree64();
            PolyPath64 p = (PolyPath64)tree.AddChild(Sq(0, 100, false));
            p.AddChild(Sq(20, 80, false));
            var polygons = Convert(tree);
            Assert.Equal(2, polygons.Count);
            Assert.All(polygons, g => Assert.Empty(g.Holes));
            Assert.Equal(10000 + 3600, Net(polygons), 6);
        }

        [Fact]
        public void Tree_LobeOnASlantedWallOfTheNotchWithOddCoordinates_IsNotAHole()
        {
            // every vertex on the holder's boundary; an edge midpoint of the lobe truncated to the grid would fall inside the holder
            var tree = new PolyTree64();
            tree.AddChild(Grid(0, 0, 30, 0, 30, 30, 21, 30, 20, 10, 11, 10, 10, 30, 0, 30));
            tree.AddChild(Grid(10, 30, 20, 10, 11, 10));
            var polygons = Convert(tree);
            Assert.Equal(2, polygons.Count);
            Assert.All(polygons, g => Assert.Empty(g.Holes));
        }

        [Fact]
        public void Tree_LobeWithAllCornersOnTheHolderButItsDiagonalThroughTheInterior_IsAHole()
        {
            // the clockwise triangle (0,0) (10,10) (10,0) on three corners of a square: only the midpoint of its diagonal is strictly inside
            var tree = new PolyTree64();
            tree.AddChild(Sq(0, 10, true));
            tree.AddChild(Grid(0, 0, 10, 10, 10, 0));
            var polygons = Convert(tree);
            Assert.Single(polygons);
            Assert.Single(polygons[0].Holes);
            Assert.Equal(100 - 50, Net(polygons), 6);
        }

        [Fact]
        public void Tree_LobeInTheDoublyWoundPartOfASelfOverlappingHolder_IsAHole()
        {
            // the holder runs twice round the square: the even-odd test calls its inside outside, the winding number is 2
            var tree = new PolyTree64();
            tree.AddChild(Grid(0, 0, 100, 0, 100, 100, 0, 100, 0, 0, 100, 0, 100, 100, 0, 100));
            tree.AddChild(Grid(40, 40, 40, 60, 60, 50));
            var polygons = Convert(tree);
            Assert.Single(polygons);
            Assert.Single(polygons[0].Holes);
        }
    }
}
