using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

#if !(NET_2_0 || NET_2_0_SUBSET)
using System.Threading.Tasks;
#endif

namespace g3
{
    public class gParallel
    {

        public static void ForEach_Sequential<T>(IEnumerable<T> source, Action<T> body)
        {
            foreach (T v in source)
                body(v);
        }
        public static void ForEach<T>( IEnumerable<T> source, Action<T> body )
        {
            if (SerialOnThisThread) {
                // (tests: one after the other on the calling thread, in the order of SerialOrderOnThisThread; the
                // source is copied only for another order than its own)
                if (serialOrderSeed == 0) {
                    foreach (T v in source)
                        body(v);
                    return;
                }
                var items = new List<T>(source);
                int[] order = serial_order(items.Count);
                for (int k = 0; k < items.Count; ++k)
                    body(items[(order == null) ? k : order[k]]);
                return;
            }
#if G3_USING_UNITY && (NET_2_0 || NET_2_0_SUBSET)
            for_each<T>(source, body);
#else
            caller_first_each<T>(source, body);
#endif
        }


        /// <summary>
        /// for tests: ForRange, ForEach (and so BlockStartEnd, Evaluate) run their items one after the other on the
        /// calling thread (parallel == serial checks)
        /// </summary>
        [ThreadStatic] internal static bool SerialOnThisThread;

        /// <summary>
        /// for tests (the environment adversary, docs/design/DETERMINISM_PROOF.md): the order in which a loop runs its
        /// items under SerialOnThisThread. 0: as given, -1: backwards, any other value: a permutation seeded by it (a
        /// new one for every loop, drawn from one sequence per thread that starts when the value is set). A result that depends
        /// on the schedule of a parallel loop (the order of shared appends, ids handed out by a counter) changes
        /// with this order; a result that does not is the same for every value
        /// </summary>
        internal static int SerialOrderOnThisThread {
            get { return serialOrderSeed; }
            set {
                // (setting it starts the sequence of permutations anew, so that two runs with one seed see the same orders)
                serialOrderSeed = value;
                // (the seed mixed once, so that the sequences of neighbouring seeds are unrelated)
                ulong z = unchecked((ulong)value * 0x9E3779B97F4A7C15UL);
                z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
                z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
                serialOrderState = z ^ (z >> 31);
            }
        }
        [ThreadStatic] static ulong serialOrderState;
        [ThreadStatic] static int serialOrderSeed;

        // the order of count items under SerialOrderOnThisThread; null: as given
        static int[] serial_order(int count)
        {
            int seed = serialOrderSeed;
            if (seed == 0 || count < 2)
                return null;
            var order = new int[count];
            if (seed == -1) {
                for (int i = 0; i < count; ++i)
                    order[i] = count - 1 - i;
                return order;
            }
            for (int i = 0; i < count; ++i)
                order[i] = i;
            for (int i = count - 1; i > 0; --i) {
                // SplitMix64 (Steele, Lea, Flood 2014), Fisher-Yates
                ulong z = unchecked(serialOrderState += 0x9E3779B97F4A7C15UL);
                z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
                z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
                z ^= z >> 31;
                int k = (int)(z % (ulong)(i + 1));
                int t = order[i]; order[i] = order[k]; order[k] = t;
            }
            return order;
        }

        /// <summary>
        /// body(i, local) for every i in [0, count), in parallel over index ranges; local comes from localInit, once
        /// per worker task, and is never used by two threads at the same time (buffers that a body reuses). An
        /// exception of a single body is rethrown as it is, not wrapped
        /// </summary>
        internal static void ForRange<TLocal>(int count, Func<TLocal> localInit, Action<int, TLocal> body)
        {
            if (SerialOnThisThread) {
                TLocal local = localInit();
                int[] order = serial_order(count);
                for (int i = 0; i < count; ++i)
                    body((order == null) ? i : order[i], local);
                return;
            }
#if G3_USING_UNITY && (NET_2_0 || NET_2_0_SUBSET)
            for_each<int>(Interval1i.Range(count), (i) => body(i, localInit()));
#else
            try {
                caller_first_range(0, count, localInit, body);
            } catch (AggregateException e) when (e.InnerExceptions.Count == 1) {
                // (the exception of the body, as a serial loop would throw it)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
            }
#endif
        }

