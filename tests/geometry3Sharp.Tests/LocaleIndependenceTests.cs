using System;
using System.Globalization;
using System.Threading;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Messages with numbers are the same in every culture (the library formats invariantly, as its writers do). Each test
    /// sets a culture with a decimal comma on its own thread, so it fails on a machine of any locale if a message follows
    /// the current culture.
    /// </summary>
    public class LocaleIndependenceTests
    {
        static T Under<T>(string culture, Func<T> f)
        {
            CultureInfo old = CultureInfo.CurrentCulture;
            try {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                return f();
            } finally {
                CultureInfo.CurrentCulture = old;
            }
        }

        [Fact]
        public void SlopeDesignMessages_AreTheSameUnderAGermanCulture()
        {
            Func<string> message = () => {
                TerrainSlopeDesign d = TerrainSlopeRampTests.Straight();
                d.Tolerance = 1e-9;
                Assert.False(d.Compute());
                return d.Message;
            };
            string invariant = Under("", message);
            Assert.Matches(@"\d\.\d", invariant);            // (a number in the message: "below the numerical margins (2.08E-05)")
            Assert.Equal(invariant, Under("de-DE", message));
        }

        [Fact]
        public void TimedMessages_AreTheSameUnderAGermanCulture()
        {
            string tripped = Under("de-DE", () => {
                var release = new ManualResetEventSlim();
                try {
                    Assert.False(Timed.MakesProgress(beat => { beat(); release.Wait(); }, TimeSpan.FromSeconds(0.3), null, out string t));
                    return t;
                } finally { release.Set(); }
            });
            Assert.Equal("no progress for 0.3 s", tripped);
        }
    }
}
