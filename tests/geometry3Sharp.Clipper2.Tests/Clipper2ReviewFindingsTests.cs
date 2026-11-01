using System;
using System.Linq;
using Clipper2Lib;
using g3;
using g3.Clipper2;
using Xunit;
using static geometry3Sharp.Clipper2.Tests.Oracle;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary> Regressions for the findings of the independent review of PR #243 (D1-D6) and the mutation check. </summary>
    public class Clipper2ReviewFindingsTests
    {
        [Fact]
        public void PointPolyLine_SquareAndButtEnds()
        {
            // D1: Clipper2 2.0 computes square and butt ends of a point with an int half size, which overflowed
            foreach (var line in new[] { new PolyLine2d(new[] { new Vector2d(5, 5) }), new PolyLine2d(new[] { new Vector2d(5, 5), new Vector2d(5, 5) }) }) {
                Clipper2Result square = Clipper2Polygons.OffsetPolyLines(new[] { line }, 1, new Clipper2Options { EndType = EndType.Square });
                CheckWellFormed(square);
                Assert.Single(square.Polygons);
                Assert.Equal(4, square.Area, 9);
                Assert.Equal(new AxisAlignedBox2d(new Vector2d(4, 4), new Vector2d(6, 6)), square.Polygons[0].Bounds);
                Clipper2Result butt = Clipper2Polygons.OffsetPolyLines(new[] { line }, 1, new Clipper2Options { EndType = EndType.Butt });
                Assert.True(butt.Ok);
                Assert.Empty(butt.Polygons);
            }
            // with other lines in the same call
            var both = new[] { new PolyLine2d(new[] { new Vector2d(0, 0) }), new PolyLine2d(new[] { new Vector2d(10, 0), new Vector2d(20, 0) }) };
            Assert.Equal(4 + 12 * 2, Clipper2Polygons.OffsetPolyLines(both, 1, new Clipper2Options { EndType = EndType.Square }).Area, 9);
        }

        [Fact]
        public void NegativeOffset_OfZeroAreaRings_IsEmpty_AndBowtieLobesShrink()
        {
            // D2: Clipper2 turns the delta positive when every ring of a group has zero area
            var flat = new[] { new GeneralPolygon2d(Ring(0, 0, 1, 0, 2, 0)) };
            foreach (JoinType join in new[] { JoinType.Round, JoinType.Miter }) {
                Clipper2Result r = Clipper2Polygons.Offset(flat, -0.1, new Clipper2Options { JoinType = join });
                Assert.True(r.Ok);
                Assert.Empty(r.Polygons);
            }
            // a bowtie outer ring (net area 0): both lobes are inside (nonzero) and both shrink
            var bowtie = new[] { new GeneralPolygon2d(Ring(0, 0, 2, 2, 2, 0, 0, 2)) };
            Assert.Equal(2, Clipper2Polygons.Union(bowtie).Area, 12);
            Clipper2Result shrunk = Clipper2Polygons.Offset(bowtie, -0.1, new Clipper2Options { JoinType = JoinType.Miter });
            CheckWellFormed(shrunk);
            Assert.Equal(2, shrunk.Polygons.Count);
            double inradius = 1 / (1 + Math.Sqrt(2));   // of each lobe (area 1, half perimeter 1 + sqrt 2)
            Assert.Equal(2 * Math.Pow(1 - 0.1 / inradius, 2), shrunk.Area, 9);
        }

        [Fact]
        public void OverlappingHoles_AndHolesOutside_Subtract()
        {
            // D3: a region is outer minus holes, whatever the holes do
            var g = new GeneralPolygon2d(Rect(0, 0, 10, 10).Outer);
            g.AddHole(Rect(1, 1, 5, 5).Outer, false, false);
            g.AddHole(Rect(3, 3, 7, 7).Outer, false, false);
            var outside = new GeneralPolygon2d(Rect(0, 0, 1, 1).Outer);
            outside.AddHole(Rect(5, 5, 6, 6).Outer, false, false);
            Assert.Equal(100 - 28, Clipper2Polygons.Union(new[] { g }).Area, 9);
            Assert.Equal(1, Clipper2Polygons.Union(new[] { outside }).Area, 12);
            // the same region in every operation, also offsets by 0 and by a tiny amount, and Simplify
            Assert.Equal(72, Clipper2Polygons.Offset(new[] { g }, 0).Area, 9);
            Assert.Equal(72, Clipper2Polygons.Offset(new[] { g }, 1e-9, new Clipper2Options { JoinType = JoinType.Miter }).Area, 6);
            Assert.Equal(72, Clipper2Polygons.Simplify(new[] { g }, 0).Area, 9);
            Assert.Equal(72, Clipper2Polygons.Intersection(new[] { g }, new[] { Rect(-1, -1, 11, 11) }).Area, 9);
            Assert.Equal(1, Clipper2Polygons.Offset(new[] { outside }, 0).Area, 12);
        }

        [Fact]
        public void InvalidExplicitFrame_IsReported()
        {
            // D4: default(ClipperFrame) has scale 0; a subnormal scale has no finite inverse
            var sq = new[] { Rect(0, 0, 1, 1) };
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.Union(sq, null, new Clipper2Options { Frame = default(ClipperFrame) }).Status);
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.Union(sq, null, new Clipper2Options { Frame = new ClipperFrame(Vector2d.Zero, 1e-310) }).Status);
        }

        [Fact]
        public void HugeMiterLimit_IsClamped_AndKeepsThePrecision()
        {
            // the margin of the grid grows with MiterLimit: a huge limit made the grid as coarse as the input
            var sq = new[] { Rect(0, 0, 1, 1) };
            Clipper2Result huge = Clipper2Polygons.Offset(sq, 1, new Clipper2Options { JoinType = JoinType.Miter, MiterLimit = 1e300 });
            Clipper2Result capped = Clipper2Polygons.Offset(sq, 1, new Clipper2Options { JoinType = JoinType.Miter, MiterLimit = Clipper2Options.MaxMiterLimit });
            CheckWellFormed(huge);
            Assert.Equal(9, huge.Area, 9);
            Assert.Equal(capped.Frame.Scale, huge.Frame.Scale);
        }

        [Fact]
        public void ResultsNearTheDoubleRange_AreFiniteOrReported()
        {
            // D5: origin plus extent beyond the double range gave infinite vertices with Success
            Clipper2Result r = Clipper2Polygons.Offset(new[] { Rect(1.7e308, 0, 1.75e308, 1) }, 1e307);
            Assert.Equal(Clipper2Status.InvalidInput, r.Status);
            Clipper2Result ok = Clipper2Polygons.Offset(new[] { Rect(1e307, 0, 1.1e307, 1) }, 1e306);
            CheckWellFormed(ok);
        }

        [Fact]
        public void LargeArcTolerance_IsClamped()
        {
            // D6: ArcTolerance >= |delta| made Clipper2 fall back to pi sqrt(r) steps on our grid (millions)
            var point = new[] { new PolyLine2d(new[] { new Vector2d(0, 0) }) };
            Clipper2Result r = Clipper2Polygons.OffsetPolyLines(point, 1, new Clipper2Options { ArcTolerance = 1.5 });
            CheckWellFormed(r);
            Assert.InRange(r.Polygons[0].Outer.VertexCount, 3, 16);
            Clipper2Result sq = Clipper2Polygons.Offset(new[] { Rect(0, 0, 1, 1) }, 1, new Clipper2Options { ArcTolerance = 5 });
            CheckWellFormed(sq);
            Assert.InRange(sq.Polygons[0].Outer.VertexCount, 4, 32);
        }

        [Fact]
        public void FarClipPolygon_IsInsideTheGrid()
        {
            // the grid covers subject and clip: a clip far from a tiny subject must not leave the frame
            var tiny = new[] { Rect(0, 0, 1e-3, 1e-3) };
            var far = new[] { Rect(1e6, 1e6, 1e6 + 1, 1e6 + 1) };
            Clipper2Result r = Clipper2Polygons.Difference(tiny, far);
            CheckWellFormed(r);
            Assert.True(Math.Abs(r.Area - 1e-6) <= 4e-3 * r.Frame.Resolution);   // the grid spans both inputs
            Assert.Equal(2, Clipper2Polygons.Union(tiny, far).Polygons.Count);
            var line = new[] { new PolyLine2d(new[] { new Vector2d(0, 0), new Vector2d(1e-3, 0) }) };
            Assert.True(Clipper2Polygons.ClipPolyLines(ClipType.Difference, line, far).Ok);
        }
    }
}
