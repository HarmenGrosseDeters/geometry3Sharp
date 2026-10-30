using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using g3;

internal static class Program
{
    private static long checksum;

    internal static int Main(string[] args)
    {
        checksum = 0;
        try {
            int cells = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 64;
            int queries = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 256;
            int baselineQueries = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : queries;
            if (cells <= 0 || queries <= 0 || baselineQueries <= 0 || baselineQueries > queries)
                throw new ArgumentException("Use: QuadtreeBenchmark [positive cells] [positive queries] [baseline queries <= queries]");
            Triangle3d[] triangles = Generate(cells);
            var boxes = new AxisAlignedBox2d[triangles.Length];
            for (int i = 0; i < boxes.Length; ++i) boxes[i] = Bounds(triangles[i]);
            var random = new Random(7);
            var points = new Vector2d[queries];
            var ends = new Vector2d[queries];
            var rectangles = new AxisAlignedBox2d[queries];
            for (int i = 0; i < queries; ++i) {
                points[i] = new Vector2d(2539000 + random.NextDouble() * cells * 7.07,
                    5640000 + random.NextDouble() * cells * 7.07);
                ends[i] = points[i] + new Vector2d(200 * random.NextDouble() - 100, 200 * random.NextDouble() - 100);
                rectangles[i] = new AxisAlignedBox2d(points[i], points[i] + new Vector2d(20, 20));
            }
            Console.WriteLine("runtime=" + Environment.Version + "; processors=" + Environment.ProcessorCount +
                "; cells=" + cells + "; items=" + boxes.Length + "; queries=" + queries +
                "; baseline_queries=" + baselineQueries);
            Console.WriteLine("operation,variant,calls,microseconds_per_call,current_thread_allocated_bytes_per_call");
            // Warm up construction separately from the measured data set.
            GC.KeepAlive(new LooseQuadtree2d(new[] { boxes[0] }));
            LooseQuadtree2d sequential = null, parallel = null;
            Measure("build", "sequential", 1, i => sequential = new LooseQuadtree2d(boxes, false));
            long beforeMemory = GC.GetTotalMemory(true);
            Measure("build", "parallel", 1, i => parallel = new LooseQuadtree2d(boxes, true));
            long afterMemory = GC.GetTotalMemory(true);
            Console.WriteLine("index_retained_bytes=" + Math.Max(0, afterMemory - beforeMemory));
            TriangleQuadtree2d heightIndex = null;
            Measure("build", "triangle_adapter", 1, i => heightIndex = new TriangleQuadtree2d(triangles));
            GC.KeepAlive(new StrtreeComparison(new[] { boxes[0] }));
            StrtreeComparison strtree = null;
            Measure("build", "strtree", 1, i => strtree = new StrtreeComparison(boxes, triangles));
            GC.KeepAlive(new NtsQuadtreeComparison(new[] { boxes[0] }));
            NtsQuadtreeComparison ntsQuadtree = null;
            Measure("build", "nts_quadtree", 1, i => ntsQuadtree = new NtsQuadtreeComparison(boxes, triangles));
            var actual = new List<int>(256);
            var expected = new List<int>(256);
            var sequentialResults = new List<int>(256);
            for (int i = 0; i < baselineQueries; ++i) {
                BruteRectangle(boxes, rectangles[i], expected);
                parallel.QueryRectangle(rectangles[i], actual);
                EqualSets(expected, actual, "rectangle");
                sequential.QueryRectangle(rectangles[i], sequentialResults);
                EqualSets(expected, sequentialResults, "sequential rectangle");
                strtree.QueryRectangle(rectangles[i], actual);
                EqualSets(expected, actual, "STRtree rectangle");
                ntsQuadtree.QueryRectangle(rectangles[i], actual);
                EqualSets(expected, actual, "NTS Quadtree rectangle");
                BruteSegment(boxes, points[i], ends[i], expected);
                parallel.QuerySegment(points[i], ends[i], actual);
                EqualSets(expected, actual, "segment");
                sequential.QuerySegment(points[i], ends[i], sequentialResults);
                EqualSets(expected, sequentialResults, "sequential segment");
                strtree.QuerySegment(points[i], ends[i], actual);
                EqualSets(expected, actual, "STRtree segment");
                ntsQuadtree.QuerySegment(points[i], ends[i], actual);
                EqualSets(expected, actual, "NTS Quadtree segment");
                int nearest = BruteNearest(boxes, points[i]);
                if (parallel.FindNearest(points[i]) != nearest || sequential.FindNearest(points[i]) != nearest)
                    throw new Exception("nearest mismatch");
                if (strtree.FindNearest(points[i]) != nearest) throw new Exception("STRtree nearest mismatch");
                if (ntsQuadtree.FindNearest(points[i]) != nearest) throw new Exception("NTS Quadtree nearest mismatch");
                double reference;
                double indexed;
                bool foundReference = BruteHeight(triangles, points[i], out reference);
                bool foundIndexed = heightIndex.TryGetHeight(points[i], out indexed, 0);
                if (foundReference != foundIndexed || (foundReference && Math.Abs(reference - indexed) > 1e-8))
                    throw new Exception("height mismatch");
                foundIndexed = strtree.TryGetHeight(points[i], out indexed);
                if (foundReference != foundIndexed || (foundReference && reference != indexed))
                    throw new Exception("STRtree height mismatch");
                foundIndexed = ntsQuadtree.TryGetHeight(points[i], out indexed);
                if (foundReference != foundIndexed || (foundReference && reference != indexed))
                    throw new Exception("NTS Quadtree height mismatch");
            }
            // Warm every query and grow the result buffer before allocation/timing samples.
            for (int i = 0; i < queries; ++i) {
                parallel.QueryRectangle(rectangles[i], actual);
                parallel.QuerySegment(points[i], ends[i], actual);
                parallel.FindNearest(points[i]);
                heightIndex.TryGetHeight(points[i], out _, 0);
                strtree.QueryRectangle(rectangles[i], actual);
                strtree.QuerySegment(points[i], ends[i], actual);
                strtree.FindNearest(points[i]);
                strtree.TryGetHeight(points[i], out _);
                ntsQuadtree.QueryRectangle(rectangles[i], actual);
                ntsQuadtree.QuerySegment(points[i], ends[i], actual);
                ntsQuadtree.FindNearest(points[i]);
                ntsQuadtree.TryGetHeight(points[i], out _);
            }
            for (int repeat = 0; repeat < 4; ++repeat) {
                Console.WriteLine("sample=" + repeat);
                Action indexed = () => {
                    if (baselineQueries != queries) {
                        Measure("rectangle", "quadtree_prefix", baselineQueries, i => { parallel.QueryRectangle(rectangles[i], actual); checksum += actual.Count; });
                        Measure("segment", "quadtree_prefix", baselineQueries, i => { parallel.QuerySegment(points[i], ends[i], actual); checksum += actual.Count; });
                        Measure("nearest", "quadtree_prefix", baselineQueries, i => checksum += parallel.FindNearest(points[i]));
                        Measure("height", "quadtree_prefix", baselineQueries, i => { if (heightIndex.TryGetHeight(points[i], out double h, 0)) checksum += (long)h; });
                    }
                    Measure("rectangle", "quadtree", queries, i => { parallel.QueryRectangle(rectangles[i], actual); checksum += actual.Count; });
                    Measure("segment", "quadtree", queries, i => { parallel.QuerySegment(points[i], ends[i], actual); checksum += actual.Count; });
                    Measure("nearest", "quadtree", queries, i => checksum += parallel.FindNearest(points[i]));
                    Measure("height", "quadtree", queries, i => { if (heightIndex.TryGetHeight(points[i], out double h, 0)) checksum += (long)h; });
                };
                Action baseline = () => {
                    Measure("rectangle", "brute_force", baselineQueries, i => { BruteRectangle(boxes, rectangles[i], expected); checksum += expected.Count; });
                    Measure("segment", "brute_force", baselineQueries, i => { BruteSegment(boxes, points[i], ends[i], expected); checksum += expected.Count; });
                    Measure("nearest", "brute_force", baselineQueries, i => checksum += BruteNearest(boxes, points[i]));
                    Measure("height", "brute_force", baselineQueries, i => { if (BruteHeight(triangles, points[i], out double h)) checksum += (long)h; });
                };
                Action packed = () => {
                    if (baselineQueries != queries)
                        MeasureStrtree(strtree, "strtree_prefix", baselineQueries, rectangles, points, ends, actual);
                    MeasureStrtree(strtree, "strtree", queries, rectangles, points, ends, actual);
                };
                Action publicQuad = () => {
                    if (baselineQueries != queries)
                        MeasureNtsQuadtree(ntsQuadtree, "nts_quadtree_prefix", baselineQueries, rectangles, points, ends, actual);
                    MeasureNtsQuadtree(ntsQuadtree, "nts_quadtree", queries, rectangles, points, ends, actual);
                };
                // Rotate all four contenders so each occupies every timing position.
                if (repeat == 0) { baseline(); indexed(); packed(); publicQuad(); }
                else if (repeat == 1) { indexed(); packed(); publicQuad(); baseline(); }
                else if (repeat == 2) { packed(); publicQuad(); baseline(); indexed(); }
                else { publicQuad(); baseline(); indexed(); packed(); }
            }
            Console.WriteLine("validated_queries=" + baselineQueries + "; checksum=" + checksum);
            GC.KeepAlive(sequential);
            GC.KeepAlive(parallel);
            GC.KeepAlive(boxes);
            return 0;
        } catch (Exception e) {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    private static void MeasureNtsQuadtree(NtsQuadtreeComparison index, string variant, int calls,
        AxisAlignedBox2d[] rectangles, Vector2d[] points, Vector2d[] ends, List<int> results)
    {
        Measure("rectangle", variant, calls, i => { index.QueryRectangle(rectangles[i], results); checksum += results.Count; });
        Measure("segment", variant, calls, i => { index.QuerySegment(points[i], ends[i], results); checksum += results.Count; });
        Measure("nearest", variant, calls, i => checksum += index.FindNearest(points[i]));
        Measure("height", variant, calls, i => { if (index.TryGetHeight(points[i], out double h)) checksum += (long)h; });
    }

    private static void MeasureStrtree(StrtreeComparison index, string variant, int calls,
        AxisAlignedBox2d[] rectangles, Vector2d[] points, Vector2d[] ends, List<int> results)
    {
        Measure("rectangle", variant, calls, i => { index.QueryRectangle(rectangles[i], results); checksum += results.Count; });
        Measure("segment", variant, calls, i => { index.QuerySegment(points[i], ends[i], results); checksum += results.Count; });
        Measure("nearest", variant, calls, i => checksum += index.FindNearest(points[i]));
        Measure("height", variant, calls, i => { if (index.TryGetHeight(points[i], out double h)) checksum += (long)h; });
    }

    private static void Measure(string operation, string variant, int calls, Action<int> action)
    {
        long startAllocation = Allocated();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < calls; ++i) action(i);
        long elapsed = Stopwatch.GetTimestamp() - start;
        long allocation = Allocated() - startAllocation;
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3:F3},{4}", operation, variant, calls,
            elapsed * 1000000.0 / Stopwatch.Frequency / calls,
            startAllocation < 0 ? "unavailable" : ((double)allocation / calls).ToString("F3", CultureInfo.InvariantCulture)));
    }

    private static long Allocated()
    {
#if NET8_0_OR_GREATER
        return GC.GetAllocatedBytesForCurrentThread();
#else
        return -1;
#endif
    }

    internal static Triangle3d[] Generate(int cells)
    {
        var random = new Random(1);
        int side = checked(cells + 1);
        var points = new Vector3d[checked(side * side)];
        for (int y = 0; y < side; ++y) {
            for (int x = 0; x < side; ++x) {
                bool border = x == 0 || y == 0 || x == cells || y == cells;
                double dx = border ? 0 : (random.NextDouble() * 0.3 - 0.15) * 7.07;
                double dy = border ? 0 : (random.NextDouble() * 0.3 - 0.15) * 7.07;
                points[y * side + x] = new Vector3d(2539000 + x * 7.07 + dx,
                    5640000 + y * 7.07 + dy, random.NextDouble() * 100);
            }
        }
        var triangles = new Triangle3d[checked(2 * cells * cells)];
        int t = 0;
        for (int y = 0; y < cells; ++y) {
            for (int x = 0; x < cells; ++x) {
                Vector3d a = points[y * side + x], b = points[y * side + x + 1];
                Vector3d c = points[(y + 1) * side + x + 1], d = points[(y + 1) * side + x];
                triangles[t++] = new Triangle3d(a, b, c);
                triangles[t++] = new Triangle3d(a, c, d);
            }
        }
        for (int i = triangles.Length - 1; i > 0; --i) {
            int j = random.Next(i + 1);
            Triangle3d swap = triangles[i]; triangles[i] = triangles[j]; triangles[j] = swap;
        }
        return triangles;
    }

    internal static AxisAlignedBox2d Bounds(Triangle3d t)
    {
        return new AxisAlignedBox2d(Math.Min(t.V0.x, Math.Min(t.V1.x, t.V2.x)),
            Math.Min(t.V0.y, Math.Min(t.V1.y, t.V2.y)), Math.Max(t.V0.x, Math.Max(t.V1.x, t.V2.x)),
            Math.Max(t.V0.y, Math.Max(t.V1.y, t.V2.y)));
    }

    internal static void BruteRectangle(AxisAlignedBox2d[] boxes, AxisAlignedBox2d query, List<int> results)
    {
        results.Clear();
        for (int i = 0; i < boxes.Length; ++i) {
            AxisAlignedBox2d b = boxes[i];
            if (b.Min.x <= query.Max.x && b.Max.x >= query.Min.x && b.Min.y <= query.Max.y && b.Max.y >= query.Min.y)
                results.Add(i);
        }
    }

    private static bool Clip(double origin, double direction, double min, double max, ref double enter, ref double exit)
    {
        if (direction == 0) return min <= origin && origin <= max;
        double a = (min - origin) / direction, b = (max - origin) / direction;
        if (a > b) { double swap = a; a = b; b = swap; }
        enter = Math.Max(enter, a); exit = Math.Min(exit, b);
        return enter <= exit;
    }

    internal static void BruteSegment(AxisAlignedBox2d[] boxes, Vector2d a, Vector2d b, List<int> results)
    {
        results.Clear();
        for (int i = 0; i < boxes.Length; ++i) {
            if (SegmentIntersects(boxes[i], a, b)) results.Add(i);
        }
    }

    internal static int BruteNearest(AxisAlignedBox2d[] boxes, Vector2d p)
    {
        int best = -1;
        double bestDistance = double.PositiveInfinity;
        for (int i = 0; i < boxes.Length; ++i) {
            double distance = BoxDistanceSquared(boxes[i], p);
            if (distance < bestDistance) { bestDistance = distance; best = i; }
        }
        return best;
    }

    internal static bool BruteHeight(Triangle3d[] triangles, Vector2d p, out double height)
    {
        for (int i = 0; i < triangles.Length; ++i)
            if (TriangleHeight(triangles[i], p, out height)) return true;
        height = 0;
        return false;
    }

    internal static bool TriangleHeight(Triangle3d t, Vector2d p, out double height)
    {
            height = 0;
            Vector2d a = new Vector2d(t.V0.x, t.V0.y), b = new Vector2d(t.V1.x, t.V1.y), c = new Vector2d(t.V2.x, t.V2.y);
            AxisAlignedBox2d bounds = Bounds(t);
            if (p.x < bounds.Min.x || p.x > bounds.Max.x || p.y < bounds.Min.y || p.y > bounds.Max.y) return false;
            int orientation = ExactPredicates.Orient2d(a, b, c);
            if (orientation == 0 || ExactPredicates.Orient2d(a, b, p) * orientation < 0 ||
                ExactPredicates.Orient2d(b, c, p) * orientation < 0 || ExactPredicates.Orient2d(c, a, p) * orientation < 0) return false;
            Vector2d ab = b - a, ac = c - a, ap = p - a;
            double denominator = ab.x * ac.y - ab.y * ac.x;
            double u = (ap.x * ac.y - ap.y * ac.x) / denominator;
            double v = (ab.x * ap.y - ab.y * ap.x) / denominator;
            height = (1 - u - v) * t.V0.z + u * t.V1.z + v * t.V2.z;
            return true;
    }

    internal const double SegmentTolerance = 0.0002;

    internal static bool SegmentIntersects(AxisAlignedBox2d box, Vector2d a, Vector2d b)
    {
        double enter = 0, exit = 1;
        return Clip(a.x, b.x - a.x, box.Min.x - SegmentTolerance, box.Max.x + SegmentTolerance, ref enter, ref exit) &&
            Clip(a.y, b.y - a.y, box.Min.y - SegmentTolerance, box.Max.y + SegmentTolerance, ref enter, ref exit);
    }

    internal static double BoxDistanceSquared(AxisAlignedBox2d box, Vector2d p)
    {
        double dx = Math.Max(0, Math.Max(box.Min.x - p.x, p.x - box.Max.x));
        double dy = Math.Max(0, Math.Max(box.Min.y - p.y, p.y - box.Max.y));
        return dx * dx + dy * dy;
    }

    internal static void EqualSets(List<int> expected, List<int> actual, string label)
    {
        expected.Sort(); actual.Sort();
        if (expected.Count != actual.Count) throw new Exception(label + " count mismatch");
        for (int i = 0; i < expected.Count; ++i)
            if (expected[i] != actual[i]) throw new Exception(label + " set mismatch");
    }
}
