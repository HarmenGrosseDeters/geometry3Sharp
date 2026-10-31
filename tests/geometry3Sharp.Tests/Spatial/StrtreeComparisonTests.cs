using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;
using geometry3Sharp.Tests;
using Xunit;

[CollectionDefinition("StrtreeComparison.Console", DisableParallelization = true)]
public class StrtreeComparisonConsoleCollection { }

[Collection("StrtreeComparison.Console")]
public class StrtreeComparisonTests
{
    [Fact]
    public void ProceduralQueriesMatchBothIndexesAndReuseBuffers()
    {
        Triangle3d[] triangles = Program.Generate(8);
        AxisAlignedBox2d[] boxes = triangles.Select(Program.Bounds).ToArray();
        var packed = new StrtreeComparison(boxes, triangles);
        var quad = new LooseQuadtree2d(boxes);
        var heights = new TriangleQuadtree2d(triangles);
        var expected = new List<int>();
        var actual = new List<int>();
        var other = new List<int>();
        var random = new Random(7);
        for (int repeat = 0; repeat < 2; ++repeat) {
            random = new Random(7);
            for (int i = 0; i < 40; ++i) {
                var p = new Vector2d(2539000 + random.NextDouble() * 8 * 7.07, 5640000 + random.NextDouble() * 8 * 7.07);
                var end = p + new Vector2d(200 * random.NextDouble() - 100, 200 * random.NextDouble() - 100);
                var rectangle = new AxisAlignedBox2d(p, p + new Vector2d(20, 20));
                Program.BruteRectangle(boxes, rectangle, expected);
                packed.QueryRectangle(rectangle, actual);
                Program.EqualSets(expected, actual, "packed rectangle");
                quad.QueryRectangle(rectangle, other);
                Program.EqualSets(expected, other, "quad rectangle");
                Program.BruteSegment(boxes, p, end, expected);
                packed.QuerySegment(p, end, actual);
                Program.EqualSets(expected, actual, "packed segment");
                quad.QuerySegment(p, end, other);
                Program.EqualSets(expected, other, "quad segment");
                Assert.Equal(Program.BruteNearest(boxes, p), packed.FindNearest(p));
                Assert.Equal(quad.FindNearest(p), packed.FindNearest(p));
                bool found = Program.BruteHeight(triangles, p, out double height);
                Assert.Equal(found, packed.TryGetHeight(p, out double indexed));
                Assert.Equal(height, indexed);
                Assert.Equal(found, heights.TryGetHeight(p, out double qheight, 0));
                if (found) Assert.True(Math.Abs(height - qheight) < 1e-8);
                Assert.False(double.IsNaN(indexed) || double.IsInfinity(indexed));
            }
        }
    }

    [Fact]
    public void TransformedInputsMatchOracleOverAllSimilarities()
    {
        var triangle = new Triangle3d(new Vector3d(0, 0, 2), new Vector3d(8, 0, 4), new Vector3d(0, 8, 6));
        var results = new List<int>();
        var expected = new List<int>();
        foreach (Similarity s in Similarity.All()) {
            var transformed = new Triangle3d(s.Apply(triangle.V0), s.Apply(triangle.V1), s.Apply(triangle.V2));
            var boxes = new[] { Program.Bounds(transformed) };
            var index = new StrtreeComparison(boxes, new[] { transformed });
            Vector3d inside = s.Apply(new Vector3d(2, 2, 3.5));
            var point = new Vector2d(inside.x, inside.y);
            var rectangle = new AxisAlignedBox2d(point, point);
            Program.BruteRectangle(boxes, rectangle, expected);
            index.QueryRectangle(rectangle, results);
            Program.EqualSets(expected, results, s.Name);
            Assert.Equal(new[] { 0 }, results);
            Vector3d outside = s.Apply(new Vector3d(20, 20, 2));
            var end = new Vector2d(outside.x, outside.y);
            Program.BruteSegment(boxes, point, end, expected);
            index.QuerySegment(point, end, results);
            Program.EqualSets(expected, results, s.Name);
            Assert.Equal(0, index.FindNearest(end));
            Assert.True(Program.BruteHeight(new[] { transformed }, point, out double reference));
            Assert.True(index.TryGetHeight(point, out double height));
            Assert.Equal(reference, height);
            double mappedHeight = s.Inverse(new Vector3d(point.x, point.y, height)).z;
            if (s.IsExact) Assert.Equal(3.5, mappedHeight);
            else Assert.True(Math.Abs(mappedHeight - 3.5) <= 64 * s.Resolution(20), s.Name);
        }
    }

