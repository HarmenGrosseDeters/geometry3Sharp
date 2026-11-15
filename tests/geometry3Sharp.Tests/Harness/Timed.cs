using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Time budgets for code that might not terminate. The code runs on a thread of its own: with Task.Run it waits
    /// for a thread-pool thread first, and while other test classes keep the pool busy (Parallel.For) that wait alone
    /// can exceed the budget (seen on net48, whose pool adds threads slowly).
    /// </summary>
    public static class Timed
    {
        /// <summary>
        /// true if action finished; false if it did not end although its thread used budget of CPU time (a hang: a loop that never
        /// ends burns CPU), or, for a run that uses no CPU at all (a deadlock, a wait that never ends), did not end within the wall
        /// ceiling (default: 20 times the budget, at least 2 minutes). A fixed number of wall-clock seconds measured the load of
        /// the machine, not the hang: in a full run an operation of 1-2 s alone took over 10 s (#733, #671, #676). An exception it
        /// threw is rethrown. A run that does not finish keeps running in the background (it cannot be cancelled). Where the CPU
        /// time of a thread cannot be read (neither Windows nor Linux), the budget is wall-clock seconds as before.
        /// </summary>
        public static bool Finishes(Action action, TimeSpan budget, TimeSpan? wallCeiling = null)
        {
            Exception error = null;
            int tid = 0;
            var thread = new Thread(() => {
                Volatile.Write(ref tid, ThreadCpu.CurrentOsThreadId());
                try { action(); } catch (Exception e) { error = e; }
            }) { IsBackground = true };
            long started = Stopwatch.GetTimestamp();
            thread.Start();
            TimeSpan ceiling = wallCeiling ?? TimeSpan.FromSeconds(Math.Max(120, 20 * budget.TotalSeconds));
            bool cpuKnown = true;
            while (!thread.Join(100)) {
                double wall = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
                if (wall >= ceiling.TotalSeconds)
                    return false;
                if (cpuKnown) {
                    int id = Volatile.Read(ref tid);
                    if (id > 0 && ThreadCpu.TryGet(id, out TimeSpan cpu)) {
                        if (cpu >= budget) return false;
                    }
                    else if (id != 0)
                        cpuKnown = false;       // (id 0: not published yet; otherwise not readable here: the wall clock of the budget below)
                }
                else if (wall >= budget.TotalSeconds)
                    return false;
            }
            if (error != null)
                ExceptionDispatchInfo.Capture(error).Throw();
            return true;
        }

        /// <summary>
        /// true if action keeps making progress: it calls the given beat() after every unit of work, and no more than
        /// stall passes between two beats (the first one counts from the start). A hang (one unit that does not end) fails,
        /// a run that is slow because the machine is loaded does not: a total budget cannot tell the two apart (the optional total is only a generous ceiling). An exception
        /// the action threw is rethrown; a stalled run keeps running in the background.
        /// </summary>
        public static bool MakesProgress(Action<Action> action, TimeSpan stall, TimeSpan? total = null)
        {
            return MakesProgress(action, stall, total, out _);
        }

        /// <summary> as above; tripped says which limit ended a failed run ("stall of N s" or "total of N s"), "" if it finished </summary>
        public static bool MakesProgress(Action<Action> action, TimeSpan stall, TimeSpan? total, out string tripped)
        {
            tripped = "";
            // (total: an optional generous ceiling for the whole run, so that a run that is uniformly far slower still fails)
            long started = Stopwatch.GetTimestamp();
            Exception error = null;
            long last = Stopwatch.GetTimestamp();
            var thread = new Thread(() => {
                try { action(() => Interlocked.Exchange(ref last, Stopwatch.GetTimestamp())); } catch (Exception e) { error = e; }
            }) { IsBackground = true };
            thread.Start();
            while (!thread.Join(100)) {
                long idle = Stopwatch.GetTimestamp() - Interlocked.Read(ref last);
                if (idle > stall.TotalSeconds * Stopwatch.Frequency) {
                    tripped = $"no progress for {stall.TotalSeconds:G} s";
                    return false;
                }
                if (total.HasValue && Stopwatch.GetTimestamp() - started > total.Value.TotalSeconds * Stopwatch.Frequency) {
                    tripped = $"total of {total.Value.TotalSeconds:G} s exceeded";
                    return false;
                }
            }
            if (error != null)
                ExceptionDispatchInfo.Capture(error).Throw();
            return true;
        }
    }

    /// <summary> The CPU time a thread of this process has used: Windows (GetCurrentThreadId) and Linux (/proc/thread-self/stat) </summary>
    static class ThreadCpu
    {
        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        /// <summary> the OS id of the calling thread, or -1 if it cannot be told (then TryGet is not asked) </summary>
        public static int CurrentOsThreadId()
        {
            try {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return (int)GetCurrentThreadId();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                    string stat = System.IO.File.ReadAllText("/proc/thread-self/stat");
                    int space = stat.IndexOf(' ');
                    if (space > 0 && int.TryParse(stat.Substring(0, space), out int id) && id > 0)
                        return id;
                }
            } catch (Exception) { }
            return -1;
        }

        public static bool TryGet(int osThreadId, out TimeSpan cpu)
        {
            cpu = TimeSpan.Zero;
            try {
                using (Process me = Process.GetCurrentProcess()) {
                    foreach (ProcessThread t in me.Threads) {
                        try {
                            if (t.Id == osThreadId) { cpu = t.TotalProcessorTime; return true; }
                        } catch (Exception) { }
                        finally { t.Dispose(); }
                    }
                }
            } catch (Exception) { }
            return false;
        }
    }
}
