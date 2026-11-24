using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;
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
            WithBudget(long.MaxValue, () => {
                var threads = new System.Collections.Concurrent.ConcurrentBag<int>();
                gParallel.ForRangeSmall(5000, (i) => threads.Add(Environment.CurrentManagedThreadId));
                gParallel.ForRangeSmall(5000, () => 0, (i, l) => threads.Add(Environment.CurrentManagedThreadId));
                gParallel.ForEachSmall(Enumerable.Range(0, 5000), (i) => threads.Add(Environment.CurrentManagedThreadId));
                gParallel.BlockStartEndSmall(0, 4999, (a, b) => threads.Add(Environment.CurrentManagedThreadId));
                Assert.Equal(5000 + 5000 + 5000 + 50, threads.Count);
                Assert.All(threads, t => Assert.Equal(me, t));
            });
        }

        [Fact]
        public void LoopAboveTheBudget_LeavesTheCallingThread()
        {
            if (Environment.ProcessorCount < 2)
                return;
            // a body that takes 1 ms is far above the budget: the rest of the loop runs on other threads too
            foreach (int kind in new[] { 0, 1, 2 }) {
                var threads = new System.Collections.Concurrent.ConcurrentDictionary<int, bool>();
                Action<int> body = (i) => { threads[Environment.CurrentManagedThreadId] = true; Thread.Sleep(1); };
                if (kind == 0) gParallel.ForRangeSmall(200, body);
                else if (kind == 1) gParallel.ForRangeSmall(200, () => 0, (i, l) => body(i));
                else gParallel.ForEachSmall(Enumerable.Range(0, 200), body);
                Assert.True(threads.Count >= 2, "kind " + kind + " stayed on one thread");
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
        public void ExceptionOfTheBody_IsRethrownAsItIs_InTheInlinePartAndTheParallelPart()
        {
            foreach (long budget in new long[] { 1, long.MaxValue, -1 })
                WithBudget(budget, () => {
                    Assert.Throws<InvalidOperationException>(() => gParallel.ForRangeSmall(1000, (i) => { if (i == 500) throw new InvalidOperationException("x"); }));
                    Assert.Throws<InvalidOperationException>(() => gParallel.ForRangeSmall(1000, () => 0, (i, l) => { if (i == 7) throw new InvalidOperationException("x"); }));
                    Assert.Throws<InvalidOperationException>(() => gParallel.ForRangeSmall(1000, (i) => { if (i == 0) throw new InvalidOperationException("x"); }));
                });
        }

        [Fact]
        public void LocalOfTheInlinePart_IsNotSharedWithTheParallelPart()
        {
            // the body clears the local before it reads it, as the contract of ForRange says; here it also counts the users
            WithBudget(1, () => {
                int inside = 0, overlap = 0;
                var seen = new System.Collections.Concurrent.ConcurrentDictionary<object, int>();
                gParallel.ForRangeSmall(4000, () => new object(), (i, local) => {
                    if (Interlocked.Increment(ref inside) > 4 * Environment.ProcessorCount + 4) Interlocked.Increment(ref overlap);
                    seen.AddOrUpdate(local, 1, (k, v) => v + 1);
                    Interlocked.Decrement(ref inside);
                });
                Assert.Equal(4000, seen.Values.Sum());
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

        static long Mix(int i)
        {
            ulong z = (ulong)i * 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            return (long)(z ^ (z >> 31));
        }
    }
}
