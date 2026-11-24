using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The Small loops of gParallel (#1502): ForRangeSmall, ForEachSmall and BlockStartEndSmall run their items on the
    /// calling thread until a time budget is spent and hand the rest to the parallel loop. A loop below the budget never
    /// leaves the calling thread (no wait for a pool worker), a long one does, and the result is that of ForRange.
    /// </summary>
    public class GParallelSmallLoopTests
    {
        // the budget knob is per thread: set it for one test body and restore it
        static void WithBudget(long ticks, Action a)
        {
            long old = gParallel.SmallLoopBudgetTicksOnThisThread;
            gParallel.SmallLoopBudgetTicksOnThisThread = ticks;
            try { a(); } finally { gParallel.SmallLoopBudgetTicksOnThisThread = old; }
        }

        // every budget that matters: default, handover after the first chunk, never, and no inline phase
        static readonly long[] Budgets = { 0, 1, 2000, long.MaxValue, -1 };

        [Fact]
        public void LoopBelowTheBudget_RunsOnTheCallingThread()
        {
            int me = Environment.CurrentManagedThreadId;
            gParallel.SmallLoopHandovers = 0;
            WithBudget(long.MaxValue, () => {
                var threads = new System.Collections.Concurrent.ConcurrentBag<int>();
                gParallel.ForRangeSmall(5000, (i) => threads.Add(Environment.CurrentManagedThreadId));
                gParallel.ForRangeSmall(5000, () => 0, (i, l) => threads.Add(Environment.CurrentManagedThreadId));
                gParallel.ForEachSmall(Enumerable.Range(0, 5000), (i) => threads.Add(Environment.CurrentManagedThreadId));
                gParallel.BlockStartEndSmall(0, 4999, (a, b) => threads.Add(Environment.CurrentManagedThreadId));
                Assert.Equal(5000 + 5000 + 5000 + 50, threads.Count);
                Assert.Equal(0, gParallel.SmallLoopHandovers);
                Assert.All(threads, t => Assert.Equal(me, t));
            });
        }

        [Fact]
        public void LoopAboveTheBudget_HandsTheRestOver_Once()
        {
            // (a budget of one tick is spent after the first chunk; the hook counts the handover, so the check does not depend on
            // whether the pool has a free thread)
            foreach (long budget in new long[] { 1, long.MaxValue }) {
                WithBudget(budget, () => {
                    int expected = (budget == 1) ? 1 : 0;
                    Action<Action> check = (run) => { gParallel.SmallLoopHandovers = 0; run(); Assert.Equal(expected, gParallel.SmallLoopHandovers); };
                    var sink = new int[200];
                    check(() => gParallel.ForRangeSmall(200, (i) => sink[i] = i));
                    check(() => gParallel.ForRangeSmall(200, () => 0, (i, l) => sink[i] = i));
                    check(() => gParallel.ForEachSmall(Enumerable.Range(0, 200), (i) => sink[i] = i));
                    check(() => gParallel.BlockStartEndSmall(0, 199, (a, b) => { }, 10));
                    // one item has nothing to hand over
                    gParallel.SmallLoopHandovers = 0;
                    gParallel.ForRangeSmall(1, (i) => { });
                    Assert.Equal(0, gParallel.SmallLoopHandovers);
                });
            }
            if (Environment.ProcessorCount >= 2) {
                // and the rest does run on other threads when the loop is slow (a 1 ms body is far above the default budget)
                var threads = new System.Collections.Concurrent.ConcurrentDictionary<int, bool>();
                gParallel.SmallLoopHandovers = 0;
                gParallel.ForRangeSmall(64, (i) => { threads[Environment.CurrentManagedThreadId] = true; Thread.Sleep(1); });
                Assert.Equal(1, gParallel.SmallLoopHandovers);
            }
        }

        [Fact]
        public void EveryIndexOnce_ForEveryBudget_AndTheResultIsTheSerialOne()
        {
            foreach (long budget in Budgets)
                WithBudget(budget, () => {
                    foreach (int n in new[] { 0, 1, 2, 3, 17, 1000, 20000 }) {
                        var serial = new long[n];
                        for (int i = 0; i < n; ++i) serial[i] = Mix(i);
                        for (int rep = 0; rep < 3; ++rep) {
                            var a = new long[n]; var hits = new int[n];
                            gParallel.ForRangeSmall(n, (i) => { a[i] = Mix(i); Interlocked.Increment(ref hits[i]); });
                            Assert.Equal(serial, a); Assert.All(hits, h => Assert.Equal(1, h));
                            var b = new long[n]; hits = new int[n];
                            gParallel.ForRangeSmall(n, () => new List<int>(), (i, buf) => { buf.Clear(); buf.Add(i); b[i] = Mix(buf[0]); Interlocked.Increment(ref hits[i]); });
                            Assert.Equal(serial, b); Assert.All(hits, h => Assert.Equal(1, h));
                            var c = new long[n]; hits = new int[n];
                            gParallel.ForEachSmall(Enumerable.Range(0, n), (i) => { c[i] = Mix(i); Interlocked.Increment(ref hits[i]); });
                            Assert.Equal(serial, c); Assert.All(hits, h => Assert.Equal(1, h));
                            // blocks of 100 and the rest: [0, n-1] covered once
                            hits = new int[n];
                            gParallel.BlockStartEndSmall(0, n - 1, (s, e) => { for (int i = s; i <= e; ++i) Interlocked.Increment(ref hits[i]); });
                            Assert.All(hits, h => Assert.Equal(1, h));
                        }
                    }
                });
        }

        [Fact]
        public void SerialOnThisThread_RunsTheSmallLoopsInTheTestOrder()
        {
            var seen = new List<int>();
            gParallel.SerialOnThisThread = true;
            gParallel.SerialOrderOnThisThread = -1;
            try {
                gParallel.ForRangeSmall(6, (i) => seen.Add(i));
                gParallel.ForEachSmall(Enumerable.Range(0, 6), (i) => seen.Add(10 + i));
            } finally {
                gParallel.SerialOrderOnThisThread = 0;
                gParallel.SerialOnThisThread = false;
            }
            Assert.Equal(new[] { 5, 4, 3, 2, 1, 0 }, seen.Take(6));
            Assert.Equal(6, seen.Skip(6).Count());
        }

        [Fact]
        public void NextChunk_DoublesWhileTheBudgetLasts_AndDoesNotOverflowOnAnUnlimitedBudget()
        {
            // 10 ticks for 1000 items so far, budget left: 1000 ticks -> room for 100000 items, so double the last chunk
            Assert.Equal(8, gParallel.next_chunk(4, 1000, 0, 10, 1010));
            // little budget left: only what fits (at least one item)
            Assert.Equal(5, gParallel.next_chunk(4, 1000, 0, 1000, 1005));
            Assert.Equal(1, gParallel.next_chunk(4, 1000, 0, 1000, 1000));
            // unlimited budget: (limit - now) * done would wrap to a negative value and make the chunk 1
            Assert.Equal(8, gParallel.next_chunk(4, 1000, 0, 10, long.MaxValue));
            Assert.Equal(8, gParallel.next_chunk(4, int.MaxValue, 0, 1, long.MaxValue));
        }

        static Type ThrownType(Action a)
        {
            try { a(); } catch (Exception e) { return e.GetType(); }
            return null;
        }

        [Fact]
        public void ExceptionOfTheBody_HasTheTypeOfTheLoopItReplaces_InTheInlinePartAndTheParallelPart()
        {
            foreach (long budget in new long[] { 0, 1, long.MaxValue, -1 })
                WithBudget(budget, () => {
                    // ForRange: the exception of a single body as it is
                    foreach (int bad in new[] { 0, 7, 500 }) {
                        Assert.Equal(typeof(InvalidOperationException), ThrownType(() => gParallel.ForRangeSmall(1000, (i) => { if (i == bad) throw new InvalidOperationException("x"); })));
                        Assert.Equal(typeof(InvalidOperationException), ThrownType(() => gParallel.ForRangeSmall(1000, () => 0, (i, l) => { if (i == bad) throw new InvalidOperationException("x"); })));
                        // ForEach: always the AggregateException of Parallel.ForEach
                        Assert.Equal(ThrownType(() => gParallel.ForEach(Enumerable.Range(0, 1000), (i) => { if (i == bad) throw new InvalidOperationException("x"); })),
                            ThrownType(() => gParallel.ForEachSmall(Enumerable.Range(0, 1000), (i) => { if (i == bad) throw new InvalidOperationException("x"); })));
                        Assert.Equal(typeof(AggregateException), ThrownType(() => gParallel.ForEachSmall(Enumerable.Range(0, 1000), (i) => { if (i == bad) throw new InvalidOperationException("x"); })));
                    }
                    // the source throwing
                    Func<IEnumerable<int>> failing = () => Enumerable.Range(0, 100).Select(i => { if (i == 50) throw new InvalidOperationException("x"); return i; });
                    Assert.Equal(ThrownType(() => gParallel.ForEach(failing(), (i) => { })), ThrownType(() => gParallel.ForEachSmall(failing(), (i) => { })));
                    // BlockStartEnd: a full block through ForEach (AggregateException), the leftover block inline (as it is)
                    foreach (int bad in new[] { 0, 250, 999 }) {
                        Action<int, int> body = (a, b) => { if (a <= bad && bad <= b) throw new InvalidOperationException("x"); };
                        Assert.Equal(ThrownType(() => gParallel.BlockStartEnd(0, 999, body, 100)), ThrownType(() => gParallel.BlockStartEndSmall(0, 999, body, 100)));
                        Assert.Equal(ThrownType(() => gParallel.BlockStartEnd(0, 1049, body, 100)), ThrownType(() => gParallel.BlockStartEndSmall(0, 1049, body, 100)));
                    }
                    Assert.Equal(typeof(InvalidOperationException), ThrownType(() => gParallel.BlockStartEndSmall(0, 1049, (a, b) => { if (b == 1049) throw new InvalidOperationException("x"); }, 100)));
                    Assert.Equal(typeof(AggregateException), ThrownType(() => gParallel.BlockStartEndSmall(0, 1049, (a, b) => { if (a == 0) throw new InvalidOperationException("x"); }, 100)));
                });
        }

        [Fact]
        public void Local_IsNeverUsedByTwoThreadsAtOnce_AndTheInlineLocalIsNotSharedWithTheParallelPart()
        {
            foreach (long budget in new long[] { 1, 2000, long.MaxValue, -1 })
                WithBudget(budget, () => {
                    int overlap = 0, items = 0;
                    var locals = new System.Collections.Concurrent.ConcurrentDictionary<int[], int>();
                    gParallel.ForRangeSmall(4000, () => new int[1], (i, busy) => {
                        // (a local is in use by one body at a time: the flag is set while the body runs)
                        if (Interlocked.Exchange(ref busy[0], 1) != 0)
                            Interlocked.Increment(ref overlap);
                        locals.AddOrUpdate(busy, 1, (k, v) => v + 1);
                        Interlocked.Increment(ref items);
                        Thread.SpinWait(20);
                        Volatile.Write(ref busy[0], 0);
                    });
                    Assert.Equal(0, overlap);
                    Assert.Equal(4000, items);
                    Assert.Equal(4000, locals.Values.Sum());
                });
        }

        [Fact]
        public void MeshNormals_AreTheSameBits_SerialInlineAndParallel()
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 40 }.Generate().MakeDMesh();
            Func<Vector3d[]> normals = () => { var n = new MeshNormals(mesh); n.Compute(); return n.Normals.ToArray(); };
            Vector3d[] reference;
            gParallel.SerialOnThisThread = true;
            try { reference = normals(); } finally { gParallel.SerialOnThisThread = false; }
            foreach (long budget in Budgets)
                WithBudget(budget, () => {
                    for (int rep = 0; rep < 3; ++rep) {
                        Vector3d[] r = normals();
                        Assert.Equal(reference.Length, r.Length);
                        for (int i = 0; i < r.Length; ++i)
                            Assert.True(BitConverter.DoubleToInt64Bits(r[i].x) == BitConverter.DoubleToInt64Bits(reference[i].x)
                                && BitConverter.DoubleToInt64Bits(r[i].y) == BitConverter.DoubleToInt64Bits(reference[i].y)
                                && BitConverter.DoubleToInt64Bits(r[i].z) == BitConverter.DoubleToInt64Bits(reference[i].z), "normal " + i);
                    }
                });
        }

        static long Hash(DMesh3 m)
        {
            ulong h = 1469598103934665603UL;
            Action<long> add = (v) => { h = (h ^ (ulong)v) * 1099511628211UL; };
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                add(v); add(BitConverter.DoubleToInt64Bits(p.x)); add(BitConverter.DoubleToInt64Bits(p.y)); add(BitConverter.DoubleToInt64Bits(p.z));
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                add(t); add(tv.a); add(tv.b); add(tv.c);
            }
            return (long)h;
        }

        static DMesh3 BumpySphere(int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, p * (1 + 0.05 * Math.Sin(7 * p.x) * Math.Cos(5 * p.y) * Math.Sin(6 * p.z)));
            }
            return m;
        }

        // f serially on this thread, then under every budget (twice): the same bits
        static void AssertSameUnderEveryBudget(Func<long> f)
        {
            long reference;
            gParallel.SerialOnThisThread = true;
            try { reference = f(); } finally { gParallel.SerialOnThisThread = false; }
            foreach (long budget in Budgets)
                WithBudget(budget, () => {
                    for (int rep = 0; rep < 2; ++rep)
                        Assert.Equal(reference, f());
                });
        }

        [Fact]
        public void Reducer_IsTheSameBits_SerialInlineAndParallel()
        {
            DMesh3 input = BumpySphere(24);
            AssertSameUnderEveryBudget(() => {
                var m = new DMesh3(input);
                var r = new Reducer(m);
                r.ReduceToTriangleCount(600);
                return Hash(m);
            });
        }

        [Fact]
        public void Remesher_IsTheSameBits_SerialInlineAndParallel()
        {
            DMesh3 input = BumpySphere(14);
            foreach (bool pro in new[] { false, true })
                AssertSameUnderEveryBudget(() => {
                    var m = new DMesh3(input);
                    var r = pro ? new RemesherPro(m) : new Remesher(m);
                    r.SetTargetEdgeLength(0.12);
                    r.SmoothSpeedT = 0.5;
                    for (int i = 0; i < 3; ++i) r.BasicRemeshPass();
                    return Hash(m);
                });
        }

        static long Mix(int i)
        {
            ulong z = (ulong)i * 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            return (long)(z ^ (z >> 31));
        }
    }
}
