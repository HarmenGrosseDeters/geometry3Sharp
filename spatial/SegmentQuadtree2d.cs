using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>Snapshot index of closed XY segments. Z does not affect intersections or distances.</summary>
    public sealed class SegmentQuadtree2d
    {
        readonly List<Segment3d> items;
        readonly LooseQuadtree2d tree;

        public SegmentQuadtree2d() : this(new Segment3d[0]) { }
        public SegmentQuadtree2d(IReadOnlyList<Segment3d> segments, bool parallelBuild = true)
        {
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            items = new List<Segment3d>(segments.Count);
            var bounds = new AxisAlignedBox2d[segments.Count];
            for (int i = 0; i < segments.Count; ++i) {
                Segment3d item = segments[i];
                bounds[i] = QuadtreeGeometry2d.Bounds(item);
                items.Add(item);
            }
            tree = new LooseQuadtree2d(bounds, parallelBuild);
        }
        public int Count { get { return tree.Count; } }
        public AxisAlignedBox2d Extent { get { return tree.Extent; } }
        public bool ContainsId(int id) { return tree.ContainsId(id); }
        public Segment3d GetItem(int id)
        {
            if (!ContainsId(id)) throw new ArgumentOutOfRangeException(nameof(id));
            return items[id];
        }
        public int Insert(Segment3d item)
        {
            AxisAlignedBox2d bounds = QuadtreeGeometry2d.Bounds(item);
            if (items.Count == items.Capacity) items.Capacity = checked(items.Count + Math.Max(4, items.Count));
            int id = tree.Insert(bounds);
            items.Add(item);
            return id;
        }
        public bool Remove(int id) { return tree.Remove(id); }
        public bool Remove(Segment3d item)
        {
            var candidates = new List<int>();
            tree.QueryRectangle(QuadtreeGeometry2d.Bounds(item), candidates);
            int first = -1;
            foreach (int id in candidates)
                if (QuadtreeGeometry2d.Same(items[id], item) && (first < 0 || id < first)) first = id;
            return first >= 0 && tree.Remove(first);
        }
        public void QueryRectangle(AxisAlignedBox2d rectangle, List<int> results) { tree.QueryRectangle(rectangle, results); }
        public void QuerySegment(Vector2d a, Vector2d b, List<int> results) { tree.QuerySegment(a, b, results); }
        public bool Traverse<TVisitor>(ref TVisitor visitor) where TVisitor : struct, IQuadtreeVisitor2d
        {
            return tree.Traverse(ref visitor);
        }
        public int FindNearest(Vector2d point, double maxDistance = double.PositiveInfinity)
        {
            var metric = new DistanceMetric { Items = items, Point = point };
            return tree.FindNearest(point, ref metric, maxDistance);
        }
        public void FindNearestK(Vector2d point, int k, List<int> results, double maxDistance = double.PositiveInfinity)
        {
            var metric = new DistanceMetric { Items = items, Point = point };
            tree.FindNearestK(point, k, results, ref metric, maxDistance);
        }
        /// <summary>Complete absolute-distance query, optionally excluding points within tolerance of either endpoint.</summary>
        public void QueryPoint(Vector2d point, double tolerance, List<int> results,
            bool excludeEndpoints = false, bool stopAfterFirst = false)
        {
            QuadtreeGeometry2d.ValidateQuery(point, tolerance, results);
            results.Clear();
            var visitor = new PointVisitor { Items = items, Point = point, Tolerance = tolerance,
                Results = results, ExcludeEndpoints = excludeEndpoints, Stop = stopAfterFirst };
            tree.Traverse(ref visitor);
        }
        /// <summary>Exact inclusive intersection classification, including duplicates, points and collinear overlap.</summary>
        public void QueryIntersections(Vector2d a, Vector2d b, List<int> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            QuadtreeGeometry2d.Validate(a);
            QuadtreeGeometry2d.Validate(b);
            results.Clear();
            var visitor = new IntersectionVisitor { Items = items, A = a, B = b,
                Bounds = new AxisAlignedBox2d(a, b), Results = results };
            tree.Traverse(ref visitor);
        }
        struct DistanceMetric : IQuadtreeDistance2d, IQuadtreeOverflowDistance2d
        {
            internal List<Segment3d> Items;
            internal Vector2d Point;
            public double Distance(int id)
            {
                Segment3d segment = Items[id];
                return QuadtreeGeometry2d.SegmentDistance(Point, QuadtreeGeometry2d.XY(segment.P0), QuadtreeGeometry2d.XY(segment.P1));
            }
            public int CompareOverflow(int a, int b)
            {
                return QuadtreeExactQuery2d.SegmentSquared(Point, QuadtreeGeometry2d.XY(Items[a].P0),
                    QuadtreeGeometry2d.XY(Items[a].P1)).CompareTo(QuadtreeExactQuery2d.SegmentSquared(Point,
                    QuadtreeGeometry2d.XY(Items[b].P0), QuadtreeGeometry2d.XY(Items[b].P1)));
            }
        }
        struct PointVisitor : IQuadtreeVisitor2d
        {
            internal List<Segment3d> Items;
            internal Vector2d Point;
            internal double Tolerance;
            internal List<int> Results;
            internal bool ExcludeEndpoints, Stop;
            public bool TestRegion(AxisAlignedBox2d region) { return QuadtreeGeometry2d.NearRegion(region, Point, Tolerance); }
            public bool Visit(int id)
            {
                Segment3d segment = Items[id];
                Vector2d a = QuadtreeGeometry2d.XY(segment.P0), b = QuadtreeGeometry2d.XY(segment.P1);
                if (ExcludeEndpoints && (QuadtreeGeometry2d.PointDistance(Point, a) <= Tolerance ||
                    QuadtreeGeometry2d.PointDistance(Point, b) <= Tolerance)) return false;
                bool hit = Tolerance == 0 ? QuadtreeGeometry2d.OnSegment(Point, a, b) :
                    QuadtreeGeometry2d.SegmentDistance(Point, a, b) <= Tolerance;
                if (!hit) return false;
                Results.Add(id);
                return Stop;
            }
        }
        struct IntersectionVisitor : IQuadtreeVisitor2d
        {
            internal List<Segment3d> Items;
            internal Vector2d A, B;
            internal AxisAlignedBox2d Bounds;
            internal List<int> Results;
            public bool TestRegion(AxisAlignedBox2d region)
            {
                return region.Min.x <= Bounds.Max.x && region.Max.x >= Bounds.Min.x &&
                    region.Min.y <= Bounds.Max.y && region.Max.y >= Bounds.Min.y;
            }
            public bool Visit(int id)
            {
                Segment3d segment = Items[id];
                if (QuadtreeGeometry2d.Intersects(A, B, QuadtreeGeometry2d.XY(segment.P0), QuadtreeGeometry2d.XY(segment.P1)))
                    Results.Add(id);
                return false;
            }
        }
    }
}
