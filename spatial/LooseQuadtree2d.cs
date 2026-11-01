using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace g3
{
    public interface IQuadtreeVisitor2d
    {
        /// <summary>The region holds every item of a part of the tree: false skips that part.</summary>
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
    /// Cells are recursively quartered, with twice-sized loose bounds that decide where an item is stored, in one place only.
    /// Queries prune by the bounds of what each part of the tree really holds.
    /// </summary>
    public sealed class LooseQuadtree2d
    {
        const int BucketSize = 8;
        // Items gathered far inside their cell may stay together up to this count: cutting would add a chain of nodes before it separates them.
        const int GatheredBucketSize = 16;
        const int DepthLimit = 64;
        internal const int ParallelThreshold = 32768;
        const double SegmentMargin = 0.0002;

        struct Entry
        {
            public AxisAlignedBox2d Box;
            public int Next;
            public int Previous;
            public int Owner;
        }

        struct Node
        {
            // Of everything stored at or below this node. Removal leaves it as it is, which stays valid.
            public AxisAlignedBox2d Bounds;
            public int Child;
            public int Head;
            public int Count;

            public Node(int head, int count)
            {
                Bounds = AxisAlignedBox2d.Empty;
                Child = -1;
                Head = head;
                Count = count;
            }
        }

        // A build works on these, side by side in memory, and links the items into their cells at the end.
        struct BuildItem
        {
            public AxisAlignedBox2d Box;
            public int Id;
            public int Class;
        }

        struct Part
        {
            public int Index, Start, Count, Depth;
            public AxisAlignedBox2d Cell;
        }

        // An array behind it, for references to nodes instead of copies of them.
        sealed class NodeList
        {
            public Node[] Items = new Node[16];
            public int Count;
            public void Add(Node node)
            {
                if (Count == Items.Length) Array.Resize(ref Items, Count * 2);
                Items[Count++] = node;
            }
            // To the given size, as doubling leaves up to half of a large array unused.
            public void Resize(int capacity)
            {
                if (capacity != Items.Length) Array.Resize(ref Items, capacity);
            }
        }

        Entry[] entries;
        int nextId;
        NodeList nodes;
        AxisAlignedBox2d extent = AxisAlignedBox2d.Empty;
        AxisAlignedBox2d rootRegion = AxisAlignedBox2d.Empty;

        public int Count { get; private set; }
        internal int NextId { get { return nextId; } }
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
            // In parallel only from what is known to allow it: another list may hold a lock, cache what it reads, or need its thread.
            if (entries.Length > 0) extent = parallelBuild && entries.Length >= ParallelThreshold && (boxes is AxisAlignedBox2d[] || boxes.GetType() == typeof(List<AxisAlignedBox2d>)) ?
                FillInBlocks(boxes) : Fill(boxes, 0, entries.Length);
            Count = nextId = entries.Length;
            rootRegion = extent;
            Rebuild(parallelBuild);
        }

        // The entries of a range, and the bounds of their boxes. The build links them afterwards.
        AxisAlignedBox2d Fill(IReadOnlyList<AxisAlignedBox2d> boxes, int start, int end)
        {
            var array = boxes as AxisAlignedBox2d[];
            double minX = double.MaxValue, minY = double.MaxValue, maxX = -double.MaxValue, maxY = -double.MaxValue;
            for (int i = start; i < end; ++i) {
                AxisAlignedBox2d box = array != null ? array[i] : boxes[i];
                ValidateBox(box, nameof(boxes));
                ref Entry entry = ref entries[i];
                entry.Box = box;
                if (box.Min.x < minX) minX = box.Min.x;
                if (box.Min.y < minY) minY = box.Min.y;
                if (box.Max.x > maxX) maxX = box.Max.x;
                if (box.Max.y > maxY) maxY = box.Max.y;
            }
            return new AxisAlignedBox2d(minX, minY, maxX, maxY);
        }

        // Blocks are joined in order, so a tie between zeros of either sign goes as in a sequential fill.
        AxisAlignedBox2d FillInBlocks(IReadOnlyList<AxisAlignedBox2d> boxes)
        {
            int blocks = (entries.Length + ParallelThreshold - 1) / ParallelThreshold;
            var parts = new AxisAlignedBox2d[blocks];
            try {
                Parallel.For(0, blocks, b => parts[b] = Fill(boxes, b * ParallelThreshold, Math.Min(entries.Length, (b + 1) * ParallelThreshold)));
            } catch (AggregateException) {
                // This reports the first bad box, as a sequential fill does.
                return Fill(boxes, 0, entries.Length);
            }
            AxisAlignedBox2d all = parts[0];
            for (int b = 1; b < blocks; ++b) {
                if (parts[b].Min.x < all.Min.x) all.Min.x = parts[b].Min.x;
                if (parts[b].Min.y < all.Min.y) all.Min.y = parts[b].Min.y;
                if (parts[b].Max.x > all.Max.x) all.Max.x = parts[b].Max.x;
                if (parts[b].Max.y > all.Max.y) all.Max.y = parts[b].Max.y;
            }
            return all;
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
            entries[id] = new Entry { Box = box, Owner = 0, Next = -1, Previous = -1 };
            ++Count;
            if (nodes.Count == 0) {
                extent = rootRegion = box;
                Rebuild(false);
                return id;
            }
            extent = Union(extent, box);
            if (Contains(rootRegion, box)) {
                InsertAt(id, 0, rootRegion, 0);
            } else {
                rootRegion = Grown(rootRegion, box);
                Rebuild(false);
            }
            return id;
        }

        public bool Remove(int id)
        {
            if (!ContainsId(id)) return false;
            Entry entry = entries[id];
            ref Node node = ref nodes.Items[entry.Owner];
            if (entry.Previous >= 0) entries[entry.Previous].Next = entry.Next;
            else node.Head = entry.Next;
            if (entry.Next >= 0) entries[entry.Next].Previous = entry.Previous;
            --node.Count;
            entries[id].Owner = -1;
            --Count;
            return true;
        }

        public bool Remove(AxisAlignedBox2d box)
        {
            ValidateBox(box, nameof(box));
            var visitor = new FirstEqualVisitor { Tree = this, Box = box, First = -1 };
            Traverse(ref visitor);
            return visitor.First >= 0 && Remove(visitor.First);
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
            ref Node node = ref nodes.Items[index];
            if ((node.Head < 0 && node.Child < 0) || !visitor.TestRegion(node.Bounds)) return false;
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
            ref Node node = ref nodes.Items[index];
            for (int id = node.Head; id >= 0; id = entries[id].Next) {
                if (Beyond(BoxDistanceTo(point, entries[id].Box), limit)) continue;
                double d = CheckedDistance(id, ref distance);
                if (d <= limit && (best < 0 || d < limit ||
                    (double.IsPositiveInfinity(d) ? Worse(best, id, ref distance) : id < best))) {
                    limit = d;
                    best = id;
                }
            }
            int child = node.Child;
            if (child < 0) return;
            double d0 = ChildDistance(child, point), d1 = ChildDistance(child + 1, point);
            double d2 = ChildDistance(child + 2, point), d3 = ChildDistance(child + 3, point);
            for (int q; (q = NextChild(ref d0, ref d1, ref d2, ref d3, limit)) >= 0;)
                NearestNode(child + q, point, ref distance, ref best, ref limit);
        }

        void NearestKNode<TDistance>(int index, Vector2d point, int k, List<int> results,
            ref TDistance distance, double maximum) where TDistance : struct, IQuadtreeDistance2d
        {
            double limit = results.Count == k ? CheckedDistance(results[0], ref distance) : maximum;
            ref Node node = ref nodes.Items[index];
            for (int id = node.Head; id >= 0; id = entries[id].Next) {
                if (Beyond(BoxDistanceTo(point, entries[id].Box), limit)) continue;
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
            int child = node.Child;
            if (child < 0) return;
            double d0 = ChildDistance(child, point), d1 = ChildDistance(child + 1, point);
            double d2 = ChildDistance(child + 2, point), d3 = ChildDistance(child + 3, point);
            for (int q; (q = NextChild(ref d0, ref d1, ref d2, ref d3, limit)) >= 0;) {
                NearestKNode(child + q, point, k, results, ref distance, maximum);
                if (results.Count == k) limit = CheckedDistance(results[0], ref distance);
            }
        }

        // The distance to a child's content. An empty child is far away, and has nothing to visit anyway.
        double ChildDistance(int index, Vector2d point)
        {
            return BoxDistanceTo(point, nodes.Items[index].Bounds);
        }

        // The nearest child left, if it is not beyond the limit, and marks it as taken by a negative distance. Nearest first tightens the limit soonest.
        static int NextChild(ref double d0, ref double d1, ref double d2, ref double d3, double limit)
        {
            int q = -1;
            double d = 0;
            if (d0 >= 0) { q = 0; d = d0; }
            if (d1 >= 0 && (q < 0 || d1 < d)) { q = 1; d = d1; }
            if (d2 >= 0 && (q < 0 || d2 < d)) { q = 2; d = d2; }
            if (d3 >= 0 && (q < 0 || d3 < d)) { q = 3; d = d3; }
            if (q < 0 || Beyond(d, limit)) return -1;
            if (q == 0) d0 = -1; else if (q == 1) d1 = -1; else if (q == 2) d2 = -1; else d3 = -1;
            return q;
        }

        // A rounded item distance can fall a little below the rounded distance to its box. Pruning exactly at the
        // limit would then skip an item that ties with the best one, and the tie would go by visiting order.
        static bool Beyond(double boxDistance, double limit)
        {
            // An infinite box distance is rounding too: an item inside the box can still reach exactly MaxValue.
            return (boxDistance > double.MaxValue ? double.MaxValue : boxDistance) * (1 - 1e-15) > limit;
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

        void InsertAt(int id, int index, AxisAlignedBox2d cell, int depth)
        {
            ref Node node = ref nodes.Items[index];
            node.Bounds = Union(node.Bounds, entries[id].Box);
            if (node.Child >= 0) {
                Vector2d center = Center(cell);
                int axes = SplitAxes(cell, center);
                Vector2d c = Center(entries[id].Box);
                int q = axes & ((c.x >= center.x ? 1 : 0) | (c.y >= center.y ? 2 : 0));
                AxisAlignedBox2d childCell = ChildBox(cell, center, q, axes);
                if (Contains(Loose(childCell), entries[id].Box)) {
                    InsertAt(id, node.Child + q, childCell, depth + 1);
                    return;
                }
            }
            entries[id].Owner = index;
            Link(id, ref node.Head);
            int count = ++node.Count;
            // A cell of equal items refuses to split, at a cost per attempt: at powers of two, so that filling it stays linear.
            // The union above left the bounds valid, so a node that cannot split costs nothing more.
            if (node.Child < 0 && count > BucketSize && (count == BucketSize + 1 || count == GatheredBucketSize + 1 || (count & (count - 1)) == 0) &&
                depth < DepthLimit && SplitAxes(cell, Center(cell)) != 0 && !AllEqual(node.Head))
                BuildCell(index, cell, depth);
        }

        // Builds the subtree of a node that has outgrown its list.
        void BuildCell(int index, AxisAlignedBox2d cell, int depth)
        {
            var items = new BuildItem[nodes.Items[index].Count];
            int count = 0;
            for (int id = nodes.Items[index].Head; id >= 0; id = entries[id].Next) {
                items[count].Box = entries[id].Box;
                items[count++].Id = id;
            }
            int first = nodes.Count;
            nodes.Items[index].Head = 0;
            BuildRange(nodes, index, items, cell, depth, new int[10]);
            LinkRange(index, items);
            for (int i = first; i < nodes.Count; ++i) LinkRange(i, items);
        }

        void Rebuild(bool parallel)
        {
            nodes = new NodeList();
            if (nextId == 0) return;
            var items = new BuildItem[Count];
            // Only the first build runs in parallel, and it has no removed items yet.
            if (parallel && Count >= ParallelThreshold) {
                int blocks = (Count + ParallelThreshold - 1) / ParallelThreshold;
                Parallel.For(0, blocks, b => {
                    for (int id = b * ParallelThreshold, end = Math.Min(Count, (b + 1) * ParallelThreshold); id < end; ++id) {
                        items[id].Box = entries[id].Box;
                        items[id].Id = id;
                    }
                });
            } else {
                int live = 0;
                for (int id = 0; id < nextId; ++id) {
                    if (entries[id].Owner < 0) continue;
                    items[live].Box = entries[id].Box;
                    items[live++].Id = id;
                }
            }
            nodes.Add(new Node(0, Count));
            if (!parallel || Count < ParallelThreshold) {
                BuildRange(nodes, 0, items, rootRegion, 0, new int[10]);
                for (int i = 0; i < nodes.Count; ++i) LinkRange(i, items);
                nodes.Resize(nodes.Count);
                return;
            }
            // Cut down side by side until a part that can still be cut holds at most an eighth of the items, or the threshold,
            // wherever they are gathered. Each worker then owns a range of the items and a private node array. A range is sorted
            // as in a sequential build, so both give the same tree and the same order of items.
            int largePart = Math.Max(ParallelThreshold, Count / 8);
            var parts = new List<Part>();
            var large = new List<Part> { new Part { Index = 0, Start = 0, Count = Count, Cell = rootRegion, Depth = 0 } };
            var cut = new List<int>();
            while (large.Count > 0) {
                var counts = new int[large.Count][];
                List<Part> current = large;
                Parallel.For(0, current.Count, p => {
                    Part part = current[p];
                    var scratch = new int[10];
                    if (SortRange(items, part.Start, part.Count, part.Cell, part.Depth, scratch, part.Count >= ParallelThreshold)) counts[p] = scratch;
                });
                var next = new List<Part>();
                for (int p = 0; p < current.Count; ++p) {
                    Part part = current[p];
                    if (counts[p] == null) {
                        parts.Add(part);
                        continue;
                    }
                    AddChildren(nodes, part.Index, counts[p]);
                    cut.Add(part.Index);
                    int child = nodes.Items[part.Index].Child;
                    Vector2d center = Center(part.Cell);
                    int axes = SplitAxes(part.Cell, center);
                    for (int q = 0; q < 4; ++q) {
                        Node node = nodes.Items[child + q];
                        (node.Count > largePart ? next : parts).Add(new Part { Index = child + q, Start = node.Head, Count = node.Count, Cell = ChildBox(part.Cell, center, q, axes), Depth = part.Depth + 1 });
                    }
                }
                large = next;
            }
            var branches = new NodeList[parts.Count];
            Parallel.For(0, parts.Count, p => {
                var branch = new NodeList();
                branch.Add(new Node(parts[p].Start, parts[p].Count));
                BuildRange(branch, 0, items, parts[p].Cell, parts[p].Depth, new int[10]);
                branches[p] = branch;
            });
            int total = nodes.Count;
            foreach (NodeList branch in branches) total += branch.Count - 1;
            nodes.Resize(total);
            for (int p = 0; p < parts.Count; ++p) {
                NodeList branch = branches[p];
                int offset = nodes.Count - 1;
                Node root = branch.Items[0];
                if (root.Child >= 0) root.Child += offset;
                nodes.Items[parts[p].Index] = root;
                for (int i = 1; i < branch.Count; ++i) {
                    Node node = branch.Items[i];
                    if (node.Child >= 0) node.Child += offset;
                    nodes.Add(node);
                }
            }
            // Children have higher indices than their parents, so this runs from the bottom up.
            for (int c = cut.Count - 1; c >= 0; --c) Fit(nodes, cut[c], items);
            Parallel.For(0, nodes.Count, i => LinkRange(i, items));
        }

        // While building, the Head of a node is the start of its items, retained ones first, then those of its children in turn.
        void BuildRange(NodeList target, int index, BuildItem[] items, AxisAlignedBox2d cell, int depth, int[] scratch)
        {
            if (CutRange(target, index, items, cell, depth, scratch)) {
                int child = target.Items[index].Child;
                Vector2d center = Center(cell);
                int axes = SplitAxes(cell, center);
                for (int q = 0; q < 4; ++q) BuildRange(target, child + q, items, ChildBox(cell, center, q, axes), depth + 1, scratch);
            }
            Fit(target, index, items);
        }

        bool CutRange(NodeList target, int index, BuildItem[] items, AxisAlignedBox2d cell, int depth, int[] scratch)
        {
            if (!SortRange(items, target.Items[index].Head, target.Items[index].Count, cell, depth, scratch, false)) return false;
            AddChildren(target, index, scratch);
            return true;
        }

        // Sets the class of each item in a part of a range, and counts them in the first five places of counts.
        static void Classify(BuildItem[] items, int start, int end, AxisAlignedBox2d cell, Vector2d center, int axes, int[] counts)
        {
            AxisAlignedBox2d loose0 = Loose(ChildBox(cell, center, 0, axes)), loose1 = Loose(ChildBox(cell, center, 1, axes));
            AxisAlignedBox2d loose2 = Loose(ChildBox(cell, center, 2, axes)), loose3 = Loose(ChildBox(cell, center, 3, axes));
            for (int i = start; i < end; ++i) {
                ref BuildItem item = ref items[i];
                Vector2d c = Center(item.Box);
                int q = axes & ((c.x >= center.x ? 1 : 0) | (c.y >= center.y ? 2 : 0));
                bool fits = q == 0 ? Contains(loose0, item.Box) : q == 1 ? Contains(loose1, item.Box) : q == 2 ? Contains(loose2, item.Box) : Contains(loose3, item.Box);
                item.Class = fits ? q + 1 : 0;
                ++counts[item.Class];
            }
        }

        static bool Gathered(BuildItem[] items, int start, int count, AxisAlignedBox2d cell)
        {
            if (count > GatheredBucketSize) return false;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = -double.MaxValue, maxY = -double.MaxValue;
            for (int i = start; i < start + count; ++i) {
                ref AxisAlignedBox2d box = ref items[i].Box;
                if (box.Min.x < minX) minX = box.Min.x;
                if (box.Min.y < minY) minY = box.Min.y;
                if (box.Max.x > maxX) maxX = box.Max.x;
                if (box.Max.y > maxY) maxY = box.Max.y;
            }
            // In halves, as a difference can overflow.
            return maxX * 0.5 - minX * 0.5 <= (cell.Max.x * 0.5 - cell.Min.x * 0.5) * 0.25 && maxY * 0.5 - minY * 0.5 <= (cell.Max.y * 0.5 - cell.Min.y * 0.5) * 0.25;
        }

        static void ClassifyInBlocks(BuildItem[] items, int start, int end, AxisAlignedBox2d cell, Vector2d center, int axes, int[] scratch)
        {
            int blocks = (end - start + ParallelThreshold - 1) / ParallelThreshold;
            Parallel.For(0, blocks, b => {
                var counts = new int[5];
                Classify(items, start + b * ParallelThreshold, Math.Min(end, start + (b + 1) * ParallelThreshold), cell, center, axes, counts);
                lock (scratch) {
                    for (int c = 0; c < 5; ++c) scratch[c] += counts[c];
                }
            });
        }

        // Sorts the items of a node into those it keeps and those of its four children, in place. Their counts end up in scratch[0..4].
        bool SortRange(BuildItem[] items, int start, int count, AxisAlignedBox2d cell, int depth, int[] scratch, bool parallel)
        {
            if (count <= BucketSize || depth >= DepthLimit) return false;
            Vector2d center = Center(cell);
            int axes = SplitAxes(cell, center);
            if (axes == 0 || AllEqual(items, start, count) || Gathered(items, start, count, cell)) return false;
            int end = start + count;
            for (int c = 0; c < 5; ++c) scratch[c] = 0;
            if (parallel) ClassifyInBlocks(items, start, end, cell, center, axes, scratch);
            else Classify(items, start, end, cell, center, axes, scratch);
            // Next free place per class in scratch[5..9], keeping the counts in scratch[0..4].
            int place = start;
            for (int c = 0; c < 5; ++c) {
                scratch[5 + c] = place;
                place += scratch[c];
            }
            int limit = start;
            for (int c = 0; c < 5; ++c) {
                limit += scratch[c];
                while (scratch[5 + c] < limit) {
                    int i = scratch[5 + c];
                    int other = items[i].Class;
                    if (other == c) {
                        ++scratch[5 + c];
                    } else {
                        int j = scratch[5 + other]++;
                        BuildItem swap = items[i];
                        items[i] = items[j];
                        items[j] = swap;
                    }
                }
            }
            return true;
        }

        // Adds the four children of a sorted node.
        static void AddChildren(NodeList target, int index, int[] scratch)
        {
            int child = target.Count, offset = target.Items[index].Head + scratch[0];
            for (int q = 0; q < 4; ++q) {
                target.Add(new Node(offset, scratch[q + 1]));
                offset += scratch[q + 1];
            }
            ref Node node = ref target.Items[index];
            node.Child = child;
            node.Count = scratch[0];
        }

        // The bounds of a node, from its items and the bounds of its children. Those of an empty child add nothing.
        static void Fit(NodeList target, int index, BuildItem[] items)
        {
            Node[] all = target.Items;
            ref Node node = ref all[index];
            double minX = double.MaxValue, minY = double.MaxValue, maxX = -double.MaxValue, maxY = -double.MaxValue;
            for (int i = node.Head, end = node.Head + node.Count; i < end; ++i) {
                ref AxisAlignedBox2d box = ref items[i].Box;
                if (box.Min.x < minX) minX = box.Min.x;
                if (box.Min.y < minY) minY = box.Min.y;
                if (box.Max.x > maxX) maxX = box.Max.x;
                if (box.Max.y > maxY) maxY = box.Max.y;
            }
            if (node.Child >= 0) {
                for (int q = 0; q < 4; ++q) {
                    ref Node child = ref all[node.Child + q];
                    if (child.Bounds.Min.x < minX) minX = child.Bounds.Min.x;
                    if (child.Bounds.Min.y < minY) minY = child.Bounds.Min.y;
                    if (child.Bounds.Max.x > maxX) maxX = child.Bounds.Max.x;
                    if (child.Bounds.Max.y > maxY) maxY = child.Bounds.Max.y;
                }
            }
            node.Bounds = new AxisAlignedBox2d(minX, minY, maxX, maxY);
        }

        // Turns the range of a finished node into its list of items.
        void LinkRange(int index, BuildItem[] items)
        {
            ref Node node = ref nodes.Items[index];
            int start = node.Head, previous = -1;
            for (int i = start, end = start + node.Count; i < end; ++i) {
                ref Entry entry = ref entries[items[i].Id];
                entry.Owner = index;
                entry.Previous = previous;
                entry.Next = i + 1 < end ? items[i + 1].Id : -1;
                previous = items[i].Id;
            }
            node.Head = node.Count > 0 ? items[start].Id : -1;
        }

        static bool AllEqual(BuildItem[] items, int start, int count)
        {
            for (int i = start + 1; i < start + count; ++i)
                if (!Equal(items[i].Box, items[start].Box)) return false;
            return true;
        }

        // Cutting cannot separate equal items: it would only build a chain of nodes down to the depth limit.
        bool AllEqual(int head)
        {
            for (int id = entries[head].Next; id >= 0; id = entries[id].Next)
                if (!Equal(entries[id].Box, entries[head].Box)) return false;
            return true;
        }

        void Link(int id, ref int head)
        {
            entries[id].Next = head;
            entries[id].Previous = -1;
            if (head >= 0) entries[head].Previous = id;
            head = id;
        }

        // Whether an item can be within tolerance of a point, or meet a box: the checks before any exact work on its geometry.
        internal bool ItemNear(int id, Vector2d point, double tolerance)
        {
            return QuadtreeGeometry2d.NearRegion(entries[id].Box, point, tolerance);
        }

        internal bool ItemMeets(int id, in AxisAlignedBox2d box)
        {
            return Intersects(entries[id].Box, box);
        }

        struct FirstEqualVisitor : IQuadtreeVisitor2d
        {
            public LooseQuadtree2d Tree;
            public AxisAlignedBox2d Box;
            public int First;
            public bool TestRegion(AxisAlignedBox2d region) { return Intersects(region, Box); }
            public bool Visit(int id)
            {
                if (Equal(Tree.entries[id].Box, Box) && (First < 0 || id < First)) First = id;
                return false;
            }
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
            return value > double.MaxValue ? double.MaxValue : value < -double.MaxValue ? -double.MaxValue : value;
        }

        static AxisAlignedBox2d Loose(in AxisAlignedBox2d box)
        {
            double x = box.Max.x * 0.5 - box.Min.x * 0.5;
            double y = box.Max.y * 0.5 - box.Min.y * 0.5;
            return new AxisAlignedBox2d(Saturate(box.Min.x - x), Saturate(box.Min.y - y),
                Saturate(box.Max.x + x), Saturate(box.Max.y + y));
        }

        // Bit 1: the cell is cut in x, bit 2: in y. A cell more than twice as long as wide is cut across its length only.
        // Halving both would keep its shape, and items as wide as a long thin extent could then never leave the root.
        static int SplitAxes(AxisAlignedBox2d box, Vector2d center)
        {
            double width = box.Max.x * 0.5 - box.Min.x * 0.5, height = box.Max.y * 0.5 - box.Min.y * 0.5;
            bool cutX = center.x > box.Min.x && center.x < box.Max.x, cutY = center.y > box.Min.y && center.y < box.Max.y;
            int axes = (cutX && width >= height * 0.5 ? 1 : 0) | (cutY && height >= width * 0.5 ? 2 : 0);
            return axes != 0 ? axes : (cutX ? 1 : 0) | (cutY ? 2 : 0); // a long axis one ulp wide cannot be cut, the short one can
        }

        // The children an uncut axis does not select stay empty.
        static AxisAlignedBox2d ChildBox(AxisAlignedBox2d box, Vector2d center, int q, int axes)
        {
            return new AxisAlignedBox2d((q & 1) == 0 || (axes & 1) == 0 ? box.Min.x : center.x,
                (q & 2) == 0 || (axes & 2) == 0 ? box.Min.y : center.y,
                (q & 1) == 0 && (axes & 1) != 0 ? center.x : box.Max.x,
                (q & 2) == 0 && (axes & 2) != 0 ? center.y : box.Max.y);
        }

        // Leaves room on the sides that have to move, at least doubling them. Items arriving in sorted order
        // then rebuild the tree a logarithmic number of times, instead of once per insert.
        static AxisAlignedBox2d Grown(AxisAlignedBox2d region, AxisAlignedBox2d box)
        {
            AxisAlignedBox2d union = Union(region, box);
            double room = Math.Max(union.Max.x * 0.5 - union.Min.x * 0.5, union.Max.y * 0.5 - union.Min.y * 0.5) * 2;
            return new AxisAlignedBox2d(
                union.Min.x < region.Min.x ? Saturate(union.Min.x - room) : region.Min.x,
                union.Min.y < region.Min.y ? Saturate(union.Min.y - room) : region.Min.y,
                union.Max.x > region.Max.x ? Saturate(union.Max.x + room) : region.Max.x,
                union.Max.y > region.Max.y ? Saturate(union.Max.y + room) : region.Max.y);
        }

        static AxisAlignedBox2d Union(AxisAlignedBox2d a, AxisAlignedBox2d b)
        {
            return new AxisAlignedBox2d(Math.Min(a.Min.x, b.Min.x), Math.Min(a.Min.y, b.Min.y),
                Math.Max(a.Max.x, b.Max.x), Math.Max(a.Max.y, b.Max.y));
        }

        static bool Contains(in AxisAlignedBox2d a, in AxisAlignedBox2d b)
        {
            return a.Min.x <= b.Min.x && a.Min.y <= b.Min.y && a.Max.x >= b.Max.x && a.Max.y >= b.Max.y;
        }

        static bool Intersects(in AxisAlignedBox2d a, in AxisAlignedBox2d b)
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

        static double BoxDistanceTo(Vector2d point, in AxisAlignedBox2d box)
        {
            double dx = point.x < box.Min.x ? box.Min.x - point.x : point.x > box.Max.x ? point.x - box.Max.x : 0;
            double dy = point.y < box.Min.y ? box.Min.y - point.y : point.y > box.Max.y ? point.y - box.Max.y : 0;
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
