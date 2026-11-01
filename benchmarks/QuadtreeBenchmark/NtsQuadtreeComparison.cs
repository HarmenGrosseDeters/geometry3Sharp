using System;
using System.Collections.Generic;
using g3;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index;
using NetTopologySuite.Index.Quadtree;

// Benchmark-only public API adapter. Reuse is serial; inputs remain immutable.
internal sealed class NtsQuadtreeComparison : IItemVisitor<int>
{
    private readonly Quadtree<int> tree = new Quadtree<int>();
    private readonly AxisAlignedBox2d[] boxes;
    private readonly Triangle3d[] triangles;
    private readonly Envelope query = new Envelope();
    private readonly List<int> candidates = new List<int>(256);
    private readonly double initialRadius;
    private List<int> output;
    private Vector2d start, end;
    private bool segment;

    internal NtsQuadtreeComparison(AxisAlignedBox2d[] boxes, Triangle3d[] triangles = null)
    {
        this.boxes = boxes;
        this.triangles = triangles;
        if (triangles != null && triangles.Length != boxes.Length)
            throw new ArgumentException("Triangle and box IDs must match.");
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        for (int i = 0; i < boxes.Length; ++i) {
            AxisAlignedBox2d b = boxes[i];
            tree.Insert(new Envelope(b.Min.x, b.Max.x, b.Min.y, b.Max.y), i);
            minX = Math.Min(minX, b.Min.x); minY = Math.Min(minY, b.Min.y);
            maxX = Math.Max(maxX, b.Max.x); maxY = Math.Max(maxY, b.Max.y);
        }
        double spacing = boxes.Length == 0 ? 0 : Math.Max(maxX - minX, maxY - minY) / Math.Sqrt(boxes.Length);
        initialRadius = spacing > 0 ? spacing : 1;
    }

    internal void QueryRectangle(AxisAlignedBox2d rectangle, List<int> results)
    {
        segment = false;
        output = results;
        results.Clear();
        query.Init(rectangle.Min.x, rectangle.Max.x, rectangle.Min.y, rectangle.Max.y);
        tree.Query(query, this);
    }

    internal void QuerySegment(Vector2d a, Vector2d b, List<int> results)
    {
        segment = true;
        start = a; end = b;
        output = results;
        results.Clear();
        query.Init(Math.Min(a.x, b.x) - Program.SegmentTolerance, Math.Max(a.x, b.x) + Program.SegmentTolerance,
            Math.Min(a.y, b.y) - Program.SegmentTolerance, Math.Max(a.y, b.y) + Program.SegmentTolerance);
        tree.Query(query, this);
    }

    public void VisitItem(int id)
    {
        AxisAlignedBox2d b = boxes[id];
        // MX-CIF queries include ancestor-node false positives: filter original item bounds, not padded NTS bounds.
        if (b.Min.x <= query.MaxX && b.Max.x >= query.MinX && b.Min.y <= query.MaxY && b.Max.y >= query.MinY &&
            (!segment || Program.SegmentIntersects(b, start, end))) output.Add(id);
    }

    internal int FindNearest(Vector2d point)
    {
        if (boxes.Length == 0) return -1;
        QueryRectangle(new AxisAlignedBox2d(point, point), candidates);
        double radius = initialRadius;
        // NTS Quadtree has no nearest API. Expand range queries to obtain an upper bound, then resolve all ties.
        while (candidates.Count == 0) {
            QuerySquare(point, radius);
            radius *= 2;
        }
        int best = candidates[0];
        double bestDistance = Program.BoxDistanceSquared(boxes[best], point);
        SelectNearest(point, ref best, ref bestDistance);
        QuerySquare(point, NextUp(Math.Sqrt(bestDistance)));
        SelectNearest(point, ref best, ref bestDistance);
        return best;
    }

    private void QuerySquare(Vector2d point, double radius)
    {
        QueryRectangle(new AxisAlignedBox2d(NextDown(point.x - radius), NextDown(point.y - radius),
            NextUp(point.x + radius), NextUp(point.y + radius)), candidates);
    }

    private void SelectNearest(Vector2d point, ref int best, ref double bestDistance)
    {
        foreach (int id in candidates) {
            double distance = Program.BoxDistanceSquared(boxes[id], point);
            if (distance < bestDistance || (distance == bestDistance && id < best)) {
                best = id; bestDistance = distance;
            }
        }
    }

    internal bool TryGetHeight(Vector2d point, out double height)
    {
        if (triangles == null) throw new InvalidOperationException("Height requires triangles with matching IDs.");
        QueryRectangle(new AxisAlignedBox2d(point, point), candidates);
        candidates.Sort();
        foreach (int id in candidates)
            if (Program.TriangleHeight(triangles[id], point, out height)) return true;
        height = 0;
        return false;
    }

    private static double NextUp(double value)
    {
        if (value == double.PositiveInfinity) return value;
        if (value == 0) return double.Epsilon;
        long bits = BitConverter.DoubleToInt64Bits(value);
        return BitConverter.Int64BitsToDouble(value > 0 ? bits + 1 : bits - 1);
    }

    private static double NextDown(double value) => -NextUp(-value);
}
