using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The helpers that the performance work on the exact booleans changed or added (benchmarks/BooleanComparison,
    /// profile): they must give the same answers as the code they replace.
    /// </summary>
    public class BooleanPerformanceTests
    {
        static List<AxisAlignedBox3d> RandomBoxes(Random rand, int n, double size)
        {
            var boxes = new List<AxisAlignedBox3d>();
            for (int i = 0; i < n; ++i) {
                var c = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * 10;
                var e = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * size;
                // (some boxes touch exactly: corners on a coarse grid)
                if (i % 5 == 0) {
                    c = new Vector3d(Math.Round(c.x), Math.Round(c.y), Math.Round(c.z));
                    e = new Vector3d(0.5, 0.5, 0.5);
                }
                boxes.Add(new AxisAlignedBox3d(c - e, c + e));
            }
            return boxes;
        }

        static bool Overlap(AxisAlignedBox3d a, AxisAlignedBox3d b)
        {
            return !(a.Max.x < b.Min.x || a.Min.x > b.Max.x || a.Max.y < b.Min.y || a.Min.y > b.Max.y || a.Max.z < b.Min.z || a.Min.z > b.Max.z);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(9)]
        [InlineData(1000)]
        public void TriangleBoxTree_Query_FindsExactlyTheOverlappingBoxes(int n)
        {
            var rand = new Random(n);
            var boxes = RandomBoxes(rand, n, 0.3);
            var tree = new TriangleBoxTree(boxes);
            var result = new List<int>();
            for (int q = 0; q < 200; ++q) {
                AxisAlignedBox3d query = (q % 4 == 0 && n > 0) ? boxes[rand.Next(n)] : RandomBoxes(rand, 1, 1.5)[0];
                result.Clear();
                tree.Query(query, result);
                var expected = Enumerable.Range(0, n).Where(i => Overlap(boxes[i], query)).ToList();
                Assert.Equal(expected, result.OrderBy(i => i).ToList());
                Assert.Equal(result.Count, result.Distinct().Count());
                // the callback version reports the same boxes in the same order
                var called = new List<int>();
                tree.Query(query, (i) => called.Add(i));
                Assert.Equal(result, called);
            }
            // empty boxes (a mesh without triangles) overlap nothing
            var withEmpty = new List<AxisAlignedBox3d>(boxes) { AxisAlignedBox3d.Empty };
            result.Clear();
            new TriangleBoxTree(withEmpty).Query(new AxisAlignedBox3d(new Vector3d(-100, -100, -100), new Vector3d(100, 100, 100)), result);
            Assert.Equal(Enumerable.Range(0, n).ToList(), result.OrderBy(i => i).ToList());
        }

        [Fact]
        public void TriangleBoxTree_QuerySegment_FindsAllHitBoxes()
        {
            var rand = new Random(5);
            var boxes = RandomBoxes(rand, 500, 0.3);
            var tree = new TriangleBoxTree(boxes);
            for (int q = 0; q < 200; ++q) {
                Vector3d p = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * 10;
                Vector3d d = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5) * 20;
                if (q % 3 == 0) d.y = 0;     // axis-parallel segments
                var hits = new List<int>();
                tree.QuerySegment(p, p + d, 1e-9, (i) => hits.Add(i));
                Assert.Equal(hits.Count, hits.Distinct().Count());
                // every box the segment passes through (sampled densely) is reported
                for (int i = 0; i < boxes.Count; ++i) {
                    bool hit = false;
                    for (int s = 0; s <= 2000 && hit == false; ++s)
                        hit = boxes[i].Contains(p + (s / 2000.0) * d);
                    if (hit)
                        Assert.Contains(i, hits);
                }
            }
        }

        [Fact]
        public void SmallListSet_FindByKey_MatchesTheIterator()
        {
            // lists of up to 30 values (spilled to the linked list beyond 8), keys with repeats: the first match in
            // the order of ValueItr
            var rand = new Random(3);
            var set = new SmallListSet();
            set.Resize(50);
            var values = new DVector<int>();
            for (int v = 0; v < 400; ++v)
                for (int k = 0; k < 4; ++k)
                    values.Add(k == 1 ? rand.Next(20) : -7);
            for (int l = 0; l < 50; ++l) {
                set.AllocateAt(l);
                int count = rand.Next(31);
                for (int k = 0; k < count; ++k)
                    set.Insert(l, rand.Next(400));
            }
            for (int l = 0; l < 50; ++l)
                for (int key = -1; key < 21; ++key) {
                    int expected = -1;
                    foreach (int v in set.ValueItr(l))
                        if (values[4 * v + 1] == key) { expected = v; break; }
                    Assert.Equal(expected, set.FindByKey(l, values, key, -1));
                }
        }

        [Fact]
        public void DMesh3_FindEdge_HighValence()
        {
            // a fan of 40 triangles around one vertex (more than the 8 edges of a list block)
            var mesh = new DMesh3();
            int c = mesh.AppendVertex(Vector3d.Zero);
            var ring = new List<int>();
            for (int k = 0; k < 40; ++k)
                ring.Add(mesh.AppendVertex(new Vector3d(Math.Cos(k * Math.PI / 20), Math.Sin(k * Math.PI / 20), 0)));
            for (int k = 0; k < 40; ++k)
                mesh.AppendTriangle(c, ring[k], ring[(k + 1) % 40]);
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(eid);
                Assert.Equal(eid, mesh.FindEdge(ev.a, ev.b));
                Assert.Equal(eid, mesh.FindEdge(ev.b, ev.a));
            }
            Assert.Equal(DMesh3.InvalidID, mesh.FindEdge(ring[0], ring[2]));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(1000, 1)]
        [InlineData(1000, 2)]
        [InlineData(100000, 3)]
        public void SortByKey_SortsStably(int n, int kind)
        {
            // edge keys (a << 32 | b): small and large ids, many equal keys; items in increasing order, so a stable
            // sort keeps equal keys' items increasing (with one, two, three and four digit passes)
            var rand = new Random(n + kind);
            var keys = new long[n];
            for (int i = 0; i < n; ++i) {
                long a = rand.Next(kind == 1 ? 50 : kind == 2 ? 70000 : int.MaxValue), b = rand.Next(kind == 1 ? 200 : 1 << 20);
                keys[i] = (kind == 3 && i % 3 == 0) ? keys[rand.Next(Math.Max(1, i))] : ((a << 32) | b);
            }
            var items = Enumerable.Range(0, n).ToArray();
            var expected = keys.Select((k, i) => (k, i)).OrderBy(p => p.k).ThenBy(p => p.i).ToArray();
            ExactMeshBoolean.sort_by_key(keys, items);
            Assert.Equal(expected.Select(p => p.k).ToArray(), keys);
            Assert.Equal(expected.Select(p => p.i).ToArray(), items);
        }

        [Fact]
        public void ForRange_ThrowsTheExceptionOfTheBody()
        {
            // (not an AggregateException: as the serial loops before, eg ExactMeshBoolean's "no non-degenerate ray")
            Assert.Throws<InvalidOperationException>(() => gParallel.ForRange(1000, (i) => { if (i == 500) throw new InvalidOperationException("x"); }));
            Assert.Throws<InvalidOperationException>(() => gParallel.ForRange(1000, () => 0, (i, local) => { if (i == 7) throw new InvalidOperationException("x"); }));
        }

        [Fact]
        public void ForRange_VisitsEveryIndexOnce_WithItsOwnLocalState()
        {
            foreach (int n in new[] { 0, 1, 17, 100000 }) {
                var count = new int[n];
                gParallel.ForRange(n, (i) => System.Threading.Interlocked.Increment(ref count[i]));
                Assert.All(count, c => Assert.Equal(1, c));
                var local = new int[n];
                gParallel.ForRange(n, () => new List<int>(), (i, buffer) => {
                    // (a buffer is never used by two threads at once: it holds only this index while the body runs)
                    buffer.Clear();
                    buffer.Add(i);
                    System.Threading.Thread.SpinWait(10);
                    local[i] = buffer.Single();
                });
                Assert.Equal(Enumerable.Range(0, n).ToArray(), local);
            }
            // serial on this thread: in order, on the calling thread, one local
            gParallel.SerialOnThisThread = true;
            try {
                var order = new List<int>();
                int thread = Environment.CurrentManagedThreadId;
                var locals = new HashSet<List<int>>();
                gParallel.ForRange(100, () => new List<int>(), (i, buffer) => {
                    Assert.Equal(thread, Environment.CurrentManagedThreadId);
                    locals.Add(buffer);
                    order.Add(i);
                });
                gParallel.ForRange(100, (i) => order.Add(100 + i));
                Assert.Equal(Enumerable.Range(0, 200).ToList(), order);
                Assert.Single(locals);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
        }
    }
}