        /// <summary> body(i) for every i in [0, count), in parallel over index ranges (an exception of a single body is rethrown as it is) </summary>
        internal static void ForRange(int count, Action<int> body)
        {
            if (SerialOnThisThread) {
                int[] order = serial_order(count);
                for (int i = 0; i < count; ++i)
                    body((order == null) ? i : order[i]);
                return;
            }
#if G3_USING_UNITY && (NET_2_0 || NET_2_0_SUBSET)
            for_each<int>(Interval1i.Range(count), body);
#else
            try {
                caller_first_range(0, count, body);
            } catch (AggregateException e) when (e.InnerExceptions.Count == 1) {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
            }
#endif
        }


        // ---- loops of cheap items: inline first, parallel only when the loop turns out to be long (#1502) ----
        //
        // Parallel.For costs 3-15 us alone and, with the thread pool busy, 5-150 ms per call (the wait for a worker),
        // however few items there are. A loop of a few dozen cheap items (a Boolean of two boxes, a Remesher pass over
        // a small mesh) is done in a few microseconds, so waiting for a worker is the whole cost. The Small variants run
        // the items one after the other on the calling thread, in chunks of 1, 2, 4, ... items, and look at the clock
        // between the chunks: when the loop has used SmallLoopBudgetTicks (about 250 us), the rest goes to the parallel
        // loop. A count threshold cannot decide this: the crossover is a total of 10-50 us of work, which is 1000
        // items of 1 ns and 4 items of 2 us (benchmarks/ParallelTinyLoops).
        //
        // Exceptions are those of the loop a Small loop replaces, whether the item failed inline or after the handover:
        // ForRangeSmall like ForRange (the exception of a single body as it is), ForEachSmall and BlockStartEndSmall like
        // ForEach (an AggregateException; the leftover block of BlockStartEnd, which runs inline, as it is).
        //
        // For callers whose items are cheap, so that the first chunk is no loss of parallelism: a loop of a few heavy
        // items (a boolean per body, Evaluate) must keep calling ForRange / ForEach, or its first item would run
        // before the others start. The result is the same as for ForRange: the body of an item does not depend on
        // which thread runs it or in which order (SerialOnThisThread and the order adversary test exactly that).

        /// <summary> the time a Small loop runs inline before it hands the rest to the parallel loop, in Stopwatch ticks (250 us) </summary>
        internal static readonly long DefaultSmallLoopBudgetTicks = Math.Max(1L, System.Diagnostics.Stopwatch.Frequency / 4000);

        /// <summary>
        /// for tests and benchmarks: the budget of the Small loops started on this thread, in Stopwatch ticks; 0: the default,
        /// long.MaxValue: never hand over (the whole loop runs on the calling thread), 1: hand over after the first item,
        /// -1: no inline phase (the Small loops start the parallel loop at once, for tests of the parallel path)
        /// </summary>
        [ThreadStatic] internal static long SmallLoopBudgetTicksOnThisThread;

        // the time at which a Small loop started at `start` hands over
        /// <summary> for tests: how many Small loops started on this thread have handed their rest to the parallel loop </summary>
        [ThreadStatic] internal static int SmallLoopHandovers;

        static long small_limit(long start)
        {
            long b = SmallLoopBudgetTicksOnThisThread;
            if (b == 0)
                b = DefaultSmallLoopBudgetTicks;
            long limit = unchecked(start + b);
            return (limit < start) ? long.MaxValue : limit;
        }

        // the size of the next chunk of a Small loop after `done` items took the time from start to now: at most double the last
        // one, and at most what the rest of the budget allows at the speed so far (so a loop stops near its budget, not at 2x)
        internal static int next_chunk(int chunk, int done, long start, long now, long limit)
        {
            long elapsed = Math.Max(1L, now - start);
            long room = limit - now;
            // (saturated: with an unlimited budget room * done would wrap)
            long fit = (room > long.MaxValue / Math.Max(1, done)) ? int.MaxValue / 2 : room * done / elapsed;
            return (int)Math.Max(1L, Math.Min((long)chunk * 2, Math.Min(fit, int.MaxValue / 2)));
        }

