using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using g3.Clipper2;
using geometry3Sharp.Tests;
using Xunit;
using V = g3.Clipper2.Vendored;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// Port of PolyClipping/Clipper2 6c7d083 "Stop ClipperOffset leaking per-path state" (Clipper2Lib/README.md): a 2-point
    /// Joined path changed the end type for the rest of its group (through the adapter: OffsetPolyLines with EndType.Joined
    /// gave another area when a 2-point polyline came first), a group with no closed path left the delta positive for the
    /// later groups, and a single point saw the previous path's normals and arc steps in a delta callback. The first four
    /// tests are the upstream ones, translated.
    /// </summary>
    public class Clipper2OffsetStateTests
    {
        static V.Path64 P(params long[] c) => new V.Path64(Enumerable.Range(0, c.Length / 2).Select(i => new V.Point64(c[2 * i], c[2 * i + 1])));

        [Fact]
        public void JoinedTwoPointPath_DoesNotChangeTheNextPathsEnds()
        {
            V.Path64 two = P(0, 0, 100, 0), tri = P(0, 1000, 500, 1000, 500, 1500);
            double triFirst = V.Clipper.Area(V.Clipper.InflatePaths(new V.Paths64 { tri, two }, 20, V.JoinType.Miter, V.EndType.Joined));
            double twoFirst = V.Clipper.Area(V.Clipper.InflatePaths(new V.Paths64 { two, tri }, 20, V.JoinType.Miter, V.EndType.Joined));
            Assert.Equal(73268.5, triFirst);
            Assert.Equal(triFirst, twoFirst);   // 47200 before
        }

        [Fact]
        public void DegenerateGroup_KeepsTheDeltaSignOfLaterGroups()
        {
            // a polygon group without a closed path is always inflated, but later groups must still shrink
            V.Path64 line = P(0, 500, 100, 500), square = P(0, 0, 100, 0, 100, 100, 0, 100);
            double Run(params V.Path64[] paths)
            {
                var offset = new V.ClipperOffset();
                foreach (V.Path64 p in paths) offset.AddPath(p, V.JoinType.Miter, V.EndType.Polygon);
                var solution = new V.Paths64();
                offset.Execute(-10, solution);
                return V.Clipper.Area(solution);
            }
            Assert.Equal(6400, Run(square));
            Assert.Equal(Run(line) + Run(square), Run(line, square));   // 16800 before (2400 + 14400)
        }

        [Fact]
        public void Joined_TwoPointPathBeforeACollapsedPath_DoesNotThrow()
        {
            // the upstream test "a single point rounded for its own delta" (TestOffsetsDeltaCallbackRoundPointOrder): in C# a
            // Joined group strips the point to an empty path, so it tests something else here: after a 2-point Joined path
            // the empty path took the open ends and OffsetOpenPath threw ArgumentOutOfRangeException (before the fix); the
            // single point's arc steps are pinned by DeltaCallback_RoundEnds_ASinglePointGetsTheArcStepsOfItsOwnDelta
            V.Path64 point = P(0, 0), line = P(0, 1000, 100, 1000);
            double Run(V.Paths64 paths)
            {
                var offset = new V.ClipperOffset();
                offset.AddPaths(paths, V.JoinType.Round, V.EndType.Joined);
                offset.DeltaCallback = (path, norms, i, j) => 20.0;
                var solution = new V.Paths64();
                offset.Execute(1.0, solution);
                return V.Clipper.Area(solution);
            }
            Assert.Equal(Run(new V.Paths64 { point, line }), Run(new V.Paths64 { line, point }));
        }

        [Fact]
        public void DeltaCallback_RoundEnds_ASinglePointGetsTheArcStepsOfItsOwnDelta()
        {
            // the C# single point is a circle for EndType.Round (C++: JoinType.Round); a callback with a delta per path: the
            // circle of the point (delta 200) must not use the arc steps that DoRound left for the line's delta (2)
            // (with a fixed arc tolerance: the default one is relative to the delta, so the steps would not depend on it)
            V.Path64 point = P(0, 0), line = P(0, 1000, 100, 1000, 100, 1100);
            double Run(V.Paths64 paths)
            {
                var offset = new V.ClipperOffset(2.0, 0.5);
                offset.AddPaths(paths, V.JoinType.Round, V.EndType.Round);
                offset.DeltaCallback = (path, norms, i, j) => path.Count == 1 ? 200.0 : 2.0;
                var solution = new V.Paths64();
                offset.Execute(1.0, solution);
                return V.Clipper.Area(solution);
            }
            Assert.Equal(Run(new V.Paths64 { point, line }), Run(new V.Paths64 { line, point }));
        }

        [Fact]
        public void DeltaCallback_SeesTheNormalsOfItsOwnPath()
        {
            var offset = new V.ClipperOffset();
            // the points come before and after a longer path so neither can see another path's normals (an open group: the
            // C# 2.0.0 code drops the single points of a Polygon group before the callback sees them)
            offset.AddPaths(new V.Paths64 { P(0, 0), P(100, 0, 200, 0, 200, 100), P(500, 500) }, V.JoinType.Square, V.EndType.Square);
            bool normalsMatchPath = true;
            offset.DeltaCallback = (path, norms, i, j) => { normalsMatchPath &= norms.Count == path.Count; return 10.0; };
            var solution = new V.Paths64();
            offset.Execute(1.0, solution);
            Assert.True(normalsMatchPath);
            Assert.Equal(3, solution.Count);
        }

        static readonly PolyLine2d Two = new PolyLine2d(new[] { new Vector2d(0, 0), new Vector2d(100, 0) });
        static readonly PolyLine2d Tri = new PolyLine2d(new[] { new Vector2d(0, 1000), new Vector2d(500, 1000), new Vector2d(500, 1500) });

        [Theory]
        [InlineData(JoinType.Miter)]
        [InlineData(JoinType.Square)]
        [InlineData(JoinType.Bevel)]
        [InlineData(JoinType.Round)]
        public void OffsetPolyLines_Joined_DoesNotDependOnTheOrder(JoinType join)
        {
            // through the adapter: the band of the triangle path with a 2-point line before or after it
            var options = new Clipper2Options { JoinType = join, EndType = EndType.Joined };
            Clipper2Result triFirst = Clipper2Polygons.OffsetPolyLines(new[] { Tri, Two }, 20, options);
            Clipper2Result twoFirst = Clipper2Polygons.OffsetPolyLines(new[] { Two, Tri }, 20, options);
            Assert.True(triFirst.Ok && twoFirst.Ok);
            Assert.Equal(triFirst.Area, twoFirst.Area, 9);   // Miter: 47 200 against 73 022 before
            // and it is the union of the two bands offset one by one
            double alone = Clipper2Polygons.OffsetPolyLines(new[] { Tri }, 20, options).Area
                         + Clipper2Polygons.OffsetPolyLines(new[] { Two }, 20, options).Area;
            Assert.True(Math.Abs(triFirst.Area - alone) < 1e-6 * alone, triFirst.Area + " against " + alone);   // the bands are disjoint
        }

        [Theory]
        [MemberData(nameof(Similarity.Names2D), MemberType = typeof(Similarity))]
        public void OffsetPolyLines_Joined_UnderSimilarity(string name)
        {
            // the order independence under every similarity of the plane; a power-of-two scale gives the same grid integers
            // and so the same result bit for bit (vertices scaled), any other the same area up to the grid rounding
            Similarity s = Similarity.ByName(name);
            PolyLine2d Map(PolyLine2d p) => new PolyLine2d(p.Vertices.Select(s.Apply2d));
            var options = new Clipper2Options { JoinType = JoinType.Miter, EndType = EndType.Joined };
            Clipper2Result plain = Clipper2Polygons.OffsetPolyLines(new[] { Two, Tri }, 20, options);
            Clipper2Result mapped = Clipper2Polygons.OffsetPolyLines(new[] { Map(Two), Map(Tri) }, 20 * s.Scale, options);
            Clipper2Result mappedOther = Clipper2Polygons.OffsetPolyLines(new[] { Map(Tri), Map(Two) }, 20 * s.Scale, options);
            Assert.True(plain.Ok && mapped.Ok && mappedOther.Ok, name);
            double expected = s.Scale * s.Scale * plain.Area;
            Assert.True(Math.Abs(mapped.Area - expected) <= 1e-6 * expected, name + ": " + mapped.Area + " against " + expected);
            Assert.True(Math.Abs(mappedOther.Area - mapped.Area) <= 1e-6 * expected, name + ": order changes the area");
            if (s.Angle2d == 0 && s.Translation.x == 0 && s.Translation.y == 0 && Clipper2DegenerateUnionTests.IsPowerOfTwo(s.Scale)) {
                Assert.Equal(plain.Polygons.Count, mapped.Polygons.Count);
                for (int i = 0; i < plain.Polygons.Count; ++i)
                    Assert.Equal(plain.Polygons[i].Outer.Vertices.Select(v => s.Scale * v), mapped.Polygons[i].Outer.Vertices);
            }
        }

        [Fact]
        public void RandomPolyLines_JoinedOffset_DoesNotDependOnTheOrder()
        {
            // random sets of 2- to 5-point polylines on a lattice, every join type: the union of the bands must not depend on
            // the order of the lines (it did whenever a 2-point line came before a longer one)
            var rand = new Random(5);
            int checkedSets = 0;
            for (int i = 0; i < 150; ++i) {
                int n = rand.Next(2, 5);
                var lines = Enumerable.Range(0, n).Select(_ => new PolyLine2d(Enumerable.Range(0, rand.Next(2, 6))
                    .Select(__ => new Vector2d(rand.Next(-20, 21), rand.Next(-20, 21))))).ToList();
                foreach (JoinType join in new[] { JoinType.Miter, JoinType.Square, JoinType.Bevel, JoinType.Round }) {
                    var options = new Clipper2Options { JoinType = join, EndType = EndType.Joined };
                    Clipper2Result forward = Clipper2Polygons.OffsetPolyLines(lines, 0.7, options);
                    Clipper2Result backward = Clipper2Polygons.OffsetPolyLines(Enumerable.Reverse(lines).ToList(), 0.7, options);
                    Assert.True(forward.Ok && backward.Ok);
                    Assert.True(Math.Abs(forward.Area - backward.Area) <= 1e-9 * Math.Max(1, forward.Area), "set " + i + " " + join + ": " + forward.Area + " against " + backward.Area);
                    ++checkedSets;
                }
            }
            Assert.Equal(600, checkedSets);
        }
    }
}
