using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using g3;
using g3.Clipper2;
using geometry3Sharp.Tests;
using Xunit;
using static geometry3Sharp.Clipper2.Tests.Oracle;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// The integer grid (ClipperFrame), invariance over Similarity.All(), determinism, and degenerate and invalid
    /// inputs.
    /// </summary>
    public class Clipper2RobustnessTests
    {
        // ---- frame ----

        [Fact]
        public void Frame_RoundTrip_WithinHalfResolution()
        {
            var rand = new Random(3);
            var box = new AxisAlignedBox2d(new Vector2d(632000, 5712000), new Vector2d(642000, 5713000));
            Assert.True(ClipperFrame.ForBounds(box, 0, ClipperFrame.DefaultPrecisionBits, out ClipperFrame frame));
            Assert.Equal(box.Center, frame.Origin);
            // power of two, and the half extent (5000) times the scale within 2^40 but above 2^39
            Assert.Equal(Math.Round(Math.Log(frame.Scale, 2)), Math.Log(frame.Scale, 2), 12);
            Assert.InRange(5000 * frame.Scale, Math.Pow(2, 39), Math.Pow(2, 40));
            foreach (Vector2d p in Samples(rand, box, 1000)) {
                Point64 q = frame.ToPoint64(p);
                Assert.InRange(Math.Abs(q.X), 0, 1L << 40);
                Vector2d back = frame.ToVector2d(q);
                double ulp = 2 * 1.2e-16 * 5713000;
                Assert.True(Math.Abs(back.x - p.x) <= 0.5 * frame.Resolution + ulp);
                Assert.True(Math.Abs(back.y - p.y) <= 0.5 * frame.Resolution + ulp);
            }
            // grid points convert exactly
            var grid = new Point64(123456789, -987654321);
            Assert.Equal(grid, frame.ToPoint64(frame.ToVector2d(grid)));
        }

        [Fact]
        public void Frame_PowerOfTwoScaling_GivesSameIntegers()
        {
            var rand = new Random(4);
            var box = new AxisAlignedBox2d(new Vector2d(-3.7, 1.1), new Vector2d(5.3, 2.9));
            Assert.True(ClipperFrame.ForBounds(box, 0.25, 30, out ClipperFrame f1));
            foreach (int k in new[] { -20, -3, 7, 40 }) {
                double s = Math.Pow(2, k);
                var scaled = new AxisAlignedBox2d(box.Min * s, box.Max * s);
                Assert.True(ClipperFrame.ForBounds(scaled, 0.25 * s, 30, out ClipperFrame fk));
                Assert.Equal(f1.Scale / s, fk.Scale);
                foreach (Vector2d p in Samples(rand, box, 50))
                    Assert.Equal(f1.ToPoint64(p), fk.ToPoint64(p * s));
            }
        }

        [Fact]
        public void Frame_RejectsOutOfRange()
        {
            Assert.False(ClipperFrame.ForBounds(new AxisAlignedBox2d(new Vector2d(0, 0), new Vector2d(1, 1)), 0, 7, out _));
            Assert.False(ClipperFrame.ForBounds(new AxisAlignedBox2d(new Vector2d(0, 0), new Vector2d(1, 1)), 0, 51, out _));
            // a finite extent of 1e308 is allowed (scale 2^-984); infinite bounds and extents below 2^-998 are not
            Assert.True(ClipperFrame.ForBounds(new AxisAlignedBox2d(new Vector2d(-1e308, 0), new Vector2d(1e308, 1)), 0, 40, out ClipperFrame wide));
            Assert.Equal(Math.Pow(2, -984), wide.Scale);
            Assert.False(ClipperFrame.ForBounds(new AxisAlignedBox2d(new Vector2d(0, 0), new Vector2d(double.PositiveInfinity, 1)), 0, 40, out _));
            Assert.False(ClipperFrame.ForBounds(new AxisAlignedBox2d(new Vector2d(0, 0), new Vector2d(1e-300, 1e-300)), 0, 40, out _));
            Assert.False(ClipperFrame.ForBounds(AxisAlignedBox2d.Empty, 0, 40, out _));
            Assert.Throws<ArgumentException>(() => new ClipperFrame(Vector2d.Zero, 0));
            Assert.Throws<ArgumentException>(() => new ClipperFrame(new Vector2d(double.NaN, 0), 1));
            var unit = new ClipperFrame(Vector2d.Zero, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => unit.ToPoint64(new Vector2d(1e17, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => unit.ToPoint64(new Vector2d(double.NaN, 0)));
        }

        [Fact]
        public void ExplicitFrame_IsUsedAndChecked()
        {
            var a = new[] { Rect(0.0004, 0, 2, 2) };
            var b = new[] { Rect(1, 1, 3, 3) };
            var mm = new ClipperFrame(Vector2d.Zero, 1000);   // millimetre grid
            Clipper2Result r = Clipper2Polygons.Union(a, b, new Clipper2Options { Frame = mm });
            CheckWellFormed(r);
            Assert.Equal(1000, r.Frame.Scale);
            Assert.Equal(7, r.Area, 9);   // 0.0004 snapped to 0
            var tooFine = new ClipperFrame(Vector2d.Zero, Math.Pow(2, 52));
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.Union(a, b, new Clipper2Options { Frame = tooFine }).Status);
        }

        // ---- invariance ----

        /// <summary>
        /// The 2D part of a Similarity: scale and the xy translation; the rotated ones rotate by 31 degrees in the
        /// plane (their 3D rotations tilt out of it).
        /// </summary>
        static Func<Vector2d, Vector2d> Map2d(Similarity s, bool inverse = false)
        {
            double angle = s.IsRotated ? 31 * Math.PI / 180 : 0, c = Math.Cos(angle), sn = Math.Sin(angle);
            Vector2d t = new Vector2d(s.Translation.x, s.Translation.y);
            if (!inverse)
                return p => s.Scale * new Vector2d(c * p.x - sn * p.y, sn * p.x + c * p.y) + t;
            return p => { Vector2d q = (p - t) / s.Scale; return new Vector2d(c * q.x + sn * q.y, -sn * q.x + c * q.y); };
        }

        static GeneralPolygon2d Mapped(GeneralPolygon2d g, Func<Vector2d, Vector2d> f)
        {
            var copy = new GeneralPolygon2d(g);
            copy.Transform(f);
            return copy;
        }

        static IEnumerable<(string name, Func<IEnumerable<GeneralPolygon2d>, IEnumerable<GeneralPolygon2d>, Func<Vector2d, Vector2d>, Clipper2Result> op)> Operations()
        {
            yield return ("union", (a, b, f) => Clipper2Polygons.Union(a, b));
            yield return ("intersection", (a, b, f) => Clipper2Polygons.Intersection(a, b));
            yield return ("difference", (a, b, f) => Clipper2Polygons.Difference(a, b));
            yield return ("xor", (a, b, f) => Clipper2Polygons.Xor(a, b));
            yield return ("offset round", (a, b, f) => Clipper2Polygons.Offset(a, 0.6 * Length(f)));
            yield return ("offset miter", (a, b, f) => Clipper2Polygons.Offset(a, -0.3 * Length(f), new Clipper2Options { JoinType = JoinType.Miter }));
            yield return ("simplify", (a, b, f) => Clipper2Polygons.Simplify(a, 0.05 * Length(f)));
            yield return ("minkowski", (a, b, f) => Clipper2Polygons.MinkowskiSum(
                new Polygon2d(new[] { new Vector2d(-0.5, -0.2), new Vector2d(0.5, -0.2), new Vector2d(0, 0.6) }.Select(v => f(v) - f(Vector2d.Zero))), a.First().Outer, true));
        }

        /// <summary> the scale of a similarity map (the length of a mapped unit vector) </summary>
        static double Length(Func<Vector2d, Vector2d> f) => (f(Vector2d.AxisX) - f(Vector2d.Zero)).Length;

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Operations_InvariantUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            var rand = new Random(17);
            var a = new List<GeneralPolygon2d> { StarWithHole(rand, new Vector2d(0, 0), 25, 5), new GeneralPolygon2d(Star(rand, new Vector2d(7, 2), 13, 1, 3)) };
            var b = new List<GeneralPolygon2d> { StarWithHole(rand, new Vector2d(3, -1), 19, 4) };
            Func<Vector2d, Vector2d> f = Map2d(s), back = Map2d(s, true);
            var ta = a.Select(g => Mapped(g, f)).ToList();
            var tb = b.Select(g => Mapped(g, f)).ToList();
            // a rotation of the inputs (but not of the grid) changes which points fall on which grid points: compare
            // up to a few grid spacings; the power-of-two scales map to the same integers: bit for bit
            foreach (var (opName, op) in Operations()) {
                Clipper2Result r0 = op(a, b, p => p), r1 = op(ta, tb, f);
                CheckWellFormed(r0); CheckWellFormed(r1);
                List<GeneralPolygon2d> mapped = r1.Polygons.Select(g => Mapped(g, back)).ToList();
                Assert.Equal(r0.Polygons.Count, mapped.Count);
                Assert.Equal(r0.Polygons.Sum(g => g.Holes.Count), mapped.Sum(g => g.Holes.Count));
                if (s.IsExact) {
                    for (int i = 0; i < mapped.Count; ++i) {
                        var rings0 = Rings(r0.Polygons[i]).ToList();
                        var rings1 = Rings(mapped[i]).ToList();
                        Assert.Equal(rings0.Count, rings1.Count);
                        for (int k = 0; k < rings0.Count; ++k)
                            Assert.True(rings0[k].SequenceEqual(rings1[k]), opName + " " + name + ": ring " + k + " not bit for bit");
                    }
                    continue;
                }
                double tol = 4 * r0.Frame.Resolution + 8 * s.Resolution(15);
                if (opName == "simplify") tol = 0.1;   // the choice of removed vertices may change near the threshold
                double area0 = r0.Area;
                Assert.True(Math.Abs(Area(mapped) - area0) <= tol * 200, opName + " " + name + ": area " + Area(mapped) + " vs " + area0);
                foreach (Vector2d v in mapped.SelectMany(Rings).SelectMany(r => r))
                    Assert.True(BoundaryDistance(r0.Polygons, v) <= tol, opName + " " + name + ": vertex " + v + " off by " + BoundaryDistance(r0.Polygons, v));
            }
        }

        // ---- determinism ----

        [Fact]
        public void RepeatedRuns_AreBitIdentical_AndOrderIndependentInArea()
        {
            var rand = new Random(23);
            var a = Enumerable.Range(0, 6).Select(i => StarWithHole(rand, new Vector2d(3 * i, rand.NextDouble()), 17, 2.5)).ToList();
            var b = Enumerable.Range(0, 4).Select(i => new GeneralPolygon2d(Star(rand, new Vector2d(4 * i + 1, 1), 11, 1, 3))).ToList();
            foreach (ClipType op in new[] { ClipType.Union, ClipType.Intersection, ClipType.Difference, ClipType.Xor }) {
                Clipper2Result first = Clipper2Polygons.Boolean(op, a, b);
                for (int k = 0; k < 3; ++k) {
                    Clipper2Result again = Clipper2Polygons.Boolean(op, a, b);
                    Assert.Equal(Key(first), Key(again));
                }
                var shuffled = a.OrderBy(x => rand.Next()).ToList();
                Assert.Equal(first.Area, Clipper2Polygons.Boolean(op, shuffled, b).Area, 9);
            }
            Assert.Equal(Key(Clipper2Polygons.Offset(a, 0.3)), Key(Clipper2Polygons.Offset(a, 0.3)));
            // the input is not modified (orientation fixes happen on copies)
            Polygon2d cw = a[0].Holes[0];
            Assert.True(cw.IsClockwise);
        }

        static string Key(Clipper2Result r)
            => string.Join(";", r.Polygons.SelectMany(Rings).Select(ring => string.Join(",", ring.Select(v => v.x.ToString("R") + " " + v.y.ToString("R")))));

        // ---- degenerate and invalid input ----

        [Fact]
        public void EmptyAndNullInputs_GiveEmptySuccess()
        {
            foreach (Clipper2Result r in new[] {
                    Clipper2Polygons.Union(new GeneralPolygon2d[0]),
                    Clipper2Polygons.Union(null),
                    Clipper2Polygons.Intersection(new[] { Rect(0, 0, 1, 1) }, null),
                    Clipper2Polygons.Union(new[] { new GeneralPolygon2d(), null }),
                    Clipper2Polygons.Offset(new GeneralPolygon2d[0], 1),
                    Clipper2Polygons.OffsetPolyLines(new[] { new PolyLine2d() }, 1),
                    Clipper2Polygons.ClipPolyLines(ClipType.Intersection, null, new[] { Rect(0, 0, 1, 1) }),
                    Clipper2Polygons.MinkowskiSum(new Polygon2d(), Rect(0, 0, 1, 1).Outer, true),
                    Clipper2Polygons.Simplify(null, 0.1) }) {
                Assert.True(r.Ok, r.ToString());
                Assert.Empty(r.Polygons);
                Assert.Empty(r.PolyLines);
            }
            Assert.Equal(1, Clipper2Polygons.Difference(new[] { Rect(0, 0, 1, 1) }, null).Area, 12);
        }

        [Fact]
        public void CollinearAndDuplicatePoints()
        {
            // zero area rings vanish
            var flat = new GeneralPolygon2d(Ring(0, 0, 1, 1, 2, 2, 3, 3));
            var point = new GeneralPolygon2d(Ring(1, 1, 1, 1, 1, 1));
            Clipper2Result r = Clipper2Polygons.Union(new[] { flat, point });
            Assert.True(r.Ok);
            Assert.Empty(r.Polygons);
            // duplicate and collinear vertices are removed from the result
            var dup = new GeneralPolygon2d(Ring(0, 0, 0, 0, 1, 0, 2, 0, 2, 0, 2, 2, 1, 2, 0, 2, 0, 2));
            r = Clipper2Polygons.Union(new[] { dup });
            CheckWellFormed(r);
            Assert.Equal(4, r.Polygons[0].Outer.VertexCount);
            Assert.Equal(4, r.Area, 12);
            r = Clipper2Polygons.Union(new[] { dup }, null, new Clipper2Options { PreserveCollinear = true });
            CheckWellFormed(r);
            Assert.Equal(6, r.Polygons[0].Outer.VertexCount);
            // a single point offset with round ends: a disc
            Clipper2Result disc = Clipper2Polygons.OffsetPolyLines(new[] { new PolyLine2d(new[] { new Vector2d(5, 5) }) }, 1);
            CheckWellFormed(disc);
            Assert.InRange(disc.Area, Math.PI * 0.99, Math.PI + 1e-9);
        }

        [Fact]
        public void SelfTouchingInputs()
        {
            // two squares that touch at a corner, and a ring touching itself at a vertex (bowtie outline)
            Clipper2Result corner = Clipper2Polygons.Union(new[] { Rect(0, 0, 1, 1), Rect(1, 1, 2, 2) });
            CheckWellFormed(corner);
            Assert.Equal(2, corner.Area, 12);
            Assert.Equal(0, Clipper2Polygons.Intersection(new[] { Rect(0, 0, 1, 1) }, new[] { Rect(1, 1, 2, 2) }).Area, 12);
            // squares sharing an edge merge into one rectangle
            Clipper2Result edge = Clipper2Polygons.Union(new[] { Rect(0, 0, 1, 1), Rect(1, 0, 2, 1) });
            CheckWellFormed(edge);
            Assert.Single(edge.Polygons);
            Assert.Equal(4, edge.Polygons[0].Outer.VertexCount);
            // a bowtie: the two lobes have opposite orientation; NonZero fills both, Positive only the CCW one
            Polygon2d bowtie = Ring(0, 0, 2, 2, 2, 0, 0, 2);
            Assert.Equal(2, Clipper2Polygons.BooleanPaths(ClipType.Union, new[] { bowtie }, null).Area, 12);
            Assert.Equal(1, Clipper2Polygons.BooleanPaths(ClipType.Union, new[] { bowtie }, null, new Clipper2Options { FillRule = FillRule.Positive }).Area, 12);
            // a hole that touches the outer ring at a vertex
            var g = new GeneralPolygon2d(Rect(0, 0, 4, 4).Outer);
            g.AddHole(Ring(0, 0, 1, 2, 2, 1), false, false);
            Clipper2Result touching = Clipper2Polygons.Union(new[] { g });
            CheckWellFormed(touching);
            Assert.Equal(16 - 1.5, touching.Area, 12);
        }

        [Fact]
        public void UtmCoordinates_MatchTheOriginResult()
        {
            var rand = new Random(31);
            var a = new[] { StarWithHole(rand, Vector2d.Zero, 29, 40) };   // a 80 m footprint
            var b = new[] { StarWithHole(rand, new Vector2d(25, 10), 23, 35) };
            Vector2d utm = new Vector2d(632117.25, 5712043.5);
            Func<Vector2d, Vector2d> shift = p => p + utm;
            Clipper2Result r0 = Clipper2Polygons.Difference(a, b);
            Clipper2Result r1 = Clipper2Polygons.Difference(a.Select(g => Mapped(g, shift)), b.Select(g => Mapped(g, shift)));
            CheckWellFormed(r1);
            Assert.Equal(r0.Polygons.Count, r1.Polygons.Count);
            Assert.True(Math.Abs(Area(r1.Polygons) - r0.Area) < 1e-6);   // mm^2 on thousands of m^2
            // the grid at UTM is still sub-nanometre for this extent
            Assert.True(r1.Frame.Resolution < 1e-9);
            foreach (Vector2d v in r1.Polygons.SelectMany(Rings).SelectMany(r => r))
                Assert.True(BoundaryDistance(r0.Polygons, v - utm) < 1e-8);
        }

        [Fact]
        public void NonFiniteAndInvalid_AreReported()
        {
            var nan = new GeneralPolygon2d(Ring(0, 0, 1, 0, double.NaN, 1));
            var inf = new GeneralPolygon2d(Ring(0, 0, double.PositiveInfinity, 0, 1, 1));
            var sq = new[] { Rect(0, 0, 1, 1) };
            Assert.Equal(Clipper2Status.InvalidInput, Clipper2Polygons.Union(new[] { nan }).Status);
            Assert.Equal(Clipper2Status.InvalidInput, Clipper2Polygons.Difference(sq, new[] { inf }).Status);
            Assert.Equal(Clipper2Status.InvalidInput, Clipper2Polygons.Offset(new[] { nan }, 1).Status);
            Assert.Equal(Clipper2Status.InvalidInput, Clipper2Polygons.MinkowskiSum(Ring(0, 0, double.NaN, 1, 1, 0), sq[0].Outer, true).Status);
            Assert.Equal(Clipper2Status.InvalidInput, Clipper2Polygons.Union(new[] { Rect(0, 0, 1e-300, 1e-300) }).Status);
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.Boolean(ClipType.NoClip, sq, sq).Status);
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.Union(sq, null, new Clipper2Options { PrecisionBits = 60 }).Status);
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.Simplify(sq, -1).Status);
            Assert.Equal(Clipper2Status.InvalidParameter, Clipper2Polygons.MinkowskiSum(null, sq[0].Outer, true).Status);
            Clipper2Result bad = Clipper2Polygons.Union(new[] { nan });
            Assert.NotNull(bad.Error);
            Assert.Empty(bad.Polygons);
            // huge but finite coordinates work on a coarse grid
            Clipper2Result huge = Clipper2Polygons.Union(new[] { Rect(0, 0, 1e300, 1e300) });
            CheckWellFormed(huge);
            AxisAlignedBox2d hb = huge.Polygons[0].Bounds;
            Assert.True(Math.Abs(hb.Min.x) <= huge.Frame.Resolution && Math.Abs(hb.Max.y - 1e300) <= huge.Frame.Resolution);
        }
    }
}
