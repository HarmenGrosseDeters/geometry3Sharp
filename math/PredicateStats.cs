// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace g3
{
    /// <summary>
    /// Counters of the exact predicates for profiling: per predicate, how often it is called and how often its
    /// floating-point filter does not decide (the exact stage runs). Only compiled with the symbol
    /// G3_PREDICATE_STATS (build with -p:G3PredicateStats=true); otherwise the calls are removed by the compiler
    /// ([Conditional]) and the counters stay 0.
    /// </summary>
    internal static class PredicateStats
    {
        internal enum Kind
        {
            Orient2d, Orient3d, InCircle,                   // ExactPredicates (double input)
            ArrOrient, ArrInCircle, ArrAlong, ArrCompare,   // ExactMeshArrangement (implicit points)
            ArrExactPoint,                                  // exact point constructions (BigInteger)
            ArrImplicitOverflow,                            // implicit points without a usable approximation
            WindingPlaneSide, WindingOrient3d,              // ExactMeshBoolean ray tests
            Count
        }

        internal static readonly string[] Names = {
            "orient2d", "orient3d", "incircle",
            "arr.orient", "arr.incircle", "arr.along", "arr.compare",
            "arr.exact_point", "arr.implicit_overflow",
            "winding.plane_side", "winding.orient3d" };

        // (padded: one cache line per counter pair)
        static readonly long[] counters = new long[16 * (int)Kind.Count];

        /// <summary> a call of a predicate </summary>
        [Conditional("G3_PREDICATE_STATS")]
        internal static void Call(Kind kind)
        {
            Interlocked.Increment(ref counters[16 * (int)kind]);
        }

        /// <summary> the filter of a predicate did not decide: its exact stage runs </summary>
        [Conditional("G3_PREDICATE_STATS")]
        internal static void ExactStage(Kind kind)
        {
            Interlocked.Increment(ref counters[16 * (int)kind + 1]);
        }

        internal static long Calls(int kind) { return Interlocked.Read(ref counters[16 * kind]); }
        internal static long ExactStages(int kind) { return Interlocked.Read(ref counters[16 * kind + 1]); }

        internal static void Reset()
        {
            for (int i = 0; i < counters.Length; ++i)
                Interlocked.Exchange(ref counters[i], 0);
        }

        internal static bool Enabled
        {
            get {
#if G3_PREDICATE_STATS
                return true;
#else
                return false;
#endif
            }
        }
    }



    /// <summary>
    /// Phase times and allocations of a computation (ExactMeshArrangement, ExactMeshBoolean): Lap(name) records
    /// the time and the bytes allocated (by all threads; .NET only, 0 on netstandard2.0) since the last lap.
    /// </summary>
    internal sealed class PhaseClock
    {
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly Dictionary<string, long> ms, bytes;
        long allocated = Allocated();

        public PhaseClock(Dictionary<string, long> ms, Dictionary<string, long> bytes)
        {
            this.ms = ms;
            this.bytes = bytes;
        }

        public void Lap(string name)
        {
            long a = Allocated();
            ms[name] = sw.ElapsedMilliseconds;
            bytes[name] = a - allocated;
            sw.Restart();
            allocated = a;
        }

        internal static long Allocated()
        {
#if NET
            return GC.GetTotalAllocatedBytes(false);
#else
            return 0;
#endif
        }
    }
}
