using System;
using System.Diagnostics;
using System.Threading;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> The watchdog helper that tells a hang from a slow machine (Timed.MakesProgress) </summary>
    public class TimedTests
    {
        [Fact]
        public void SlowButSteadyRun_MakesProgress_WhateverItsTotalTime()
        {
            // units of 300 ms until the run is 1.3 times the stall limit of 1 s long: the total is longer than the limit,
            // no unit is (and a unit is long enough that a watchdog with a far shorter limit would trip). A loaded machine can hold a sleeping thread back for longer than the limit (a net48 CI runner
            // did: 12 sleeps of 100 ms took 4 s, #755), and then the watchdog is right to report a stall. So the run
            // measures its own waits between two beats (from the call to the first beat, and from the last beat to the
            // watchdog's answer, too): an attempt in which one of them reached 0.7 of the limit says nothing about the
            // watchdog and is repeated; an attempt without one must be reported as progress
            TimeSpan stall = TimeSpan.FromSeconds(1);
            long tooLong = (long)(0.7 * stall.TotalSeconds * Stopwatch.Frequency);
            for (int attempt = 0; attempt < 5; ++attempt) {
                long start = Stopwatch.GetTimestamp(), lastBeat = start, longest = 0;
                bool ok = Timed.MakesProgress(beat => {
                    while (true) {
                        long now = Stopwatch.GetTimestamp();
                        long wait = now - Interlocked.Read(ref lastBeat);
                        if (wait > Interlocked.Read(ref longest))
                            Interlocked.Exchange(ref longest, wait);
                        Interlocked.Exchange(ref lastBeat, now);
                        beat();
                        if (now - start > 1.3 * stall.TotalSeconds * Stopwatch.Frequency)
                            break;
                        Thread.Sleep(300);
                    }
                }, stall);
                long end = Stopwatch.GetTimestamp();
                long waited = Math.Max(Interlocked.Read(ref longest), end - Interlocked.Read(ref lastBeat));
                if (waited >= tooLong)
                    continue;
                // (a finished run was longer than the limit: its last beat came after 1.3 times the limit)
                Assert.True(ok, $"a run whose longest wait was {(double)waited / Stopwatch.Frequency:F3} s was reported as stalled at a limit of {stall.TotalSeconds} s");
                Assert.True(Interlocked.Read(ref lastBeat) - start > stall.TotalSeconds * Stopwatch.Frequency);
                return;
            }
            Assert.True(false, "the machine held the run back for 0.7 of the stall limit or more in each of 5 attempts");
        }

        [Fact]
        public void HangingUnit_IsDetected()
        {
            var release = new ManualResetEventSlim();
            try {
                Assert.False(Timed.MakesProgress(beat => { beat(); release.Wait(); }, TimeSpan.FromSeconds(0.3)));
            } finally { release.Set(); }
        }

        [Fact]
        public void RunThatNeverBeats_IsDetectedFromItsStart()
        {
            var release = new ManualResetEventSlim();
            try {
                Assert.False(Timed.MakesProgress(beat => release.Wait(), TimeSpan.FromSeconds(0.3)));
            } finally { release.Set(); }
        }

        [Fact]
        public void TotalCeiling_FailsARunThatKeepsBeatingButNeverEnds()
        {
            var release = new ManualResetEventSlim();
            try {
                Assert.False(Timed.MakesProgress(beat => { while (!release.IsSet) { Thread.Sleep(20); beat(); } }, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.5), out string tripped));
                Assert.Equal("total of 0.5 s exceeded", tripped);
            } finally { release.Set(); }
        }

        [Fact]
        public void TheLimitThatTripped_IsReported()
        {
            var release = new ManualResetEventSlim();
            try {
                Assert.False(Timed.MakesProgress(beat => { beat(); release.Wait(); }, TimeSpan.FromSeconds(0.3), null, out string tripped));
                Assert.Equal("no progress for 0.3 s", tripped);
            } finally { release.Set(); }
            Assert.True(Timed.MakesProgress(beat => beat(), TimeSpan.FromSeconds(5), null, out string none));
            Assert.Equal("", none);
        }

        [Fact]
        public void ExceptionOfTheAction_IsRethrown()
        {
            Assert.Throws<InvalidOperationException>(() => Timed.MakesProgress(beat => { beat(); throw new InvalidOperationException("x"); }, TimeSpan.FromSeconds(5)));
        }
    }
}
