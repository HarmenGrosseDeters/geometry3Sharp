using System;
using System.Collections.Generic;
using g3;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index;
using NetTopologySuite.Index.Strtree;

// Benchmark-only public API adapter. One instance is reused serially, not concurrently.
internal sealed class StrtreeComparison : IItemVisitor<int>, IItemDistance<Envelope, int>
{
    private readonly STRtree<int> tree = new STRtree<int>();
    private readonly AxisAlignedBox2d[] boxes;
    private readonly Triangle3d[] triangles;
    private readonly Envelope query = new Envelope();
    private readonly List<int> candidates = new List<int>(256);
    private List<int> output;
    private Vector2d start, end;
    private bool segment;

    internal StrtreeComparison(AxisAlignedBox2d[] boxes, Triangle3d[] triangles = null)
    {
        this.boxes = boxes;
        this.triangles = triangles;
        if (triangles != null && triangles.Length != boxes.Length)
            throw new ArgumentException("Triangle and box IDs must match.");
        for (int i = 0; i < boxes.Length; ++i) {
            AxisAlignedBox2d b = boxes[i];
            tree.Insert(new Envelope(b.Min.x, b.Max.x, b.Min.y, b.Max.y), i);
        }
        tree.Build(); // Include envelopes, inserts and the lazy packed build in construction timing.
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
        if (!segment || Program.SegmentIntersects(boxes[id], start, end)) output.Add(id);
    }

    public double Distance(IBoundable<Envelope, int> a, IBoundable<Envelope, int> b)
    {
        int id = a.Item == -1 ? b.Item : a.Item;
        return Math.Sqrt(Program.BoxDistanceSquared(boxes[id], start));
    }

    internal int FindNearest(Vector2d point)
    {
        if (boxes.Length == 0) return -1;
        start = point;
        query.Init(point.x, point.x, point.y, point.y);
        int best = tree.NearestNeighbour(query, -1, this);
        double bestDistance = Program.BoxDistanceSquared(boxes[best], point);
        double radius = Math.Sqrt(bestDistance);
        // NTS does not specify stable ties. Query only the nearest-distance square, then order by squared distance/ID.
        // Outward rounding prevents a tied envelope on the square boundary from being dropped.
        radius = NextUp(radius);
        query.Init(NextDown(point.x - radius), NextUp(point.x + radius),
            NextDown(point.y - radius), NextUp(point.y + radius));
        segment = false;
        output = candidates;
        candidates.Clear();
        tree.Query(query, this);
        foreach (int id in candidates) {
            double distance = Program.BoxDistanceSquared(boxes[id], point);
            if (distance < bestDistance || (distance == bestDistance && id < best)) {
                best = id; bestDistance = distance;
            }
        }
        return best;
    }

    internal bool TryGetHeight(Vector2d point, out double height)
    {
        if (triangles == null) throw new InvalidOperationException("Height requires triangles with matching IDs.");
        QueryRectangle(new AxisAlignedBox2d(point, point), candidates);
        candidates.Sort(); // Match brute-force first-containing-triangle ID, including shared edges/overlaps.
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
