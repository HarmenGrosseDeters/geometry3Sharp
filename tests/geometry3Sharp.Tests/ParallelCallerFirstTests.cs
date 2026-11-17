using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// gParallel.ForRangeCallerFirst (#698): every index exactly once, exceptions as ForRange throws them, and a caller that
    /// never waits for a helper that has not started; and the Reducer's MaxDeviation check that uses it gives the same mesh as
    /// ForRange, as a serial loop and as SerialOnThisThread, on repeated runs and while other work keeps the pool busy.
    /// </summary>
    public class ParallelCallerFirstTests
    {
        [Theory]
        [InlineData(0, -1)]
        [InlineData(1, -1)]
        [InlineData(2, -1)]
        [InlineData(10, -1)]
        [InlineData(10, 0)]
        [InlineData(10, 1)]
        [InlineData(10, 20)]
        [InlineData(5000, -1)]
        public void EveryIndexExactlyOnce(int count, int helpers)
        {
            for (int run = 0; run < 20; ++run) {
                var hits = new int[count];
                gParallel.ForRangeCallerFirst(count, i => Interlocked.Increment(ref hits[i]), helpers);
                Assert.All(hits, h => Assert.Equal(1, h));
            }
        }

        // the helpers are held back until the caller has returned: the caller ran every item on its own thread, and the helpers
        // that start afterwards find the loop closed and run nothing
        [Fact]
        public void NoHelperStarted_CallerRunsAll_LateHelpersRunNothing()
        {
            var held = new List<(WaitCallback cb, object state)>();
            int caller = Environment.CurrentManagedThreadId, calls = 0;
            var threads = new HashSet<int>();
            gParallel.ForRangeCallerFirst(12, i => { calls++; threads.Add(Environment.CurrentManagedThreadId); }, 3, (cb, st) => held.Add((cb, st)));
            Assert.Equal(12, calls);
            Assert.Equal(new[] { caller }, threads);
            Assert.Equal(3, held.Count);
            foreach (var (cb, st) in held) cb(st);
            Assert.Equal(12, calls);
        }

        // a helper that starts before the caller (here: at once, on the queueing thread) takes indices from the same counter
        [Fact]
        public void HelpersStartedFirst_EveryIndexOnce()
        {
            var hits = new int[12];
            gParallel.ForRangeCallerFirst(12, i => hits[i]++, 2, (cb, st) => cb(st));
            Assert.All(hits, h => Assert.Equal(1, h));
        }

        [Fact]
        public void OneException_RethrownAsItIs_NoMoreIndicesHandedOut()
        {
            int calls = 0;
            var e = Assert.Throws<InvalidOperationException>(() =>
                gParallel.ForRangeCallerFirst(100, i => { calls++; if (i == 5) throw new InvalidOperationException("five"); }, 0));
            Assert.Equal("five", e.Message);
            Assert.Equal(6, calls);     // no helpers: indices 0-5 on the caller, none after the exception
            // with helpers on the pool: still the exception of the body (or several of them), never a hang
            for (int run = 0; run < 20; ++run) {
                Exception x = Record.Exception(() => gParallel.ForRangeCallerFirst(64, i => { if (i % 7 == 3) throw new ArgumentException("x"); }));
                Assert.True(x is ArgumentException || (x is AggregateException a && a.InnerExceptions.All(y => y is ArgumentException)), x?.GetType().Name);
            }
        }

        [Fact]
        public void SerialOnThisThread_RunsInOrderOnTheCaller()
        {
            var seen = new List<int>();
            gParallel.SerialOnThisThread = true;
            try { gParallel.ForRangeCallerFirst(8, i => seen.Add(i)); } finally { gParallel.SerialOnThisThread = false; }
            Assert.Equal(Enumerable.Range(0, 8), seen);
        }

        static DMesh3 Bumpy(int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, p * (1 + 0.1 * Math.Sin(5 * p.x + 1) * Math.Cos(4 * p.y - 0.5) + 0.05 * Math.Sin(9 * p.z)));
            }
            return m;
        }

        static string Print(DMesh3 m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices()) sb.Append(t).Append(':').Append(m.GetTriangle(t)).Append(';');
            return sb.ToString();
        }

        static string Reduce(DMesh3 input, double tol, int loop)
        {
            var m = new DMesh3(input);
            new Reducer(m) { MaxDeviation = tol, DeviationLoop = loop }.ReduceToTriangleCount(4);
            return Print(m);
        }

        // the default (caller-first) against ForRange, a serial loop and SerialOnThisThread, bit for bit; repeated runs, alone and
        // while other parallel loops keep the pool busy (the caller then runs most fans itself)
        [Fact]
        public void Reducer_CallerFirst_SameAsForRangeSerialAndRepeated()
        {
            DMesh3 input = Bumpy(16);
            double tol = 3e-3 * input.CachedBounds.DiagonalLength;
            string reference = Reduce(input, tol, 2);
            Assert.Equal(reference, Reduce(input, tol, 1));
            gParallel.SerialOnThisThread = true;
            try { Assert.Equal(reference, Reduce(input, tol, 0)); } finally { gParallel.SerialOnThisThread = false; }
            for (int run = 0; run < 2; ++run)
                Assert.Equal(reference, Reduce(input, tol, 0));
            using (var stop = new CancellationTokenSource()) {
                var busy = Enumerable.Range(0, 2).Select(_ => Task.Run(() => {
                    while (!stop.IsCancellationRequested) Parallel.For(0, 16, i => Thread.SpinWait(2000));
                })).ToArray();
                try { Assert.Equal(reference, Reduce(input, tol, 0)); } finally { stop.Cancel(); Task.WaitAll(busy); }
            }
        }
    }
}
