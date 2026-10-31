using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Snapshot index of closed XY segments, by their endpoints. A Segment3d keeps a center, a normalized direction and
    /// an extent, so its P0 and P1 are rounded, depending on z too: build from endpoints when queries have to hit them exactly.
    /// </summary>
    public sealed class SegmentQuadtree2d
    {
        struct Endpoints
        {
            internal Vector3d A, B;
            internal Endpoints(Vector3d a, Vector3d b) { A = a; B = b; }
            internal Endpoints(Segment3d segment) { A = segment.P0; B = segment.P1; }
            internal Vector2d XYA { get { return QuadtreeGeometry2d.XY(A); } }
            internal Vector2d XYB { get { return QuadtreeGeometry2d.XY(B); } }
        }

        readonly List<Endpoints> items;
        readonly LooseQuadtree2d tree;

        public SegmentQuadtree2d() : this(new Segment3d[0]) { }
        public SegmentQuadtree2d(IReadOnlyList<Segment3d> segments, bool parallelBuild = true)
        {
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            items = new List<Endpoints>(segments.Count);
            var bounds = new AxisAlignedBox2d[segments.Count];
            for (int i = 0; i < segments.Count; ++i) {
                Segment3d item = segments[i];
                bounds[i] = QuadtreeGeometry2d.Bounds(item);
                items.Add(new Endpoints(item));
            }
            tree = new LooseQuadtree2d(bounds, parallelBuild);
        }
        /// <summary>One segment per edge, between two of the given vertices. IDs are positions in the edge list.</summary>
        public SegmentQuadtree2d(IReadOnlyList<Vector3d> vertices, IReadOnlyList<Index2i> edges, bool parallelBuild = true)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (edges == null) throw new ArgumentNullException(nameof(edges));
            items = new List<Endpoints>(edges.Count);
            var bounds = new AxisAlignedBox2d[edges.Count];
            for (int i = 0; i < edges.Count; ++i) {
                Vector3d a = vertices[edges[i].a], b = vertices[edges[i].b];
                bounds[i] = QuadtreeGeometry2d.Bounds(a, b);
                items.Add(new Endpoints(a, b));
            }
            tree = new LooseQuadtree2d(bounds, parallelBuild);
        }
        public int Count { get { return tree.Count; } }
        public AxisAlignedBox2d Extent { get { return tree.Extent; } }
        public bool ContainsId(int id) { return tree.ContainsId(id); }
        /// <summary>A segment rebuilt from the stored endpoints, so rounded. GetEndpoints returns them as they are.</summary>
        public Segment3d GetItem(int id)
        {
            if (!ContainsId(id)) throw new ArgumentOutOfRangeException(nameof(id));
            return new Segment3d(items[id].A, items[id].B);
        }
        public void GetEndpoints(int id, out Vector3d a, out Vector3d b)
        {
            if (!ContainsId(id)) throw new ArgumentOutOfRangeException(nameof(id));
            a = items[id].A;
            b = items[id].B;
        }
        public AxisAlignedBox2d GetBounds(int id) { return tree.GetBounds(id); }
        public int Insert(Segment3d item)
        {
            return Add(QuadtreeGeometry2d.Bounds(item), new Endpoints(item));
        }
        public int Insert(Vector3d a, Vector3d b)
        {
            return Add(QuadtreeGeometry2d.Bounds(a, b), new Endpoints(a, b));
        }
        int Add(AxisAlignedBox2d bounds, Endpoints item)
        {
            if (items.Count == items.Capacity) items.Capacity = checked(items.Count + Math.Max(4, items.Count));
            int id = tree.Insert(bounds);
            items.Add(item);
            return id;
        }
        public bool Remove(int id) { return tree.Remove(id); }
        public bool Remove(Segment3d item)
        {
            return RemoveFirst(QuadtreeGeometry2d.Bounds(item), new Endpoints(item));
        }
        public bool Remove(Vector3d a, Vector3d b)
        {
            return RemoveFirst(QuadtreeGeometry2d.Bounds(a, b), new Endpoints(a, b));
        }
        bool RemoveFirst(AxisAlignedBox2d bounds, Endpoints item)
        {
            var candidates = new List<int>();
            tree.QueryRectangle(bounds, candidates);
            int first = -1;
            foreach (int id in candidates)
                if (items[id].A == item.A && items[id].B == item.B && (first < 0 || id < first)) first = id;
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
            internal List<Endpoints> Items;
            internal Vector2d Point;
            public double Distance(int id)
            {
                Endpoints segment = Items[id];
                return QuadtreeGeometry2d.SegmentDistance(Point, segment.XYA, segment.XYB);
            }
            public int CompareOverflow(int a, int b)
            {
                return QuadtreeExactQuery2d.SegmentSquared(Point, Items[a].XYA, Items[a].XYB).CompareTo(
                    QuadtreeExactQuery2d.SegmentSquared(Point, Items[b].XYA, Items[b].XYB));
            }
        }
        struct PointVisitor : IQuadtreeVisitor2d
        {
            internal List<Endpoints> Items;
            internal Vector2d Point;
            internal double Tolerance;
            internal List<int> Results;
            internal bool ExcludeEndpoints, Stop;
            public bool TestRegion(AxisAlignedBox2d region) { return QuadtreeGeometry2d.NearRegion(region, Point, Tolerance); }
            public bool Visit(int id)
            {
                Vector2d a = Items[id].XYA, b = Items[id].XYB;
                if (ExcludeEndpoints && (QuadtreeGeometry2d.WithinPoint(Point, a, Tolerance) ||
                    QuadtreeGeometry2d.WithinPoint(Point, b, Tolerance))) return false;
                bool hit = Tolerance == 0 ? QuadtreeGeometry2d.OnSegment(Point, a, b) :
                    QuadtreeGeometry2d.WithinSegment(Point, a, b, Tolerance);
                if (!hit) return false;
                Results.Add(id);
                return Stop;
            }
        }
        struct IntersectionVisitor : IQuadtreeVisitor2d
        {
            internal List<Endpoints> Items;
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
                if (QuadtreeGeometry2d.Intersects(A, B, Items[id].XYA, Items[id].XYB))
                    Results.Add(id);
                return false;
            }
        }
    }
}