        /// <summary> the loop from..count-1 of ForRange, without the serial test mode (the caller handled it) </summary>
        static void parallel_range(int from, int count, Action<int> body)
        {
#if G3_USING_UNITY && (NET_2_0 || NET_2_0_SUBSET)
            for_each<int>(Interval1i.Range(from, count - from), body);
#else
            try {
                caller_first_range(from, count, body);
            } catch (AggregateException e) when (e.InnerExceptions.Count == 1) {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
            }
#endif
        }

        static void parallel_range_local<TLocal>(int from, int count, Func<TLocal> localInit, Action<int, TLocal> body)
        {
#if G3_USING_UNITY && (NET_2_0 || NET_2_0_SUBSET)
            for_each<int>(Interval1i.Range(from, count - from), (k) => body(k, localInit()));
#else
            try {
                caller_first_range(from, count, localInit, body);
            } catch (AggregateException e) when (e.InnerExceptions.Count == 1) {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
            }
#endif
        }

        /// <summary> ForRange(count, body) for cheap items: inline while the loop is short, see above </summary>
        internal static void ForRangeSmall(int count, Action<int> body)
        {
            if (SerialOnThisThread || count < 2) {
                ForRange(count, body);
                return;
            }
            if (SmallLoopBudgetTicksOnThisThread < 0) {
                parallel_range(0, count, body);
                return;
            }
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            long limit = small_limit(start);
            int i = 0;
            for (int chunk = 1; i < count; ) {
                int end = (chunk >= count - i) ? count : i + chunk;
                for (; i < end; ++i)
                    body(i);
                if (i < count) {
                    long now = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (now >= limit) {
                        ++SmallLoopHandovers;
                        parallel_range(i, count, body);
                        return;
                    }
                    chunk = next_chunk(chunk, i, start, now, limit);
                }
            }
        }

        /// <summary> ForRange(count, localInit, body) for cheap items: inline while the loop is short, see above </summary>
        internal static void ForRangeSmall<TLocal>(int count, Func<TLocal> localInit, Action<int, TLocal> body)
        {
            if (SerialOnThisThread || count < 2) {
                ForRange(count, localInit, body);
                return;
            }
            if (SmallLoopBudgetTicksOnThisThread < 0) {
                parallel_range_local(0, count, localInit, body);
                return;
            }
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            long limit = small_limit(start);
            TLocal local = localInit();
            int i = 0;
            for (int chunk = 1; i < count; ) {
                int end = (chunk >= count - i) ? count : i + chunk;
                for (; i < end; ++i)
                    body(i, local);
                long now = (i < count) ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                if (i < count && now < limit)
                    chunk = next_chunk(chunk, i, start, now, limit);
                if (i < count && now >= limit) {
                    ++SmallLoopHandovers;
                    // (the workers of the rest start with locals of their own)
                    int from = i;
#if G3_USING_UNITY && (NET_2_0 || NET_2_0_SUBSET)
                    for_each<int>(Interval1i.Range(from, count - from), (k) => body(k, localInit()));
#else
                    try {
                        caller_first_range(from, count, localInit, body);
                    } catch (AggregateException e) when (e.InnerExceptions.Count == 1) {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
                    }
#endif
                    return;
                }
            }
        }

