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
        /// a run that is slow because the machine is loaded does not: a total budget cannot tell the two apart. An exception
        /// the action threw is rethrown; a stalled run keeps running in the background.
        /// </summary>
        public static bool MakesProgress(Action<Action> action, TimeSpan stall)
        {
            Exception error = null;
            long last = Stopwatch.GetTimestamp();
            var thread = new Thread(() => {
                try { action(() => Interlocked.Exchange(ref last, Stopwatch.GetTimestamp())); } catch (Exception e) { error = e; }
            }) { IsBackground = true };
            thread.Start();
            while (!thread.Join(100)) {
                long idle = Stopwatch.GetTimestamp() - Interlocked.Read(ref last);
                if (idle > stall.TotalSeconds * Stopwatch.Frequency)
                    return false;
            }
            if (error != null)
                ExceptionDispatchInfo.Capture(error).Throw();
            return true;
        }
    }
}
