using System;
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
    }
}