        /// <summary> ForEach(source, body) for cheap items: inline while the loop is short, see above </summary>
        internal static void ForEachSmall<T>(IEnumerable<T> source, Action<T> body)
        {
            if (SerialOnThisThread || SmallLoopBudgetTicksOnThisThread < 0) {
                ForEach(source, body);
                return;
            }
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            long limit = small_limit(start);
            using (IEnumerator<T> e = source.GetEnumerator()) {
                int done = 0;
                for (int chunk = 1; ; ) {
                    try {
                        for (int k = 0; k < chunk; ++k) {
                            if (!e.MoveNext())
                                return;
                            body(e.Current);
                            ++done;
                        }
                    } catch (Exception x) {
                        // (as ForEach throws it: Parallel.ForEach wraps even the exception of a single item, and callers catch that type)
                        throw new AggregateException(x);
                    }
                    long now = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (now >= limit) {
                        ++SmallLoopHandovers;
                        ForEach(rest_of(e), body);
                        return;
                    }
                    chunk = next_chunk(chunk, done, start, now, limit);
                }
            }
        }

        // what is left of an enumerator that a Small loop has started (read under the lock of the parallel loop)
        static IEnumerable<T> rest_of<T>(IEnumerator<T> e)
        {
            while (e.MoveNext())
                yield return e.Current;
        }

        /// <summary> BlockStartEnd for cheap blocks (a block of the default size is 100 items): inline while the loop is short </summary>
        internal static void BlockStartEndSmall(int iStart, int iEnd, Action<int, int> blockF, int iBlockSize = -1)
        {
            if (iBlockSize == -1)
                iBlockSize = 100;
            int N = (iEnd - iStart + 1);
            int num_blocks = N / iBlockSize;
            ForEachSmall(Interval1i.Range(num_blocks), (bi) => {
                int k = iStart + iBlockSize * bi;
                blockF(k, k + iBlockSize - 1);
            });
            int remaining = N - (num_blocks * iBlockSize);
            if (remaining > 0) {
                int k = iStart + num_blocks * iBlockSize;
                blockF(k, k + remaining - 1);
            }
        }


        /// <summary>
        /// The first InvalidOperationException among the (flattened) inner exceptions of e, else the first
        /// ArgumentException, else null: for a caller that documents these exception types, so that its parallel path
        /// throws one of them instead of an AggregateException. Only the type preference is fixed: which exception
        /// (and so which message) comes first among several depends on the thread schedule and need not be the one
        /// the serial path throws; with null (only other exception types) the caller lets the AggregateException pass
        /// </summary>
        internal static Exception FirstOperationFailure(AggregateException e)
        {
            var inner = e.Flatten().InnerExceptions;
            foreach (Exception x in inner)
                if (x is InvalidOperationException) return x;
            foreach (Exception x in inner)
                if (x is ArgumentException) return x;
            return null;
        }


        /// <summary>
        /// Evaluate input actions in parallel
        /// </summary>
        public static void Evaluate(params Action[] funcs)
        {
            int N = funcs.Length;
            gParallel.ForEach(Interval1i.Range(N), (i) => {
                funcs[i]();
            });
        }



        /// <summary>
        /// Process indices [iStart,iEnd] *inclusive* by passing sub-intervals [start,end] to blockF.
        /// Blocksize is automatically determind unless you specify one.
        /// Iterate over [start,end] *inclusive* in each block
        /// </summary>
        public static void BlockStartEnd(int iStart, int iEnd, Action<int,int> blockF, int iBlockSize = -1, bool bDisableParallel = false )
        {
            if (iBlockSize == -1)
                iBlockSize = 100;  // seems to work
            int N = (iEnd - iStart + 1);
            int num_blocks = N / iBlockSize;
            // process main blocks in parallel
            if (bDisableParallel) {
                ForEach_Sequential(Interval1i.Range(num_blocks), (bi) => {
                    int k = iStart + iBlockSize * bi;
                    blockF(k, k + iBlockSize - 1);
                });
            } else {
                ForEach(Interval1i.Range(num_blocks), (bi) => {
                    int k = iStart + iBlockSize * bi;
                    blockF(k, k + iBlockSize - 1);
                });
            }
            // process leftover elements
            int remaining = N - (num_blocks * iBlockSize);
            if (remaining > 0) {
                int k = iStart + num_blocks * iBlockSize;
                blockF(k, k+remaining-1);
            }
        }



#if !(G3_USING_UNITY && (NET_2_0 || NET_2_0_SUBSET))
        // ---- the parallel loops: caller first (#1502) ----
        //
        // Parallel.For waits for every worker task it queued, including those that have not started: called from a thread that is not a
        // pool thread (an application thread, a test runner's thread), the tasks sit in the pool's global queue, and with the pool busy one
        // call of a few dozen trivial items took 292 ms instead of 0.01 ms (4 cores; benchmarks/ParallelTinyLoops). These loops queue helpers
        // and run the items on the calling thread at once: the caller and the helpers take chunks of indices from one counter, and the
        // caller waits only for chunks that were taken. A helper that starts after the loop has ended finds nothing to do and touches
        // nothing of the caller's. Which thread runs an item does not change the result (the contract of ForRange: a body writes per
        // item); SerialOnThisThread and the order adversary test exactly that. The helpers are queued with ThreadPool.QueueUserWorkItem,
        // which flows the ExecutionContext (AsyncLocal values) as Parallel.For's tasks do. Exceptions: an AggregateException of every
        // failure, as Parallel.For; after a failure no participant starts another item (the items running finish). The helpers run on
        // the default pool whatever TaskScheduler.Current is (Parallel.For queued to TaskScheduler.Current; a ConcurrentExclusiveSchedulerPair
        // with a concurrency level of 2 did not limit its loops either: 4 items at once were measured on 4 cores).

