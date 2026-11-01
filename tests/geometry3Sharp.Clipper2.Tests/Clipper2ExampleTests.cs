using System.Collections.Generic;
using System.Linq;
using g3;
using g3.Clipper2;
using g3.Examples;
using Xunit;
using static geometry3Sharp.Clipper2.Tests.Oracle;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary> runs docs/examples/Clipper2/Clipper2Example.cs (linked from docs/OPERATIONS.md) </summary>
    public class Clipper2ExampleTests
    {
        [Fact]
        public void Clipper2Example_FreeAreaAndBreakLine()
        {
            // a 100 x 60 m parcel at UTM coordinates, two overlapping footprints and a third one, a 3 m setback
            Vector2d o = new Vector2d(632100, 5712000);
            GeneralPolygon2d R(double x0, double y0, double x1, double y1) => Rect(o.x + x0, o.y + y0, o.x + x1, o.y + y1);
            var footprints = new List<GeneralPolygon2d> { R(10, 10, 30, 25), R(25, 15, 40, 30), R(60, 20, 80, 40) };
            var line = new PolyLine2d(new[] { o + new Vector2d(-10, 20), o + new Vector2d(110, 20) });

            Clipper2Result free = Clipper2Example.Run(R(0, 0, 100, 60), footprints, line, 3, out Clipper2Result pieces);

            CheckWellFormed(free);
            // mitered offsets of axis-aligned footprints are the footprints grown by 3 m on each side:
            // [7,33]x[7,28] u [22,43]x[12,33] (811 m^2) and [57,83]x[17,43] (676 m^2), inside the parcel as holes
            Assert.Single(free.Polygons);
            Assert.Equal(2, free.Polygons[0].Holes.Count);
            Assert.True(System.Math.Abs(Area(free.Polygons) - (6000 - 811 - 676)) < 1e-6);
            // the break line at y = 20 is free on x in [0,7], [43,57] and [83,100]
            Assert.True(pieces.Ok);
            Assert.Equal(3, pieces.PolyLines.Count);
            Assert.True(System.Math.Abs(pieces.PolyLines.Sum(l => l.ArcLength) - (7 + 14 + 17)) < 1e-6);
            foreach (PolyLine2d piece in pieces.PolyLines)
                Assert.Equal(1, ResultCount(free.Polygons, piece.Vertices[0] * 0.5 + piece.Vertices[piece.VertexCount - 1] * 0.5));
        }
    }
}
