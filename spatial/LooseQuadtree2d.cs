using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace g3
{
    public interface IQuadtreeVisitor2d
    {
        bool TestRegion(AxisAlignedBox2d region);
        bool Visit(int id);
    }

    /// <summary>The distance must be nonnegative and at least the query point's distance to the item's AABB.</summary>
    public interface IQuadtreeDistance2d
    {
        double Distance(int id);
    }

    /// <summary>
    /// An AABB snapshot index with stable insertion IDs. Queries may run concurrently, including nested
    /// queries; mutations require exclusive access. Caller-owned result lists are cleared by queries.
    /// Regions are recursively quartered, with twice-sized loose bounds and one storage location per item.
    /// </summary>
    public sealed class LooseQuadtree2d
    {
        const int BucketSize = 16;
        const int DepthLimit = 32;
        const int ParallelThreshold = 32768;
        const double SegmentMargin = 0.0002;

        struct Entry
        {
            public AxisAlignedBox2d Box;
            public int Next;
            public int Owner;
        }

        struct Node
        {
            public AxisAlignedBox2d Tight;
            public int Child;
            public int Head;
            public int Count;

            public Node(AxisAlignedBox2d tight, int head, int count)
            {
                Tight = tight;
                Child = -1;
                Head = head;
                Count = count;
            }
        }

        Entry[] entries;
        int nextId;
        List<Node> nodes;
        AxisAlignedBox2d extent = AxisAlignedBox2d.Empty;

        public int Count { get; private set; }
        public AxisAlignedBox2d Extent { get { return extent; } }
        public double Looseness { get { return 2; } }
        public int BucketCapacity { get { return BucketSize; } }
        public int MaxDepth { get { return DepthLimit; } }
        public bool ParallelBuild { get; private set; }

        public LooseQuadtree2d() : this(new AxisAlignedBox2d[0]) { }

        public LooseQuadtree2d(IReadOnlyList<AxisAlignedBox2d> boxes, bool parallelBuild = true)
        {
            if (boxes == null) throw new ArgumentNullException(nameof(boxes));
            ParallelBuild = parallelBuild;
            entries = new Entry[boxes.Count];
            for (int i = 0; i < entries.Length; ++i) {
                AxisAlignedBox2d box = boxes[i];
                ValidateBox(box, nameof(boxes));
                entries[i] = new Entry { Box = box, Owner = 0, Next = -1 };
                extent = i == 0 ? box : Union(extent, box);
            }
            Count = nextId = entries.Length;
            Rebuild(parallelBuild);
        }

        public bool ContainsId(int id)
        {
            return id >= 0 && id < nextId && entries[id].Owner >= 0;
        }

        public AxisAlignedBox2d GetBounds(int id)
        {
            if (!ContainsId(id)) throw new ArgumentOutOfRangeException(nameof(id));
            return entries[id].Box;
        }

        public int Insert(AxisAlignedBox2d box)
        {
            ValidateBox(box, nameof(box));
            if (nextId == int.MaxValue) throw new InvalidOperationException("Insertion IDs exhausted.");
            if (nextId == entries.Length) {
                int capacity = entries.Length == 0 ? 4 : (int)Math.Min(int.MaxValue, (long)entries.Length * 2);
                Array.Resize(ref entries, capacity);
            }
            int id = nextId++;
            entries[id] = new Entry { Box = box, Owner = 0, Next = -1 };
            ++Count;
            if (nodes.Count == 0 || !Contains(extent, box)) {
                extent = nodes.Count == 0 ? box : Union(extent, box);
                Rebuild(false);
            } else {
                InsertAt(id, 0, 0);
            }
            return id;
        }

        public bool Remove(int id)
        {
            if (!ContainsId(id)) return false;
            int owner = entries[id].Owner;
            Node node = nodes[owner];
            int previous = -1;
            for (int current = node.Head; current >= 0; current = entries[current].Next) {
                if (current == id) {
                    if (previous < 0) node.Head = entries[current].Next;
                    else entries[previous].Next = entries[current].Next;
                    --node.Count;
                    nodes[owner] = node;
                    entries[id].Owner = -1;
                    entries[id].Next = -1;
                    --Count;
                    return true;
                }
                previous = current;
            }
            return false;
        }

        public bool Remove(AxisAlignedBox2d box)
        {
            ValidateBox(box, nameof(box));
            for (int i = 0; i < nextId; ++i) {
                if (ContainsId(i) && Equal(entries[i].Box, box)) return Remove(i);
            }
            return false;
        }

        public void QueryRectangle(AxisAlignedBox2d box, List<int> results)
        {
            ValidateBox(box, nameof(box));
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            var visitor = new RectangleVisitor { Tree = this, Box = box, Results = results };
            Traverse(ref visitor);
        }

        public void QuerySegment(Vector2d a, Vector2d b, List<int> results)
        {
            ValidatePoint(a, nameof(a));
            ValidatePoint(b, nameof(b));
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            var visitor = new SegmentVisitor { Tree = this, A = a, B = b, Results = results };
            Traverse(ref visitor);
        }

        public bool Traverse<TVisitor>(ref TVisitor visitor) where TVisitor : struct, IQuadtreeVisitor2d
        {
            return Count != 0 && TraverseNode(0, ref visitor);
        }

        bool TraverseNode<TVisitor>(int index, ref TVisitor visitor) where TVisitor : struct, IQuadtreeVisitor2d
        {
            Node node = nodes[index];
            if (!visitor.TestRegion(Loose(node.Tight))) return false;
            for (int id = node.Head; id >= 0; id = entries[id].Next)
                if (visitor.Visit(id)) return true;
            if (node.Child >= 0) {
                for (int i = 0; i < 4; ++i)
                    if (TraverseNode(node.Child + i, ref visitor)) return true;
            }
            return false;
        }

        public int FindNearest(Vector2d point, double maxDistance = double.PositiveInfinity)
        {
            var distance = new BoxDistance { Tree = this, Point = point };
            return FindNearest(point, ref distance, maxDistance);
        }

        public int FindNearest<TDistance>(Vector2d point, ref TDistance distance,
            double maxDistance = double.PositiveInfinity) where TDistance : struct, IQuadtreeDistance2d
        {
            ValidateNearest(point, maxDistance);
            int best = -1;
            if (Count != 0) NearestNode(0, point, ref distance, ref best, ref maxDistance);
            return best;
        }

        public void FindNearestK(Vector2d point, int k, List<int> results, double maxDistance = double.PositiveInfinity)
        {
            var distance = new BoxDistance { Tree = this, Point = point };
            FindNearestK(point, k, results, ref distance, maxDistance);
        }

        public void FindNearestK<TDistance>(Vector2d point, int k, List<int> results, ref TDistance distance,
            double maxDistance = double.PositiveInfinity) where TDistance : struct, IQuadtreeDistance2d
        {
            ValidateNearest(point, maxDistance);
            if (k < 0) throw new ArgumentOutOfRangeException(nameof(k));
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            if (k == 0 || Count == 0) return;
            NearestKNode(0, point, k, results, ref distance, maxDistance);
            for (int end = results.Count - 1; end > 0; --end) {
                int value = results[end];
                results[end] = results[0];
                results[0] = value;
                SiftDown(results, end, ref distance);
            }
        }

        void NearestNode<TDistance>(int index, Vector2d point, ref TDistance distance,
            ref int best, ref double limit) where TDistance : struct, IQuadtreeDistance2d
        {
            Node node = nodes[index];
            if (BoxDistanceTo(point, Loose(node.Tight)) > limit) return;
            for (int id = node.Head; id >= 0; id = entries[id].Next) {
                if (BoxDistanceTo(point, entries[id].Box) > limit) continue;
                double d = CheckedDistance(id, ref distance);
                if (d <= limit && (best < 0 || d < limit ||
                    (double.IsPositiveInfinity(d) ? Worse(best, id, ref distance) : id < best))) {
                    limit = d;
                    best = id;
                }
            }
            if (node.Child < 0) return;
            int first = Quadrant(node.Tight, point);
            NearestNode(node.Child + first, point, ref distance, ref best, ref limit);
            for (int i = 0; i < 4; ++i)
                if (i != first) NearestNode(node.Child + i, point, ref distance, ref best, ref limit);
        }

        void NearestKNode<TDistance>(int index, Vector2d point, int k, List<int> results,
            ref TDistance distance, double maximum) where TDistance : struct, IQuadtreeDistance2d
        {
            double limit = results.Count == k ? CheckedDistance(results[0], ref distance) : maximum;
            Node node = nodes[index];
            if (BoxDistanceTo(point, Loose(node.Tight)) > limit) return;
            for (int id = node.Head; id >= 0; id = entries[id].Next) {
                if (BoxDistanceTo(point, entries[id].Box) > limit) continue;
                double d = CheckedDistance(id, ref distance);
                if (d > limit) continue;
                if (results.Count < k) {
                    int position = results.Count;
                    results.Add(id);
                    while (position > 0) {
                        int parent = (position - 1) / 2;
                        if (!Worse(id, results[parent], ref distance)) break;
                        results[position] = results[parent];
                        position = parent;
                    }
                    results[position] = id;
                } else if (d < limit ||
                    (double.IsPositiveInfinity(d) ? Worse(results[0], id, ref distance) : id < results[0])) {
                    results[0] = id;
                    SiftDown(results, results.Count, ref distance);
                }
                if (results.Count == k) limit = CheckedDistance(results[0], ref distance);
            }
            if (node.Child < 0) return;
            int first = Quadrant(node.Tight, point);
            NearestKNode(node.Child + first, point, k, results, ref distance, maximum);
            for (int i = 0; i < 4; ++i)
                if (i != first) NearestKNode(node.Child + i, point, k, results, ref distance, maximum);
        }

        static void SiftDown<TDistance>(List<int> heap, int count, ref TDistance distance)
            where TDistance : struct, IQuadtreeDistance2d
        {
            int position = 0, id = heap[0];
            while (position < count / 2) {
                int child = position * 2 + 1;
                if (child + 1 < count && Worse(heap[child + 1], heap[child], ref distance)) ++child;
                if (!Worse(heap[child], id, ref distance)) break;
                heap[position] = heap[child];
                position = child;
            }
            heap[position] = id;
        }

        static bool Worse<TDistance>(int a, int b, ref TDistance distance) where TDistance : struct, IQuadtreeDistance2d
        {
            double da = CheckedDistance(a, ref distance), db = CheckedDistance(b, ref distance);
            if (double.IsPositiveInfinity(da) && double.IsPositiveInfinity(db) &&
                distance is IQuadtreeOverflowDistance2d) {
                int order = ((IQuadtreeOverflowDistance2d)distance).CompareOverflow(a, b);
                return order > 0 || (order == 0 && a > b);
            }
            return da > db || (da == db && a > b);
        }

        static double CheckedDistance<TDistance>(int id, ref TDistance distance) where TDistance : struct, IQuadtreeDistance2d
        {
            double value = distance.Distance(id);
            if (double.IsNaN(value) || value < 0) throw new ArgumentException("Invalid item distance.", nameof(distance));
            return value;
        }

        void InsertAt(int id, int index, int depth)
        {
            Node node = nodes[index];
            if (node.Child >= 0) {
                int q = Quadrant(node.Tight, Center(entries[id].Box));
                if (Contains(Loose(nodes[node.Child + q].Tight), entries[id].Box)) {
                    InsertAt(id, node.Child + q, depth + 1);
                    return;
                }
            }
            entries[id].Owner = index;
            entries[id].Next = node.Head;
            node.Head = id;
            ++node.Count;
            nodes[index] = node;
            if (node.Child < 0 && node.Count > BucketSize) BuildNode(nodes, index, depth);
        }

        void Rebuild(bool parallel)
        {
            nodes = new List<Node>();
            if (nextId == 0) return;
            int head = -1;
            for (int id = nextId - 1; id >= 0; --id) {
                if (entries[id].Owner < 0) continue;
                entries[id].Next = head;
                head = id;
                entries[id].Owner = 0;
            }
            nodes.Add(new Node(extent, head, Count));
            if (!parallel || Count < ParallelThreshold || !Split(nodes, 0, 0)) {
                BuildNode(nodes, 0, 0);
                return;
            }
            // Each worker owns disjoint item chains and a private node array. No query scratch is retained.
            var branches = new List<Node>[4];
            Parallel.For(0, 4, q => {
                var branch = new List<Node> { nodes[1 + q] };
                SetOwners(branch, 0);
                BuildNode(branch, 0, 1);
                branches[q] = branch;
            });
            for (int q = 0; q < 4; ++q) {
                List<Node> branch = branches[q];
                int offset = nodes.Count - 1;
                Node root = branch[0];
                if (root.Child >= 0) root.Child += offset;
                nodes[1 + q] = root;
                SetOwners(nodes, 1 + q);
                for (int i = 1; i < branch.Count; ++i) {
                    Node node = branch[i];
                    if (node.Child >= 0) node.Child += offset;
                    nodes.Add(node);
                    SetOwners(nodes, nodes.Count - 1);
                }
            }
        }

        void BuildNode(List<Node> target, int index, int depth)
        {
            if (!Split(target, index, depth)) return;
            int child = target[index].Child;
            for (int q = 0; q < 4; ++q) BuildNode(target, child + q, depth + 1);
        }

        bool Split(List<Node> target, int index, int depth)
        {
            Node node = target[index];
            if (node.Child >= 0 || node.Count <= BucketSize || depth >= DepthLimit) return false;
            Vector2d center = Center(node.Tight);
            bool splitX = center.x > node.Tight.Min.x && center.x < node.Tight.Max.x;
            bool splitY = center.y > node.Tight.Min.y && center.y < node.Tight.Max.y;
            if (!splitX && !splitY) return false;
            int child = target.Count;
            for (int q = 0; q < 4; ++q) {
                AxisAlignedBox2d tight = ChildBox(node.Tight, center, q);
                target.Add(new Node(tight, -1, 0));
            }
            int retained = -1, retainedCount = 0;
            for (int id = node.Head; id >= 0;) {
                int next = entries[id].Next;
                int q = Quadrant(node.Tight, Center(entries[id].Box));
                Node destination = target[child + q];
                if (Contains(Loose(destination.Tight), entries[id].Box)) {
                    entries[id].Next = destination.Head;
                    entries[id].Owner = child + q;
                    destination.Head = id;
                    ++destination.Count;
                    target[child + q] = destination;
                } else {
                    entries[id].Next = retained;
                    entries[id].Owner = index;
                    retained = id;
                    ++retainedCount;
                }
                id = next;
            }
            node.Child = child;
            node.Head = retained;
            node.Count = retainedCount;
            target[index] = node;
            return true;
        }

        void SetOwners(List<Node> target, int index)
        {
            for (int id = target[index].Head; id >= 0; id = entries[id].Next) entries[id].Owner = index;
        }

        struct RectangleVisitor : IQuadtreeVisitor2d
        {
            public LooseQuadtree2d Tree;
            public AxisAlignedBox2d Box;
            public List<int> Results;
            public bool TestRegion(AxisAlignedBox2d region) { return Intersects(region, Box); }
            public bool Visit(int id) { if (Intersects(Tree.entries[id].Box, Box)) Results.Add(id); return false; }
        }

        struct SegmentVisitor : IQuadtreeVisitor2d
        {
            public LooseQuadtree2d Tree;
            public Vector2d A, B;
            public List<int> Results;
            public bool TestRegion(AxisAlignedBox2d region) { return SegmentIntersects(A, B, region); }
            public bool Visit(int id) { if (SegmentIntersects(A, B, Tree.entries[id].Box)) Results.Add(id); return false; }
        }

        struct BoxDistance : IQuadtreeDistance2d, IQuadtreeOverflowDistance2d
        {
            public LooseQuadtree2d Tree;
            public Vector2d Point;
            public double Distance(int id) { return BoxDistanceTo(Point, Tree.entries[id].Box); }
            public int CompareOverflow(int a, int b)
            {
                return QuadtreeExactQuery2d.BoxSquared(Point, Tree.entries[a].Box).CompareTo(
                    QuadtreeExactQuery2d.BoxSquared(Point, Tree.entries[b].Box));
            }
        }

        static double Midpoint(double a, double b)
        {
            return (a < 0 && b > 0) ? a * 0.5 + b * 0.5 : a + (b - a) * 0.5;
        }

        static Vector2d Center(AxisAlignedBox2d box)
        {
            return new Vector2d(Midpoint(box.Min.x, box.Max.x), Midpoint(box.Min.y, box.Max.y));
        }

        static double Saturate(double value)
        {
            return Math.Max(-double.MaxValue, Math.Min(double.MaxValue, value));
        }

        static AxisAlignedBox2d Loose(AxisAlignedBox2d box)
        {
            double x = box.Max.x * 0.5 - box.Min.x * 0.5;
            double y = box.Max.y * 0.5 - box.Min.y * 0.5;
            return new AxisAlignedBox2d(Saturate(box.Min.x - x), Saturate(box.Min.y - y),
                Saturate(box.Max.x + x), Saturate(box.Max.y + y));
        }

        static AxisAlignedBox2d ChildBox(AxisAlignedBox2d box, Vector2d center, int q)
        {
            return new AxisAlignedBox2d((q & 1) == 0 ? box.Min.x : center.x,
                (q & 2) == 0 ? box.Min.y : center.y,
                (q & 1) == 0 ? center.x : box.Max.x, (q & 2) == 0 ? center.y : box.Max.y);
        }

        static int Quadrant(AxisAlignedBox2d box, Vector2d point)
        {
            Vector2d center = Center(box);
            return (point.x >= center.x ? 1 : 0) | (point.y >= center.y ? 2 : 0);
        }

        static AxisAlignedBox2d Union(AxisAlignedBox2d a, AxisAlignedBox2d b)
        {
            return new AxisAlignedBox2d(Math.Min(a.Min.x, b.Min.x), Math.Min(a.Min.y, b.Min.y),
                Math.Max(a.Max.x, b.Max.x), Math.Max(a.Max.y, b.Max.y));
        }

        static bool Contains(AxisAlignedBox2d a, AxisAlignedBox2d b)
        {
            return a.Min.x <= b.Min.x && a.Min.y <= b.Min.y && a.Max.x >= b.Max.x && a.Max.y >= b.Max.y;
        }

        static bool Intersects(AxisAlignedBox2d a, AxisAlignedBox2d b)
        {
            return a.Min.x <= b.Max.x && a.Max.x >= b.Min.x && a.Min.y <= b.Max.y && a.Max.y >= b.Min.y;
        }

        static bool Equal(AxisAlignedBox2d a, AxisAlignedBox2d b)
        {
            return a.Min.x == b.Min.x && a.Min.y == b.Min.y && a.Max.x == b.Max.x && a.Max.y == b.Max.y;
        }

        static bool SegmentIntersects(Vector2d a, Vector2d b, AxisAlignedBox2d box)
        {
            double enter = 0, exit = 1;
            var expanded = new AxisAlignedBox2d(Saturate(box.Min.x - SegmentMargin), Saturate(box.Min.y - SegmentMargin),
                Saturate(box.Max.x + SegmentMargin), Saturate(box.Max.y + SegmentMargin));
            if (!ClipAxis(a.x, b.x, expanded.Min.x, expanded.Max.x, ref enter, ref exit) ||
                !ClipAxis(a.y, b.y, expanded.Min.y, expanded.Max.y, ref enter, ref exit)) return false;
            // Each normalized bound has absolute error below 8 ulps at 1 (subtraction, division,
            // and optional reversal). Resolve gaps/overlaps within twice that bound exactly.
            if (Math.Abs(enter - exit) <= 3.552713678800501e-15)
                return QuadtreeExactQuery2d.SegmentIntersects(a, b, expanded);
            return enter <= exit;
        }

        static bool ClipAxis(double a, double b, double min, double max, ref double enter, ref double exit)
        {
            double low = Math.Min(a, b), high = Math.Max(a, b);
            if (high < min || low > max) return false;
            if (a == b) return true;
            double denominator = high - low;
            double start = 0, end = 1;
            // Half-scaled differences avoid overflowing for finite endpoints of opposite signs.
            bool overflow = double.IsInfinity(denominator);
            if (overflow) denominator = high * 0.5 - low * 0.5;
            if (min > low) start = (overflow ? min * 0.5 - low * 0.5 : min - low) / denominator;
            if (max < high) end = (overflow ? max * 0.5 - low * 0.5 : max - low) / denominator;
            if (a > b) { double saved = start; start = 1 - end; end = 1 - saved; }
            enter = Math.Max(enter, start);
            exit = Math.Min(exit, end);
            return true;
        }

        static double BoxDistanceTo(Vector2d point, AxisAlignedBox2d box)
        {
            double dx = Math.Max(0, Math.Max(box.Min.x - point.x, point.x - box.Max.x));
            double dy = Math.Max(0, Math.Max(box.Min.y - point.y, point.y - box.Max.y));
            double squared = dx * dx + dy * dy;
            if (squared >= 2.2250738585072014e-308 && !double.IsInfinity(squared)) return Math.Sqrt(squared);
            double large = Math.Max(dx, dy), small = Math.Min(dx, dy);
            if (large == 0 || double.IsInfinity(large)) return large;
            double ratio = small / large;
            return large * Math.Sqrt(1 + ratio * ratio);
        }

        static void ValidateNearest(Vector2d point, double maximum)
        {
            ValidatePoint(point, nameof(point));
            if (double.IsNaN(maximum) || maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        static void ValidatePoint(Vector2d point, string name)
        {
            if (!Finite(point.x) || !Finite(point.y)) throw new ArgumentException("Coordinates must be finite.", name);
        }

        static void ValidateBox(AxisAlignedBox2d box, string name)
        {
            ValidatePoint(box.Min, name);
            ValidatePoint(box.Max, name);
            if (box.Min.x > box.Max.x || box.Min.y > box.Max.y) throw new ArgumentException("Bounds are inverted.", name);
        }

        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
