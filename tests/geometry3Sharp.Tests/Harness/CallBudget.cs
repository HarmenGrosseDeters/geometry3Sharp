using System.Threading;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A hang guard for a run with a CancelF that does not depend on the load of the machine: cancels after a number of CancelF calls
    /// (the refinement, the wrap and the improvement call it once per unit of work, from several threads in the wrap, so the count is
    /// atomic) instead of after some seconds of wall clock. A wall-clock guard fired on loaded Windows / net48 runners (master run
    /// 44485576442: five 15 s cancels on a run that takes about 1 s alone) and a wall-clock assertion would do the same; the count of calls
    /// is the work, so the same run gives the same verdict on every runner. The old code, which does not stop, is still cancelled
    /// after the budget (about 10 s of work).
    /// </summary>
    public sealed class CallBudget
    {
        long calls;
        readonly long limit;
        public CallBudget(long limit) { this.limit = limit; }
        /// <summary> the CancelF: true once more than the limit of calls were made </summary>
        public bool Exceeded() { return Interlocked.Increment(ref calls) > limit; }
        public long Calls => Interlocked.Read(ref calls);
        /// <summary> most calls of a legitimate tet run of the tests (466k for a 130k-tet run), with a margin of three </summary>
        public const long Standard = 1500000;
    }
}
