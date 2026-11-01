using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;
using geometry3Sharp.Tests;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Quadtree;
using Xunit;

[Collection("StrtreeComparison.Console")]
public class NtsQuadtreeComparisonTests
{
    [Fact]
    public void ProceduralQueriesMatchScanAndLibraryWithReusedBuffers()
    {
        Triangle3d[] triangles = Program.Generate(8);
        AxisAlignedBox2d[] boxes = triangles.Select(Program.Bounds).ToArray();
        var index = new NtsQuadtreeComparison(boxes, triangles);
        var quad = new LooseQuadtree2d(boxes);
        var heights = new TriangleQuadtree2d(triangles);
        var expected = new List<int>();
        var actual = new List<int>();
        var other = new List<int>();
        for (int repeat = 0; repeat < 2; ++repeat) {
            var random = new Random(7);
            for (int i = 0; i < 40; ++i) {
                var p = new Vector2d(2539000 + random.NextDouble() * 8 * 7.07, 5640000 + random.NextDouble() * 8 * 7.07);
                var end = p + new Vector2d(200 * random.NextDouble() - 100, 200 * random.NextDouble() - 100);
                var rectangle = new AxisAlignedBox2d(p, p + new Vector2d(20, 20));
                Program.BruteRectangle(boxes, rectangle, expected);
                index.QueryRectangle(rectangle, actual);
                Program.EqualSets(expected, actual, "NTS rectangle");
                quad.QueryRectangle(rectangle, other);
                Program.EqualSets(expected, other, "library rectangle");
                Program.BruteSegment(boxes, p, end, expected);
                index.QuerySegment(p, end, actual);
                Program.EqualSets(expected, actual, "NTS segment");
                quad.QuerySegment(p, end, other);
                Program.EqualSets(expected, other, "library segment");
                Assert.Equal(Program.BruteNearest(boxes, p), index.FindNearest(p));
                Assert.Equal(quad.FindNearest(p), index.FindNearest(p));
                bool found = Program.BruteHeight(triangles, p, out double reference);
                Assert.Equal(found, index.TryGetHeight(p, out double height));
                Assert.Equal(reference, height);
                Assert.Equal(found, heights.TryGetHeight(p, out double libraryHeight, 0));
                if (found) Assert.True(Math.Abs(height - libraryHeight) < 1e-8);
                Assert.False(double.IsNaN(height) || double.IsInfinity(height));
            }
        }
    }

    [Fact]
    public void SimilaritiesPreserveQueriesAndMappedHeight()
    {
        var triangle = new Triangle3d(new Vector3d(0, 0, 2), new Vector3d(8, 0, 4), new Vector3d(0, 8, 6));
        var expected = new List<int>();
        var actual = new List<int>();
        foreach (Similarity s in Similarity.All()) {
            var t = new Triangle3d(s.Apply(triangle.V0), s.Apply(triangle.V1), s.Apply(triangle.V2));
            var boxes = new[] { Program.Bounds(t) };
            var index = new NtsQuadtreeComparison(boxes, new[] { t });
            Vector3d inside = s.Apply(new Vector3d(2, 2, 3.5));
            Vector3d outside = s.Apply(new Vector3d(20, 20, 2));
            var point = new Vector2d(inside.x, inside.y);
            var end = new Vector2d(outside.x, outside.y);
            index.QueryRectangle(new AxisAlignedBox2d(point, point), actual);
            Assert.Equal(new[] { 0 }, actual);
            Program.BruteSegment(boxes, point, end, expected);
            index.QuerySegment(point, end, actual);
            Program.EqualSets(expected, actual, s.Name);
            Assert.Equal(0, index.FindNearest(end));
            Assert.True(Program.BruteHeight(new[] { t }, point, out double reference));
            Assert.True(index.TryGetHeight(point, out double height));
            Assert.Equal(reference, height);
            double mapped = s.Inverse(new Vector3d(point.x, point.y, height)).z;
            if (s.IsExact) Assert.Equal(3.5, mapped);
            else Assert.True(Math.Abs(mapped - 3.5) <= 64 * s.Resolution(20), s.Name);
        }
    }

