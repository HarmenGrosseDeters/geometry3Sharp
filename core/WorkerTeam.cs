// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Threading;

namespace g3
{
    /// <summary>
    /// A fixed team of worker threads for many short parallel loops in a row (the rounds of MeshAlphaWrap's carving: tens
    /// of thousands of loops of a few hundred items each, where waking thread-pool workers for every Parallel.For left
    /// half of the threads idle). For(count, body) runs body(i) for every i in [0, count): the calling thread and the
    /// workers take the indices in small contiguous chunks, and the call returns when every index is done. A worker that
    /// comes late (descheduled, eg when other computations run at the same time) finds no work and does not hold the loop
    /// up: the caller waits only for chunks that were taken. Between loops the workers spin for a short while, then block.
    /// The bodies must be independent of each other (each writes only its own data), so which thread runs an index does not
    /// change the result. An exception of a body is rethrown on the calling thread after the loop (the first one taken;
    /// the remaining chunks are skipped). Not reentrant; one caller thread at a time. Dispose ends the workers.
    /// </summary>
    internal sealed class WorkerTeam : IDisposable
    {
        sealed class Loop
        {
            public Action<int> Body;
            public int Count, Chunk;
            public int Next;        // next index to take
            public int Done;        // indices done (or skipped after an exception)
            public Exception Error;
        }

        readonly Thread[] workers;
        readonly object gate = new object();
        Loop current;
        int sleepers;
        volatile bool disposed;

        /// <summary> spin iterations of a worker between loops before it yields, and before it blocks </summary>
        const int SpinLimit = 2000, YieldLimit = 50;

        /// <summary> threads: the team's size including the caller (at least 1; 1 = no workers, plain loops) </summary>
        public WorkerTeam(int threads)
        {
            Threads = Math.Max(1, threads);
            workers = new Thread[Threads - 1];
            for (int k = 0; k < workers.Length; ++k) {
                workers[k] = new Thread(work) { IsBackground = true, Name = "g3 WorkerTeam" };
                workers[k].Start();
            }
        }

        public int Threads { get; }

        /// <summary> (tests) worker threads still running </summary>
        internal int AliveWorkers
        {
            get {
                int n = 0;
                foreach (Thread t in workers) if (t.IsAlive) n++;
                return n;
            }
        }

        public void For(int n, Action<int> body)
        {
            if (disposed) throw new ObjectDisposedException(nameof(WorkerTeam));
            if (workers.Length == 0 || n <= 1) {
                for (int i = 0; i < n; ++i) body(i);
                return;
            }
            var loop = new Loop() { Body = body, Count = n, Chunk = Math.Max(1, n / (8 * Threads)) };
            Volatile.Write(ref current, loop);
            if (Volatile.Read(ref sleepers) > 0)
                lock (gate) Monitor.PulseAll(gate);
            run(loop);
            int spins = 0;
            while (Volatile.Read(ref loop.Done) < n) {
                // (a chunk taken by a worker that is still running it; yield the core if that takes long)
                if (++spins < SpinLimit) Thread.SpinWait(8);
                else Thread.Yield();
            }
            Exception e = loop.Error;
            if (e != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw();
        }

        static void run(Loop loop)
        {
            int n = loop.Count, chunk = loop.Chunk;
            while (true) {
                int i0 = Interlocked.Add(ref loop.Next, chunk) - chunk;
                if (i0 >= n) return;
                int i1 = Math.Min(n, i0 + chunk);
                try {
                    if (Volatile.Read(ref loop.Error) == null)
                        for (int i = i0; i < i1; ++i) loop.Body(i);
                } catch (Exception e) {
                    Interlocked.CompareExchange(ref loop.Error, e, null);
                }
                Interlocked.Add(ref loop.Done, i1 - i0);
            }
        }

        void work()
        {
            Loop seen = null;
            while (true) {
                int spins = 0;
                Loop loop;
                while ((loop = Volatile.Read(ref current)) == seen && !disposed) {
                    ++spins;
                    if (spins < SpinLimit) {
                        Thread.SpinWait(20);
                    } else if (spins < SpinLimit + YieldLimit) {
                        Thread.Yield();
                    } else {
                        lock (gate) {
                            Interlocked.Increment(ref sleepers);
                            if (Volatile.Read(ref current) == seen && !disposed)
                                Monitor.Wait(gate);
                            Interlocked.Decrement(ref sleepers);
                        }
                        spins = 0;
                    }
                }
                if (disposed) return;
                seen = loop;
                run(loop);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (gate) Monitor.PulseAll(gate);
            foreach (Thread t in workers)
                t.Join();
        }
    }
}
