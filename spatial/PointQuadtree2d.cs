using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>Mutable XY point index owning snapshots, with stable insertion IDs. Queries may run concurrently.</summary>
    public sealed class PointQuadtree2d
    {
        readonly List<Vector3d> items;
        readonly LooseQuadtree2d tree;

        public PointQuadtree2d() : this(new Vector3d[0]) { }
        public PointQuadtree2d(IReadOnlyList<Vector3d> points, bool parallelBuild = true)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            items = new List<Vector3d>(points.Count);
            var bounds = new AxisAlignedBox2d[points.Count];
            for (int i = 0; i < points.Count; ++i) {
                Vector3d item = points[i];
                bounds[i] = QuadtreeGeometry2d.Bounds(item);
                items.Add(item);
            }
            tree = new LooseQuadtree2d(bounds, parallelBuild);
        }
        public int Count { get { return tree.Count; } }
        public AxisAlignedBox2d Extent { get { return tree.Extent; } }
        public bool ContainsId(int id) { return tree.ContainsId(id); }
        public Vector3d GetItem(int id)
        {
            if (!ContainsId(id)) throw new ArgumentOutOfRangeException(nameof(id));
            return items[id];
        }
        public int Insert(Vector3d item)
        {
            AxisAlignedBox2d bounds = QuadtreeGeometry2d.Bounds(item);
            if (items.Count == items.Capacity) items.Capacity = checked(items.Count + Math.Max(4, items.Count));
            int id = tree.Insert(bounds);
            items.Add(item);
            return id;
        }
        public bool Remove(int id) { return tree.Remove(id); }
        public bool Remove(Vector3d item)
        {
            QuadtreeGeometry2d.Validate(item);
            for (int i = 0; i < items.Count; ++i)
                if (ContainsId(i) && items[i] == item) return tree.Remove(i);
            return false;
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
        public void QueryNearby(Vector2d point, double tolerance, List<int> results)
        {
            QuadtreeGeometry2d.ValidateQuery(point, tolerance, results);
            results.Clear();
            var visitor = new NearbyVisitor { Items = items, Point = point, Tolerance = tolerance, Results = results };
            tree.Traverse(ref visitor);
        }
        struct DistanceMetric : IQuadtreeDistance2d, IQuadtreeOverflowDistance2d
        {
            internal List<Vector3d> Items;
            internal Vector2d Point;
            public double Distance(int id) { return QuadtreeGeometry2d.PointDistance(Point, QuadtreeGeometry2d.XY(Items[id])); }
            public int CompareOverflow(int a, int b)
            {
                return QuadtreeExactQuery2d.PointSquared(Point, QuadtreeGeometry2d.XY(Items[a])).CompareTo(
                    QuadtreeExactQuery2d.PointSquared(Point, QuadtreeGeometry2d.XY(Items[b])));
            }
        }
        struct NearbyVisitor : IQuadtreeVisitor2d
        {
            internal List<Vector3d> Items;
            internal Vector2d Point;
            internal double Tolerance;
            internal List<int> Results;
            public bool TestRegion(AxisAlignedBox2d region) { return QuadtreeGeometry2d.NearRegion(region, Point, Tolerance); }
            public bool Visit(int id)
            {
                if (QuadtreeGeometry2d.PointDistance(Point, QuadtreeGeometry2d.XY(Items[id])) <= Tolerance) Results.Add(id);
                return false;
            }
        }
    }
}