    [Fact]
    public void AncestorFalsePositivesAndPaddedPointBoundsAreFiltered()
    {
        var boxes = new[] { new AxisAlignedBox2d(-100, -100, 100, 100), new AxisAlignedBox2d(new Vector2d(4, 4)) };
        var raw = new Quadtree<int>();
        raw.Insert(new Envelope(-100, 100, -100, 100), 0);
        Assert.Contains(0, raw.Query(new Envelope(1000, 1001, 1000, 1001)));
        var index = new NtsQuadtreeComparison(boxes);
        var actual = new List<int> { -1 };
        index.QueryRectangle(new AxisAlignedBox2d(1000, 1000, 1001, 1001), actual);
        Assert.Empty(actual);
        index.QueryRectangle(new AxisAlignedBox2d(new Vector2d(100, 100)), actual);
        Assert.Equal(new[] { 0 }, actual);
        var points = new NtsQuadtreeComparison(new[] { new AxisAlignedBox2d(Vector2d.Zero), new AxisAlignedBox2d(new Vector2d(4, 4)) });
        points.QueryRectangle(new AxisAlignedBox2d(new Vector2d(0.25, 0.25)), actual);
        Assert.Empty(actual);
        points.QueryRectangle(new AxisAlignedBox2d(Vector2d.Zero), actual);
        Assert.Equal(new[] { 0 }, actual);
        index.QuerySegment(new Vector2d(-101, 100.0001), new Vector2d(101, 100.0001), actual);
        Assert.Equal(new[] { 0 }, actual);
        index.QuerySegment(new Vector2d(1000, 1000), new Vector2d(1001, 1001), actual);
        Assert.Empty(actual);
    }

    [Fact]
    public void ExpandingNearestSearchHandlesEmptyFarAndTiedInputsDeterministically()
    {
        var boxes = Enumerable.Range(0, 40).Select(i => new AxisAlignedBox2d(i % 2 == 0 ? -2 : 1, -1,
            i % 2 == 0 ? -1 : 2, 1)).ToArray();
        foreach (Vector2d point in new[] { Vector2d.Zero, new Vector2d(0, 10), new Vector2d(-1, 0), new Vector2d(1e8, -1e8) }) {
            int expected = Program.BruteNearest(boxes, point);
            for (int build = 0; build < 3; ++build) {
                var index = new NtsQuadtreeComparison(boxes);
                for (int reuse = 0; reuse < 3; ++reuse) Assert.Equal(expected, index.FindNearest(point));
            }
        }
        var points = new[] { new AxisAlignedBox2d(new Vector2d(5, 5)), new AxisAlignedBox2d(new Vector2d(-5, -5)) };
        Assert.Equal(0, new NtsQuadtreeComparison(points).FindNearest(Vector2d.Zero));
        var empty = new NtsQuadtreeComparison(new AxisAlignedBox2d[0]);
        Assert.Equal(-1, empty.FindNearest(Vector2d.Zero));
        var actual = new List<int> { -1 };
        empty.QueryRectangle(new AxisAlignedBox2d(Vector2d.Zero), actual);
        Assert.Empty(actual);
    }

    [Fact]
    public void HeightUsesStableIdsAndRejectsDegenerateAndOutsideTriangles()
    {
        var t = new Triangle3d(new Vector3d(0, 0, 10), new Vector3d(4, 0, 10), new Vector3d(0, 4, 10));
        var triangles = new[] { new Triangle3d(Vector3d.Zero, Vector3d.Zero, Vector3d.Zero), t,
            new Triangle3d(new Vector3d(0, 0, 20), new Vector3d(4, 0, 20), new Vector3d(0, 4, 20)) };
        var index = new NtsQuadtreeComparison(triangles.Select(Program.Bounds).ToArray(), triangles);
        foreach (Vector2d p in new[] { Vector2d.Zero, new Vector2d(2, 2), new Vector2d(3, 3), new Vector2d(50, 50) }) {
            bool found = Program.BruteHeight(triangles, p, out double expected);
            Assert.Equal(found, index.TryGetHeight(p, out double actual));
            Assert.Equal(expected, actual);
        }
        Assert.Throws<InvalidOperationException>(() => new NtsQuadtreeComparison(new AxisAlignedBox2d[0]).TryGetHeight(Vector2d.Zero, out _));
        Assert.Throws<ArgumentException>(() => new NtsQuadtreeComparison(new AxisAlignedBox2d[0], new[] { t }));
    }

    [Fact]
    public void CliReportsAllFourContendersWithStableChecksums()
    {
        TextWriter original = Console.Out;
        try {
            string prior = null;
            for (int repeat = 0; repeat < 2; ++repeat) {
                var text = new StringWriter();
                Console.SetOut(text);
                Assert.Equal(0, Program.Main(new[] { "4", "12", "6" }));
                string output = text.ToString();
                Assert.Contains("build,nts_quadtree,1,", output);
                Assert.Equal(4, output.Split('\n').Count(line => line.StartsWith("sample=")));
                foreach (string operation in new[] { "rectangle", "segment", "nearest", "height" }) {
                    Assert.Contains(operation + ",nts_quadtree,12,", output);
                    Assert.Contains(operation + ",nts_quadtree_prefix,6,", output);
                }
                string validation = output.Split('\n').Single(line => line.StartsWith("validated_queries="));
                if (prior != null) Assert.Equal(prior, validation);
                prior = validation;
            }
        } finally { Console.SetOut(original); }
    }
}
