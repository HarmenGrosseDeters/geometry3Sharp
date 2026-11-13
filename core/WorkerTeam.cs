// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Threading;

namespace g3
{
    /// <summary>
    /// A fixed team of worker threads for many short parallel loops in a row (the rounds of MeshAlphaWrap's carving:
    /// tens of thousands of loops of a few hundred items each, where waking thread-pool workers for every
    /// Parallel.For left half of the threads idle). For(count, body) runs body(i) for every i in [0, count) on the
    /// calling thread and the workers, which take the indices in small contiguous chunks (neighbouring items often share cache lines); between loops the workers spin for a short
    /// while, then block. The bodies must be independent of each other (each writes only its own data), so which
    /// thread runs an index does not change the result. An exception of a body is rethrown on the calling thread after
    /// the loop (the first one taken). Not reentrant; one caller thread at a time. Dispose ends the workers.
    /// </summary>
    internal sealed class WorkerTeam : IDisposable
    {
        readonly Thread[] workers;
        readonly object gate = new object();
        // the current loop
        Action<int> body;
        int count;
        int next;           // next index to take
        int pending;        // workers still in the current loop
        int generation;     // incremented for each loop
        int sleepers;
        volatile bool disposed;
        Exception error;

        /// <summary> spin iterations of a worker between loops before it blocks </summary>
        const int SpinLimit = 2000;

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

        public void For(int n, Action<int> f)
        {
            if (disposed) throw new ObjectDisposedException(nameof(WorkerTeam));
            if (workers.Length == 0 || n <= 1) {
                for (int i = 0; i < n; ++i) f(i);
                return;
            }
            body = f;
            count = n;
            error = null;
            Volatile.Write(ref next, 0);
            Volatile.Write(ref pending, workers.Length);
            Interlocked.Increment(ref generation);
            if (Volatile.Read(ref sleepers) > 0)
                lock (gate) Monitor.PulseAll(gate);
            run();
            // (busy wait: the workers finish within microseconds; SpinWait.SpinOnce would sleep for a millisecond)
            while (Volatile.Read(ref pending) > 0)
                Thread.SpinWait(4);
            body = null;
            Exception e = error;
            if (e != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw();
        }

        void run()
        {
            Action<int> f = body;
            int n = count;
            int chunk = Math.Max(1, n / (8 * Threads));
            while (true) {
                int i0 = Interlocked.Add(ref next, chunk) - chunk;
                if (i0 >= n) return;
                int i1 = Math.Min(n, i0 + chunk);
                try {
                    for (int i = i0; i < i1; ++i) f(i);
                } catch (Exception e) {
                    Interlocked.CompareExchange(ref error, e, null);
                    // (the remaining indices are skipped)
                    Volatile.Write(ref next, n);
                    return;
                }
            }
        }

        void work()
        {
            int seen = 0;
            while (true) {
                int spins = 0;
                while (Volatile.Read(ref generation) == seen && !disposed) {
                    if (++spins < SpinLimit) {
                        Thread.SpinWait(20);
                        continue;
                    }
                    lock (gate) {
                        Interlocked.Increment(ref sleepers);
                        if (Volatile.Read(ref generation) == seen && !disposed)
                            Monitor.Wait(gate);
                        Interlocked.Decrement(ref sleepers);
                    }
                    spins = 0;
                }
                if (disposed) return;
                seen = Volatile.Read(ref generation);
                run();
                Interlocked.Decrement(ref pending);
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