        /// <summary>
        /// for tests (contention): every chunk of the loops started on this thread has this size (0: the guided chunks of production)
        /// </summary>
        [ThreadStatic] internal static int ChunkForTestsOnThisThread;

        // the state shared by the caller and the helpers of one loop
        class CallerFirstLoop
        {
            public int Next, Count, MinChunk, Parts, FixedChunk;
            public int Active, Closed, Queued, MaxHelpers;
            public volatile bool Stop;
            public List<Exception> Errors;
            // (spins up to 2047 iterations before it blocks, as a helper usually finishes its last chunk within microseconds; never
            // disposed: a late helper may still call Set after the caller has returned, and without a blocking wait it holds no handle)
            public readonly ManualResetEventSlim Idle = new ManualResetEventSlim(false, 2047);

            public void Fail(Exception e)
            {
                lock (this) {
                    if (Errors == null) Errors = new List<Exception>();
                    Errors.Add(e);
                }
                Stop = true;
            }

            // a helper joins the loop; false: the caller has closed it (the helper started late and must not touch it). A helper that
            // joins queues the next one while indices are left, up to MaxHelpers: one wake-up at a time, as Parallel.For starts its
            // tasks, so that a loop the caller finishes alone wakes one pool thread, not ProcessorCount - 1
            public bool Enter(WaitCallback help)
            {
                Interlocked.Increment(ref Active);
                if (Volatile.Read(ref Closed) == 1) {
                    Leave();
                    return false;
                }
                if (Volatile.Read(ref Next) < Count && Interlocked.Increment(ref Queued) <= MaxHelpers) {
                    try {
                        ThreadPool.QueueUserWorkItem(help);
                    } catch (Exception) {
                        // (no further helper: this one stays and works, the caller finishes the rest; an exception thrown on to the pool
                        // thread would end the process)
                    }
                }
                return true;
            }

            public void Leave()
            {
                if (Interlocked.Decrement(ref Active) == 0 && Volatile.Read(ref Closed) == 1)
                    Idle.Set();
            }

            // the caller starts the loop: the first helper (the others follow from Enter)
            public void Start(int n, WaitCallback help)
            {
                Count = n;
                MaxHelpers = Math.Max(0, Math.Min(Environment.ProcessorCount - 1, n - 1));
                Parts = MaxHelpers + 1;
                // (the smallest chunk, 1/64 of a participant's share: the end of the loop balances unequal items)
                MinChunk = Math.Max(1, Math.Min(4096, n / (64 * Parts)));
                FixedChunk = ChunkForTestsOnThisThread;
                if (MaxHelpers > 0) {
                    Queued = 1;
                    ThreadPool.QueueUserWorkItem(help);
                }
            }

