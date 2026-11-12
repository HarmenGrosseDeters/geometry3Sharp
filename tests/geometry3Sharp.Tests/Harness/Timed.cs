using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
        /// true if action finished within budget; an exception it threw is rethrown. A run that does not finish keeps
        /// running in the background (it cannot be cancelled).
        /// </summary>
        public static bool Finishes(Action action, TimeSpan budget)
        {
            Exception error = null;
            var thread = new Thread(() => {
                try { action(); } catch (Exception e) { error = e; }
            }) { IsBackground = true };
            thread.Start();
            if (thread.Join(budget) == false)
                return false;
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
}
