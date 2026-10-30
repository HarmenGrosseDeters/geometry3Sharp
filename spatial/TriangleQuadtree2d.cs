using System;
using System.Collections.Generic;

namespace g3
{
    public struct QuadtreeHeightHit2d
    {
        public int Id;
        public double Height;
    }

    /// <summary>Snapshot index of XY triangles with affine Z interpolation. Concurrent queries own their output buffers.</summary>
    public sealed class TriangleQuadtree2d
    {
        readonly List<Triangle3d> items;
        readonly LooseQuadtree2d tree;

        public TriangleQuadtree2d() : this(new Triangle3d[0]) { }
        public TriangleQuadtree2d(IReadOnlyList<Triangle3d> triangles, bool parallelBuild = true)
        {
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            items = new List<Triangle3d>(triangles.Count);
            var bounds = new AxisAlignedBox2d[triangles.Count];
            for (int i = 0; i < triangles.Count; ++i) {
                Triangle3d item = triangles[i];
                bounds[i] = QuadtreeGeometry2d.Bounds(item);
                items.Add(item);
            }
            tree = new LooseQuadtree2d(bounds, parallelBuild);
        }
        public int Count { get { return tree.Count; } }
        public AxisAlignedBox2d Extent { get { return tree.Extent; } }
        public bool ContainsId(int id) { return tree.ContainsId(id); }
        public Triangle3d GetItem(int id)
        {
            if (!ContainsId(id)) throw new ArgumentOutOfRangeException(nameof(id));
            return items[id];
        }
        public int Insert(Triangle3d item)
        {
            AxisAlignedBox2d bounds = QuadtreeGeometry2d.Bounds(item);
            if (items.Count == items.Capacity) items.Capacity = checked(items.Count + Math.Max(4, items.Count));
            int id = tree.Insert(bounds);
            items.Add(item);
            return id;
        }
        public bool Remove(int id) { return tree.Remove(id); }
        public bool Remove(Triangle3d item)
        {
            QuadtreeGeometry2d.Bounds(item);
            for (int i = 0; i < items.Count; ++i)
                if (ContainsId(i) && QuadtreeGeometry2d.Same(items[i], item)) return tree.Remove(i);
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
        /// <summary>All exact hits, otherwise all absolute-distance tolerance hits. Degenerate triangles never yield heights.</summary>
        public void QueryHeights(Vector2d point, List<QuadtreeHeightHit2d> results,
            double tolerance = 0.01, bool stopAfterFirst = false)
        {
            QuadtreeGeometry2d.ValidateQuery(point, tolerance, results);
            results.Clear();
            var visitor = new HeightVisitor { Items = items, Tree = tree, Point = point, Results = results, Stop = stopAfterFirst };
            tree.Traverse(ref visitor);
            if (visitor.Found || tolerance == 0) return;
            visitor.Tolerance = tolerance;
            tree.Traverse(ref visitor);
        }
        /// <summary>
        /// Returns an affine height without a result-list allocation. Robust numeric fallbacks may allocate.
        /// A height outside the finite double range throws ArithmeticException. Classification is exact;
        /// interpolated heights are rounded constructions.
        /// </summary>
        public bool TryGetHeight(Vector2d point, out double height, double tolerance = 0.01)
        {
            QuadtreeGeometry2d.Validate(point);
            QuadtreeGeometry2d.ValidateTolerance(tolerance);
            var visitor = new HeightVisitor { Items = items, Tree = tree, Point = point, Stop = true };
            tree.Traverse(ref visitor);
            if (!visitor.Found && tolerance > 0) {
                visitor.Tolerance = tolerance;
                tree.Traverse(ref visitor);
            }
            height = visitor.Height;
            return visitor.Found;
        }
        struct DistanceMetric : IQuadtreeDistance2d, IQuadtreeOverflowDistance2d
        {
            internal List<Triangle3d> Items;
            internal Vector2d Point;
            public double Distance(int id) { return QuadtreeGeometry2d.TriangleDistance(Items[id], Point); }
            public int CompareOverflow(int a, int b)
            {
                return QuadtreeExactQuery2d.TriangleSquared(Point, Items[a]).CompareTo(
                    QuadtreeExactQuery2d.TriangleSquared(Point, Items[b]));
            }
        }
        struct HeightVisitor : IQuadtreeVisitor2d
        {
            internal List<Triangle3d> Items;
            internal LooseQuadtree2d Tree;
            internal Vector2d Point;
            internal double Tolerance, Height;
            internal List<QuadtreeHeightHit2d> Results;
            internal bool Stop, Found;
            public bool TestRegion(AxisAlignedBox2d region) { return QuadtreeGeometry2d.NearRegion(region, Point, Tolerance); }
            public bool Visit(int id)
            {
                if (Tolerance == 0 && !QuadtreeGeometry2d.NearRegion(Tree.GetBounds(id), Point, 0)) return false;
                Triangle3d triangle = Items[id];
                if (Tolerance == 0) {
                    if (!QuadtreeGeometry2d.Contains(triangle, Point)) return false;
                } else {
                    Vector2d a = QuadtreeGeometry2d.XY(triangle.V0), b = QuadtreeGeometry2d.XY(triangle.V1), c = QuadtreeGeometry2d.XY(triangle.V2);
                    if (ExactPredicates.Orient2d(a, b, c) == 0 ||
                        QuadtreeGeometry2d.TriangleDistance(triangle, Point) > Tolerance) return false;
                }
                Height = QuadtreeGeometry2d.Height(triangle, Point);
                if (!QuadtreeGeometry2d.Finite(Height))
                    throw new ArithmeticException("The interpolated height is outside the finite double range.");
                Found = true;
                if (Results != null) Results.Add(new QuadtreeHeightHit2d { Id = id, Height = Height });
                return Stop;
            }
        }
    }
}