            // the next chunk [i0, i1) of the indices; false: none left. Guided: half of the rest divided by the participants, at least
            // MinChunk, so the first chunks are large (few counter operations, each participant a contiguous run) and the last small
            public bool Take(out int i0, out int i1)
            {
                while (true) {
                    i0 = Volatile.Read(ref Next);
                    if (i0 >= Count) {
                        i1 = i0;
                        return false;
                    }
                    int rest = Count - i0;
                    i1 = i0 + Math.Min(rest, (FixedChunk > 0) ? FixedChunk : Math.Max(MinChunk, rest / (2 * Parts)));
                    // (the chunk [i0, i1) is this caller's only if Next was still i0: the chunks of all participants are disjoint and cover
                    // [0, Count); Next never exceeds Count)
                    if (Interlocked.CompareExchange(ref Next, i1, i0) == i0)
                        return true;
                }
            }

            // the caller: no helper joins after this; wait for the helpers that joined, then throw the failures
            public void Close()
            {
                // (a full fence between closing and reading Active, against Enter's increment before its read of Closed)
                Interlocked.Exchange(ref Closed, 1);
                if (Volatile.Read(ref Active) != 0)
                    Idle.Wait();
                if (Errors != null)
                    throw new AggregateException(Errors);
            }
        }

        sealed class CallerFirstRange : CallerFirstLoop
        {
            public int From;
            public Action<int> Body;
            public WaitCallback HelpCallback;

            public void Help(object unused)
            {
                if (!Enter(HelpCallback))
                    return;
                try { Run(); } finally { Leave(); }
            }

            // a participant: chunks until none is left or an item failed
            public void Run()
            {
                try {
                    int i0, i1;
                    while (!Stop && Take(out i0, out i1)) {
                        for (int i = i0; i < i1 && !Stop; ++i)
                            Body(From + i);
                    }
                } catch (Exception e) {
                    Fail(e);
                }
            }
        }

        sealed class CallerFirstRange<TLocal> : CallerFirstLoop
        {
            public int From;
            public Func<TLocal> LocalInit;
            public Action<int, TLocal> Body;
            public WaitCallback HelpCallback;

            public void Help(object unused)
            {
                if (!Enter(HelpCallback))
                    return;
                try { Run(); } finally { Leave(); }
            }

            // a participant: its own local, made when it takes its first chunk
            public void Run()
            {
                try {
                    bool haveLocal = false;
                    TLocal local = default(TLocal);
                    int i0, i1;
                    while (!Stop && Take(out i0, out i1)) {
                        if (!haveLocal) {
                            local = LocalInit();
                            haveLocal = true;
                        }
                        for (int i = i0; i < i1 && !Stop; ++i)
                            Body(From + i, local);
                    }
                } catch (Exception e) {
                    Fail(e);
                }
            }
        }

        /// <summary> body(i) for every i in [from, to), caller first </summary>
        static void caller_first_range(int from, int to, Action<int> body)
        {
            long span = (long)to - from;
            if (span <= 0)
                return;
            if (span > int.MaxValue) {
                // (more indices than an int counts, only for a negative from: the loop of before)
                Parallel.For(from, to, body);
                return;
            }
            int n = (int)span;
            var loop = new CallerFirstRange() { From = from, Body = body };
            loop.HelpCallback = loop.Help;
            loop.Start(n, loop.HelpCallback);
            loop.Run();
            loop.Close();
        }

        /// <summary> body(i, local) for every i in [from, to), caller first; local from localInit once per participant that takes a chunk </summary>
        static void caller_first_range<TLocal>(int from, int to, Func<TLocal> localInit, Action<int, TLocal> body)
        {
            long span = (long)to - from;
            if (span <= 0)
                return;
            if (span > int.MaxValue) {
                Parallel.For(from, to, localInit, (i, state, l) => { body(i, l); return l; }, (l) => { });
                return;
            }
            int n = (int)span;
            var loop = new CallerFirstRange<TLocal>() { From = from, LocalInit = localInit, Body = body };
            loop.HelpCallback = loop.Help;
            loop.Start(n, loop.HelpCallback);
            loop.Run();
            loop.Close();
        }

