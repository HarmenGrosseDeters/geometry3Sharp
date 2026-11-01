using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests.Spatial
{
    /// <summary>
    /// Random sequences of inserts, removals and queries on one tree, each query checked against a scan of a plain
    /// list. It shares no helper with the tree: the point is to find state left wrong by an earlier operation.
    /// </summary>
    public class QuadtreeStateStressTests
    {
        const double Margin = 0.0002;

        /// <summary>Scales and offsets of the terrain use cases, and sets that force ties, duplicates and thin extents.</summary>
        public enum Layout { Unit, Utm, Negative, Tiny, Lattice, OneLine, Clusters, LongThin }

        static AxisAlignedBox2d NextBox(Random random, Layout layout)
        {
            switch (layout) {
                case Layout.Utm: return RandomBox(random, 2539000, 5640000, 5000, 5);
                case Layout.Negative: return RandomBox(random, -700, -300, 400, 2);
                case Layout.Tiny: return RandomBox(random, 1, 1, 1e-6, 1e-8);
                case Layout.Lattice: {
                        double x = random.Next(0, 32), y = random.Next(0, 32);
                        return new AxisAlignedBox2d(x, y, x + random.Next(0, 3), y + random.Next(0, 3));
                    }
                case Layout.OneLine: {
                        double x = random.Next(0, 1000) * 0.25;
                        return new AxisAlignedBox2d(x, 7, x + random.Next(0, 4) * 0.25, 7);
                    }
                case Layout.Clusters: {
                        double x = random.Next(0, 3) * 100, y = random.Next(0, 3) * 100;
                        return new AxisAlignedBox2d(x, y, x + random.Next(0, 2), y);
                    }
                case Layout.LongThin: {
                        double x = random.Next(0, 64), y = random.Next(0, 64);
                        return random.Next(2) == 0
                            ? new AxisAlignedBox2d(0, y, 64, y + 0.125)
                            : new AxisAlignedBox2d(x, 0, x + 0.125, 64);
                    }
                default: return RandomBox(random, 0, 0, 100, 1);
            }
        }

        static AxisAlignedBox2d RandomBox(Random random, double x, double y, double range, double size)
        {
            double minX = x + random.NextDouble() * range, minY = y + random.NextDouble() * range;
            return new AxisAlignedBox2d(minX, minY, minX + random.NextDouble() * size, minY + random.NextDouble() * size);
        }

        static Vector2d NextPoint(Random random, AxisAlignedBox2d around)
        {
            if (random.Next(4) == 0) // on a corner of the extent, or on its centre lines
                return new Vector2d(random.Next(2) == 0 ? around.Min.x : (around.Min.x + around.Max.x) * 0.5,
                    random.Next(2) == 0 ? around.Max.y : (around.Min.y + around.Max.y) * 0.5);
            return new Vector2d(around.Min.x + random.NextDouble() * 1.5 * around.Width - 0.25 * around.Width,
                around.Min.y + random.NextDouble() * 1.5 * around.Height - 0.25 * around.Height);
        }

        static bool Overlap(AxisAlignedBox2d a, AxisAlignedBox2d b)
        {
            return a.Min.x <= b.Max.x && b.Min.x <= a.Max.x && a.Min.y <= b.Max.y && b.Min.y <= a.Max.y;
        }

        static double SquaredDistance(Vector2d p, AxisAlignedBox2d box)
        {
            double dx = Math.Max(0, Math.Max(box.Min.x - p.x, p.x - box.Max.x));
            double dy = Math.Max(0, Math.Max(box.Min.y - p.y, p.y - box.Max.y));
            return dx * dx + dy * dy;
        }

        /// <summary>Slab clipping against the box grown by margin. Only used well away from the rounding of its comparisons.</summary>
        static bool SegmentTouches(Vector2d a, Vector2d b, AxisAlignedBox2d box, double margin)
        {
            double enter = 0, exit = 1;
            for (int axis = 0; axis < 2; ++axis) {
                double from = axis == 0 ? a.x : a.y, delta = (axis == 0 ? b.x : b.y) - from;
                double min = (axis == 0 ? box.Min.x : box.Min.y) - margin, max = (axis == 0 ? box.Max.x : box.Max.y) + margin;
                if (delta == 0) {
                    if (from < min || from > max) return false;
                    continue;
                }
                double t0 = (min - from) / delta, t1 = (max - from) / delta;
                enter = Math.Max(enter, Math.Min(t0, t1));
                exit = Math.Min(exit, Math.Max(t0, t1));
            }
            return enter <= exit;
        }

        static void CheckRectangle(LooseQuadtree2d tree, Dictionary<int, AxisAlignedBox2d> live, AxisAlignedBox2d query, List<int> buffer, string context)
        {
            tree.QueryRectangle(query, buffer);
            int[] expected = live.Where(e => Overlap(e.Value, query)).Select(e => e.Key).OrderBy(i => i).ToArray();
            int[] actual = buffer.OrderBy(i => i).ToArray();
            if (!expected.SequenceEqual(actual))
                Assert.Fail($"{context}: rectangle {query}: expected {string.Join(",", expected)}, got {string.Join(",", actual)}");
        }

        static void CheckSegment(LooseQuadtree2d tree, Dictionary<int, AxisAlignedBox2d> live, Vector2d a, Vector2d b, List<int> buffer, string context)
        {
            tree.QuerySegment(a, b, buffer);
            var found = new HashSet<int>(buffer);
            Assert.Equal(buffer.Count, found.Count);
            foreach (KeyValuePair<int, AxisAlignedBox2d> entry in live) {
                // Inside the band of the margin either answer would pass a floating point oracle, so only the clear cases are judged.
                if (!found.Contains(entry.Key) && SegmentTouches(a, b, entry.Value, Margin * 0.999))
                    Assert.Fail($"{context}: segment {a} {b} misses {entry.Key} {entry.Value}");
                if (found.Contains(entry.Key) && !SegmentTouches(a, b, entry.Value, Margin * 1.001))
                    Assert.Fail($"{context}: segment {a} {b} returns the far item {entry.Key} {entry.Value}");
            }
            foreach (int id in buffer) Assert.True(live.ContainsKey(id), $"{context}: dead id {id}");
        }

        static void CheckNearest(LooseQuadtree2d tree, Dictionary<int, AxisAlignedBox2d> live, Vector2d p, int k, List<int> buffer, string context)
        {
            // The tree compares rounded square roots, so squared distances that round to the same root count as ties.
            int[] order = live.Keys.OrderBy(id => Math.Sqrt(SquaredDistance(p, live[id]))).ThenBy(id => id).ToArray();
            int nearest = tree.FindNearest(p);
            if (nearest != (order.Length == 0 ? -1 : order[0]))
                Assert.Fail($"{context}: nearest {p}: expected {(order.Length == 0 ? -1 : order[0])}, got {nearest}");
            tree.FindNearestK(p, k, buffer);
            int[] expected = order.Take(k).ToArray();
            if (!expected.SequenceEqual(buffer))
                Assert.Fail($"{context}: nearest {k} {p}: expected {string.Join(",", expected)}, got {string.Join(",", buffer)}");
        }

        static void CheckLive(LooseQuadtree2d tree, Dictionary<int, AxisAlignedBox2d> live)
        {
            Assert.Equal(live.Count, tree.Count);
            foreach (KeyValuePair<int, AxisAlignedBox2d> entry in live) {
                Assert.True(tree.ContainsId(entry.Key));
                Assert.Equal(entry.Value.Min, tree.GetBounds(entry.Key).Min);
                Assert.Equal(entry.Value.Max, tree.GetBounds(entry.Key).Max);
            }
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (Layout layout in Enum.GetValues(typeof(Layout)))
                foreach (int seed in new[] { 1, 2, 3 })
                    foreach (int initial in new[] { 0, 40, 600 })
                        yield return new object[] { layout, seed, initial };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void RandomOperations_MatchAScanOfAPlainList(Layout layout, int seed, int initial)
        {
            var random = new Random(seed);
            AxisAlignedBox2d[] start = Enumerable.Range(0, initial).Select(i => NextBox(random, layout)).ToArray();
            var tree = new LooseQuadtree2d(start, false);
            var live = new Dictionary<int, AxisAlignedBox2d>();
            for (int i = 0; i < initial; ++i) live[i] = start[i];
            var removed = new List<int>();
            var history = new List<AxisAlignedBox2d>(start);
            var buffer = new List<int>();
            int nextId = initial;

            for (int step = 0; step < 900; ++step) {
                string context = $"{layout}, seed {seed}, step {step}";
                AxisAlignedBox2d around = tree.Count == 0 && history.Count == 0 ? NextBox(random, layout) : tree.Extent;
                switch (random.Next(10)) {
                    case 0:
                    case 1:
                    case 2: {
                            AxisAlignedBox2d box = random.Next(8) == 0 && history.Count > 0 ? history[random.Next(history.Count)] : NextBox(random, layout);
                            Assert.Equal(nextId, tree.Insert(box));
                            live[nextId++] = box;
                            history.Add(box);
                            break;
                        }
                    case 3:
                        if (live.Count > 0) {
                            int id = live.Keys.ElementAt(random.Next(live.Count));
                            Assert.True(tree.Remove(id));
                            Assert.False(tree.Remove(id));
                            Assert.False(tree.ContainsId(id));
                            live.Remove(id);
                            removed.Add(id);
                        }
                        break;
                    case 4:
                        if (history.Count > 0) {
                            // Removing by value takes the first live equal snapshot in insertion order.
                            AxisAlignedBox2d box = history[random.Next(history.Count)];
                            int[] equal = live.Where(e => e.Value.Min == box.Min && e.Value.Max == box.Max).Select(e => e.Key).OrderBy(i => i).ToArray();
                            Assert.Equal(equal.Length > 0, tree.Remove(box));
                            if (equal.Length > 0) {
                                live.Remove(equal[0]);
                                removed.Add(equal[0]);
                            }
                        }
                        break;
                    case 5: {
                            Vector2d a = NextPoint(random, around), b = NextPoint(random, around);
                            CheckRectangle(tree, live, new AxisAlignedBox2d(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y)), buffer, context);
                            break;
                        }
                    case 6: {
                            Vector2d a = NextPoint(random, around), b = NextPoint(random, around);
                            CheckSegment(tree, live, a, random.Next(5) == 0 ? a : b, buffer, context);
                            break;
                        }
                    case 7:
                        CheckNearest(tree, live, NextPoint(random, around), random.Next(0, 24), buffer, context);
                        break;
                    case 8:
                        if (live.Count > 0) { // the exact bounds of a live item must find it
                            AxisAlignedBox2d box = live.Values.ElementAt(random.Next(live.Count));
                            CheckRectangle(tree, live, box, buffer, context);
                        }
                        break;
                    default:
                        if (random.Next(40) == 0) { // empty it, then keep using it
                            foreach (int id in live.Keys.ToArray()) Assert.True(tree.Remove(id));
                            removed.AddRange(live.Keys);
                            live.Clear();
                        }
                        break;
                }
            }

            foreach (int id in removed) Assert.False(tree.ContainsId(id));
            CheckLive(tree, live);
            foreach (AxisAlignedBox2d box in live.Values) Assert.True(tree.Extent.Min.x <= box.Min.x && tree.Extent.Min.y <= box.Min.y && tree.Extent.Max.x >= box.Max.x && tree.Extent.Max.y >= box.Max.y);
        }

        [Fact]
        public void InsertingInSortedOrder_DoesNotRebuildEachTime()
        {
            // 20000 of them took 22 s when every insert beyond the extent rebuilt the tree. Now tens of milliseconds.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var line = new LooseQuadtree2d();
            var diagonal = new PointQuadtree2d();
            for (int i = 0; i < 20000; ++i) {
                line.Insert(new AxisAlignedBox2d(new Vector2d(i, 0)));
                diagonal.Insert(new Vector3d(-i, -i, 0));
            }
            Assert.True(watch.Elapsed.TotalSeconds < 5, $"took {watch.Elapsed.TotalSeconds} s");
            Assert.Equal(new AxisAlignedBox2d(0, 0, 19999, 0), line.Extent);
            Assert.Equal(new AxisAlignedBox2d(-19999, -19999, 0, 0), diagonal.Extent);
            var found = new List<int>();
            line.QueryRectangle(new AxisAlignedBox2d(7000, 0, 7001, 0), found);
            Assert.Equal(new[] { 7000, 7001 }, found.OrderBy(i => i));
        }

        [Fact]
        public void RemovingByValue_UsesTheIndex_AndTakesTheFirstEqualItem()
        {
            // A scan of the whole list per removal: 40000 removals out of 120000 items took minutes.
            Vector3d[] points = Enumerable.Range(0, 120000).Select(i => new Vector3d(i % 400, i / 400, 0)).ToArray();
            var tree = new PointQuadtree2d(points);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 120000; i += 3) Assert.True(tree.Remove(points[i]));
            Assert.True(watch.Elapsed.TotalSeconds < 5, $"took {watch.Elapsed.TotalSeconds} s");
            Assert.Equal(80000, tree.Count);

            Vector3d p = new Vector3d(1000, 1000, 5);
            int a = tree.Insert(p), b = tree.Insert(p), c = tree.Insert(p);
            Assert.False(tree.Remove(new Vector3d(1000, 1000, 6))); // same bounds, other z
            Assert.True(tree.Remove(b));
            Assert.True(tree.Remove(p));
            Assert.False(tree.ContainsId(a));
            Assert.True(tree.ContainsId(c));
            Assert.True(tree.Remove(p));
            Assert.False(tree.Remove(p));
        }

        [Fact]
        public void AdaptersRemoveByValue_TheFirstLiveEqualItem()
        {
            var segment = new Segment3d(new Vector3d(1, 1, 0), new Vector3d(3, 2, 0));
            var segments = new SegmentQuadtree2d(Enumerable.Range(0, 200).Select(i => new Segment3d(new Vector3d(i, i, 0), new Vector3d(i + 1, i, 0))).ToArray());
            int s0 = segments.Insert(segment), s1 = segments.Insert(segment);
            Assert.True(segments.Remove(segment));
            Assert.False(segments.ContainsId(s0));
            Assert.True(segments.ContainsId(s1));
            Assert.False(segments.Remove(new Segment3d(new Vector3d(3, 2, 0), new Vector3d(1, 1, 0)))); // reversed is another item

            var triangle = new Triangle3d(new Vector3d(1, 1, 0), new Vector3d(3, 1, 0), new Vector3d(1, 3, 0));
            var triangles = new TriangleQuadtree2d(Enumerable.Range(0, 200).Select(i => new Triangle3d(new Vector3d(i, i, 0), new Vector3d(i + 1, i, 0), new Vector3d(i, i + 1, 0))).ToArray());
            int t0 = triangles.Insert(triangle), t1 = triangles.Insert(triangle);
            Assert.True(triangles.Remove(triangle));
            Assert.False(triangles.ContainsId(t0));
            Assert.True(triangles.ContainsId(t1));
        }

        [Theory]
        [InlineData(Layout.Unit)]
        [InlineData(Layout.Utm)]
        [InlineData(Layout.Lattice)]
        [InlineData(Layout.Clusters)]
        public void GrowingOutwardKeepsEveryItemFindable(Layout layout)
        {
            // Each insert lies beyond the extent so far, in a different direction: every one has to move the root.
            var random = new Random(5);
            var tree = new LooseQuadtree2d();
            var live = new Dictionary<int, AxisAlignedBox2d>();
            var buffer = new List<int>();
            for (int i = 0; i < 400; ++i) {
                AxisAlignedBox2d box = NextBox(random, layout);
                double reach = i * 3.0;
                var far = new Vector2d(i % 4 == 0 ? reach : i % 4 == 1 ? -reach : 0, i % 4 == 2 ? reach : i % 4 == 3 ? -reach : 0);
                box = new AxisAlignedBox2d(box.Min + far, box.Max + far);
                live[tree.Insert(box)] = box;
                if (i % 16 == 0) {
                    CheckRectangle(tree, live, box, buffer, "growth");
                    CheckNearest(tree, live, box.Center, 5, buffer, "growth");
                }
            }
            CheckRectangle(tree, live, tree.Extent, buffer, "growth");
            CheckLive(tree, live);
        }
    }
}
