// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Throwaway measurement code (branch runs/predicate-bound): time per predicate stage. Compiled only with
// -p:G3PredicateTime=true (symbols G3_PREDICATE_TIME and G3_PREDICATE_STATS); not part of any shipped build.
#if G3_PREDICATE_TIME
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace g3
{
    /// <summary>
    /// Stage timing of the predicates. A predicate wrapper calls Enter / Leave; the filter time is sampled (every
    /// SamplePeriod-th top-level call takes two timestamps; the mean filter time of the calls that the filter settled
    /// is the cost of one filter evaluation), the exact time is taken for every exact region (one begin and one
    /// end timestamp) and only for the outermost region (a predicate called inside another's exact stage belongs to
    /// that stage). Exact points computed on demand (LPI / TPI constructions) are a region of their own, subtracted
    /// from the enclosing exact region. Thread-local accumulators, merged on read.
    /// </summary>
    internal static class PredicateTime
    {
        internal const int SamplePeriod = 32;
        internal const int MaxKinds = 24;
        internal const int MaxTags = 8;

        internal sealed class State
        {
            public int depth;
            public int countdown = SamplePeriod;
            public long estart, nested;          // start of the open exact region, construction ticks inside it
            public int ekind = -1, tag;
            public readonly long[] top = new long[MaxKinds];                 // top-level calls
            public readonly long[] fTicks = new long[MaxKinds], fN = new long[MaxKinds];   // sampled filter-settled calls
            public readonly long[] exTicks = new long[MaxKinds * MaxTags], exN = new long[MaxKinds * MaxTags];
            public long conTicks, conN;           // exact point constructions
        }

        [ThreadStatic] static State st;
        static readonly ConcurrentBag<State> all = new ConcurrentBag<State>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static State S()
        {
            State s = st;
            if (s == null) { s = new State(); st = s; all.Add(s); }
            return s;
        }

        internal static void Reset()
        {
            foreach (State s in all) {
                Array.Clear(s.top, 0, s.top.Length); Array.Clear(s.fTicks, 0, s.fTicks.Length); Array.Clear(s.fN, 0, s.fN.Length);
                Array.Clear(s.exTicks, 0, s.exTicks.Length); Array.Clear(s.exN, 0, s.exN.Length);
                s.conTicks = 0; s.conN = 0; s.depth = 0; s.nested = 0;
            }
        }

        // -1: nested in an exact region (not counted), 0: top-level, not sampled, > 0: sampled start timestamp
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long Enter(int kind)
        {
            State s = S();
            if (s.depth != 0) return -1;
            s.top[kind]++;
            if (--s.countdown > 0) return 0;
            s.countdown = SamplePeriod;
            return Stopwatch.GetTimestamp();
        }

        // end of a wrapped predicate: closes the exact region the predicate opened, else records a filter sample
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Leave(int kind, long tok)
        {
            if (tok < 0) return;
            State s = S();
            if (s.depth != 0 && s.ekind == kind) { close(s, kind); return; }
            if (tok > 0) {
                s.fTicks[kind] += Stopwatch.GetTimestamp() - tok;
                s.fN[kind]++;
            }
        }

        // the filter did not decide: the exact region of the enclosing wrapped predicate starts
        internal static void ExactStage(int kind)
        {
            State s = S();
            if (s.depth != 0) return;
            s.depth = 1; s.ekind = kind; s.tag = 0; s.nested = 0;
            s.estart = Stopwatch.GetTimestamp();
        }

        internal static void Tag(int tag) { State s = S(); if (s.depth != 0) s.tag = tag; }

        static void close(State s, int kind)
        {
            long t = Stopwatch.GetTimestamp() - s.estart - s.nested;
            int i = kind * MaxTags + s.tag;
            s.exTicks[i] += t; s.exN[i]++;
            s.depth = 0; s.ekind = -1; s.nested = 0;
        }

        // a free-standing exact region (no filter in front of it): -1 if nested
        internal static long EBegin(int kind)
        {
            State s = S();
            if (s.depth != 0) return -1;
            s.top[kind]++;
            s.depth = 1; s.ekind = kind; s.tag = 0; s.nested = 0;
            s.estart = Stopwatch.GetTimestamp();
            return 1;
        }

        internal static void EEnd(int kind, long tok) { if (tok < 0) return; close(S(), kind); }

        // an exact point construction (LPI / TPI, BigInteger): its own bucket, taken out of the enclosing region
        internal static long CBegin() { return Stopwatch.GetTimestamp(); }
        internal static void CEnd(long t0)
        {
            State s = S();
            long d = Stopwatch.GetTimestamp() - t0;
            s.conTicks += d; s.conN++;
            if (s.depth != 0) s.nested += d;
        }

        internal static void Snapshot(long[] top, long[] fTicks, long[] fN, long[] exTicks, long[] exN, out long conTicks, out long conN)
        {
            conTicks = 0; conN = 0;
            foreach (State s in all) {
                for (int k = 0; k < MaxKinds; ++k) { top[k] += s.top[k]; fTicks[k] += s.fTicks[k]; fN[k] += s.fN[k]; }
                for (int i = 0; i < exTicks.Length; ++i) { exTicks[i] += s.exTicks[i]; exN[i] += s.exN[i]; }
                conTicks += s.conTicks; conN += s.conN;
            }
        }

        /// <summary> the mean cost of two back-to-back timestamps (the overhead inside every timed interval), in ticks </summary>
        internal static double TimerOverheadTicks()
        {
            long sum = 0; int n = 2000000;
            for (int i = 0; i < n; ++i) { long a = Stopwatch.GetTimestamp(); long b = Stopwatch.GetTimestamp(); sum += b - a; }
            return (double)sum / n;
        }
    }
}
#endif