        /// <summary> body(v) for every v of source, caller first (an IList by index, any other source in growing chunks under a lock) </summary>
        static void caller_first_each<T>(IEnumerable<T> source, Action<T> body)
        {
            var list = source as IList<T>;
            if (list != null) {
                caller_first_range(0, list.Count, (i) => body(list[i]));
                return;
            }
            var loop = new CallerFirstLoop();
            using (IEnumerator<T> e = source.GetEnumerator()) {
                bool ended = false;
                Action work = () => {
                    var items = new T[64];
                    int chunk = 1;
                    try {
                        while (!loop.Stop) {
                            int got = 0;
                            lock (loop) {
                                try {
                                    while (got < chunk && !ended) {
                                        if (e.MoveNext())
                                            items[got++] = e.Current;
                                        else {
                                            ended = true;
                                            Volatile.Write(ref loop.Next, int.MaxValue);
                                        }
                                    }
                                } catch {
                                    // (a failed MoveNext ends the source for every participant: nobody calls it again)
                                    ended = true;
                                    loop.Stop = true;
                                    Volatile.Write(ref loop.Next, int.MaxValue);
                                    throw;
                                }
                            }
                            if (got == 0)
                                break;
                            for (int k = 0; k < got && !loop.Stop; ++k)
                                body(items[k]);
                            if (chunk < items.Length)
                                chunk *= 2;
                        }
                    } catch (Exception x) {
                        loop.Fail(x);
                    }
                };
                WaitCallback helper = null;
                helper = (unused) => {
                    if (!loop.Enter(helper))
                        return;
                    try { work(); } finally { loop.Leave(); }
                };
                // (the length is unknown: Next < Count until the source has ended, so that the helpers chain up to ProcessorCount - 1)
                loop.Start(int.MaxValue, helper);
                work();
                // (the enumerator is disposed after Close: no helper reads it then)
                loop.Close();
            }
        }


#endif

        // parallel for-each that will work on .net 3.5 (maybe?)
        // adapted from https://www.microsoft.com/en-us/download/details.aspx?id=19222
        static void for_each<T>( IEnumerable<T> source, Action<T> body )
        {
            int numProcs = Environment.ProcessorCount;
            int remainingWorkItems = numProcs;
            Exception last_exception = null;
            using (var enumerator = source.GetEnumerator()) {
                using (ManualResetEvent mre = new ManualResetEvent(false)) {
                    // Create each of the work items.
                    for (int p = 0; p < numProcs; p++) {
                        ThreadPool.QueueUserWorkItem(delegate {
                            // Iterate until there's no more work.
                            while (true) {
                                // Get the next item under a lock,
                                // then process that item.
                                T nextItem;
                                lock (enumerator) {
                                    if (!enumerator.MoveNext())
                                        break;
                                    nextItem = enumerator.Current;
                                }

                                // [RMS] added try/catch here because if body() throws an exception
                                // then one thread breaks out of loop, and somehow we end up hung forever.
                                // (Maybe the WaitOne() on other threads never finishes?)
                                // Anyway, we will just hold onto the exception and throw it at the end.
                                try {
                                    body(nextItem);
                                } catch (Exception e) {
                                    last_exception = e;
                                    break;
                                }
                            }
                            if (Interlocked.Decrement(ref remainingWorkItems) == 0)
                                mre.Set();
                        });
                    }
                    // Wait for all threads to complete.
                    mre.WaitOne();
                }
            }

            // pass on last exception thrown by enumerables
            if (last_exception != null)
                throw last_exception;
        }


    }








