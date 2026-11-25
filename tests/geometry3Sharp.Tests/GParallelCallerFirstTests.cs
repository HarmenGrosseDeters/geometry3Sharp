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
    /// The parallel loops of gParallel run caller first (#1502): the calling thread takes chunks of the items at once and waits
    /// only for chunks a helper has taken, never for a helper that has not started. Parallel.For, which the loops used before,
    /// waited for every task it had queued: from a thread outside the pool, with the pool busy, a loop of a few trivial items
    /// waited until the pool ran its tasks (292 ms per call measured, benchmarks/ParallelTinyLoops). The contract stays that of
    /// ForRange: every index exactly once, on any thread in any order, a local never used by two threads at once, exceptions as before.
    /// </summary>
    public class GParallelCallerFirstTests
    {
        // A pool with no free worker, made deterministically: more blocked work items are queued than the pool runs, so a work item
        // queued afterwards waits behind them until Dispose releases them (the pool adds threads slowly, and each new one takes a
        // blocker first). Held only for the few milliseconds a caller-first loop needs.
        sealed class BlockedPool : IDisposable
        {
            readonly ManualResetEventSlim release = new ManualResetEventSlim(false);
            int started;

            public BlockedPool()
            {
                ThreadPool.GetMinThreads(out int minWorkers, out int unusedIo);
#if NET
                int running = ThreadPool.ThreadCount;
#else
                int running = 4 * Environment.ProcessorCount;
#endif
                int blockers = Math.Max(minWorkers, Environment.ProcessorCount) + running + 16;
                for (int k = 0; k < blockers; ++k)
                    ThreadPool.QueueUserWorkItem((unused) => { Interlocked.Increment(ref started); release.Wait(); });
                // (until the workers there are have taken a blocker, or 200 ms if some are busy elsewhere; the rest of the blockers
                // stay queued, and a worker the pool adds later takes one of them first)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (Volatile.Read(ref started) < Math.Min(blockers, running) && sw.ElapsedMilliseconds < 200)
                    Thread.Sleep(1);
            }

            public void Dispose() { release.Set(); }
        }

        // runs a on a thread of its own (not a pool thread, as a test runner's or an application's thread); false if it did not
        // finish within the time (the thread is left to finish once the pool is released); an exception of a is rethrown
        static bool OnDedicatedThread(Action a, int timeoutMs)
        {
            Exception error = null;
            var t = new Thread(() => { try { a(); } catch (Exception e) { error = e; } }) { IsBackground = true };
            t.Start();
            bool done = t.Join(timeoutMs);
            if (done && error != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            return done;
        }

        // every loop variant once over n items, writing hits[i]
        static void AllVariants(int n, int[] hits)
        {
            gParallel.ForRange(n, (i) => Interlocked.Increment(ref hits[i]));
            gParallel.ForRange(n, () => 0, (i, l) => Interlocked.Increment(ref hits[i]));
            gParallel.ForEach(Enumerable.Range(0, n), (i) => Interlocked.Increment(ref hits[i]));            // an enumerable
            gParallel.ForEach(Enumerable.Range(0, n).ToArray(), (i) => Interlocked.Increment(ref hits[i]));  // an IList
            gParallel.BlockStartEnd(0, n - 1, (a, b) => { for (int i = a; i <= b; ++i) Interlocked.Increment(ref hits[i]); }, 7);
            // (the Small loops hand over at once with a budget of one tick)
            long old = gParallel.SmallLoopBudgetTicksOnThisThread;
            gParallel.SmallLoopBudgetTicksOnThisThread = 1;
            try {
                gParallel.ForRangeSmall(n, (i) => Interlocked.Increment(ref hits[i]));
                gParallel.ForEachSmall(Enumerable.Range(0, n), (i) => Interlocked.Increment(ref hits[i]));
            } finally { gParallel.SmallLoopBudgetTicksOnThisThread = old; }
        }
        const int Variants = 7;

        [Fact]
        public void BusyPool_DedicatedCaller_LoopsDoNotWaitForWorkers()
        {
            // 40 rounds of every loop variant over 32 items, from a dedicated thread, while no pool worker is free. Caller first:
            // a few milliseconds. Parallel.For: each call waits for its queued tasks, which sit behind the blocked items (it does
            // not finish in the 3 s given)
            var hits = new int[32];
            bool done;
            using (new BlockedPool()) {
                done = OnDedicatedThread(() => {
                    for (int r = 0; r < 40; ++r)
                        AllVariants(32, hits);
                }, 3000);
            }
            Assert.True(done, "the loops waited for pool workers");
            Assert.All(hits, h => Assert.Equal(40 * Variants, h));
        }

        [Fact]
        public void BusyPool_NestedLoops_Finish()
        {
            // a loop whose items run loops: the inner callers (the caller or a helper) never wait for an unstarted helper either
            var hits = new int[8 * 50];
            bool done;
            using (new BlockedPool()) {
                done = OnDedicatedThread(() => {
                    gParallel.ForRange(8, (o) => gParallel.ForRange(50, (i) => Interlocked.Increment(ref hits[o * 50 + i])));
                    gParallel.ForEach(Enumerable.Range(0, 8), (o) => gParallel.ForEach(Enumerable.Range(0, 50), (i) => Interlocked.Increment(ref hits[o * 50 + i])));
                }, 3000);
            }
            Assert.True(done, "the nested loops waited for pool workers");
            Assert.All(hits, h => Assert.Equal(2, h));
        }

        [Fact]
        public void LateHelpers_DoNotRunItems_AfterTheLoopReturned()
        {
            // the helpers of these loops start only after Dispose, when every loop has returned: they must find nothing to do
            var hits = new int[100];
            int afterReturn = -1;
            using (new BlockedPool()) {
                Assert.True(OnDedicatedThread(() => {
                    for (int r = 0; r < 10; ++r)
                        AllVariants(100, hits);
                    afterReturn = hits.Sum();
                }, 3000));
            }
            // (let the pool run the late helpers)
#if NET
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (ThreadPool.PendingWorkItemCount > 0 && sw.ElapsedMilliseconds < 2000)
                Thread.Sleep(5);
#endif
            Thread.Sleep(50);
            Assert.Equal(10 * Variants * 100, afterReturn);
            Assert.Equal(afterReturn, hits.Sum());
            Assert.All(hits, h => Assert.Equal(10 * Variants, h));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(17)]
        [InlineData(1000)]
        [InlineData(100003)]
        public void EveryIndex_ExactlyOnce(int n)
        {
            // from a pool thread and from a dedicated thread, every variant (chunks of 1 up to 4096 items)
            var hits = new int[n];
            Assert.True(System.Threading.Tasks.Task.Run(() => AllVariants(n, hits)).Wait(30000));
            Assert.True(OnDedicatedThread(() => AllVariants(n, hits), 30000));
            Assert.All(hits, h => Assert.Equal(2 * Variants, h));
        }

        [Fact]
        public void Local_IsNeverUsedByTwoThreadsAtOnce_AndEachIsMadeOnce()
        {
            int made = 0, overlaps = 0;
            var hits = new int[20000];
            gParallel.ForRange(hits.Length, () => { Interlocked.Increment(ref made); return new int[1]; }, (i, busy) => {
                if (Interlocked.Exchange(ref busy[0], 1) != 0) Interlocked.Increment(ref overlaps);
                hits[i]++;
                Thread.SpinWait(50);
                Volatile.Write(ref busy[0], 0);
            });
            Assert.Equal(0, overlaps);
            Assert.All(hits, h => Assert.Equal(1, h));
            // at most one local per participant: the caller and at most ProcessorCount - 1 helpers
            Assert.InRange(made, 1, Environment.ProcessorCount);
        }

        [Fact]
        public void Exceptions_KeepTheirTypes()
        {
            // ForRange: the exception of a single body as it is; ForEach: an AggregateException, as before
            var e1 = Assert.Throws<InvalidOperationException>(() => gParallel.ForRange(1000, (i) => { if (i == 517) throw new InvalidOperationException("517"); }));
            Assert.Equal("517", e1.Message);
            var e2 = Assert.Throws<ArgumentException>(() => gParallel.ForRange(1000, () => 0, (i, l) => { if (i == 3) throw new ArgumentException("3"); }));
            Assert.Equal("3", e2.Message);
            var e3 = Assert.Throws<AggregateException>(() => gParallel.ForEach(Enumerable.Range(0, 1000), (i) => { if (i == 999) throw new InvalidOperationException("999"); }));
            Assert.IsType<InvalidOperationException>(Assert.Single(e3.InnerExceptions));
            var e4 = Assert.Throws<AggregateException>(() => gParallel.ForEach(Enumerable.Range(0, 1000).ToList(), (i) => { if (i == 0) throw new InvalidOperationException("0"); }));
            Assert.IsType<InvalidOperationException>(Assert.Single(e4.InnerExceptions));
            // every item fails: one failure as it is, or several participants failed before they saw the stop (an AggregateException of them)
            Exception e5 = Record.Exception(() => gParallel.ForRange(50, (i) => { Thread.Sleep(1); throw new InvalidOperationException(); }));
            var all = (e5 is AggregateException ae) ? ae.Flatten().InnerExceptions.ToList() : new List<Exception>() { e5 };
            Assert.NotEmpty(all);
            Assert.All(all, x => Assert.IsType<InvalidOperationException>(x));
            // the single-item case of a failure on the calling thread
            Assert.Throws<InvalidOperationException>(() => gParallel.ForRange(1, (i) => throw new InvalidOperationException()));
        }

        [Fact]
        public void Failure_StopsTheLoop_AndNoItemRunsAfterTheThrow()
        {
            // the first item that a helper runs fails, while the caller is still taking chunks: the caller must stop too (a failure
            // on the caller alone ends the loop anyway, since the caller then closes it). The items are slow (about 75 ms on one
            // thread), so a helper starts; if none did (a pool without a free worker) the loop ends without a failure and there is
            // nothing to check
            int caller = Environment.CurrentManagedThreadId;
            int ran = 0;
            const int n = 100000;
            Exception e = Record.Exception(() => gParallel.ForRange(n, (i) => {
                if (Environment.CurrentManagedThreadId != caller) throw new InvalidOperationException();
                Interlocked.Increment(ref ran);
                Thread.SpinWait(200);
            }));
            int atThrow = Volatile.Read(ref ran);
            Thread.Sleep(50);
            Assert.Equal(atThrow, Volatile.Read(ref ran));
            if (e == null) {
                Assert.Equal(n, atThrow);
                return;
            }
            var all = (e is AggregateException ae) ? ae.Flatten().InnerExceptions.ToList() : new List<Exception>() { e };
            Assert.All(all, x => Assert.IsType<InvalidOperationException>(x));
            Assert.True(atThrow < n / 2, $"{atThrow} of {n} items ran on the caller after a helper failed");
        }

        [Fact]
        public void ExecutionContext_FlowsToTheHelpers()
        {
            var value = new AsyncLocal<int>() { Value = 42 };
            int wrong = 0;
            gParallel.ForRange(4000, (i) => { if (value.Value != 42) Interlocked.Increment(ref wrong); Thread.SpinWait(100); });
            gParallel.ForEach(Enumerable.Range(0, 4000), (i) => { if (value.Value != 42) Interlocked.Increment(ref wrong); Thread.SpinWait(100); });
            Assert.Equal(0, wrong);
        }

        // ---- the library through the new loops: parallel equals serial, over repeated runs ----

        static DMesh3 Sphere(int n, Vector3d shift)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, shift);
            return m;
        }

        static string Bits(DMesh3 m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';'); }
            foreach (int t in m.TriangleIndices()) { Index3i tri = m.GetTriangle(t); sb.Append(t).Append(':').Append(tri.a).Append(',').Append(tri.b).Append(',').Append(tri.c).Append(';'); }
            return sb.ToString();
        }

        static string Serial(Func<string> f)
        {
            gParallel.SerialOnThisThread = true;
            try { return f(); } finally { gParallel.SerialOnThisThread = false; }
        }

        [Fact]
        public void ExactBoolean_Reducer_Remesher_ParallelEqualsSerial_Repeated()
        {
            DMesh3 a = Sphere(12, Vector3d.Zero), b = Sphere(12, new Vector3d(0.6, 0.3, 0.2));
            Func<string> boolean = () => Bits(ExactMeshBoolean.Union(new[] { a, b }));
            Func<string> reducer = () => { var m = Sphere(24, Vector3d.Zero); new Reducer(m).ReduceToTriangleCount(400); return Bits(m); };
            Func<string> remesher = () => { var m = Sphere(16, Vector3d.Zero); var r = new Remesher(m); r.SetTargetEdgeLength(0.15); for (int k = 0; k < 3; ++k) r.BasicRemeshPass(); return Bits(m); };
            foreach (Func<string> f in new[] { boolean, reducer, remesher }) {
                string serial = Serial(f);
                for (int run = 0; run < 3; ++run)
                    Assert.Equal(serial, f());
                // and from a dedicated thread with the pool blocked (every loop then runs on the calling thread)
                string blocked = null;
                using (new BlockedPool())
                    Assert.True(OnDedicatedThread(() => blocked = f(), 4000));
                Assert.Equal(serial, blocked);
            }
        }
    }
}
