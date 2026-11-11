using System;
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
            // 12 units of 100 ms against a stall limit of 1 s: the total (1.2 s) is longer than the limit, no unit is
            Assert.True(Timed.MakesProgress(beat => { for (int i = 0; i < 12; ++i) { Thread.Sleep(100); beat(); } }, TimeSpan.FromSeconds(1)));
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
                Assert.False(Timed.MakesProgress(beat => { while (!release.IsSet) { Thread.Sleep(20); beat(); } }, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.5)));
            } finally { release.Set(); }
        }

        [Fact]
        public void ExceptionOfTheAction_IsRethrown()
        {
            Assert.Throws<InvalidOperationException>(() => Timed.MakesProgress(beat => { beat(); throw new InvalidOperationException("x"); }, TimeSpan.FromSeconds(5)));
        }
    }
}
