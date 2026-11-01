using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using g3;
using g3.Clipper2;
using Xunit;
using static geometry3Sharp.Clipper2.Tests.Oracle;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary> Offsets of polygons and polylines, checked by area formulas and by distances at sample points. </summary>
    public class Clipper2OffsetTests
    {
        [Fact]
        public void Square_Inflate_AreasByJoinType()
        {
            var square = new[] { Rect(0, 0, 4, 4) };
            double d = 1;
            Clipper2Result miter = Clipper2Polygons.Offset(square, d, new Clipper2Options { JoinType = JoinType.Miter });
            Clipper2Result round = Clipper2Polygons.Offset(square, d);
            Clipper2Result bevel = Clipper2Polygons.Offset(square, d, new Clipper2Options { JoinType = JoinType.Bevel });
            CheckWellFormed(miter); CheckWellFormed(round); CheckWellFormed(bevel);
            Assert.Equal(36, miter.Area, 9);                                   // (4 + 2d)^2
            Assert.Equal(16 + 16 * d + 2 * d * d, bevel.Area, 9);              // corners cut at the edge normals
            // round: 16 + perimeter d + pi d^2, the arcs as polygons within ArcTolerance (d / 1000) inside
            double exact = 16 + 16 * d + Math.PI * d * d;
            Assert.InRange(round.Area, exact - 2 * Math.PI * d * 1e-3 * d - 1e-9, exact + 1e-9);
            // about 70 segments per full circle by default; finer with a smaller ArcTolerance
            int n = round.Polygons[0].Outer.VertexCount;
            Assert.InRange(n, 4 * 10, 4 * 30);
            Clipper2Result fine = Clipper2Polygons.Offset(square, d, new Clipper2Options { ArcTolerance = 1e-6 });
            Assert.True(fine.Polygons[0].Outer.VertexCount > 4 * n);
            Assert.True(Math.Abs(fine.Area - exact) < Math.Abs(round.Area - exact));
        }

        [Fact]
        public void Miter_LimitSquaresOffSharpCorners()
        {
            // a thin triangle with a sharp tip: the miter would reach far beyond MiterLimit * d
            var tri = new[] { new GeneralPolygon2d(Ring(0, 0, 10, 0, 10, 1)) };
            double d = 0.5;
            foreach (double limit in new[] { 1.0, 2.0, 5.0 }) {
                Clipper2Result r = Clipper2Polygons.Offset(tri, d, new Clipper2Options { JoinType = JoinType.Miter, MiterLimit = limit });
                CheckWellFormed(r);
                double reach = r.Polygons[0].Outer.Vertices.Max(v => RingDistance(tri[0].Outer.Vertices, v));
                // squared corners reach up to sqrt(2) d from the input
                Assert.True(reach <= Math.Max(limit, Math.Sqrt(2)) * d + 1e-9, limit + ": " + reach);
                Assert.True(reach >= d - 1e-9);
            }
        }

        [Theory]
        [InlineData(1, 0.7)]
        [InlineData(2, -0.4)]
        [InlineData(3, 1.5)]
        public void RoundOffset_MatchesDistanceAtSamples(int seed, double d)
        {
            var rand = new Random(seed);
            // disjoint inputs: the oracle measures the distance to their boundaries, which is the boundary of their union
            var input = new[] { StarWithHole(rand, new Vector2d(0, 0), 21, 6), new GeneralPolygon2d(Star(rand, new Vector2d(11, 1), 15, 2, 4)) };
            var options = new Clipper2Options { ArcTolerance = 1e-4 };
            Clipper2Result r = Clipper2Polygons.Offset(input, d, options);
            var box = new AxisAlignedBox2d(new Vector2d(-9, -9), new Vector2d(17, 9));
            Vector2d[] samples = Samples(rand, box, 4000).ToArray();
            CheckWellFormed(r, samples);
            int tested = 0;
            foreach (Vector2d p in samples) {
                double dist = BoundaryDistance(input, p);
                bool inside = input.Any(g => Winding(g, p) > 0);
                double signed = inside ? -dist : dist;   // signed distance to the input region
                if (Math.Abs(signed - d) < 2e-4) continue;   // arc tolerance and grid
                Assert.True((signed < d) == (ResultCount(r.Polygons, p) == 1), "offset " + d + " wrong at " + p + " (signed distance " + signed + ")");
                ++tested;
            }
            Assert.True(tested > 3900);
        }

        [Fact]
        public void Deflate_ShrinksOuterAndGrowsHole()
        {
            var g = new GeneralPolygon2d(Rect(0, 0, 10, 10).Outer);
            Polygon2d hole = Rect(4, 4, 6, 6).Outer; hole.Reverse();
            g.AddHole(hole);
            Clipper2Result r = Clipper2Polygons.Offset(new[] { g }, -1, new Clipper2Options { JoinType = JoinType.Miter });
            CheckWellFormed(r);
            Assert.Single(r.Polygons);
            Assert.Equal(new AxisAlignedBox2d(new Vector2d(1, 1), new Vector2d(9, 9)), r.Polygons[0].Bounds);
            Assert.Equal(64 - 16, r.Area, 9);
            // deflated away completely: empty, still a success
            Clipper2Result gone = Clipper2Polygons.Offset(new[] { Rect(0, 0, 1, 1) }, -0.6);
            Assert.True(gone.Ok);
            Assert.Empty(gone.Polygons);
        }

        [Fact]
        public void Inflate_MergesNearbyPolygons()
        {
            var two = new[] { Rect(0, 0, 1, 1), Rect(1.5, 0, 2.5, 1) };
            Assert.Equal(2, Clipper2Polygons.Offset(two, 0.2).Polygons.Count);
            Assert.Single(Clipper2Polygons.Offset(two, 0.3).Polygons);
        }

        [Fact]
        public void PolyLine_Offset_BandAreas()
        {
            var line = new[] { new PolyLine2d(new[] { new Vector2d(0, 0), new Vector2d(10, 0) }) };
            double d = 0.5;
            Assert.Equal(10, Clipper2Polygons.OffsetPolyLines(line, d, new Clipper2Options { EndType = EndType.Butt }).Area, 9);
            Assert.Equal(11 * 1, Clipper2Polygons.OffsetPolyLines(line, d, new Clipper2Options { EndType = EndType.Square }).Area, 9);
            Clipper2Result round = Clipper2Polygons.OffsetPolyLines(line, d);
            CheckWellFormed(round);
            Assert.InRange(round.Area, 10 + Math.PI * d * d * 0.99, 10 + Math.PI * d * d + 1e-9);
            // a closed polyline (Joined): a ring of width 2d around the square outline
            var outline = new[] { new PolyLine2d(Rect(0, 0, 4, 4).Outer.Vertices) };
            Clipper2Result joined = Clipper2Polygons.OffsetPolyLines(outline, d, new Clipper2Options { EndType = EndType.Joined, JoinType = JoinType.Miter });
            CheckWellFormed(joined);
            Assert.Single(joined.Polygons);
            Assert.Single(joined.Polygons[0].Holes);
            Assert.Equal(25 - 9, joined.Area, 9);
        }

        [Fact]
        public void Offset_InvalidParameters_AreReported()
        {
            var sq = new[] { Rect(0, 0, 1, 1) };
            var line = new[] { new PolyLine2d(new[] { Vector2d.Zero, Vector2d.AxisX }) };
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.Offset(sq, double.NaN).Status);
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.Offset(sq, double.PositiveInfinity).Status);
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.Offset(sq, 1, new Clipper2Options { MiterLimit = 0.5 }).Status);
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.Offset(sq, 1, new Clipper2Options { ArcTolerance = -1 }).Status);
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.OffsetPolyLines(line, -1).Status);
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.OffsetPolyLines(line, 1, new Clipper2Options { EndType = EndType.Polygon }).Status);
            Assert.True(Clipper2Polygons.Offset(sq, 0).Ok);
            Assert.Equal(1, Clipper2Polygons.Offset(sq, 0).Area, 12);
        }

        [Fact]
        public void Offset_LargeDelta_StaysInRange()
        {
            // the margin (delta times the miter limit) is part of the grid extent: no overflow for delta >> input size
            var sq = new[] { Rect(0, 0, 1e-3, 1e-3) };
            foreach (double delta in new[] { 1e3, 1e5 }) {
                Clipper2Result r = Clipper2Polygons.Offset(sq, delta, new Clipper2Options { JoinType = JoinType.Miter });
                CheckWellFormed(r);
                Assert.True(Math.Abs(r.Area / Math.Pow(2 * delta + 1e-3, 2) - 1) < 1e-10);   // a few grid spacings (2^-39 of the extent)
                // every result vertex is a grid point within the documented range (exact in double, below 2^52)
                foreach (Vector2d v in r.Polygons[0].Outer.Vertices)
                    Assert.Equal(v, r.Frame.ToVector2d(r.Frame.ToPoint64(v)));
            }
        }
    }
}
