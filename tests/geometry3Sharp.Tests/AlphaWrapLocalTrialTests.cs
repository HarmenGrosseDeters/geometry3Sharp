using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshAlphaWrap.LocalPinchTrials: the trial runs of the manifold step during the pinch refinement start from the
    /// vertices whose star changed since the last trial. Not bit-identical to the full trials, so the tests check the
    /// guarantees and that the result stays close to the one with full trials, and that only full trials are reused.
    /// </summary>
    public class AlphaWrapLocalTrialTests
    {
        public static IEnumerable<object[]> Inputs()
        {
            foreach (int seed in new[] { 42, 173, 7, 11 })
                yield return new object[] { seed, 20.0, 200.0 };
            yield return new object[] { 42, 60.0, 600.0 };
            // (the pinch refinement of these ends with a local trial that moved nothing: its labels must not be kept)
            yield return new object[] { 3, 20.0, 200.0 };
            yield return new object[] { 17, 20.0, 200.0 };
            yield return new object[] { 1, 40.0, 400.0 };
        }

        static double MinAngle(DMesh3 m, int t)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            double A = Vector3d.AngleD((b - a).Normalized, (c - a).Normalized), B = Vector3d.AngleD((a - b).Normalized, (c - b).Normalized);
            return Math.Min(A, Math.Min(B, 180 - A - B));
        }

        static double P01(DMesh3 m)
        {
            var v = m.TriangleIndices().Select(t => MinAngle(m, t)).OrderBy(x => x).ToList();
            return v[(int)(0.01 * (v.Count - 1))];
        }

        // local against full trials: both clean and within the separation bound; the local one runs local trials, and
        // its result differs at most a little (triangles within 2 %, the 1 %-worst angle within 1 degree)
        [Theory]
        [MemberData(nameof(Inputs))]
        public void LocalTrials_GuaranteesKept_CloseToFullTrials(int seed, double relAlpha, double relOffset)
        {
            DMesh3 input = AlphaWrapTests.RandomSoup(seed);
            var local = new MeshAlphaWrap(input) { RelativeAlpha = relAlpha, RelativeOffset = relOffset };
            var full = new MeshAlphaWrap(input) { RelativeAlpha = relAlpha, RelativeOffset = relOffset, LocalPinchTrials = false };
            Assert.True(local.LocalPinchTrials);        // the default
            Assert.True(local.Compute(), local.Error);
            Assert.True(full.Compute(), full.Error);
            AlphaWrapTests.CheckWrap(local, AlphaWrapTests.Vertices(input));
            AlphaWrapTests.AssertSeparationBound(local, $"soup {seed} local");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(local.Result));
            Assert.False(full.Counts.ContainsKey("local_trials"));
            long trials = local.Counts.ContainsKey("local_trials") ? local.Counts["local_trials"] : 0;
            long pinches = local.Counts.ContainsKey("pinch_points") ? local.Counts["pinch_points"] : 0;
            // (a local trial follows every pinch round after the first)
            if (pinches > 0)
                Assert.True(trials > 0, $"soup {seed}: {pinches} pinch points, no local trial");
            double rel = Math.Abs(local.Result.TriangleCount - full.Result.TriangleCount) / (double)full.Result.TriangleCount;
            Assert.True(rel <= 0.02, $"soup {seed}: triangles {local.Result.TriangleCount} vs {full.Result.TriangleCount}");
            Assert.True(Math.Abs(P01(local.Result) - P01(full.Result)) <= 1.0, $"soup {seed}: p01 {P01(local.Result)} vs {P01(full.Result)}");
        }

        // local trials happen on these inputs (else the test above compares the full trials with themselves) and find
        // pinches (seeds that miss every changed star would find none: the confirming full trials would find them all);
        // a local trial that finds nothing is confirmed by a full one, whose labels are then reused (#417) when it moved
        // nothing
        [Fact]
        public void LocalTrials_Happen_FindPinches_AndAreConfirmed()
        {
            int withLocal = 0, reused = 0;
            long found = 0, confirmed = 0, missed = 0;
            foreach (object[] c in Inputs()) {
                var w = new MeshAlphaWrap(AlphaWrapTests.RandomSoup((int)c[0])) { RelativeAlpha = (double)c[1], RelativeOffset = (double)c[2] };
                Assert.True(w.Compute(), w.Error);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
                if (w.Counts["local_trials"] == 0)
                    continue;
                withLocal++;
                found += w.Counts["local_trials_found"];
                confirmed += w.Counts["local_trials_confirmed"];
                missed += w.Counts["local_trials_missed"];
                Assert.True(w.Counts["local_trials_missed"] <= w.Counts["local_trials_confirmed"]);
                if (w.Counts.ContainsKey("manifold_reused")) reused++;
            }
            Assert.True(withLocal >= 5, $"local trials on {withLocal} inputs");
            Assert.True(found > 0 && found >= missed, $"local trials found pinches {found} times, the full confirmation {missed} times");
            Assert.True(confirmed > 0, "no local trial was confirmed");
            Assert.True(reused > 0, "the confirming full trial was never reused");
        }

        // reused instance and the 2^k scales: the same result bit for bit
        [Fact]
        public void LocalTrials_ReuseAndScales()
        {
            DMesh3 input = AlphaWrapTests.RandomSoup(42);
            var w = new MeshAlphaWrap(input) { RelativeAlpha = 20, RelativeOffset = 200 };
            Assert.True(w.Compute(), w.Error);
            DMesh3 first = new DMesh3(w.Result);
            Assert.True(w.Compute(), w.Error);
            Assert.Equal("", Invariance.Difference(first, w.Result));
            foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                DMesh3 scaled = new DMesh3(input);
                MeshTransforms.Scale(scaled, s);
                var ws = new MeshAlphaWrap(scaled) { RelativeAlpha = 20, RelativeOffset = 200 };
                Assert.True(ws.Compute(), ws.Error);
                DMesh3 back = new DMesh3(ws.Result);
                MeshTransforms.Scale(back, 1 / s);
                Assert.Equal("", Invariance.Difference(first, back));
            }
        }
    }
}