    // the idea for this class is that it provides a clean way to
    // process data through a stream of operations, where the ordering
    // of data members must be maintained. You provide a Producer and Consumer
    // as lambdas, and then provide an enumerator to define the order.
    // 
    // The idea is that between Producer and Consumer can be N intermediate
    // stages (Operators). However this is not implemented yet.
    //
    // If the compute steps are too short, it seems like this approach is not effective.
    // I think this is because the memory overhead of the Queue gets too high, as it
    // can get quite large, eg if the consumer is slower than the producer, or even
    // just can block (eg like writing to disk).
    // Also locking overhead will have some effect.
    //
    // Perhaps an alternative would be to use a fixed buffer of T?
    // However, if T is a class (eg by-reference), then this doesn't help as they still have to be allocated....
    public class ParallelStream<V, T>
    {
        public Func<V, T> ProducerF = null;
        //public List<Action<T>> Operators = new List<Action<T>>();
        public Action<T> ConsumerF = null;

        LockingQueue<T> store0 = new LockingQueue<T>();
        IEnumerable<V> source = null;


        // this is the non-threaded variant. useful for comparing/etc.
        public void Run_NoThreads( IEnumerable<V> sourceIn )
        {
            foreach ( V v in sourceIn ) {
                T product = ProducerF(v);
                //foreach (var op in Operators)
                //    op(product);
                ConsumerF(product);
            }
        }


        volatile bool producer_done = false;
        volatile Exception thread_exception = null;
        AutoResetEvent consumer_done_event;

        //int max_queue_size = 0;

        public void Run( IEnumerable<V> sourceIn )
        {
            source = sourceIn;
            producer_done = false;
            thread_exception = null;
            consumer_done_event = new AutoResetEvent(false);

            Thread producer = new Thread(ProducerThreadFunc);
            producer.Name = "ParallelStream_producer";
            producer.Start();
            Thread consumer = new Thread(ConsumerThreadFunc);
            consumer.Name = "ParallelStream_consumer";
            consumer.Start();

            // wait for threads to finish
            consumer_done_event.WaitOne();
            producer.Join();

            // an exception in a worker thread would otherwise terminate the process
            if (thread_exception != null)
                throw new Exception("ParallelStream: " + thread_exception.Message, thread_exception);

            //System.Console.WriteLine("MAX QUEUE SIZE " + max_queue_size);       
        }



        void ProducerThreadFunc() {
            try {
                foreach ( V v in source ) {
                    if (thread_exception != null)
                        break;
                    T product = ProducerF(v);
                    store0.Add(product);
                }
            } catch (Exception e) {
                thread_exception = e;
            } finally {
                producer_done = true;
            }
        }


        void ConsumerThreadFunc()
        {
            // this just spins...is that a good idea??

            T next = default(T);
            try {
                while ( (producer_done == false || store0.Count > 0) && thread_exception == null ) {
                    //max_queue_size = Math.Max(max_queue_size, store0.Count);
                    bool ok = store0.Remove(ref next);
                    if (ok)
                        ConsumerF(next);
                }
            } catch (Exception e) {
                thread_exception = e;
            } finally {
                consumer_done_event.Set();
            }
        }

    }










    // locking queue - provides thread-safe sequential add/remove/count to Queue<T>
    public class LockingQueue<T>
    {
        Queue<T> queue;
        object queue_lock;

        public LockingQueue()
        {
            queue = new Queue<T>();
            queue_lock = new object();
        }

        public bool Remove(ref T val)
        {
            lock (queue_lock) {
                if (queue.Count > 0) {
                    val = queue.Dequeue();
                    return true;
                } else {
                    return false;
                }
            }
        }

        public void Add(T obj)
        {
            lock (queue_lock) {
                queue.Enqueue(obj);
            }
        }

        public int Count {
            get {
                lock (queue_lock) {
                    return queue.Count;
                }
            }
        }
    }




#if G3_USING_UNITY && (NET_2_0 || NET_2_0_SUBSET)

    /*
     * .NET 3.5 (default in Unity) does not have SpinLock object, which we
     * are using in a few places. So provide a wrapper around Monitor.
     * Note that this is class and SpinLock is a struct, so this may cause
     * disasters, but at least things build...
     */
    public class SpinLock
    {
        object o;
        public SpinLock()
        {
            o = new object();
        }

        public void Enter(ref bool entered)
        {
            Monitor.Enter(o);
            entered = true;
        }

        public void Exit()
        {
            Monitor.Exit(o);
        }

    }


#endif


}