    [Fact]
    public void NearestUsesStableIdTiesAndRepeatedBuilds()
    {
        var boxes = new AxisAlignedBox2d[40];
        for (int i = 0; i < boxes.Length; ++i) {
            double x = i % 2 == 0 ? -2 : 1;
            boxes[i] = new AxisAlignedBox2d(x, -1, x + 1, 1);
        }
        foreach (Vector2d p in new[] { Vector2d.Zero, new Vector2d(0, 10), new Vector2d(-1, 0) })
            for (int repeat = 0; repeat < 3; ++repeat) {
                var index = new StrtreeComparison(boxes);
                for (int reuse = 0; reuse < 3; ++reuse)
                    Assert.Equal(Program.BruteNearest(boxes, p), index.FindNearest(p));
            }
        Assert.Equal(-1, new StrtreeComparison(new AxisAlignedBox2d[0]).FindNearest(Vector2d.Zero));
    }

    [Fact]
    public void ClosedSegmentsFilterBroadPhaseAndKeepToleranceHits()
    {
        var boxes = new[] { new AxisAlignedBox2d(0, 0, 1, 1), new AxisAlignedBox2d(0, 4, 1, 5),
            new AxisAlignedBox2d(2, 2, 2, 2) };
        var index = new StrtreeComparison(boxes);
        var expected = new List<int>();
        var actual = new List<int>();
        var starts = new[] { new Vector2d(-1, 1.0001), new Vector2d(2, 2), new Vector2d(-1, -1) };
        var ends = new[] { new Vector2d(2, 1.0001), new Vector2d(2, 2), new Vector2d(5, 5) };
        for (int i = 0; i < starts.Length; ++i) {
            Program.BruteSegment(boxes, starts[i], ends[i], expected);
            index.QuerySegment(starts[i], ends[i], actual);
            Program.EqualSets(expected, actual, "closed segment");
            Assert.NotEmpty(actual);
            Assert.DoesNotContain(1, actual);
        }
    }

    [Fact]
    public void HeightUsesFirstIdAndRejectsDegenerateOrOutsideTriangles()
    {
        var t = new Triangle3d(new Vector3d(0, 0, 10), new Vector3d(4, 0, 10), new Vector3d(0, 4, 10));
        var triangles = new[] { new Triangle3d(Vector3d.Zero, Vector3d.Zero, Vector3d.Zero), t,
            new Triangle3d(new Vector3d(0, 0, 20), new Vector3d(4, 0, 20), new Vector3d(0, 4, 20)) };
        var index = new StrtreeComparison(triangles.Select(Program.Bounds).ToArray(), triangles);
        foreach (Vector2d p in new[] { Vector2d.Zero, new Vector2d(2, 2), new Vector2d(3, 3), new Vector2d(50, 50) }) {
            bool found = Program.BruteHeight(triangles, p, out double expected);
            Assert.Equal(found, index.TryGetHeight(p, out double actual));
            Assert.Equal(expected, actual);
        }
        Assert.Throws<InvalidOperationException>(() => new StrtreeComparison(new AxisAlignedBox2d[0]).TryGetHeight(Vector2d.Zero, out _));
    }

    [Fact]
    public void ResultCheckingRejectsWrongCountAndWrongIds()
    {
        Assert.Throws<Exception>(() => Program.EqualSets(new List<int> { 1 }, new List<int>(), "count"));
        Assert.Throws<Exception>(() => Program.EqualSets(new List<int> { 1 }, new List<int> { 2 }, "ids"));
    }

    [Fact]
    public void CliIncludesFullAndPrefixRowsAndIsRepeatable()
    {
        TextWriter original = Console.Out;
        try {
            string prior = null;
            for (int repeat = 0; repeat < 2; ++repeat) {
                var text = new StringWriter();
                Console.SetOut(text);
                Assert.Equal(0, Program.Main(new[] { "4", "12", "6" }));
                string output = text.ToString();
                Assert.Contains("current_thread_allocated_bytes_per_call", output);
                Assert.Contains("build,strtree,1,", output);
                foreach (string operation in new[] { "rectangle", "segment", "nearest", "height" }) {
                    Assert.Contains(operation + ",strtree,12,", output);
                    Assert.Contains(operation + ",strtree_prefix,6,", output);
                    Assert.Contains(operation + ",quadtree_prefix,6,", output);
                }
                string validation = output.Split('\n').Single(line => line.StartsWith("validated_queries="));
                if (prior != null) Assert.Equal(prior, validation);
                prior = validation;
            }
        } finally { Console.SetOut(original); }
    }
}
